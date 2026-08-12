using AwesomeAssertions;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// A table navigating in a tag's order: the coupling between the two cursors.
///
/// Layer two and three. What these catch that the corpus cannot: an index entry naming a record the table
/// does not have, both ends of a two-record table, a declared index with no file, a tag whose expression
/// cannot be typed, and the promise that selecting a tag does not move the cursor. Every corpus index is
/// consistent with its table and every corpus table has thirty-two records or more, so none of it is
/// reachable from there.
/// </summary>
[Trait("Layer", "Component")]
public sealed class TableTagsTests
{
    /// <summary>
    /// Three records stored in the reverse of their alphabetical order, with a well-formed index over
    /// them — so the tag's order is 3, 2, 1 and cannot be confused with record order.
    /// </summary>
    private static (CodeBaseEngine Engine, Table Table) Reversed() =>
        IndexedTableImage.Open(
            ["CHARL", "BRAVO", "ALPHA"],
            [("ALPHA", 3), ("BRAVO", 2), ("CHARL", 1)]);

    [Fact]
    public void Open_ATableDeclaringAnIndexReportsItsTags()
    {
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.HasIndex.Should().BeTrue();
            table.Tags.Should().HaveCount(1);
            table.Tags[0].Expression.Should().Be("TEXT");
            table.Tags[0].KeyLength.Should().Be(IndexedTableImage.FieldWidth);
        }
    }

    [Fact]
    public void SelectTag_DoesNotMoveTheCursor()
    {
        // A caller that selects a tag mid-walk must not jump. The next Top or Skip is what re-positions,
        // exactly as in the C library.
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.Go(2);

            table.SelectTag(table.Tags[0]);

            table.RecordNumber.Should().Be(2);
            table.SelectedTag.Should().NotBeNull();
        }
    }

    [Fact]
    public void Walking_ASelectedTagFollowsTheIndexOrderAndNotTheRecordOrder()
    {
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);

            List<int> walked = [];
            for (GoResult go = table.Top(); go == GoResult.Ok; )
            {
                walked.Add(table.RecordNumber);
                if (table.Skip(1) != SkipResult.Moved)
                    break;
            }

            walked.Should().Equal([3, 2, 1]);
        }
    }

    [Fact]
    public void SelectTag_WithNullReturnsToRecordOrder()
    {
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            table.Top().Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(3);

            table.SelectTag(null);

            table.SelectedTag.Should().BeNull();
            table.Top().Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);
        }
    }

    [Fact]
    public void Go_IgnoresTheSelection()
    {
        // A record number is a record number whatever tag is selected, and that asymmetry with Top and
        // Skip is worth stating.
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);

            table.Go(1).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);
        }
    }

    [Fact]
    public void Skipping_FromARecordReachedByNumberContinuesInTagOrder()
    {
        // The two cursors can drift apart, because Go moves one and not the other. Stepping has to
        // continue from where the *record* is, or a caller mixing the two gets nonsense.
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            table.Go(2);

            table.Skip(1).Should().Be(SkipResult.Moved);
            table.RecordNumber.Should().Be(1, "CHARL follows BRAVO in this tag's order");
        }
    }

    [Fact]
    public void Skipping_OffTheEndOfATagLeavesNoRecordToRead()
    {
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);

            table.Bottom().Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1, "the tag ends on CHARL, which is record one");

            table.Skip(1).Should().Be(SkipResult.Eof);
            table.Eof.Should().BeTrue();
            table.Bof.Should().BeFalse("running out going forward is one end, not both");
            table.RecordNumber.Should().Be(table.RecordCount + 1);
        }
    }

    [Fact]
    public void Skipping_BackPastTheFirstEntryStaysOnItAndReportsTheBeginning()
    {
        // The shape a backwards skip has in record order too: the record stays readable and only the flag
        // says the skip could not be made (d4skip.c:1343-1354).
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            table.Top();

            table.Skip(-1).Should().Be(SkipResult.Bof);

            table.RecordNumber.Should().Be(3, "ALPHA is the tag's first entry, and it names record three");
            table.Bof.Should().BeTrue();
            table.Eof.Should().BeFalse();
        }
    }

    [Fact]
    public void Skipping_BackwardsFromTheEndComesInThroughTheTagsLastRecord()
    {
        // At end of file a backwards skip re-enters through the bottom, and reaching it counts as one of
        // the steps (d4skip.c:1208-1225). The forward direction has no such courtesy.
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            table.Bottom();
            table.Skip(1).Should().Be(SkipResult.Eof);

            table.Skip(-1).Should().Be(SkipResult.Moved);
            table.RecordNumber.Should().Be(1, "the tag's last entry, reached as the one step asked for");

            table.Skip(1).Should().Be(SkipResult.Eof);
            table.Skip(-2).Should().Be(SkipResult.Moved);
            table.RecordNumber.Should().Be(2, "one step to the bottom and one more back from it");

            table.Skip(1).Should().Be(SkipResult.Moved);
            table.Skip(1).Should().Be(SkipResult.Eof);
            table.Skip(1).Should().Be(SkipResult.Eof, "forwards from the end stays at the end");
        }
    }

    [Fact]
    public void Skip_ByZeroStaysWhereItIs()
    {
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            table.Top();
            table.Skip(1);

            table.Skip(0).Should().Be(SkipResult.Moved);
            table.RecordNumber.Should().Be(2);
        }
    }

    [Fact]
    public void Walking_AnEntryNamingARecordTheTableDoesNotHaveIsSteppedOver()
    {
        // Legitimate under concurrency — another process may add a key before the record — and a file the
        // C library reads, so refusing it would be a divergence. The entry for record 9 does not exist in
        // a two-record table (d4skip.c:1296-1308).
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["ALPHA", "BRAVO"],
            [("ALPHA", 1), ("BEFORE", 9), ("BRAVO", 2)]);

        using (engine)
        {
            table.SelectTag(table.Tags[0]);

            List<int> walked = [];
            for (GoResult go = table.Top(); go == GoResult.Ok; )
            {
                walked.Add(table.RecordNumber);
                if (table.Skip(1) != SkipResult.Moved)
                    break;
            }

            walked.Should().Equal([1, 2], "the entry pointing past the end was skipped, not refused");
        }
    }

    [Fact]
    public void TheExplicitFormWalksWithoutTouchingTheSelection()
    {
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            Tag tag = table.Tags[0];

            List<int> walked = [];
            for (GoResult go = table.GoFirstIndexed(tag); go == GoResult.Ok; go = table.GoNextIndexed(tag))
                walked.Add(table.RecordNumber);

            walked.Should().Equal([3, 2, 1]);
            table.SelectedTag.Should().BeNull("naming the tag per call leaves the mode alone");
        }
    }

    [Fact]
    public void TheExplicitFormReachesBothEnds()
    {
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            Tag tag = table.Tags[0];

            table.GoFirstIndexed(tag).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(3);

            table.GoLastIndexed(tag).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);

            table.GoNextIndexed(tag).Should().Be(GoResult.NoRecord, "the tag has no more entries");
        }
    }

    [Fact]
    public void TheTwoFormsCanBeMixedInOneWalk()
    {
        // They share a cursor per tag, so a walk begun with one continues with the other. That is the
        // point of having both rather than two parallel implementations.
        (CodeBaseEngine engine, Table table) = Reversed();
        using (engine)
        {
            Tag tag = table.Tags[0];

            table.GoFirstIndexed(tag);
            table.SelectTag(tag);

            table.Skip(1).Should().Be(SkipResult.Moved);
            table.RecordNumber.Should().Be(2);

            table.GoNextIndexed(tag).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);
        }
    }

    [Fact]
    public void ADescendingTagIsWalkedInItsOwnOrder()
    {
        // The index below is in ascending key order; the flag is what reverses the walk.
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["ALPHA", "BRAVO", "CHARL"],
            [("ALPHA", 1), ("BRAVO", 2), ("CHARL", 3)],
            descending: true);

        using (engine)
        {
            Tag tag = table.Tags[0];
            tag.Descending.Should().BeTrue();

            List<int> walked = [];
            for (GoResult go = table.GoFirstIndexed(tag); go == GoResult.Ok; go = table.GoNextIndexed(tag))
                walked.Add(table.RecordNumber);

            walked.Should().Equal([3, 2, 1]);
        }
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void Skipping_FromARecordTheTagDoesNotListIsRefusedRatherThanAnswered()
    {
        // A filtered or unique tag leaves records out, and the C library carries on from the nearest key by
        // re-deriving the record's own key through the expression. Without the expression engine there is
        // no honest answer here, and end of file would be a plausible-looking wrong one (ADR-28).
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["ALPHA", "BRAVO"],
            [("ALPHA", 1)]);

        using (engine)
        {
            table.SelectTag(table.Tags[0]);
            table.Go(2);

            Action act = () => table.Skip(1);

            act.Should().Throw<CodeBaseException>()
                .Where(e => e.Code == ErrorCode.NotSupported)
                .WithMessage("*Record 2*TEXT*");

            // The tag's own records are still reachable, and so is record order.
            table.Top().Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1);
        }
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void Open_ATableDeclaringAnIndexThatIsNotThereFails()
    {
        // The same rule a declared memo file follows: a table that opened without its index would
        // navigate in record order and answer differently, with nothing saying why.
        using CodeBaseEngine engine = IndexedTableImage.WithoutTheIndexFile("ALPHA");

        Action act = () => engine.OpenTable("memory.dbf");

        act.Should().Throw<CodeBaseException>().WithMessage("*index*");
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void SelectTag_ATagWhoseExpressionCannotBeTypedIsRefusedWhenItIsUsed()
    {
        // Refused at selection and not at open, so one exotic tag does not make a table unopenable
        // (ADR-28). The table itself, and any other tag, stays perfectly usable.
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            ["ALPHA"],
            [("ALPHA", 1)],
            expression: "UPPER(TEXT)");

        using (engine)
        {
            table.HasIndex.Should().BeTrue("the file opened; only the tag is unusable");
            table.Tags.Should().HaveCount(1);

            Action act = () => table.SelectTag(table.Tags[0]);

            act.Should().Throw<CodeBaseException>().WithMessage("*UPPER(TEXT)*");

            // And record order still works, which is the point of refusing late.
            table.Top().Should().Be(GoResult.Ok);
        }
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void SelectTag_ATagFromAnotherTableIsRefused()
    {
        (CodeBaseEngine first, Table one) = Reversed();
        (CodeBaseEngine second, Table two) = Reversed();

        using (first)
        using (second)
        {
            // Same name, same expression, same shape — and still not this table's tag. Identity is what
            // decides, because a tag carries a cursor into one particular file.
            Action act = () => two.SelectTag(one.Tags[0]);

            act.Should().Throw<CodeBaseException>().WithMessage("*does not belong*");
        }
    }
}
