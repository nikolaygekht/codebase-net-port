namespace CodeBase.Net.IO;

/// <summary>
/// Five least-recently-used lists, one per priority, and nothing on top of them.
///
/// This is the reference's structure without the reference's heuristics. What is kept, because it is
/// what makes the structure work at all:
///
/// the **five lists** and the **order a victim is looked for in** — [c]Other[/c], [c]DataLow[/c],
/// [c]IndexLow[/c], [c]DataHigh[/c], [c]IndexHigh[/c], which is [c]prio[0][/c] to [c]prio[4][/c] at
/// [c]D4OPT.C:324-328[/c] — so a table scan cannot evict the index tree; the lists themselves as
/// **intrusive doubly-linked chains over arrays**, so admitting, touching and choosing are each a
/// handful of pointer writes rather than a walk over the whole pool (a scan per miss would cost more
/// than the cache saves); and **pinning**, which is the one rule that is about correctness rather than
/// speed.
///
/// What is left to the faithful implementation, and recorded here so nobody thinks it was missed:
/// availability marking with its priority counting system and the [c]selected[/c] protocol, list
/// normalisation by average length, per-block hit-count and access-time scoring, and promotion of a
/// data block from low to high by hit rate ([c]o4opt.c:903-1000[/c], [c]o4opt.c:1017-1028[/c]). Those
/// are speed heuristics: they change which block is given up, never what a read returns. Whether they
/// earn their complexity is a measurement this port has not taken, which is why
/// [c]IBlockResidency[/c] exists.
/// </summary>
internal sealed class BlockResidency : IBlockResidency
{
    /// <summary>How many priorities there are, which is how many lists.</summary>
    private const int Priorities = 5;

    /// <summary>Stands for "no slot" in a link or a list end.</summary>
    private const int None = -1;

    private readonly int[] next;
    private readonly int[] previous;
    private readonly int[] pins;
    private readonly BlockPriority[] priorities;
    private readonly bool[] resident;

    /// <summary>The least-recently-used end of each list, which is where a victim comes from.</summary>
    private readonly int[] oldest = new int[Priorities];

    /// <summary>The most-recently-used end of each list, which is where a touch moves a block to.</summary>
    private readonly int[] newest = new int[Priorities];

    /// <summary>Free slots, chained through the same links.</summary>
    private int free = None;

    /// <summary>
    /// Creates the lists for a fixed number of slots, all of them free.
    /// </summary>
    /// <param name="capacity">How many blocks can be resident at once.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is not positive.</exception>
    public BlockResidency(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        Capacity = capacity;
        next = new int[capacity];
        previous = new int[capacity];
        pins = new int[capacity];
        priorities = new BlockPriority[capacity];
        resident = new bool[capacity];

        Array.Fill(oldest, None);
        Array.Fill(newest, None);

        // Chained downwards so that the first slot handed out is slot zero, which makes a failing test
        // name the slot a reader would expect rather than the highest one.
        for (int slot = capacity - 1; slot >= 0; slot--)
        {
            next[slot] = free;
            previous[slot] = None;
            free = slot;
        }
    }

    /// <inheritdoc/>
    public int Capacity { get; }

    /// <inheritdoc/>
    public int Resident { get; private set; }

    /// <inheritdoc/>
    public void Admit(int slot, BlockPriority priority)
    {
        Validate(slot);

        if (resident[slot])
            throw new InvalidOperationException($"Slot {slot} already holds a block.");

        Unchain(ref free, slot);
        resident[slot] = true;
        priorities[slot] = priority;
        pins[slot] = 0;
        Resident++;

        AppendAsNewest(slot, priority);
    }

    /// <inheritdoc/>
    public void Touch(int slot)
    {
        Validate(slot);
        RequireResident(slot);

        BlockPriority priority = priorities[slot];
        int list = (int)priority;

        // Already the most recent: nothing to move, and moving would be two writes for no reason. A
        // walk touches the same block repeatedly, so this is the common case rather than a niggle.
        if (newest[list] == slot)
            return;

        RemoveFromList(slot, priority);
        AppendAsNewest(slot, priority);
    }

    /// <inheritdoc/>
    public void Pin(int slot)
    {
        Validate(slot);
        RequireResident(slot);

        pins[slot]++;
    }

    /// <inheritdoc/>
    public void Unpin(int slot)
    {
        Validate(slot);

        if (pins[slot] == 0)
        {
            throw new InvalidOperationException(
                $"Slot {slot} is not pinned, so there is nothing to give up. Returning a block twice " +
                "is how a buffer ends up being written under a live reader.");
        }

        pins[slot]--;
    }

    /// <inheritdoc/>
    public void Forget(int slot)
    {
        Validate(slot);
        RequireResident(slot);

        if (pins[slot] > 0)
            throw new InvalidOperationException($"Slot {slot} is pinned {pins[slot]} times.");

        RemoveFromList(slot, priorities[slot]);
        resident[slot] = false;
        Resident--;

        next[slot] = free;
        previous[slot] = None;
        free = slot;
    }

    /// <inheritdoc/>
    public bool TryFindFree(out int slot)
    {
        slot = free;
        return slot != None;
    }

    /// <inheritdoc/>
    public bool TryChooseVictim(out int slot)
    {
        // The reference's order, and the whole reason for the split: a scan over table blocks gives up
        // its own blocks before it touches the index tree, and the tree's interior nodes go last of
        // all (D4OPT.C:324-328).
        for (int list = 0; list < Priorities; list++)
        {
            // From the least recently used end forwards. Pinned blocks are stepped over rather than
            // removed from the list: they are few — bounded by how many readers are parked — and they
            // become retirable again as soon as their readers move on.
            for (int candidate = oldest[list]; candidate != None; candidate = next[candidate])
            {
                if (pins[candidate] == 0)
                {
                    slot = candidate;
                    return true;
                }
            }
        }

        slot = None;
        return false;
    }

    private void Validate(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, Capacity);
    }

    private void RequireResident(int slot)
    {
        if (!resident[slot])
            throw new InvalidOperationException($"Slot {slot} holds no block.");
    }

    private void AppendAsNewest(int slot, BlockPriority priority)
    {
        int list = (int)priority;

        previous[slot] = newest[list];
        next[slot] = None;

        if (newest[list] == None)
            oldest[list] = slot;
        else
            next[newest[list]] = slot;

        newest[list] = slot;
    }

    private void RemoveFromList(int slot, BlockPriority priority)
    {
        int list = (int)priority;

        if (previous[slot] == None)
            oldest[list] = next[slot];
        else
            next[previous[slot]] = next[slot];

        if (next[slot] == None)
            newest[list] = previous[slot];
        else
            previous[next[slot]] = previous[slot];

        next[slot] = None;
        previous[slot] = None;
    }

    private void Unchain(ref int head, int slot)
    {
        if (head == slot)
        {
            head = next[slot];
            next[slot] = None;
            return;
        }

        for (int on = head; on != None; on = next[on])
        {
            if (next[on] != slot)
                continue;

            next[on] = next[slot];
            next[slot] = None;
            return;
        }

        throw new InvalidOperationException($"Slot {slot} is not free, so it cannot be taken.");
    }
}
