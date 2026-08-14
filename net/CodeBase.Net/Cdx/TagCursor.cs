namespace CodeBase.Net.Cdx;

/// <summary>
/// A position in a tag, and the four moves that change it.
///
/// Walking follows the chain of leaf blocks. Every leaf carries the node numbers of its neighbours,
/// which the split code maintains, so stepping off the end of one block means following a pointer
/// rather than climbing back up the tree. Reaching the first or last leaf in the first place is a
/// descent from the root, taking the first or last child at each level.
///
/// Descending tags are stored ascending: the flag inverts what "next" means and nothing else, so
/// [c]Top[/c] on a descending tag lands on the greatest key. That is the direction the C library
/// navigates in, and it is worth knowing that its own key counter gets this wrong — tfile4count tops
/// a descending tag and then skips forward physically, which from the last key moves nowhere
/// (I4TAG.C:1000-1019).
///
/// Governing specification: CDX-FORMAT.md sections 4, 5.2 and 7.
/// </summary>
internal sealed class TagCursor
{
    private readonly CdxTag tag;

    /// <summary>
    /// The deepest tree this reader will descend before calling it a cycle.
    ///
    /// With the smallest possible fan-out of two, this many levels already address more blocks than a
    /// node number can name, so a deeper descent is not a tree.
    /// </summary>
    private const int MaxDepth = 32;

    private TreeBlock block;
    private int index;

    /// <summary>
    /// Initializes a new instance positioned nowhere.
    /// </summary>
    /// <param name="tag">The tag to walk.</param>
    public TagCursor(CdxTag tag)
    {
        this.tag = tag;
        Eof = true;
        Bof = true;
    }

    /// <summary>Gets a value indicating whether the cursor is past the last key.</summary>
    public bool Eof { get; private set; }

    /// <summary>Gets a value indicating whether the cursor is before the first key.</summary>
    public bool Bof { get; private set; }

    /// <summary>Gets a value indicating whether the cursor is on a key that can be read.</summary>
    public bool IsOnKey => !Eof && !Bof && block.Leaf is not null && block.Leaf.Count > 0;

    /// <summary>
    /// Gets the entry the cursor is on.
    /// </summary>
    /// <value>The key and its record number.</value>
    /// <exception cref="CodeBaseException">
    /// The cursor is not on a key, which is a caller error rather than a corrupt file.
    /// </exception>
    public IndexEntry Current
    {
        get
        {
            if (!IsOnKey)
            {
                throw new CodeBaseException(
                    ErrorCode.Info,
                    $"The cursor on tag {tag.Name} is not on a key, so there is nothing to read.");
            }

            return block.Leaf!.EntryAt(index);
        }
    }

    /// <summary>
    /// Moves to the first key in the tag's own order.
    /// </summary>
    /// <returns>True when there is one, false when the tag holds no keys at all.</returns>
    public bool Top() => tag.Descending ? DescendLast() : DescendFirst();

    /// <summary>
    /// Moves to the last key in the tag's own order.
    /// </summary>
    /// <returns>True when there is one, false when the tag holds no keys at all.</returns>
    public bool Bottom() => tag.Descending ? DescendFirst() : DescendLast();

    /// <summary>
    /// Moves by a number of keys in the tag's own order.
    /// </summary>
    /// <param name="count">
    /// How far to move. Negative moves backwards. Zero moves nowhere and reports whether the cursor is
    /// on a key.
    /// </param>
    /// <returns>
    /// How many keys were actually moved, which is fewer than asked for when the tag ran out. The
    /// cursor is then at one end, with the matching flag set.
    /// </returns>
    public long Skip(long count)
    {
        long moved = 0;
        long step = tag.Descending ? -1 : 1;

        for (; moved < Math.Abs(count); moved++)
        {
            if (!StepPhysical(count > 0 ? step : -step))
                break;
        }

        return count < 0 ? -moved : moved;
    }

