using AwesomeAssertions;
using CodeBase.Net.Queries;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// The bitset, including the two things about it that are easy to get wrong.
///
/// Record numbers count from one, so bit zero is never a record; and the last byte of the set runs
/// past the end of the table, so its spare bits are not records either. Both matter only when a set
/// stands for its complement, which is when they would otherwise come back set and be counted as
/// records that do not exist.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class RecordSetTests
{
    [Fact]
    public void AnEmptySetHoldsNothing()
    {
        RecordSet set = RecordSet.Empty(10);

        set.Count.Should().Be(0);
        set.Records().Should().BeEmpty();
        set.NextFrom(1).Should().Be(0);
    }

    [Fact]
    public void AFullSetHoldsEveryRecordAndNoOthers()
    {
        RecordSet set = RecordSet.All(10);

        set.Records().Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        set.Contains(0).Should().BeFalse("there is no record zero");
        set.Contains(11).Should().BeFalse("the table ends at ten");
    }

    /// <summary>
    /// Complementing an empty set yields every record and no others, at every width around a byte
    /// boundary. What this catches is an off-by-one in the complement, not the spare bits in the last
    /// byte -- those are unobservable, because reading the set always goes through its range check.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(17)]
    public void ComplementingAnEmptySetYieldsEveryRecord(int records)
    {
        RecordSet set = RecordSet.Empty(records);
        set.MeaningTheRest();

        set.Records().Should().Equal(Enumerable.Range(1, records));
        set.Count.Should().Be(records);
    }

    [Fact]
    public void FlaggingWhileComplementedRemovesTheRecord()
    {
        RecordSet set = RecordSet.Empty(5);

        set.Flag(3);
        set.MeaningTheRest();

        set.Records().Should().Equal(1, 2, 4, 5);
    }

    [Fact]
    public void ARecordPastTheEndIsDiscardedRatherThanRefused()
    {
        RecordSet set = RecordSet.Empty(4);

        set.Flag(99);
        set.Flag(0);

        set.Count.Should().Be(0);
    }

    [Fact]
    public void KeepingBothLeavesOnlyWhatBothHold()
    {
        RecordSet left = RecordSet.Empty(8);
        RecordSet right = RecordSet.Empty(8);

        foreach (int record in new[] { 1, 2, 3, 4 })
            left.Flag(record);
        foreach (int record in new[] { 3, 4, 5 })
            right.Flag(record);

        left.KeepBoth(right);
        left.Records().Should().Equal(3, 4);
    }

    [Fact]
    public void KeepingEitherLeavesWhatEitherHolds()
    {
        RecordSet left = RecordSet.Empty(8);
        RecordSet right = RecordSet.Empty(8);

        left.Flag(1);
        right.Flag(5);

        left.KeepEither(right);
        left.Records().Should().Equal(1, 5);
    }

    /// <summary>
    /// Combining normalises a complement away first, so a set built the cheap way answers the same as
    /// one built the plain way. This is the case that would go wrong if the flag were carried into
    /// the combination instead.
    /// </summary>
    [Fact]
    public void CombiningWorksWhenEitherSideStandsForItsComplement()
    {
        RecordSet allButThree = RecordSet.Empty(6);
        allButThree.Flag(3);
        allButThree.MeaningTheRest();

        RecordSet lowHalf = RecordSet.Empty(6);
        foreach (int record in new[] { 1, 2, 3 })
            lowHalf.Flag(record);

        allButThree.KeepBoth(lowHalf);
        allButThree.Records().Should().Equal(1, 2);
    }

    [Fact]
    public void NextFromSkipsRunsOfRecordsTheSetDoesNotHold()
    {
        RecordSet set = RecordSet.Empty(100);
        set.Flag(7);
        set.Flag(64);

        set.NextFrom(1).Should().Be(7);
        set.NextFrom(8).Should().Be(64);
        set.NextFrom(65).Should().Be(0);
    }
}
