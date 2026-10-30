using System.Buffers.Binary;
using System.IO.Compression;
using CodeBase.Net.IO;

namespace CodeBase.Net.Memo;

/// <summary>
/// Reads the entry a block number points at.
///
/// The whole of the arithmetic that turns a block number into a file offset, and the guards that
/// keep a corrupt one from being believed. It owns no handle and decodes no text.
///
/// Blocks address entries; they do not chop them up. An entry is its eight-byte header followed by
/// its payload, contiguous, however many block boundaries that crosses. Blocks matter for finding
/// an entry and for allocating one, and allocation belongs to the write path.
///
/// A compressed entry, which only CodeBase writes, is decompressed here, so every caller sees the
/// payload the writer was given.
///
/// Governing specification: FPT-MEMO.md sections 3.6 and 3.9.
/// </summary>
internal sealed class MemoReader
{
    /// <summary>
    /// The largest payload the reference implementation will believe.
    /// </summary>
    /// <value>
    /// From the corruption guard at m4file.c:229-238. A length above this is taken as a damaged
    /// entry rather than as a very large one.
    /// </value>
    private const uint MaximumLength = 0x7FFFFFF0;

    /// <summary>
    /// The declared payload length above which the file is asked whether it really holds that much.
    /// </summary>
    /// <remarks>
    /// Not a limit on a memo entry: a longer one is read, it is only checked against the file first.
    /// The value is a balance and nothing more — large enough that no ordinary entry pays a syscall,
    /// small enough that a buffer allocated for a corrupt claim and then thrown away costs nothing.
    /// </remarks>
    private const uint LengthWorthAsking = 64 * 1024;

    /// <summary>
    /// The bytes a compressed entry spends on its uncompressed length, ahead of the zlib stream.
    /// </summary>
    private const int CompressedLengthSize = 4;

    private readonly IRandomAccessSource source;
    private readonly int blockSize;

    /// <summary>
    /// Initializes a new instance over an open memo file.
    /// </summary>
    /// <param name="source">The memo file.</param>
    /// <param name="blockSize">
    /// Bytes per block, from the file header. Zero is legal and means byte granularity rather than
    /// an error.
    /// </param>
    public MemoReader(IRandomAccessSource source, int blockSize)
    {
        this.source = source;
        this.blockSize = blockSize;
    }

    /// <summary>
    /// Gets the number of bytes a block occupies when addressing an entry.
    /// </summary>
    /// <value>
    /// One where the header says zero. A block size of zero is legal and means the file is addressed
    /// by the byte (m4file.c:620-631).
    /// </value>
    public int EffectiveBlockSize => blockSize == 0 ? 1 : blockSize;

    /// <summary>
    /// Returns where an entry starts in the memo file.
    /// </summary>
    /// <param name="blockNumber">The block the entry begins at.</param>
    /// <returns>The offset of its first byte, which is the first byte of its header.</returns>
    public long OffsetOf(int blockNumber) => (long)blockNumber * EffectiveBlockSize;

