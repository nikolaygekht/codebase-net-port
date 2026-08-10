using System.Buffers.Binary;

namespace CodeBase.Net.Memo;

/// <summary>
/// The eight-byte header that begins a memo file.
///
/// Both of its numbers are stored big-endian, which is worth stating because the table beside it is
/// little-endian throughout. Endianness in these formats is a property of the field, not of the
/// file, so every read here says which one it means.
///
/// Governing specification: FPT-MEMO.md section 3.1.
/// </summary>
internal readonly struct MemoFileHeader
{
    /// <summary>
    /// The number of bytes a memo file header occupies.
    /// </summary>
    public const int Size = 8;

    private MemoFileHeader(uint nextBlock, int blockSize)
    {
        NextBlock = nextBlock;
        BlockSize = blockSize;
    }

    /// <summary>
    /// Gets the block number at which the next entry written would go.
    /// </summary>
    /// <value>
    /// An allocation pointer, not a free list. This format never reuses a freed block, so the file
    /// grows until it is compacted.
    /// </value>
    public uint NextBlock { get; }

    /// <summary>
    /// Gets the number of bytes in a block.
    /// </summary>
    /// <value>
    /// Usually 512. Zero is legal and means byte granularity rather than an error, so it must not
    /// be treated as a corrupt file.
    /// </value>
    public int BlockSize { get; }

    /// <summary>
    /// Reads a memo file header.
    /// </summary>
    /// <param name="bytes">The first eight bytes of the file.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="CodeBaseException">Fewer than eight bytes were supplied.</exception>
    public static MemoFileHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"A memo file header is {Size} bytes; the file holds only {bytes.Length}.");
        }

        return new MemoFileHeader(
            BinaryPrimitives.ReadUInt32BigEndian(bytes),
            BinaryPrimitives.ReadUInt16BigEndian(bytes[6..]));
    }
}
