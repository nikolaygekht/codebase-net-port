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
/// Governing specification: FPT-MEMO.md section 3.6.
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

    private readonly IRandomAccessSource source;
    private readonly int blockSize;
    private readonly bool mayHaveCompressedMemos;

    /// <summary>
    /// Initializes a new instance over an open memo file.
    /// </summary>
    /// <param name="source">The memo file.</param>
    /// <param name="blockSize">
    /// Bytes per block, from the file header. Zero is legal and means byte granularity rather than
    /// an error.
    /// </param>
    /// <param name="mayHaveCompressedMemos">
    /// Whether the table was created with the compressed-memo extension enabled, so that meeting a
    /// compressed entry can be explained rather than merely refused.
    /// </param>
    public MemoReader(IRandomAccessSource source, int blockSize, bool mayHaveCompressedMemos)
    {
        this.source = source;
        this.blockSize = blockSize;
        this.mayHaveCompressedMemos = mayHaveCompressedMemos;
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
    /// <returns>The entry, or an absent one where the block number says there is none.</returns>
    /// <exception cref="CodeBaseException">
    /// The entry lies outside the file, declares an impossible length, or is compressed.
    /// </exception>
    public MemoEntry Read(int blockNumber, string what)
    {
        if (blockNumber <= MemoReference.None)
            return MemoEntry.Absent;

        long offset = OffsetOf(blockNumber);

        if (offset < 0 || offset > source.Length - MemoBlockHeader.Size)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"{what} refers to memo block {blockNumber}, whose header would be at offset " +
                $"{offset} of a file {source.Length} bytes long.");
        }

        MemoBlockHeader header = MemoBlockHeader.Parse(
            source.ReadExactly(offset, MemoBlockHeader.Size, $"the header of memo block {blockNumber}"));

        if (header.Type == MemoType.Compressed)
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"{what} is a compressed memo, at block {blockNumber}. Compressed entries are a " +
                "CodeBase extension that Visual FoxPro cannot read, and this library does not read " +
                "them yet: no file it can generate contains one, so a decoder for them could not be " +
                "checked against the reference implementation. " +
                (mayHaveCompressedMemos
                    ? "The table was created with compressed memos enabled, so others are likely."
                    : "The table does not declare compressed memos, so this entry is unexpected."));
        }

        if (header.Length > MaximumLength || header.Length > source.Length - offset - MemoBlockHeader.Size)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"{what} declares {header.Length} bytes at memo block {blockNumber}, which does not " +
                $"fit in a file {source.Length} bytes long. The memo file is shorter than its " +
                "entries claim.");
        }

        byte[] payload = header.Length == 0
            ? []
            : source.ReadExactly(
                offset + MemoBlockHeader.Size, (int)header.Length, $"memo block {blockNumber}");

        return new MemoEntry(header.Type, payload);
    }
}
