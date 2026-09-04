using System.Text;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;

namespace CodeBase.Net.TestUtils;

/// <summary>
/// Constraint values built by hand, for tests that need one without compiling an expression.
/// </summary>
internal static class QueryValues
{
    /// <summary>
    /// Builds a character value at its own width, which is what a literal of that length compiles to.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    internal static ConstraintValue Text(string text)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        return ConstraintValue.From(ExpressionValue.Text(bytes), bytes.Length);
    }

    /// <summary>
    /// Builds a numeric value at the eight bytes a numeric literal compiles to.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    internal static ConstraintValue Number(double value) =>
        ConstraintValue.From(ExpressionValue.Numeric(value), 8);
}
