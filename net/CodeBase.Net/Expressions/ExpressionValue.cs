using System.Buffers.Binary;

namespace CodeBase.Net.Expressions;

/// <summary>
/// One evaluated value.
///
/// A closed union over the ten types an expression can produce, and never an object: the type is
/// carried, the payload sits in whichever of three fields the type calls for, and nothing boxes. The
/// ten are not a choice — a value enters an expression through a field, a literal or a function over
/// those, and that closure is what EXPRESSIONS.md section 1 works out.
///
/// A string value carries its bytes rather than a string, because the whole point is byte-exactness:
/// upper casing, comparison and concatenation all run over the stored bytes under the table's code
/// page, and a decode-then-re-encode round trip through UTF-16 would not be faithful for every one.
///
/// Two lengths, and the second is not redundant. The bytes are always the declared width, padded, as
/// the reference's own work buffer is. A trim shortens the value inside that width without shortening
/// it, and records how far it reaches; the containment operator and the take-from-the-end function
/// both read that shorter reach rather than the width.
///
/// Governing specification: EXPRESSIONS.md sections 1 and 7.
/// </summary>
internal readonly struct ExpressionValue
{
    private readonly double number;
    private readonly long integer;
    private readonly byte[]? bytes;

    private ExpressionValue(
        ExpressionType type,
        double number,
        long integer,
        byte[]? bytes,
        int trimmedLength,
        bool isNull)
    {
        Type = type;
        this.number = number;
        this.integer = integer;
        this.bytes = bytes;
        TrimmedLength = trimmedLength;
        IsNull = isNull;
    }

    /// <summary>
    /// Gets the type of this value.
    /// </summary>
    public ExpressionType Type { get; }

    /// <summary>
    /// Gets a value indicating whether the value is absent.
    /// </summary>
    /// <value>
    /// A null carries a type and a zero payload rather than being a case of its own, because that is
    /// how the reference propagates one: a kernel with a null operand answers zero and marks its own
    /// result null, so the type has to survive for the node above to know what it was handed.
    /// </value>
    public bool IsNull { get; }

    /// <summary>
    /// Gets how far a string value reaches, which is its width unless a trim shortened it.
    /// </summary>
    public int TrimmedLength { get; }

    /// <summary>
    /// Gets the declared width of the value.
    /// </summary>
    public int Length => bytes?.Length ?? (Type is ExpressionType.Integer or ExpressionType.Logical
        ? sizeof(int)
        : Type is ExpressionType.BinaryFloat ? sizeof(float) : sizeof(double));

    /// <summary>
    /// Gets the value as a double.
    /// </summary>
    public double Number => number;

    /// <summary>
    /// Gets the value's bytes, which is the whole of a string value.
    /// </summary>
    public ReadOnlySpan<byte> Bytes => bytes;

    /// <summary>
    /// Gets the value's bytes as far as a trim left them reaching.
    /// </summary>
    public ReadOnlySpan<byte> Trimmed => bytes is null ? default : bytes.AsSpan(0, TrimmedLength);

    /// <summary>
    /// Gets the value as a whole number, which is what a logical and an integer carry.
    /// </summary>
    public int Integer => (int)integer;

    /// <summary>
    /// Gets the value as a truth value.
    /// </summary>
    public bool Logical => integer != 0;

    /// <summary>
    /// Gets a currency value's scaled whole number, which is ten thousand times the amount.
    /// </summary>
    public long Scaled => integer;

    /// <summary>
    /// Gets a timestamp's julian day.
    /// </summary>
    public int TimestampDay => (int)(integer >> 32);

    /// <summary>
    /// Gets a timestamp's milliseconds since midnight.
    /// </summary>
    public int TimestampTime => (int)(uint)integer;

    /// <summary>Builds a numeric value.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Numeric(double value) =>
        new(ExpressionType.NumericAsDouble, value, 0, null, 0, false);

    /// <summary>Builds a date, held as its julian day.</summary>
    /// <param name="julian">The julian day.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Date(double julian) =>
        new(ExpressionType.DateAsDouble, julian, 0, null, 0, false);

    /// <summary>Builds a four-byte binary float.</summary>
    /// <param name="value">The number, narrowed to single precision.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Float(double value) =>
        new(ExpressionType.BinaryFloat, (float)value, 0, null, 0, false);

    /// <summary>Builds a truth value.</summary>
    /// <param name="value">The truth.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Logic(bool value) =>
        new(ExpressionType.Logical, 0, value ? 1 : 0, null, 0, false);

    /// <summary>Builds a four-byte integer.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Whole(int value) =>
        new(ExpressionType.Integer, 0, value, null, 0, false);

    /// <summary>Builds a currency amount from its scaled whole number.</summary>
    /// <param name="scaled">Ten thousand times the amount.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Currency(long scaled) =>
        new(ExpressionType.Currency, 0, scaled, null, 0, false);

    /// <summary>Builds a timestamp from its two halves.</summary>
    /// <param name="julian">The julian day.</param>
    /// <param name="milliseconds">Milliseconds since midnight.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Timestamp(int julian, int milliseconds) =>
        new(ExpressionType.Timestamp, 0, ((long)julian << 32) | (uint)milliseconds, null, 0, false);

    /// <summary>Builds a string value.</summary>
    /// <param name="value">The bytes, at the value's full declared width.</param>
    /// <param name="trimmedLength">How far the value reaches, or minus one for the whole width.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue Text(byte[] value, int trimmedLength = -1) =>
        new(ExpressionType.Character, 0, 0, value, trimmedLength < 0 ? value.Length : trimmedLength, false);

    /// <summary>Builds a numeric value in the text form a record stores it in.</summary>
    /// <param name="value">The bytes.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue NumericText(byte[] value) =>
        new(ExpressionType.NumericAsText, 0, 0, value, value.Length, false);

    /// <summary>Builds a date in the eight-character form a record stores it in.</summary>
    /// <param name="value">The bytes.</param>
    /// <returns>The value.</returns>
    public static ExpressionValue DateText(byte[] value) =>
        new(ExpressionType.DateAsText, 0, 0, value, value.Length, false);

    /// <summary>
    /// Writes the value in the form the reference implementation's own work buffer holds.
    /// </summary>
    /// <returns>The bytes.</returns>
    /// <value>
    /// This is what the reference hands back from an evaluation, before anything turns it into key
    /// bytes: a string is its bytes, a number is a little-endian double, a logical and an integer are
    /// a little-endian four-byte int, a binary float is four bytes, a currency is its scaled
    /// eight-byte whole number, and a timestamp is its two halves side by side. It is not the key
    /// form; the key transforms are the other half of the reference's split and have their own gate.
    /// </value>
    public byte[] ToRaw()
    {
        if (bytes is not null)
            return bytes;

        byte[] result = new byte[Length];

        switch (Type)
        {
            case ExpressionType.Logical:
            case ExpressionType.Integer:
                BinaryPrimitives.WriteInt32LittleEndian(result, Integer);
                break;

            case ExpressionType.BinaryFloat:
                BinaryPrimitives.WriteSingleLittleEndian(result, (float)number);
                break;

            case ExpressionType.Currency:
                BinaryPrimitives.WriteInt64LittleEndian(result, integer);
                break;

            case ExpressionType.Timestamp:
            case ExpressionType.TimestampWithMilliseconds:
                BinaryPrimitives.WriteInt32LittleEndian(result, TimestampDay);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(sizeof(int)), TimestampTime);
                break;

            default:
                BinaryPrimitives.WriteDoubleLittleEndian(result, number);
                break;
        }

        return result;
    }

    /// <summary>
    /// Returns this value marked absent.
    /// </summary>
    /// <returns>The value, with its payload kept and its null marking set.</returns>
    /// <value>
    /// The payload is kept rather than zeroed, because the reference marks a node null and goes on
    /// computing with whatever the operand held; what a caller must not do is read the payload
    /// without asking about the marking first.
    /// </value>
    public ExpressionValue AsNull() =>
        new(Type, number, integer, bytes, TrimmedLength, true);
}