    /// <summary>
    /// Moves one key forward in the tag's own order.
    /// </summary>
    /// <returns>True when the cursor moved, false when it was already on the last key.</returns>
    public bool Next() => Skip(1) == 1;

    /// <summary>
    /// Moves one key backward in the tag's own order.
    /// </summary>
    /// <returns>True when the cursor moved, false when it was already on the first key.</returns>
    public bool Previous() => Skip(-1) == -1;


    /// <summary>
    /// Moves to the first entry, in the tag's own order, that is not before a search value.
    /// </summary>
    /// <param name="search">What to look for.</param>
    /// <returns>
    /// [c]Found[/c] with the cursor on the first matching entry, [c]After[/c] with it on the first entry
    /// beyond the value, or [c]Eof[/c] with it past the end when nothing is at or beyond the value.
    /// </returns>
    /// <remarks>
    /// "Before" is in the tag's order, so on a descending tag this looks for the greatest key not
    /// *greater* than the value — the two byte-level searches swap places. That is also what the C
    /// library does, by incrementing the search value and stepping back (I4TAG.C:2295-2356).
    /// </remarks>
    public SeekOutcome Seek(KeySearch search)
    {
        // An empty value is before everything, so the tag's first entry is the answer either way.
        if (search.Length == 0)
            return Top() ? SeekOutcome.Found : SeekOutcome.Eof;

        // A value with no successor is above every key. On a descending tag that is past the *end*, and
        // the C library reports exactly that rather than the greatest key — the increment fails, so its
        // descending path takes the "otherwise want an eof type condition" branch (I4TAG.C:2341-2350).
        if (tag.Descending && !search.TryIncrement(out _))
        {
            Eof = true;
            Bof = false;
            return SeekOutcome.Eof;
        }

        bool landed = tag.Descending ? SeekLastAtOrBelow(search) : SeekFirstAtOrAbove(search);

        if (!landed)
        {
            Eof = true;
            Bof = false;
            return SeekOutcome.Eof;
        }

        return search.Matches(Current.Key) ? SeekOutcome.Found : SeekOutcome.After;
    }

    /// <summary>
    /// Moves to the last entry, in the tag's own order, that is not after a search value.
    /// </summary>
    /// <param name="search">What to look for.</param>
    /// <returns>
    /// [c]Found[/c] with the cursor on the last matching entry, [c]Before[/c] with it on the last entry
    /// short of the value, or [c]Bof[/c] when nothing is at or short of the value.
    /// </returns>
    /// <remarks>
    /// A range's other end, and the primitive the backwards operations are built on. It is the mirror of
    /// [c]Seek[/c] and so uses the opposite byte-level search, which on a descending tag means the same
    /// one [c]Seek[/c] uses on an ascending tag.
    /// </remarks>
    public SeekOutcome SeekAtOrBefore(KeySearch search)
    {
        // An empty value matches everything, so the *last* entry is the last one that is not after it.
        if (search.Length == 0)
            return Bottom() ? SeekOutcome.Found : SeekOutcome.Bof;

        bool landed = tag.Descending ? SeekFirstAtOrAbove(search) : SeekLastAtOrBelow(search);

        if (!landed)
        {
            Bof = true;
            Eof = false;
            return SeekOutcome.Bof;
        }

        return search.Matches(Current.Key) ? SeekOutcome.Found : SeekOutcome.Before;
    }

    /// <summary>
    /// Moves to the last entry whose key matches a search value.
    /// </summary>
    /// <param name="search">What to look for.</param>
    /// <returns>
    /// [c]Found[/c] with the cursor on the last matching entry, or [c]NoEntry[/c] when nothing matches —
    /// in which case the cursor is left wherever the search reached, as [c]SeekAtOrBefore[/c] left it.
    /// </returns>
    /// <remarks>
    /// One comparison on top of [c]SeekAtOrBefore[/c]: the last entry not *greater* than the value is
    /// the last entry *equal* to it whenever an equal one exists. What makes this worth its own name is
    /// what it reports when none does.
    /// </remarks>
    public SeekOutcome SeekLast(KeySearch search) =>
        SeekAtOrBefore(search) == SeekOutcome.Found ? SeekOutcome.Found : SeekOutcome.NoEntry;

