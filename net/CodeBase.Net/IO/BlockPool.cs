namespace CodeBase.Net.IO;

/// <summary>
/// The blocks an engine keeps in memory, and who is allowed to overwrite which.
///
/// It owns [c]Capacity[/c] buffers of one size, allocated once, and hands them out by slot number. A
/// slot is **claimed** before it is filled and **released** when the reader is done with it, and
/// between those two calls it cannot be retired — which is the whole lifetime rule. Nothing else about
/// a block is this class's business: what a block *is*, which file and offset it came from, and whether
/// a read is a hit are the caching layer's, above.
///
/// It allocates nothing per read, and that is the point. A descent read a fresh [c]byte[512][/c] per
/// block before this existed, which measured at **~40% of a RAM-backed descent** — more than every
/// other saving in this step put together (claude/dev/009-performance-audit/results-compare-in-place.txt
/// section 8).
///
/// **One size per pool, and no size logic anywhere.** A block size is fixed for the file it belongs to
/// — a CDX declares one per file and every file Visual FoxPro writes says 512 — so a pool per file has
/// exactly one, and a pool with several sizes never has to exist. [c]Block[/c] carries the length
/// beside the buffer anyway, so a buffer larger than the block it holds would be *correct*; that is a
/// safety net, not a licence to mix.
///
/// **Not safe against concurrent use, deliberately.** A table and its cursors must not be used by two
/// threads at once, and a pool belongs to one file of one table, so the contract above it already
/// covers this one. Nothing here is thread-affine either: claiming on one thread and releasing on
/// another — which is what an [c]await[/c] between them does — is fine.
/// </summary>
internal sealed class BlockPool
{
    private readonly byte[][] buffers;
    private readonly IBlockResidency residency;

    /// <summary>
    /// Creates the pool and its buffers.
    /// </summary>
    /// <param name="blockSize">How many bytes one block is.</param>
    /// <param name="capacity">How many blocks can be held at once.</param>
    /// <param name="residency">
    /// Which block to retire when a claim needs room, or null for the default rule.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The block size or the capacity is not positive.</exception>
    /// <exception cref="ArgumentException">The residency does not have the capacity asked for.</exception>
    public BlockPool(int blockSize, int capacity, IBlockResidency? residency = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        this.residency = residency ?? new BlockResidency(capacity);

        if (this.residency.Capacity != capacity)
        {
            throw new ArgumentException(
                $"The residency holds {this.residency.Capacity} slots and the pool was asked for " +
                $"{capacity}.",
                nameof(residency));
        }

        BlockSize = blockSize;
        Capacity = capacity;

        // Allocated up front, all of it, because a cache that allocates while serving a read is not
        // one. The reference does the same: code4optStart takes the pool before any file uses it.
        buffers = new byte[capacity][];
        for (int slot = 0; slot < capacity; slot++)
            buffers[slot] = new byte[blockSize];
    }

    /// <summary>Gets how many bytes each block in this pool is.</summary>
    public int BlockSize { get; }

    /// <summary>Gets how many blocks the pool can hold.</summary>
    public int Capacity { get; }

    /// <summary>Gets how many slots currently hold a block.</summary>
    public int Held => residency.Resident;

    /// <summary>
    /// Takes a slot to read a block into, retiring one if that is what it costs.
    /// </summary>
    /// <param name="priority">What the block being read is worth keeping.</param>
    /// <param name="slot">The slot claimed, valid only when this returns true.</param>
    /// <param name="retired">
    /// The slot whose block was given up to make room, or -1 when a free slot was used. The caller has
    /// to forget whatever it had recorded about that slot before filling the new one.
    /// </param>
    /// <returns>
    /// False when every held block is still being read out of. That is not a failure: the caller reads
    /// without the pool rather than overwriting bytes under a reader.
    /// </returns>
    /// <remarks>
    /// The slot comes back **claimed**, which is to say pinned: it cannot be retired until
    /// [c]Release[/c] is called on it. Filling it before releasing is the caller's job and the reason
    /// the claim exists.
    /// </remarks>
    public bool TryClaim(BlockPriority priority, out int slot, out int retired)
    {
        retired = -1;

        if (!residency.TryFindFree(out slot))
        {
            if (!residency.TryChooseVictim(out slot))
                return false;

            retired = slot;
            residency.Forget(slot);
        }

        residency.Admit(slot, priority);
        residency.Pin(slot);
        return true;
    }

    /// <summary>
    /// Gives the block a slot holds.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    /// <returns>The block, over this pool's own buffer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such slot.</exception>
    /// <remarks>
    /// The returned [c]Block[/c] is only valid while the slot is claimed. Keeping one past
    /// [c]Release[/c] is a bug this class cannot see and will not report: the slot may be retired and
    /// refilled, and the bytes then belong to another block. Holding a claim for as long as the block
    /// is read is what makes that impossible, and it is why claiming is explicit.
    /// </remarks>
    public Block At(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, Capacity);

        return new Block(buffers[slot], 0, BlockSize);
    }

    /// <summary>
    /// Records that a held block was used again, so it is retired later than its neighbours.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    /// <exception cref="InvalidOperationException">The slot holds no block.</exception>
    public void Touch(int slot) => residency.Touch(slot);

    /// <summary>
    /// Claims a block that is already held, so that reading it again cannot race a retirement.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    /// <exception cref="InvalidOperationException">The slot holds no block.</exception>
    public void Claim(int slot) => residency.Pin(slot);

    /// <summary>
    /// Gives up one claim on a slot.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    /// <exception cref="InvalidOperationException">Nothing was claiming it.</exception>
    public void Release(int slot) => residency.Unpin(slot);

    /// <summary>
    /// Drops a block the pool holds, whether or not anything else is in the pool.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    /// <exception cref="InvalidOperationException">The slot holds no block, or is still claimed.</exception>
    /// <remarks>
    /// What invalidation is built out of: a caller that knows the file has moved under it drops what
    /// the pool believes rather than waiting for the block to age out.
    /// </remarks>
    public void Drop(int slot) => residency.Forget(slot);
}
