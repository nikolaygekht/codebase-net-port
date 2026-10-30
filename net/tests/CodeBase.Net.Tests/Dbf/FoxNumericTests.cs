using System.Globalization;
using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What a numeric field's text reads as, at the inputs no corpus field holds.
///
/// Every numeric value in the corpus is a well-formed number, so the corpus proves the parse and
/// nothing about its edges. The C library's own answer is [c]c4atod[/c], whose body is not in the
/// source drop, so these are **decisions recorded**, not discoveries: what this port answers, pinned
/// so that a change to it is a decision too. Text that is not a number reads as zero, as a blank
/// field does; it is never an error, because this is the decoder for bytes already in a file.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FoxNumericTests
{
    [Theory]
    [InlineData("        ")]
    [InlineData("")]
    public void ToDouble_OfABlankField_IsZero(string text)
    {
        ToDouble(text).Should().Be(0.0);
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("********")]
    [InlineData("12AB")]
    [InlineData("1,5")]
    [InlineData("1e5")]
    [InlineData("0x10")]
    public void ToDouble_OfTextThatIsNotANumber_IsZero(string text)
    {
        // Asterisks are what an overflowed write leaves; an exponent and a hexadecimal prefix are not
        // part of the stored form, so they are not numbers here either.
        ToDouble(text).Should().Be(0.0);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData(".")]
    [InlineData("-.")]
    public void ToDouble_OfASignOrPointWithNoDigits_IsZero(string text)
    {
        ToDouble(text).Should().Be(0.0);
    }

    [Theory]
    [InlineData("   -2.500", -2.5)]
    [InlineData("  +7", 7.0)]
    [InlineData("     .125", 0.125)]
    [InlineData("-0.000", 0.0)]
    public void ToDouble_OfAWellFormedValue_IsThatNumber(string text, double expected)
    {
        ToDouble(text).Should().Be(expected);
    }

    [Fact]
    public void ToDouble_OfNegativeZero_KeepsItsSign()
    {
        // A key built from it goes through the positive path all the same (KEY-COLLATION.md), but the
        // value itself is the IEEE negative zero, not zero.
        double.IsNegative(ToDouble("  -0.000")).Should().BeTrue();
    }

    [Theory]
    [InlineData("123456789012345678901234567890")]
    [InlineData("0.12345678901234567890123")]
    [InlineData("99999999999999999999.999")]
    public void ToDouble_OfMoreDigitsThanADoubleHolds_IsTheNearestDouble(string text)
    {
        // The parse is correctly rounded, so the expectation is the same text through the same
        // definition rather than a typed-out constant.
        ToDouble(text).Should().Be(double.Parse(text, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("  -2.9", -2)]
    [InlineData("   2.9", 2)]
    [InlineData("   ABC", 0)]
    public void ToInt32_DiscardsTheFraction(string text, int expected)
    {
        FoxNumeric.ToInt32(Encoding.ASCII.GetBytes(text)).Should().Be(expected);
    }

    private static double ToDouble(string text) => FoxNumeric.ToDouble(Encoding.ASCII.GetBytes(text));
}
