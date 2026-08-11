using System.Buffers.Binary;

namespace CodeBase.Net.Dbf;

/// <summary>
/// Which decoder a field's type calls for, and which questions its type refuses to answer.
///
/// There are three outcomes for every pairing of an accessor and a type, not two. Most are the
/// natural decode. Some are a conversion the C library performs across types and that has to be
/// kept because stored values were produced by it: asking a date for a number gives its Julian day,
/// and asking a currency for one goes through four decimal places. The rest are refusals, raised by
/// the reference under the error checking its shipped configuration has switched on.
///
/// Holding the matrix in one place is what makes it testable as a matrix rather than as a scattering
/// of special cases.
/// </summary>
internal static class FieldValueDecoder
{
    /// <summary>
    /// Types that report a width of their own and ignore the one the descriptor declares.
    /// </summary>
    /// <value>
    /// A short descriptor makes the decoder read on into the field that follows, which is what the
    /// C library does. Only leaving the record is an error. See Decision 10.
    /// </value>
    private static readonly Dictionary<char, int> NaturalWidths = new()
    {
        ['I'] = 4,
        ['B'] = 8,
        ['Y'] = FoxCurrency.Length,
        ['T'] = FoxDateTime.Length,
        ['7'] = FoxDateTime.Length,
        ['H'] = 4,
    };

    /// <summary>
    /// Types that refuse to be read as a number, whether whole or not.
    /// </summary>
    /// <value>
    /// The list [c]f4double[/c] and [c]f4long[/c] both carry (F4DOUBLE.C:279-291, F4LONG.C:220-231).
    /// A logical is not a number, and a datetime is two numbers rather than one.
    /// </value>
    private static readonly char[] RefuseAsNumber = ['L', 'T', '7', '0'];

    /// <summary>
    /// Returns the bytes a field occupies, which is not always the width its descriptor declares.
    /// </summary>
    /// <param name="record">The record to read from.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>That field's bytes.</returns>
    /// <exception cref="CodeBaseException">The field does not lie inside the record.</exception>
    public static ReadOnlySpan<byte> Bytes(RecordBuffer record, FieldDefinition field) =>
        record.Slice(field.RecordOffset, WidthOf(field), field.Name);

    /// <summary>
    /// Reads a field as a truth value.
    /// </summary>
    /// <param name="record">The record to read from.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>
    /// True for the four letters the C library accepts as true, and false for everything else
    /// including an empty field (F4TRUE.C:41-49).
    /// </returns>
    /// <exception cref="CodeBaseException">The field is not a logical one.</exception>
    public static bool Boolean(RecordBuffer record, FieldDefinition field)
    {
        Refuse(field, field.Type != 'L', "read as a truth value", "only a logical field holds one");

        ReadOnlySpan<byte> bytes = Bytes(record, field);

        return bytes.Length > 0 && bytes[0] is (byte)'Y' or (byte)'y' or (byte)'T' or (byte)'t';
    }

