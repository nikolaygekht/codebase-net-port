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
    /// Moves one entry in physical key order, following the leaf chain when a block runs out.
    /// </summary>
    /// <param name="direction">Positive to move towards greater keys, negative towards lesser.</param>
    /// <returns>True when the cursor moved to a key.</returns>
    private bool StepPhysical(long direction)
    {
        if (block.Leaf is null)
            return false;

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
        while (true)
        {
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

            // A tree deeper than this is a cycle, not a tree: with the smallest possible fan-out of
            // two, thirty-two levels already address more blocks than a node number can name.
            if (++guard > 32)
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
