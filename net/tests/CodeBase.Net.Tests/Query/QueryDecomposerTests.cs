using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// What plan a filter decomposes into, over tags built in memory.
/// </summary>
/// <value>
/// The golden gates hold the decomposer to the reference's own decisions; these hold it to the rules
/// in QUERY-OPTIMIZER.md sections 3 and 4, one rule at a time, with no table in sight. A plan is
/// described as its leaves — the tag and the constraint each puts on it — under the connectives that
/// join them. Children are listed sorted, because which order the walk meets them in is not a promise.
/// </value>
[Trait("Layer", "Unit")]
public sealed class QueryDecomposerTests
{
    private static readonly IFieldMetadata Fields = new TableFieldMetadata(
        [
            new FieldDefinition("K_NUM", 'N', 'N', 12, 3, 1, false, false, false, false, null),
            new FieldDefinition("K_TEXT", 'C', 'C', 10, 0, 13, false, false, false, false, null),
            new FieldDefinition("K_INT", 'I', 'I', 4, 0, 23, false, false, false, false, null),
            new FieldDefinition("K_CUR", 'Y', 'Y', 8, 4, 27, false, false, false, false, null),
            new FieldDefinition("K_DATE", 'D', 'D', 8, 0, 35, false, false, false, false, null),
            new FieldDefinition("K_LOG", 'L', 'L', 1, 0, 43, false, false, false, false, null),
        ],
        "T");

    private static readonly QueryTag[] Tags =
    [
        Tag(0, "T_NUM", "K_NUM", KeyKind.Double),
        Tag(1, "T_TEXT", "K_TEXT", KeyKind.Character),
        Tag(2, "T_INT", "K_INT", KeyKind.Int32),
        Tag(3, "T_CUR", "K_CUR", KeyKind.Currency),
        Tag(4, "T_DATE", "K_DATE", KeyKind.Date),
    ];

    // ------------------------------------------------------------- one term

    [Theory]
    [InlineData("K_NUM = 5", "T_NUM = 5")]
    [InlineData("K_NUM # 5", "T_NUM not 5")]
    [InlineData("K_NUM <> 5", "T_NUM not 5")]
    [InlineData("K_NUM > 5", "T_NUM > 5")]
    [InlineData("K_NUM >= 5", "T_NUM >= 5")]
    [InlineData("K_NUM < 5", "T_NUM < 5")]
    [InlineData("K_NUM <= 5", "T_NUM <= 5")]
    public void AComparisonWithAConstantConstrainsTheTagOverItsOtherSide(string filter, string plan)
    {
        QueryPlan decomposed = Plan(filter);

        Describe(decomposed).Should().Be(plan);
        decomposed.FullyMapped.Should().BeTrue();
        decomposed.LeafCount.Should().Be(1);
    }

    [Theory]
    // With the constant first, the operator is read from the tag's side: 5 > K_NUM bounds it above.
    [InlineData("5 = K_NUM", "T_NUM = 5")]
    [InlineData("5 # K_NUM", "T_NUM not 5")]
    [InlineData("5 < K_NUM", "T_NUM > 5")]
    [InlineData("5 <= K_NUM", "T_NUM >= 5")]
    [InlineData("5 > K_NUM", "T_NUM < 5")]
    [InlineData("5 >= K_NUM", "T_NUM <= 5")]
    public void AReflectedComparisonIsReadFromTheTagsSide(string filter, string plan) =>
        Describe(Plan(filter)).Should().Be(plan);

    [Theory]
    // A constant shorter than the key stands for a prefix, and every key opening with it is greater
    // than it; an exclusive lower bound would step past them all, so it admits its own value.
    [InlineData("K_TEXT > \"SM\"", "T_TEXT >= \"SM\"")]
    [InlineData("\"SM\" < K_TEXT", "T_TEXT >= \"SM\"")]
    [InlineData("K_TEXT > \"ABCDEFGHIJ\"", "T_TEXT > \"ABCDEFGHIJ\"")]
    [InlineData("K_TEXT < \"SM\"", "T_TEXT < \"SM\"")]
    [InlineData("\"SM\" > K_TEXT", "T_TEXT < \"SM\"")]
    public void OnlyALowerBoundShorterThanTheKeyIsWidened(string filter, string plan) =>
        Describe(Plan(filter)).Should().Be(plan);

    [Theory]
    // Neither a bare field, a negation, a comparison of two fields, nor an expression no tag is
    // built over can be answered by a seek.
    [InlineData("K_LOG")]
    [InlineData(".NOT. K_NUM = 1")]
    [InlineData("K_NUM > K_INT")]
    [InlineData("K_NUM + 1 = 2")]
    public void ATermNoTagCanAnswerLeavesNothingToSeek(string filter)
    {
        QueryPlan decomposed = Plan(filter);

        decomposed.Root.Should().BeNull();
        decomposed.FullyMapped.Should().BeFalse();
        decomposed.LeafCount.Should().Be(0);
    }

