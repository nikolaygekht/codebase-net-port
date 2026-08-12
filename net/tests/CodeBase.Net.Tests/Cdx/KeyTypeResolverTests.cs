using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Tests.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// Working out a tag's pad byte from the table it belongs to — ADR-28's rule, row by row.
///
/// Layer one. What this catches that the corpus cannot: the four field types no corpus tag indexes
/// (currency, datetime, binary character and float), and every refusal. A wrong pad byte does not throw
/// and does not stop a walk; it corrupts the padded tail of every key in the tag, which is why the whole
/// table is checked here rather than the types that happen to be in use.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class KeyTypeResolverTests
{
    [Theory]
    [InlineData('C', KeyPadding.Space)]
    [InlineData('Z', KeyPadding.Space)]
    [InlineData('N', KeyPadding.Nul)]
    [InlineData('F', KeyPadding.Nul)]
    [InlineData('B', KeyPadding.Nul)]
    [InlineData('Y', KeyPadding.Nul)]
    [InlineData('I', KeyPadding.Nul)]
    [InlineData('D', KeyPadding.Nul)]
    [InlineData('T', KeyPadding.Nul)]
    public void PadByteFor_AFieldTypeDecidesThePadByte(char fieldType, byte expected)
    {
        // Character data pads with spaces because that is how a character field is stored; every
        // fixed-width numeric key pads with NUL (i4init.c:557-604).
        IndexHeader header = Header("KEYFIELD", keyLength: 8);

        KeyTypeResolver.PadByteFor(header, [Field("KEYFIELD", fieldType)]).Should().Be(expected);
    }

    [Fact]
    public void PadByteFor_TheNameIsMatchedWithoutRegardToCaseOrTrailingBlanks()
    {
        // The engine upper-cases field names on open, and an expression may carry blanks the index kept.
        IndexHeader header = Header("  keyfield ", keyLength: 8);

        KeyTypeResolver.PadByteFor(header, [Field("KEYFIELD", 'C')]).Should().Be(KeyPadding.Space);
    }

    [Fact]
    public void PadByteFor_ACollatedTagNeedsNoFieldAtAll()
    {
        // Any collation other than machine order pads with NUL by itself, character keys included, so the
        // field table is never consulted — the reason a GENERAL tag was readable before this existed.
        IndexHeader header = Header("KEYFIELD", keyLength: 40, collation: "GENERAL");

        KeyTypeResolver.PadByteFor(header, []).Should().Be(KeyPadding.Nul);
    }

    [Fact]
    public void PadByteFor_AnExpressionThatIsNotAFieldNameIsRefusedAndQuoted()
    {
        // The line ADR-28 draws: this is the case that waits for the expression engine, and the message
        // has to say which expression so a caller knows which tag to avoid.
        IndexHeader header = Header("UPPER(NAME)", keyLength: 20);

        Action act = () => KeyTypeResolver.PadByteFor(header, [Field("NAME", 'C')]);

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported)
            .WithMessage("*UPPER(NAME)*");
    }

    [Fact]
    public void PadByteFor_AnExpressionNamingAFieldTheTableDoesNotHaveIsRefused()
    {
        IndexHeader header = Header("GONE", keyLength: 8);

        Action act = () => KeyTypeResolver.PadByteFor(header, [Field("NAME", 'C')]);

        act.Should().Throw<CodeBaseException>().WithMessage("*GONE*");
    }

    [Theory]
    [InlineData('L')]
    [InlineData('M')]
    [InlineData('G')]
    public void PadByteFor_AFieldTypeWithNoOrdinaryKeyIsRefused(char fieldType)
    {
        // A logical, memo or general field is not something the C library builds an ordinary key from.
        // Guessing a pad byte for one would be worse than saying so.
        IndexHeader header = Header("KEYFIELD", keyLength: 8);

        Action act = () => KeyTypeResolver.PadByteFor(header, [Field("KEYFIELD", fieldType)]);

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported)
            .WithMessage($"*'{fieldType}'*");
    }

    private static IndexHeader Header(string expression, int keyLength, string collation = "") =>
        IndexHeader.Parse(
            IndexImage.SingleTag(keyLength, IndexImage.NodeOf(0), expression: expression, collation: collation)
                .Build(),
            "an image");

    private static FieldDefinition Field(string name, char type) =>
        new(name, type, type, length: 8, decimals: 0, recordOffset: 1,
            isBinary: false, isSystem: false, isAutoIncrement: false, isAutoTimestamp: false, nullBit: null);
}
