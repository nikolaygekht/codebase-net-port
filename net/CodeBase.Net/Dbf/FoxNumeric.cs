using System.Globalization;

namespace CodeBase.Net.Dbf;

/// <summary>
/// The ASCII digits of a numeric field, and the number they stand for.
///
/// A numeric or float field is stored as text: right-aligned digits, space padded, with an optional
/// sign and decimal point. The C library converts it with its own hand-rolled [c]c4atod[/c], and
/// what matters is not that this produces a reasonable double but that it produces the same double,
/// because every stored index key and every range comparison was built from that value.
///
/// The body of [c]c4atod[/c] is not in the source drop, only its declaration, so the standard
/// library's correctly-rounded parser is used and the corpus is what says whether the two agree.
/// All 224 numeric values in the corpus are compared bit for bit by the golden suite.
/// </summary>
internal static class FoxNumeric
{
    /// <summary>
    /// Converts the stored text of a numeric field to a double.
    /// </summary>
    /// <param name="bytes">The field's bytes.</param>
    /// <returns>
    /// The number, or zero where the field is blank or holds something that is not one. Zero rather
    /// than an error because a numeric field full of spaces is an ordinary empty value, and a field
    /// full of asterisks is what an overflowed write leaves behind.
    /// </returns>
    public static double ToDouble(ReadOnlySpan<byte> bytes)
    {
        Span<char> text = bytes.Length <= 64 ? stackalloc char[bytes.Length] : new char[bytes.Length];

        for (int i = 0; i < bytes.Length; i++)
            text[i] = (char)bytes[i];

        ReadOnlySpan<char> trimmed = text.Trim();

        return double.TryParse(
            trimmed,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0.0;
    }

    /// <summary>
    /// Converts the stored text of a numeric field to a whole number.
    /// </summary>
    /// <param name="bytes">The field's bytes.</param>
    /// <returns>
    /// The number with any fraction discarded, or zero where the field holds nothing that is one.
    /// </returns>
    public static int ToInt32(ReadOnlySpan<byte> bytes) => unchecked((int)(long)ToDouble(bytes));
}
