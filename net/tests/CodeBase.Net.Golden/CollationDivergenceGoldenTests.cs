using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Where the reference answers a query with a collated tag, and the port deliberately does not.
///
/// The reference refuses a collated tag only in a build that does not define
/// [c]S4USE_GENERAL_TAGS_IN_RELATE[/c], and every S4FOX build that is not S4TESTING defines it
/// (d4defs.h:669-673), so the refusal at r4log.c:114-120 is compiled out. The reference then seeks a
/// GENERAL tag, whose keys are ordered case- and accent-insensitively, for a filter it compares in byte
/// order, and returns the wrong records. The port refuses the tag (ADR-54, ADR-59) and answers what
/// the filter means.
///
/// So on these cases the reference's decisions are no expectation at all, and the other query gates
/// leave them out. What is checked here instead comes from the reference too: its own scan, taken with
/// its optimizer switched off, and the order its index stores the keys in.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CollationDivergenceGoldenTests
{
    public static TheoryData<string> Tables => ["CDXCOLL", "QRYGATE"];

    [Fact]
    public void TheReferenceAnswersWithACollatedTagAndContradictsItsOwnScan()
    {
        // The witness that the divergence is the reference's, not a misreading of it: on these cases
        // its optimized answer and its scan differ.
        DumpQueryCase[] divergent =
        [
            .. Tables.SelectMany(row =>
            {
                CorpusQueryDump dump = CorpusQueryDump.Load(row.Data);
                return dump.Cases.Where(dump.UsesCollatedTag);
            }),
        ];

        divergent.Should().HaveCountGreaterThanOrEqualTo(6);
        divergent.Should().Contain(c => !c.Agrees);
        divergent.Should().Contain(c => c.Filter == "K_TEXT > \"M\"" && !c.Agrees, "ADR-54's own case");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void ThePortTakesNoCollatedTag(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases.Where(harness.Dump.UsesCollatedTag))
        {
            harness.Match(filter.Filter)
                .Select(t => t?.Name)
                .Should().NotContain(name => name != null && harness.Dump.CollatedTags.Contains(name),
                    "the tags matched for {0}", filter.Filter);
        }
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void ThePortReturnsWhatTheReferenceScanReturns(string table)
    {
        using CodeBaseEngine engine = new();
        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));
        CorpusQueryDump dump = CorpusQueryDump.Load(table);
        int seen = 0;

        opened.SelectTag(null);
        foreach (DumpQueryCase filter in dump.Cases.Where(dump.UsesCollatedTag))
        {
            seen++;
            Walk(opened.Query(filter.Filter)).Should().Equal(
                filter.Scanned, "the records {0} means, by the reference's own scan", filter.Filter);
        }

        seen.Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void UnderASelectedCollatedTagThePortReturnsTheScanInTheTagsStoredOrder(string table)
    {
        // Ordering by a collated tag is not answering with it: the records are the scan's, and their
        // order is the order the reference stored that tag's keys in.
        using CodeBaseEngine engine = new();
        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));
        CorpusQueryDump dump = CorpusQueryDump.Load(table);
        CorpusIndexDump index = CorpusIndexDump.Load(table + ".cdx");

        foreach (DumpQueryCase filter in dump.Cases.Where(c => c.OrderingTag is not null && dump.UsesCollatedTag(c)))
        {
            HashSet<int> scanned = [.. filter.Scanned];
            List<int> stored =
            [
                .. index.RealTags.Single(t => t.Name.TrimEnd() == filter.OrderingTag)
                    .Keys.Select(k => (int)k.Record).Where(scanned.Contains),
            ];

            opened.SelectTag(opened.Tags[filter.OrderingTag!]);
            Walk(opened.Query(filter.Filter)).Should().Equal(
                stored, "the records {0} returns in {1}'s order", filter.Filter, filter.OrderingTag);
        }
    }

    private static List<int> Walk(Query query)
    {
        List<int> records = [];

        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
            records.Add(query.RecordNumber);

        return records;
    }
}
