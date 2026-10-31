using System.Text;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Turns the tokens of an expression into the postfix node array a compiled expression is made of.
///
/// The grammar is small — a value, then any number of operator-and-value pairs, with one prefix
/// operator and no unary minus — and binding power is read out of the ported function table rather
/// than written down here a second time. What the reference implementation does with one flat
/// operator stack and three barrier markers, this does with one stack per nesting level and
/// recursion, which produces the same grouping because a barrier is exactly where a recursive call
/// begins and ends.
///
/// The pass is larger than a shunting yard, because the reference draws its line after the yard and
/// before the type system, and four things fall on this side of it. A field's loader row is chosen
/// from the field's type, so names are resolved here. A function's parameter count is checked against
/// its row here. Nine functions read a constant parameter here and fold it into the calling node,
/// removing the parameter's node altogether, because the number decides how long the result is. And
/// a date or timestamp formatted with two parameters is rewritten here rather than refused.
///
/// What is left for overload resolution is each node's type, the rest of each node's length, and the
/// choice among a name's overloads. It makes those by rewriting nodes, never by adding one, so
/// everything this pass counts is already final.
///
/// Governing specification: EXPRESSIONS.md sections 4 and 6.
/// </summary>
internal sealed class ExpressionParser
{
    /// <summary>
    /// Stands on the operator stack for one repeat of a variadic operator.
    /// </summary>
    /// <value>
    /// Logical and and logical or are variadic, and a run of the same one collapses into a single
    /// node with an operand for each term. The reference marks each repeat with a value its operator
    /// stack cannot otherwise hold and counts the marks when it pops the operator (e4parse.c:1374-1381,
    /// 1293-1298); this does the same with a value no row index can be.
    /// </value>
    private const int AnotherOperand = -1;

    private readonly string source;
    private readonly IReadOnlyList<ExpressionToken> tokens;
    private readonly IFieldMetadata fields;
    private readonly Encoding encoding;
    private readonly List<ExpressionNode> nodes = [];

    private int position;

    private ExpressionParser(
        string source,
        IReadOnlyList<ExpressionToken> tokens,
        IFieldMetadata fields,
        Encoding encoding)
    {
        this.source = source;
        this.tokens = tokens;
        this.fields = fields;
        this.encoding = encoding;
    }

    private bool AtEnd => position >= tokens.Count;

    private ExpressionToken Current => tokens[position];

    /// <summary>
    /// Parses an expression.
    /// </summary>
    /// <param name="text">The expression's text.</param>
    /// <param name="fields">What the expression's names refer to.</param>
    /// <param name="aliases">The table aliases a field reference may be qualified by.</param>
    /// <param name="longFieldNames">Whether the table the expression is over holds long field names.</param>
    /// <param name="encoding">
    /// The code page a string literal is encoded back to. Latin1 by default, which maps each
    /// character to the byte it was read from and is therefore what the expression's text already
    /// is: a tag header holds bytes, and the scanner is documented to see them one to one.
    /// </param>
    /// <returns>The nodes, in postfix order with the root last.</returns>
    /// <exception cref="CodeBaseException">
    /// The text is not something the reference implementation would have parsed. The message names
    /// the expression and what was wrong with it.
    /// </exception>
    public static ParsedExpression Parse(
        string text,
        IFieldMetadata fields,
        IReadOnlyCollection<string>? aliases = null,
        bool longFieldNames = false,
        Encoding? encoding = null)
    {
        IReadOnlyList<ExpressionToken> tokens = ExpressionLexer.Tokenize(text, aliases, longFieldNames);
        ExpressionParser parser = new(text, tokens, fields, encoding ?? Encoding.Latin1);
        parser.ParseExpression();
        return new ParsedExpression(text, parser.Finish());
    }

