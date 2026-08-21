namespace CodeBase.Net.IO;

/// <summary>
/// Fills a window from the file behind it.
/// </summary>
/// <param name="start">Where the window begins in the file.</param>
/// <param name="into">Where to put the bytes; its length is the window size.</param>
/// <returns>How many bytes arrived, which is fewer than asked for at the end of the file.</returns>
internal delegate int FillWindow(long start, Span<byte> into);

/// <summary>
/// Which windows of which files are in memory, over one shared pool.
///
/// **The mapping lives here and not in the per-file source, and that is a correctness decision rather
/// than a tidiness one.** One pool serves every file an engine has open, so a slot retired to make room
/// for one file's window may be the slot another file believes its own window is in. If each file kept
/// its own map, that other file's next read would be served bytes belonging to a different file, with
/// nothing raising an error — a wrong record set of the worst kind. Holding both directions here means
/// retiring a slot removes its key in the same breath, so the situation cannot arise.
///
/// It is also where the reference keeps it: [c]opt4fileHash[/c] is over
/// [c](file, block-aligned position)[/c], with a per-file salt so two files do not collide
/// systematically ([c]o4opt.c:1203[/c]).
///
/// The budget is therefore the engine's, not the file's — one table's index, memo and data compete for
/// the same slots, as they do in the reference, rather than each getting a pool of its own.
/// </summary>
internal sealed class BlockCache
{
    private readonly BlockPool pool;

    /// <summary>Where each held window lives.</summary>
    private readonly Dictionary<WindowKey, Window> held = [];

    /// <summary>Which window each slot holds, so retiring one can remove its key.</summary>
    private readonly WindowKey?[] byslot;

    private int owners;

    /// <summary>
    /// Creates a cache over a pool.
    /// </summary>
    /// <param name="pool">The buffers and slots to use.</param>
    public BlockCache(BlockPool pool)
    {
        this.pool = pool;
        byslot = new WindowKey?[pool.Capacity];
    }

    /// <summary>Gets how many bytes one window is.</summary>
    public int WindowSize => pool.BlockSize;

    /// <summary>Gets how many windows can be held at once.</summary>
    public int Capacity => pool.Capacity;

    /// <summary>Gets how many windows are being held.</summary>
    public int Holding => held.Count;

    /// <summary>
    /// Registers a file, giving it an identity its windows are keyed by.
    /// </summary>
    /// <returns>The file's identity, which it passes back on every call.</returns>
    /// <remarks>
    /// A number rather than the source object, so that a file which is closed and reopened does not
    /// inherit the windows of the one before it — the same reason the reference salts its hash per
    /// file rather than by name.
    /// </remarks>
    public int NewOwner() => ++owners;

    /// <summary>
    /// Serves a window, reading it in if it is not held.
    /// </summary>
    /// <param name="owner">Whose window it is.</param>
    /// <param name="start">Where the window begins.</param>
    /// <param name="priority">What a window of this file is worth keeping.</param>
    /// <param name="fill">How to read it on a miss.</param>
    /// <param name="slot">Where the window is, claimed, valid only when this returns true.</param>
    /// <param name="valid">How many of the window's bytes are real.</param>
    /// <returns>
    /// False when the window could not be held — every slot is being read out of, or the file has
    /// nothing at that offset. The caller then reads around the cache rather than displacing a live
    /// window.
    /// </returns>
    /// <remarks>
    /// One call, not a claim-then-register pair, because a pair can be got wrong: a slot claimed and
    /// never registered is a slot that never comes back, and one registered without being filled
    /// serves whatever was in it. Taking the fill as a delegate keeps both halves inside here.
    ///
    /// **The slot comes back claimed and the caller must release it.**
    /// </remarks>
    public bool TryHold(
        int owner, long start, BlockPriority priority, FillWindow fill, out int slot, out int valid)
    {
        WindowKey key = new(owner, start);

        if (held.TryGetValue(key, out Window found))
        {
            pool.Claim(found.Slot);
            pool.Touch(found.Slot);

            slot = found.Slot;
            valid = found.Valid;
            Hits++;
            return true;
        }

        if (!pool.TryClaim(priority, out slot, out int retired))
        {
            valid = 0;
            return false;
        }

        // Whatever the retired slot held is gone. Removing its key here — where both directions are
        // known — is what stops another file being served this one's bytes.
        if (retired >= 0)
            Evict(retired);

        int read;
        try
        {
            read = fill(start, pool.At(slot).Writable);
        }
        catch
        {
            // A failed fetch must leave nothing behind: a half-filled window would be served in full
            // next time, and the bytes that never arrived would read as zeros.
            Abandon(slot);
            throw;
        }

        Misses++;

        if (read <= 0)
        {
            Abandon(slot);
            valid = 0;
            return false;
        }

        held[key] = new Window(slot, read);
        byslot[slot] = key;
        valid = read;
        return true;
    }

    /// <summary>Gets how many windows were served without a read.</summary>
    public int Hits { get; private set; }

    /// <summary>Gets how many windows had to be read.</summary>
    public int Misses { get; private set; }

    /// <summary>
    /// Gives the bytes of a slot.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    /// <returns>The window's block.</returns>
    public Block At(int slot) => pool.At(slot);

    /// <summary>
    /// Gives up a claim taken by <see cref="TryHold"/>.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    public void Release(int slot) => pool.Release(slot);

    /// <summary>
    /// Forgets every window of one file.
    /// </summary>
    /// <param name="owner">Whose windows to drop.</param>
    /// <returns>How many were dropped.</returns>
    /// <remarks>
    /// A window still claimed is left alone rather than pulled out from under its reader. No caller
    /// holds a claim across a call, so in practice that is nothing; the check is there because "in
    /// practice" is not a guarantee.
    /// </remarks>
    public int Invalidate(int owner)
    {
        List<WindowKey> dropping = [.. held.Keys.Where(key => key.Owner == owner)];
        int dropped = 0;

        foreach (WindowKey key in dropping)
        {
            int slot = held[key].Slot;

            try
            {
                pool.Drop(slot);
            }
            catch (InvalidOperationException)
            {
                continue;   // still being read out of
            }

            held.Remove(key);
            byslot[slot] = null;
            dropped++;
        }

        return dropped;
    }

    /// <summary>
    /// Forgets every window of every file.
    /// </summary>
    /// <returns>How many were dropped.</returns>
    public int InvalidateAll()
    {
        int dropped = 0;

        foreach (int owner in held.Keys.Select(key => key.Owner).Distinct().ToArray())
            dropped += Invalidate(owner);

        return dropped;
    }

    private void Evict(int slot)
    {
        if (byslot[slot] is not WindowKey key)
            return;

        held.Remove(key);
        byslot[slot] = null;
    }

    private void Abandon(int slot)
    {
        pool.Release(slot);
        pool.Drop(slot);
        byslot[slot] = null;
    }

    /// <summary>Which file's window, and where it starts.</summary>
    /// <param name="Owner">The file's identity from <see cref="NewOwner"/>.</param>
    /// <param name="Start">Where the window begins in that file.</param>
    private readonly record struct WindowKey(int Owner, long Start);

    /// <summary>Where a window is and how much of it is real.</summary>
    /// <param name="Slot">The pool slot.</param>
    /// <param name="Valid">
    /// How many bytes the file gave, which is the window size except at the end of the file.
    /// </param>
    private readonly record struct Window(int Slot, int Valid);
}
