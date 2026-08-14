using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Stepping in a tag's order from a record reached by number, against the order the C library wrote.
///
/// This is the path that used to walk the whole tag looking for a record number and now derives the
/// record's key and descends to it instead. The change is meant to be one of speed alone, so what
/// matters is that the records reached are identical — and the expectation for that comes from the
/// dump's own key sequence rather than from the code being replaced.
///
/// Every position is tested, not a sample: after [c]Go(n)[/c] the tag cursor is wherever the last
/// indexed move left it, so each starting point exercises a different amount of drift.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class SynchronizeGoldenTests
{
    /// <summary>Tags that list every record of their table, with the index file they live in.</summary>
    /// <value>
    /// Filtered and unique tags are left out on purpose: they do not list every record, so stepping
    /// from one they omit is the refusal ADR-30 describes rather than a move to compare.
    /// </value>
    public static TheoryData<string, string, string> WholeTags() => new()
    {
        { "CDXBASE", "CDXBASE.cdx", "T_TEXT" },
        { "CDXBASE", "CDXBASE.cdx", "T_TEXTD" },
        { "CDXBASE", "CDXBASE.cdx", "T_NUM" },
        { "CDXBASE", "CDXBASE.cdx", "T_DATE" },
        { "CDXCOLL", "CDXCOLL.cdx", "C_GEN" },
        { "CDXDEEP", "CDXDEEP.cdx", "D_WIDE" },
        { "CDXTIME", "CDXTIME.cdx", "T_TS" },
        { "CDXTIME", "CDXTIME.cdx", "T_TSD" },
    };

    [Fact]
    public void TheGateCoversEnoughTagsToBeWorthRunning()
    {
        WholeTags().Should().HaveCount(8);
    }

    [Theory]
    [MemberData(nameof(WholeTags))]
    public void GoThenSkip_ReachesTheRecordTheTagOrdersNext(string tableName, string indexFile, string tagName)
    {
        CorpusIndexDump dump = CorpusIndexDump.Load(indexFile);
        DumpIndexTag expected = dump.RealTags.Single(t => t.Name.TrimEnd() == tagName);

        // The dump lists a tag's keys in the tag's own order, descending tags included -- it was
        // written by walking the tag, not by reading the blocks in file order. So this is the
        // sequence a caller should see, with no inversion needed.
        List<uint> order = [.. expected.Keys.Select(k => k.Record)];

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.SelectTag(table.Tags[tagName]);

        for (int at = 0; at + 1 < order.Count; at++)
        {
            // Go by number, which leaves the tag cursor pointing somewhere else entirely, then step.
            table.Go((int)order[at]).Should().Be(GoResult.Ok);
            table.Skip(1).Should().Be(SkipResult.Moved);

            table.RecordNumber.Should().Be(
                (int)order[at + 1],
                $"{tagName} orders record {order[at]} before {order[at + 1]}");
        }
    }

    [Theory]
    [MemberData(nameof(WholeTags))]
    public void GoThenSkipBackwards_ReachesTheRecordTheTagOrdersBefore(
        string tableName, string indexFile, string tagName)
    {
        CorpusIndexDump dump = CorpusIndexDump.Load(indexFile);
        DumpIndexTag expected = dump.RealTags.Single(t => t.Name.TrimEnd() == tagName);

        List<uint> order = [.. expected.Keys.Select(k => k.Record)];

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.SelectTag(table.Tags[tagName]);

        for (int at = 1; at < order.Count; at++)
        {
            table.Go((int)order[at]).Should().Be(GoResult.Ok);
            table.Skip(-1).Should().Be(SkipResult.Moved);

            table.RecordNumber.Should().Be((int)order[at - 1]);
        }
    }
}