    /// <summary>
    /// Parses a value and then any number of operator-and-value pairs (e4parse.c:1304-1400).
    /// </summary>
    /// <value>
    /// Ends at a closing parenthesis, at a comma, or when the tokens run out, and never consumes the
    /// token it ended at. Those three are the reference's barriers, and each call of this owns the
    /// operator stack between two of them, which is what makes the stack local.
    /// </value>
    private void ParseExpression()
    {
        List<int> operators = [];

        ParseValue(operators);

        while (true)
        {
            if (AtEnd || Current.Kind is ExpressionTokenKind.RightParenthesis or ExpressionTokenKind.Comma)
            {
                while (operators.Count > 0)
                    EmitOperator(operators);

                return;
            }

            int row = Current.Row;
            int priority = FunctionTable.Row(row).Priority;
            position++;

            // Everything already stacked that binds at least as tightly runs first, which is what
            // makes operators of equal priority group from the left.
            while (operators.Count > 0)
            {
                int stacked = operators[^1];
                if (priority > FunctionTable.Row(stacked).Priority)
                    break;

                if (stacked == row && FunctionTable.Row(row).ParameterCount < 0)
                {
                    operators[^1] = AnotherOperand;
                    break;
                }

                EmitOperator(operators);
            }

            operators.Add(row);
            ParseValue(operators);
        }
    }

    /// <summary>
    /// Parses one value (e4parse.c:2518-2573).
    /// </summary>
    /// <param name="operators">
    /// The operator stack of the expression this value belongs to, which a prefix negation pushes
    /// itself onto so that it binds looser than anything inside its operand.
    /// </param>
    private void ParseValue(List<int> operators)
    {
        ExpressionToken token = Current;
        position++;

        switch (token.Kind)
        {
            case ExpressionTokenKind.LeftParenthesis:
                ParseExpression();
                Expect(ExpressionTokenKind.RightParenthesis);
                return;

            case ExpressionTokenKind.PrefixNot:
                // The operand is read first and the negation stacked after it, so an operator that
                // binds tighter than priority five is still free to take the operand away from it.
                ParseValue(operators);
                operators.Add(token.Row);
                return;

            case ExpressionTokenKind.LogicalConstant:
                nodes.Add(new ExpressionNode(token.Row, 0));
                return;

            case ExpressionTokenKind.Number:
                nodes.Add(new ExpressionNode(
                    FunctionRows.NumericConstant,
                    0,
                    length: sizeof(double),
                    constant: ExpressionConstant.FromLiteral(token.Text)));
                return;

            case ExpressionTokenKind.String:
            {
                ExpressionConstant literal = Literal(token.Text);
                nodes.Add(new ExpressionNode(
                    FunctionRows.StringConstant,
                    0,
                    length: literal.Text.Length,
                    constant: literal));
                return;
            }

            case ExpressionTokenKind.FieldName:
                ParseField(token);
                return;

            case ExpressionTokenKind.FunctionName:
                Expect(ExpressionTokenKind.LeftParenthesis);
                ParseFunction(token);
                return;

            default:
                throw Failure(ErrorCode.UnrecognizedValue, "this begins no value");
        }
    }

    /// <summary>
    /// Resolves a field name and gives it the loader row its type calls for (e4parse.c:2276-2347, 2350-2489).
    /// </summary>
    private void ParseField(ExpressionToken token)
    {
        FieldReference? found = fields.Find(token.Alias, token.Text);
        if (found is null)
            throw Failure(ErrorCode.FieldName, $"there is no field named '{token.Text}'");

        FieldReference field = found.Value;
        int row = LoaderRow(field.Definition.Type);
        if (row < 0)
        {
            throw Failure(
                ErrorCode.TypeMismatch,
                $"field '{field.Definition.Name}' has type '{field.Definition.Type}', which no expression can read");
        }

        nodes.Add(new ExpressionNode(row, 0, field: field));
    }

    /// <summary>
    /// Gives a field type the row that loads it, or minus one where no row does.
    /// </summary>
    /// <value>
    /// The reference maps its internal field type, which is the type letter itself, straight onto a
    /// row (e4parse.c:2276-2347). The two binary types arrive here under their own letters rather
    /// than the ones they are stored under, and map where their unmarked forms map, which is what the
    /// reference's conversion of them to plain character and memo comes to (d4defs.h:2771-2772).
    /// </value>
    private static int LoaderRow(char type) => type switch
    {
        'C' or 'Z' => FunctionRows.FieldCharacter,
        'N' or 'F' => FunctionRows.FieldNumericAsText,
        'D' => FunctionRows.FieldDateAsText,
        'L' => FunctionRows.FieldLogical,
        'M' or 'X' or 'G' => FunctionRows.FieldMemo,
        'I' => FunctionRows.FieldInteger,
        'B' => FunctionRows.FieldDouble,
        'Y' => FunctionRows.FieldCurrency,
        'T' => FunctionRows.FieldTimestamp,
        'H' => FunctionRows.FieldBinaryFloat,
        _ => -1,
    };

