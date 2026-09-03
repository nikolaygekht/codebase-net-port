namespace CodeBase.Net.Expressions;

/// <summary>
/// One token of an expression.
/// </summary>
/// <param name="Kind">What the token is.</param>
/// <param name="Text">The text the token was scanned from, without any delimiters.</param>
/// <param name="Row">
/// The function table row the token resolved to, for the tokens the scanner recognizes by name, and
/// minus one for the rest. An operator is recognized by matching the table, not by a symbol table of
/// the scanner's own, so the row is known the moment the token is, and carrying it costs nothing.
/// </param>
/// <param name="Alias">The table a field reference was qualified by, or null where it was not.</param>
internal readonly record struct ExpressionToken(
    ExpressionTokenKind Kind,
    string Text,
    int Row = -1,
    string? Alias = null);
