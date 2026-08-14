using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The gate for seeking by value: every key the corpus holds, looked up through the public surface.
///
/// The transforms are already gated against the stored bytes. What this adds is that the whole path
/// works end to end — value in, the right record out — through the tag's own tree, for every key of
/// every character tag in the corpus, including collated ones and descending ones.
///
/// A seek that found the right record by luck of a short tag would not survive CDXDEEP, whose tags
/// are three levels deep and whose keys repeat across block boundaries.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class SeekByValueGoldenTests
{
    /// <summary>Every character tag in the corpus, with its table and the field behind it.</summary>
    public static TheoryData<string, string, string> CharacterTags() => new()
    {
        { "CDXBASE", "T_TEXT", "K_C" },
        { "CDXBASE", "T_TEXTD", "K_C" },
        { "CDXCOLL", "C_MACH", "K_TEXT" },
        { "CDXCOLL", "C_GEN", "K_TEXT" },
        { "CDXDEEP", "D_WIDE", "K_WIDE" },
        { "IDXONE", "X_WIDE", "K_WIDE" },
    };

    [Fact]
    public void TheGateCoversEveryCharacterTagInTheCorpus()
    {
        // A data-driven suite that discovered nothing would report success having proved nothing.
        CharacterTags().Should().HaveCount(6);
    }

    [Theory]
    [MemberData(nameof(CharacterTags))]
    public void Seek_FindsEveryValueTheTagHolds(string tableName, string tagName, string fieldName)
    {
        // For each record, seek the value that record holds and expect to land on a record holding
        // the same value. Not necessarily the same record: equal keys are ordered by record number,
        // and a seek lands on the first of a run, which is the documented behaviour.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        FieldDefinition field = table.Fields[fieldName];
        table.SelectTag(table.Tags[tagName]);

        int sought = 0;

        for (int number = 1; number <= table.RecordCount; number++)
        {
            table.Go(number).Should().Be(GoResult.Ok);
            string wanted = table.GetString(field);

            table.Seek(wanted).Should().Be(GoResult.Ok, $"record {number} holds '{wanted.TrimEnd()}'");

            // On a collated tag the record found need not hold the same text at all. GENERAL is
            // case- and accent-insensitive and expands ligatures, so "AEther" and "Æther" are one
            // key; equal keys order by record number and a seek lands on the first of the run. No
            // string comparison can express that, and the keys themselves are already gated by
            // KeyTransformGoldenTests, which rebuilds all 68 of this file's. What is left to prove
            // here is that every value in the tag is findable, which is the assertion above.
            if (tagName != "C_GEN")
                table.GetString(field).Should().Be(wanted);

            sought++;
        }

        sought.Should().Be(table.RecordCount);
    }

    [Theory]
    [MemberData(nameof(CharacterTags))]
    public void SeekPrefix_FindsAValueByItsOpeningCharacters(string tableName, string tagName, string fieldName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        FieldDefinition field = table.Fields[fieldName];
        table.SelectTag(table.Tags[tagName]);

        for (int number = 1; number <= table.RecordCount; number++)
        {
            table.Go(number).Should().Be(GoResult.Ok);
            string whole = table.GetString(field).TrimEnd();

            if (whole.Length < 2)
                continue;

            string opening = whole[..2];

            table.SeekPrefix(opening).Should().Be(GoResult.Ok, $"'{opening}' opens '{whole}'");

            // A collated tag matches case- and accent-insensitively, so the record it lands on need
            // not begin with these characters as *text* -- only as weights. Asserting the string
            // prefix there would be asserting that GENERAL does not do its job.
            if (tagName != "C_GEN")
                table.GetString(field).TrimEnd().Should().StartWith(opening);
        }
    }

    [Fact]
    public void Seek_AValueNoRecordHolds_ReportsNoRecordAndLeavesTheCursorOnNone()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));

        table.SelectTag(table.Tags["T_TEXT"]);

        table.Seek("NOTHING HOLDS THIS").Should().Be(GoResult.NoRecord);
        table.RecordNumber.Should().BeLessThan(1, "a miss positions on nothing, not on a neighbour");
    }

    [Fact]
    public void SeekAtOrAfter_AValueNoRecordHolds_LandsOnTheNextOneUpAndSaysSo()
    {
        // The difference from Seek, stated against real data: the same miss, a different contract.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));

        FieldDefinition text = table.Fields["K_C"];
        table.SelectTag(table.Tags["T_TEXT"]);

        table.SeekAtOrAfter("CUSTOMER-B").Should().Be(SeekResult.After);
        table.GetString(text).TrimEnd().Should().Be("CUSTOMER-BETA");
    }

    [Fact]
    public void SeekAtOrBefore_LandsOnTheOtherEndOfARange()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));

        FieldDefinition text = table.Fields["K_C"];
        table.SelectTag(table.Tags["T_TEXT"]);

        // CUSTOMER-ALPHA-TWO is the last key below CUSTOMER-B, not CUSTOMER-ALPHA: the hyphen and
        // the letters after it still sort before the B.
        table.SeekAtOrBefore("CUSTOMER-B").Should().Be(SeekResult.Before);
        table.GetString(text).TrimEnd().Should().Be("CUSTOMER-ALPHA-TWO");
    }

    [Fact]
    public void SeekNext_WalksARunOfEqualKeysAndThenStops()
    {
        // T_DUP holds runs of identical keys. Seeking one and stepping must visit every record in
        // the run and no more, which is what makes a duplicate key usable rather than ambiguous.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));

        FieldDefinition dup = table.Fields["K_DUP"];
        table.SelectTag(table.Tags["T_DUP"]);

        table.Seek("BLUE").Should().Be(GoResult.Ok);

        int found = 1;
        while (table.SeekNext() == GoResult.Ok)
        {
            table.GetString(dup).TrimEnd().Should().Be("BLUE");
            found++;
        }

        // The value sits at two positions of the six-value cycle, so ten of the 32 records carry it.
        found.Should().Be(10);
    }

    [Fact]
    public void Seek_OnADescendingTagFindsTheSameRecordsAsOnTheAscendingOne()
    {
        // Descending inverts traversal, not the keys, so a seek must find the same values either
        // way. A port that inverted the search value instead would pass on the ascending tag alone.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));

        FieldDefinition text = table.Fields["K_C"];

        foreach (string tagName in new[] { "T_TEXT", "T_TEXTD" })
        {
            table.SelectTag(table.Tags[tagName]);
            table.Seek("ZEBRA").Should().Be(GoResult.Ok, $"through {tagName}");
            table.GetString(text).TrimEnd().Should().Be("ZEBRA");
        }
    }

    [Fact]
    public void Seek_OnANumericTagFindsByNumberRatherThanByText()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));

        FieldDefinition number = table.Fields["K_N"];
        table.SelectTag(table.Tags["T_NUM"]);

        table.Seek(4242.424).Should().Be(GoResult.Ok);
        table.GetDouble(number).Should().Be(4242.424);
    }

    [Fact]
    public void Seek_OnADateTagFindsByDate()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXBASE.DBF"));

        FieldDefinition date = table.Fields["K_D"];
        table.SelectTag(table.Tags["T_DATE"]);

        table.Seek(new DateOnly(2000, 2, 29)).Should().Be(GoResult.Ok);
        table.GetDate(date).Should().Be(new DateOnly(2000, 2, 29));
    }
}