    // --------------------------------------------------------- connectives

    [Fact]
    public void AnAndKeepsTheTermsItCanAnswerAndLeavesTheRestToBeChecked()
    {
        CompiledExpression filter = Compile("K_NUM = 1 .AND. K_LOG");
        QueryPlan decomposed = new QueryDecomposer(Tags, QueryTagScope.Compatible).Decompose(filter);

        Describe(decomposed).Should().Be("T_NUM = 1");
        decomposed.FullyMapped.Should().BeFalse();
        AnsweredTerms(decomposed, filter).Should().Be(1);
    }

    [Theory]
    // Dropping a disjunct would narrow the candidates and lose records, so one term no tag answers
    // takes the whole disjunction with it, from either side.
    [InlineData("K_NUM = 1 .OR. K_LOG")]
    [InlineData("K_LOG .OR. K_NUM = 1")]
    public void AnOrWithATermNoTagCanAnswerIsAbandoned(string filter)
    {
        QueryPlan decomposed = Plan(filter);

        decomposed.Root.Should().BeNull();
        decomposed.FullyMapped.Should().BeFalse();
    }

    [Fact]
    public void AnAndWithNoTermATagCanAnswerLeavesNothingToSeek()
    {
        QueryPlan decomposed = Plan("K_LOG .AND. K_NUM > K_INT");

        decomposed.Root.Should().BeNull();
        decomposed.FullyMapped.Should().BeFalse();
    }

    [Fact]
    public void AnOrOverTwoTagsSeeksBoth()
    {
        QueryPlan decomposed = Plan("K_NUM = 1 .OR. K_TEXT = \"A\"");

        Describe(decomposed).Should().Be("any(T_NUM = 1; T_TEXT = \"A\")");
        decomposed.FullyMapped.Should().BeTrue();
        decomposed.LeafCount.Should().Be(2);
    }

    [Fact]
    public void TwoTermsOverOneTagUnderAnAndBecomeOneRange()
    {
        CompiledExpression filter = Compile("K_NUM > 1 .AND. K_NUM < 5");
        QueryPlan decomposed = new QueryDecomposer(Tags, QueryTagScope.Compatible).Decompose(filter);

        Describe(decomposed).Should().Be("T_NUM > 1 and < 5");
        decomposed.LeafCount.Should().Be(1);
        AnsweredTerms(decomposed, filter).Should().Be(2, "the one leaf answers both terms it absorbed");
    }

    [Theory]
    // Under an or, two ranges fold only where their union is itself one range.
    [InlineData("K_NUM < 5 .OR. K_NUM > 2", "T_NUM anything")]
    [InlineData("K_NUM < 2 .OR. K_NUM > 5", "any(T_NUM < 2; T_NUM > 5)")]
    public void TwoTermsOverOneTagUnderAnOrFoldOnlyIntoOneRange(string filter, string plan) =>
        Describe(Plan(filter)).Should().Be(plan);

    [Fact]
    public void NoTermIsFoldedAcrossAConnective()
    {
        // K_NUM = 1 sits under the and, K_NUM = 3 under the or above it: different questions.
        Describe(Plan("(K_NUM = 1 .AND. K_TEXT = \"A\") .OR. K_NUM = 3"))
            .Should().Be("any(T_NUM = 3; all(T_NUM = 1; T_TEXT = \"A\"))");
    }

    // ----------------------------------------------------------- the tags

    [Fact]
    public void ATagTheOptimizerRefusesDoesNotEndTheSearch()
    {
        QueryTag[] tags =
        [
            Tag(0, "T_FILT", "K_NUM", KeyKind.Double, filtered: true),
            Tag(1, "T_UNIQ", "K_NUM", KeyKind.Double, unique: true),
            Tag(2, "T_PLAIN", "K_NUM", KeyKind.Double),
        ];

        Describe(Plan("K_NUM = 1", tags: tags)).Should().Be("T_PLAIN = 1");
    }

    [Theory]
    // An integer, currency or datetime tag is a widened one, used only when the caller allows it.
    [InlineData(QueryTagScope.Compatible, "none")]
    [InlineData(QueryTagScope.AllSeekable, "T_INT = 3")]
    public void AWidenedTagIsUsedOnlyInTheWidenedScope(QueryTagScope scope, string plan) =>
        Describe(Plan("K_INT = 3", scope)).Should().Be(plan);

