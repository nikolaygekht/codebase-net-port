using System.Buffers.Binary;
using System.Globalization;

namespace CodeBase.Net.Dbf;

/// <summary>
/// The eight bytes of a datetime field: a Julian day and a count of milliseconds.
///
/// Two little-endian 32-bit numbers. The first is the same Julian day number a date field converts
/// to, so the two types share their calendar. The second is milliseconds since midnight.
///
/// The text form the C library produces is not simply the two of them printed. It renders a blank
/// date as eight spaces rather than as a year, and it discards the milliseconds while rounding the
/// seconds up at half a second, because FoxPro ignores that part (time4assign, F4FIELD.C:1859-1890).
/// Both are reproduced here, because both are what the corpus dump holds.
/// </summary>
internal static class FoxDateTime
{
    /// <summary>
    /// How many bytes a stored datetime occupies.
    /// </summary>
    public const int Length = 8;

    /// <summary>
    /// The Julian day number of the day before the first day a date can be expressed as.
    /// </summary>
    private const int DayNumberOffset = 1721426;

    /// <summary>
    /// Reads the two numbers a datetime is stored as.
    /// </summary>
    /// <param name="bytes">The field's bytes. Only the first eight are read.</param>
    /// <returns>The Julian day and the milliseconds since midnight.</returns>
    public static (int Julian, int Milliseconds) ToParts(ReadOnlySpan<byte> bytes) =>
        (BinaryPrimitives.ReadInt32LittleEndian(bytes),
         BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]));

    /// <summary>
    /// Converts a stored datetime to a date and time.
    /// </summary>
    /// <param name="bytes">The field's bytes. Only the first eight are read.</param>
    /// <returns>
    /// The moment, with its milliseconds kept, or null where the field is blank. Blank is a Julian
    /// day of zero or less, which is how the C library's own rendering decides the same thing.
    /// </returns>
    public static DateTime? ToDateTime(ReadOnlySpan<byte> bytes)
    {
        (int julian, int milliseconds) = ToParts(bytes);
        if (julian <= 0)
            return null;

        int dayNumber = julian - DayNumberOffset;
        if (dayNumber < 0 || dayNumber > DateOnly.MaxValue.DayNumber)
            return null;

        DateTime midnight = DateOnly.FromDayNumber(dayNumber).ToDateTime(TimeOnly.MinValue);

        // A count past the end of the day is not refused: the C library does not check either, and a
        // stored value of 90000000 means the same tomorrow-morning moment to both. What is refused is
        // one so large it leaves the calendar, because AddMilliseconds would then throw a type the
        // caller cannot catch alongside the library's own (API-ERRORS.md).
        if (milliseconds > (DateTime.MaxValue - midnight).TotalMilliseconds)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"A datetime field holds {milliseconds} milliseconds past {midnight:yyyy-MM-dd}, " +
                "which is not a moment this calendar reaches.");
        }

        return midnight.AddMilliseconds(milliseconds);
    }

    /// <summary>
    /// Renders a stored datetime the way the C library renders it.
    /// </summary>
    /// <param name="bytes">The field's bytes. Only the first eight are read.</param>
    /// <returns>
    /// Eight characters of date followed by the time. The date is eight spaces when the field is
    /// blank, so a blank datetime renders as spaces and a zero time rather than as an empty string.
    /// </returns>
    public static string ToText(ReadOnlySpan<byte> bytes)
    {
        (int julian, int milliseconds) = ToParts(bytes);

        string date = "        ";
        if (julian > 0)
        {
            int dayNumber = julian - DayNumberOffset;
            if (dayNumber >= 0 && dayNumber <= DateOnly.MaxValue.DayNumber)
            {
                DateOnly value = DateOnly.FromDayNumber(dayNumber);
                date = value.Year.ToString("D4", CultureInfo.InvariantCulture) +
                       value.Month.ToString("D2", CultureInfo.InvariantCulture) +
                       value.Day.ToString("D2", CultureInfo.InvariantCulture);
            }
        }

        long seconds = milliseconds / 1000;
        long remainder = milliseconds - (seconds * 1000);

        // The reference rounds the second up at half rather than truncating, because the stored
        // milliseconds are thrown away (F4FIELD.C:1868-1873).
        if (remainder >= 500)
            seconds++;

        string time =
            (seconds / 3600).ToString("D2", CultureInfo.InvariantCulture) + ":" +
            (seconds / 60 % 60).ToString("D2", CultureInfo.InvariantCulture) + ":" +
            (seconds % 60).ToString("D2", CultureInfo.InvariantCulture);

        return date + time;
    }
}
