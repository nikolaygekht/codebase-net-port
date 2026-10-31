using T = CodeBase.Net.Expressions.ExpressionType;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Chooses each node's overload, types it, and works out how long its result is.
///
/// This is the reference implementation's [c]e4massage[/c] (e4parse.c:152-980), and it is a separate
/// pass for a reason the parser cannot get around: a name's overloads differ only by parameter type,
/// so which row a call resolves to cannot be known until its operands have been typed, and typing the
/// operands means walking the array. The walk carries a simulated operand stack of types and lengths,
/// exactly as the reference does, and it is a walk over the postfix array rather than a recursion over
/// a tree.
///
/// Two mechanisms do the work. Where a parameter's type does not match the row's, six [i]implicit
/// promotions[/i] rewrite the [b]producer[/b] node to a row that yields the wanted type; where none
/// applies, the row advances to its next sibling overload and the parameters are tried again, until
/// the row's operation code changes and the expression is a type error. A promotion changes a
/// producer's row and nothing else, so the node count is the same before and after this pass, which is
/// what lets the shape the parser produced be final.
///
/// One consequence of transcribing the reference rather than tidying it: a promotion updates the
/// simulated stack's length but never the producer node's own declared length, and never its recorded
/// type. So a numeric field promoted to a double still reports the field's width, which is what the
/// reference's own dump shows and what the query optimizer reads.
///
/// Governing specification: EXPRESSIONS.md sections 2.4 and 6.
/// </summary>
internal sealed class TypeResolver
{
    /// <summary>
    /// How deep the simulated operand stack goes.
    /// </summary>
    /// <value>
    /// The reference's [c]E4MAX_STACK_ENTRIES[/c] (e4expr.h:69). An expression that would need more
    /// operand slots than this is refused rather than evaluated, and the limit is on operands in
    /// flight, not on nodes.
    /// </value>
    private const int StackDepth = 65;

    /// <summary>
    /// The picture a date takes when it is formatted as text.
    /// </summary>
    /// <value>
    /// Only its length is needed here, and it is what the result length of a formatted date is. The
    /// reference sets this picture at startup and lets a caller replace it (c4code.c:1126,
    /// [c]code4dateFormatSet[/c]); this port has no such setting yet, and gains one when the
    /// formatting family is ported.
    /// </value>
    private const string DateFormat = "MM/DD/YY";

    /// <summary>
    /// How long a memo operand's value is taken to be.
    /// </summary>
    /// <value>
    /// The reference reads it from a setting whose default is not in this source drop; the corpus is
    /// the witness that the default is this, and EXPRESSIONS.md section 11 records it. A memo that is
    /// the whole of a tag's expression is capped further, at the key size.
    /// </value>
    private const int MemoOperandLength = 1024;

    private readonly string source;
    private readonly ExpressionNode[] nodes;
    private readonly TagContext? tag;
    private readonly T[] types = new T[StackDepth + 1];
    private readonly int[] lengths = new int[StackDepth + 1];
    private readonly int[] producers = new int[StackDepth + 1];

    private int top;
    private bool hasTrim;

    /// <summary>
    /// Whether a trim, a memo field or a calculation has been seen anywhere before this point.
    /// </summary>
    /// <value>
    /// The reference's [c]doneTrimMemoOrCalc[/c], and it runs over the whole expression rather than
    /// over a subtree: once one is seen, the next string concatenation becomes the overload that
    /// moves trailing blanks. That is why it is a field and not a parameter.
    /// </value>
    private bool trimmedOrMemoSeen;

    private TypeResolver(ParsedExpression parsed, TagContext? tag)
    {
        source = parsed.Source;
        nodes = [.. parsed.Nodes];
        this.tag = tag;
    }

    /// <summary>
    /// Types a parsed expression.
    /// </summary>
    /// <param name="parsed">What the parser produced.</param>
    /// <param name="tag">The tag the expression is resolved for, or null for a filter.</param>
    /// <returns>The compiled expression.</returns>
    /// <exception cref="CodeBaseException">
    /// No overload of an operation accepts the types its operands have, a call's parameter count
    /// disagrees with the row it resolved to, or the expression needs more operand slots than the
    /// engine has.
    /// </exception>
    public static CompiledExpression Resolve(ParsedExpression parsed, TagContext? tag = null)
    {
        TypeResolver resolver = new(parsed, tag);
        return resolver.Run();
    }

