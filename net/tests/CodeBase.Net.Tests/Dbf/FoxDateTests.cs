using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// The date conversion, at the values the corpus does not hold.
///
/// The corpus dates are all ordinary and all valid, so blankness, illegality and the boundaries of
/// the calendar are only reachable here. The Julian arithmetic is ported from the C library rather
/// than handed to a calendar class, because the stored index keys were built with it.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FoxDateTests
{
    [Fact]
    public void ToJulian_OfTheDateTheCLibraryDocuments()
    {
        // The one value the reference states outright, which anchors everything else.
        Julian("19810101").Should().Be(2444606);
    }

    [Theory]
    [InlineData("00010101", 1721426)]
    [InlineData("19000101", 2415021)]
    [InlineData("20260811", 2461264)]
    public void ToJulian_CountsDaysSinceTheEpoch(string date, long expected)
    {
        Julian(date).Should().Be(expected);
    }

    [Theory]
    [InlineData("        ")]
    [InlineData("00000000")]
    public void ToJulian_OfABlankDate_IsZero(string date)
    {
        // Visual FoxPro writes the all-zero form for a blank date, and the reference treats the two
        // the same (D4DATE.C:690-693).
        Julian(date).Should().Be(FoxDate.Blank);
    }

    [Theory]
    [InlineData("20260231")]
    [InlineData("20261301")]
    [InlineData("20260100")]
    [InlineData("2026013X")]
    [InlineData("19000229")]
    public void ToJulian_OfSomethingThatIsNotADate_IsMinusOne(string date)
    {
        // Distinct from blank, because the reference distinguishes them and a caller reading the
        // numeric form can tell an empty date from a broken one.
        Julian(date).Should().Be(FoxDate.Illegal);
    }

    [Theory]
    [InlineData("20000229")]
    [InlineData("20240229")]
    public void ToJulian_AcceptsALeapDayInALeapYear(string date)
    {
        Julian(date).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ToJulian_TreatsACenturyYearThatIsNotDivisibleByFourHundredAsCommon()
    {
        Julian("19000229").Should().Be(FoxDate.Illegal);
        Julian("20000229").Should().BeGreaterThan(0);
    }

    [Fact]
    public void ToJulian_AndBackAgain_RoundTripsEveryDayOfFourCenturies()
    {
        // The property the corpus cannot state: the conversion is one to one across the whole
        // Gregorian cycle, which is where a leap-year rule off by one would show.
        DateOnly day = new(1800, 1, 1);
        int checked_ = 0;

        while (day.Year < 2200)
        {
            string text = $"{day.Year:D4}{day.Month:D2}{day.Day:D2}";

            FoxDate.ToDate(Encoding.ASCII.GetBytes(text)).Should().Be(day);
            Julian(text).Should().Be(day.DayNumber + 1721426);

            day = day.AddDays(1);
            checked_++;
        }

        checked_.Should().BeGreaterThan(140000);
    }

    [Theory]
    [InlineData("        ")]
    [InlineData("00000000")]
    [InlineData("20260231")]
    public void ToDate_OfAnythingWithoutADateInIt_IsNothing(string date)
    {
        // Blank and illegal are one answer here, unlike the Julian form: there is no date to report
        // for either, and a caller who needs to tell them apart asks for the number.
        FoxDate.ToDate(Encoding.ASCII.GetBytes(date)).Should().BeNull();
    }

    [Fact]
    public void ToDate_OfYearZero_IsNothingEvenThoughTheJulianFormHasAValue()
    {
        // Year zero is a legal date to the C library and has no counterpart in a DateOnly. Reported
        // as no date rather than as a wrong one, and the divergence is deliberate.
        Julian("00000101").Should().BeGreaterThan(0);

        FoxDate.ToDate("00000101"u8).Should().BeNull();
    }

    [Fact]
    public void ToJulian_OfAFieldTooShortToHoldADate_IsIllegal()
    {
        FoxDate.ToJulian("2026"u8).Should().Be(FoxDate.Illegal);
    }

    private static long Julian(string date) => FoxDate.ToJulian(Encoding.ASCII.GetBytes(date));
}
