using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The search for a string through a tag's ASCEND or DESCEND conversion, by its shape.
/// </summary>
/// <value>
/// The bytes are gated against the stored keys of TAGCTX and TAGGEN. What is pinned here are the
/// relations a seek depends on, stated without any expected bytes: a prefix search is a prefix of the
/// whole one, so a partial seek finds the key; and DESCEND's search is ASCEND's complemented, so the
/// two orders are each other's mirror.
/// </value>
[Trait("Layer", "Unit")]
public sealed class ConversionSearchTests
{
    private static readonly FieldDefinition[] Fields =
    [
        new("K_TEXT", 'C', 'C', 20, 0, 1, false, false, false, false, null),
    ];

    [Theory]
    [InlineData("ASCEND(K_TEXT)", "", 20)]
    [InlineData("DESCEND(K_TEXT)", "", 20)]
    [InlineData("ASCEND(K_TEXT)", "GENERAL", 40)]
    [InlineData("DESCEND(K_TEXT)", "GENERAL", 40)]
    public void APrefixSearchIsAPrefixOfTheWholeOne(string expression, string collation, int keyLength)
    {
        SeekConverter converter = Converter(expression, keyLength, collation);

        byte[] whole = new byte[keyLength];
        byte[] opening = new byte[keyLength];
        int wholeLength = ConversionSearch.Write(converter, "zebra"u8, prefix: false, whole);
        int openingLength = ConversionSearch.Write(converter, "zeb"u8, prefix: true, opening);

        wholeLength.Should().Be(keyLength);
        openingLength.Should().BeInRange(1, keyLength - 1);
        whole.AsSpan().StartsWith(opening.AsSpan(0, openingLength)).Should().BeTrue();
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData("GENERAL", 40)]
    public void DescendingIsAscendingComplemented(string collation, int keyLength)
    {
        // "café" in cp1252, so a collation has a tail to write as well as heads.
        byte[] text = [(byte)'c', (byte)'a', (byte)'f', 0xE9];
        byte[] ascending = new byte[keyLength];
        byte[] descending = new byte[keyLength];

        ConversionSearch.Write(Converter("ASCEND(K_TEXT)", keyLength, collation), text, prefix: false, ascending);
        ConversionSearch.Write(Converter("DESCEND(K_TEXT)", keyLength, collation), text, prefix: false, descending);

        descending.Should().Equal(ascending.Select(b => (byte)~b));
    }

    [Fact]
    public void InMachineOrderAnAscendingSearchIsTheTextPaddedWithBlanks()
    {
        // Machine order converts nothing: the search is the text at the operand's width.
        byte[] search = new byte[20];

        ConversionSearch.Write(Converter("ASCEND(K_TEXT)", 20, ""), "zebra"u8, prefix: false, search);

        search.Should().Equal([.. "zebra"u8.ToArray(), .. Enumerable.Repeat((byte)' ', 15)]);
    }

    [Fact]
    public void UnderGeneralAnAscendingSearchIsTheCollatedKeyWithItsTails()
    {
        // The whole search weighs the padded text with its tails, as the stored key was weighed;
        // "café" has a tail, so leaving the tails out would be a different key.
        byte[] text = [(byte)'c', (byte)'a', (byte)'f', 0xE9];
        byte[] padded = [.. text, .. Enumerable.Repeat((byte)' ', 16)];
        byte[] expected = new byte[40];
        CollatedKey.Write(
            padded,
            CollationWeights.TableFor(CollationName.General, 1252),
            CollationWeights.ExpansionsFor(CollationName.General, 1252),
            includeTails: true,
            expected);

        byte[] search = new byte[40];
        ConversionSearch.Write(Converter("ASCEND(K_TEXT)", 40, "GENERAL"), text, prefix: false, search);

        search.Should().Equal(expected);
    }

    [Fact]
    public void AValueWiderThanTheOperandIsCutToIt()
    {
        SeekConverter converter = Converter("DESCEND(K_TEXT)", 20, "");
        byte[] cut = new byte[20];
        byte[] exact = new byte[20];

        ConversionSearch.Write(converter, "abcdefghijklmnopqrstuvwxyz"u8, prefix: false, cut);
        ConversionSearch.Write(converter, "abcdefghijklmnopqrst"u8, prefix: false, exact);

        cut.Should().Equal(exact);
    }

    private static SeekConverter Converter(string expression, int keyLength, string collation)
    {
        byte[] image = IndexImage.SingleTag(keyLength, root: 1024, expression: expression, collation: collation).Build();

        return SeekConverter.For(
            IndexHeader.Parse(image, "an image"), Fields, codePage: 1252, ExpressionOptions.Invariant, alias: "T");
    }
}
