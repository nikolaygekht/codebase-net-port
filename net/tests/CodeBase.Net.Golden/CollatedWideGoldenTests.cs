using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Collated Unicode keys, sought through the public surface: every record of each collated wide tag in case WGEN found by its own text.
///
/// The keys themselves are gated by KeyTransformGoldenTests, which rebuilds every one of WGEN's. What
/// this adds is the seek: a whole value padded with U+2000 as a stored one is, then weighed, lands on
/// a key the record's own text keys to; and a prefix, weighed into heads alone, finds a record
/// beginning with those characters' weights. Records are compared by key, not text: GENERAL is case-
/// and accent-insensitive, so "ALPHA" and "alpha" are one key and a seek lands on the first of them.
///
/// Governing decision: ADR-85.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CollatedWideGoldenTests
{
    private const string TableName = "WGEN";

    private const char Blank = ' ';

    [Theory]
    [InlineData("W_GEN", "K_W")]
    [InlineData("W_AVAYA", "K_W")]
    [InlineData("W_G850", "K_W")]
    [InlineData("O_GEN", "K_O")]
    public void EveryRecordIsFoundByTheTextItWasAssigned(string tagName, string fieldName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));
        FieldDefinition field = table.Fields[fieldName];
        Tag tag = table.Tags[tagName];
        table.SelectTag(tag);
        int sought = 0;

        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record).Should().Be(GoResult.Ok);
            string assigned = table.GetString(field).TrimEnd(Blank);
            byte[] own = KeyOf(table, tag);

            table.Seek(assigned).Should().Be(GoResult.Ok, $"record {record} holds '{assigned}'");
            KeyOf(table, tag).Should().Equal(own, $"record {record} holds '{assigned}'");
            sought++;
        }

        sought.Should().Be(table.RecordCount);
    }

    [Theory]
    [InlineData("W_GEN", "K_W")]
    [InlineData("W_AVAYA", "K_W")]
    [InlineData("O_GEN", "K_O")]
    public void APrefixFindsARecordWhoseKeyBeginsWithItsHeads(string tagName, string fieldName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));
        FieldDefinition field = table.Fields[fieldName];
        Tag tag = table.Tags[tagName];
        table.SelectTag(tag);
        int sought = 0;

        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record).Should().Be(GoResult.Ok);
            string assigned = table.GetString(field).TrimEnd(Blank);
            if (assigned.Length < 2 || char.IsSurrogate(assigned[1]))
                continue;

            byte[] own = KeyOf(table, tag);

            table.SeekPrefix(assigned[..2]).Should().Be(GoResult.Ok, $"'{assigned[..2]}' opens '{assigned}'");

            // The two units' heads are the first bytes of the found key and of the record's own.
            int heads = HeadBytes(assigned[..2]);
            KeyOf(table, tag)[..heads].Should().Equal(own[..heads], $"'{assigned[..2]}' opens '{assigned}'");
            sought++;
        }

        sought.Should().BeGreaterThan(15, "the words, at least, are two characters or more");
    }

    /// <summary>The key the selected tag holds for the record the table is on, rebuilt as the library rebuilds it.</summary>
    private static byte[] KeyOf(Table table, Tag tag)
    {
        Cdx.SeekConverter converter = Cdx.SeekConverter.For(
            tag.Inner.Header, [.. table.Fields], table.CodePageNumber!.Value, table.ExpressionOptions, TableName, table.LongFieldNames);
        Expressions.ExpressionEvaluator evaluator = new(converter.Compiled);

        byte[] key = new byte[converter.KeyLength];
        Cdx.RecordKey.WriteValue(converter, evaluator.Evaluate(table.EvaluationContext()), key);
        return key;
    }

    /// <summary>How many head bytes two code units weigh into: four each for an expanding one, two otherwise.</summary>
    private static int HeadBytes(string two) =>
        two.Sum(c => c is '\u009C' or 'Æ' or 'æ' or 'ß' or 'Þ' or 'þ' or '\u008C' ? 4 : 2);
}