    /// <summary>
    /// Reads a field as a whole number.
    /// </summary>
    /// <param name="record">The record to read from.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>
    /// The value, converted the way [c]f4long[/c] converts it: a date gives its Julian day, a double
    /// or a currency gives its value truncated, and a text-shaped field gives what its digits say.
    /// </returns>
    /// <exception cref="CodeBaseException">The field's type refuses to be read as a number.</exception>
    public static int Int32(RecordBuffer record, FieldDefinition field)
    {
        Refuse(
            field,
            RefuseAsNumber.Contains(field.Type),
            "read as a whole number",
            "the reference implementation refuses this type");

        ReadOnlySpan<byte> bytes = Bytes(record, field);

        return field.Type switch
        {
            'I' => BinaryPrimitives.ReadInt32LittleEndian(bytes),
            'D' => unchecked((int)FoxDate.ToJulian(bytes)),
            'B' => unchecked((int)(long)BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
            'H' => unchecked((int)(long)BinaryPrimitives.ReadSingleLittleEndian(bytes)),
            'Y' => unchecked((int)(long)FoxCurrency.ToDecimal(bytes)),
            _ => FoxNumeric.ToInt32(bytes),
        };
    }

    /// <summary>
    /// Reads a field as a number.
    /// </summary>
    /// <param name="record">The record to read from.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>
    /// The value, converted the way [c]f4double[/c] converts it. Two of those conversions cross
    /// types and are kept deliberately: a date gives its Julian day (F4DOUBLE.C:296-297) and a
    /// currency goes through its four decimal places (F4DOUBLE.C:333-334).
    /// </returns>
    /// <exception cref="CodeBaseException">The field's type refuses to be read as a number.</exception>
    public static double Double(RecordBuffer record, FieldDefinition field)
    {
        Refuse(
            field,
            RefuseAsNumber.Contains(field.Type),
            "read as a number",
            "the reference implementation refuses this type");

        ReadOnlySpan<byte> bytes = Bytes(record, field);

        return field.Type switch
        {
            'D' => FoxDate.ToJulian(bytes),
            'B' => BinaryPrimitives.ReadDoubleLittleEndian(bytes),
            'H' => BinaryPrimitives.ReadSingleLittleEndian(bytes),
            'I' => BinaryPrimitives.ReadInt32LittleEndian(bytes),
            'Y' => FoxCurrency.ToDouble(bytes),
            _ => FoxNumeric.ToDouble(bytes),
        };
    }

    /// <summary>
    /// Reads a field as an exact decimal.
    /// </summary>
    /// <param name="record">The record to read from.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>The value, without the rounding a double would introduce.</returns>
    /// <exception cref="CodeBaseException">The field is not a currency one.</exception>
    public static decimal Decimal(RecordBuffer record, FieldDefinition field)
    {
        Refuse(
            field,
            field.Type != 'Y',
            "read as an exact decimal",
            "only a currency field is stored as one (F4FIELD.C:1673-1678)");

        return FoxCurrency.ToDecimal(Bytes(record, field));
    }

    /// <summary>
    /// Reads a field as a date.
    /// </summary>
    /// <param name="record">The record to read from.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>The date, or null where the field is blank or holds no date.</returns>
    /// <exception cref="CodeBaseException">The field is not a date one.</exception>
    public static DateOnly? Date(RecordBuffer record, FieldDefinition field)
    {
        Refuse(field, field.Type != 'D', "read as a date", "only a date field holds one");

        return FoxDate.ToDate(Bytes(record, field));
    }

    /// <summary>
    /// Reads a field as a date and time.
    /// </summary>
    /// <param name="record">The record to read from.</param>
    /// <param name="field">The field to read.</param>
    /// <returns>The moment, or null where the field is blank.</returns>
    /// <exception cref="CodeBaseException">The field is not a datetime one.</exception>
    public static DateTime? DateTime(RecordBuffer record, FieldDefinition field)
    {
        Refuse(
            field,
            field.Type is not ('T' or '7'),
            "read as a date and time",
            "only a datetime field holds one (F4FIELD.C:1925-1929)");

        return FoxDateTime.ToDateTime(Bytes(record, field));
    }

    /// <summary>
    /// Returns how many bytes of the record a field's value occupies.
    /// </summary>
    private static int WidthOf(FieldDefinition field) =>
        NaturalWidths.TryGetValue(field.Type, out int natural) ? natural : field.Length;

    /// <summary>
    /// Refuses a pairing the reference implementation refuses.
    /// </summary>
    private static void Refuse(FieldDefinition field, bool refused, string what, string why)
    {
        if (refused)
        {
            throw new CodeBaseException(
                ErrorCode.FieldType,
                $"Field '{field.Name}' is of type '{field.Type}' and cannot be {what}: {why}.");
        }
    }
}
