namespace CodeBase.Net.Cdx;

/// <summary>
/// One tag of an index file: what it is, and access to the blocks of its tree.
///
/// Immutable, and holds no position. Where the C library keeps one cursor per tag, this is the
/// description and [c]TagCursor[/c] is the position, so several walks of one tag can be in progress at
/// once and the state machine can be tested without a file.
///
/// Governing specification: CDX-FORMAT.md sections 3 and 5 to 7.
/// </summary>
internal sealed class CdxTag
{
    private readonly NodeReader nodes;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="name">The tag's name, upper-cased and trimmed as the file stores it.</param>
    /// <param name="header">The tag's header.</param>
    /// <param name="padByte">The byte a trailing-pad count stands for in this tag's keys.</param>
    /// <param name="nodes">Reads the blocks of the file this tag lives in.</param>
    public CdxTag(string name, IndexHeader header, byte padByte, NodeReader nodes)
    {
        this.nodes = nodes;
        Name = name;
        Header = header;
        PadByte = padByte;
    }

    /// <summary>Gets the tag's name.</summary>
    /// <value>
    /// In a compound file it is the key the tag directory holds, trimmed. In a single-tag file the
    /// file itself does not record a name, so it is the file's own name without path or extension,
    /// which is what the C library uses (i4index.c:1694).
    /// </value>
    public string Name { get; }

    /// <summary>Gets the tag's header.</summary>
    public IndexHeader Header { get; }

    /// <summary>Gets the byte a trailing-pad count stands for in this tag's keys.</summary>
    public byte PadByte { get; }

    /// <summary>Gets the number of bytes in each of the tag's keys.</summary>
    public int KeyLength => Header.KeyLength;

    /// <summary>Gets a value indicating whether the tag is walked from its greatest key down.</summary>
    public bool Descending => Header.Descending;

    /// <summary>
    /// Opens a cursor over the tag, positioned nowhere.
    /// </summary>
    /// <returns>A cursor that has to be positioned before it reports anything.</returns>
    public TagCursor OpenCursor() => new(this);

    /// <summary>
    /// Reads a block of this tag's tree and decodes it as whichever kind it says it is.
    /// </summary>
    /// <param name="node">The node number of the block.</param>
    /// <returns>The block, as a leaf or as an interior node.</returns>
    /// <exception cref="CodeBaseException">The block cannot be read or contradicts itself.</exception>
    public TreeBlock ReadBlock(uint node)
    {
        byte[] block = nodes.Read(node);
        NodeHeader header = NodeHeader.Parse(block, node);

        return header.IsLeaf
            ? new TreeBlock(node, header, LeafBlock.Parse(block, header, KeyLength, PadByte, node), null)
            : new TreeBlock(node, header, null, BranchBlock.Parse(block, header, KeyLength, node));
    }
}

/// <summary>
/// A block of a tag's tree, decoded as the kind its attribute says it is.
/// </summary>
/// <param name="Node">The node number the block was read from.</param>
/// <param name="Header">The block's common header.</param>
/// <param name="Leaf">The block as a leaf, or null when it is an interior node.</param>
/// <param name="Branch">The block as an interior node, or null when it is a leaf.</param>
internal readonly record struct TreeBlock(uint Node, NodeHeader Header, LeafBlock? Leaf, BranchBlock? Branch)
{
    /// <summary>Gets a value indicating whether the block holds keys rather than child pointers.</summary>
    public bool IsLeaf => Leaf is not null;

    /// <summary>Gets the number of entries in the block, whichever kind it is.</summary>
    public int Count => Leaf?.Count ?? Branch!.Count;
}
