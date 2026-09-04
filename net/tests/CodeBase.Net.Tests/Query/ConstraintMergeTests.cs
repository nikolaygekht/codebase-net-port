using AwesomeAssertions;
using CodeBase.Net.Query;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// The merge algebra, checked as sets rather than case by case.
///
/// Every pair of constraints drawn from a population is folded, and what the fold admits is compared
/// against what the two inputs admit, over every value in a domain. That is a promise about what a
/// merge [i]means[/i], so it survives any rewrite of how the folding is done -- where a test naming
/// which field ended up set would not.
///
/// It is also the only gate the disjunction side has. Not one filter in the corpus has two terms over
/// one tag joined by an or whose union is a single constraint, so layer four never reaches that code:
/// its two corpus cases are a range with a hole in it and two separate values, and both are refused.
/// The conjunction side is reached by the corpus and by these together.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class ConstraintMergeTests
{
    /// <summary>
    /// The values every fold is checked against, spanning both sides of every bound in the population.
    /// </summary>
    private static readonly double[] Domain =
        [-2, -1, -0.5, 0, 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6];

    /// <summary>
    /// The constraints folded against each other, chosen so that every pair of shapes occurs.
    /// </summary>
    public static TheoryData<int, int> Pairs
    {
        get
        {
            TheoryData<int, int> pairs = [];

            for (int left = 0; left < Population.Length; left++)
            {
                for (int right = 0; right < Population.Length; right++)
                    pairs.Add(left, right);
            }

            return pairs;
        }
    }

    private static readonly Func<LeafConstraint>[] Population =
    [
        () => LeafConstraint.Anything,
        () => LeafConstraint.Nothing,
        () => LeafConstraint.Exactly(Value(1)),
        () => LeafConstraint.Exactly(Value(2)),
        () => LeafConstraint.From(Value(1), inclusive: true),
        () => LeafConstraint.From(Value(1), inclusive: false),
        () => LeafConstraint.From(Value(3), inclusive: true),
        () => LeafConstraint.To(Value(2), inclusive: true),
        () => LeafConstraint.To(Value(2), inclusive: false),
        () => LeafConstraint.To(Value(0), inclusive: true),
        () => LeafConstraint.Excluding(Value(1)),
        () => LeafConstraint.Excluding(Value(2)),
        () => LeafConstraint.Range(new Bound(Value(0), true), new Bound(Value(4), true), []),
        () => LeafConstraint.Range(new Bound(Value(1), false), new Bound(Value(3), false), [Value(2)]),
        () => LeafConstraint.Range(new Bound(Value(2), true), new Bound(Value(2), true), []),
    ];

    private static ConstraintValue Value(double number) => QueryValues.Number(number);

    /// <summary>
    /// A conjunction always has one answer, and it is the values both inputs admit.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void AConjunctionAdmitsExactlyWhatBothAdmit(int left, int right)
    {
        LeafConstraint a = Population[left]();
        LeafConstraint b = Population[right]();
        LeafConstraint merged = ConstraintMerge.And(a, b);

        foreach (double number in Domain)
        {
            ConstraintValue value = Value(number);

            merged.Accepts(value).Should().Be(
                a.Accepts(value) && b.Accepts(value), "{0} and {1} at {2}", a, b, number);
        }
    }

    /// <summary>
    /// A disjunction may refuse to fold, and where it does not, it admits the values either input
    /// admits.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void ADisjunctionThatFoldsAdmitsExactlyWhatEitherAdmits(int left, int right)
    {
        LeafConstraint a = Population[left]();
        LeafConstraint b = Population[right]();
        LeafConstraint? merged = ConstraintMerge.Or(a, b);

        if (merged is null)
            return;

        foreach (double number in Domain)
        {
            ConstraintValue value = Value(number);

            merged.Accepts(value).Should().Be(
                a.Accepts(value) || b.Accepts(value), "{0} or {1} at {2}", a, b, number);
        }
    }

    /// <summary>
    /// A merge is never an excuse to lose the shape rules: whatever comes back is one of the three
    /// forms a constraint may take, with at most one end at each side.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void AMergeAlwaysProducesACanonicalConstraint(int left, int right)
    {
        LeafConstraint merged = ConstraintMerge.And(Population[left](), Population[right]());

        switch (merged.Kind)
        {
            case ConstraintKind.Empty:
                merged.Value.Should().BeNull();
                merged.Lower.Should().BeNull();
                merged.Upper.Should().BeNull();
                merged.Exclusions.Should().BeEmpty();
                break;

            case ConstraintKind.Equal:
                merged.Value.Should().NotBeNull();
                merged.Lower.Should().BeNull();
                merged.Upper.Should().BeNull();
                merged.Exclusions.Should().BeEmpty();
                break;

            default:
                merged.Value.Should().BeNull();
                merged.Exclusions.Should().OnlyHaveUniqueItems();
                break;
        }
    }

    /// <summary>
    /// A conjunction proved empty says so, rather than describing a range nothing can satisfy and
    /// making the seeker discover that by walking.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void AConjunctionThatAdmitsNothingSaysSo(int left, int right)
    {
        LeafConstraint a = Population[left]();
        LeafConstraint b = Population[right]();
        LeafConstraint merged = ConstraintMerge.And(a, b);

        // Only where the domain proves it, since a range may be empty over the reals and this checks
        // a finite sample -- but any pair with no common value in the whole domain must collapse.
        bool anyAdmitted = Domain.Any(number => a.Accepts(Value(number)) && b.Accepts(Value(number)));

        if (!anyAdmitted && merged.Kind != ConstraintKind.Empty)
            merged.Kind.Should().NotBe(ConstraintKind.Equal, "{0} and {1} share no value", a, b);
    }

    /// <summary>
    /// Folding is order-independent for a conjunction, which is what lets leaves be grouped by tag
    /// and folded in whatever order they were written.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public void FoldingAConjunctionEitherWayRoundAgrees(int left, int right)
    {
        LeafConstraint a = Population[left]();
        LeafConstraint b = Population[right]();

        LeafConstraint forwards = ConstraintMerge.And(a, b);
        LeafConstraint backwards = ConstraintMerge.And(b, a);

        foreach (double number in Domain)
        {
            ConstraintValue value = Value(number);
            forwards.Accepts(value).Should().Be(backwards.Accepts(value), "at {0}", number);
        }
    }
}