    private CompiledExpression Run()
    {
        for (int index = 0; index < nodes.Length; index++)
            Visit(index);

        return new CompiledExpression(source, nodes, hasTrim);
    }

    private void Visit(int index)
    {
        ExpressionNode node = nodes[index];
        int operands = node.ParameterCount;

        // The reference checks the count here as well as at parse time, and with a narrower
        // condition: a row that declares no parameters is not re-checked (e4parse.c:198-205).
        if (FunctionTable.Row(node.Row).ParameterCount != operands
            && FunctionTable.Row(node.Row).ParameterCount > 0)
        {
            throw Failure(ErrorCode.ParameterCount, "a call takes a number of parameters its overload does not");
        }

        int row = SelectOverload(node.Row, operands);
        row = MaterializeOperands(row, operands);

        top -= operands;
        T type = FunctionTable.Row(row).ReturnType;
        types[top] = type;

        int declared = node.Length;
        int immediate = node.Immediate;
        int immediate2 = node.Immediate2;
        AssignLength(row, type, node, ref declared, ref immediate, ref immediate2);

        nodes[index] = new ExpressionNode(
            row,
            operands,
            node.SubtreeSize,
            lengths[top],
            immediate,
            node.Field,
            node.Constant,
            type,
            immediate2);

        producers[top] = index;
        top++;

        if (top >= StackDepth)
            throw Failure(ErrorCode.Overflow, "the expression needs more operand slots than the engine has");
    }

    /// <summary>
    /// Walks forward through a row's sibling overloads until one accepts the operands (e4parse.c:207-292).
    /// </summary>
    /// <value>
    /// Siblings are the run of rows sharing an operation code, and the run ends where the code
    /// changes, which is what makes a type error detectable without knowing how long the run is. The
    /// terminator row's code matches nothing, so the scan cannot run off the table.
    /// </value>
    private int SelectOverload(int row, int operands)
    {
        int code = FunctionTable.Row(row).Code;

        while (true)
        {
            if (FunctionTable.Row(row).Code != code)
                throw Failure(ErrorCode.TypeMismatch, "no overload of this operation accepts these types");

            bool matched = true;

            for (int position = 0; position < operands; position++)
            {
                FunctionRow entry = FunctionTable.Row(row);
                T wanted = entry.ParameterCount < 0 ? entry.Parameter1 : entry.ParameterType(position);
                int slot = top - operands + position;

                if (types[slot] == wanted || TryPromote(slot, wanted))
                    continue;

                row++;
                matched = false;
                break;
            }

            if (matched)
                return row;
        }
    }

    /// <summary>
    /// Rewrites a producer node to a row that yields the wanted type (e4parse.c:227-289).
    /// </summary>
    /// <returns>True where one of the six promotions applied.</returns>
    /// <value>
    /// Five of the six retarget a field loader and the sixth retargets a numeric literal, which is
    /// why the sixth is the only one guarded on what the producer already is. The stack's length for
    /// the slot becomes the promoted width, and three things deliberately do not change: the
    /// producer's own declared length, the type recorded for the slot, and the number of nodes. The
    /// first two are how the reference behaves and its dump proves it; the third is what the
    /// resolution gate and the query optimizer both rely on.
    /// </value>
    private bool TryPromote(int slot, T wanted)
    {
        int promoted = (types[slot], wanted) switch
        {
            (T.DateAsText, T.DateAsDouble) => FunctionRows.FieldDateAsDouble,
            (T.NumericAsText, T.NumericAsDouble) => FunctionRows.FieldNumericAsDouble,
            (T.Integer, T.NumericAsDouble) => FunctionRows.FieldIntegerAsDouble,
            (T.Currency, T.NumericAsDouble) => FunctionRows.FieldCurrencyAsDouble,
            (T.NumericAsDouble, T.BinaryFloat) when nodes[producers[slot]].Row == FunctionRows.NumericConstant
                => FunctionRows.FloatConstant,
            (T.Integer, T.BinaryFloat) => FunctionRows.FieldIntegerAsFloat,
            _ => -1,
        };

        if (promoted < 0)
            return false;

        int producer = producers[slot];
        nodes[producer] = nodes[producer].WithRow(promoted);
        lengths[slot] = promoted is FunctionRows.FloatConstant or FunctionRows.FieldIntegerAsFloat
            ? sizeof(float)
            : sizeof(double);

        return true;
    }