    /// <summary>
    /// Moves to the next entry that still matches a search value.
    /// </summary>
    /// <param name="search">What the run is of.</param>
    /// <returns>
    /// [c]Found[/c] on the next matching entry, [c]NoEntry[/c] when the run has ended, or whatever a
    /// fresh seek reports when the cursor was not on a matching entry to begin with.
    /// </returns>
    /// <remarks>
    /// The C library's own three steps, including the one that looks like a rough edge: when the current
    /// entry does not match, this **degrades to a plain seek** rather than reporting nothing
    /// (d4seekNextN, D4SEEK.C:1195-1210). That is what makes it safe to call without knowing where the
    /// cursor is, and it is reproduced rather than tidied.
    /// </remarks>
    public SeekOutcome SeekNext(KeySearch search) => SeekAdjacentMatch(search, 1);

    /// <summary>
    /// Moves to the previous entry that still matches a search value.
    /// </summary>
    /// <param name="search">What the run is of.</param>
    /// <returns>
    /// [c]Found[/c] on the previous matching entry, [c]NoEntry[/c] when the run has ended, or whatever
    /// seeking the last match reports when the cursor was not on a matching entry.
    /// </returns>
    /// <remarks>
    /// The mirror of [c]SeekNext[/c], and an operation the C library does not have. Where its
    /// counterpart falls back to a plain seek, this falls back to [c]SeekLast[/c] — the equivalent
    /// starting point for walking a run backwards.
    /// </remarks>
    public SeekOutcome SeekPrevious(KeySearch search) => SeekAdjacentMatch(search, -1);

    /// <summary>
    /// Moves to an exact key and record number.
    /// </summary>
    /// <param name="search">The key to look for.</param>
    /// <param name="record">The record number to look for among the entries holding that key.</param>
    /// <returns>
    /// [c]Found[/c] when that exact pair is present. Otherwise [c]After[/c] with the cursor on the first
    /// entry that sorts after the pair, or [c]Eof[/c] when there is none.
    /// </returns>
    /// <remarks>
    /// The tree orders by key *and* record number, so an exact position needs both: seek the key, then
    /// walk forward while the record number is below the target (tfile4go2fox, I4TAG.C:1339-1458). It
    /// answers a different question from [c]Seek[/c] — "is this entry present" rather than "where does
    /// this key start" — and the write path will need it again.
    /// </remarks>
    public SeekOutcome SeekExact(KeySearch search, uint record)
    {
        SeekOutcome outcome = Seek(search);

        while (outcome == SeekOutcome.Found)
        {
            IndexEntry entry = Current;

            if (entry.Record == record)
                return SeekOutcome.Found;

            // Equal keys are ordered by record number, so a run is walked in that order — and a
            // descending tag walks it backwards, from the highest record number to the lowest. Testing
            // the wrong way round would give up on the first entry of every descending run.
            bool passedIt = tag.Descending ? entry.Record < record : entry.Record > record;
            if (passedIt)
                return SeekOutcome.After;

            if (!StepInTagOrder(1))
                return SeekOutcome.Eof;

            outcome = search.Matches(Current.Key) ? SeekOutcome.Found : SeekOutcome.After;
        }

        return outcome;
    }

    /// <summary>
    /// Steps one entry in the tag's order and reports whether the entry there still matches.
    /// </summary>
    private SeekOutcome SeekAdjacentMatch(KeySearch search, int direction)
    {
        // Not on a matching entry: start over, which is what the C library does for the forward case.
        if (!IsOnKey || !search.Matches(Current.Key))
            return direction > 0 ? Seek(search) : SeekLast(search);

        if (!StepInTagOrder(direction))
            return SeekOutcome.NoEntry;

        return search.Matches(Current.Key) ? SeekOutcome.Found : SeekOutcome.NoEntry;
    }

