namespace CodeBase.Net.Cdx;

/// <summary>
/// Turns a node number into a byte offset, which is the only arithmetic the tree needs.
///
/// A node number is multiplied by the file's multiplier. With the multiplier of one that Visual
/// FoxPro uses, node numbers are byte offsets and are always multiples of the block size, so the
/// arithmetic is invisible; CodeBase can also write larger blocks with a multiplier above one, and
/// then it is not (B4NODE.C:24-29).
///
/// Two node values are never block references: zero, because the file header lives there, and
/// 0xFFFFFFFF, which means none.
///
/// Governing specification: CDX-FORMAT.md section 1.
/// </summary>
internal readonly struct BlockAddressing
{
    /// <summary>
    /// The block size Visual FoxPro uses, and the unit tag headers are placed in.
    /// </summary>
    public const int StandardBlockSize = 512;

    /// <summary>
    /// The number of bytes the file header occupies, before which no tree block can begin.
    /// </summary>
    /// <value>
    /// Two blocks: a tag header is 512 bytes of fields plus a 512-byte area holding the expression
    /// and filter text.
    /// </value>
    public const int HeaderSize = 2 * StandardBlockSize;

    /// <summary>
    /// The node value that means "no node".
    /// </summary>
    public const uint NoNode = 0xFFFFFFFF;

    /// <summary>
    /// The marker that says a header's block size and multiplier were written and are meaningful.
    /// </summary>
    public const uint CodeBaseNote = 0xABCD;

    private BlockAddressing(int blockSize, int multiplier)
    {
        BlockSize = blockSize;
        Multiplier = multiplier;
    }

    /// <summary>
    /// The addressing every Visual FoxPro index file uses.
    /// </summary>
    public static BlockAddressing Standard => new(StandardBlockSize, 1);

    /// <summary>Gets the number of bytes in a tree block.</summary>
    public int BlockSize { get; }

    /// <summary>Gets what a node number is multiplied by to reach its byte offset.</summary>
    public int Multiplier { get; }

    /// <summary>
    /// Reads the block geometry a file header declares.
    /// </summary>
    /// <param name="note">The marker field, which is meaningful only when it holds 0xABCD.</param>
    /// <param name="blockSize">The block size the header carries.</param>
    /// <param name="multiplier">The multiplier the header carries.</param>
    /// <returns>
    /// The declared geometry, or the standard 512 and one when the marker is absent, which is the
    /// case for every file Visual FoxPro writes.
    /// </returns>
    /// <exception cref="CodeBaseException">
    /// The declared geometry contradicts itself: a block size that is not a positive multiple of 512,
    /// or a multiplier that does not divide it. Both are refusals the C library makes at open
    /// (i4init.c:536-548).
    /// </exception>
    public static BlockAddressing Resolve(uint note, uint blockSize, uint multiplier)
    {
        if (note != CodeBaseNote)
            return Standard;

        if (blockSize == 0 || blockSize % StandardBlockSize != 0 || blockSize > int.MaxValue)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"The index declares a block size of {blockSize}, which is not a positive multiple " +
                $"of {StandardBlockSize}.");
        }

        if (multiplier == 0 || blockSize % multiplier != 0)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"The index declares a multiplier of {multiplier}, which does not divide its block " +
                $"size of {blockSize}.");
        }

        return new BlockAddressing((int)blockSize, (int)multiplier);
    }

    /// <summary>
    /// Gives the byte offset a node number refers to.
    /// </summary>
    /// <param name="node">The node number, as a header or a sibling or child pointer holds it.</param>
    /// <returns>The offset of the block in the file.</returns>
    /// <exception cref="CodeBaseException">
    /// The node is zero or 0xFFFFFFFF, neither of which addresses a block, or it names an offset that
    /// falls inside the file header. A caller that expects those values has to test for them before
    /// asking, because reaching here with one means the tree is corrupt.
    /// </exception>
    public long OffsetOf(uint node)
    {
        if (node == 0 || node == NoNode)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Node {node} is not a block reference. Zero is the file header and 0xFFFFFFFF means " +
                $"no node.");
        }

        long offset = (long)node * Multiplier;
        if (offset < HeaderSize)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Node {node} is at offset {offset}, which is inside the {HeaderSize}-byte header.");
        }

        return offset;
    }
}
