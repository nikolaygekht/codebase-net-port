namespace CodeBase.Net.Expressions;

/// <summary>
/// One row of the built-in function table: an operation at one combination of parameter types.
///
/// The C library's table holds 193 of these for 36 distinct names, because a row is an overload and
/// not a function (e4expr.h:73-87). Overloads of one source-level operation share a
/// [c]Code[/c] and sit next to each other, and overload resolution walks forward through a run of
/// equal codes until the parameter types match, so the order rows appear in is part of the meaning.
///
/// The fields are in the order the C literal writes them, so that a row here can be read against a
/// row there without reordering anything by eye.
///
/// Governing specification: EXPRESSIONS.md sections 2.3 and 5.
/// </summary>
internal readonly struct FunctionRow
{
    /// <summary>
    /// Builds a row.
    /// </summary>
    /// <param name="name">The name, or null where the row is an unnamed overload of the row above it.</param>
    /// <param name="code">The operation this row is an overload of.</param>
    /// <param name="nameLength">The length the table records for the name.</param>
    /// <param name="priority">The binding power, for an operator, and zero for everything else.</param>
    /// <param name="returnType">The type of the value the row produces.</param>
    /// <param name="parameterCount">How many parameters the row takes, or minus one for variadic.</param>
    /// <param name="parameter1">The first parameter's type.</param>
    /// <param name="parameter2">The second parameter's type.</param>
    /// <param name="parameter3">The third parameter's type.</param>
    /// <param name="parameter4">The fourth parameter's type, which no row of the table uses.</param>
    public FunctionRow(
        string? name,
        int code,
        int nameLength,
        int priority,
        ExpressionType returnType,
        int parameterCount,
        ExpressionType parameter1 = ExpressionType.None,
        ExpressionType parameter2 = ExpressionType.None,
        ExpressionType parameter3 = ExpressionType.None,
        ExpressionType parameter4 = ExpressionType.None)
    {
        Name = name;
        Code = code;
        NameLength = nameLength;
        Priority = priority;
        ReturnType = returnType;
        ParameterCount = parameterCount;
        Parameter1 = parameter1;
        Parameter2 = parameter2;
        Parameter3 = parameter3;
        Parameter4 = parameter4;
    }

    /// <summary>
    /// Gets the name the parser matches, or null where the row cannot be reached by name.
    /// </summary>
    /// <value>
    /// Null for every row that is an unnamed overload, for the field and constant loaders, and for
    /// the two report rows this build compiles out. A lookup skips such a row without comparing.
    /// </value>
    public string? Name { get; }

    /// <summary>
    /// Gets the operation code that groups this row with its sibling overloads.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// Gets the name length the reference table records.
    /// </summary>
    /// <value>
    /// Normally the length of [c]Name[/c], and it is what a lookup compares. It is transcribed
    /// separately because the reference table does not always agree with itself: several unnamed
    /// rows carry a non-zero length, and the wide-string overload of [c]STR[/c] carries a length of
    /// one. Both are harmless, because a lookup skips an unnamed row and requires the lengths to be
    /// equal, and both are gated, because the transcription gate compares this field.
    /// </value>
    public int NameLength { get; }

    /// <summary>
    /// Gets how tightly an operator binds, where a larger number binds tighter.
    /// </summary>
    /// <value>
    /// Nine for power down to three for logical or, and zero for everything that is not an operator,
    /// which is what puts a named function at the loosest level. The parser reads binding power from
    /// here rather than from a table of its own, so that a transcription slip shows up as a parse
    /// difference instead of hiding behind a parser that happens to agree with it.
    /// </value>
    public int Priority { get; }

    /// <summary>
    /// Gets the type of the value this row produces.
    /// </summary>
    public ExpressionType ReturnType { get; }

    /// <summary>
    /// Gets how many parameters the row takes.
    /// </summary>
    /// <value>
    /// Minus one for the two variadic rows, logical and and logical or, which the parser flattens a
    /// run of into a single node.
    /// </value>
    public int ParameterCount { get; }

    /// <summary>Gets the first parameter's type.</summary>
    public ExpressionType Parameter1 { get; }

    /// <summary>Gets the second parameter's type.</summary>
    public ExpressionType Parameter2 { get; }

    /// <summary>Gets the third parameter's type.</summary>
    public ExpressionType Parameter3 { get; }

    /// <summary>Gets the fourth parameter's type.</summary>
    /// <value>
    /// Always none. The reference struct reserves four slots and no row fills more than three; the
    /// slot is transcribed so that the gate compares what the reference table holds rather than what
    /// this port assumes it holds.
    /// </value>
    public ExpressionType Parameter4 { get; }

    /// <summary>
    /// Gets a value indicating whether this row ends the table.
    /// </summary>
    /// <value>
    /// True for the single terminator row. A lookup stops at it rather than reading past the end,
    /// which is how the reference implementation bounds its own scans.
    /// </value>
    public bool IsTerminator => Code == FunctionTable.Terminator;

    /// <summary>
    /// Gets one parameter's type by position.
    /// </summary>
    /// <param name="index">The zero-based position, from zero to three.</param>
    /// <returns>The type at that position.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is not one of the four.</exception>
    public ExpressionType ParameterType(int index) => index switch
    {
        0 => Parameter1,
        1 => Parameter2,
        2 => Parameter3,
        3 => Parameter4,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}
