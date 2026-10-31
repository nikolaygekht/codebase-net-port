using AwesomeAssertions;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Gate A: an optimized query returns exactly what reading every record and testing it returns.
///
/// This is the check that needs no reference at all, and it is the only one that can be applied to
/// the tags the reference refuses. Everything else in this suite compares against what the C library
/// answered, so it can say nothing about a query answered with an integer or a collated tag -- the
/// reference would not have used one, and there is no recorded answer to compare with. Here the
/// expectation is computed rather than stored: the same compiled filter, evaluated once per record.
///
/// It runs in both tag scopes, and the point of running it twice is that the scope must change which
/// tags a query is answered with and never which records it returns. A widened scope that returned
/// different records would mean the optimizer was answering a different question from the filter.
///
/// The filter population is the corpus's own, plus a few written here. Both are legitimate: a filter
/// list needs no generator run to be an expectation when the expectation is a scan.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class QueryEquivalenceGoldenTests
{
    /// <summary>
    /// Filters that no dump case covers, aimed at what widening the scope unlocks.
    /// </summary>
    /// <value>
    /// The reference refuses an integer, currency or datetime tag, so its dump records those filters
    /// as unoptimizable and there is nothing to compare a widened run against. A scan can still say
    /// whether the answer is right, which is what makes these worth writing.
    /// </value>
    /// <summary>
    /// Filters over the table whose tags are collated, beyond the four its query dump records.
    /// </summary>
    /// <value>
    /// CDXCOLL carries a collated tag and a machine one over the same field, and the collated one
    /// comes first. The port refuses the collated tag in both scopes (ADR-54), so the machine tag
    /// answers every term; the reference uses the collated one and gets these wrong (ADR-59), so its
    /// answers are no expectation here. A scan is, and needs no reference.
    /// </value>
    private static readonly string[] Collated =
    [
        "K_TEXT > \"M\"",
        "K_TEXT >= \"M\"",
        "K_TEXT < \"M\"",
        "K_TEXT = \"E\"",
        "K_TEXT <> \"E\"",
        "K_TEXT >= \"A\" .AND. K_TEXT <= \"Z\"",
        "K_TEXT > \"A\" .AND. K_TEXT < \"E\"",
        "K_TEXT = \"\u0153\"",
        "K_TEXT > \"\u00df\"",
    ];

    private static readonly string[] Extra =
    [
        "K_INT > 0",
        "K_INT > 0 .AND. K_INT < 100",
        "K_INT = 0",
        "K_INT <> 0",
        "K_CUR > 0",
        "K_CUR >= 0 .AND. K_CUR <= 100",
        "K_TS > K_TS",
        "K_NUM > 0 .AND. K_INT > 0",
        "K_INT > 0 .OR. K_NUM > 0",
    ];

    public static TheoryData<string, bool> Cases
    {
        get
        {
            TheoryData<string, bool> cases = [];

            foreach (string table in new[] { "CDXEXPR", "CDXBASE", "CDXDEEP", "CDXCOLL", "QRYGATE", "TAGCTX", "TAGGEN" })
            {
                cases.Add(table, false);
                cases.Add(table, true);
            }

            return cases;
        }
    }

    /// <summary>
    /// The gate, in record-number order.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryFilterReturnsWhatAFullScanReturns(string table, bool widened)
    {
        using var engine = new CodeBaseEngine
        {
            QueryTagScope = widened ? QueryTagScope.AllSeekable : QueryTagScope.Compatible,
        };

        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));
        opened.SelectTag(null);

        foreach (string filter in Filters(table))
        {
            Walk(opened.Query(filter)).Should().Equal(
                Scan(opened, filter), "the records {0} returns under {1}", filter, engine.QueryTagScope);
        }
    }

    /// <summary>
    /// And in the order of every tag the table has, because which order results come out in is
    /// user-visible and each tag is a different one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryFilterUnderEveryTagReturnsWhatAFullScanReturns(string table, bool widened)
    {
        using var engine = new CodeBaseEngine
        {
            QueryTagScope = widened ? QueryTagScope.AllSeekable : QueryTagScope.Compatible,
        };

        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));

        foreach (Tag tag in opened.Tags)
        {
            if (tag.Filtered)
                continue;

            opened.SelectTag(tag);

            foreach (string filter in Filters(table))
            {
                Walk(opened.Query(filter)).Should().Equal(
                    ScanInTagOrder(opened, tag, filter),
                    "the records {0} returns in {1}'s order under {2}",
                    filter, tag.Name, engine.QueryTagScope);
            }
        }
    }

    /// <summary>
    /// Widening the scope changes which tags answer a query and never which records it returns.
    /// </summary>
    [Theory]
    [InlineData("CDXEXPR")]
    [InlineData("CDXBASE")]
    public void WideningTheScopeChangesTheSeeksAndNotTheAnswers(string table)
    {
        using var narrow = new CodeBaseEngine { QueryTagScope = QueryTagScope.Compatible };
        using var wide = new CodeBaseEngine { QueryTagScope = QueryTagScope.AllSeekable };
        using Table a = narrow.OpenTable(Corpus.PathOf(table + ".DBF"));
        using Table b = wide.OpenTable(Corpus.PathOf(table + ".DBF"));

        a.SelectTag(null);
        b.SelectTag(null);

        foreach (string filter in Filters(table))
            Walk(a.Query(filter)).Should().Equal(Walk(b.Query(filter)), "the records {0} returns", filter);
    }

    /// <summary>
    /// And it really does change the seeks: an integer tag answers nothing by default and answers a
    /// term when widened, which is what makes the two runs above different work and not the same run
    /// twice.
    /// </summary>
    [Fact]
    public void WideningActuallyUnlocksATagTheReferenceRefuses()
    {
        using QueryHarness compatible = QueryHarness.For("CDXEXPR");
        using QueryHarness wide = QueryHarness.For("CDXEXPR", QueryTagScope.AllSeekable);

        compatible.Plan("K_INT > 0").Root.Should().BeNull();
        wide.Plan("K_INT > 0").Root.Should().NotBeNull();
        wide.Plan("K_CUR > 0").Root.Should().NotBeNull();
    }

    /// <summary>
    /// And a constant the widened tag's keys cannot express exactly is still refused, in both scopes.
    /// </summary>
    /// <value>
    /// An integer key holds no halves, and rounding one either way moves the boundary: the records
    /// with two in them are outside "at least two and a half" and inside "at least two". So the term
    /// is checked per record instead. The reference never faces this, because it gives the tag up
    /// before it ever looks at the constant.
    /// </value>
    [Theory]
    [InlineData("K_INT > 2.5")]
    [InlineData("K_INT >= 2.5")]
    [InlineData("K_CUR > 0.00005")]
    public void AConstantTheKeysCannotExpressIsRefusedEvenWhenWidened(string filter)
    {
        using QueryHarness compatible = QueryHarness.For("CDXEXPR");
        using QueryHarness wide = QueryHarness.For("CDXEXPR", QueryTagScope.AllSeekable);

        compatible.Plan(filter).Root.Should().BeNull();
        wide.Plan(filter).Root.Should().BeNull("no key means exactly {0}", filter);
    }

    /// <summary>
    /// A datetime tag is never used, and two independent things stop it: no literal is a moment, and
    /// a timestamp field will not compile against a number at all.
    /// </summary>
    /// <value>
    /// Recorded because the guard that refuses such a constant is unreachable today, and a reader
    /// finding it should know it is a guard rather than a live path. It earns its place by being what
    /// would keep a datetime tag safe if a timestamp constant ever became expressible -- without it,
    /// a number would be written into a datetime key.
    /// </value>
    [Fact]
    public void ADatetimeTagIsNeverUsedBecauseNoConstantCanReachIt()
    {
        using QueryHarness wide = QueryHarness.For("CDXEXPR", QueryTagScope.AllSeekable);

        wide.Plan("K_TS > K_TS").Root.Should().BeNull("neither side is a constant");
        Assert.Throws<CodeBaseException>(() => wide.Plan("K_TS > 0"));
    }

    /// <summary>
    /// A collated tag is never used, in either scope, because a filter's comparisons are in byte
    /// order and its keys are not.
    /// </summary>
    /// <value>
    /// CDXCOLL has a collated tag and a machine one over the same field, and the collated one comes
    /// first in the table's tag order -- so if a collated tag were usable at all it would be the one
    /// found. The machine tag answers instead, in both scopes, which is what the filters over this
    /// table above are checking the consequences of.
    ///
    /// This is the one refusal the widened scope does not lift, and the scan proved why: over this
    /// table "greater than M" includes alpha, cafe, cafe-with-an-accent, aeon and two more, because
    /// their bytes are above that letter's. A collated tag sorts every one of them below it, so
    /// seeking its range lost them -- and nothing puts a record back once a term is answered.
    /// </value>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACollatedTagIsNeverUsedInEitherScope(bool widened)
    {
        using QueryHarness harness = QueryHarness.For(
            "CDXCOLL", widened ? QueryTagScope.AllSeekable : QueryTagScope.Compatible);

        harness.OnlyLeaf("K_TEXT > \"M\"").Tag!.Value.Name.Should().Be("C_MACH");
    }

    private static IEnumerable<string> Filters(string table)
    {
        if (File.Exists(Path.Combine(Corpus.Root, table + ".query.dump.txt")))
        {
            foreach (DumpQueryCase filter in CorpusQueryDump.Load(table).Cases)
                yield return filter.Filter;
        }

        foreach (string filter in table switch
        {
            "CDXEXPR" => Extra,
            "CDXCOLL" => Collated,
            _ => [],
        })
        {
            yield return filter;
        }
    }

    /// <summary>
    /// Reads every record and evaluates the filter on it, which is what the optimizer exists to avoid.
    /// </summary>
    private static List<int> Scan(Table table, string filter)
    {
        CompiledExpression compiled = table.CompileFilter(filter);
        ExpressionEvaluator evaluator = new(compiled);
        List<int> matches = [];

        for (int record = 1; record <= table.RecordCount; record++)
        {
            if (table.Go(record) != GoResult.Ok)
                continue;

            ExpressionValue value = evaluator.Evaluate(table.EvaluationContext());

            if (!value.IsNull && value.Logical)
                matches.Add(record);
        }

        return matches;
    }

    /// <summary>
    /// The same, walked in a tag's order rather than in record order.
    /// </summary>
    private static List<int> ScanInTagOrder(Table table, Tag tag, string filter)
    {
        CompiledExpression compiled = table.CompileFilter(filter);
        ExpressionEvaluator evaluator = new(compiled);
        List<int> matches = [];

        for (GoResult go = table.GoFirstIndexed(tag); go == GoResult.Ok; go = table.GoNextIndexed(tag))
        {
            ExpressionValue value = evaluator.Evaluate(table.EvaluationContext());

            if (!value.IsNull && value.Logical)
                matches.Add(table.RecordNumber);
        }

        return matches;
    }

    private static List<int> Walk(Query query)
    {
        List<int> records = [];

        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
            records.Add(query.RecordNumber);

        return records;
    }
}