    /// <summary>
    /// Parses a call, folds away the parameters that had to be constants, and checks the count
    /// (e4parse.c:1984-2094).
    /// </summary>
    private void ParseFunction(ExpressionToken token)
    {
        int row = FunctionTable.FindByName(token.Text);
        if (row < 0)
            throw Failure(ErrorCode.UnrecognizedFunction, $"there is no function named '{token.Text}'");

        int operands = ParseParameters();
        int length = 0;
        int immediate = 0;

        // The order matters and is the reference's own: the three-parameter forms reduce themselves
        // to two-parameter forms, which the later tests then see.
        if (row == FunctionRows.Space)
            length = FoldedNumber();

        if (row is FunctionRows.Str or FunctionRows.StrZero)
            operands = FoldFormatWidth(operands, ref immediate, ref length);

        if (operands == 2 && row == FunctionRows.Right)
            operands = FoldRightOffset(operands, ref immediate, ref length);

        if (operands == 2 && row is FunctionRows.PadLeft or FunctionRows.PadRight)
            operands = FoldCount(operands, ref length);

        if (operands == 2 && row == FunctionRows.SubStr)
            operands = FoldSubStringLength(operands, ref immediate, ref length);

        if ((operands == 3 && row == FunctionRows.SubStr) || (operands == 2 && row == FunctionRows.Left))
            operands = FoldCount(operands, ref length);

        if (operands == 2 && row == FunctionRows.SubStr)
            operands = FoldSubStringStart(operands, ref immediate);

        if (operands is >= 3 and <= 7 && row == FunctionRows.Timestamp)
            operands = FoldTimeOfDay(ref row, operands, ref immediate);

        // The two Visual FoxPro functions whose arity is not fixed. Where a caller leaves out which
        // occurrence to find, one is meant, and it is supplied as a literal rather than defaulted
        // later so that the operand is a real node the evaluator reads like any other. The repeat
        // count goes the other way and is folded away, because the result is that many times the
        // operand's width and a key cannot be sized without knowing it.
        if (operands == 2 && row == FunctionRows.PositionOf)
            operands = SupplyFirstOccurrence();

        if (operands == 2 && row == FunctionRows.Repeat)
            operands = FoldRepeatCount(operands, ref immediate);

        int declared = FunctionTable.Row(row).ParameterCount;
        if (operands != declared && declared >= 0)
        {
            // A date or timestamp asked to format itself with a picture takes the picture off and,
            // for a date, becomes the sortable form instead of being refused.
            if (row is FunctionRows.DateToText or FunctionRows.TimestampToText && operands == 2)
            {
                Discard();
                operands--;
                if (row == FunctionRows.DateToText)
                    row = FunctionRows.DateToSortableText;
            }
            else
            {
                throw Failure(
                    ErrorCode.ParameterCount,
                    $"'{token.Text}' takes {declared} parameters and was given {operands}");
            }
        }

        nodes.Add(new ExpressionNode(row, operands, length: length, immediate: immediate));
    }

    /// <summary>
    /// Parses a call's parameters and returns how many there were (e4parse.c:1858-1917).
    /// </summary>
    private int ParseParameters()
    {
        int count = 0;

        while (true)
        {
            if (AtEnd)
                throw Failure(ErrorCode.RightParenthesisMissing, "a function's parameter list is not closed");

            if (Current.Kind == ExpressionTokenKind.RightParenthesis)
            {
                position++;
                return count;
            }

            ParseExpression();
            count++;

            if (AtEnd || Current.Kind != ExpressionTokenKind.RightParenthesis)
            {
                if (AtEnd || Current.Kind != ExpressionTokenKind.Comma)
                    throw Failure(ErrorCode.CommaExpected, "a function's parameters are not separated by a comma");

                position++;
                continue;
            }

            position++;
            return count;
        }
    }

