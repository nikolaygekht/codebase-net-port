using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Memo;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The gate: every field of every record of every corpus table, against what the C library read back.
///
/// Layers below this can pass on a self-consistent misreading — a decoder can be perfect while every
/// record is fetched one too early, and the whole suite would still be green. This is the only thing
/// that compares against the reference implementation, so it is what says the port is right.
///
/// Nothing is skipped. Step 002 gated the ordinary fields and subtracted the memo ones from its
/// count; step 003 closed that half, so the assertion count must now reach the field list exactly.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class RecordGoldenTests
{
    [Fact]
    public void TheGateCoversEveryTableInTheCorpus()
    {
        // Part of the gate rather than commentary: a data-driven suite that discovers nothing
        // reports success having proved nothing.
        AllTables().Should().HaveCount(7);
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Walking_VisitsEveryRecordInOrderWithTheDeletionFlagTheCLibraryReports(string tableName)
    {
        // Needs no decoder at all, and catches the failure that would otherwise look like a decoding
        // bug: reading one record too early or too late.
        CorpusDump expected = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(expected.FileName));

        List<(int Number, bool Deleted)> visited = [];

        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            visited.Add((table.RecordNumber, table.Deleted));
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        visited.Should().HaveCount(expected.RecordCount);
        visited.Select(v => v.Number).Should().Equal(expected.Records.Select(r => r.Number));
        visited.Select(v => v.Deleted).Should().Equal(expected.Records.Select(r => r.Deleted));
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Walking_EndsPastTheLastRecord(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(expected.FileName));

        table.Bottom().Should().Be(GoResult.Ok);
        table.RecordNumber.Should().Be(expected.RecordCount);

        table.Skip(1).Should().Be(SkipResult.Eof);
        table.RecordNumber.Should().Be(expected.RecordCount + 1);
        table.Eof.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Go_ReachesEveryRecordDirectlyAndRefusesTheOnePastTheEnd(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(expected.FileName));

        foreach (DumpRecord record in expected.Records)
        {
            table.Go(record.Number).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(record.Number);
            table.Deleted.Should().Be(record.Deleted);
        }

        table.Go(expected.RecordCount + 1).Should().Be(GoResult.NoRecord);
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryOrdinaryFieldOfEveryRecord_ReadsWhatTheCLibraryRead(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(expected.FileName));

        int fieldsAsserted = 0;

        foreach (DumpRecord record in expected.Records)
        {
            table.Go(record.Number).Should().Be(GoResult.Ok);
            table.Deleted.Should().Be(record.Deleted, "record {0} deletion flag", record.Number);

            foreach (DumpValue value in record.Values)
            {
                FieldDefinition field = table.Fields[value.Name];

                AssertField(table, field, value, record.Number);
                fieldsAsserted++;
            }

            AssertNullFlags(table, expected, record);
        }

        // Step 002 skipped the memo fields and subtracted them here; step 003 closed that half, so
        // the count must now reach the field list exactly. Nothing is skipped, and this is what says
        // so — a field quietly dropped from the loop shows up as a shortfall.
        fieldsAsserted.Should().Be(
            ExpectedFieldCount(expected), "every field of every record is asserted, none skipped");
        fieldsAsserted.Should().BeGreaterThan(0, "a gate that asserts nothing proves nothing");
    }

    /// <summary>
    /// Asserts one field against everything the dump records for it.
    /// </summary>
    private static void AssertField(Table table, FieldDefinition field, DumpValue value, int recordNumber)
    {
        string because = $"record {recordNumber} field {value.Name}";

        table.GetRawBytes(field).Should().Equal(value.Bytes, because);
        table.IsNull(field).Should().Be(value.IsNull, because);

        if (value.IsMemo)
        {
            // The in-record reference, then the entry behind it. Both come from the dump, so a
            // reference that resolved to the wrong block would have to hit an entry whose payload
            // happens to match — which is what makes this an assertion and not a tautology.
            table.GetMemoBlock(field).Should()
                 .Be(MemoReference.Read(value.Bytes), "{0} block number", because);
            table.GetMemoLength(field).Should().Be((int)value.MemoLength!.Value, "{0} length", because);
            table.GetMemoBytes(field).Should().Equal(value.MemoBytes!, "{0} payload", because);
            table.GetMemoType(field).Should().Be(MemoType.Text, "{0} type", because);
        }

        if (value.Number is double number)
        {
            BitConverter.DoubleToInt64Bits(table.GetDouble(field))
                        .Should().Be(BitConverter.DoubleToInt64Bits(number), because);
        }

        if (value.Integer is long integer)
            table.GetInt32(field).Should().Be((int)integer, because);

        if (value.Text is not null && field.Type == 'D')
            RenderDate(table, field).Should().Be(value.Text, because);

        if (value.Text is not null && field.Type is 'T' or '7')
            FoxDateTime.ToText(table.GetRawBytes(field), field.Type == '7').Should().Be(value.Text, because);
    }

    /// <summary>
    /// Renders a date the way the dump's own string form does, which is the stored text itself.
    /// </summary>
    private static string RenderDate(Table table, FieldDefinition field) =>
        System.Text.Encoding.ASCII.GetString(table.GetRawBytes(field));

    /// <summary>
    /// Asserts the stored null bitmap, not merely the marks read out of it.
    /// </summary>
    private static void AssertNullFlags(Table table, CorpusDump expected, DumpRecord record)
    {
        if (record.NullFlags is null)
        {
            table.NullFlags.Should().BeNull(
                "{0} has no null-flags line in its dump", expected.FileName);
            return;
        }

        table.NullFlags.Should().NotBeNull();
        table.GetRawBytes(table.NullFlags!).Should().Equal(
            record.NullFlags, "record {0} null bitmap", record.Number);
    }

    /// <summary>
    /// How many field assertions the table should contribute, counted from the dump rather than
    /// from what the loop happened to visit.
    /// </summary>
    private static int ExpectedFieldCount(CorpusDump dump) => dump.Fields.Count * dump.RecordCount;

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
