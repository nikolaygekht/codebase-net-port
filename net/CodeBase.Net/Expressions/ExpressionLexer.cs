namespace CodeBase.Net.Expressions;

/// <summary>
/// Turns the text of an expression into tokens.
///
/// The reference implementation has no scanner: it reads a value and an operator alternately from
/// inside its parser, and what it reads depends on which of the two it is expecting. That is not a
/// detail to tidy away, because the language needs it. A minus sign begins a numeric literal where a
/// value is expected and is subtraction where an operator is expected; a dot introduces true, false
/// or negation in the first place and logical and or logical or in the second. So this scanner keeps
/// the same two states and drives itself between them, which is possible because the grammar is
/// value, operator, value.
///
/// Everything it recognizes by name it recognizes by matching the ported function table in row
/// order, exactly as the reference does. Nothing here holds a list of operators of its own.
///
/// The scanner settles what the text is, never whether it means anything: whether a function name is
/// one the table knows, whether a field exists, whether the parentheses balance, are all questions
/// for the passes after it. What it does refuse is text that begins no value at all, an unclosed
/// string, an unknown operator, and a field name longer than the table can hold, because those are
/// where the reference refuses too and this port must accept exactly what the reference accepted.
///
/// Governing specification: EXPRESSIONS.md sections 3 and 4.
/// </summary>
internal sealed class ExpressionLexer
{
    /// <summary>
    /// The longest field name a table without long-name support can hold.
    /// </summary>
    private const int ShortFieldNameLength = 10;

    private readonly string text;
    private readonly IReadOnlyCollection<string> aliases;
    private readonly bool longFieldNames;

    private int position;

    /// <summary>
    /// Whether the next thing to read is a value rather than an operator.
    /// </summary>
    private bool valueExpected = true;

    /// <summary>
    /// Whether what comes next is the start of a function's parameter.
    /// </summary>
    /// <value>
    /// True after the parenthesis that opens a parameter list and after each comma inside one, which
    /// is exactly where the reference implementation re-enters the loop that reads a parameter
    /// (e4parse.c:1868-1890). Two of that loop's behaviours are what this flag buys, and both apply
    /// at either place: a closing bracket here ends the list rather than beginning no value, and text
    /// that ends here is a list left unclosed rather than a value left out.
    /// </value>
    private bool parameterStart;

    private ExpressionLexer(string text, IReadOnlyCollection<string> aliases, bool longFieldNames)
    {
        this.text = text;
        this.aliases = aliases;
        this.longFieldNames = longFieldNames;
    }

    private bool AtEnd => position >= text.Length;

    private char Current => position < text.Length ? text[position] : '\0';

    /// <summary>
    /// Scans an expression into its tokens.
    /// </summary>
    /// <param name="text">The expression's text.</param>
    /// <param name="aliases">
    /// The table aliases a field reference may be qualified by. Empty by default, which is what a
    /// tag expression over a single table amounts to, and it is not a simplification: with no alias
    /// known, a qualified name scans exactly as the reference scans it, as a field of that name
    /// followed by whatever the qualifier's punctuation turns out to be.
    /// </param>
    /// <param name="longFieldNames">Whether the table the expression is over holds long field names.</param>
    /// <returns>The tokens, in order.</returns>
    /// <exception cref="CodeBaseException">
    /// The text is not something the reference implementation would have scanned. The message names
    /// the expression and the point the scanner reached.
    /// </exception>
    public static IReadOnlyList<ExpressionToken> Tokenize(
        string text,
        IReadOnlyCollection<string>? aliases = null,
        bool longFieldNames = false)
    {
        ExpressionLexer lexer = new(text, aliases ?? [], longFieldNames);
        return lexer.Run();
    }

    private List<ExpressionToken> Run()
    {
        List<ExpressionToken> tokens = [];

        while (true)
        {
            if (valueExpected)
                ReadValue(tokens);
            else if (!ReadOperator(tokens))
                return tokens;
        }
    }

