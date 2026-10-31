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
    [InlineData("DESCEND(K_TEXT)", "GENERAL", 40, "Character")]
    [InlineData("ASCEND(K_TEXT)", "GENERAL", 40, "Character")]
    [InlineData("DESCEND(K_TEXT) + K_TEXT", "GENERAL", 60, "Character")]
    [InlineData("K_TEXT", "GENERAL", 40, "CollatedCharacter")]
    [InlineData("DESCEND(K_TEXT)", "", 20, "Character")]
    // The bypass is on the string path only: a number computed from a conversion keys as a number.
    [InlineData("VAL(DESCEND(K_TEXT))", "GENERAL", 8, "Double")]
    public void ATagOverAConversionKeysAsTheConversionLeftIt(string expression, string collation, int keyLength, string kind)
    {
        Converter(expression, keyLength, collation).Kind.Should().Be(Enum.Parse<KeyKind>(kind));
    }

    [Theory]
    // Only a conversion that is the whole expression maps a sought string onto a key.
    [InlineData("DESCEND(K_TEXT)", "GENERAL", 40, "Descending")]
    [InlineData("ASCEND(K_TEXT)", "GENERAL", 40, "Ascending")]
    [InlineData("DESCEND(K_TEXT)", "", 20, "Descending")]
    [InlineData("ASCEND(K_TEXT)", "", 20, "Ascending")]
    [InlineData("DESCEND(K_TEXT) + K_TEXT", "", 40, "None")]
    [InlineData("K_TEXT", "GENERAL", 40, "None")]
    [InlineData("UPPER(K_TEXT)", "", 20, "None")]
    public void OnlyAWholeExpressionConversionIsSoughtThroughIt(string expression, string collation, int keyLength, string conversion)
    {
        Converter(expression, keyLength, collation).Conversion.Should().Be(Enum.Parse<KeyConversion>(conversion));
    }

    [Theory]
    // The length the reference computes at open, against a header that says otherwise
    // (i4init.c:485-490): a collated string adds its collation's bytes, a conversion does not, and
    // a memo under GENERAL is half the cap with a key of twice that.
    [InlineData("K_TEXT", "", 20, false)]
    [InlineData("K_TEXT", "", 30, true)]
    [InlineData("K_TEXT", "GENERAL", 40, false)]
    [InlineData("K_TEXT", "GENERAL", 20, true)]
    [InlineData("DESCEND(K_TEXT)", "GENERAL", 40, false)]
    [InlineData("DESCEND(K_TEXT)", "GENERAL", 20, true)]
    [InlineData("K_MEMO", "GENERAL", 240, false)]
    [InlineData("K_MEMO", "GENERAL", 120, true)]
    public void ATagWhoseHeaderDisagreesWithItsExpressionIsRefused(string expression, string collation, int keyLength, bool refused)
    {
        Action open = () => Converter(expression, keyLength, collation);

        if (!refused)
        {
            open.Should().NotThrow();
            return;
        }

        CodeBaseException refusal = open.Should().Throw<CodeBaseException>().Which;
        refusal.Code.Should().Be(ErrorCode.Index);
        refusal.Message.Should().Contain(expression);
    }

    [Theory]
    // The conversion weighs its operand with the tag's collation, so a collated tag over one needs the
    // weights as much as a collated tag over the plain field does; machine order needs none.
    [InlineData("DESCEND(K_TEXT)", "GENERAL", 40, true)]
    [InlineData("K_TEXT", "GENERAL", 40, true)]
    [InlineData("DESCEND(K_TEXT)", "", 20, false)]
    public void ACollationWithNoWeightsForTheCodePageIsRefused(string expression, string collation, int keyLength, bool refused)
    {
        Action open = () => Converter(expression, keyLength, collation, codePage: 1251);

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
