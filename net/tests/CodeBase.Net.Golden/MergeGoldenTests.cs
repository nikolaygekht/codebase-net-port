using AwesomeAssertions;
using CodeBase.Net.Query;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Merging leaves the records alone and reduces the seeks, which are two claims that fail
/// differently.
///
/// That the records are unchanged is asserted by the candidate-set gate, which was green before any
/// merge code existed and has to stay green after -- so it is not repeated here. What is here is the
/// other half, and it is the half no correctness check can see: a merge that is perfectly correct and
/// never merges anything passes every record-set assertion in the suite. Leaf count is seek count, so
/// counting leaves is how the optimization itself is gated.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class MergeGoldenTests
{
    /// <summary>
    /// Two bounds on one tag become one range: one descent instead of two, which is the whole reason
    /// the algebra exists.
    /// </summary>
    [Fact]
    public void TwoBoundsOnOneTagBecomeOneLeaf()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.Plan("K_NUM > 0 .AND. K_NUM < 100").LeafCount.Should().Be(1);
    }

    /// <summary>
    /// And terms over different tags do not, because there is no one range over two tags. This is the
    /// case a merge that folded everything together would get wrong while still counting fewer leaves.
    /// </summary>
    [Fact]
    public void TermsOverDifferentTagsStayApart()
    {
        using QueryHarness harness = QueryHarness.For("CDXBASE");

        harness.Plan("K_N > 0 .AND. K_C > \"M\"").LeafCount.Should().Be(2);
    }

    /// <summary>
    /// A three-term filter folds the two that share a tag and leaves the third alone.
    /// </summary>
    /// <value>
    /// The third term names a tag over an expression rather than a bare field, because CDXEXPR has no
    /// tag over K_TEXT itself -- every one of its character tags indexes a function of it. The
    /// corpus's own three-term filter uses the bare field and so has only two optimizable terms,
    /// which is why it folds to one leaf rather than two.
    /// </value>
    [Fact]
    public void OnlyTheTermsSharingATagFold()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.Plan("K_NUM > -100 .AND. K_NUM < 100 .AND. UPPER(K_TEXT) > \"A\"")
            .LeafCount.Should().Be(2);

        harness.Plan("K_NUM > -100 .AND. K_NUM < 100 .AND. K_TEXT > \"A\"")
            .LeafCount.Should().Be(1, "no tag indexes K_TEXT itself");
    }

    /// <summary>
    /// Bounds that meet at one value collapse to that value, which turns a range walk into a seek.
    /// </summary>
    [Fact]
    public void BoundsThatMeetBecomeAnExactValue()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");
        ConstraintNode leaf = harness.OnlyLeaf("K_NUM >= 0 .AND. K_NUM <= 0");

        leaf.Constraint!.Kind.Should().Be(ConstraintKind.Equal);
    }

    /// <summary>
    /// Bounds that cross admit nothing, and saying so costs no reads at all -- the tag is never
    /// touched.
    /// </summary>
    [Fact]
    public void BoundsThatCrossBecomeNothing()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");
        ConstraintNode leaf = harness.OnlyLeaf("K_NUM > 100 .AND. K_NUM < 0");

        leaf.Constraint!.Kind.Should().Be(ConstraintKind.Empty);
        harness.Reach("K_NUM > 100 .AND. K_NUM < 0").Count.Should().Be(0);
    }

    /// <summary>
    /// Two struck-out values on one tag become one leaf holding both.
    /// </summary>
    [Fact]
    public void TwoExclusionsOnOneTagBecomeOneLeaf()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");
        ConstraintNode leaf = harness.OnlyLeaf("K_NUM <> 0 .AND. K_NUM <> 2.5");

        leaf.Constraint!.Exclusions.Should().HaveCount(2);
    }

    /// <summary>
    /// A value struck out at an inclusive end moves the end past itself. This is the one place the
    /// algebra strengthens an operator, and leaving it out returns exactly one record too many --
    /// which the candidate-set gate would catch, by one record, on this filter alone.
    /// </summary>
    [Fact]
    public void AValueStruckOutAtABoundMovesTheBound()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");
        ConstraintNode leaf = harness.OnlyLeaf("K_NUM >= 0 .AND. K_NUM <> 0");

        leaf.Constraint!.Lower!.Value.Inclusive.Should().BeFalse();
        leaf.Constraint!.Exclusions.Should().BeEmpty("the exclusion became the bound");
    }

    /// <summary>
    /// A disjunction whose union is not one range keeps its leaves apart, deliberately. Two seeks and
    /// a union is the same answer, and inventing a shape for a set that has none is how a wrong one
    /// gets built.
    /// </summary>
    [Theory]
    [InlineData("K_NUM < 0 .OR. K_NUM > 100")]
    [InlineData("K_NUM = 0 .OR. K_NUM = 2.5")]
    public void ADisjunctionWithAHoleInItKeepsItsLeaves(string filter)
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.Plan(filter).LeafCount.Should().Be(2);
    }

    /// <summary>
    /// A merged leaf answers every term it absorbed, which is what the residual filter reads. A term
    /// it forgot would be checked again -- harmless -- and one it invented would be skipped, which is
    /// not.
    /// </summary>
    [Fact]
    public void AMergedLeafAnswersBothOfTheTermsItAbsorbed()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");
        QueryPlan plan = harness.Plan("K_NUM > 0 .AND. K_NUM < 100");

        plan.Root!.SourceNodes.Should().HaveCount(2);
        plan.FullyMapped.Should().BeTrue();

        foreach (int term in plan.Root.SourceNodes)
            plan.Answers(term).Should().BeTrue();
    }
}
