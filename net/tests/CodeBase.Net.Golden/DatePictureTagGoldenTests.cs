using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// A tag keyed through the date picture is used only under a picture the caller chose, and then answers
/// as the reference stored it.
///
/// CDXEXPR's T_DTOC holds DTOC(K_DATE) under the reference's picture, MM/DD/YY. The index records the
/// key's length, eight, and not the picture, and the engine's default picture is eight characters too.
/// Before ADR-74 the tag was used under the default anyway, and a DTOC filter counted one set of records
/// through the tag and another record by record. The expectations are the reference's own keys, from the
/// index dump: a filter naming a stored key must find exactly the records stored under it.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class DatePictureTagGoldenTests
{
    private const string TagName = "T_DTOC";

    [Fact]
    public void UnderTheDefaultOptionsTheTagIsRefusedNamingWhatToSet()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        CodeBaseException refusal = table.Invoking(t => t.SelectTag(t.Tags[TagName]))
            .Should().Throw<CodeBaseException>().Which;

        refusal.Code.Should().Be(ErrorCode.NotSupported);
        refusal.Message.Should().Contain("DTOC()").And.Contain("ExpressionOptions");
        table.QueryTags().Select(t => t.Name.TrimEnd()).Should().NotContain(TagName);
    }

    [Fact]
    public void UnderTheDefaultOptionsADtocFilterCountsWhatTheRecordsSay()
    {
        // The tag is not used, so the count is the per-record check's, under the default picture.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        foreach (string stored in StoredKeys().Distinct())
        {
            string filter = Filter(stored);
            table.Query(filter).Count.Should().Be(PerRecord(table, filter), filter);
        }
    }

    [Fact]
    public void UnderTheReferencePictureADtocFilterFindsTheRecordsStoredUnderThatKey()
    {
        using CodeBaseEngine engine = new() { ExpressionOptions = ExpressionOptions.CodeBase };
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));
        table.QueryTags().Select(t => t.Name.TrimEnd()).Should().Contain(TagName);

        List<string> keys = StoredKeys();
        foreach (string stored in keys.Distinct())
        {
            string filter = Filter(stored);
            int expected = keys.Count(k => k == stored);

            table.Query(filter).Count.Should().Be(expected, filter);
            PerRecord(table, filter).Should().Be(expected, filter);
        }

        keys.Distinct().Count().Should().BeGreaterThan(5, "the gate needs several keys to count");
    }

    /// <summary>The keys the reference stored in T_DTOC, one per record, as text.</summary>
    private static List<string> StoredKeys() =>
    [
        .. CorpusIndexDump.Load("CDXEXPR.cdx").RealTags
            .Single(t => t.Name.TrimEnd() == TagName)
            .Keys.Select(k => Encoding.ASCII.GetString(k.Key)),
    ];

    private static string Filter(string key) => $"DTOC(K_DATE) = \"{key}\"";

    /// <summary>Counts the records the filter admits, evaluating it record by record.</summary>
    private static int PerRecord(Table table, string filter)
    {
        ExpressionEvaluator evaluator = new(table.CompileFilter(filter));
        int count = 0;

        for (int number = 1; number <= table.RecordCount; number++)
        {
            table.Go(number);
            if (evaluator.Evaluate(table.EvaluationContext()).Logical == true)
                count++;
        }

        return count;
    }
}
