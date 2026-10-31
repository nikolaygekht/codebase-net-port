using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// A tag whose expression resolves to a key length other than its header's is refused, as the reference refuses it.
///
/// The reference resolves every tag's expression at open and refuses the index when the length it
/// computes differs from the header's (E84901, i4init.c:485-490). The check has two ways to be wrong,
/// and each has its gate here: refusing a tag the reference opens, which every corpus tag would
/// catch, and opening one it refuses, which TAGBAD catches — a file the reference wrote itself, with
/// i4tagAdd, and then could not open.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class KeyLengthGoldenTests
{
    /// <summary>Every table whose index the reference opened and dumped.</summary>
    public static TheoryData<string> IndexedTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames.Where(t => File.Exists(Path.Combine(Corpus.Root, t + ".cdx.dump.txt"))))
            data.Add(table);
        return data;
    }

    [Fact]
    public void TheGateCoversEveryIndexedTable()
    {
        IndexedTables().Should().HaveCount(9);
    }

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void EveryTagTheReferenceOpenedResolvesToItsHeadersKeyLength(string tableName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));
        List<string> refused = [];

        foreach (Tag tag in table.Tags)
        {
            try
            {
                SeekConverter.For(
                    tag.Inner.Header,
                    [.. table.Fields],
                    table.CodePageNumber ?? CodePageMap.UnmarkedCodePage,
                    ExpressionOptions.Invariant,
                    tableName);
            }
            catch (CodeBaseException error)
            {
                refused.Add($"{tag.Name}: {error.Message}");
            }
        }

        string.Join(Environment.NewLine, refused).Should().BeEmpty();
    }

    [Fact]
    public void TheTagTheReferenceRefusesIsRefusedHereWithTheSameCode()
    {
        // The open dump records what the reference answered for TAGBAD.cdx. Here the table opens and
        // the refusal comes when the tag is selected: one tag at a time, as every refusal of a tag
        // is in this library, where the reference fails the whole index.
        ErrorCode expected = (ErrorCode)OpenDumpErrorCode("TAGBAD.open.dump.txt");
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("TAGBAD.DBF"));

        CodeBaseException refusal = table.Invoking(t => t.SelectTag(t.Tags["G_DESC"]))
            .Should().Throw<CodeBaseException>().Which;

        refusal.Code.Should().Be(expected);
        refusal.Message.Should().Contain("DESCEND(K_TEXT)");
    }

    [Fact]
    public void TheRestOfThatIndexStillServes()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("TAGBAD.DBF"));

        table.SelectTag(table.Tags["T_TEXT"]);
        table.Seek("zebra").Should().Be(GoResult.Ok);

        // A query leaves the refused tag out rather than failing.
        table.SelectTag(null);
        table.Query("K_TEXT = \"cafe\"").Count.Should().Be(1);
    }

    private static int OpenDumpErrorCode(string fileName)
    {
        string line = File.ReadLines(Path.Combine(Corpus.Root, fileName)).Single(l => l.StartsWith("open ", StringComparison.Ordinal));
        string code = line[(line.IndexOf("errorCode=", StringComparison.Ordinal) + "errorCode=".Length)..];
        return int.Parse(code, System.Globalization.CultureInfo.InvariantCulture);
    }
}
