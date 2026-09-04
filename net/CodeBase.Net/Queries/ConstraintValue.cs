using CodeBase.Net.Expressions;

namespace CodeBase.Net.Queries;

/// <summary>
/// One value a filter constrains a tag to, and the length it was written at.
///
/// The length is the one thing about this that is easy to get wrong and expensive to get wrong. It is
/// the length the compiled expression [b]declares[/b] for the constant, not the width of the value
/// once it has been evaluated: the literal in [c]NAME &gt; "SM"[/c] has a declared length of two even
/// though the keys it will be compared against are twenty bytes wide. That is what makes a short
/// constant a prefix, and handing a padded runtime width here instead turns every prefix range into
/// an equality that matches almost nothing.
///
/// The type is the constant's own, taken from the row overload resolution chose for it, and it is
/// what decides how two of these compare. Four types reach here: a number, a string, a truth value,
/// and a date read out of text.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 2.1 and 5.4.
/// </summary>
internal sealed class ConstraintValue
{
    private readonly byte[] bytes;

    private ConstraintValue(ExpressionValue source, int length, byte[] bytes)
    {
        Source = source;
        Length = length;
        this.bytes = bytes;
    }

    /// <summary>Gets the evaluated value this came from.</summary>
    /// <value>
    /// Kept because turning the constant into the bytes a tag's keys are compared against is the same
    /// transform that builds a key from a record, and that transform takes a value. Rebuilding one
    /// from the bytes would be a second conversion with its own chance of disagreeing.
    /// </value>
    public ExpressionValue Source { get; }

    /// <summary>Gets the type of the constant this came from.</summary>
    public ExpressionType Type => Source.Type;

    /// <summary>Gets the length the compiled expression declared for it.</summary>
    public int Length { get; }

    /// <summary>Gets its value as a number, where its type is one that compares as a number.</summary>
    public double Number => Source.Number;

    /// <summary>Gets its bytes, truncated to the declared length.</summary>
    public ReadOnlySpan<byte> Bytes => bytes;

    /// <summary>
    /// Takes the value of a constant subexpression, at the length its node declares.
    /// </summary>
    /// <param name="value">What evaluating the subexpression produced.</param>
    /// <param name="declaredLength">The length the constant's node declares.</param>
    /// <returns>The constraint value.</returns>
    /// <value>
    /// The reference copies exactly the declared number of bytes out of the evaluated result
    /// (C4CONST.C:975-989), so a shorter declaration truncates and the copy is what the seek is
    /// driven from. That is reproduced here rather than the length being recomputed from the value.
    /// </value>
    public static ConstraintValue From(ExpressionValue value, int declaredLength)
    {
        byte[] raw = value.ToRaw();
        int length = Math.Clamp(declaredLength, 0, raw.Length);

        return new ConstraintValue(value, length, length == raw.Length ? raw : raw[..length]);
    }

    /// <summary>
    /// Orders two values of one tag's key domain.
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>Negative when this sorts first, zero when they are the same, positive otherwise.</returns>
    /// <value>
    /// The reference's [c]const4less[/c] (C4CONST.C:998-1027), which is two rules and not one. A
    /// number or a date compares as a double. Text compares over the [b]shorter[/b] of the two
    /// lengths, which makes a value a prefix of the values that extend it -- and a prefix sorts
    /// [i]before[/i] them, so "SM" is less than "SMITH". That is the same rule the strict comparisons
    /// of an expression follow, where a tie on the common bytes is broken by width, and it is why a
    /// short constant can bound a range without the bound being wrong.
    /// </value>
    public int CompareTo(ConstraintValue other)
    {
        if (Type is not (ExpressionType.Character or ExpressionType.NumericAsText))
            return Number.CompareTo(other.Number);

        int common = Math.Min(Length, other.Length);
        int order = Bytes[..common].SequenceCompareTo(other.Bytes[..common]);

        return order != 0 ? order : Length.CompareTo(other.Length);
    }

    /// <summary>
    /// Returns the value as text, for a failure message.
    /// </summary>
    /// <returns>A readable form.</returns>
    public override string ToString() =>
        Type is ExpressionType.Character or ExpressionType.NumericAsText
            ? $"\"{System.Text.Encoding.Latin1.GetString(bytes)}\""
            : Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
