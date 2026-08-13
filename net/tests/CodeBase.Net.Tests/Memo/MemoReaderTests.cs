using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Memo;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Memo;

/// <summary>
/// Finding an entry, reading it whole, and refusing one that cannot be believed.
///
/// The corpus proves the layout against real files, all of which use a 512-byte block and hold only
/// text entries. What is left for here is the geometry the corpus does not have — other block sizes,
/// including zero, and payloads spanning three blocks — and every way a file can be wrong.
///
/// Hand-built memo images are legitimate test [i]input[/i]. What a real payload decodes to still
/// comes from the corpus. See DEV_APPROACH.md section 4.
/// </summary>
[Trait("Layer", "Component")]
public sealed class MemoReaderTests
{
    [Fact]
    public void Read_OfBlockZero_IsAnAbsentMemoAndTouchesNoFile()
    {
        // The reference implementation returns before opening anything, so a table whose memo file
        // is missing still reads its empty memo fields.
        MemoReader reader = new(new InMemorySource([]), blockSize: 512, mayHaveCompressedMemos: false);

        MemoEntry entry = reader.Read(0, "a field");

        entry.Payload.Should().BeEmpty();
        entry.Type.Should().Be(MemoType.Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Read_OfANegativeBlock_IsAnAbsentMemo(int block)
    {
        MemoReader reader = new(new InMemorySource([]), 512, false);

        reader.Read(block, "a field").Payload.Should().BeEmpty();
    }

    [Fact]
    public void OffsetOf_MultipliesTheBlockNumberByTheBlockSize()
    {
        MemoReader reader = new(new InMemorySource([]), 512, false);

        reader.OffsetOf(1).Should().Be(512);
        reader.OffsetOf(27).Should().Be(13824);
    }

    [Fact]
    public void ABlockSizeOfZero_AddressesByTheByte()
    {
        // Legal, and it means one rather than an error. A reader that divided by it would fault on a
        // file the reference implementation reads happily. Ungated: every corpus file uses 512.
        MemoReader reader = new(new InMemorySource(Image(1, [1, 2, 3], blockSize: 1)), 0, false);

        reader.EffectiveBlockSize.Should().Be(1);
        reader.OffsetOf(9).Should().Be(9);
        reader.Read(1, "a field").Payload.Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(17)]
    public void Read_FetchesTheEntryAtTheBlockAsked(int block)
    {
        byte[] payload = Encoding.ASCII.GetBytes($"entry at block {block}");
        MemoReader reader = new(new InMemorySource(Image(block, payload)), 512, false);

        reader.Read(block, "a field").Payload.Should().Equal(payload);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(505)]
    [InlineData(1200)]
    [InlineData(2000)]
    public void Read_OfAPayloadSpanningBlocks_ReadsItWhole(int length)
    {
        // Blocks address an entry; they do not chop it up. 505 crosses one boundary, which is the
        // only straddle the corpus has; the larger two cross three and five and exist only here.
        byte[] payload = new byte[length];
        for (int i = 0; i < length; i++)
            payload[i] = (byte)(i % 251);

        MemoReader reader = new(new InMemorySource(Image(1, payload)), 512, false);

        reader.Read(1, "a field").Payload.Should().Equal(payload);
    }

    [Fact]
    public void Read_OfAnEmptyEntry_IsAnEmptyPayloadRatherThanAFailure()
    {
        MemoReader reader = new(new InMemorySource(Image(1, [])), 512, false);

        reader.Read(1, "a field").Payload.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MemoType.Picture)]
    [InlineData(MemoType.ObjectLinking)]
    [InlineData((MemoType)77)]
    public void Read_PreservesATypeItDoesNotProduce(MemoType type)
    {
        // The reference echoes the type back without validating it, so an unknown one is reported
        // rather than refused. Ungated: every corpus entry is text.
        MemoReader reader = new(new InMemorySource(Image(1, [9, 9], type: type)), 512, false);

        MemoEntry entry = reader.Read(1, "a field");

        entry.Type.Should().Be(type);
        entry.Payload.Should().Equal(9, 9);
    }

    [Fact]
    public void Read_OfACompressedEntry_IsRefusedAndSaysWhy()
    {
        MemoReader reader = new(
            new InMemorySource(Image(1, [1, 2, 3], type: MemoType.Compressed)), 512, false);

        Action act = () => reader.Read(1, "Field 'NOTES' of record 4");

        act.Should().Throw<CodeBaseException>()
           .WithMessage("*compressed*").And.Message.Should().Contain("Field 'NOTES' of record 4");
    }

    [Fact]
    public void Read_OfACompressedEntry_SaysWhetherTheTableExpectedOne()
    {
        // The flag is already parsed at open, so meeting a compressed entry can be explained rather
        // than merely refused.
        byte[] image = Image(1, [1, 2, 3], type: MemoType.Compressed);

        new MemoReader(new InMemorySource(image), 512, true)
            .Invoking(r => r.Read(1, "a field")).Should().Throw<CodeBaseException>()
            .WithMessage("*created with compressed memos enabled*");

        new MemoReader(new InMemorySource(image), 512, false)
            .Invoking(r => r.Read(1, "a field")).Should().Throw<CodeBaseException>()
            .WithMessage("*does not declare compressed memos*");
    }

    [Fact]
    public void Read_OfABlockPastTheEndOfTheFile_IsRefused()
    {
        MemoReader reader = new(new InMemorySource(Image(1, [1, 2, 3])), 512, false);

        Action act = () => reader.Read(99, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*memo block 99*");
    }

    [Fact]
    public void Read_OfAPayloadRunningPastTheEndOfTheFile_IsRefused()
    {
        // A truncated memo file. Returning what did arrive would produce a short string that looks
        // exactly like a short memo.
        byte[] image = Image(1, new byte[1000]);
        MemoReader reader = new(new InMemorySource(image[..700]), 512, false);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*shorter than its entries claim*");
    }

    [Fact]
    public void Read_OfAnAbsurdLength_IsRefused()
    {
        byte[] image = new byte[1024];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(512), (uint)MemoType.Text);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(516), 0x7FFFFFF1);

        MemoReader reader = new(new InMemorySource(image), 512, false);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>();
    }

    [Fact]
    public void Read_OfAShortReadingFile_IsADataError()
    {
        // The shared reader now takes its error code from the caller so that an index can say
        // ErrorCode.Index. A memo keeps saying Data, deliberately: there is no memo-specific code,
        // and a memo file is part of the data side of the table.
        MemoReader reader = new(
            new FaultySource(4096, FaultySource.Fault.ShortRead), 512, false);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Read_OfAFileThatCannotBeRead_LetsTheFailureThrough()
    {
        MemoReader reader = new(
            new FaultySource(4096, FaultySource.Fault.Throw), 512, false);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<IOException>();
    }

    /// <summary>
    /// Builds a memo file holding one entry at the given block.
    /// </summary>
    private static byte[] Image(
        int block, byte[] payload, MemoType type = MemoType.Text, int blockSize = 512)
    {
        int start = block * blockSize;
        int length = start + MemoBlockHeader.Size + payload.Length;

        // Round up to a whole block, as the reference implementation does after a write.
        if (length % blockSize != 0)
            length += blockSize - (length % blockSize);

        byte[] image = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(start), (uint)type);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(start + 4), (uint)payload.Length);
        payload.CopyTo(image.AsSpan(start + MemoBlockHeader.Size));

        return image;
    }
}
