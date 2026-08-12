using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The gate for seeking: every search case of every corpus tag, against what the C library answered.
///
/// Two of the five operations are ports and three are additions, and the tests are split so that stays
/// visible. [c]Seek[/c] and [c]SeekNext[/c] are compared with recorded results. [c]SeekAtOrBefore[/c],
/// [c]SeekLast[/c] and [c]SeekPrevious[/c] have no counterpart in the C library, so they are held to
/// properties over the key sequence step 004 recorded — and tied back to the reference by the adjacency
/// check, which is the one that would catch a plausible-but-wrong definition.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class SeekGoldenTests
{
    /// <summary>Every index file the corpus holds.</summary>
    private static readonly string[] IndexFiles =
        ["CDXBASE.cdx", "CDXCOLL.cdx", "CDXDEEP.cdx", "IDXONE.cdx", "IDXONE.IDX"];

    /// <summary>The same list, for the data-driven tests.</summary>
    public static TheoryData<string> AllIndexes()
    {
        TheoryData<string> data = [];
        foreach (string file in IndexFiles)
            data.Add(file);
        return data;
    }

    [Fact]
    public void TheGateCoversEverySeekCaseTheCorpusHolds()
    {
        int cases = 0;
        int runs = 0;

        foreach (string file in IndexFiles)
        {
            CorpusIndexDump dump = CorpusIndexDump.Load(file);
            cases += dump.Tags.Sum(t => t.Seeks.Count);
            runs += dump.Tags.Sum(t => t.SeekNextRuns.Count);
        }

        // Part of the gate rather than commentary: a data-driven suite that discovers nothing reports
        // success having proved nothing.
        cases.Should().Be(206);
        runs.Should().Be(104);
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Seek_LandsWhereTheCLibraryLanded(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = SeekCorpus.Open(indexFile, expected);
        int compared = 0;

        foreach (DumpIndexTag tag in expected.Tags)
        {
            CdxTag actual = SeekCorpus.TagOf(index, tag);

            foreach (DumpSeekCase seek in tag.Seeks)
            {
                TagCursor cursor = actual.OpenCursor();
                KeySearch search = KeySearch.For(seek.Search.AsSpan(0, seek.Length), actual.KeyLength, actual.PadByte);

                SeekOutcome outcome = cursor.Seek(search);
                string because = $"{tag.Name} case {seek.What}";

                // The dump records the result code *and* whether the cursor ended past the end, because
                // the C library can return r4after with the cursor at end of file — the data-file
                // wrapper normalises that to r4eof and the tag-level call does not.
                if (seek.AtEnd)
                {
                    outcome.Should().Be(SeekOutcome.Eof, because);
                    cursor.Eof.Should().BeTrue(because);
                }
                else
                {
                    outcome.Should().Be(
                        seek.ResultCode == 0 ? SeekOutcome.Found : SeekOutcome.After, because);
                    cursor.Current.Record.Should().Be(seek.Record, because);
                    cursor.Current.Key.Should().Equal(seek.Key, because);
                }

                compared++;
            }
        }

        compared.Should().Be(expected.Tags.Sum(t => t.Seeks.Count));
        compared.Should().BePositive();
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void SeekNext_WalksTheRunTheCLibraryWalked(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = SeekCorpus.Open(indexFile, expected);
        int compared = 0;

        foreach (DumpIndexTag tag in expected.RealTags.Where(t => t.IdentityTransform))
        {
            CdxTag actual = index.Tag(tag.Name);

            foreach (DumpSeekNextRun run in tag.SeekNextRuns)
            {
                // The zero-length case is not comparable here, and the dump records why: through the
                // data file's API an empty value means "seek for .NULL." and is converted to a full-width
                // key of zero bytes (data4seekConvertKeyToTagFormat, D4SEEK.C:951-956), where at the tag
                // level it is a value that matches everything. Two different questions, so the recorded
                // answer belongs to the other one.
                if (run.Length == 0)
                {
                    compared++;
                    continue;
                }

                TagCursor cursor = actual.OpenCursor();
                KeySearch search = KeySearch.For(run.Search.AsSpan(0, run.Length), actual.KeyLength, actual.PadByte);
                List<uint> visited = [];

                for (SeekOutcome outcome = cursor.Seek(search);
                     outcome == SeekOutcome.Found;
                     outcome = cursor.SeekNext(search))
                {
                    visited.Add(cursor.Current.Record);

                    if (visited.Count > 5000)
                        break;
                }

                visited.Should().Equal(run.Records, "{0} case {1}", tag.Name, run.What);
                compared++;
            }
        }

        compared.Should().Be(expected.RealTags.Sum(t => t.SeekNextRuns.Count));
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void SeekAtOrBefore_LandsOnTheLastEntryNotGreaterThanTheValue(string indexFile)
    {
        // A property, not a recorded result: the C library has no such operation. What makes it more
        // than our own definition restated is the adjacency test below.
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = SeekCorpus.Open(indexFile, expected);

        foreach (DumpIndexTag tag in expected.Tags)
        {
            CdxTag actual = SeekCorpus.TagOf(index, tag);

            foreach (DumpSeekCase seek in tag.Seeks)
            {
                KeySearch search = KeySearch.For(seek.Search.AsSpan(0, seek.Length), actual.KeyLength, actual.PadByte);
                int last = SeekCorpus.LastNotGreaterThan(tag, search);
                string because = $"{tag.Name} case {seek.What}";

                TagCursor cursor = actual.OpenCursor();
                SeekOutcome outcome = cursor.SeekAtOrBefore(search);

                if (last < 0)
                {
                    outcome.Should().Be(SeekOutcome.Bof, because);
                    continue;
                }

                outcome.Should().BeOneOf([SeekOutcome.Found, SeekOutcome.Before], because);
                cursor.Current.Record.Should().Be(tag.Keys[last].Record, because);
                cursor.Current.Key.Should().Equal(tag.Keys[last].Key, because);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void SeekAtOrBefore_AndSeekLandOnAdjacentEntriesOrBracketTheRun(string indexFile)
    {
        // The check that ties the added operation to the ported one, and it has to compare where the two
        // *cursors* actually land: Seek is gated against the C library, so if SeekAtOrBefore is always
        // its immediate neighbour then it inherits that gate's authority. Comparing the two expectations
        // computed from the dump instead would only prove the helpers agree with each other.
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = SeekCorpus.Open(indexFile, expected);
        int checked_ = 0;

        foreach (DumpIndexTag tag in expected.Tags)
        {
            CdxTag actual = SeekCorpus.TagOf(index, tag);

            foreach (DumpSeekCase seek in tag.Seeks)
            {
                KeySearch search = KeySearch.For(seek.Search.AsSpan(0, seek.Length), actual.KeyLength, actual.PadByte);
                string because = $"{tag.Name} case {seek.What}";

                TagCursor ahead = actual.OpenCursor();
                TagCursor behind = actual.OpenCursor();

                SeekOutcome forward = ahead.Seek(search);
                SeekOutcome backward = behind.SeekAtOrBefore(search);

                if (forward == SeekOutcome.Eof || backward == SeekOutcome.Bof)
                    continue;

                int at = SeekCorpus.PositionOf(tag, ahead.Current);
                int before = SeekCorpus.PositionOf(tag, behind.Current);

                if (forward == SeekOutcome.Found)
                {
                    // Both matched, so they bracket the run: the first of it and the last of it.
                    backward.Should().Be(SeekOutcome.Found, because);
                    before.Should().BeGreaterThanOrEqualTo(at, because);
                    search.Matches(tag.Keys[at].Key).Should().BeTrue(because);
                    search.Matches(tag.Keys[before].Key).Should().BeTrue(because);

                    if (at > 0)
                        search.Matches(tag.Keys[at - 1].Key).Should().BeFalse($"{because}: at the run's start");
                    if (before + 1 < tag.Keys.Count)
                        search.Matches(tag.Keys[before + 1].Key).Should().BeFalse($"{because}: at the run's end");
                }
                else
                {
                    // Neither matched, so they are neighbours with the value falling between them.
                    backward.Should().Be(SeekOutcome.Before, because);
                    (at - before).Should().Be(1, $"{because}: the two landings are adjacent");
                }

                checked_++;
            }
        }

        checked_.Should().BePositive();
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void SeekLastThenPrevious_IsSeekThenNextReversed(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = SeekCorpus.Open(indexFile, expected);

        foreach (DumpIndexTag tag in expected.Tags)
        {
            CdxTag actual = SeekCorpus.TagOf(index, tag);

            foreach (DumpSeekCase seek in tag.Seeks)
            {
                KeySearch search = KeySearch.For(seek.Search.AsSpan(0, seek.Length), actual.KeyLength, actual.PadByte);
                string because = $"{tag.Name} case {seek.What}";

                List<uint> forwards = [];
                TagCursor forward = actual.OpenCursor();
                for (SeekOutcome o = forward.Seek(search); o == SeekOutcome.Found; o = forward.SeekNext(search))
                    forwards.Add(forward.Current.Record);

                List<uint> backwards = [];
                TagCursor backward = actual.OpenCursor();
                for (SeekOutcome o = backward.SeekLast(search); o == SeekOutcome.Found; o = backward.SeekPrevious(search))
                    backwards.Add(backward.Current.Record);

                backwards.Should().Equal(Enumerable.Reverse(forwards), because);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void SeekExact_FindsEveryEntryTheTagHolds(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = SeekCorpus.Open(indexFile, expected);
        int found = 0;

        foreach (DumpIndexTag tag in expected.Tags)
        {
            CdxTag actual = SeekCorpus.TagOf(index, tag);
            TagCursor cursor = actual.OpenCursor();

            foreach (DumpIndexKey key in tag.Keys)
            {
                KeySearch search = KeySearch.For(key.Key, actual.KeyLength, actual.PadByte);

                cursor.SeekExact(search, key.Record)
                    .Should().Be(SeekOutcome.Found, "{0} holds record {1}", tag.Name, key.Record);
                cursor.Current.Record.Should().Be(key.Record);
                found++;
            }
        }

        found.Should().Be(expected.Tags.Sum(t => t.Keys.Count));
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Seeking_AndTraversing_Compose(string indexFile)
    {
        // Seek then walk unconditionally: the whole tail of the tag from wherever the seek landed. This
        // is what a range scan does, and what makes index-order traversal from an arbitrary key work.
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = SeekCorpus.Open(indexFile, expected);

        foreach (DumpIndexTag tag in expected.Tags)
        {
            CdxTag actual = SeekCorpus.TagOf(index, tag);

            foreach (DumpSeekCase seek in tag.Seeks)
            {
                KeySearch search = KeySearch.For(seek.Search.AsSpan(0, seek.Length), actual.KeyLength, actual.PadByte);
                string because = $"{tag.Name} case {seek.What}";

                TagCursor cursor = actual.OpenCursor();
                if (cursor.Seek(search) == SeekOutcome.Eof)
                    continue;

                int landed = tag.Keys
                    .Select((k, i) => (k, i))
                    .First(x => x.k.Record == cursor.Current.Record && x.k.Key.SequenceEqual(cursor.Current.Key))
                    .i;

                List<uint> tail = [cursor.Current.Record];
                while (cursor.Next())
                    tail.Add(cursor.Current.Record);

                tail.Should().Equal(tag.Keys.Skip(landed).Select(k => k.Record), because);
            }
        }
    }
}