    /// <summary>
    /// Reads the entry at a block number.
    /// </summary>
    /// <param name="blockNumber">The block, or zero where the record has no memo.</param>
    /// <param name="what">What is being read, for the message if it cannot be.</param>
    /// <returns>
    /// The entry, or an absent one where the block number says there is none. A compressed entry is
    /// returned decompressed, still reporting its type as compressed.
    /// </returns>
    /// <exception cref="CodeBaseException">
    /// The entry lies outside the file, declares an impossible length, or is compressed and damaged.
    /// </exception>
    public MemoEntry Read(int blockNumber, string what)
    {
        if (blockNumber <= MemoReference.None)
            return MemoEntry.Absent;

        long offset = OffsetOf(blockNumber);

        // A block past the end of the file is caught by the header read failing, not by asking the file
        // its length first: that question is a syscall and this method is on the memo path for every
        // field access (ADR-35, and results-length-syscall.txt section 7 for what it cost here). The
        // header is a fixed few bytes, so nothing is sized from the file before the read.
        MemoBlockHeader header = MemoBlockHeader.Parse(ReadBlockHeader(offset, blockNumber));

        // Unlike the header, the payload's size comes out of the file and sizes an allocation, so it
        // cannot simply be left to the read: a corrupt header claiming half a gigabyte would have the
        // buffer allocated before the read came up short. A claim small enough that a wasted buffer
        // costs nothing is left to the read; only a claim big enough to matter is worth the syscall
        // that refuses it.
        if (header.Length > MaximumLength ||
            (header.Length > LengthWorthAsking &&
             header.Length > source.Length - offset - MemoBlockHeader.Size))
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"{what} declares {header.Length} bytes at memo block {blockNumber}, which does not " +
                $"fit in a file {source.Length} bytes long. The memo file is shorter than its " +
                "entries claim.");
        }

        byte[] payload = header.Length == 0
            ? []
            : ReadPayload(offset + MemoBlockHeader.Size, (int)header.Length, blockNumber);

        // The reference decompresses whatever says it is compressed, whether or not the table declares
        // the extension (m4file.c:207, 268-282), and keeps reporting the type as stored (m4file.c:256).
        if (header.Type == MemoType.Compressed)
            payload = Decompress(payload, blockNumber, what);

        return new MemoEntry(header.Type, payload);
    }

    /// <summary>
    /// Decompresses the payload of a compressed entry.
    /// </summary>
    /// <param name="stored">
    /// The payload as stored: the uncompressed length as a 4-byte little-endian integer, then a zlib
    /// stream (C4CODE.C:402-444).
    /// </param>
    /// <param name="blockNumber">Which block, for the message when it is damaged.</param>
    /// <param name="what">What is being read, for the message when it is damaged.</param>
    /// <returns>The uncompressed payload, exactly as long as the entry says.</returns>
    /// <exception cref="CodeBaseException">
    /// The entry is too short, declares an impossible length, is not a zlib stream, or does not
    /// decompress to exactly the length it declares.
    /// </exception>
    private static byte[] Decompress(byte[] stored, int blockNumber, string what)
    {
        // The reference ignores what uncompress() returns (m4file.c:281), so a damaged entry would come
        // back as a buffer of whatever had been written into it. A wrong memo that reads as a right one
        // is the worst outcome available here; every way the entry can disagree with itself is refused.
        string where = $"{what} is a compressed memo at block {blockNumber}";

        if (stored.Length < CompressedLengthSize + 2)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"{where}, but its {stored.Length} bytes cannot hold a length and a zlib stream.");
        }

        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(stored);
        if (declared > MaximumLength)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"{where} declaring {declared} bytes, more than any memo entry can hold.");
        }

        // The zlib header: deflate with a window the format allows, and a check value that makes the
        // two bytes a multiple of 31 (RFC 1950 section 2.2). An entry from a CodeBase built with QuickLZ
        // carries the same type and the same length prefix, and fails here instead of inflating to
        // noise.
        byte method = stored[CompressedLengthSize];
        byte flags = stored[CompressedLengthSize + 1];
        if ((method & 0x0F) != 8 || (method >> 4) > 7 || ((method << 8) | flags) % 31 != 0)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"{where}, but its data is not a zlib stream. It may have been written by a CodeBase " +
                "built with a different compression library.");
        }

        byte[] payload = new byte[declared];
        try
        {
            using MemoryStream compressed = new(
                stored, CompressedLengthSize, stored.Length - CompressedLengthSize, writable: false);
            using ZLibStream inflater = new(compressed, CompressionMode.Decompress);

            int got = inflater.ReadAtLeast(payload, payload.Length, throwOnEndOfStream: false);

            // Reading one byte past the declared length is what reaches the end of the stream, and so
            // what has the trailing Adler-32 checked.
            if (got == payload.Length && inflater.ReadByte() < 0)
                return payload;

            throw new CodeBaseException(
                ErrorCode.Data,
                $"{where} declaring {declared} bytes, but its data " +
                (got < payload.Length ? $"ends after {got}." : "holds more."));
        }
        catch (InvalidDataException failure)
        {
            throw new CodeBaseException(
                ErrorCode.Data, $"{where}, and its data is damaged: {failure.Message}", failure);
        }
    }

    /// <summary>
    /// Reads the header that starts a memo block.
    /// </summary>
    /// <param name="offset">Where the block begins.</param>
    /// <param name="blockNumber">Which block, for the message when it is not all there.</param>
    /// <returns>The header's bytes.</returns>
    /// <exception cref="CodeBaseException">The file ends inside the header.</exception>
    private byte[] ReadBlockHeader(long offset, int blockNumber)
    {
        try
        {
            return source.ReadExactly(offset, MemoBlockHeader.Size);
        }
        catch (ShortReadException failure)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"Reading the header of memo block {blockNumber} needed {failure.Needed} bytes at " +
                $"offset {failure.Offset} but the file gave {failure.Got}.",
                failure);
        }
    }

    /// <summary>
    /// Reads the bytes a memo block's header says follow it.
    /// </summary>
    /// <param name="offset">Where the payload begins.</param>
    /// <param name="length">How long the header says it is.</param>
    /// <param name="blockNumber">Which block, for the message when it is not all there.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="CodeBaseException">The file ends inside the payload.</exception>
    private byte[] ReadPayload(long offset, int length, int blockNumber)
    {
        try
        {
            return source.ReadExactly(offset, length);
        }
        catch (ShortReadException failure)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"Memo block {blockNumber} declares {failure.Needed} bytes at offset " +
                $"{failure.Offset} and the file gave {failure.Got}. The memo file is shorter than " +
                "its entries claim.",
                failure);
        }
    }
}
