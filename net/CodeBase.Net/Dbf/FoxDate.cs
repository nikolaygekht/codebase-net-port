namespace CodeBase.Net.Dbf;

/// <summary>
/// The eight ASCII digits of a date field, and the Julian day number behind them.
///
/// A date is stored as [c]YYYYMMDD[/c] text, and the engine's numeric form of it is the Julian day
/// number: days since the first of January 4713 BC, so that the first of January 1981 is 2444606.
/// Both forms are needed, because asking a date field for a number is a conversion the C library
/// performs rather than refuses.
///
/// The calendar is proleptic Gregorian, applied to every year including those before the reform.
/// That is what [c]c4ytoj[/c] computes (D4DATE.C:346-361) and it is what the stored keys were built
/// with, so the arithmetic is ported rather than handed to a calendar class.
///
/// Dates before AD 1 are out of scope. A stored date is digits and spaces, so it carries no sign and
/// no year below zero can be written at all. Year zero is reachable only because a blank digit counts
/// as a zero, which makes it a malformed field rather than a date; its Julian number is reproduced to
/// match the C library, and nothing beyond that is promised.
/// </summary>
internal static class FoxDate
{
    /// <summary>
    /// How many bytes a stored date occupies.
    /// </summary>
    public const int Length = 8;

    /// <summary>
    /// The Julian day number of the last day of year zero, which anchors the conversion.
    /// </summary>
    /// <value>The C library's [c]JULIAN4ADJUSTMENT[/c] (d4defs.h:2715).</value>
    private const long JulianAdjustment = 1721425;

    /// <summary>
    /// The Julian day number a blank date reports.
    /// </summary>
    public const long Blank = 0;

    /// <summary>
    /// The Julian day number an unreadable date reports.
    /// </summary>
    public const long Illegal = -1;

    /// <summary>
    /// Days elapsed before the first of each month in a common year, indexed by month.
    /// </summary>
    private static readonly int[] MonthTotals =
        [0, 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365];

    /// <summary>
    /// Converts the stored text of a date to its Julian day number.
    /// </summary>
    /// <param name="bytes">The field's bytes. Only the first eight are read.</param>
    /// <returns>
    /// The Julian day number, zero for a blank date, or minus one for one that cannot be read. The
    /// three answers are distinct because the C library distinguishes them (D4DATE.C:670-699).
    /// </returns>
    public static long ToJulian(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Length)
            return Illegal;

        for (int i = 0; i < Length; i++)
        {
            if ((bytes[i] < (byte)'0' || bytes[i] > (byte)'9') && bytes[i] != (byte)' ')
                return Illegal;
        }

        int year = Digits(bytes, 0, 4);

        // A blank date and the all-zero date Visual FoxPro sometimes writes mean the same thing.
        if (year == 0 &&
            (bytes[..Length].SequenceEqual("        "u8) || bytes[..Length].SequenceEqual("00000000"u8)))
        {
            return Blank;
        }