    [Theory]
    // A tag whose keys cannot hold the constant exactly would answer another question.
    [InlineData("K_INT = 2.5", "none")]
    [InlineData("K_CUR = 1.23456", "none")]
    [InlineData("K_CUR = 1.2345", "T_CUR = 1.2345")]
    public void AConstantTheTagCannotHoldExactlyIsCheckedPerRecord(string filter, string plan) =>
        Describe(Plan(filter, QueryTagScope.AllSeekable)).Should().Be(plan);

    [Fact]
    public void AConstantIsReadUnderTheDatePolicyTheDecomposerWasGiven()
    {
        // A two-digit year means what the caller's century says, so the same date written two ways
        // seeks the same key.
        ExpressionOptions twentieth = ExpressionOptions.CodeBase.WithCentury(20);

        LeafValue("K_DATE = CTOD(\"01/02/00\")", twentieth)
            .Should().Be(LeafValue("K_DATE = STOD(\"20000102\")", twentieth));
    }

    // ------------------------------------------------- which nodes were tried

    [Fact]
    public void AnOrIsAbandonedWithoutTryingTheTermsItHasNotReached()
    {
        // The walk meets an or's operands last first, so a failing last operand ends it before the
        // first is tried, and a failing first operand only after the last was.
        int Matched(string text) =>
            new QueryDecomposer(Tags, QueryTagScope.Compatible).MatchTags(Compile(text)).Count(tag => tag is not null);

        Matched("K_LOG .OR. K_NUM = 1").Should().Be(1);
        Matched("K_NUM = 1 .OR. K_LOG").Should().Be(0);
    }

    // ------------------------------------------------------- the descriptions

    [Fact]
    public void AConstraintDescribesItself()
    {
        ConstraintValue one = Number(1);
        ConstraintValue three = Number(3);
        ConstraintValue five = Number(5);

        LeafConstraint.Nothing.ToString().Should().Be("nothing");
        LeafConstraint.Anything.ToString().Should().Be("anything");
        LeafConstraint.Range(new Bound(one, true), new Bound(five, false), [three]).ToString()
            .Should().Be(">= 1 and < 5 and not 3");
        LeafConstraint.Range(null, new Bound(five, true), []).ToString().Should().Be("<= 5");
    }

    [Theory]
    // Texts compare on their common bytes, and a tie there is broken by width, so a prefix sorts
    // before every text it opens (QUERY-OPTIMIZER.md section 4.4).
    [InlineData("AB", "AC", -1)]
    [InlineData("AC", "AB", 1)]
    [InlineData("AB", "ABC", -1)]
    [InlineData("ABC", "AB", 1)]
    [InlineData("AB", "AB", 0)]
    public void TextsCompareByTheirCommonBytesThenByWidth(string left, string right, int order) =>
        Math.Sign(Text(left).CompareTo(Text(right))).Should().Be(order);

    [Fact]
    public void AValueDescribesItselfAsANumberOrAQuotedText()
    {
        Number(1.5).ToString().Should().Be("1.5");
        ConstraintValue.From(ExpressionValue.Text("AB"u8.ToArray()), 2).ToString().Should().Be("\"AB\"");
    }

    // ---------------------------------------------------------------- helpers

    private static QueryTag Tag(
        int ordinal, string name, string expression, KeyKind kind, bool filtered = false, bool unique = false)
    {
        CompiledExpression key = Compile(expression);
        return new QueryTag(ordinal, name, ExpressionKey.Length(key), kind, CollationName.Machine, filtered, unique, key);
    }

    private static CompiledExpression Compile(string text) =>
        TypeResolver.Resolve(ExpressionParser.Parse(text, Fields));

    private static QueryPlan Plan(
        string filter,
        QueryTagScope scope = QueryTagScope.Compatible,
        IReadOnlyList<QueryTag>? tags = null,
        ExpressionOptions? options = null) =>
        new QueryDecomposer(tags ?? Tags, scope, options).Decompose(Compile(filter));

    private static double LeafValue(string filter, ExpressionOptions options) =>
        Plan(filter, options: options).Root!.Constraint!.Value!.Number;

    private static ConstraintValue Number(double value) => ConstraintValue.From(ExpressionValue.Numeric(value), 8);

    private static ConstraintValue Text(string value) =>
        ConstraintValue.From(ExpressionValue.Text(System.Text.Encoding.ASCII.GetBytes(value)), value.Length);

    private static int AnsweredTerms(QueryPlan plan, CompiledExpression filter) =>
        Enumerable.Range(0, filter.Nodes.Count).Count(plan.Answers);

    private static string Describe(QueryPlan plan) => plan.Root is null ? "none" : Describe(plan.Root);

    private static string Describe(ConstraintNode node) =>
        node.IsLeaf
            ? $"{node.Tag!.Value.Name} {node.Constraint}"
            : (node.Combination == ConstraintCombination.All ? "all" : "any")
                + "(" + string.Join("; ", node.Children.Select(Describe).Order(StringComparer.Ordinal)) + ")";
}
