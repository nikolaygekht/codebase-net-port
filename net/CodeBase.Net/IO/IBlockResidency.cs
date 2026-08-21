namespace CodeBase.Net.IO;

/// <summary>
/// Which cached block to retire when a new one has to be made room for.
///
/// Nothing here touches a file, a byte or a clock: it is told what happened to numbered slots and
/// answers which slot to give up. That is what makes the one part of caching that can be *wrong*
/// provable exhaustively at memory speed — a wrong victim is a wrong record set, and it is the only
/// way this cache can produce one.
///
/// **It is an interface because a second implementation is planned, not in case one is.** The
/// reference's own policy is a good deal more than five LRU lists — availability marking with a
/// priority counting system, list normalisation by average length, per-block hit-count and access-time
/// scoring ([c]o4opt.c:903-1000[/c]) — and whether that machinery earns its keep is a question to
/// settle by measuring both against each other, not by assuming. So the cheap policy lands first, this
/// seam is where the faithful one arrives, and the comparison is the point.
///
/// One rule binds every implementation, and it is not a heuristic: **a pinned slot is never a victim.**
/// A pinned block is one somebody is still reading out of, so retiring it would overwrite bytes under
/// a live reader.
/// </summary>
internal interface IBlockResidency
{
    /// <summary>Gets how many slots there are.</summary>
    int Capacity { get; }

    /// <summary>Gets how many slots currently hold a block.</summary>
    int Resident { get; }

    /// <summary>
    /// Takes a free slot for a block that is about to be read into it.
    /// </summary>
    /// <param name="slot">Which slot, which must be free.</param>
    /// <param name="priority">What the block is worth keeping.</param>
    /// <exception cref="InvalidOperationException">The slot already holds a block.</exception>
    void Admit(int slot, BlockPriority priority);

    /// <summary>
    /// Records that a resident block was used, so it is retired later than its neighbours.
    /// </summary>
    /// <param name="slot">Which slot, which must be resident.</param>
    /// <exception cref="InvalidOperationException">The slot holds no block.</exception>
    void Touch(int slot);

    /// <summary>
    /// Marks a resident block as being read out of, so it cannot be retired.
    /// </summary>
    /// <param name="slot">Which slot, which must be resident.</param>
    /// <exception cref="InvalidOperationException">The slot holds no block.</exception>
    /// <remarks>
    /// Pins are **counted**, not a flag: two cursors on one tag can sit on the same block, and each
    /// holds it independently. The count is small — it is bounded by how many readers are parked on
    /// one block — and it is not a general reference-counting scheme, only the answer to "is anybody
    /// still in there".
    /// </remarks>
    void Pin(int slot);

    /// <summary>
    /// Gives up one hold on a block, making it retirable again once the last hold goes.
    /// </summary>
    /// <param name="slot">Which slot.</param>
    /// <exception cref="InvalidOperationException">The slot is not pinned.</exception>
    void Unpin(int slot);

    /// <summary>
    /// Drops a block from residency, freeing its slot.
    /// </summary>
    /// <param name="slot">Which slot, which must be resident and unpinned.</param>
    /// <exception cref="InvalidOperationException">The slot holds no block, or is pinned.</exception>
    void Forget(int slot);

    /// <summary>
    /// Finds a slot holding no block, without taking it.
    /// </summary>
    /// <param name="slot">The free slot, valid only when this returns true.</param>
    /// <returns>True when there was one.</returns>
    /// <remarks>
    /// A look and not a claim: the slot is still free afterwards, and [c]Admit[/c] is what takes it.
    /// Asking twice in a row therefore names the same slot both times.
    /// </remarks>
    bool TryFindFree(out int slot);

    /// <summary>
    /// Names the block to retire.
    /// </summary>
    /// <param name="slot">The slot to give up, valid only when this returns true.</param>
    /// <returns>
    /// False when every resident block is pinned, which is not an error: the caller then reads without
    /// caching rather than overwriting something being read.
    /// </returns>
    bool TryChooseVictim(out int slot);
}
