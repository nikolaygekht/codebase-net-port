using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Walking a whole table four different ways, and getting the same records in the same order each time.
///
/// The record suite asserts each record's contents once the cursor is on it. This asserts the thing
/// no per-record check can: that a traversal visits **every** record, **once**, in the **right
/// order**, and stops where it should. A cursor that skipped a record, repeated one, or ran the
/// sequence backwards would satisfy every field assertion in the suite and still be wrong.
///
/// Each record is identified by one field whose value is different in all thirty-two of them, chosen
/// from the dump rather than named here. That the field is unique is asserted, so a future corpus
/// case with no unique field fails loudly instead of comparing a constant to itself.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class TableScanGoldenTests
{
    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryWayOfWalkingATable_VisitsTheSameRecordsInTheSameOrder(string tableName)
    {
        CorpusDump dump = CorpusDump.Load(tableName);
        DumpField identity = IdentityFieldOf(dump);

        IReadOnlyList<string> expected =
            [.. dump.Records.Select(r => $"{r.Number}:{Hex(r[identity.Name].Bytes)}")];

        expected.Should().HaveCount(dump.RecordCount);
        expected.Should().OnlyHaveUniqueItems("the identity field must tell the records apart");

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(dump.FileName));
        FieldDefinition field = table.Fields[identity.Name];

        ByNumber(table, field, dump.RecordCount).Should().Equal(expected, "walking by record number");
        Forwards(table, field).Should().Equal(expected, "walking from the top with Skip");
        Backwards(table, field).Should().Equal(expected, "walking from the bottom with Skip");
        BackwardsFromEndOfFile(table, field).Should().Equal(expected, "walking back from end of file");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void AForwardWalk_EndsPastTheLastRecordAndABackwardOneEndsOnTheFirst(string tableName)
    {
        // Where each traversal stops is part of it being correct: a loop that never reaches its end
        // condition, or reaches it one record early, produces the right records and the wrong count.
        CorpusDump dump = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(dump.FileName));

        table.Top().Should().Be(GoResult.Ok);
        while (table.Skip(1) == SkipResult.Moved)
        {
            // Walk to the end.
        }

        table.Eof.Should().BeTrue();
        table.Bof.Should().BeFalse();
        table.RecordNumber.Should().Be(dump.RecordCount + 1);

        table.Bottom().Should().Be(GoResult.Ok);
        while (table.Skip(-1) == SkipResult.Moved)
        {
            // Walk back to the start.
        }

        table.Bof.Should().BeTrue();
        table.Eof.Should().BeFalse();
        table.RecordNumber.Should().Be(1, "a backward walk stops on record one, not before it");
    }

    /// <summary>
    /// Walks by record number, the way a caller who already knows the count does.
    /// </summary>
    private static List<string> ByNumber(Table table, FieldDefinition field, int recordCount)
    {
        List<string> visited = [];

        for (int number = 1; number <= recordCount; number++)
        {
            table.Go(number).Should().Be(GoResult.Ok);
            visited.Add(Identity(table, field));
        }

        // The record after the last one is not there, which is what makes the count above the count.
        table.Go(recordCount + 1).Should().Be(GoResult.NoRecord);

        return visited;
    }

    /// <summary>
    /// Walks from the first record forwards until the skip runs out.
    /// </summary>
    private static List<string> Forwards(Table table, FieldDefinition field)
    {
        List<string> visited = [];

        table.Top().Should().Be(GoResult.Ok);
        do
        {
            visited.Add(Identity(table, field));
        }
        while (table.Skip(1) == SkipResult.Moved);

        return visited;
    }

    /// <summary>
    /// Walks from the last record backwards until the skip runs out, and puts the order back.
    /// </summary>
    private static List<string> Backwards(Table table, FieldDefinition field)
    {
        List<string> visited = [];

        table.Bottom().Should().Be(GoResult.Ok);
        do
        {
            visited.Add(Identity(table, field));
        }
        while (table.Skip(-1) == SkipResult.Moved);

        visited.Reverse();
        return visited;
    }

    /// <summary>
    /// Walks backwards starting from past the end rather than from the last record.
    /// </summary>
    private static List<string> BackwardsFromEndOfFile(Table table, FieldDefinition field)
    {
        List<string> visited = [];

        table.Bottom().Should().Be(GoResult.Ok);
        table.Skip(1).Should().Be(SkipResult.Eof);
        table.Eof.Should().BeTrue();

        // The first step back off end of file lands on the last record. A cursor that treated end of
        // file as one record further out would start this walk one record short.
        table.Skip(-1).Should().Be(SkipResult.Moved);

        do
        {
            visited.Add(Identity(table, field));
        }
        while (table.Skip(-1) == SkipResult.Moved);

        visited.Reverse();
        return visited;
    }

    /// <summary>
    /// Renders the current record as its number and the bytes of its identifying field.
    /// </summary>
    private static string Identity(Table table, FieldDefinition field) =>
        $"{table.RecordNumber}:{Hex(table.GetRawBytes(field))}";

    /// <summary>
    /// Picks a field whose value differs in every record, so that a record can be told from any other.
    /// </summary>
    private static DumpField IdentityFieldOf(CorpusDump dump)
    {
        foreach (DumpField field in dump.Fields)
        {
            // A memo field's in-record value is a block reference, which step 003 owns; the ordinary
            // fields are enough and every corpus table has one that works.
            if (dump.Records[0][field.Name].IsMemo)
                continue;

            int distinct = dump.Records.Select(r => Hex(r[field.Name].Bytes)).Distinct().Count();
            if (distinct == dump.RecordCount)
                return field;
        }

        throw new InvalidDataException(
            $"No field of {dump.FileName} has a different value in every record, so no record can be " +
            "told from another and a scan test over it would prove nothing. Add one to the generator " +
            "case.");
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
