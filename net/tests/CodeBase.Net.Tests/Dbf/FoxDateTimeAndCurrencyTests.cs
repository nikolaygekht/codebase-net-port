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

    [Fact]
    public void ToDateTime_AMillisecondCountPastTheEndOfTheDay_RollsForwardRatherThanBeingRefused()
    {
        // Reproduced, not fixed: nothing in the reference constrains this field to one day, so a
        // stored 25 hours means the same moment to both. Pinned so that "tidying" it would fail.
        FoxDateTime.ToDateTime(Stored(2444606, 25 * 3600 * 1000))
                   .Should().Be(new DateTime(1981, 1, 2, 1, 0, 0));
    }

    [Fact]
    public void ToDateTime_AMillisecondCountThatLeavesTheCalendar_IsALibraryError()
    {
        // The line between the two: rolling a day is the reference's behaviour, but running off the
        // end of DateTime is this port's own arithmetic failing. It has to surface as the library's
        // exception type, not as an ArgumentOutOfRangeException the caller cannot catch with the rest.
        // The last day the calendar holds, plus about 24 days of milliseconds. A smaller julian is
        // caught by the existing range guard and reported as no datetime, which is a different path.
        Action act = () => FoxDateTime.ToDateTime(Stored(5373484, int.MaxValue));

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
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
