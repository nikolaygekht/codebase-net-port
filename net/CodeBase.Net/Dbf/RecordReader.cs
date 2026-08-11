using CodeBase.Net.IO;

namespace CodeBase.Net.Dbf;

/// <summary>
/// Fetches the bytes of a numbered record into a buffer.
///
/// The whole of the arithmetic that turns a record number into a file offset, and nothing else. It
/// owns no handle and decodes nothing, which is what lets the offset formula be tested against a
/// byte array and lets a decoder be tested with no file at all.
///
/// A record that reads short is refused rather than padded. The bytes that did not arrive would
/// otherwise be zeros, and a record of zeros decodes into a whole row of plausible blanks.
/// </summary>
internal sealed class RecordReader
{
    private readonly IRandomAccessSource source;
    private readonly int headerLength;
    private readonly int recordLength;

    /// <summary>
    /// Initializes a new instance for a table's layout.
    /// </summary>
    /// <param name="source">Where the table's bytes are.</param>
    /// <param name="headerLength">How many bytes come before the first record.</param>
    /// <param name="recordLength">Record width in bytes, including the leading deletion flag.</param>
    public RecordReader(IRandomAccessSource source, int headerLength, int recordLength)
    {
        this.source = source;
        this.headerLength = headerLength;
        this.recordLength = recordLength;
    }

    /// <summary>
    /// Returns where a record starts in the file.
    /// </summary>
    /// <param name="recordNumber">The record, counting from one.</param>
    /// <returns>The offset of its first byte, which is its deletion flag.</returns>
    public long OffsetOf(int recordNumber) =>
        headerLength + ((long)(recordNumber - 1) * recordLength);

    /// <summary>
    /// Reads a record into a buffer.
    /// </summary>
    /// <param name="recordNumber">The record to read, counting from one.</param>
    /// <param name="buffer">Where to put it. Filled completely or not at all.</param>
    /// <exception cref="CodeBaseException">
    /// The file gave fewer bytes than the record needs, which means it is shorter than its own
    /// header says.
    /// </exception>
    public void Read(int recordNumber, RecordBuffer buffer)
    {
        long offset = OffsetOf(recordNumber);
        Span<byte> into = buffer.Raw;
        int read = 0;

        while (read < into.Length)
        {
            int step = source.Read(offset + read, into[read..]);
            if (step <= 0)
            {
                throw new CodeBaseException(
                    ErrorCode.Data,
                    $"Reading record {recordNumber} needed {into.Length} bytes at offset {offset} " +
                    $"but the file gave {read}. The file is shorter than its header says.");
            }

            read += step;
        }
    }
}
