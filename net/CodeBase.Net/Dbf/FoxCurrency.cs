using System.Buffers.Binary;

namespace CodeBase.Net.Dbf;

/// <summary>
/// The eight bytes of a currency field, which hold the value multiplied by ten thousand.
///
/// A signed 64-bit little-endian integer of the value times ten thousand, so the type has exactly
/// four decimal places and cannot have any other number of them. The descriptor always says four
/// because [c]d4create[/c] writes four regardless of what the caller asked for
/// (D4CREATE.C:1569-1571), and the accessor never consults it (F4FIELD.C:1653-1697). The range that
/// follows is -922337203685477.5808 to 922337203685477.5807.
/// </summary>
internal static class FoxCurrency
{
    /// <summary>
    /// How many bytes a stored currency value occupies.
    /// </summary>
    public const int Length = 8;

    /// <summary>
    /// How much larger the stored integer is than the value.
    /// </summary>
    private const decimal Scale = 10000m;

    /// <summary>
    /// Converts a stored currency value to a decimal.
    /// </summary>
    /// <param name="bytes">The field's bytes. Only the first eight are read.</param>
    /// <returns>The value, exactly. A decimal holds every value the type can store.</returns>
    public static decimal ToDecimal(ReadOnlySpan<byte> bytes) =>
        BinaryPrimitives.ReadInt64LittleEndian(bytes) / Scale;

    /// <summary>
    /// Converts a stored currency value to a double.
    /// </summary>
    /// <param name="bytes">The field's bytes. Only the first eight are read.</param>
    /// <returns>The value, rounded to the nearest double.</returns>
    public static double ToDouble(ReadOnlySpan<byte> bytes) => (double)ToDecimal(bytes);
}
