using System.Buffers.Binary;

namespace CodeBase.Net.Cdx;

/// <summary>
/// The twelve bytes every tree block begins with, leaf and interior alike.
///
/// Whether a block is a leaf is a bit test and not a comparison. The C library tests bit 0x02
/// (b4block.c:2003-2014), and it matters: FoxPro 8.0 sets other bits, so an attribute of 5 is a leaf
/// while an attribute of 4 is not, and a reader comparing against two gets both wrong.
///
/// Governing specification: CDX-FORMAT.md section 4.
/// </summary>
internal readonly struct NodeHeader
{
    /// <summary>
    /// The number of bytes a block header occupies.
    /// </summary>
    public const int Size = 12;

    /// <summary>The bit that marks a block as a leaf.</summary>
    private const int LeafBit = 0x02;

    /// <summary>The bit that marks a block as the root of its tree.</summary>
    private const int RootBit = 0x01;

    private NodeHeader(int attribute, int keyCount, uint leftNode, uint rightNode)
    {
        Attribute = attribute;
        KeyCount = keyCount;
        LeftNode = leftNode;
        RightNode = rightNode;
    }

    /// <summary>Gets the attribute word exactly as stored.</summary>
    public int Attribute { get; }

    /// <summary>Gets the number of entries the block holds.</summary>
    public int KeyCount { get; }

    /// <summary>Gets the node number of the block to the left, or 0xFFFFFFFF when there is none.</summary>
    public uint LeftNode { get; }

    /// <summary>Gets the node number of the block to the right, or 0xFFFFFFFF when there is none.</summary>
    public uint RightNode { get; }

    /// <summary>Gets a value indicating whether the block holds keys rather than child pointers.</summary>
    public bool IsLeaf => (Attribute & LeafBit) != 0;

    /// <summary>Gets a value indicating whether the block is the root of its tree.</summary>
    /// <value>A one-block tree is both root and leaf, and its attribute is 3.</value>
    public bool IsRoot => (Attribute & RootBit) != 0;

    /// <summary>Gets a value indicating whether the block has a left sibling.</summary>
    public bool HasLeft => LeftNode != BlockAddressing.NoNode && LeftNode != 0;

    /// <summary>Gets a value indicating whether the block has a right sibling.</summary>
    public bool HasRight => RightNode != BlockAddressing.NoNode && RightNode != 0;

    /// <summary>
    /// Reads a block header.
    /// </summary>
    /// <param name="block">The block, of which the first twelve bytes are read.</param>
    /// <param name="node">The node the block came from, for the message if it is unreadable.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="CodeBaseException">
    /// The block is short, or its key count is negative or larger than the block could hold.
    /// </exception>
    public static NodeHeader Parse(ReadOnlySpan<byte> block, uint node)
    {
        if (block.Length < Size)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Node {node} needs {Size} bytes of header; only {block.Length} were available.");
        }

        int keyCount = BinaryPrimitives.ReadInt16LittleEndian(block[2..]);

        // A negative count would index backwards out of the block, and a count above the block size
        // cannot be right whatever the entry width is. Both are the checks the C library makes when
        // index verification is on (I4TAG.C:291-303).
        if (keyCount < 0 || keyCount > block.Length)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Node {node} says it holds {keyCount} keys, which does not fit a block of " +
                $"{block.Length} bytes.");
        }

        return new NodeHeader(
            BinaryPrimitives.ReadInt16LittleEndian(block),
            keyCount,
            BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(block[8..]));
    }
}
