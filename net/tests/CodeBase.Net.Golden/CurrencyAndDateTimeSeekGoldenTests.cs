using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Seeking currency and datetime tags by value, through the public API, against keys the reference and Visual FoxPro wrote.
///
/// Every record is sought by its own value and must be landed on, or on another record holding the
/// same value. [c]VFPTIME[/c] carries the weight: its 100 422 keys include every second on which the
/// reference's datetime bitmap and Visual FoxPro's conversion disagree, so a seek built the
/// reference's way would miss there (ADR-78). The positional seeks are checked against an expectation
/// computed from the values themselves, sorted, which owes nothing to the tag.
///
/// Governing decisions: ADR-58, ADR-78, ADR-80.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CurrencyAndDateTimeSeekGoldenTests
{
    [Fact]
    public void EveryCurrencyIsFoundByItsOwnValue()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));
        FieldDefinition field = table.Fields["K_CUR"];
        table.SelectTag(table.Tags["T_CUR"]);

        List<string> wrong = [];
        int sought = 0;

        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record);
            decimal value = table.GetDecimal(field);

            sought++;
            if (table.Seek(value) != GoResult.Ok || table.GetDecimal(field) != value)
                wrong.Add($"record {record}: {value}");
        }

        string.Join(Environment.NewLine, wrong).Should().BeEmpty();
        sought.Should().Be(16);
    }

    [Theory]
    [InlineData("CDXTIME.DBF", "T_TS", "TS", 255)]   // 256 records, one blank
    [InlineData("CDXTIME.DBF", "T_TSD", "TS", 255)]
    [InlineData("DTMILLI.DBF", "M_ASC", "K_DTMS", 299)] // 300 records, one blank
    [InlineData("vfp/VFPTIME.DBF", "TS", "TS", 100_422)]
    public void EveryDatetimeIsFoundByItsOwnValue(string file, string tagName, string fieldName, int moments)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(file));
        FieldDefinition field = table.Fields[fieldName];
        table.SelectTag(table.Tags[tagName]);

        List<string> wrong = [];
        int sought = 0;

        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record);
            if (table.GetDateTime(field) is not DateTime value)
                continue;

            sought++;
            if (table.Seek(value) != GoResult.Ok || table.GetDateTime(field) != value)
                wrong.Add($"record {record}: {value:yyyy-MM-dd HH:mm:ss.fff}");
        }

        string.Join(Environment.NewLine, wrong.Take(5)).Should().BeEmpty($"{wrong.Count} not found");
        sought.Should().Be(moments, "every record holding a moment is sought");
    }

    [Fact]
    public void ADatetimeBetweenTwoKeysLandsOnTheNeighbourEitherSide()
    {
        // A second past each stored moment: at or after it is the first moment not below it, at or
        // before it the last not above it -- computed from the sorted values, not read off the tag.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXTIME.DBF"));
        FieldDefinition field = table.Fields["TS"];
        table.SelectTag(table.Tags["T_TS"]);

        List<DateTime> values = [];
        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record);
            if (table.GetDateTime(field) is DateTime value)
                values.Add(value);
        }

        values.Sort();
        List<string> wrong = [];

        foreach (DateTime target in values.Select(v => v.AddSeconds(1)).Distinct())
        {
            DateTime? after = values.Where(v => v >= target).Select(v => (DateTime?)v).FirstOrDefault();
            DateTime? before = values.Where(v => v <= target).Select(v => (DateTime?)v).LastOrDefault();

            SeekResult up = table.SeekAtOrAfter(target);
            if (!Agrees(up, after, target, SeekResult.After, SeekResult.Eof, table, field))
                wrong.Add($"at or after {target:yyyy-MM-dd HH:mm:ss}: {up}");

            SeekResult down = table.SeekAtOrBefore(target);
            if (!Agrees(down, before, target, SeekResult.Before, SeekResult.Bof, table, field))
                wrong.Add($"at or before {target:yyyy-MM-dd HH:mm:ss}: {down}");
        }

        string.Join(Environment.NewLine, wrong.Take(5)).Should().BeEmpty($"{wrong.Count} landed elsewhere");
    }

    /// <summary>Decides whether a positional seek landed where the sorted values say it should.</summary>
    private static bool Agrees(
        SeekResult result, DateTime? expected, DateTime target, SeekResult past, SeekResult end, Table table, FieldDefinition field)
    {
        if (expected is null)
            return result == end;

        SeekResult status = expected == target ? SeekResult.Found : past;
        return result == status && table.GetDateTime(field) == expected;
    }
}
