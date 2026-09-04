using AwesomeAssertions;
using CodeBase.Net.Queries;
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
    /// A disjunction whose union really is one constraint folds to one leaf, so the tag is seeked
    /// once. All five shapes the algebra folds are corpus filters, and the candidate-set gate checks
    /// each against the reference's own answer -- so this side is no longer gated by hand-written
    /// expectations alone.
    /// </summary>
    [Theory]
    [InlineData("K_NUM > 0 .OR. K_NUM > 100", "overlapping ranges")]
    [InlineData("K_NUM <= 0 .OR. K_NUM > 0", "ranges that meet exactly")]
    [InlineData("K_NUM > 0 .OR. K_NUM = 0", "a value on the open end of a range")]
    [InlineData("K_NUM <> 0 .OR. K_NUM <> 2.5", "two exclusions, which no value fails together")]
    [InlineData("UPPER(K_TEXT) > \"S\" .OR. UPPER(K_TEXT) > \"M\"", "overlapping character ranges")]
    public void ADisjunctionWhoseUnionIsOneConstraintFoldsToOneLeaf(string filter, string shape)
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.Plan(filter).LeafCount.Should().Be(1, "{0} is {1}", filter, shape);
    }

    /// <summary>
    /// A range with a value struck out of it stays one leaf, and the struck-out value is kept rather
    /// than folded into an end -- there is no end for it to move.
    /// </summary>
    [Fact]
    public void ABoundedRangeKeepsTheValueStruckOutOfItsMiddle()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");
        ConstraintNode leaf =
            harness.OnlyLeaf("K_NUM > -100 .AND. K_NUM < 1000 .AND. K_NUM <> 0.125");

        leaf.Constraint!.Lower.Should().NotBeNull();
        leaf.Constraint!.Upper.Should().NotBeNull();
        leaf.Constraint!.Exclusions.Should().ContainSingle();
        leaf.SourceNodes.Should().HaveCount(3, "all three terms are answered by the one leaf");
    }

    /// <summary>
    /// A range whose two exclusive ends bracket no key at all reaches nothing.
    /// </summary>
    /// <value>
    /// The case the reference guards with a collision check, because seeking both ends of it lands
    /// them on the same pair of neighbouring keys and a careless range would return the one they
    /// collided on. This port needs no such guard: it seeks the lower end and tests the upper as it
    /// walks, so the first key it looks at is already past the range.
    /// </value>
    [Fact]
    public void ARangeWithNothingInItReachesNothing()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.Reach("K_NUM > 0.125 .AND. K_NUM < 0.135").Count.Should().Be(0);
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
