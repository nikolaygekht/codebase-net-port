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
    /// <returns>The block, over a fresh array the caller owns.</returns>
    /// <exception cref="CodeBaseException">
    /// The node is not a block reference, or the file does not hold a whole block there — whether
    /// because the node lies past the end of it or because the read came up short.
    /// </exception>
    public Block Read(uint node) => ReadAt(node, addressing.BlockSize, "block");

    /// <summary>
    /// Reads the tag header a node number refers to.
    /// </summary>
    /// <param name="node">The node number, as the tag directory records it.</param>
    /// <returns>The header, over a fresh array the caller owns.</returns>
    /// <exception cref="CodeBaseException">
    /// The node is not a block reference, or the file does not hold a whole header there.
    /// </exception>
    /// <remarks>
    /// A header is 1024 bytes rather than a block, so it gets its own read — but the same refusal, so
    /// that a directory pointing outside the file is reported as a corrupt index naming the node rather
    /// than as a short read of something unnamed.
    /// </remarks>
    public Block ReadHeader(uint node) => ReadAt(node, IndexHeader.Size, "tag header");

    /// <summary>
    /// Reads a block or a header, and refuses a node the file does not hold.
    /// </summary>
    /// <param name="node">The node number.</param>
    /// <param name="length">How many bytes to read there.</param>
    /// <param name="what">What is being read, as a literal, for the message when it cannot be.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="CodeBaseException">
    /// The node is not a block reference, or the file does not hold a whole block there.
    /// </exception>
    /// <remarks>
    /// **A node past the end of the file is caught by the read failing, not by a check before it**, and
    /// that is a performance decision with a measurement behind it. Asking a file its length is a
    /// syscall — [c]RandomAccess.GetLength[/c] — and this method runs once per block read, so a check
    /// here cost **850 nanoseconds per block, 3.4 microseconds of a 3.9-microsecond cached descent**:
    /// 87 per cent of the work, on the path the block cache exists to make syscall-free. Measured three
    /// ways, in claude/dev/009-performance-audit/results-length-syscall.txt.
    ///
    /// The reference does the same and for the same reason: [c]tfile4blockRead[/c] computes the position
    /// and reads ([c]i4tag.c:185[/c]), with no length check anywhere, because a read that cannot be
    /// satisfied already reports itself ([c]file4readError[/c], [c]f4file.c:2141[/c]).
    ///
    /// Nothing is given up. The length is still named in the message, asked for in the [c]catch[/c],
    /// where a syscall costs nothing because the call is already failing.
    /// </remarks>
    private Block ReadAt(uint node, int length, string what)
    {
        long offset = addressing.OffsetOf(node);

        try
        {
            return new Block(source.ReadExactly(offset, length));
        }
        catch (ShortReadException failure)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Node {node} is at offset {offset}, and a {length}-byte {what} there runs past the " +
                $"end of a {source.Length}-byte index file; the read got {failure.Got} bytes.",
                failure);
        }
    }
}