    /// <summary>
    /// Points a character operand at the loader that materializes it, where the row needs the bytes
    /// laid down (e4parse.c:340-367).
    /// </summary>
    /// <returns>
    /// The row, which changes where a plain concatenation follows a trim and becomes the overload
    /// that moves trailing blanks instead.
    /// </returns>
    private int MaterializeOperands(int row, int operands)
    {
        if (!NeedsMaterializedOperands(row))
            return row;

        for (int position = 0; position < operands; position++)
        {
            int producer = producers[top - operands + position];
            if (nodes[producer].Row == FunctionRows.FieldCharacter)
                nodes[producer] = nodes[producer].WithRow(FunctionRows.FieldCharacterMaterialized);
        }

        return row == FunctionRows.Concatenate && trimmedOrMemoSeen
            ? FunctionRows.ConcatenateAfterTrim
            : row;
    }

    private static bool NeedsMaterializedOperands(int row) => row
        is FunctionRows.FieldMemo
        or FunctionRows.Concatenate
        or FunctionRows.Concatenate + 1
        or FunctionRows.ConcatenateMovingBlanks
        or FunctionRows.SubStr
        or FunctionRows.Space
        or FunctionRows.Trim
        or FunctionRows.LeftTrim
        or FunctionRows.AllTrim
        or FunctionRows.Left
        or FunctionRows.Right
        or FunctionRows.PadLeft
        or FunctionRows.PadRight
        or FunctionRows.Upper;

    /// <summary>
    /// Works out how long the node's result is, and the numbers that go with it (e4parse.c:395-833).
    /// </summary>
    /// <value>
    /// Length is decided by the result [i]type[/i] first and by the row only where the type is a
    /// string, which is the one type whose width is not fixed. On entry the slot already holds the
    /// first operand's length, which is what the rules that shorten or extend an operand read.
    /// </value>
    private void AssignLength(int row, T type, ExpressionNode node, ref int declared, ref int immediate, ref int immediate2)
    {
        switch (type)
        {
            case T.Character:
                AssignStringLength(row, node, ref declared, ref immediate, ref immediate2);
                return;

            case T.NumericAsText:
                lengths[top] = node.Field!.Value.Definition.Length;
                return;

            case T.Logical:
                AssignLogicalDetail(row, ref immediate);
                lengths[top] = sizeof(int);
                return;

            case T.Integer:
                lengths[top] = sizeof(int);
                return;

            case T.BinaryFloat:
                lengths[top] = sizeof(float);
                return;

            case T.Currency:
            case T.Timestamp:
            case T.TimestampWithMilliseconds:
            case T.NumericAsDouble:
            case T.DateAsDouble:
            case T.DateAsText:
                lengths[top] = sizeof(double);
                return;

            default:
                throw Failure(ErrorCode.TypeMismatch, "an operation produces a type this engine cannot carry");
        }
    }

