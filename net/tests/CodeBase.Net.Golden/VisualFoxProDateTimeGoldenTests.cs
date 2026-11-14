using System.Buffers.Binary;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The datetime key against keys Visual FoxPro wrote itself, across every second of a day and all three binades of the Julian day.
///
/// A datetime key is the double of the day plus its fraction, nudged down one unit in its last byte
/// wherever an 86400-bit table says so. The reference carries that table as an empirical patch over a
/// conversion its authors could not decipher, and this library copies it; until this case it had only
/// been checked against keys the reference wrote, which it matches by construction. [c]VFPTIME[/c] is
/// written by Visual FoxPro 9 (ADR-44): every second of 2000-01-01, and every 37th second of six more
/// days chosen by binade, because adding a whole day to a fraction rounds it to the grid of the sum's
/// binade and a table indexed by the second alone can only be wrong across one.
///
/// Governing specification: KEY-COLLATION.md section 2.8 and section 9 item 1.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class VisualFoxProDateTimeGoldenTests
{
    private const string Oracle = "vfp/VFPTIME.DBF";

    /// <summary>The first Julian day of the binade every ordinary date is in, 2 to the 21st.</summary>
    private const int InsideFrom = 1 << 21;

    /// <summary>The first Julian day above it, 2 to the 22nd.</summary>
    private const int AboveFrom = 1 << 22;

    /// <summary>The Julian day of 0001-01-01, which [c]DateOnly.DayNumber[/c] counts from.</summary>
    private const int JulianOffset = 1_721_426;

    [Fact]
    public void EveryKeyVisualFoxProStoredForADatetimeIsRebuiltFromItsRecord()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Oracle));

        CdxTag tag = table.Tags["TS"].Inner;
        SeekConverter converter = SeekConverter.For(
            tag.Header, [.. table.Fields], table.CodePageNumber!.Value, table.ExpressionOptions, "VFPTIME", table.LongFieldNames);
        ExpressionEvaluator evaluator = new(converter.Compiled);
        FieldDefinition field = table.Fields["TS"];

        List<string> failures = [];
        Dictionary<string, int> perDay = [];
        int compared = 0;
        TagCursor cursor = tag.OpenCursor();

        for (bool on = cursor.Top(); on; on = cursor.Next())
        {
            IndexEntry stored = cursor.Current;
            table.Go((int)stored.Record);

            byte[] built = new byte[converter.KeyLength];
            RecordKey.WriteValue(converter, evaluator.Evaluate(table.EvaluationContext()), built);

            compared++;
            if (built.AsSpan().SequenceEqual(stored.Key))
                continue;

            (int julian, int milliseconds) = Moment(table, field);
            string day = $"julian {julian} ({Binade(julian)})";
            perDay[day] = perDay.GetValueOrDefault(day) + 1;
            failures.Add(
                $"record {stored.Record}, {day}, {milliseconds} ms: " +
                $"{Convert.ToHexString(built)}, stored {Convert.ToHexString(stored.Key)}");
        }

        string summary = string.Join(Environment.NewLine, perDay.OrderBy(d => d.Key).Select(d => $"  {d.Key}: {d.Value}"));
        string.Join(Environment.NewLine, failures.Take(5)).Should().BeEmpty(
            $"{failures.Count} keys disagree:{Environment.NewLine}{summary}{Environment.NewLine}");
        compared.Should().Be(table.RecordCount, "every record is in the tag once");
    }

    [Fact]
    public void EveryDatetimeVisualFoxProWroteReadsAsTheSecondItMeans()
    {
        // The script wrote 2000-01-01 plus n seconds into record n + 1, and Visual FoxPro stored about
        // half of them one millisecond short (ADR-79). Read back, each is the whole second it was.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Oracle));
        FieldDefinition field = table.Fields["TS"];
        DateTime midnight = new(2000, 1, 1);
        List<string> wrong = [];

        for (int second = 0; second < 86_400; second++)
        {
            table.Go(second + 1);
            DateTime? read = table.GetDateTime(field);

            if (read != midnight.AddSeconds(second))
                wrong.Add($"record {second + 1}: {read:yyyy-MM-dd HH:mm:ss.fff}");
        }

        string.Join(Environment.NewLine, wrong.Take(5)).Should().BeEmpty($"{wrong.Count} records read otherwise");
    }

    [Fact]
    public void TheOracleStillHoldsWhatTheRebuildRestsOn()
    {
        // A reader that found nothing, or a sample that missed a binade or the bitmap's set half,
        // would let the test above pass by comparing less than it claims to.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Oracle));
        FieldDefinition field = table.Fields["TS"];

        table.RecordCount.Should().Be(100_422);

        int baseDay = new DateOnly(2000, 1, 1).DayNumber + JulianOffset;
        int[] secondsOfBaseDay = new int[86_400];
        Dictionary<int, int> recordsPerDay = [];
        Dictionary<string, (int Flagged, int Plain)> halves = [];

        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            (int julian, int milliseconds) = Moment(table, field);

            // VFP holds a datetime as a double of days and truncates it into the field, so
            // DTOT(day) + n stores n seconds less a millisecond about half the time.
            (milliseconds % 1000).Should().BeOneOf([0, 999], "VFP writes a whole second, or one millisecond short of one");
            int second = (milliseconds + 500) / 1000;

            recordsPerDay[julian] = recordsPerDay.GetValueOrDefault(julian) + 1;

            if (julian == baseDay)
                secondsOfBaseDay[second]++;

            (int flagged, int plain) = halves.GetValueOrDefault(Binade(julian));
            halves[Binade(julian)] = DateTimeKeyFlags.NeedsDecrement(second)
                ? (flagged + 1, plain)
                : (flagged, plain + 1);

            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        secondsOfBaseDay.Should().AllSatisfy(count => count.Should().Be(1), "every second of the base day once");
        recordsPerDay.Should().HaveCount(7);
        recordsPerDay.Where(day => day.Key != baseDay).Should().AllSatisfy(day => day.Value.Should().Be(2337));

        halves.Keys.Should().BeEquivalentTo(["below", "inside", "above"]);
        foreach ((string binade, (int flagged, int plain)) in halves)
        {
            flagged.Should().BeGreaterThan(500, $"the bitmap's set half is exercised {binade} 2^21..2^22");
            plain.Should().BeGreaterThan(500, $"and so is its clear half, {binade}");
        }
    }

    /// <summary>Reads the current record's datetime as its Julian day and its milliseconds since midnight.</summary>
    private static (int Julian, int Milliseconds) Moment(Table table, FieldDefinition field)
    {
        ReadOnlySpan<byte> raw = table.GetRawBytes(field);
        return (BinaryPrimitives.ReadInt32LittleEndian(raw), BinaryPrimitives.ReadInt32LittleEndian(raw[4..]));
    }

    /// <summary>Names the binade of the Julian day a datetime falls in.</summary>
    private static string Binade(int julian) =>
        julian < InsideFrom ? "below" : julian < AboveFrom ? "inside" : "above";
}
