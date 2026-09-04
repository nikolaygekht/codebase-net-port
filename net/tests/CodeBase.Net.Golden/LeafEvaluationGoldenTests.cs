using AwesomeAssertions;
using CodeBase.Net.Queries;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The first end-to-end signal: a filter that decomposes to a single leaf reaches the records the
/// reference's own bitmap holds.
///
/// This is depth 3 of the gate, taken as far as it can go before anything combines two leaves. A
/// filter qualifies when the reference considered its bitmap to answer the whole question and this
/// port decomposed it to exactly one leaf -- so there is nothing to intersect and nothing left to
/// check per record, and the leaf's records are the whole answer.
///
/// It compares the records the two sets [i]hold[/i] and not the bits they store. The reference flags
/// the complement of a range that covers more than half a tag, so most of these are stored inverted;
/// that is a decision about cost and not about which records answer the filter, and this port does
/// not make it.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class LeafEvaluationGoldenTests
{
    public static TheoryData<string> Tables => ["CDXEXPR", "CDXBASE", "CDXDEEP"];

    [Theory]
    [MemberData(nameof(Tables))]
    public void ASingleLeafReachesTheRecordsTheReferenceBitmapHolds(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);
        int checkedFilters = 0;

        foreach (DumpQueryCase filter in harness.Dump.Cases)
        {
            QueryPlan plan = harness.Plan(filter.Filter);

            if (!filter.FullyMapped || plan.Root is null || plan.LeafCount != 1)
                continue;

            ConstraintNode leaf = plan.Root.Walk().Single(node => node.IsLeaf);
            RecordSet reached = ConstraintSeeker.Evaluate(
                leaf.Constraint!, harness.Probe(leaf.Tag!.Value), harness.RecordCount);

            checkedFilters++;
            reached.Records().Should().Equal(
                filter.Set!.Effective(harness.Dump.RecordCount),
                "the records {0} reaches", filter.Filter);
        }

        // 41 filters across the three tables qualify -- 24, 9 and 8 -- so a table contributing none
        // would mean the plan stopped producing single-leaf trees rather than the gate passing.
        checkedFilters.Should().BeGreaterThanOrEqualTo(8, "the gate needs filters it applies to");
    }

    /// <summary>
    /// And the count agrees, which is the number the reference answers without reading a record when
    /// a plan covers the whole filter.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void ASingleLeafHoldsAsManyRecordsAsTheReferenceReturned(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases)
        {
            QueryPlan plan = harness.Plan(filter.Filter);

            if (!filter.FullyMapped || plan.Root is null || plan.LeafCount != 1)
                continue;

            ConstraintNode leaf = plan.Root.Walk().Single(node => node.IsLeaf);

            ConstraintSeeker.Evaluate(leaf.Constraint!, harness.Probe(leaf.Tag!.Value), harness.RecordCount)
                .Count.Should().Be(filter.Count, "the count for {0}", filter.Filter);
        }
    }
}
