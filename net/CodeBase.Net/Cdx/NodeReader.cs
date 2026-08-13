using CodeBase.Net.IO;

namespace CodeBase.Net.Cdx;

/// <summary>
/// Reads a tree block by node number, and refuses the ones that cannot be blocks.
///
/// Every block of every tag comes through here, so the guards are in one place: a node that is not a
/// block reference, a block that would run past the end of the file, a short read. A tree that names
/// a block outside the file is corrupt, and following the reference anyway would hand a caller
/// whatever bytes happened to be there.
///
/// Governing specification: CDX-FORMAT.md section 11.
/// </summary>
internal sealed class NodeReader
{
    private readonly IRandomAccessSource source;
    private readonly BlockAddressing addressing;

    /// <summary>
    /// Initializes a new instance over an open index file.
    /// </summary>
    /// <param name="source">The index file.</param>
    /// <param name="addressing">How node numbers turn into offsets in it.</param>
    public NodeReader(IRandomAccessSource source, BlockAddressing addressing)
    {
        this.source = source;
        this.addressing = addressing;
    }

    /// <summary>Gets the number of bytes in a block of this file.</summary>
    public int BlockSize => addressing.BlockSize;

    /// <summary>
    /// Gets how many blocks the file is large enough to hold.
    /// </summary>
    /// <value>
    /// An upper bound on how many distinct blocks any walk through the file can visit, which is what
    /// makes a walk that visits more than this a cycle rather than a long chain.
    /// </value>
    public long BlockCount => source.Length / addressing.BlockSize;

    /// <summary>
    /// Reads the block a node number refers to.
    /// </summary>
    /// <param name="node">The node number, from a header or a sibling or child pointer.</param>
    /// <returns>The block's bytes, a fresh array the caller owns.</returns>
    /// <exception cref="CodeBaseException">
    /// The node is not a block reference, the block would lie past the end of the file, or the file
    /// gave fewer bytes than a block.
    /// </exception>
    public byte[] Read(uint node) => ReadAt(node, addressing.BlockSize, "block");

    /// <summary>
    /// Reads the tag header a node number refers to.
    /// </summary>
    /// <param name="node">The node number, as the tag directory records it.</param>
    /// <returns>The header's bytes, a fresh array the caller owns.</returns>
    /// <exception cref="CodeBaseException">
    /// The node is not a block reference, the header would lie past the end of the file, or the file
    /// gave fewer bytes than a header.
    /// </exception>
    /// <remarks>
    /// A header is 1024 bytes rather than a block, so it gets its own read — but the same guard, so
    /// that a directory pointing outside the file is refused as a corrupt index rather than reported as
    /// a short read of something.
    /// </remarks>
    public byte[] ReadHeader(uint node) => ReadAt(node, IndexHeader.Size, "tag header");

    private byte[] ReadAt(uint node, int length, string what)
    {
        long offset = addressing.OffsetOf(node);

        if (offset + length > source.Length)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Node {node} is at offset {offset}, and a {length}-byte {what} there runs past the " +
                $"end of a {source.Length}-byte index file.");
        }

        return source.ReadExactly(offset, length, $"index node {node}", ErrorCode.Index);
    }
}
