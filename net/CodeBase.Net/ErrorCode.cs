namespace CodeBase.Net;

/// <summary>
/// Identifies why an operation failed, using the error numbers of the original CodeBase library.
///
/// The values are the [c]e4[/c] constants of the C library, preserved exactly, so a failure here
/// can be compared against the reference implementation and against its documentation. Only the
/// codes the port actually raises are listed; more are added as the capabilities that raise them
/// are ported.
///
/// Codes that describe flow rather than failure, such as end of file or key not found, are not
/// errors and are never reported this way. They are return values.
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// The file could not be opened.
    /// </summary>
    Open = -60,

    /// <summary>
    /// The data file is invalid or corrupt.
    /// </summary>
    Data = -200,

    /// <summary>
    /// An expression names a field the table does not have.
    /// </summary>
    /// <value>
    /// The C library's [c]e4fieldName[/c] (d4defs.h:1962). Raised while parsing, because a field's
    /// loader row is chosen from its type, so a name that resolves to nothing cannot be given a node
    /// at all (e4parse.c:2452-2456). An alias qualifier the table does not answer to reaches this the
    /// same way: the name in front of the arrow is read as a field of the one table there is.
    /// </value>
    FieldName = -210,

    /// <summary>
    /// A field descriptor names a type this library does not recognize.
    /// </summary>
    FieldType = -220,

    /// <summary>
    /// The index file is invalid or corrupt.
    /// </summary>
    /// <value>
    /// The C library's [c]e4index[/c] (d4defs.h:2018). Raised where a tree contradicts itself: a key
    /// whose compression counts do not fit its length, a node reference outside the file, a header
    /// that describes an index this library cannot read.
    /// </value>
    Index = -310,

    /// <summary>
    /// A function's parameters are not separated by a comma.
    /// </summary>
    /// <value>
    /// The C library's [c]e4commaExpected[/c] (d4defs.h:1978). Raised where a parameter ends at
    /// something that is neither a comma nor the closing bracket (e4parse.c:1907-1912).
    /// </value>
    CommaExpected = -400,

    /// <summary>
    /// A parameter that has to be a numeric constant is not one.
    /// </summary>
    /// <value>
    /// The C library's [c]e4notConstant[/c] (d4defs.h:1982). Nine functions read a parameter at parse
    /// time rather than evaluating it — the width and decimals of [c]STR[/c] and [c]STRZERO[/c], the
    /// start and count of [c]SUBSTR[/c], the count of [c]LEFT[/c], [c]RIGHT[/c], [c]PADL[/c] and
    /// [c]PADR[/c], the count of [c]SPACE[/c], and the time parts of [c]DATETIME[/c] — because the
    /// number decides the result's length. A parameter that is not a literal has no value to read.
    /// </value>
    NotConstant = -425,

    /// <summary>
    /// A function was called with a number of parameters it does not take.
    /// </summary>
    /// <value>
    /// The C library's [c]e4numParms[/c] (d4defs.h:1983). Checked while parsing, against the row the
    /// name resolved to, before any type is known (e4parse.c:2064-2081).
    /// </value>
    ParameterCount = -430,

    /// <summary>
    /// An expression nests deeper than the engine evaluates.
    /// </summary>
    /// <value>
    /// The C library's [c]e4overflow[/c] (d4defs.h:1984). Overload resolution walks the expression
    /// with a simulated operand stack of 65 entries and refuses one that would outgrow it
    /// (e4parse.c:949-952).
    /// </value>
    Overflow = -440,

    /// <summary>
    /// A function call's parameter list was never closed.
    /// </summary>
    /// <value>
    /// The C library's [c]e4rightMissing[/c] (d4defs.h:1984). Raised where the expression ends
    /// while a function's parameter list is still open, which the reference distinguishes from a
    /// value simply being absent (e4parse.c:1877-1884).
    /// </value>
    RightParenthesisMissing = -450,

    /// <summary>
    /// A value in an expression has a type nothing there can accept.
    /// </summary>
    /// <value>
    /// The C library's [c]e4typeSub[/c] (d4defs.h:1986). Raised where no overload of an operation
    /// accepts the types its operands turned out to have, and also where a field's type has no loader
    /// row at all (e4parse.c:2343-2345).
    /// </value>
    TypeMismatch = -460,

    /// <summary>
    /// An expression calls a function that does not exist.
    /// </summary>
    /// <value>
    /// The C library's [c]e4unrecFunction[/c] (d4defs.h:1987). A name followed by an opening bracket
    /// that matches no row of the function table, the reference's registered calculations having been
    /// ruled out of this port's scope.
    /// </value>
    UnrecognizedFunction = -470,

    /// <summary>
    /// An expression has something where an operator was expected, and it is not one.
    /// </summary>
    /// <value>
    /// The C library's [c]e4unrecOperator[/c] (d4defs.h:1988). Raised where the text after a value
    /// begins with none of the operator names, which is what a dotted alias prefix over a name that
    /// is not an open table comes to.
    /// </value>
    UnrecognizedOperator = -480,

    /// <summary>
    /// An expression has something where a value was expected, and it does not begin one.
    /// </summary>
    /// <value>
    /// The C library's [c]e4unrecValue[/c] (d4defs.h:1989). Raised for a brace date literal, for a
    /// minus sign in front of anything but a numeric literal, for an expression that ends where a
    /// value should be, and for a field name longer than the ten characters the table allows.
    /// </value>
    UnrecognizedValue = -490,

    /// <summary>
    /// A string constant was never closed.
    /// </summary>
    /// <value>The C library's [c]e4unterminated[/c] (d4defs.h:1990).</value>
    UnterminatedString = -500,

    /// <summary>
    /// An operation was asked for in a state that has no meaning for it.
    /// </summary>
    /// <value>
    /// Raised where the caller has not done something first, such as skipping before positioning on
    /// any record. The C library's [c]e4info[/c] (d4defs.h:2040).
    /// </value>
    Info = -910,

    /// <summary>
    /// The file uses a feature this library does not implement.
    /// </summary>
    NotSupported = -1090,
}
