namespace CodeBase.Net.Expressions;

/// <summary>
/// What one token of an expression is.
/// </summary>
internal enum ExpressionTokenKind
{
    /// <summary>
    /// A numeric literal, carried as the text the reference implementation would convert.
    /// </summary>
    /// <value>
    /// The text and not the number, because where a numeric literal ends is the scanner's decision
    /// and what it converts to is not. Two rules make that decision interesting: a dot that begins
    /// one of the dotted operators ends the number, and a digit run followed by a letter is not a
    /// number at all.
    /// </value>
    Number,

    /// <summary>
    /// A string literal, carried without its delimiters.
    /// </summary>
    String,

    /// <summary>
    /// One of the four spellings of true and false.
    /// </summary>
    LogicalConstant,

    /// <summary>
    /// The prefix logical negation, in either of its two spellings.
    /// </summary>
    PrefixNot,

    /// <summary>
    /// A binary operator.
    /// </summary>
    Operator,

    /// <summary>
    /// The name of a function, which is a name the text follows with an opening parenthesis.
    /// </summary>
    /// <value>
    /// A name is a function only because of what follows it, so this is settled while scanning and
    /// not later. Whether the name is one the table knows is a separate question, and not this
    /// scanner's.
    /// </value>
    FunctionName,

    /// <summary>
    /// A field reference, with the alias it was qualified by where it had one.
    /// </summary>
    FieldName,

    /// <summary>
    /// An opening parenthesis, whether it groups an expression or opens a parameter list.
    /// </summary>
    LeftParenthesis,

    /// <summary>
    /// A closing parenthesis.
    /// </summary>
    RightParenthesis,

    /// <summary>
    /// A comma between a function's parameters.
    /// </summary>
    Comma,
}
