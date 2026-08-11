using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// The arithmetic that turns a record number into a file offset, and what happens when it runs out of file.
///
/// Reading one record too early or too late is the most plausible-looking failure available: every
/// field of every record comes back wrong while still parsing perfectly. So the offsets are asserted
/// directly, against an image whose records say which one they are.
/// </summary>
[Trait("Layer", "Component")]
public sealed class RecordReaderTests
{
    private const int HeaderLength = 97;
    private const int RecordLength = 8;

    [Fact]
    public void OffsetOf_PutsTheFirstRecordAtTheEndOfTheHeader()
    {
        Reader(Image(3)).OffsetOf(1).Should().Be(HeaderLength);
    }

    [Fact]
    public void OffsetOf_AdvancesOneRecordLengthPerRecord()
    {
        RecordReader reader = Reader(Image(3));

        reader.OffsetOf(2).Should().Be(HeaderLength + RecordLength);
        reader.OffsetOf(3).Should().Be(HeaderLength + (2 * RecordLength));
    }

    [Fact]
    public void OffsetOf_ComputesInSixtyFourBitsSoALargeTableCannotWrap()
    {
        // A record number near the end of a two-billion-record table overflows a 32-bit product and
        // wraps to a plausible-looking offset inside the file.
        RecordReader reader = new(new InMemorySource([]), headerLength: 0, recordLength: 1000);

        reader.OffsetOf(2_000_000_000).Should().Be(1_999_999_999_000L);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Read_FetchesTheRecordAsked(int recordNumber)
    {
        // Each record says which one it is, so reading one record out means reading the wrong text
        // rather than a subtly wrong value.
        RecordBuffer buffer = new(RecordLength);

        Reader(Image(3)).Read(recordNumber, buffer);

        Encoding.ASCII.GetString(buffer.Slice(0, RecordLength, "record"))
                      .Should().Be($" rec {recordNumber}  ");
    }

    [Fact]
    public void Read_FillsTheBufferEvenWhenTheSourceHandsBackOneByteAtATime()
    {
        RecordBuffer buffer = new(RecordLength);
        RecordReader reader = new(new DribblingSource(Image(3)), HeaderLength, RecordLength);

        reader.Read(2, buffer);

        Encoding.ASCII.GetString(buffer.Slice(0, RecordLength, "record")).Should().Be(" rec 2  ");
    }

    [Fact]
    public void Read_AFileTruncatedPartwayThroughARecord_IsRefused()
    {
        // Zero-filling the tail would turn a truncated file into a record of plausible blanks.
        byte[] truncated = Image(3)[..(HeaderLength + RecordLength + 3)];
        RecordBuffer buffer = new(RecordLength);

        Action act = () => Reader(truncated).Read(2, buffer);

        act.Should().Throw<CodeBaseException>().WithMessage("*record 2*");
    }

    [Fact]
    public void Read_PastTheEndOfTheFile_IsRefused()
    {
        RecordBuffer buffer = new(RecordLength);

        Action act = () => Reader(Image(3)).Read(4, buffer);

        act.Should().Throw<CodeBaseException>().WithMessage("*shorter than its header says*");
    }

    [Fact]
    public void Read_AFileThatCannotBeRead_LetsTheFailureThrough()
    {
        // Not wrapped: an unreadable device is not a malformed table, and saying it is would send a
        // caller looking in the wrong place.
        RecordReader reader = new(
            new FaultySource(1024, FaultySource.Fault.Throw), HeaderLength, RecordLength);

        Action act = () => reader.Read(1, new RecordBuffer(RecordLength));

        act.Should().Throw<IOException>();
    }

    private static RecordReader Reader(byte[] image) =>
        new(new InMemorySource(image), HeaderLength, RecordLength);

    /// <summary>
    /// Builds a table image whose every record names itself.
    /// </summary>
    private static byte[] Image(int recordCount)
    {
        byte[] image = new byte[HeaderLength + (recordCount * RecordLength)];
        image.AsSpan(0, HeaderLength).Fill((byte)'H');

        for (int i = 1; i <= recordCount; i++)
        {
            Encoding.ASCII.GetBytes($" rec {i}  ")
                          .CopyTo(image.AsSpan(HeaderLength + ((i - 1) * RecordLength)));
        }

        return image;
    }

    /// <summary>
    /// A source that returns one byte per read, as a network filesystem may.
    /// </summary>
    private sealed class DribblingSource : CodeBase.Net.IO.IRandomAccessSource
    {
        private readonly InMemorySource inner;

        public DribblingSource(byte[] bytes) => inner = new InMemorySource(bytes);

        public long Length => inner.Length;

        public int Read(long offset, Span<byte> buffer) =>
            buffer.Length == 0 ? 0 : inner.Read(offset, buffer[..1]);

        public void Dispose() => inner.Dispose();
    }
}
