using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The five seek operations over hand-built trees.
///
/// Layer two. What these catch that the corpus cannot: the ends of a tag reached deliberately rather than
/// by luck of the data, a run of equal keys that straddles a leaf boundary, and the difference between the
/// two operations that agree whenever the value is present — [c]SeekAtOrBefore[/c] and [c]SeekLast[/c]
/// differ only where it is absent, so a suite whose cases all hit would never tell them apart.
/// </summary>
[Trait("Layer", "Component")]
public sealed class SeekTests
{
    private static byte Spaces(IndexHeader header) => KeyPadding.Space;

    [Fact]
    public void Seek_FindsAKeyTwoLevelsDown()
    {
        using IndexFileReader index = TwoLevelTree();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "DELTA")).Should().Be(SeekOutcome.Found);
        cursor.Current.Record.Should().Be(4);
    }

    [Fact]
    public void Seek_AValueBetweenTwoKeysLandsOnTheGreaterAndSaysSo()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "BS")).Should().Be(SeekOutcome.After);
        cursor.Current.Record.Should().Be(3, "CHARLIE is the first key above BS");
    }

    [Fact]
    public void Seek_AValueAboveEverythingIsEndOfFile()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "ZZZZ")).Should().Be(SeekOutcome.Eof);
        cursor.Eof.Should().BeTrue();
    }

    [Fact]
    public void Seek_AValueBelowEverythingLandsOnTheFirstKey()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "AA")).Should().Be(SeekOutcome.After);
        cursor.Current.Record.Should().Be(1);
    }

    [Fact]
    public void Seek_CrossesIntoTheNextLeafWhenTheAnswerIsNotInThisOne()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        // BRAVO is the last key of the first leaf, so anything above it must be found in the second.
        cursor.Seek(Search(index, "CHARLIE")).Should().Be(SeekOutcome.Found);
        cursor.Current.Record.Should().Be(3);
    }

    [Fact]
    public void SeekAtOrBefore_LandsOnTheLesserKeyWhereSeekLandsOnTheGreater()
    {
        using IndexFileReader index = TwoLeaves();
        KeySearch search = Search(index, "BS");

        TagCursor ahead = index.Tags[0].OpenCursor();
        TagCursor behind = index.Tags[0].OpenCursor();

        ahead.Seek(search).Should().Be(SeekOutcome.After);
        behind.SeekAtOrBefore(search).Should().Be(SeekOutcome.Before);

        ahead.Current.Record.Should().Be(3, "CHARLIE");
        behind.Current.Record.Should().Be(2, "BRAVO — the two are neighbours with the value between them");
    }

    [Fact]
    public void SeekAtOrBefore_AValueBelowEverythingIsBeginningOfFile()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.SeekAtOrBefore(Search(index, "AA")).Should().Be(SeekOutcome.Bof);
    }

    [Fact]
    public void SeekAtOrBefore_AValueAboveEverythingLandsOnTheLastKey()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.SeekAtOrBefore(Search(index, "ZZZZ")).Should().Be(SeekOutcome.Before);
        cursor.Current.Record.Should().Be(4, "DELTA is the last key");
    }

    [Fact]
    public void SeekAtOrBefore_AndSeekLast_DifferOnlyWhenTheValueIsAbsent()
    {
        // The pair that would otherwise be indistinguishable: on a value that is present they agree, and
        // on one that is not, the first reports the predecessor while the second reports nothing.
        using IndexFileReader index = TwoLeaves();
        KeySearch present = Search(index, "BRAVO");
        KeySearch absent = Search(index, "BS");

        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.SeekAtOrBefore(present).Should().Be(SeekOutcome.Found);
        cursor.SeekLast(present).Should().Be(SeekOutcome.Found);

        cursor.SeekAtOrBefore(absent).Should().Be(SeekOutcome.Before);
        cursor.SeekLast(absent).Should().Be(SeekOutcome.NoEntry);
    }

    [Fact]
    public void SeekLast_LandsOnTheLastOfARunWhereSeekLandsOnTheFirst()
    {
        using IndexFileReader index = RunAcrossTwoLeaves();
        KeySearch search = Search(index, "SAME");

        TagCursor first = index.Tags[0].OpenCursor();
        TagCursor last = index.Tags[0].OpenCursor();

        first.Seek(search).Should().Be(SeekOutcome.Found);
        last.SeekLast(search).Should().Be(SeekOutcome.Found);

        first.Current.Record.Should().Be(1);
        last.Current.Record.Should().Be(5, "the run crosses the leaf boundary and ends in the second leaf");
    }

    [Fact]
    public void SeekNext_WalksARunOnceEachAndThenReportsNoEntry()
    {
        using IndexFileReader index = RunAcrossTwoLeaves();
        KeySearch search = Search(index, "SAME");
        TagCursor cursor = index.Tags[0].OpenCursor();

        List<uint> visited = [];
        SeekOutcome outcome = cursor.Seek(search);

        for (; outcome == SeekOutcome.Found; outcome = cursor.SeekNext(search))
            visited.Add(cursor.Current.Record);

        visited.Should().Equal([1u, 2u, 3u, 4u, 5u]);
        outcome.Should().Be(SeekOutcome.NoEntry, "the run ended on the entry that ended it");

        // Calling it again does *not* report NoEntry a second time: the cursor is now on a
        // non-matching entry, and from there the operation degrades to a fresh seek by design.
        cursor.SeekNext(search).Should().Be(SeekOutcome.Found);
    }

    [Fact]
    public void SeekPrevious_WalksTheSameRunBackwards()
    {
        using IndexFileReader index = RunAcrossTwoLeaves();
        KeySearch search = Search(index, "SAME");
        TagCursor cursor = index.Tags[0].OpenCursor();

        List<uint> visited = [];
        for (SeekOutcome o = cursor.SeekLast(search); o == SeekOutcome.Found; o = cursor.SeekPrevious(search))
            visited.Add(cursor.Current.Record);

        visited.Should().Equal(5u, 4u, 3u, 2u, 1u);
    }

    [Fact]
    public void SeekNext_OnANonMatchingEntryDegradesToAPlainSeek()
    {
        // The C library's own behaviour, reproduced rather than tidied (D4SEEK.C:1195-1210): it makes the
        // operation safe to call without knowing where the cursor is.
        using IndexFileReader index = RunAcrossTwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Top();
        cursor.Current.Record.Should().Be(1);

        cursor.SeekNext(Search(index, "ZULU")).Should().Be(SeekOutcome.Found);
        cursor.Current.Record.Should().Be(6, "the search started over rather than reporting nothing");
    }

    [Fact]
    public void SeekNext_OnACursorThatWasNeverPositionedSeeksInstead()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.SeekNext(Search(index, "CHARLIE")).Should().Be(SeekOutcome.Found);
        cursor.Current.Record.Should().Be(3);
    }

    [Fact]
    public void SeekExact_FindsOneEntryOfARunByItsRecordNumber()
    {
        using IndexFileReader index = RunAcrossTwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.SeekExact(Search(index, "SAME"), 4).Should().Be(SeekOutcome.Found);
        cursor.Current.Record.Should().Be(4);
    }

    [Fact]
    public void SeekExact_ARecordThatIsNotInTheRunIsReportedRatherThanGuessed()
    {
        using IndexFileReader index = RunAcrossTwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.SeekExact(Search(index, "SAME"), 99).Should().NotBe(SeekOutcome.Found);
    }

    [Fact]
    public void Seeking_ThenSteppingUnconditionally_WalksTheRestOfTheTag()
    {
        // The composition that makes a range scan: seek the low bound, then step until the high bound
        // goes past. The step is the unbounded one, which leaves the matching run on purpose.
        using IndexFileReader index = RunAcrossTwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "SAME")).Should().Be(SeekOutcome.Found);

        List<uint> tail = [cursor.Current.Record];
        while (cursor.Next())
            tail.Add(cursor.Current.Record);

        tail.Should().Equal(
            [1u, 2u, 3u, 4u, 5u, 6u], "the walk crossed out of the run and carried on");
    }

    [Fact]
    public void Stepping_BackFromAnEndOfFileLandingReEntersTheTag()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "ZZZZ")).Should().Be(SeekOutcome.Eof);

        cursor.Previous().Should().BeTrue("a cursor past the end can step back into the tag");
        cursor.Current.Record.Should().Be(4);
    }

    [Fact]
    public void Stepping_ForwardFromABeginningOfFileLandingReEntersTheTag()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.SeekAtOrBefore(Search(index, "AA")).Should().Be(SeekOutcome.Bof);

        cursor.Next().Should().BeTrue("a cursor before the start can step forward into the tag");
        cursor.Current.Record.Should().Be(1);
    }

    [Fact]
    public void Seek_ADescendingTagIsSearchedInItsOwnOrder()
    {
        using IndexFileReader index = TwoLeaves(descending: true);
        TagCursor cursor = index.Tags[0].OpenCursor();

        // The tag runs DELTA, CHARLIE, BRAVO, ALPHA, so a value between BRAVO and CHARLIE lands on
        // BRAVO — the first entry *after* the value in the tag's order.
        cursor.Seek(Search(index, "BS")).Should().Be(SeekOutcome.After);
        cursor.Current.Record.Should().Be(2);

        // A value above every key sorts at the *start* of a descending tag, so the first entry is where
        // this lands — not end of file. End of file is what the corpus pins for a value that cannot be
        // incremented at all (all 0xFF), which is a different case and takes a different branch.
        cursor.Seek(Search(index, "ZZZZ")).Should().Be(SeekOutcome.After);
        cursor.Current.Record.Should().Be(4, "DELTA is the greatest key and so the tag's first");

        cursor.Seek(Search(index, "AA")).Should().Be(SeekOutcome.Eof);
        cursor.Eof.Should().BeTrue("a value below everything is past the end of a descending tag");
    }

    [Fact]
    public void SeekAtOrBefore_OnADescendingTagIsTheMirrorOfSeek()
    {
        using IndexFileReader index = TwoLeaves(descending: true);
        KeySearch search = Search(index, "BS");

        TagCursor ahead = index.Tags[0].OpenCursor();
        TagCursor behind = index.Tags[0].OpenCursor();

        ahead.Seek(search);
        behind.SeekAtOrBefore(search).Should().Be(SeekOutcome.Before);

        ahead.Current.Record.Should().Be(2, "BRAVO comes after BS in a descending tag");
        behind.Current.Record.Should().Be(3, "CHARLIE comes before it");
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void Seek_ATreeWhoseBranchPointsOutsideTheFileIsRefused()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(1))
            .WithLeaf(8, [("ALPHA", 1)], attribute: 2)
            .WithBranch(8, [("ZULU", 1u, 0x7F00u)])
            .Build();

        using IndexFileReader index = IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
        TagCursor cursor = index.Tags[0].OpenCursor();

        Action act = () => cursor.Seek(Search(index, "ALPHA"));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void Seek_ATreeThatDoesNotReachALeafIsRefused()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithBranch(8, [("ZULU", 1, IndexImage.NodeOf(0))])
            .Build();

        using IndexFileReader index = IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
        TagCursor cursor = index.Tags[0].OpenCursor();

        Action act = () => cursor.Seek(Search(index, "ALPHA"));

        act.Should().Throw<CodeBaseException>().WithMessage("*cycle*");
    }

    [Fact]
    public void Seek_AnEmptyTagFindsNothing()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [])
            .Build();

        using IndexFileReader index = IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "ALPHA")).Should().Be(SeekOutcome.Eof);
        cursor.SeekAtOrBefore(Search(index, "ALPHA")).Should().Be(SeekOutcome.Bof);
    }

    [Fact]
    public void Seek_ADescentThatLandsOnAKeylessLeafFindsTheNextOneAlong()
    {
        // A branch can point at a leaf a delete emptied. Walking already steps over such a block
        // (CDX-FORMAT.md section 14, item 10); a descent used to stop on it and report end of file,
        // which would hide every key past the gap from a seek while a walk still found them.
        using IndexFileReader index = BranchOverAnEmptyMiddleLeaf();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Seek(Search(index, "MIKE")).Should().Be(SeekOutcome.After);

        cursor.Eof.Should().BeFalse();
        cursor.Current.Record.Should().Be(9, "ZULU is the first key at or above MIKE");
    }

    private static KeySearch Search(IndexFileReader index, string value) =>
        KeySearch.For(
            System.Text.Encoding.Latin1.GetBytes(value),
            index.Tags[0].KeyLength,
            index.Tags[0].PadByte);

    /// <summary>Two leaves joined by their siblings, with a root branch above them.</summary>
    private static IndexFileReader TwoLeaves(bool descending = false)
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(2), descending: descending)
            .WithLeaf(8, [("ALPHA", 1), ("BRAVO", 2)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [("CHARLIE", 3), ("DELTA", 4)], attribute: 2, left: IndexImage.NodeOf(0))
            .WithBranch(8, [("BRAVO", 2, IndexImage.NodeOf(0)), ("DELTA", 4, IndexImage.NodeOf(1))])
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }

    /// <summary>A run of five equal keys spanning two leaves, with one greater key after it.</summary>
    private static IndexFileReader RunAcrossTwoLeaves()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(2))
            .WithLeaf(8, [("SAME", 1), ("SAME", 2), ("SAME", 3)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [("SAME", 4), ("SAME", 5), ("ZULU", 6)], attribute: 2, left: IndexImage.NodeOf(0))
            .WithBranch(8, [("SAME", 3, IndexImage.NodeOf(0)), ("ZULU", 6, IndexImage.NodeOf(1))])
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }

    /// <summary>Two leaves under a middle branch, so a descent goes down twice.</summary>
    private static IndexFileReader TwoLevelTree()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(4))
            .WithLeaf(8, [("ALPHA", 1), ("BRAVO", 2)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [("CHARLIE", 3), ("DELTA", 4)], attribute: 2, left: IndexImage.NodeOf(0))
            .WithBranch(8, [("BRAVO", 2, IndexImage.NodeOf(0))], attribute: 0)
            .WithBranch(8, [("DELTA", 4, IndexImage.NodeOf(1))], attribute: 0)
            .WithBranch(8, [("BRAVO", 2, IndexImage.NodeOf(2)), ("DELTA", 4, IndexImage.NodeOf(3))])
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }

    /// <summary>Three leaves whose middle one is keyless, with a branch that points straight at it.</summary>
    private static IndexFileReader BranchOverAnEmptyMiddleLeaf()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(3))
            .WithLeaf(8, [("ALPHA", 1)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [], attribute: 2, left: IndexImage.NodeOf(0), right: IndexImage.NodeOf(2))
            .WithLeaf(8, [("ZULU", 9)], attribute: 2, left: IndexImage.NodeOf(1))
            .WithBranch(8, [
                ("ALPHA", 1, IndexImage.NodeOf(0)),
                ("MIKE", 1, IndexImage.NodeOf(1)),
                ("ZULU", 9, IndexImage.NodeOf(2))])
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }
}
