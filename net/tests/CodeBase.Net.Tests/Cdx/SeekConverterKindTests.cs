using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The kind of key a tag stores, read off its expression's result type, and the expressions no
/// tag may be keyed by.
/// </summary>
/// <value>
/// The kinds are i4init.c:557-753's rows as KEY-COLLATION.md section 2 lists them: text keys as
/// its bytes, or as weights under a collation; every number, a date as a double and numeric text
/// key through the double transform; and each of the others through its own. The refusal is
/// 010-expr's Decision 5: a key built from the clock or the cursor could never be found again.
/// </value>
[Trait("Layer", "Component")]
public sealed class SeekConverterKindTests
{
    private static readonly FieldDefinition[] Fields =
    [
        new("K_TEXT", 'C', 'C', 10, 0, 1, false, false, false, false, null),
        new("K_MEMO", 'M', 'M', 4, 0, 11, false, false, false, false, null),
        new("K_NUM", 'N', 'N', 12, 3, 15, false, false, false, false, null),
        new("K_FLOAT", 'F', 'F', 12, 3, 27, false, false, false, false, null),
        new("K_DBL", 'B', 'B', 8, 2, 39, false, false, false, false, null),
        new("K_DATE", 'D', 'D', 8, 0, 47, false, false, false, false, null),
        new("K_INT", 'I', 'I', 4, 0, 55, false, false, false, false, null),
        new("K_CUR", 'Y', 'Y', 8, 4, 59, false, false, false, false, null),
        new("K_TS", 'T', 'T', 8, 0, 67, false, false, false, false, null),
        new("K_LOG", 'L', 'L', 1, 0, 75, false, false, false, false, null),
        new("K_SFLT", 'H', 'H', 4, 0, 76, false, false, false, false, null),
    ];

    [Theory]
    [InlineData("K_TEXT", "", 10, "Character")]
    [InlineData("K_TEXT", "GENERAL", 20, "CollatedCharacter")]
    [InlineData("K_MEMO", "", 240, "Character")]
    [InlineData("K_NUM", "", 8, "Double")]
    [InlineData("K_FLOAT", "", 8, "Double")]
    [InlineData("K_DBL", "", 8, "Double")]
    [InlineData("VAL(K_TEXT)", "", 8, "Double")]
    [InlineData("K_DATE", "", 8, "Date")]
    [InlineData("K_DATE + 1", "", 8, "Double")]
    [InlineData("DTOS(K_DATE)", "", 8, "Character")]
    [InlineData("K_INT", "", 4, "Int32")]
    [InlineData("K_CUR", "", 8, "Currency")]
    [InlineData("K_TS", "", 8, "DateTime")]
    [InlineData("K_SFLT", "", 4, "Single")]
    [InlineData("K_LOG", "", 1, "Logical")]
    public void ATagKeysByTheTypeItsExpressionProduces(string expression, string collation, int keyLength, string kind)
    {
        Converter(expression, keyLength, collation).Kind.Should().Be(Enum.Parse<KeyKind>(kind));
    }

    [Theory]
    [InlineData("DTOS(DATE())", 8, "DATE")]
    [InlineData("TIME()", 8, "TIME")]
    [InlineData("STR(RECNO(), 10)", 10, "RECNO")]
    [InlineData("STR(RECCOUNT(), 10)", 10, "RECCOUNT")]
    [InlineData("K_TEXT + TIME()", 18, "TIME")]
    public void AKeyThatIsNotTheSameTwiceIsRefusedByName(string expression, int keyLength, string function)
    {
        Action act = () => Converter(expression, keyLength, "");

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported && e.Message.Contains(function + "()"));
    }

    private static SeekConverter Converter(string expression, int keyLength, string collation) =>
        SeekConverter.For(
            IndexHeader.Parse(
                IndexImage.SingleTag(keyLength, IndexImage.NodeOf(0), expression: expression, collation: collation)
                    .WithLeaf(keyLength, [])
                    .Build(),
                "an image"),
            Fields,
            1252,
            ExpressionOptions.Invariant,
            "T");
}
