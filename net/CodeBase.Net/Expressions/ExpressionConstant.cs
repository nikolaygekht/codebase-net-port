using System.Globalization;
using System.Text;

namespace CodeBase.Net.Expressions;

/// <summary>
/// A literal an expression carries.
///
/// Two kinds, because the lexer produces two: a number and a string. The reference implementation
/// keeps them in one byte blob and remembers each one's offset on its node (e4parse.c:984-999); here
/// the value sits on the node itself, which says the same thing with nothing to index into.
///
/// The value is needed at parse time and not only at evaluation time. Nine functions read a
/// constant parameter while parsing, because the number decides how long their result is, so a
/// literal has to be a value the moment it is scanned rather than a piece of text kept for later.
/// </summary>
internal readonly struct ExpressionConstant
{
    private readonly double number;
    private readonly byte[]? text;

    private ExpressionConstant(double number, byte[]? text)
    {
        this.number = number;
        this.text = text;
    }

    /// <summary>
    /// Gets a value indicating whether this is a string rather than a number.
    /// </summary>
    public bool IsText => text is not null;

    /// <summary>
    /// Gets the number.
    /// </summary>
    /// <exception cref="InvalidOperationException">This constant is a string.</exception>
    public double Number => text is null
        ? number
        : throw new InvalidOperationException("This constant is a string, not a number.");

    /// <summary>
    /// Gets the string's bytes.
    /// </summary>
    /// <value>
    /// Bytes and not characters, because a string literal in an index key is compared and upper
    /// cased as stored bytes under the table's code page. The text is encoded once, while parsing,
    /// so that its byte count is what the compiled expression records as the literal's length.
    /// </value>
    /// <exception cref="InvalidOperationException">This constant is a number.</exception>
    public ReadOnlySpan<byte> Text => text
        ?? throw new InvalidOperationException("This constant is a number, not a string.");

    /// <summary>
    /// Builds a numeric constant.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The constant.</returns>
    public static ExpressionConstant FromNumber(double value) => new(value, null);

    /// <summary>
    /// Builds a string constant.
    /// </summary>
    /// <param name="value">The characters, without the delimiters they were written between.</param>
    /// <param name="encoding">The code page the expression's text was read with.</param>
    /// <returns>The constant.</returns>
    public static ExpressionConstant FromText(string value, Encoding encoding) =>
        new(0, encoding.GetBytes(value));

    /// <summary>
    /// Reads a numeric literal the way the reference implementation reads one.
    /// </summary>
    /// <param name="literal">The text the scanner took to be a number.</param>
    /// <returns>The constant.</returns>
    /// <value>
    /// The scanner has already decided where the literal ends, so what is left is a conversion that
    /// cannot fail on anything the scanner would have produced: an optional sign, digits, and at most
    /// the dots the scanner allowed through. A literal with nothing but a sign or a dot in it reads
    /// as zero, which is what the reference's own conversion of an empty digit run comes to.
    /// </value>
    public static ExpressionConstant FromLiteral(string literal) =>
        FromNumber(double.TryParse(
            literal,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : 0);
}
