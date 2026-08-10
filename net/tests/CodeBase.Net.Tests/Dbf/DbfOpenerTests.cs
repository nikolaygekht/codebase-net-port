using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What opening a table promises: the sequence it reads in, and what it refuses.
///
/// The bytes are real corpus tables held in memory, so these tests exercise the open path without a
/// disk. The mocked ones are the handful that need behaviour a file cannot be made to produce.
/// </summary>
[Trait("Layer", "Component")]
public sealed class DbfOpenerTests
{
    private const string TablePath = "/data/VFPMEMO.DBF";
    private const string MemoPath = "/data/VFPMEMO.fpt";

    [Fact]
    public void Open_ReadsTheTableAndItsMemoFile()
    {
        FakeFileSystem files = CorpusFileSystem();

        OpenedTable opened = Open(files, TablePath);

        opened.Header.RecordCount.Should().Be(32);
        opened.Fields.Fields.Should().HaveCount(6);
        opened.Memo.Should().NotBeNull();
        opened.MemoHeader!.Value.BlockSize.Should().Be(512);
        files.Opened.Should().Equal(TablePath, MemoPath);
    }

    [Fact]
    public void Open_ATableDeclaringNoMemo_NeverLooksForOne()
    {
        // A memo file sitting beside a table that does not declare one is not its memo file.
        FakeFileSystem files = new(
            ("/data/VFPTYPE.DBF", Corpus.ReadAllBytes("VFPTYPE.DBF")),
            ("/data/VFPTYPE.fpt", Corpus.ReadAllBytes("VFPMEMO.fpt")));

        OpenedTable opened = Open(files, "/data/VFPTYPE.DBF");

        opened.Memo.Should().BeNull();
        files.CompanionLookups.Should().BeEmpty();
        files.Opened.Should().ContainSingle();
    }

    [Fact]
    public void Open_FindsAMemoFileWhoseExtensionDiffersOnlyInCase()
    {
        // What CodeBase writes: a lower-case extension beside an upper-case name. The reverse
        // happens too once files have been renamed by hand.
        FakeFileSystem files = new(
            (TablePath, Corpus.ReadAllBytes("VFPMEMO.DBF")),
            ("/data/VFPMEMO.FPT", Corpus.ReadAllBytes("VFPMEMO.fpt")));

        Open(files, TablePath).Memo.Should().NotBeNull();
        files.Opened.Should().Contain("/data/VFPMEMO.FPT");
    }

    [Fact]
    public void Open_ATableWhoseDeclaredMemoFileIsMissing_IsRejectedAsCorruptData()
    {
        FakeFileSystem files = new((TablePath, Corpus.ReadAllBytes("VFPMEMO.DBF")));

        Rejection(files, TablePath).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Open_AMemoFileTooShortForItsHeader_IsRejectedAsCorruptData()
    {
        FakeFileSystem files = new(
            (TablePath, Corpus.ReadAllBytes("VFPMEMO.DBF")),
            (MemoPath, new byte[4]));

        Rejection(files, TablePath).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Open_AHeaderClaimingToBeLongerThanTheFile_IsRejectedAsCorruptData()
    {
        byte[] table = Corpus.ReadAllBytes("VFPTYPE.DBF");
        table[8] = 0xFF;
        table[9] = 0xFF;

        Rejection(new FakeFileSystem((TablePath, table)), TablePath).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Open_AHeaderClaimingMoreRecordsThanTheFileHolds_IsRejectedAsCorruptData()
    {
        byte[] table = Corpus.ReadAllBytes("VFPTYPE.DBF");
        table[4] = 33;      // the file holds 32

        Rejection(new FakeFileSystem((TablePath, table)), TablePath).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Open_ARecordCountThatWouldOverflowThirtyTwoBitArithmetic_IsRejectedAsCorruptData()
    {
        // Counted in 64-bit, so a product that would wrap cannot come back as a plausible number.
        byte[] table = Corpus.ReadAllBytes("VFPTYPE.DBF");
        table[4] = 0xFF;
        table[5] = 0xFF;
        table[6] = 0xFF;
        table[7] = 0x7F;

        Rejection(new FakeFileSystem((TablePath, table)), TablePath).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Open_ATruncatedFile_IsRejectedAsCorruptData()
    {
        byte[] table = Corpus.ReadAllBytes("VFPTYPE.DBF")[..100];

        Rejection(new FakeFileSystem((TablePath, table)), TablePath).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Open_AFailedOpen_LeavesNoFileHandleBehind()
    {
        // The memo file is opened after the table, so a failure there is the case that could leak.
        FakeFileSystem files = new(
            (TablePath, Corpus.ReadAllBytes("VFPMEMO.DBF")),
            (MemoPath, new byte[4]));

        Rejection(files, TablePath);

        files.Sources.Should().OnlyContain(s => s.IsDisposed);
    }

    [Fact]
    public void Open_ASourceThatReturnsFewerBytesThanAskedFor_IsRejectedAsCorruptData()
    {
        // A file cannot be made to do this on demand, but a network filesystem can. Padding the
        // gap with zeros would turn a short read into a plausible-looking header.
        FaultySource source = new(4096, FaultySource.Fault.ShortRead);

        Action act = () => new DbfOpener(new StubFactory(source), new NoCompanions()).Open(TablePath);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
        source.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Open_ASourceThatThrowsWhileReading_ClosesTheFileBeforeTheErrorEscapes()
    {
        FaultySource source = new(4096, FaultySource.Fault.Throw);

        Action act = () => new DbfOpener(new StubFactory(source), new NoCompanions()).Open(TablePath);

        act.Should().Throw<IOException>();
        source.IsDisposed.Should().BeTrue();
    }

    private static FakeFileSystem CorpusFileSystem() => new(
        (TablePath, Corpus.ReadAllBytes("VFPMEMO.DBF")),
        (MemoPath, Corpus.ReadAllBytes("VFPMEMO.fpt")));

    private static OpenedTable Open(FakeFileSystem files, string path) =>
        new DbfOpener(files, files).Open(path);

    private static CodeBaseException Rejection(FakeFileSystem files, string path)
    {
        Action act = () => Open(files, path);
        return act.Should().Throw<CodeBaseException>().Which;
    }
}