    /// <summary>
    /// Reads what the reference reads where a value is expected (e4parse.c:2517-2573).
    /// </summary>
    private void ReadValue(List<ExpressionToken> tokens)
    {
        // The two things the start of a parameter decides before anything else is read, blanks
        // included: ending here is an unclosed list, and a closing bracket here ends the list
        // (e4parse.c:1871-1890). A blank before that bracket is therefore not the end of the list,
        // it is a value that never arrives, and this order is what reproduces that.
        bool startOfParameter = parameterStart;
        parameterStart = false;

        if (startOfParameter)
        {
            if (AtEnd)
                throw Failure(ErrorCode.RightParenthesisMissing, "a function's parameter list is not closed");

            if (Current == ')')
            {
                Emit(tokens, ExpressionTokenKind.RightParenthesis, ")");
                position++;
                valueExpected = false;
                return;
            }
        }

        // Where a value is expected the reference skips the space character and nothing else, unlike
        // the two other places it skips blanks (e4parse.c:2531 against :1187 and :2506). A tab here
        // is not whitespace; it begins no value, and it is refused as such.
        SkipRange(' ', ' ');

        if (AtEnd)
            throw Failure(ErrorCode.UnrecognizedValue, "the expression ends where a value is expected");

        switch (Current)
        {
            case '(':
                Emit(tokens, ExpressionTokenKind.LeftParenthesis, "(");
                position++;
                return;

            case '.':
            case '!':
                if (TryReadLogical(tokens))
                    return;

                // Not a logical after all, so carry on with the remaining forms. A dot can still
                // begin a numeric literal.
                break;

            case '\'':
            case '"':
            case '[':
                ReadString(tokens);
                return;
        }

        if (Current is (>= '0' and <= '9') or '-' or '+' or '.' && TryReadNumber(tokens))
            return;

        if (IsNameCharacter(Current))
        {
            ReadFunctionOrField(tokens);
            return;
        }

        throw Failure(ErrorCode.UnrecognizedValue, "this begins no value");
    }

    /// <summary>
    /// Reads what the reference reads where an operator is expected (e4parse.c:1187-1200).
    /// </summary>
    /// <returns>False where the expression has ended, which it is allowed to do here.</returns>
    private bool ReadOperator(List<ExpressionToken> tokens)
    {
        // Here every control character counts as a blank, which is the widest of the three rules.
        SkipRange('\u0001', ' ');

        if (AtEnd)
            return false;

        switch (Current)
        {
            case ')':
                Emit(tokens, ExpressionTokenKind.RightParenthesis, ")");
                position++;
                return true;

            case ',':
                Emit(tokens, ExpressionTokenKind.Comma, ",");
                position++;
                valueExpected = true;
                parameterStart = true;
                return true;
        }

        int row = FunctionTable.FindByPrefix(
            text.AsSpan(position),
            FunctionTable.FirstOperator,
            FunctionTable.LastOperator);

        if (row < 0)
            throw Failure(ErrorCode.UnrecognizedOperator, "this is no operator");

        int length = FunctionTable.Row(row).NameLength;
        Emit(tokens, ExpressionTokenKind.Operator, text.Substring(position, length), row);
        position += length;
        valueExpected = true;
        return true;
    }

    /// <summary>
    /// Reads true, false or a prefix negation, where one of them is what follows.
    /// </summary>
    /// <returns>False where the text only looked like one.</returns>
    private bool TryReadLogical(List<ExpressionToken> tokens)
    {
        int row = FunctionTable.FindByPrefix(
            text.AsSpan(position),
            FunctionTable.FirstLogical,
            FunctionTable.LastLogical);

        if (row < 0)
            return false;

        bool negation = FunctionTable.Row(row).Priority > 0;
        int length = FunctionTable.Row(row).NameLength;

        Emit(
            tokens,
            negation ? ExpressionTokenKind.PrefixNot : ExpressionTokenKind.LogicalConstant,
            text.Substring(position, length),
            row);

        position += length;

        // A negation is a prefix, so a value is still what comes next; a constant is a value, so an
        // operator is.
        valueExpected = negation;
        return true;
    }

    /// <summary>
    /// Reads a string constant (e4parse.c:2150-2189).
    /// </summary>
    private void ReadString(List<ExpressionToken> tokens)
    {
        // Three delimiters, and brackets are the only pair that differ from each other. There is no
        // escape of any kind: the scan runs to the first matching close.
        char closing = Current == '[' ? ']' : Current;
        position++;

        int start = position;
        int end = text.IndexOf(closing, start);
        if (end < 0)
            throw Failure(ErrorCode.UnterminatedString, "a string constant is not closed");

        Emit(tokens, ExpressionTokenKind.String, text[start..end]);
        position = end + 1;
        valueExpected = false;
    }

