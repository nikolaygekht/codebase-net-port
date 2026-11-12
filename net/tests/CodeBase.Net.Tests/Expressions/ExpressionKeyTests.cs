using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// The key an evaluated value is written as: by its type, behind a byte saying whether it is there,
/// and with a trimmed string's tail made blank again.
/// </summary>
/// <value>
/// EXPRESSIONS.md sections 9.2 to 9.4. A number or a date held as the text a record stores keys as
/// the double it reads as, so its expectation is the double's key from KeyTransform, which is gated
/// against stored keys on its own (KeyTransformGoldenTests); here the promise is that the two forms
/// of one value key alike.
/// </value>
[Trait("Layer", "Unit")]
public sealed class ExpressionKeyTests
{
    [Fact]
    public void AnAbsentValueIsAKeyOfZeroBytes()
    {
        byte[] key = Enumerable.Repeat((byte)0xEE, 9).ToArray();

        int written = ExpressionKey.Write(ExpressionValue.Numeric(5).AsNull(), false, withIndicator: true, key);

        written.Should().Be(9);
        key.Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void APresentValueIsItsKeyBehind0x80()
    {
        byte[] plain = new byte[8];
        ExpressionKey.Write(ExpressionValue.Numeric(5), false, withIndicator: false, plain);
        byte[] key = new byte[9];

        int written = ExpressionKey.Write(ExpressionValue.Numeric(5), false, withIndicator: true, key);

        written.Should().Be(9);
        key.Should().Equal([0x80, .. plain]);
    }

    [Theory]
    [InlineData("   12.50", 12.5)]
    [InlineData("  -12.50", -12.5)]
    [InlineData("        ", 0)]
    public void ANumberHeldAsTextKeysAsTheDoubleItReads(string text, double number)
    {
        // num (text) to key is t4dblToFox(c4atod(text)) (EXPRESSIONS.md section 9.3).
        byte[] key = new byte[8];

        ExpressionKey.Write(ExpressionValue.NumericText(Encoding.ASCII.GetBytes(text)), false, false, key);

        key.Should().Equal(DoubleKey(number));
    }

    [Theory]
    [InlineData("20000115", 2451559)]
    [InlineData("        ", 0)]
    public void ADateHeldAsTextKeysAsItsJulianDay(string text, double julian)
    {
        // date (text) to key is t4dblToFox(date4long(text)), and a blank date is day zero.
        byte[] key = new byte[8];

        ExpressionKey.Write(ExpressionValue.DateText(Encoding.ASCII.GetBytes(text)), false, false, key);

        key.Should().Equal(DoubleKey(julian));
    }

    [Fact]
    public void ATimestampKeysThroughTheDatetimeTransform()
    {
        byte[] key = new byte[8];
        byte[] expected = new byte[8];
        KeyTransform.FromDateTime(2451559, 45296000, expected);

        ExpressionKey.Write(ExpressionValue.Timestamp(2451559, 45296000), false, false, key);

        key.Should().Equal(expected);
    }

    [Fact]
    public void ATimestampWithMillisecondsKeepsThemInItsKey()
    {
        // The '7' type, loaded as its own expression type, keys without rounding (ADR-75).
        byte[] key = new byte[8];
        byte[] expected = new byte[8];
        KeyTransform.FromDateTime(2451559, 45296600, expected, keepMilliseconds: true);

        ExpressionKey.Write(ExpressionValue.TimestampWithMilliseconds(2451559, 45296600), false, false, key);

        key.Should().Equal(expected);
    }

    [Fact]
    public void AStringIsPaddedWithBlanksOrCutToTheKey()
    {
        byte[] padded = new byte[5];
        byte[] cut = new byte[2];

        ExpressionKey.Write(Text("AB"), false, false, padded);
        ExpressionKey.Write(Text("ABC"), false, false, cut);

        Encoding.ASCII.GetString(padded).Should().Be("AB   ");
        Encoding.ASCII.GetString(cut).Should().Be("AB");
    }

    [Fact]
    public void UnderATrimATrailingRunOfZeroBytesBecomesBlanks()
    {
        // EXPRESSIONS.md section 9.4: a trim leaves zeros where its characters were, and the key turns
        // them back into blanks -- only at the end, so a zero inside the text is kept.
        byte[] key = new byte[6];

        ExpressionKey.Write(ExpressionValue.Text([(byte)'A', 0, (byte)'B', 0, 0, 0]), hasTrim: true, false, key);

        key.Should().Equal((byte)'A', 0, (byte)'B', (byte)' ', (byte)' ', (byte)' ');
    }

    [Fact]
    public void AStringTrimmedToNothingKeysAsBlanks()
    {
        byte[] key = new byte[3];

        ExpressionKey.Write(ExpressionValue.Text([0, 0, 0], 0), hasTrim: true, false, key);

        Encoding.ASCII.GetString(key).Should().Be("   ");
    }

    [Fact]
    public void WithoutATrimZeroBytesStayInTheKey()
    {
        byte[] key = new byte[4];

        ExpressionKey.Write(ExpressionValue.Text([(byte)'A', (byte)'B', 0, 0]), hasTrim: false, false, key);

        key.Should().Equal((byte)'A', (byte)'B', 0, 0);
    }

    [Fact]
    public void ATrimmedValueAndTheBlankPaddedTextKeyAlike()
    {
        // The reason for the rule: two records differing only in trailing blanks key the same.
        byte[] trimmed = new byte[6];
        byte[] padded = new byte[6];

        ExpressionKey.Write(ExpressionValue.Text([(byte)'A', (byte)'B', 0, 0, 0, 0], 2), hasTrim: true, false, trimmed);
        ExpressionKey.Write(Text("AB    "), hasTrim: false, false, padded);

        trimmed.Should().Equal(padded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AWideValueKeysAsBigEndianUtf16(bool withLength)
    {
        ExpressionType type = withLength ? ExpressionType.WideStringWithLength : ExpressionType.WideString;
        // t4unicodeToMachine swaps every code unit (i4conv.c:588-602), so a key sorts by code unit.
        string text = "A\u0416\u2000";
        byte[] key = new byte[6];

        ExpressionKey.Write(ExpressionValue.Wide(Encoding.Unicode.GetBytes(text), type), false, false, key);

        key.Should().Equal(Encoding.BigEndianUnicode.GetBytes(text));
    }

    private static ExpressionValue Text(string text) => ExpressionValue.Text(Encoding.ASCII.GetBytes(text));

    private static byte[] DoubleKey(double value)
    {
        byte[] key = new byte[8];
        KeyTransform.FromDouble(value, key);
        return key;
    }
}