    /// <summary>
    /// Takes the width and the decimals of a formatted number (e4parse.c:1404-1483).
    /// </summary>
    /// <value>
    /// The width defaults to ten and the decimals to none, and the two are then reconciled the way
    /// the reference reconciles them: a width that leaves no room for a decimal point and one digit
    /// takes the decimals down with it, and never below none. The wide-string overload is unreachable
    /// here, because no field this library opens has that type.
    /// </value>
    private int FoldFormatWidth(int operands, ref int immediate, ref int length)
    {
        length = 10;

        if (operands == 3)
        {
            immediate = FoldedNumber(ErrorCode.TypeMismatch);
            Discard();
            operands--;
        }

        if (operands == 2)
        {
            length = FoldedNumber(ErrorCode.TypeMismatch);
            Discard();
            operands--;
        }

        if (length < 0)
            length = 10;

        if (length <= immediate + 1)
            immediate = length - 2;

        if (immediate < 0)
            immediate = 0;

        return operands;
    }

    /// <summary>
    /// Takes the count of characters from the end of a string, and works out where they start
    /// (e4parse.c:1562-1611).
    /// </summary>
    /// <value>
    /// The offset is worked out here where the operand's width is known — from the field, or from the
    /// string literal — and left as the count itself where the operand is a subexpression whose width
    /// only the evaluator will know.
    /// </value>
    private int FoldRightOffset(int operands, ref int immediate, ref int length)
    {
        int count = FoldedNumber();
        ExpressionNode operand = nodes[^2];

        int offset = operand.Field is not null
            ? operand.Field.Value.Definition.Length - count
            : operand.Row == FunctionRows.StringConstant ? operand.Length - count : count;

        length = count < 0 ? 0 : count;
        immediate = offset;

        Discard();
        return operands - 1;
    }

    /// <summary>
    /// Takes a count that becomes the result's length (e4parse.c:1648-1678, 1721-1750).
    /// </summary>
    private int FoldCount(int operands, ref int length)
    {
        length = FoldedNumber();
        Discard();
        return operands - 1;
    }

    /// <summary>
    /// Takes a substring's start and works out how much of the operand is left after it
    /// (e4parse.c:1680-1719).
    /// </summary>
    private int FoldSubStringLength(int operands, ref int immediate, ref int length)
    {
        int start = FoldedNumber() - 1;
        ExpressionNode operand = nodes[^2];

        int remaining = (operand.Field is not null ? operand.Field.Value.Definition.Length : operand.Length) - start;

        immediate = start;
        length = remaining < 0 ? 0 : remaining;

        Discard();
        return operands - 1;
    }

    /// <summary>
    /// Adds the occurrence a search defaults to, which is the first.
    /// </summary>
    /// <returns>Three, the operand count the row declares.</returns>
    private int SupplyFirstOccurrence()
    {
        nodes.Add(new ExpressionNode(
            FunctionRows.NumericConstant,
            0,
            length: sizeof(double),
            constant: ExpressionConstant.FromNumber(1)));

        return 3;
    }

    /// <summary>
    /// Takes the number of times a string is repeated, which decides how long the result is.
    /// </summary>
    private int FoldRepeatCount(int operands, ref int immediate)
    {
        immediate = FoldedNumber();
        Discard();

        return operands - 1;
    }

    /// <summary>
    /// Takes a substring's start, where its length has already been taken (e4parse.c:1828-1856).
    /// </summary>
    private int FoldSubStringStart(int operands, ref int immediate)
    {
        immediate = FoldedNumber() - 1;
        Discard();
        return operands - 1;
    }

    /// <summary>
    /// Takes a timestamp's time of day, which is up to four constants, into one count of milliseconds
    /// (e4parse.c:1752-1826).
    /// </summary>
    /// <value>
    /// The parameters are read from the last back towards the first, so the multipliers run from
    /// milliseconds up to hours. A call carrying milliseconds resolves to the overload that keeps
    /// them, which is the row after the one the name found.
    /// </value>
    private int FoldTimeOfDay(ref int row, int operands, ref int immediate)
    {
        int milliseconds = 0;

        if (operands >= 7)
        {
            milliseconds = FoldedNumber();
            Discard();
            row++;
        }

        if (operands >= 6)
        {
            milliseconds += 1000 * FoldedNumber();
            Discard();
        }

        if (operands >= 5)
        {
            milliseconds += 60000 * FoldedNumber();
            Discard();
        }

        if (operands >= 4)
        {
            milliseconds += 3600000 * FoldedNumber();
            Discard();
        }

        immediate = milliseconds;
        return 3;
    }