    /// <summary>
    /// Positions on the first entry whose key is not less than a value, in **byte** order.
    /// </summary>
    /// <returns>False when every key sorts below the value, and the cursor is then past the end.</returns>
    /// <summary>
    /// Positions on the first entry whose key is not below a value, in stored byte order.
    /// </summary>
    /// <param name="search">What to look for.</param>
    /// <returns>True when the cursor landed on an entry, false when every key sorts below it.</returns>
    /// <remarks>
    /// Byte order, not the tag's order, so a descending tag is not inverted here. That is what the C
    /// library's [c]tfile4go[/c] positions with when it cannot find an exact key and record
    /// (I4TAG.C:1339-1516), and reproducing where a failed lookup leaves the cursor is the point of
    /// exposing it.
    /// </remarks>
    public bool SeekNearestByBytes(KeySearch search) => SeekFirstAtOrAbove(search);

    private bool SeekFirstAtOrAbove(KeySearch search)
    {
        TreeBlock current = tag.ReadBlock(tag.Header.Root);
        int guard = 0;

        while (!current.IsLeaf)
        {
            if (current.Count == 0)
            {
                throw new CodeBaseException(
                    ErrorCode.Index,
                    $"Interior node {current.Node} of tag {tag.Name} has no children.");
            }

            if (++guard > MaxDepth)
            {
                throw new CodeBaseException(
                    ErrorCode.Index,
                    $"Seeking tag {tag.Name} did not reach a leaf, so its tree has a cycle.");
            }

            current = tag.ReadBlock(current.Branch!.EntryAt(current.Branch!.Seek(search)).Child);
        }

        block = current;
        Eof = false;
        Bof = false;

        if (current.Count > 0)
        {
            current.Leaf!.Seek(search, out int at);

            if (at < current.Count)
            {
                index = at;
                return true;
            }
        }

        // Past the last entry of this leaf, so the answer is the first entry of the next one along —
        // a step in physical order, since this search is byte-ordered rather than tag-ordered.
        //
        // A descent that lands on a keyless leaf takes this path too, rather than reporting the end
        // of the tag. StepPhysical already steps over empty blocks, and a descent can reach one for
        // the same reason a walk can: the reference implementation's delete path leaves them behind.
        // Short-circuiting here on a count of zero was the one place the two disagreed.
        index = Math.Max(current.Count - 1, 0);

        if (!StepPhysical(1))
        {
            Eof = true;
            Bof = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Positions on the last entry whose key is not greater than a value, in **byte** order.
    /// </summary>
    /// <returns>False when every key sorts above the value, and the cursor is then before the start.</returns>
    /// <remarks>
    /// One past the value, then one step back: what lies before "the first key above v" is "the last key
    /// not above v". A value with no successor has nothing above it, so the answer is the last key of all.
    /// </remarks>
    private bool SeekLastAtOrBelow(KeySearch search)
    {
        if (!search.TryIncrement(out KeySearch past))
            return DescendLast();

        if (!SeekFirstAtOrAbove(past))
            return DescendLast();

        if (!StepPhysical(-1))
        {
            Bof = true;
            Eof = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Steps one entry in the tag's own order, whichever way that is physically.
    /// </summary>
    private bool StepInTagOrder(int direction) =>
        StepPhysical(tag.Descending ? -direction : direction);

    /// <summary>
    /// Moves one entry in physical key order, following the leaf chain when a block runs out.
    /// </summary>
    /// <param name="direction">Positive to move towards greater keys, negative towards lesser.</param>
    /// <returns>True when the cursor moved to a key.</returns>
    private bool StepPhysical(long direction)
    {
        if (block.Leaf is null)
            return false;

        // Re-entering the tag from either end lands *on* the boundary entry rather than stepping past
        // it: the index still remembers where the walk ran out, so stepping back from the end must
        // return to the last entry and not to the one before it. This is the same rule the record
        // cursor follows for a table (d4skip.c:1197-1202), and the reason it needs saying is that a
        // walk which merely stops at the end never notices the difference.
        if (Eof && direction < 0 && index < block.Leaf.Count)
        {
            Eof = false;
            Bof = false;
            return true;
        }

        if (Bof && direction > 0 && index < block.Leaf.Count)
        {
            Eof = false;
            Bof = false;
            return true;
        }

        int next = index + (direction > 0 ? 1 : -1);

        if (next >= 0 && next < block.Leaf.Count)
        {
            index = next;
            Eof = false;
            Bof = false;
            return true;
        }

        uint sibling = direction > 0 ? block.Header.RightNode : block.Header.LeftNode;
        bool hasSibling = direction > 0 ? block.Header.HasRight : block.Header.HasLeft;

        // A leaf with no neighbour that way is the end of the tag. Which flag that sets depends on the
        // tag's direction, not on the physical one, because both are about the tag's own order.
        if (!hasSibling)
        {
            if (direction > 0 == !tag.Descending)
                Eof = true;
            else
                Bof = true;

            return false;
        }

        // A block with no keys is skipped rather than refused. The C library's own delete path can
        // leave one behind, and treating it as the end of the tag would silently lose every key past
        // it (CDX-FORMAT.md section 14, item 10).
        uint node = sibling;
        long visited = 0;

        while (true)
        {
            // A run of empty blocks is legitimate and can be long, so the bound cannot be a constant.
            // What it can be is the file itself: a chain that visits more blocks than the file holds
            // has come back to one it already saw, whatever the shape of the loop.
            if (++visited > tag.BlockCount)
            {
                throw new CodeBaseException(
                    ErrorCode.Index,
                    $"The leaf chain of tag {tag.Name} passed through more than {tag.BlockCount} " +
                    $"blocks without finding a key, so it has a cycle at node {node}.");
            }

            TreeBlock candidate = tag.ReadBlock(node);
            if (!candidate.IsLeaf)
            {
                throw new CodeBaseException(
                    ErrorCode.Index,
                    $"The leaf chain of tag {tag.Name} reaches node {node}, which is an interior node.");
            }

            if (candidate.Count > 0)
            {
                block = candidate;
                index = direction > 0 ? 0 : candidate.Count - 1;
                Eof = false;
                Bof = false;
                return true;
            }

            bool more = direction > 0 ? candidate.Header.HasRight : candidate.Header.HasLeft;
            if (!more)
            {
                if (direction > 0 == !tag.Descending)
                    Eof = true;
                else
                    Bof = true;

                return false;
            }

            node = direction > 0 ? candidate.Header.RightNode : candidate.Header.LeftNode;
        }
    }

    private bool DescendFirst() => Descend(first: true);

    private bool DescendLast() => Descend(first: false);

    /// <summary>
    /// Walks from the root to the leftmost or rightmost leaf and lands on its outermost key.
    /// </summary>
    private bool Descend(bool first)
    {
        TreeBlock current = tag.ReadBlock(tag.Header.Root);
        int guard = 0;

        while (!current.IsLeaf)
        {
            if (current.Count == 0)
            {
                throw new CodeBaseException(
                    ErrorCode.Index,
                    $"Interior node {current.Node} of tag {tag.Name} has no children.");
            }

            if (++guard > MaxDepth)
            {
                throw new CodeBaseException(
                    ErrorCode.Index,
                    $"Descending tag {tag.Name} did not reach a leaf, so its tree has a cycle.");
            }

            BranchEntry entry = current.Branch!.EntryAt(first ? 0 : current.Count - 1);
            current = tag.ReadBlock(entry.Child);
        }

        block = current;

        if (current.Count == 0)
        {
            // Only a root leaf can legitimately be empty, and it means the tag has no keys.
            Eof = true;
            Bof = true;
            index = 0;
            return false;
        }

        index = first ? 0 : current.Count - 1;
        Eof = false;
        Bof = false;
        return true;
    }
}
