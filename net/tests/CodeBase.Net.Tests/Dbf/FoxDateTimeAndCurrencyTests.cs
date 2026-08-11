using System.Buffers.Binary;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// The datetime and currency conversions, at values the corpus does not hold.
///
/// The corpus gates 64 datetime values and 64 currency values, but every one of its datetimes falls
/// on a whole second, so the rule that rounds the second up at half a second is exercised nowhere in
/// it. That rule is the reason two different stored moments can render as the same text, so it is
/// covered here and named as ungated in the step's summary.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FoxDateTimeAndCurrencyTests
{
    [Fact]
    public void ToText_RendersABlankDateTimeAsSpacesAndAZeroTime()
    {
        FoxDateTime.ToText(new byte[8]).Should().Be("        00:00:00");
    }

    [Fact]
    public void ToDateTime_OfABlankDateTime_IsNothing()
    {
        FoxDateTime.ToDateTime(new byte[8]).Should().BeNull();
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(499, "00:00:00")]
    [InlineData(500, "00:00:01")]
    [InlineData(999, "00:00:01")]
    [InlineData(1499, "00:00:01")]
    [InlineData(1500, "00:00:02")]
    public void ToText_RoundsTheSecondUpAtHalfASecond(int milliseconds, string expected)
    {
        // FoxPro ignores the millisecond part and rounds rather than truncating, which the C library
        // reproduces deliberately (F4FIELD.C:1859-1873). Ungated: no corpus datetime has a fraction.
        FoxDateTime.ToText(Stored(2444606, milliseconds))[8..].Should().Be(expected);
    }

    [Fact]
    public void ToText_KeepsTheMillisecondsForTheVariantThatHasThem()
    {
        // The millisecond-keeping type has no corpus case at all, so this is its only cover.
        FoxDateTime.ToText(Stored(2444606, 1500), includeMilliseconds: true)[8..]
                   .Should().Be("00:00:01.500");
    }

    [Fact]
    public void ToDateTime_KeepsTheMillisecondsTheTextThrowsAway()
    {
        // The rendered form rounds; the value does not. A caller asking for the moment gets what the
        // file holds, not what the reference would have printed.
        FoxDateTime.ToDateTime(Stored(2444606, 1500))
                   .Should().Be(new DateTime(1981, 1, 1, 0, 0, 1, 500));
    }

    [Fact]
    public void ToText_RendersAnHourCountThatRunsPastADay()
    {
        // Nothing constrains the millisecond field to one day, and the reference lets the hours run.
        FoxDateTime.ToText(Stored(2444606, 25 * 3600 * 1000))[8..].Should().Be("25:00:00");
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(1L, "0.0001")]
    [InlineData(-1L, "-0.0001")]
    [InlineData(10000L, "1")]
    [InlineData(-999999999L, "-99999.9999")]
    [InlineData(9223372036854775807L, "922337203685477.5807")]
    [InlineData(-9223372036854775808L, "-922337203685477.5808")]
    public void ToDecimal_ScalesByTenThousandExactlyAcrossTheWholeRange(long stored, string expected)
    {
        // The type is fixed at four decimal places, whatever its descriptor says, so the extremes are
        // the extremes of a 64-bit integer divided by ten thousand.
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, stored);

        FoxCurrency.ToDecimal(bytes).Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ToDouble_OfACurrency_AgreesWithItsExactValue()
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, -999999999);

        FoxCurrency.ToDouble(bytes).Should().Be(-99999.9999);
    }

    private static byte[] Stored(int julian, int milliseconds)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, julian);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), milliseconds);
        return bytes;
    }
}