    /// <summary>
    /// Reads a numeric literal (e4parse.c:2191-2245).
    /// </summary>
    /// <returns>
    /// False where the text turned out to be a name that starts with digits, in which case the
    /// position is back where it started and the name forms are what should be tried next.
    /// </returns>
    private bool TryReadNumber(List<ExpressionToken> tokens)
    {
        int start = position;

        // The first character is taken whatever it is, which is what makes a leading sign part of
        // the literal — and, since there is no other way to reach a sign here, what makes a minus in
        // front of anything but a literal a refusal rather than a negation.
        position++;
        int length = 1;

        while (Current is (>= '0' and <= '9') or '.')
        {
            if (Current == '.')
            {
                // A dot that begins one of the dotted operators ends the number instead of
                // continuing it, which is what makes a logical operator reachable after a digit.
                if (BeginsDottedOperator())
                    break;

                // A dot followed by a letter is not a decimal point: it is the alias separator of a
                // table whose name begins with digits.
                position++;
                if (Upper(Current) is >= 'A' and <= 'Z')
                    break;

                position--;
            }

            length++;
            position++;
        }

        if (Upper(Current) is >= 'A' and <= 'Z')
        {
            position = start;
            return false;
        }

        Emit(tokens, ExpressionTokenKind.Number, text.Substring(start, length));
        valueExpected = false;
        return true;
    }

    /// <summary>
    /// Reads a name and decides from what follows it whether it is a function or a field (e4parse.c:2491-2514).
    /// </summary>
    private void ReadFunctionOrField(List<ExpressionToken> tokens)
    {
        int start = position;
        while (IsNameCharacter(Current))
            position++;

        int length = position - start;

        // Blanks between a name and its parenthesis are allowed, and here every control character
        // counts as one. The position is put back when no parenthesis follows, because a logical
        // operator may be waiting in those blanks.
        int afterName = position;
        SkipRange('\0', ' ');

        if (Current == '(')
        {
            Emit(tokens, ExpressionTokenKind.FunctionName, text.Substring(start, length));
            Emit(tokens, ExpressionTokenKind.LeftParenthesis, "(");
            position++;
            parameterStart = true;
            return;
        }

        position = afterName;
        ReadField(tokens, start, length);
    }

    /// <summary>
    /// Reads a field reference, with the alias it may be qualified by (e4parse.c:2350-2420).
    /// </summary>
    private void ReadField(List<ExpressionToken> tokens, int start, int length)
    {
        string? alias = null;

        if (Current == '.')
        {
            // This build spells an alias prefix with a dot as well as with an arrow, which collides
            // head-on with the dotted operators. The reference settles it by asking whether the name
            // is an open table and simply carrying on when it is not, so that a logical field
            // followed by .AND. still reads as a field.
            alias = MatchAlias(start, length);
        }
        else if (Current == '-' && Peek(1) == '>')
        {
            alias = MatchAlias(start, length);

            // Stepped over whether or not the name was an alias, which is what leaves an unknown
            // qualifier reading as the greater-than operator rather than as an error here.
            position++;
        }

        if (alias is not null)
        {
            position++;
            start = position;
            while (IsNameCharacter(Current))
                position++;

            length = position - start;
        }

        if (length > ShortFieldNameLength && !longFieldNames)
            throw Failure(ErrorCode.UnrecognizedValue, "a field name is longer than ten characters");

        Emit(tokens, ExpressionTokenKind.FieldName, text.Substring(start, length), alias: alias);
        valueExpected = false;
    }

    /// <summary>
    /// Answers whether a name is one of the aliases a field may be qualified by.
    /// </summary>
    private string? MatchAlias(int start, int length)
    {
        ReadOnlySpan<char> name = text.AsSpan(start, length);

        foreach (string candidate in aliases)
        {
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Answers whether the dot at the current position begins one of the three dotted operators.
    /// </summary>
    private bool BeginsDottedOperator()
    {
        ReadOnlySpan<char> rest = text.AsSpan(position);
        return rest.StartsWith(".AND.", StringComparison.OrdinalIgnoreCase)
            || rest.StartsWith(".OR.", StringComparison.OrdinalIgnoreCase)
            || rest.StartsWith(".NOT.", StringComparison.OrdinalIgnoreCase);
    }

    private char Peek(int ahead) => position + ahead < text.Length ? text[position + ahead] : '\0';

    private void SkipRange(char first, char last)
    {
        while (position < text.Length && text[position] >= first && text[position] <= last)
            position++;
    }

    private static void Emit(
        List<ExpressionToken> tokens,
        ExpressionTokenKind kind,
        string tokenText,
        int row = -1,
        string? alias = null) =>
        tokens.Add(new ExpressionToken(kind, tokenText, row, alias));

    private static bool IsNameCharacter(char ch) =>
        ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';

    private static char Upper(char ch) => ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch;

    private CodeBaseException Failure(ErrorCode code, string what) =>
        new(code, $"The expression '{text}' cannot be read: {what}, at '{text[position..]}'.");
}
