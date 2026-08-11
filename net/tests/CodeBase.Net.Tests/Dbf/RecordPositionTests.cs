using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// The cursor as a state machine, checked at every edge the corpus cannot reach.
///
/// Every corpus table holds thirty-two records, so the interesting positions — an empty table, one
/// record, either end — exist only here. These are also the transitions a reader is most likely to
/// get subtly wrong, because the flags are stored rather than derived and two of them are set in
/// combinations that look impossible until the C library is read.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class RecordPositionTests
{
    [Fact]
    public void ANewPosition_IsOnNoRecord()
    {
        RecordPosition position = new();

        position.Number.Should().Be(RecordPosition.Invalid);
        position.IsOnRecord.Should().BeFalse();
        position.Eof.Should().BeFalse();
        position.Bof.Should().BeFalse();
    }

    [Fact]
    public void MovedTo_PutsTheCursorOnTheRecord()
    {
        RecordPosition position = new();

        position.MovedTo(7);

        position.Number.Should().Be(7);
        position.IsOnRecord.Should().BeTrue();
        position.Eof.Should().BeFalse();
        position.Bof.Should().BeFalse();
    }

    [Fact]
    public void MovedTo_ClearsBothFlags()
    {
        // The only thing in the engine that clears either flag, which is why being at the beginning
        // survives every other call. See Decision 1.
        RecordPosition position = new();
        position.MovedPastEnd(5);
        position.MovedBeforeStart(endOfFileBefore: true);

        position.MovedTo(3);

        position.Eof.Should().BeFalse();
        position.Bof.Should().BeFalse();
    }

    [Fact]
    public void ClearBof_LowersTheFlagWithoutMovingTheCursor()
    {
        // A skip does this before it decides where to go (d4skip.c:1149), which is why a skip that
        // lands anywhere but the front leaves the flag clear.
        RecordPosition position = new();
        position.MovedTo(1);
        position.MovedBeforeStart(endOfFileBefore: false);

        position.ClearBof();

        position.Bof.Should().BeFalse();
        position.Number.Should().Be(1, "clearing the flag is not a move");
    }

    [Fact]
    public void MovedPastEnd_LeavesTheBeginningFlagAloneOnATableThatHasRecords()
    {
        // It raises the flag only when the end-of-file position is record one, which is to say only
        // on an empty table. It never lowers it, so whatever a skip left is what survives. That one
        // rule is why the C library's two explicit flag-raises on the empty path are redundant.
        RecordPosition position = new();
        position.MovedTo(1);
        position.MovedBeforeStart(endOfFileBefore: false);

        position.MovedPastEnd(5);

        position.Bof.Should().BeTrue("nothing here clears it");

        position.ClearBof();
        position.MovedPastEnd(5);

        position.Bof.Should().BeFalse("and nothing here raises it either");
    }

    [Fact]
    public void MovedPastEnd_ReportsOnePastTheLastRecord()
    {
        RecordPosition position = new();

        position.MovedPastEnd(32).Should().Be(33);

        position.Number.Should().Be(33);
        position.Eof.Should().BeTrue();
        position.Bof.Should().BeFalse();
        position.IsOnRecord.Should().BeFalse();
    }

    [Fact]
    public void MovedPastEnd_OnAnEmptyTable_IsAtBothEndsAtOnce()
    {
        // An empty table's end-of-file position is record one, and d4goEof sets the beginning flag
        // there too. A reader that treats the two as exclusive gets this wrong. See Decision 1.
        RecordPosition position = new();

        position.MovedPastEnd(0).Should().Be(1);

        position.Eof.Should().BeTrue();
        position.Bof.Should().BeTrue();
        position.IsOnRecord.Should().BeFalse();
    }

    [Fact]
    public void MovedBeforeStart_LeavesTheCursorOnRecordOne()
    {
        // Skipping back past the start does not move to a position before everything: it stays on
        // record one, which stays readable. See Decision 3.
        RecordPosition position = new();
        position.MovedTo(1);

        position.MovedBeforeStart(endOfFileBefore: false);

        position.Number.Should().Be(1);
        position.Bof.Should().BeTrue();
        position.Eof.Should().BeFalse();
        position.IsOnRecord.Should().BeTrue();
    }

    [Fact]
    public void MovedBeforeStart_RestoresTheEndOfFileFlagItFound()
    {
        // d4skip saves the flag across its move to record one and puts it back, so a table that was
        // at end of file is still at end of file after a backwards skip that ran out.
        RecordPosition position = new();
        position.MovedTo(1);

        position.MovedBeforeStart(endOfFileBefore: true);

        position.Bof.Should().BeTrue();
        position.Eof.Should().BeTrue();
    }

    [Fact]
    public void Invalidate_ReportsNoRecordAndLeavesTheFlagsAlone()
    {
        // The fourth state. Falling off the end through a direct positioning is not end of file:
        // the flags say what they said before. See Decision 14.
        RecordPosition position = new();
        position.MovedTo(4);

        position.Invalidate();

        position.Number.Should().Be(RecordPosition.Invalid);
        position.IsOnRecord.Should().BeFalse();
        position.Eof.Should().BeFalse();
        position.Bof.Should().BeFalse();
    }

    [Fact]
    public void Invalidate_AfterEndOfFile_KeepsTheEndOfFileFlag()
    {
        RecordPosition position = new();
        position.MovedPastEnd(10);

        position.Invalidate();

        position.Number.Should().Be(RecordPosition.Invalid);
        position.Eof.Should().BeTrue();
    }

    [Fact]
    public void ATableOfOneRecord_SkippedForwardsThenBackwards_EndsAtBothFlagsInTurn()
    {
        RecordPosition position = new();

        position.MovedTo(1);
        position.Eof.Should().BeFalse();
        position.Bof.Should().BeFalse();

        position.MovedPastEnd(1);
        position.Number.Should().Be(2);
        position.Eof.Should().BeTrue();
        position.Bof.Should().BeFalse();

        position.MovedTo(1);
        position.MovedBeforeStart(endOfFileBefore: false);
        position.Number.Should().Be(1);
        position.Bof.Should().BeTrue();
        position.Eof.Should().BeFalse();
    }
}
