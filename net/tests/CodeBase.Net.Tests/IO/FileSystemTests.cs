using AwesomeAssertions;
using CodeBase.Net.IO;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// What the real filesystem boundary promises, chiefly about finding a companion file.
///
/// These need a disk, because the thing under test is the part that talks to one. The corpus cannot
/// stand in: its memo files are already named exactly as the obvious path predicts, so opening a
/// corpus table never exercises the fallback that exists for the case where they are not.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FileSystemTests : IDisposable
{
    private readonly string directory;

    public FileSystemTests()
    {
        directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Resolve_FindsACompanionNamedExactlyAsExpected()
    {
        Write("TABLE.DBF");
        Write("TABLE.fpt");

        FileSystem.Instance.Resolve(Path.Combine(directory, "TABLE.DBF"), ".fpt")
            .Should().Be(Path.Combine(directory, "TABLE.fpt"));
    }

    [Fact]
    public void Resolve_FindsACompanionWhoseExtensionDiffersOnlyInCase()
    {
        // The case this method exists for. CodeBase writes a lower-case extension beside an
        // upper-case name, and a file renamed by hand can end up either way round.
        Write("TABLE.DBF");
        Write("TABLE.FPT");

        FileSystem.Instance.Resolve(Path.Combine(directory, "TABLE.DBF"), ".fpt")
            .Should().Be(Path.Combine(directory, "TABLE.FPT"));
    }

    [Fact]
    public void Resolve_PrefersTheExactNameWhenBothSpellingsExist()
    {
        if (!IsCaseSensitive())
            Assert.Skip("This filesystem cannot hold two names differing only in case.");

        Write("TABLE.DBF");
        Write("TABLE.FPT");
        Write("TABLE.fpt");

        FileSystem.Instance.Resolve(Path.Combine(directory, "TABLE.DBF"), ".fpt")
            .Should().Be(Path.Combine(directory, "TABLE.fpt"));
    }

    [Fact]
    public void Resolve_ReportsNothingWhenThereIsNoCompanion()
    {
        Write("TABLE.DBF");

        FileSystem.Instance.Resolve(Path.Combine(directory, "TABLE.DBF"), ".fpt").Should().BeNull();
    }

    [Fact]
    public void Open_AMissingFile_RaisesTheRuntimeFailureUnwrapped()
    {
        // Deliberately not wrapped: a caller already knows how to handle a missing file, and hiding
        // it inside a library-specific error would lose that.
        Action act = () => FileSystem.Instance.Open(Path.Combine(directory, "ABSENT.DBF"));

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_ADirectory_RaisesTheRuntimeFailureRatherThanFailingLater()
    {
        Directory.CreateDirectory(Path.Combine(directory, "TABLE.DBF"));

        Action act = () => FileSystem.Instance.Open(Path.Combine(directory, "TABLE.DBF"));

        act.Should().Throw<UnauthorizedAccessException>();
    }

    private void Write(string name) => File.WriteAllBytes(Path.Combine(directory, name), [1, 2, 3]);

    /// <summary>
    /// Decides whether this filesystem distinguishes names by case, which not all do.
    /// </summary>
    private bool IsCaseSensitive()
    {
        string probe = Path.Combine(directory, "CaseProbe.tmp");
        File.WriteAllBytes(probe, []);
        bool sensitive = !File.Exists(Path.Combine(directory, "caseprobe.tmp"));
        File.Delete(probe);
        return sensitive;
    }
}
