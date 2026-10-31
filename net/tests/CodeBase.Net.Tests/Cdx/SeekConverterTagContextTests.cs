using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// A tag's expression is resolved for that tag, not as a filter would be.
/// </summary>
/// <value>
/// Golden tests prove the rules against the reference's tags in TAGCTX and TAGGEN. This pins the
/// converter's own decisions over headers built for the purpose: the context it hands the resolver,
/// the kind a collated tag over a conversion keys as, and when a seek has to go through the
/// conversion. The lengths are the ones the corpus's tag-context section witnesses.
/// </value>
[Trait("Layer", "Component")]
public sealed class SeekConverterTagContextTests
{
    private static readonly FieldDefinition[] Fields =
    [
        new("K_TEXT", 'C', 'C', 20, 0, 1, false, false, false, false, null),
        new("K_MEMO", 'M', 'M', 4, 0, 21, false, false, false, false, null),
    ];

    [Fact]
    public void ATagOverABareMemoResolvesItAtTheKeySize()
    {
        Converter("K_MEMO", keyLength: 240, collation: "").Compiled.Length.Should().Be(240);
    }

    [Fact]
    public void AGeneralTagOverADescendingStringResolvesItAtTheCollatedWidth()
    {
        Converter("DESCEND(K_TEXT)", keyLength: 40, collation: "GENERAL").Compiled.Length.Should().Be(40);
    }

    [Theory]
    // A collated string tag over a conversion stores bytes already weighed, so it keys as plain
    // character data (E4EXPR.C:671-684); without a conversion it is collated as usual.
    [InlineData("DESCEND(K_TEXT)", "GENERAL", "Character")]
    [InlineData("ASCEND(K_TEXT)", "GENERAL", "Character")]
    [InlineData("DESCEND(K_TEXT) + K_TEXT", "GENERAL", "Character")]
    [InlineData("K_TEXT", "GENERAL", "CollatedCharacter")]
    [InlineData("DESCEND(K_TEXT)", "", "Character")]
    // The bypass is on the string path only: a number computed from a conversion keys as a number.
    [InlineData("VAL(DESCEND(K_TEXT))", "GENERAL", "Double")]
    public void ATagOverAConversionKeysAsTheConversionLeftIt(string expression, string collation, string kind)
    {
        Converter(expression, keyLength: 40, collation).Kind.Should().Be(Enum.Parse<KeyKind>(kind));
    }

    [Theory]
    // Only a conversion that is the whole expression maps a sought string onto a key.
    [InlineData("DESCEND(K_TEXT)", "GENERAL", "Descending")]
    [InlineData("ASCEND(K_TEXT)", "GENERAL", "Ascending")]
    [InlineData("DESCEND(K_TEXT)", "", "Descending")]
    [InlineData("ASCEND(K_TEXT)", "", "Ascending")]
    [InlineData("DESCEND(K_TEXT) + K_TEXT", "", "None")]
    [InlineData("K_TEXT", "GENERAL", "None")]
    [InlineData("UPPER(K_TEXT)", "", "None")]
    public void OnlyAWholeExpressionConversionIsSoughtThroughIt(string expression, string collation, string conversion)
    {
        Converter(expression, keyLength: 40, collation).Conversion.Should().Be(Enum.Parse<KeyConversion>(conversion));
    }

    [Theory]
    // The conversion weighs its operand with the tag's collation, so a collated tag over one needs the
    // weights as much as a collated tag over the plain field does; machine order needs none.
    [InlineData("DESCEND(K_TEXT)", "GENERAL", true)]
    [InlineData("K_TEXT", "GENERAL", true)]
    [InlineData("DESCEND(K_TEXT)", "", false)]
    public void ACollationWithNoWeightsForTheCodePageIsRefused(string expression, string collation, bool refused)
    {
        Action open = () => Converter(expression, keyLength: 40, collation, codePage: 1251);

        if (refused)
            open.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
        else
            open.Should().NotThrow();
    }

    private static SeekConverter Converter(string expression, int keyLength, string collation, int codePage = 1252)
    {
        byte[] image = IndexImage.SingleTag(keyLength, root: 1024, expression: expression, collation: collation).Build();

        return SeekConverter.For(
            IndexHeader.Parse(image, "an image"), Fields, codePage, ExpressionOptions.Invariant, alias: "T");
    }
}
