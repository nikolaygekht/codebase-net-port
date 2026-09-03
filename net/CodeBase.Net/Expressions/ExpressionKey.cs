using CodeBase.Net.Cdx;
using T = CodeBase.Net.Expressions.ExpressionType;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Turns an evaluated value into the bytes an index stores for it.
///
/// This is the reference implementation's other half — [c]expr4vary[/c] returns a value and
/// [c]expr4keyConvert[/c] turns it into key bytes — and it is kept apart from the evaluator for the
/// reason the split exists: a wrong kernel and a wrong transform can cancel each other out on the
/// tags a corpus happens to hold, and a single stacked check would pass while both were wrong. So
/// this side is gated from the values the reference recorded, with the evaluator out of the circuit,
/// as well as end to end.
///
/// The transforms themselves are not new. Every one of them was ported for bare-field tags and is
/// already gated against stored keys; what is new is choosing between them from a result [i]type[/i]
/// rather than from a field's declared type, and the three things an expression key has that a field
/// key does not: a leading byte when the value can be absent, the rewrite that makes a trimmed
/// result key as though it were blank-padded, and a length computed rather than read.
///
/// Governing specification: EXPRESSIONS.md sections 9.1 to 9.4.
/// </summary>
internal static class ExpressionKey
{
    /// <summary>
    /// The longest key a Visual FoxPro index will hold.
    /// </summary>
    /// <value>The reference's [c]I4MAX_KEY_SIZE[/c] (d4defs.h:2128).</value>
    public const int MaximumLength = 240;

    /// <summary>
    /// Works out how long the key of a compiled expression is (E4EXPR.C:928-1100).
    /// </summary>
    /// <param name="expression">The compiled expression.</param>
    /// <param name="perCharacterAdd">
    /// How many extra bytes a collation adds per character, and zero for a machine collation or for
    /// an expression whose result is already collated by an ascending or descending conversion.
    /// </param>
    /// <returns>The key length in bytes.</returns>
    /// <value>
    /// Length is a property of the result [i]type[/i] and never of the function that produced it, so
    /// two expressions of one type key to the same width however differently they were written. The
    /// consequence worth knowing is that comparing a computed length against a tag header's is a
    /// strong check on result length and a blind one among the five eight-byte types.
    /// </value>
    public static int Length(CompiledExpression expression, int perCharacterAdd = 0)
    {
        int indicator = CanBeAbsent(expression) ? 1 : 0;

        int value = expression.ResultType switch
        {
            T.NumericAsText or T.DateAsText or T.NumericAsDouble or T.DateAsDouble
                or T.Currency or T.Timestamp or T.TimestampWithMilliseconds => sizeof(double),
            T.Integer or T.UnsignedInteger or T.Short or T.UnsignedShort => sizeof(int),
            T.BinaryFloat => sizeof(float),
            T.Logical => 1,
            _ => expression.Length * (1 + perCharacterAdd),
        };

        return indicator + value;
    }

    /// <summary>
    /// Answers whether the expression reads a field that can be absent.
    /// </summary>
    /// <param name="expression">The compiled expression.</param>
    /// <returns>True where one leading byte is spent saying whether the value is there.</returns>
    /// <value>
    /// Any nullable field anywhere in the expression, because the marking travels up: a kernel with
    /// an absent operand marks its own result absent, so the root can be absent if any leaf can. A
    /// candidate key is the exception the reference makes and it is a tag property, so it is applied
    /// by the caller that knows the tag rather than here.
    /// </value>
    public static bool CanBeAbsent(CompiledExpression expression)
    {
        foreach (ExpressionNode node in expression.Nodes)
        {
            if (node.Field?.Definition.IsNullable == true)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the key for one evaluated value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="hasTrim">Whether the expression contains a trim.</param>
    /// <param name="withIndicator">Whether the key carries the leading present-or-absent byte.</param>
    /// <param name="destination">Where to write, of at least the key's length.</param>
    /// <returns>How many bytes were written.</returns>
    /// <value>
    /// An absent value writes a key of nothing but zero bytes, which sorts before every present one;
    /// a present value writes [c]0x80[/c] and then its bytes (E4EXPR.C:795-807). Without the leading
    /// byte there is nowhere to say the value is absent, which is why the length depends on whether
    /// it can be.
    /// </value>
    public static int Write(ExpressionValue value, bool hasTrim, bool withIndicator, Span<byte> destination)
    {
        if (withIndicator)
        {
            if (value.IsNull)
            {
                destination.Clear();
                return destination.Length;
            }

            destination[0] = 0x80;
            return 1 + WriteValue(value, hasTrim, destination[1..]);
        }

        return WriteValue(value, hasTrim, destination);
    }

    /// <summary>
    /// Writes the value's own bytes, by its result type (EXPRESSIONS.md section 9.3).
    /// </summary>
    private static int WriteValue(ExpressionValue value, bool hasTrim, Span<byte> destination) =>
        value.Type switch
        {
            T.Character or T.Memo or T.General => WriteText(value, hasTrim, destination),
            T.NumericAsText => KeyTransform.FromDouble(FoxNumericOf(value), destination),
            T.DateAsText => KeyTransform.FromDouble(Dbf.FoxDate.ToJulian(value.Bytes), destination),
            T.Integer => KeyTransform.FromInt32(value.Integer, destination),
            T.Currency => KeyTransform.FromInt64(value.Scaled, destination),
            T.BinaryFloat => KeyTransform.FromSingle((float)value.Number, destination),
            T.Timestamp or T.TimestampWithMilliseconds =>
                KeyTransform.FromDateTime(value.TimestampDay, value.TimestampTime, destination),
            T.Logical => KeyTransform.FromLogical(value.Logical, destination),
            _ => KeyTransform.FromDouble(value.Number, destination),
        };

    /// <summary>
    /// Writes a string result, cut or padded to the key's width.
    /// </summary>
    /// <value>
    /// Where the expression contains a trim, the key's trailing zero bytes become blanks
    /// (E4EXPR.C:736-748). That is the whole reason a trim keeps the source width rather than
    /// narrowing it: the value carries zeros where the trimmed characters were, and this turns them
    /// back into the blank padding a fixed-length Visual FoxPro key has. Without it, two records
    /// whose text differs only in trailing blanks would key differently.
    /// </value>
    private static int WriteText(ExpressionValue value, bool hasTrim, Span<byte> destination)
    {
        int copied = Math.Min(value.Bytes.Length, destination.Length);
        value.Bytes[..copied].CopyTo(destination);
        destination[copied..].Fill((byte)' ');

        if (!hasTrim)
            return destination.Length;

        int at = destination.Length;
        while (at > 0 && destination[at - 1] == 0)
            at--;

        destination[at..].Fill((byte)' ');

        return destination.Length;
    }

    /// <summary>
    /// Reads the double behind a numeric held as the text a record stores.
    /// </summary>
    private static double FoxNumericOf(ExpressionValue value) => Dbf.FoxNumeric.ToDouble(value.Bytes);
}
