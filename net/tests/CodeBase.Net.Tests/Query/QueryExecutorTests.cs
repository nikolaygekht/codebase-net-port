using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// The record set a plan produces, over tags whose keys are lists in memory.
/// </summary>
/// <value>
/// The probes are [c]SortedKeyProbe[/c]s, which [c]TagProbeContract[/c] holds to the real tag probe, so
/// what is under test here is the executor alone: a leaf reaches the records its tag lists in range,
/// and a branch combines its children as its connective says (QUERY-OPTIMIZER.md section 6).
/// </value>
[Trait("Layer", "Component")]
public sealed class QueryExecutorTests
{
    private const int Records = 6;

    private static readonly IFieldMetadata Fields = new TableFieldMetadata(
        [
            new FieldDefinition("K_TEXT", 'C', 'C', 4, 0, 1, false, false, false, false, null),
            new FieldDefinition("K_CODE", 'C', 'C', 4, 0, 5, false, false, false, false, null),
            new FieldDefinition("K_LOG", 'L', 'L', 1, 0, 9, false, false, false, false, null),
        ],
        "T");

    private static readonly QueryTag[] Tags =
    [
        Tag(0, "T_TEXT", "K_TEXT"),
        Tag(1, "T_CODE", "K_CODE"),
    ];

    /// <summary>Record n holds the n-th key of each list.</summary>
    private static readonly string[] Texts = ["A", "B", "C", "B", "D", "A"];

    private static readonly string[] Codes = ["X", "Y", "X", "Z", "Y", "X"];

    [Fact]
    public void APlanWithNothingToSeekAdmitsEveryRecord() =>
        Reached("K_LOG").Should().Equal(1, 2, 3, 4, 5, 6);

    [Fact]
    public void ALeafReachesTheRecordsItsTagListsInRange()
    {
        Reached("K_TEXT = \"B\"").Should().Equal(2, 4);

        // A strict comparison breaks a tie on the common bytes by width, so "B   " is greater than
        // "B" and the records holding B are in range too (QUERY-OPTIMIZER.md section 4.4).
        Reached("K_TEXT > \"B\"").Should().Equal(2, 3, 4, 5);
        Reached("K_TEXT > \"B   \"").Should().Equal(3, 5);
    }

    [Fact]
    public void AnAndReachesTheRecordsEveryTermReaches() =>
        Reached("K_TEXT >= \"B\" .AND. K_CODE = \"Y\"").Should().Equal(2, 5);

    [Fact]
    public void AnOrReachesTheRecordsAnyTermReaches() =>
        Reached("K_TEXT = \"A\" .OR. K_CODE = \"Z\"").Should().Equal(1, 4, 6);

    [Fact]
    public void ATagSeekedTwiceInOnePlanIsOpenedOnce()
    {
        // Two ranges of one tag that do not fold: two leaves, one probe.
        int opened = 0;
        QueryPlan plan = Plan("K_TEXT < \"B\" .OR. K_TEXT > \"C\"");
        plan.LeafCount.Should().Be(2);

        new QueryExecutor(tag => { opened++; return Probe(tag); }, Records).Evaluate(plan)
            .Records().Should().Equal(1, 3, 5, 6);
        opened.Should().Be(1);
    }

    private static int[] Reached(string filter) =>
        [.. new QueryExecutor(Probe, Records).Evaluate(Plan(filter)).Records()];

    private static QueryPlan Plan(string filter) =>
        new QueryDecomposer(Tags, QueryTagScope.Compatible)
            .Decompose(TypeResolver.Resolve(ExpressionParser.Parse(filter, Fields)));

    private static ITagProbe Probe(QueryTag tag) =>
        SortedKeyProbe.OfText(4, tag.Name == "T_TEXT" ? Texts : Codes);

    private static QueryTag Tag(int ordinal, string name, string expression)
    {
        CompiledExpression key = TypeResolver.Resolve(ExpressionParser.Parse(expression, Fields));
        return new QueryTag(ordinal, name, 4, KeyKind.Character, CollationName.Machine, false, false, key);
    }
}
