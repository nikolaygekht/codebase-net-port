using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Guards the corpus itself, so that a suite driven by it cannot pass while covering nothing.
///
/// Every golden test enumerates the corpus. If discovery silently returned an empty set, through a
/// wrong output path, a rename, or a publish that dropped the files, a data-driven suite would
/// report success having asserted nothing at all. These tests fail loudly in that case, and they
/// run before any decoding exists to be tested.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CorpusLayoutTests
{
    /// <summary>The cases the corpus is documented to hold (net/corpus/README.md).</summary>
    private static readonly string[] ExpectedTables =
    [
        "CDXBASE", "CDXCOLL", "CDXDEEP", "CDXTIME", "CP1251", "CP936",
        "DB3TYPE", "F2XMEMO", "IDXONE", "VFPMEMO", "VFPNULL", "VFPTYPE",
    ];

    /// <summary>The index files the corpus is documented to hold, with the table each belongs to.</summary>
    public static TheoryData<string, string> IndexFiles() => new()
    {
        { "CDXBASE", "CDXBASE.cdx" },
        { "CDXCOLL", "CDXCOLL.cdx" },
        { "CDXDEEP", "CDXDEEP.cdx" },
        { "CDXTIME", "CDXTIME.cdx" },
        { "IDXONE", "IDXONE.cdx" },
        { "IDXONE", "IDXONE.IDX" },
    };

    [Fact]
    public void Corpus_HoldsExactlyTheDocumentedTables()
    {
        Corpus.TableNames.Should().Equal(ExpectedTables);
    }

    [Fact]
    public void Corpus_PairsEveryTableWithADump()
    {
        foreach (string table in Corpus.TableNames)
            File.Exists(Path.Combine(Corpus.Root, table + ".dump.txt")).Should().BeTrue($"{table} needs its dump");
    }

    [Theory]
    [InlineData("CP1251")]
    [InlineData("CP936")]
    [InlineData("F2XMEMO")]
    [InlineData("VFPMEMO")]
    [InlineData("VFPNULL")]
    public void Corpus_PairsEveryMemoTableWithAnFpt(string table)
    {
        // Lower-case .fpt beside an upper-case .DBF is what CodeBase writes (d4defs.h:2589-2598).
        // Asserted here because it is the reason companion resolution must be case-insensitive.
        File.Exists(Path.Combine(Corpus.Root, table + ".fpt")).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(IndexFiles))]
    public void Corpus_PairsEveryIndexFileWithADump(string table, string indexFile)
    {
        // A production index is lower-case ".cdx" beside an upper-case ".DBF", the same asymmetry
        // as ".fpt" (d4defs.h:2589-2598). The derived single-tag file is ".IDX" (ADR-25).
        File.Exists(Path.Combine(Corpus.Root, indexFile)).Should().BeTrue($"{indexFile} is a documented case");

        string dump = Path.ChangeExtension(indexFile, null) + "." +
                      Path.GetExtension(indexFile).TrimStart('.').ToLowerInvariant() + ".dump.txt";
        File.Exists(Path.Combine(Corpus.Root, dump)).Should().BeTrue($"{indexFile} needs its dump");

        File.Exists(Path.Combine(Corpus.Root, table + ".DBF")).Should().BeTrue($"{indexFile} needs its table");
    }

    [Fact]
    public void Corpus_FileIsReadable()
    {
        Corpus.ReadAllBytes("VFPNULL.DBF").Should().HaveCountGreaterThan(32);
    }
}