    /// <summary>
    /// Works out how long a string result is, which is the only length the row decides.
    /// </summary>
    private void AssignStringLength(int row, ExpressionNode node, ref int declared, ref int immediate, ref int immediate2)
    {
        switch (row)
        {
            case FunctionRows.FieldCharacter:
            case FunctionRows.FieldCharacterMaterialized:
                lengths[top] = node.Field!.Value.Definition.Length;
                return;

            case FunctionRows.FieldMemo:
                // A memo that is the whole of a tag's expression is capped at the key size, less the
                // byte a nullable field's key spends on whether it is there (e4parse.c:433-457).
                lengths[top] = tag is not null && nodes.Length == 1
                    ? Math.Min(ExpressionKey.MaximumLength - (node.Field!.Value.Definition.IsNullable ? 1 : 0), MemoOperandLength)
                    : MemoOperandLength;
                trimmedOrMemoSeen = true;
                return;

            case FunctionRows.Concatenate:
            case FunctionRows.ConcatenateAfterTrim:
            case FunctionRows.ConcatenateMovingBlanks:
                immediate = lengths[top];
                lengths[top] += lengths[top + 1];
                return;

            case FunctionRows.Conditional:
                immediate = lengths[top + 1];
                immediate2 = lengths[top + 2];
                lengths[top] = Math.Max(lengths[top + 1], lengths[top + 2]);
                return;

            case FunctionRows.DateToSortableText:
            case FunctionRows.DateToSortableText + 1:
            case FunctionRows.Time:
                lengths[top] = sizeof(double);
                return;

            case FunctionRows.TimestampToText:
            case FunctionRows.TimestampToText + 1:
            case FunctionRows.TimestampToText + 2:
                lengths[top] = 14;
                return;

            case FunctionRows.DateToText:
            case FunctionRows.DateToText + 1:
                lengths[top] = DateFormat.Length;
                return;

            case FunctionRows.Chr:
            case FunctionRows.DeletedFlag:
                lengths[top] = sizeof(byte);
                return;

            case FunctionRows.Right:
                immediate = Clamp(immediate, lengths[top]);
                declared = Math.Min(declared, lengths[top]);
                lengths[top] = declared;
                return;

            case FunctionRows.Space:
                lengths[top] = declared;
                return;

            case FunctionRows.SubStr:
            case FunctionRows.Left:
                immediate = Clamp(immediate, lengths[top]);
                lengths[top] -= immediate;
                declared = Math.Min(declared, lengths[top]);
                lengths[top] = declared;
                return;

            case FunctionRows.Trim:
            case FunctionRows.LeftTrim:
            case FunctionRows.AllTrim:
                // The operand's length is kept: a trim shortens the value, never the field it is
                // written into. What it does change is how the key is padded, which hasTrim carries.
                trimmedOrMemoSeen = true;
                hasTrim = true;
                return;

            case FunctionRows.PadLeft:
            case FunctionRows.PadRight:
                immediate = Math.Max(declared - lengths[top], 0);
                lengths[top] = declared;
                return;

            case FunctionRows.LongToBinary:
                lengths[top] = sizeof(int);
                return;

            case FunctionRows.Upper:
            case FunctionRows.Lower:
                // Both keep the operand's width, which is what makes a tag over one of them key to
                // the field's width -- witnessed in Visual FoxPro's own header for LOWER over a
                // C(20), which is 20.
                return;

            case FunctionRows.Repeat:
                // The count times the operand's width. Visual FoxPro sizes the key the same way:
                // REPLICATE over a C(20) twice has a key length of 40.
                lengths[top] *= immediate;
                return;

            case >= FunctionRows.Descend and <= FunctionRows.LastAscend:
                AssignKeyConversionLength(row);
                return;

            default:
                lengths[top] = declared;
                return;
        }
    }

    /// <summary>
    /// Works out how long an ascending or descending key conversion's result is (e4parse.c:614-687).
    /// </summary>
    /// <value>
    /// Every one of these rows returns a string, so the length has to come from what it was handed
    /// rather than from what it returns. A character operand keeps its own width, except under a tag
    /// whose collation adds bytes per character, which widens it by that much (e4parse.c:655-672).
    /// </value>
    private void AssignKeyConversionLength(int row)
    {
        switch (FunctionTable.Row(row).Parameter1)
        {
            case T.Character:
                lengths[top] *= 1 + (tag?.PerCharacterAdd ?? 0);
                return;

            case T.DateAsDouble:
            case T.NumericAsDouble:
            case T.Timestamp:
            case T.TimestampWithMilliseconds:
                lengths[top] = sizeof(double);
                return;

            case T.Logical:
                lengths[top] = sizeof(byte);
                return;

            case T.Integer:
            case T.UnsignedInteger:
            case T.Short:
            case T.UnsignedShort:
                lengths[top] = sizeof(int);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Works out the compare length a logical-returning row needs (e4parse.c:763-824).
    /// </summary>
    /// <value>
    /// A comparison compares the shorter of its two operands' widths, which is what makes a short
    /// character constant behave as a prefix against a longer key. The rows that answer a logical
    /// from something other than a comparison have no such number, and the reference lists them
    /// explicitly rather than deriving them.
    /// </value>
    private void AssignLogicalDetail(int row, ref int immediate)
    {
        if (row is FunctionRows.FieldLogical
            or FunctionRows.Deleted
            or FunctionRows.Calculation
            or (>= FunctionRows.Empty and <= FunctionRows.Empty + 4)
            or (>= FunctionRows.FirstLogicalWord and <= FunctionRows.LastLogicalWord)
            or FunctionRows.Or
            or FunctionRows.And)
        {
            return;
        }

        immediate = Math.Min(lengths[top], lengths[top + 1]);
    }

    private static int Clamp(int value, int limit) => Math.Max(Math.Min(value, limit), 0);

    private CodeBaseException Failure(ErrorCode code, string what) =>
        new(code, $"The expression '{source}' cannot be read: {what}.");
}