    /// <summary>
    /// Reads the numeric literal the last node loads.
    /// </summary>
    /// <param name="code">
    /// What to report when it is not a literal. The reference implementation reports a type mismatch
    /// from the width of a formatted number and a missing constant from the other eight functions
    /// (e4parse.c:1434-1436 against 1577-1581), and the corpus records both, so the difference is
    /// carried rather than tidied away.
    /// </param>
    /// <returns>The literal, truncated towards zero.</returns>
    /// <exception cref="CodeBaseException">The last node does not load a numeric literal.</exception>
    private int FoldedNumber(ErrorCode code = ErrorCode.NotConstant)
    {
        ExpressionNode node = nodes[^1];
        if (node.Row != FunctionRows.NumericConstant)
            throw Failure(code, "a parameter that has to be a number written out is not one");

        return (int)node.Constant!.Value.Number;
    }

    /// <summary>
    /// Removes the last node, whose value has been folded into the node that will replace it.
    /// </summary>
    private void Discard() => nodes.RemoveAt(nodes.Count - 1);

    /// <summary>
    /// Emits the operator on top of the stack, taking its repeats with it (e4parse.c:1280-1301).
    /// </summary>
    private void EmitOperator(List<int> operators)
    {
        int row = operators[^1];
        operators.RemoveAt(operators.Count - 1);

        int operands = FunctionTable.Row(row).ParameterCount;
        if (operands < 0)
            operands = 2;

        while (operators.Count > 0 && operators[^1] == AnotherOperand)
        {
            operators.RemoveAt(operators.Count - 1);
            operands++;
        }

        nodes.Add(new ExpressionNode(row, operands));
    }

    /// <summary>
    /// Consumes the token that has to come next.
    /// </summary>
    private void Expect(ExpressionTokenKind kind)
    {
        if (AtEnd || Current.Kind != kind)
        {
            throw Failure(
                kind == ExpressionTokenKind.RightParenthesis
                    ? ErrorCode.RightParenthesisMissing
                    : ErrorCode.UnrecognizedValue,
                $"a {Spelling(kind)} was expected here");
        }

        position++;
    }

    private static string Spelling(ExpressionTokenKind kind) => kind switch
    {
        ExpressionTokenKind.RightParenthesis => "closing parenthesis",
        ExpressionTokenKind.LeftParenthesis => "opening parenthesis",
        _ => "token",
    };

    /// <summary>
    /// Fills in each node's subtree size, which is the last thing the parse pass knows.
    /// </summary>
    /// <value>
    /// A node's subtree is itself and the subtrees of the operands immediately before it, so the
    /// sizes are worked out in one forward pass over the postfix array. The reference works them out
    /// during overload resolution, because that pass already walks the array with a stack
    /// (e4parse.c:941-946); they belong to the shape rather than to the types, and having them here
    /// is what lets the shape be checked against the reference's own before any type exists.
    /// </value>
    private ExpressionNode[] Finish()
    {
        ExpressionNode[] result = new ExpressionNode[nodes.Count];
        int[] sizes = new int[nodes.Count];
        int top = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            ExpressionNode node = nodes[i];

            int size = 1;
            for (int operand = 0; operand < node.ParameterCount; operand++)
                size += sizes[--top];

            sizes[top++] = size;
            result[i] = new ExpressionNode(
                node.Row,
                node.ParameterCount,
                size,
                node.Length,
                node.Immediate,
                node.Field,
                node.Constant);
        }

        return result;
    }

    /// <summary>
    /// Encodes a string literal, refusing a character the encoding cannot hold.
    /// </summary>
    /// <value>
    /// A filter's literal is the caller's text and is encoded as the table's text is read, which a
    /// character outside the code page cannot survive. Turned into a question mark it would compare
    /// against bytes no one wrote, so the expression is refused instead.
    /// </value>
    private ExpressionConstant Literal(string text)
    {
        try
        {
            return ExpressionConstant.FromText(text, encoding);
        }
        catch (EncoderFallbackException)
        {
            throw Failure(
                ErrorCode.UnrecognizedValue,
                $"the text \"{text}\" holds a character code page {encoding.CodePage} cannot hold");
        }
    }

    private CodeBaseException Failure(ErrorCode code, string what) =>
        new(code, $"The expression '{source}' cannot be read: {what}.");
}
