using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Depth 4 of the query gate: the records a query returns, and how many, match the reference's.
///
/// This is the whole path end to end -- decompose, seek, combine, then check the terms no tag
/// answered -- and the two orders a result can come out in are both compared, because which one a
/// caller gets is user-visible and they are genuinely different.
///
/// It is also where the port's one deliberate divergence in the residual filter is settled. The
/// reference decides whether a term still needs checking by looking at two fixed positions beside it,
/// and those are its operands only when the right one occupies a single node; where they are not, it
/// re-checks a term its own bitmap already answered. This port asks the plan instead, so it checks
/// strictly fewer terms -- and must still return the same records.
///
/// One filter is measured against the reference's scan rather than its optimized answer, and the dump
/// says which: where the reference's two readings of a filter disagree it has demonstrated its own
/// range-inverting defect, and this port implements what was meant.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class QueryResultGoldenTests
{
    public static TheoryData<string> Tables => ["CDXEXPR", "CDXBASE", "CDXDEEP", "QRYGATE"];

    /// <summary>
    /// The gate, in record-number order: no tag selected, so the query walks its own record set and
    /// passes over runs of records it does not hold without reading them.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void AQueryReturnsTheRecordsTheReferenceReturned(string table)
    {
        using var engine = new CodeBaseEngine();
        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));
        CorpusQueryDump dump = CorpusQueryDump.Load(table);

        foreach (DumpQueryCase filter in dump.Cases)
        {
            opened.SelectTag(null);
            Walk(opened.Query(filter.Filter)).Should().Equal(
                filter.Expected, "the records {0} returns", filter.Filter);
        }
    }

    /// <summary>
    /// And in tag order, which is a different sequence of the same records -- the tag is stepped and
    /// the record set says which of its entries to keep.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void AQueryUnderASelectedTagReturnsTheRecordsInTheTagsOrder(string table)
    {
        using var engine = new CodeBaseEngine();
        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));
        CorpusQueryDump dump = CorpusQueryDump.Load(table);
        int seen = 0;

        foreach (DumpQueryCase filter in dump.Cases.Where(c => c.OrderingTag is not null && c.Agrees))
        {
            seen++;
            opened.SelectTag(opened.Tags[filter.OrderingTag!]);
            Walk(opened.Query(filter.Filter)).Should().Equal(
                filter.ByTag, "the records {0} returns in {1}'s order", filter.Filter, filter.OrderingTag);
        }

        seen.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The count agrees, and for a fully mapped plan the reference produces it without reading a
    /// record at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void TheCountAgreesWithTheReference(string table)
    {
        using var engine = new CodeBaseEngine();
        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));
        CorpusQueryDump dump = CorpusQueryDump.Load(table);

        opened.SelectTag(null);

        foreach (DumpQueryCase filter in dump.Cases.Where(c => c.Agrees))
            opened.Query(filter.Filter).Count.Should().Be(filter.Count, "the count for {0}", filter.Filter);
    }

    /// <summary>
    /// Walking backwards from the end returns the same records reversed, which is what a result set
    /// being a set rather than a stream means.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void WalkingBackwardsReturnsTheSameRecordsReversed(string table)
    {
        using var engine = new CodeBaseEngine();
        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));
        CorpusQueryDump dump = CorpusQueryDump.Load(table);

        opened.SelectTag(null);

        foreach (DumpQueryCase filter in dump.Cases)
        {
            Query query = opened.Query(filter.Filter);
            List<int> backwards = [];

            for (GoResult go = query.Bottom(); go == GoResult.Ok; go = query.Previous())
                backwards.Add(query.RecordNumber);

            backwards.Should().Equal(
                filter.Expected.Reverse(), "walking {0} backwards", filter.Filter);
        }
    }

    /// <summary>
    /// A filter that is not a logical expression is refused rather than answered, and refused when it
    /// is used rather than when it is written -- which is where the reference reports it too.
    /// </summary>
    [Fact]
    public void AFilterThatIsNotLogicalIsRefused()
    {
        using var engine = new CodeBaseEngine();
        using Table opened = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        Query query = opened.Query("K_NUM");

        Assert.Throws<CodeBaseException>(() => query.Top())
            .Code.Should().Be(ErrorCode.Relate);
    }

    /// <summary>
    /// A plan that answers the whole filter leaves the residual filter nothing to do, which is what
    /// makes a count answerable from the record set without reading anything.
    /// </summary>
    /// <value>
    /// Worth asserting directly, because checking a term the plan already answered is invisible
    /// everywhere else: it can only agree with the bitmap, so no record set moves and no count
    /// changes. The saving is real and nothing but this can see it.
    /// </value>
    [Theory]
    [MemberData(nameof(Tables))]
    public void AFullyMappedPlanLeavesTheResidualFilterNothingToDo(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);
        int seen = 0;

        foreach (DumpQueryCase filter in harness.Dump.Comparable.Where(c => c.FullyMapped))
        {
            seen++;
            harness.Residual(filter.Filter).HasWork.Should().BeFalse(
                "{0} is answered entirely by its tags", filter.Filter);
        }

        seen.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// A field may be qualified by the table's own name, and by no other.
    /// </summary>
    /// <value>
    /// The reference reads the aliases a name may carry while it is still scanning, so a table always
    /// knows its own; this port has to be told, and until the query path made the omission visible it
    /// was not -- so [c]CDXEXPR-&gt;K_NUM[/c] came back as an unknown field in filters and in tag
    /// expressions alike. A foreign alias still fails, with the code the reference reports for it,
    /// which is the corpus's one recorded alias case.
    /// </value>
    [Fact]
    public void AFieldMayBeQualifiedByTheTablesOwnNameAndNoOther()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.Plan("CDXEXPR->K_NUM > 0").LeafCount.Should().Be(1, "the table knows its own name");
        harness.OnlyLeaf("CDXEXPR->K_NUM > 0").Constraint!.ToString()
            .Should().Be(harness.OnlyLeaf("K_NUM > 0").Constraint!.ToString());

        Assert.Throws<CodeBaseException>(() => harness.Plan("OTHER->K_NUM > 0"))
            .Code.Should().Be(ErrorCode.FieldName);
    }

    /// <summary>
    /// And a query written that way returns the same records as one written without it.
    /// </summary>
    [Fact]
    public void AQualifiedFilterReturnsWhatTheUnqualifiedOneDoes()
    {
        using var engine = new CodeBaseEngine();
        using Table opened = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));
        opened.SelectTag(null);

        Walk(opened.Query("CDXEXPR->K_NUM > 0")).Should().Equal(Walk(opened.Query("K_NUM > 0")));
    }

    private static List<int> Walk(Query query)
    {
        List<int> records = [];

        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
            records.Add(query.RecordNumber);

        return records;
    }
}
