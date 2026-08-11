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

        // Year zero is a legal date to the C library and has no counterpart in a DateOnly, whose
        // calendar starts at year one. Reported as no date rather than as a wrong one.
        return year < 1
            ? null
            : new DateOnly(year, Digits(bytes, 4, 2), Digits(bytes, 6, 2));
    }

    /// <summary>
    /// Counts the days before the given year, treating blanks in the calendar as the C library does.
    /// </summary>
    private static long YearToDays(int year)
    {
        long y = year - 1;

        // The correction for negative years is the C library's, and is kept so that the arithmetic
        // matches for every input rather than only for the ones a real file holds.
        return (y * 365L) + (y / 4L) - (y / 100L) + (y / 400L) - (y < 0 ? 1 : 0);
    }

    /// <summary>
    /// Returns the day of the year, or minus one where the date does not exist.
    /// </summary>
    private static int DayOfYear(int year, int month, int day)
    {
        if (month < 1 || month > 12)
            return -1;

        // Year zero is 1 BC and is not a leap year, which the C library corrected for deliberately.
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
