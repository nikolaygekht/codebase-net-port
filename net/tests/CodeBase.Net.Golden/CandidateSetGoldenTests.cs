using AwesomeAssertions;
using CodeBase.Net.Query;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Depth 3 of the query gate: the candidate set this port builds holds the records the reference's
/// own bitmap holds, for every filter of the corpus.
///
/// This is the assertion the whole step is arranged around. It compares the set [b]before[/b] any
/// residual filtering, so a decomposition error cannot be hidden by a later pass that happens to
/// remove the same records -- and it runs before merging exists, so what it measures is
/// decomposition, seeking and combination and nothing else.
///
/// It compares the records the two sets hold rather than the bits they store. The reference flags the
/// complement of a range covering more than half a tag, and this port only ever does so for an
/// exclusion; that is a decision about cost, not about which records answer the filter.
///
/// One filter is measured against the reference's [i]scan[/i] rather than its bitmap, and the dump
/// says which. Where the reference's two readings of a filter disagree it has demonstrated its own
/// range-inverting defect on that filter, and this port implements what was meant instead. Reading
/// that from the dump rather than naming the filter here is what stops the exception widening.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CandidateSetGoldenTests
{
    public static TheoryData<string> Tables => ["CDXEXPR", "CDXBASE", "CDXDEEP"];

    /// <summary>
    /// The gate.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void TheCandidateSetHoldsWhatTheReferenceBitmapHolds(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases.Where(c => c.Agrees))
        {
            harness.Reach(filter.Filter).Records().Should().Equal(
                filter.Set!.Effective(harness.Dump.RecordCount),
                "the candidate set for {0}", filter.Filter);
        }
    }

    /// <summary>
    /// Where the reference contradicts itself, the port answers what the filter means -- which is
    /// what its own scan returned once its optimizer was taken out of the circuit.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void WhereTheReferenceContradictsItselfThePortAnswersTheScan(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases.Where(c => !c.Agrees))
        {
            // The one filter this reaches is a conjunction whose other term no tag answers, so the
            // candidate set is a superset that the residual check will narrow. What matters here is
            // that it has not LOST a record: the reference's optimized answer did, and that is the
            // defect. A superset is recovered downstream; a missing record never is.
            harness.Plan(filter.Filter).FullyMapped.Should().BeFalse(
                "{0} has a term no tag answers", filter.Filter);

            harness.Reach(filter.Filter).Records().Should().Contain(
                filter.Scanned, "the candidate set for {0} must not have lost a record", filter.Filter);
        }
    }

    /// <summary>
    /// A filter nothing could be optimized in admits every record, which is what makes the fall-back
    /// need no special case at any call site above.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void AFilterWithNothingToSeekAdmitsEveryRecord(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases.Where(c => !c.Optimized_Anything))
        {
            harness.Reach(filter.Filter).Count.Should().Be(
                harness.RecordCount, "nothing in {0} could be seeked", filter.Filter);
        }
    }

    /// <summary>
    /// A fully mapped plan needs no further checking, so its candidate set is exactly the answer and
    /// its size is the count the reference reported.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void AFullyMappedPlanCountsWhatTheReferenceReturned(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);
        int seen = 0;

        foreach (DumpQueryCase filter in harness.Dump.Cases.Where(c => c.FullyMapped && c.Agrees))
        {
            seen++;
            harness.Reach(filter.Filter).Count.Should().Be(
                filter.Count, "the count for {0}", filter.Filter);
        }

        seen.Should().BeGreaterThan(0);
    }
}
