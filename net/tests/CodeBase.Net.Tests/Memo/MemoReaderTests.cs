using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Memo;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Memo;

/// <summary>
/// Finding an entry, reading it whole, and refusing one that cannot be believed.
///
/// The corpus proves the layout against real files, all of which use a 512-byte block, and proves the
/// reference's compressed entries. What is left for here is the geometry the corpus does not have —
/// other block sizes, including zero, and payloads spanning three blocks — and every way a file can
/// be wrong, a damaged compressed entry included.
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
        MemoReader reader = new(new InMemorySource([]), blockSize: 512);

        MemoEntry entry = reader.Read(0, "a field");

        entry.Payload.Should().BeEmpty();
        entry.Type.Should().Be(MemoType.Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Read_OfANegativeBlock_IsAnAbsentMemo(int block)
    {
        MemoReader reader = new(new InMemorySource([]), 512);

        reader.Read(block, "a field").Payload.Should().BeEmpty();
    }

    [Fact]
    public void OffsetOf_MultipliesTheBlockNumberByTheBlockSize()
    {
        MemoReader reader = new(new InMemorySource([]), 512);

        reader.OffsetOf(1).Should().Be(512);
        reader.OffsetOf(27).Should().Be(13824);
    }

    [Fact]
    public void ABlockSizeOfZero_AddressesByTheByte()
    {
        // Legal, and it means one rather than an error. A reader that divided by it would fault on a
        // file the reference implementation reads happily. Ungated: every corpus file uses 512.
        MemoReader reader = new(new InMemorySource(Image(1, [1, 2, 3], blockSize: 1)), 0);

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
        MemoReader reader = new(new InMemorySource(Image(block, payload)), 512);

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

        MemoReader reader = new(new InMemorySource(Image(1, payload)), 512);

        reader.Read(1, "a field").Payload.Should().Equal(payload);
    }

    [Fact]
    public void Read_OfAnEmptyEntry_IsAnEmptyPayloadRatherThanAFailure()
    {
        MemoReader reader = new(new InMemorySource(Image(1, [])), 512);

        reader.Read(1, "a field").Payload.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MemoType.Picture)]
    [InlineData(MemoType.ObjectLinking)]
    [InlineData((MemoType)77)]
    public void Read_PreservesATypeItDoesNotProduce(MemoType type)
    {
        // The reference echoes the type back without validating it, so an unknown one is reported
        // rather than refused. Ungated: the corpus holds only types 1 and 3.
        MemoReader reader = new(new InMemorySource(Image(1, [9, 9], type: type)), 512);

        MemoEntry entry = reader.Read(1, "a field");

        entry.Type.Should().Be(type);
        entry.Payload.Should().Equal(9, 9);
    }

    [Fact]
    public void Read_OfACompressedEntry_ReturnsItDecompressedAndKeepsItsType()
    {
        // The corpus proves the reference's own entries; this only pins that the stream the payload
        // holds comes back whole, whichever deflate produced it.
        byte[] content = Content(2000);
        MemoReader reader = new(new InMemorySource(CompressedImage(Compress(content))), 512);

        MemoEntry entry = reader.Read(1, "a field");

        entry.Type.Should().Be(MemoType.Compressed);
        entry.Payload.Should().Equal(content);
    }

    [Fact]
    public void Read_OfACompressedEntryTooShortForALengthAndAStream_IsRefused()
    {
        MemoReader reader = new(new InMemorySource(CompressedImage([1, 2, 3])), 512);

        Action act = () => reader.Read(1, "Field 'NOTES' of record 4");

        act.Should().Throw<CodeBaseException>()
           .WithMessage("*cannot hold a length*").And.Message.Should().Contain("Field 'NOTES' of record 4");
    }

    [Fact]
    public void Read_OfACompressedEntryDeclaringAnAbsurdLength_IsRefusedBeforeAllocating()
    {
        // The reference's own guard (m4file.c:229-238), applied to the decompressed length the prefix
        // declares rather than to the stored one.
        byte[] stored = Compress(Content(100));
        BinaryPrimitives.WriteUInt32LittleEndian(stored, 0x7FFFFFF1);
        MemoReader reader = new(new InMemorySource(CompressedImage(stored)), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*declaring 2147483633 bytes*");
    }

    [Fact]
    public void Read_OfACompressedEntryThatIsNotAZlibStream_IsRefused()
    {
        // A QuickLZ build of CodeBase writes the same type and the same prefix; the zlib header is the
        // only thing that tells them apart. Here its check value is broken.
        byte[] stored = Compress(Content(100));
        stored[5] ^= 0x01;
        MemoReader reader = new(new InMemorySource(CompressedImage(stored)), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*not a zlib stream*");
    }

    [Fact]
    public void Read_OfACompressedEntryShorterThanItDeclares_IsRefused()
    {
        byte[] stored = Compress(Content(100));
        BinaryPrimitives.WriteUInt32LittleEndian(stored, 101);
        MemoReader reader = new(new InMemorySource(CompressedImage(stored)), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*ends after 100*");
    }

    [Fact]
    public void Read_OfACompressedEntryLongerThanItDeclares_IsRefused()
    {
        // The reference would hand back the declared length and drop the rest without a word.
        byte[] stored = Compress(Content(100));
        BinaryPrimitives.WriteUInt32LittleEndian(stored, 99);
        MemoReader reader = new(new InMemorySource(CompressedImage(stored)), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*holds more*");
    }

    [Fact]
    public void Read_OfACompressedEntryWhoseStreamIsCutShort_IsRefused()
    {
        byte[] stored = Compress(Content(2000));
        MemoReader reader = new(new InMemorySource(CompressedImage(stored[..^10])), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*compressed memo at block 1*");
    }

    [Fact]
    public void Read_OfACompressedEntryWhoseChecksumIsWrong_IsRefused()
    {
        // The Adler-32 trailer is all that catches damage which still inflates to the right length.
        byte[] stored = Compress(Content(2000));
        stored[^1] ^= 0x01;
        MemoReader reader = new(new InMemorySource(CompressedImage(stored)), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*damaged*");
    }

    [Fact]
    public void Read_OfABlockPastTheEndOfTheFile_IsRefused()
    {
        MemoReader reader = new(new InMemorySource(Image(1, [1, 2, 3])), 512);

        Action act = () => reader.Read(99, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*memo block 99*");
    }

    [Fact]
    public void Read_OfAPayloadRunningPastTheEndOfTheFile_IsRefused()
    {
        // A truncated memo file. Returning what did arrive would produce a short string that looks
        // exactly like a short memo.
        byte[] image = Image(1, new byte[1000]);
        MemoReader reader = new(new InMemorySource(image[..700]), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().WithMessage("*shorter than its entries claim*");
    }

    [Fact]
    public void Read_OfAnAbsurdLength_IsRefused()
    {
        byte[] image = new byte[1024];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(512), (uint)MemoType.Text);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(516), 0x7FFFFFF1);

        MemoReader reader = new(new InMemorySource(image), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>();
    }

    [Fact]
    public void Read_OfAShortReadingFile_IsADataError()
    {
        // The shared reader reports a short read and each caller classifies it, so an index can say
        // ErrorCode.Index. A memo keeps saying Data, deliberately: there is no memo-specific code,
        // and a memo file is part of the data side of the table.
        MemoReader reader = new(
            new FaultySource(4096, FaultySource.Fault.ShortRead), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Read_OfAFileThatCannotBeRead_LetsTheFailureThrough()
    {
        MemoReader reader = new(
            new FaultySource(4096, FaultySource.Fault.Throw), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<IOException>();
    }

    /// <summary>
    /// Builds a memo file holding one entry at the given block.
    /// </summary>
    [Trait("Layer", "Performance")]
    [Fact]
    public void Read_OfALargeClaimInASmallFile_IsRefusedWithoutAllocatingIt()
    {
        // The reason the payload's length is checked against the file at all, rather than left to the
        // read the way the header is (ADR-35). A length that comes out of the file sizes an
        // allocation: SourceReader allocates before it reads, so a corrupt header claiming a quarter
        // of a gigabyte would have the buffer taken and thrown away. Below the threshold that is
        // nothing and the read refuses it; above it, this must not happen at all.
        //
        // The assertion is on bytes allocated, not on the message, because both routes now refuse
        // with the same sentence -- and the whole point of the check is what it avoids doing, which
        // no message can show.
        const uint Claim = 256 * 1024 * 1024;

        byte[] image = new byte[1024];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(512), (uint)MemoType.Text);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(516), Claim);

        MemoReader reader = new(new InMemorySource(image), 512);

        Action act = () => reader.Read(1, "a field");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);

        // Measured on a second call, so the exception machinery's own one-off costs are not charged to
        // the refusal. A generous ceiling on purpose: it has to separate "a few kilobytes of message"
        // from "a quarter of a gigabyte", and nothing in between is interesting.
        long before = GC.GetAllocatedBytesForCurrentThread();
        act.Should().Throw<CodeBaseException>();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.Should().BeLessThan(
            1024 * 1024,
            "refusing a {0}-byte claim must not begin by allocating {0} bytes",
            Claim);
    }

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

    private static byte[] CompressedImage(byte[] stored) => Image(1, stored, MemoType.Compressed);

    /// <summary>
    /// Builds a compressed entry's payload: the content's length, little-endian, then a zlib stream.
    /// </summary>
    private static byte[] Compress(byte[] content)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, content.Length);

        using MemoryStream stored = new();
        stored.Write(length);
        using (ZLibStream deflater = new(stored, CompressionLevel.Optimal, leaveOpen: true))
            deflater.Write(content);
        return stored.ToArray();
    }

    private static byte[] Content(int length) =>
        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("the quick brown fox ", length / 20 + 1)))[..length];
}
