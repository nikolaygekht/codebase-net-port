using System.Buffers.Binary;

namespace CodeBase.Net.Cdx;

/// <summary>
/// An interior block: a packed array of keys, each with the record it belongs to and the child below
/// it.
///
/// Entries start immediately after the block header with no padding and no geometry of their own, and
/// each is the key length plus eight bytes. The key is stored whole — interior nodes are not
/// compressed — and it is the greatest key of the child it points at.
///
/// The two numbers are the format's other big-endian island. Everything around them, the block
/// header included, is little-endian; these two are byte-reversed. A reader that gets it wrong still
/// descends to a block, which is why this is worth stating twice.
///
/// Governing specification: CDX-FORMAT.md sections 5.1 and 5.2.
/// </summary>
internal sealed class BranchBlock
{
    /// <summary>The bytes each entry adds to the key: a record number and a child node.</summary>
    private const int PointerSize = 8;

    private readonly byte[] block;
    private readonly int keyLength;
    private readonly int entrySize;

    private BranchBlock(byte[] block, NodeHeader header, int keyLength)
    {
        this.block = block;
        this.keyLength = keyLength;
        entrySize = keyLength + PointerSize;
        Header = header;
    }

    /// <summary>Gets the block's common header.</summary>
    public NodeHeader Header { get; }

    /// <summary>Gets the number of entries, which is the number of children.</summary>
    public int Count => Header.KeyCount;

    /// <summary>
    /// Reads an interior block.
    /// </summary>
    /// <param name="block">The block, its whole length.</param>
    /// <param name="header">The block's already-decoded common header.</param>
    /// <param name="keyLength">The tag's key length.</param>
    /// <param name="node">The node the block came from, for messages.</param>
    /// <returns>The block, ready to be asked for entries.</returns>
    /// <exception cref="CodeBaseException">
    /// The entries would not fit the block, which means the key count and the key length disagree
    /// with each other.
    /// </exception>
    public static BranchBlock Parse(byte[] block, NodeHeader header, int keyLength, uint node)
    {
        long end = NodeHeader.Size + ((long)header.KeyCount * (keyLength + PointerSize));
        if (end > block.Length)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Interior node {node} holds {header.KeyCount} entries of {keyLength + PointerSize} " +
                $"bytes, which runs {end - block.Length} bytes past the block.");
        }

        return new BranchBlock(block, header, keyLength);
    }

    /// <summary>
    /// Gives the entry at a position.
    /// </summary>
    /// <param name="index">Which entry, counting from zero.</param>
    /// <returns>The greatest key of the child, its record number, and the child's node number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The block has no entry at that position.</exception>
    public BranchEntry EntryAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        int offset = NodeHeader.Size + (index * entrySize);
        ReadOnlySpan<byte> entry = block.AsSpan(offset, entrySize);

        return new BranchEntry(
            entry[..keyLength].ToArray(),
            BinaryPrimitives.ReadUInt32BigEndian(entry[keyLength..]),
            BinaryPrimitives.ReadUInt32BigEndian(entry[(keyLength + 4)..]));
    }
}

/// <summary>
/// One interior entry: a child block, named by the greatest key it holds.
/// </summary>
/// <param name="Key">The child's greatest key, stored whole and padded as the tag pads keys.</param>
/// <param name="Record">The record number of that key, stored big-endian.</param>
/// <param name="Child">The node number of the child block, stored big-endian.</param>
internal readonly record struct BranchEntry(byte[] Key, uint Record, uint Child);
