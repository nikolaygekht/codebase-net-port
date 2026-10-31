using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// A tag over a field that can be null is sought with the byte its keys begin with.
///
/// Such a tag's keys spend a leading byte on whether the value is there, 0x80 for a value; the
/// reference puts that byte in front of whatever it is asked to seek (tfile4stok, D4SEEK.C:47-57,
/// and its numeric siblings at 154-156 and 177-179). A search without it compares a value's first
/// byte against the indicator and finds nothing. The corpus has three such tags — a number, a memo,
/// and a memo under GENERAL — and every value each holds must be found.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class NullableTagSeekGoldenTests
{
    [Fact]
    public void EveryNumberANullableNumericTagHoldsIsFound()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));
        FieldDefinition field = table.Fields["K_NNUM"];
        table.SelectTag(table.Tags["T_NULL"]);
        int sought = 0;

        for (int number = 1; number <= table.RecordCount; number++)
        {
            table.Go(number);
            if (table.IsNull(field))
                continue;

            double wanted = table.GetDouble(field);
            table.Seek(wanted).Should().Be(GoResult.Ok, $"record {number} holds {wanted}");
            table.GetDouble(field).Should().Be(wanted);
            sought++;
        }

        sought.Should().Be(13, "three of the sixteen records are null");
    }

    [Theory]
    [InlineData("TAGCTX", "T_NMEMO", false)]
    [InlineData("TAGGEN", "G_NMEMO", true)]
    public void EveryMemoANullableMemoTagHoldsIsFoundByItsOpening(string tableName, string tagName, bool collated)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));
        FieldDefinition field = table.Fields["K_NMEMO"];
        table.SelectTag(table.Tags[tagName]);
        int sought = 0;

        for (int number = 1; number <= table.RecordCount; number++)
        {
            table.Go(number);
            string memo = table.GetMemoString(field);
            if (memo.Length < 2)
                continue;

            string opening = memo[..2];
            table.SeekPrefix(opening).Should().Be(GoResult.Ok, $"record {number}'s memo opens with '{opening}'");

            // Under GENERAL a prefix matches by weight, not by text, so only machine order can be
            // held to the characters.
            if (!collated)
                table.GetMemoString(field).Should().StartWith(opening);

            sought++;
        }

        sought.Should().Be(6, "two of the eight records are null, which for a memo means empty");
    }
}
