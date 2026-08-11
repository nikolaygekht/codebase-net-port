using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// Moving a table's cursor, over tables the corpus does not hold.
///
/// Every corpus table has thirty-two records and none of them are deleted, so the positions that
/// matter most — no records at all, one record, either end, a deleted row — are only reachable here.
/// </summary>
[Trait("Layer", "Component")]
public sealed class TableNavigationTests
{
    [Fact]
    public void ANewlyOpenedTable_IsOnNoRecord()
    {
        using Fixture fixture = new(" REC001", " REC002");

        fixture.Table.RecordNumber.Should().Be(-1);
        fixture.Table.Eof.Should().BeFalse();
        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Go_PutsTheCursorOnTheRecord()
    {
        using Fixture fixture = new(" REC001", " REC002", " REC003");

        fixture.Table.Go(2).Should().Be(GoResult.Ok);

        fixture.Table.RecordNumber.Should().Be(2);
        fixture.Table.Eof.Should().BeFalse();
        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Go_FromEndOfFile_ClearsTheEndOfFileFlag()
    {
        // A successful positioning is the only thing in the engine that lowers this flag
        // (d4go.c:326), so it is worth asserting through the surface and not only on the cursor.
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Bottom();
        fixture.Table.Skip(1);
        fixture.Table.Eof.Should().BeTrue();

        fixture.Table.Go(1).Should().Be(GoResult.Ok);

        fixture.Table.Eof.Should().BeFalse();
        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Go_FromTheBeginningFlag_ClearsIt()
    {
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Top();
        fixture.Table.Skip(-1);
        fixture.Table.Bof.Should().BeTrue();

        fixture.Table.Go(2).Should().Be(GoResult.Ok);

        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void TopAndBottom_ClearBothFlags()
    {
        using Fixture fixture = new(" REC001", " REC002");

        fixture.Table.Top();
        fixture.Table.Skip(-1);
        fixture.Table.Bof.Should().BeTrue();

        fixture.Table.Bottom().Should().Be(GoResult.Ok);
        fixture.Table.Bof.Should().BeFalse();
        fixture.Table.Eof.Should().BeFalse();

        fixture.Table.Skip(1);
        fixture.Table.Eof.Should().BeTrue();

        fixture.Table.Top().Should().Be(GoResult.Ok);
        fixture.Table.Eof.Should().BeFalse();
        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Go_PastTheEnd_ReportsNoRecordAndDoesNotSayEndOfFile()
    {
        // The fourth state: no position, and the flags say what they said before. See Decision 14.
        using Fixture fixture = new(" REC001", " REC002");

        fixture.Table.Go(3).Should().Be(GoResult.NoRecord);

        fixture.Table.RecordNumber.Should().Be(-1);
        fixture.Table.Eof.Should().BeFalse();
        fixture.Table.Bof.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Go_ToARecordNumberBelowOne_IsRefused(int recordNumber)
    {
        // A deliberate divergence. The C library accepts zero and computes a position inside the
        // header, decoding header bytes as a record. See Decision 5.
        using Fixture fixture = new(" REC001");

        Action act = () => fixture.Table.Go(recordNumber);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Top_PutsTheCursorOnTheFirstRecord()
    {
        using Fixture fixture = new(" REC001", " REC002");

        fixture.Table.Top().Should().Be(GoResult.Ok);

        fixture.Table.RecordNumber.Should().Be(1);
    }

    [Fact]
    public void Bottom_PutsTheCursorOnTheLastRecord()
    {
        using Fixture fixture = new(" REC001", " REC002", " REC003");

        fixture.Table.Bottom().Should().Be(GoResult.Ok);

        fixture.Table.RecordNumber.Should().Be(3);
    }

    [Fact]
    public void AnEmptyTable_IsAtBothEndsAtOnce()
    {
        // Not a contradiction: the C library sets both flags, and a reader that treats them as
        // exclusive answers wrongly for every empty table. See Decision 1.
        using Fixture fixture = new();

        fixture.Table.Top().Should().Be(GoResult.NoRecord);

        fixture.Table.RecordNumber.Should().Be(1);
        fixture.Table.Eof.Should().BeTrue();
        fixture.Table.Bof.Should().BeTrue();
    }

    [Fact]
    public void AnEmptyTable_HasNothingAtItsBottomEither()
    {
        using Fixture fixture = new();

        fixture.Table.Bottom().Should().Be(GoResult.NoRecord);

        fixture.Table.Eof.Should().BeTrue();
        fixture.Table.Bof.Should().BeTrue();
    }

    [Fact]
    public void Skip_MovesForwards()
    {
        using Fixture fixture = new(" REC001", " REC002", " REC003");
        fixture.Table.Top();

        fixture.Table.Skip(2).Should().Be(SkipResult.Moved);

        fixture.Table.RecordNumber.Should().Be(3);
    }

    [Fact]
    public void Skip_MovesBackwards()
    {
        using Fixture fixture = new(" REC001", " REC002", " REC003");
        fixture.Table.Bottom();

        fixture.Table.Skip(-2).Should().Be(SkipResult.Moved);

        fixture.Table.RecordNumber.Should().Be(1);
    }

    [Fact]
    public void Skip_PastTheLastRecord_ReportsEndOfFileOnePastIt()
    {
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Bottom();

        fixture.Table.Skip(1).Should().Be(SkipResult.Eof);

        fixture.Table.RecordNumber.Should().Be(3);
        fixture.Table.Eof.Should().BeTrue();
        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Skip_BackFromEndOfFile_LandsOnTheLastRecord()
    {
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Bottom();
        fixture.Table.Skip(1);

        fixture.Table.Skip(-1).Should().Be(SkipResult.Moved);

        fixture.Table.RecordNumber.Should().Be(2);
        fixture.Table.Eof.Should().BeFalse();
    }

    [Fact]
    public void Skip_BackPastTheFirstRecord_StopsOnRecordOneWhichStaysReadable()
    {
        // Not a position before the table. See Decision 3.
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Top();

        fixture.Table.Skip(-1).Should().Be(SkipResult.Bof);

        fixture.Table.RecordNumber.Should().Be(1);
        fixture.Table.Bof.Should().BeTrue();
        fixture.Table.Eof.Should().BeFalse();
    }

    [Fact]
    public void Skip_BackPastTheFirstRecordFromEndOfFile_PutsTheEndOfFileFlagBack()
    {
        // d4skip saves the flag across its move to record one and restores it, so a table can be at
        // both ends at once without being empty.
        using Fixture fixture = new(" REC001");
        fixture.Table.Top();
        fixture.Table.Skip(1);
        fixture.Table.Eof.Should().BeTrue();

        fixture.Table.Skip(-5).Should().Be(SkipResult.Bof);

        fixture.Table.RecordNumber.Should().Be(1);
        fixture.Table.Bof.Should().BeTrue();
        fixture.Table.Eof.Should().BeTrue();
    }

    [Fact]
    public void Skip_LandingOnARecord_ClearsTheBeginningFlag()
    {
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Top();
        fixture.Table.Skip(-1);
        fixture.Table.Bof.Should().BeTrue();

        fixture.Table.Skip(1).Should().Be(SkipResult.Moved);

        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Skip_OfNothing_RereadsTheCurrentRecordAndClearsTheBeginningFlag()
    {
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Top();
        fixture.Table.Skip(-1);

        fixture.Table.Skip(0).Should().Be(SkipResult.Moved);

        fixture.Table.RecordNumber.Should().Be(1);
        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Skip_FromTheBeginningFlagOffTheEnd_LeavesTheBeginningFlagCleared()
    {
        // The case that says the flag is really cleared up front rather than merely overwritten by
        // landing somewhere. A skip that ends past the last record never raises it, so if the clear
        // were missing the table would report being at both ends of a two-record table.
        using Fixture fixture = new(" REC001", " REC002");
        fixture.Table.Top();
        fixture.Table.Skip(-1);
        fixture.Table.Bof.Should().BeTrue();

        fixture.Table.Skip(5).Should().Be(SkipResult.Eof);

        fixture.Table.Eof.Should().BeTrue();
        fixture.Table.Bof.Should().BeFalse();
    }

    [Fact]
    public void Skip_OnAnEmptyTable_ReportsWhicheverEndItWasHeadedFor()
    {
        using Fixture forwards = new();
        forwards.Table.Top();
        forwards.Table.Skip(1).Should().Be(SkipResult.Eof);

        using Fixture backwards = new();
        backwards.Table.Top();
        backwards.Table.Skip(-1).Should().Be(SkipResult.Bof);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Skip_OnAnEmptyTable_LeavesItAtBothEndsWhicheverWayItWent(int count)
    {
        // The empty-table branch raises the beginning flag explicitly before deciding which end it
        // is reporting, so an empty table is still at both of its ends afterwards — even after a
        // forward skip. Without that the flag would be cleared and never put back.
        using Fixture fixture = new();
        fixture.Table.Top();

        fixture.Table.Skip(count);

        fixture.Table.Eof.Should().BeTrue();
        fixture.Table.Bof.Should().BeTrue();
    }

    [Fact]
    public void Skip_BeforePositioning_IsAnError()
    {
        // Not a silent no-op: it means the caller has not positioned yet. See Decision 6.
        using Fixture fixture = new(" REC001");

        Action act = () => fixture.Table.Skip(1);

        act.Should().Throw<CodeBaseException>().WithMessage("*needs a position*");
    }

    [Fact]
    public void Skip_AfterAFailedGo_IsAnError()
    {
        using Fixture fixture = new(" REC001");
        fixture.Table.Go(9).Should().Be(GoResult.NoRecord);

        Action act = () => fixture.Table.Skip(-1);

        act.Should().Throw<CodeBaseException>();
    }

    [Fact]
    public void ATableOfOneRecord_SkipsOffBothEnds()
    {
        using Fixture fixture = new(" ONLY01");

        fixture.Table.Top().Should().Be(GoResult.Ok);
        fixture.Table.Skip(1).Should().Be(SkipResult.Eof);
        fixture.Table.RecordNumber.Should().Be(2);
        fixture.Table.Skip(-1).Should().Be(SkipResult.Moved);
        fixture.Table.RecordNumber.Should().Be(1);
        fixture.Table.Skip(-1).Should().Be(SkipResult.Bof);
        fixture.Table.RecordNumber.Should().Be(1);
    }

    [Fact]
    public void Deleted_IsTrueForAnyFirstByteOtherThanASpace()
    {
        // The corpus has no deleted record, so this is the only place the flag is seen true. See
        // Decision 12.
        using Fixture fixture = new(" LIVE01", "*GONE02", "XODD003");

        fixture.Table.Go(1);
        fixture.Table.Deleted.Should().BeFalse();

        fixture.Table.Go(2);
        fixture.Table.Deleted.Should().BeTrue();

        fixture.Table.Go(3);
        fixture.Table.Deleted.Should().BeTrue();
    }

    [Fact]
    public void AtEndOfFile_TheRecordIsBlankRatherThanTheLastOneRead()
    {
        // Leaving the previous record in the buffer is the worst kind of wrong, because it is
        // plausible. See Decision 2.
        using Fixture fixture = new("*GONE01");
        fixture.Table.Go(1);
        fixture.Table.Deleted.Should().BeTrue();

        fixture.Table.Skip(1);

        fixture.Table.Deleted.Should().BeFalse("the blank record is not a deleted one");
    }

    [Fact]
    public void AfterAFailedGo_TheRecordIsBlankToo()
    {
        using Fixture fixture = new("*GONE01");
        fixture.Table.Go(1);

        fixture.Table.Go(2).Should().Be(GoResult.NoRecord);

        fixture.Table.Deleted.Should().BeFalse();
    }

    [Fact]
    public void AClosedTable_RefusesToMove()
    {
        Fixture fixture = new(" REC001");
        Table table = fixture.Table;
        fixture.Dispose();

        table.Invoking(t => t.Go(1)).Should().Throw<ObjectDisposedException>();
        table.Invoking(t => t.Top()).Should().Throw<ObjectDisposedException>();
        table.Invoking(t => t.Bottom()).Should().Throw<ObjectDisposedException>();
        table.Invoking(t => t.Skip(1)).Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    /// An open table over a hand-built image, closed with the test.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly CodeBaseEngine engine;

        public Fixture(params string[] records)
        {
            (engine, Table) = TableImage.Open(records);
        }

        public Table Table { get; }

        public void Dispose() => engine.Dispose();
    }
}