        return ToJulian(year, Digits(bytes, 4, 2), Digits(bytes, 6, 2));
    }

    /// <summary>
    /// Converts a year, month and day to a Julian day number.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The month, counting from one.</param>
    /// <param name="day">The day of the month, counting from one.</param>
    /// <returns>The Julian day number, or minus one where the date does not exist.</returns>
    public static long ToJulian(int year, int month, int day)
    {
        int dayOfYear = DayOfYear(year, month, day);
        if (dayOfYear < 1)
            return Illegal;

        return YearToDays(year) + dayOfYear + JulianAdjustment;
    }

    /// <summary>
    /// Converts the stored text of a date to a date.
    /// </summary>
    /// <param name="bytes">The field's bytes. Only the first eight are read.</param>
    /// <returns>
    /// The date, or null where the field is blank or holds something that is not a date. The two are
    /// one answer here, unlike the Julian form, because there is no date to report for either.
    /// </returns>
    public static DateOnly? ToDate(ReadOnlySpan<byte> bytes)
    {
        if (ToJulian(bytes) <= 0)
            return null;

        int year = Digits(bytes, 0, 4);

        // There is no year zero to report: the calendar runs from 1 BC straight to AD 1, and a
        // DateOnly starts at year one for the same reason. The C library still gives the bytes a
        // Julian number, which ToJulian reproduces; here there is no date, so there is none to hand
        // back.
        return year < 1
            ? null
            : new DateOnly(year, Digits(bytes, 4, 2), Digits(bytes, 6, 2));
    }

    /// <summary>
    /// Converts a Julian day number back to the stored text of a date.
    /// </summary>
    /// <param name="julian">The Julian day number.</param>
    /// <param name="into">Where to write, of which the first eight bytes are used.</param>
    /// <value>
    /// A Julian day of zero or less writes eight blanks, which is how the C library renders a blank
    /// date and how it renders an unreadable one too (D4DATE.C:370-373) — the two are distinct going
    /// in and the same coming out. The year is found by dividing by the mean year length and then
    /// corrected in both directions, exactly as the reference does, because the estimate can land on
    /// either side.
    /// </value>
    public static void FromJulian(long julian, Span<byte> into)
    {
        if (julian <= 0)
        {
            into[..Length].Fill((byte)' ');
            return;
        }

        long totalDays = julian - JulianAdjustment;
        int year = (int)((double)totalDays / 365.2425) + 1;

        int dayOfYear = (int)(totalDays - YearToDays(year));
        if (dayOfYear <= 0)
        {
            year--;
            dayOfYear = (int)(totalDays - YearToDays(year));
        }

        int daysInYear = IsLeapYear(year) ? 366 : 365;
        if (dayOfYear > daysInYear)
        {
            year++;
            dayOfYear -= daysInYear;
        }

        (int month, int day) = MonthAndDay(year, dayOfYear);

        WriteDigits(into, 0, 4, year);
        WriteDigits(into, 4, 2, month);
        WriteDigits(into, 6, 2, day);
    }

    /// <summary>
    /// Splits a day of the year into its month and its day of the month.
    /// </summary>
    /// <param name="year">The year, which decides whether February has a twenty-ninth.</param>
    /// <param name="dayOfYear">The day of the year, counting from one.</param>
    /// <returns>The month and the day of the month, both counting from one.</returns>
    /// <value>
    /// The inverse of the day-of-year sum, written as the inverse rather than as a table of its own
    /// so that the two cannot drift apart. The C library's own version of this is declared and not
    /// defined in this source drop, so the corpus is what says it is right.
    /// </value>
    public static (int Month, int Day) MonthAndDay(int year, int dayOfYear)
    {
        int leap = IsLeapYear(year) ? 1 : 0;

        for (int month = 12; month >= 1; month--)
        {
            int before = MonthTotals[month] + (month <= 2 ? 0 : leap);
            if (dayOfYear > before)
                return (month, dayOfYear - before);
        }

        return (1, dayOfYear);
    }

    /// <summary>
    /// Answers whether a year has a twenty-ninth of February, the way the C library asks.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <returns>True where the year is leap.</returns>
    public static bool IsLeapYear(int year) =>
        (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;

    /// <summary>
    /// Writes a number right-aligned and zero-filled, as the C library's own conversion does.
    /// </summary>
    private static void WriteDigits(Span<byte> into, int offset, int count, int value)
    {
        for (int i = offset + count - 1; i >= offset; i--)
        {
            into[i] = (byte)('0' + (value % 10));
            value /= 10;
        }
    }

    /// <summary>
    /// Counts the days before the given year, treating blanks in the calendar as the C library does.
    /// </summary>
    private static long YearToDays(int year)
    {
        long y = year - 1;

        // The correction for negative years is the C library's (D4DATE.C:358-361), and one input
        // reaches it: year zero, where y is -1. Years below zero cannot be stored -- a date field
        // holds digits and spaces only, so there is no sign to write -- and DayOfYear refuses them
        // before this is ever called. It is kept because it is what the stored keys were built with.
        return (y * 365L) + (y / 4L) - (y / 100L) + (y / 400L) - (y < 0 ? 1 : 0);
    }

    /// <summary>
    /// Returns the day of the year, or minus one where the date does not exist.
    /// </summary>
    private static int DayOfYear(int year, int month, int day)
    {
        if (month < 1 || month > 12)
            return -1;

        // Year zero takes the 400 branch and comes out leap. That is deliberate: YearToDays(0) is
        // -366, so year zero runs 366 days, and -366 + 366 + JulianAdjustment lands exactly on the
        // last day of year zero. A common year zero would move every date from AD 1 on by a day.
        //
        // The C library's own comment here says the opposite -- "year 0 is really 1BC, and is not a
        // leap year!" (D4DATE.C:323) -- but the line beneath it was never changed to match, while
        // the same author's sibling fix in c4ytoj was. Reproduce the code, not the comment.
        int leap = ((year % 4 == 0 && year % 100 != 0) || year % 400 == 0) ? 1 : 0;
        int monthDays = MonthTotals[month + 1] - MonthTotals[month] + (month == 2 ? leap : 0);

        if (year < 0 || day < 1 || day > monthDays)
            return -1;

        return MonthTotals[month] + day + (month <= 2 ? 0 : leap);
    }

    /// <summary>
    /// Reads a run of digits, treating a space as a zero, as the C library's own conversion does.
    /// </summary>
    private static int Digits(ReadOnlySpan<byte> bytes, int offset, int count)
    {
        int value = 0;

        for (int i = offset; i < offset + count; i++)
            value = (value * 10) + (bytes[i] == (byte)' ' ? 0 : bytes[i] - (byte)'0');

        return value;
    }
}
