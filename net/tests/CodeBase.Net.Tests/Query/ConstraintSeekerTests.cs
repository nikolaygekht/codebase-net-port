using AwesomeAssertions;
using CodeBase.Net.Queries;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// Turning one constraint into records, over keys chosen to sit exactly on the boundaries.
///
/// A corpus tag obliges a boundary case only by accident; these keys are picked so that every rule
/// has a value on each side of it -- a group of equal keys, a prefix that names three of them, a
/// range whose ends land on real keys and one whose ends land between them.
///
/// The keys are ALPHA, BRAVO, BRAVO, BRAVO, CHARLIE, DELTA, numbered one to six in that order.
/// </summary>
[Trait("Layer", "Component")]
public sealed class ConstraintSeekerTests
{
    private const int Records = 6;

    private static SortedKeyProbe Probe() =>
        SortedKeyProbe.OfText(10, "ALPHA", "BRAVO", "BRAVO", "BRAVO", "CHARLIE", "DELTA");

    private static int[] Seek(LeafConstraint constraint) =>
        [.. ConstraintSeeker.Evaluate(constraint, Probe(), Records).Records()];

    private static ConstraintValue Value(string text) => QueryValues.Text(text);

    [Fact]
    public void AnEmptyConstraintReachesNothingAndReadsNothing() =>
        Seek(LeafConstraint.Nothing).Should().BeEmpty();

    [Fact]
    public void AnUnconstrainedTagReachesEveryRecord() =>
        Seek(LeafConstraint.Anything).Should().Equal(1, 2, 3, 4, 5, 6);

    [Fact]
    public void AnExactValueReachesEveryRecordUnderIt() =>
        Seek(LeafConstraint.Exactly(Value("BRAVO"))).Should().Equal(2, 3, 4);

    [Fact]
    public void AnExactValueNothingHoldsReachesNothing() =>
        Seek(LeafConstraint.Exactly(Value("BRAVA"))).Should().BeEmpty();

    /// <summary>
    /// A value shorter than the keys names the group that begins with it. This is what the widening
    /// rule in decomposition exists to protect, seen from the seeking side.
    /// </summary>
    [Fact]
    public void AShortValueReachesTheWholeGroupThatBeginsWithIt() =>
        Seek(LeafConstraint.Exactly(Value("B"))).Should().Equal(2, 3, 4);

    [Fact]
    public void AnInclusiveLowerBoundAdmitsItsOwnValue() =>
        Seek(LeafConstraint.From(Value("BRAVO"), inclusive: true)).Should().Equal(2, 3, 4, 5, 6);

    /// <summary>
    /// An exclusive bound steps over the whole group that equals it, not just the first of them --
    /// which is the case a seek that stopped at the first mismatch would get wrong.
    /// </summary>
    [Fact]
    public void AnExclusiveLowerBoundStepsOverEveryRecordAtThatValue() =>
        Seek(LeafConstraint.From(Value("BRAVO"), inclusive: false)).Should().Equal(5, 6);

    [Fact]
    public void AnInclusiveUpperBoundAdmitsItsOwnValue() =>
        Seek(LeafConstraint.To(Value("BRAVO"), inclusive: true)).Should().Equal(1, 2, 3, 4);

    [Fact]
    public void AnExclusiveUpperBoundStopsBeforeEveryRecordAtThatValue() =>
        Seek(LeafConstraint.To(Value("BRAVO"), inclusive: false)).Should().Equal(1);

    [Fact]
    public void ALowerBoundBelowEverythingReachesTheWholeTag() =>
        Seek(LeafConstraint.From(Value("A"), inclusive: true)).Should().Equal(1, 2, 3, 4, 5, 6);

    [Fact]
    public void ALowerBoundAboveEverythingReachesNothing() =>
        Seek(LeafConstraint.From(Value("Z"), inclusive: true)).Should().BeEmpty();

    [Fact]
    public void AnUpperBoundBelowEverythingReachesNothing() =>
        Seek(LeafConstraint.To(Value("A"), inclusive: false)).Should().BeEmpty();

    /// <summary>
    /// An exclusion on its own is nearly the whole table, so the set is built by naming what it
    /// leaves out and saying so -- and it must still read as the records it holds.
    /// </summary>
    [Fact]
    public void AnExclusionOnItsOwnReachesEverythingElse()
    {
        RecordSet set = ConstraintSeeker.Evaluate(
            LeafConstraint.Excluding(Value("BRAVO")), Probe(), Records);

        set.IsComplement.Should().BeTrue("the cheap way to describe nearly everything");
        set.Records().Should().Equal(1, 5, 6);
        set.Count.Should().Be(3);
    }

    [Fact]
    public void AnExclusionNothingHoldsRemovesNothing() =>
        ConstraintSeeker.Evaluate(LeafConstraint.Excluding(Value("ZULU")), Probe(), Records)
            .Records().Should().Equal(1, 2, 3, 4, 5, 6);
}
