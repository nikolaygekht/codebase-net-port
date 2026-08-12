using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The gate for navigating a table in a tag's order: the records visited, and their contents.
///
/// Two halves, and the second is what makes it a *table* test. Visiting the record numbers the index dump
/// records proves the tag was followed; reading every field of each and comparing against the table's own
/// record dump proves the right record was actually read. A walk that returned the right numbers while
/// reading each one's neighbour would pass any index-only test.
///
/// It runs over both surfaces — the selected-tag form and the explicit one — because they share an
/// implementation but not their entry points, and a fix applied to one would otherwise drift.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class TagNavigationGoldenTests
{
    /// <summary>The tables that carry a production index, with the dump of that index.</summary>
    public static TheoryData<string, string> IndexedTables() => new()
    {
        { "CDXBASE", "CDXBASE.cdx" },
        { "CDXCOLL", "CDXCOLL.cdx" },
        { "CDXDEEP", "CDXDEEP.cdx" },
        { "IDXONE", "IDXONE.cdx" },
    };

    /// <summary>The tables that carry none, which must be unaffected by any of this.</summary>
    public static TheoryData<string> UnindexedTables() =>
        ["CP1251", "CP936", "DB3TYPE", "F2XMEMO", "VFPMEMO", "VFPNULL", "VFPTYPE"];

    [Fact]
    public void TheGateCoversEveryIndexedTableAndEveryTableWithout()
    {
        // Both halves are part of the gate: the seven older tables are the regression guard for record
        // order, which is the risk this step carries.
        IndexedTables().Should().HaveCount(4);
        UnindexedTables().Should().HaveCount(7);
    }

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void Table_ReportsTheTagsTheIndexHolds(string tableName, string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.HasIndex.Should().BeTrue();
        table.Tags.Select(t => t.Name).Should().Equal(expected.RealTags.Select(t => t.Name));

        foreach (DumpIndexTag dumped in expected.RealTags)
        {
            Tag tag = table.Tags[dumped.Name];

            tag.KeyLength.Should().Be(dumped.KeyLength, "tag {0} key length", tag.Name);
            tag.Descending.Should().Be(dumped.Descending, "tag {0} direction", tag.Name);
            tag.Expression.Should().Be(Text(dumped.ExpressionBytes), "tag {0} expression", tag.Name);
            tag.Filter.Should().Be(Text(dumped.FilterBytes), "tag {0} filter", tag.Name);
            tag.Collation.Should().Be(dumped.SortSequence, "tag {0} collation", tag.Name);
            tag.Unique.Should().Be((dumped.TypeCode & 0x05) != 0, "tag {0} uniqueness", tag.Name);
            tag.Filtered.Should().Be((dumped.TypeCode & 0x08) != 0, "tag {0} filter presence", tag.Name);
        }
    }

    [Theory]
    [MemberData(nameof(UnindexedTables))]
    public void Table_WithoutAProductionIndexHasNoTags(string tableName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.HasIndex.Should().BeFalse();
        table.Tags.Should().BeEmpty();
        table.SelectedTag.Should().BeNull();

        // And record order still works exactly as it did before any of this existed.
        table.Top().Should().Be(GoResult.Ok);
        table.RecordNumber.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void Walking_ASelectedTag_VisitsTheRecordsTheIndexNames(string tableName, string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        CorpusDump records = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        int visited = 0;

        foreach (DumpIndexTag dumped in expected.RealTags)
        {
            table.SelectTag(table.Tags[dumped.Name]);

            List<int> walked = [];
            for (GoResult go = table.Top(); go == GoResult.Ok; go = table.Skip(1) == SkipResult.Moved ? GoResult.Ok : GoResult.NoRecord)
            {
                walked.Add(table.RecordNumber);

                // The half that makes this a table test: the record actually read must be the one the
                // index named, field for field, against the table's own dump.
                AssertFieldsMatch(table, records, table.RecordNumber, dumped.Name);
                visited++;
            }

            walked.Should().Equal(
                dumped.Keys.Select(k => (int)k.Record),
                "tag {0} orders the records this way", dumped.Name);
        }

        visited.Should().Be(expected.RealTags.Sum(t => t.Keys.Count));
        visited.Should().BePositive();
    }

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void Walking_TheExplicitForm_VisitsExactlyTheSameRecords(string tableName, string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        foreach (DumpIndexTag dumped in expected.RealTags)
        {
            Tag tag = table.Tags[dumped.Name];

            List<int> walked = [];
            for (GoResult go = table.GoFirstIndexed(tag); go == GoResult.Ok; go = table.GoNextIndexed(tag))
                walked.Add(table.RecordNumber);

            walked.Should().Equal(
                dumped.Keys.Select(k => (int)k.Record),
                "the explicit form must not drift from the selected-tag one on {0}", dumped.Name);

            // And it left the selection alone, so a mode-based call still means what it did.
            table.SelectedTag.Should().BeNull();
        }
    }

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void Walking_BackwardsFromTheLastRecordReversesTheOrder(string tableName, string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        foreach (DumpIndexTag dumped in expected.RealTags)
        {
            Tag tag = table.Tags[dumped.Name];

            List<int> backwards = [];
            for (GoResult go = table.GoLastIndexed(tag); go == GoResult.Ok; go = table.GoPreviousIndexed(tag))
                backwards.Add(table.RecordNumber);

            backwards.Should().Equal(
                dumped.Keys.Select(k => (int)k.Record).Reverse(),
                "tag {0} walked backwards", dumped.Name);
        }
    }

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void AFilteredOrUniqueTagReachesFewerRecordsThanTheTableHolds(string tableName, string indexFile)
    {
        // Not a defect but the point: a filtered tag holds keys only for the records that satisfy it, and a
        // unique one only for the first record of each value. Both are visible as a shorter walk.
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        foreach (DumpIndexTag dumped in expected.RealTags)
        {
            Tag tag = table.Tags[dumped.Name];

            if (!tag.Filtered && !tag.Unique)
                continue;

            int walked = 0;
            for (GoResult go = table.GoFirstIndexed(tag); go == GoResult.Ok; go = table.GoNextIndexed(tag))
                walked++;

            walked.Should().Be(dumped.Keys.Count).And.BeLessThan(table.RecordCount);
        }
    }

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void EveryTagsPadByteIsTheOneTheCLibraryUsed(string tableName, string indexFile)
    {
        // ADR-28's rule, checked against what the reference implementation actually did: the pad byte is
        // now *derived* from the field descriptors rather than supplied by the test, so this is the
        // assertion that keeps the derivation honest.
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        foreach (DumpIndexTag dumped in expected.RealTags)
        {
            table.Tags[dumped.Name].Inner.PadByte
                .Should().Be(dumped.PadByte, "tag {0} pads its keys with this byte", dumped.Name);
        }
    }

    private static void AssertFieldsMatch(Table table, CorpusDump records, int recordNumber, string tagName)
    {
        DumpRecord expected = records.Records[recordNumber - 1];

        expected.Number.Should().Be(recordNumber, "the dump is in record order");

        foreach (DumpValue value in expected.Values)
        {
            if (value.IsMemo)
                continue;

            FieldDefinition field = table.Fields[value.Name];

            table.GetRawBytes(field).Should().Equal(
                value.Bytes,
                "record {0} field {1}, reached through tag {2}", recordNumber, value.Name, tagName);
        }
    }

    private static string Text(byte[] bytes) => System.Text.Encoding.ASCII.GetString(bytes);
}
