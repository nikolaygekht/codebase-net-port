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
        // There is no year zero to report -- the calendar runs from 1 BC to AD 1 -- but the C library
        // still gives the bytes a number, so the two forms answer differently on purpose (ADR-33).
        Julian("00000101").Should().BeGreaterThan(0);

        FoxDate.ToDate("00000101"u8).Should().BeNull();
    }

    [Fact]
    public void ToJulian_RunsYearZeroAsALeapYear_WhichIsWhatTheAnchorConstantAssumes()
    {
        // The C library computes year zero as leap (D4DATE.C:324) and its JULIAN4ADJUSTMENT depends
        // on it: make year zero common and every date from AD 1 on moves by a day. Stated as an
        // identity rather than against the constant, so it needs no magic number to be convincing.
        (Julian("00001231") - Julian("00000101")).Should().Be(365, "year zero runs 366 days");

        Julian("00000229").Should().BeGreaterThan(0, "year zero has a leap day");
    }

    [Fact]
    public void ToJulian_JoinsYearZeroToYearOneWithoutAGap()
    {
        // The seam the anchor sits on. If year zero's length were wrong this is where it would show,
        // because AD 1 is pinned independently by ToJulian_CountsDaysSinceTheEpoch.
        (Julian("00010101") - Julian("00001231")).Should().Be(1);
    }

    [Fact]
    public void ToJulian_ReadsABlankYearAsYearZero_BecauseASpaceCountsAsAZeroDigit()
    {
        // How year zero is actually reached: not by anyone writing "0000", but by a partly written
        // date field. Dates before AD 1 are out of scope (ADR-33); this is a malformed field whose
        // number still has to match the C library, because a date tag's key is built from it.
        Julian("    0229").Should().Be(Julian("00000229"));
    }

    [Fact]
    public void ToJulian_OfAFieldTooShortToHoldADate_IsIllegal()
    {
        FoxDate.ToJulian("2026"u8).Should().Be(FoxDate.Illegal);
    }

    private static long Julian(string date) => FoxDate.ToJulian(Encoding.ASCII.GetBytes(date));
}
