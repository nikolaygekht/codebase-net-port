using System.Buffers.Binary;
using System.Globalization;
using CodeBase.Net.Dbf;
using T = CodeBase.Net.Expressions.ExpressionType;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Evaluates a compiled expression over a record.
///
/// The walk is the postfix array with an operand stack, which is what the reference implementation
/// does and what the compiled form was shaped for. Each node takes its operands off the top of the
/// stack and puts one value back; the array's last node leaves the answer.
///
/// Three things about the surface, all of them the query optimizer's requirements. It evaluates
/// [b]at a node[/b], not only at the root, because deciding whether a tag can stand in for a
/// subexpression means asking what that subexpression's constant operand is worth. It needs
/// [b]no record[/b] when the subtree it is asked for is constant. And it can answer whether a node's
/// subtree [i]is[/i] constant, which is a question about rows and not about values.
///
/// One evaluator is built per compiled expression and reused across records: it owns its operand
/// stack so that a scan does not allocate one per record. It carries nothing else between calls, and
/// like everything else on a table it is not for two threads at once.
///
/// What it does not do is turn a value into key bytes. That is the other half of the reference's own
/// split and it has its own gate, driven from the values recorded here rather than from what this
/// produces, so that a wrong kernel and a wrong transform cannot cancel.
///
/// Governing specification: EXPRESSIONS.md sections 7 and 8.
/// </summary>
internal sealed class ExpressionEvaluator
{
    private readonly CompiledExpression expression;
    private readonly ExpressionValue[] stack;
    private readonly int[] sources;

    /// <summary>
    /// Builds an evaluator for one compiled expression.
    /// </summary>
    /// <param name="expression">The expression.</param>
    public ExpressionEvaluator(CompiledExpression expression)
    {
        this.expression = expression;
        stack = new ExpressionValue[expression.Nodes.Count];
        sources = new int[expression.Nodes.Count];
    }

    /// <summary>
    /// Evaluates the whole expression.
    /// </summary>
    /// <param name="context">The record and the settings to evaluate against.</param>
    /// <returns>The value.</returns>
    public ExpressionValue Evaluate(EvaluationContext context) =>
        Evaluate(expression.Nodes.Count - 1, context);

    /// <summary>
    /// Evaluates the subexpression rooted at one node.
    /// </summary>
    /// <param name="node">The index of the subexpression's root.</param>
    /// <param name="context">The record and the settings to evaluate against.</param>
    /// <returns>The value.</returns>
    /// <value>
    /// The reference does the same, and for the same caller: the optimizer reads a comparison's
    /// constant side by evaluating that side alone (C4CONST.C:971-996). A constant subtree needs no
    /// record, so the context's record may be null.
    /// </value>
    public ExpressionValue Evaluate(int node, EvaluationContext context)
    {
        IReadOnlyList<ExpressionNode> nodes = expression.Nodes;
        int first = node - nodes[node].SubtreeSize + 1;
        int top = 0;

        for (int index = first; index <= node; index++)
        {
            ExpressionNode current = nodes[index];
            top -= current.ParameterCount;
            ExpressionValue value = Apply(
                current,
                stack.AsSpan(top, current.ParameterCount),
                sources.AsSpan(top, current.ParameterCount),
                context);

            stack[top] = value;
            sources[top] = index;
            top++;
        }

        return stack[0];
    }

    /// <summary>
    /// Answers whether a node's subtree depends on no record.
    /// </summary>
    /// <param name="node">The index of the subtree's root.</param>
    /// <returns>True where the subtree can be evaluated with no record at all.</returns>
    /// <value>
    /// The reference's [c]e4isConstant[/c] (r4log.c:27-52), transcribed including what it leaves out.
    /// It answers from the row alone for a literal, and walks the subtree only for the two functions
    /// that read a date out of text, rejecting any field load or calculation inside them. Notably the
    /// narrowed float literal is [b]not[/b] in its list, so a comparison against a binary float field
    /// is predicted never optimizable; narrowing that here would be this port disagreeing with the
    /// reference about which queries can be optimized.
    /// </value>
    public bool IsConstant(int node)
    {
        IReadOnlyList<ExpressionNode> nodes = expression.Nodes;
        int row = nodes[node].Row;

        if (row is FunctionRows.NumericConstant or FunctionRows.StringConstant
            or (>= FunctionRows.FirstLogicalWord and <= FunctionRows.FirstLogicalWord + 3))
        {
            return true;
        }

        if (row is not (FunctionRows.SortableTextToDate or FunctionRows.TextToDate))
            return false;

        for (int index = node - nodes[node].SubtreeSize + 1; index <= node; index++)
        {
            if (nodes[index].Field is not null || nodes[index].Row >= FunctionRows.Calculation)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Applies one node to its operands.
    /// </summary>
    private ExpressionValue Apply(
        ExpressionNode node,
        ReadOnlySpan<ExpressionValue> operands,
        ReadOnlySpan<int> operandNodes,
        EvaluationContext context) => node.Row switch
    {
        // ---- what a value enters through -----------------------------------
        FunctionRows.FieldCharacter or FunctionRows.FieldCharacterMaterialized
            or FunctionRows.FieldLogical or FunctionRows.FieldDateAsDouble
            or FunctionRows.FieldDateAsText or FunctionRows.FieldNumericAsDouble
            or FunctionRows.FieldNumericAsText or FunctionRows.FieldCurrency
            or FunctionRows.FieldDouble or FunctionRows.FieldInteger
            or FunctionRows.FieldTimestamp or FunctionRows.FieldIntegerAsDouble
            or FunctionRows.FieldCurrencyAsDouble or FunctionRows.FieldBinaryFloat
            or FunctionRows.FieldIntegerAsFloat or FunctionRows.FieldMemo
            => LoadField(node, context),

        FunctionRows.NumericConstant => ExpressionValue.Numeric(node.Constant!.Value.Number),
        FunctionRows.FloatConstant => ExpressionValue.Float(node.Constant!.Value.Number),
        FunctionRows.StringConstant => ExpressionValue.Text(node.Constant!.Value.Text.ToArray()),

        // ---- the logical vocabulary ----------------------------------------
        FunctionRows.FirstLogicalWord or FunctionRows.FirstLogicalWord + 1 => ExpressionValue.Logic(true),
        FunctionRows.FirstLogicalWord + 2 or FunctionRows.FirstLogicalWord + 3 => ExpressionValue.Logic(false),
        FunctionRows.FirstLogicalWord + 4 or FunctionRows.LastLogicalWord => Negate(operands[0]),
        FunctionRows.Or => AnyOf(operands),
        FunctionRows.And => AllOf(operands),

        // ---- strings joined ------------------------------------------------
        FunctionRows.Concatenate => Join(node, operands),
        FunctionRows.ConcatenateAfterTrim => JoinMoving(node, operands, 0),
        FunctionRows.ConcatenateMovingBlanks => JoinMoving(node, operands, (byte)' '),

        // ---- arithmetic ----------------------------------------------------
        FunctionRows.Concatenate + 4 => Arithmetic(operands, (a, b) => a + b, T.NumericAsDouble),
        FunctionRows.Concatenate + 5 or FunctionRows.Concatenate + 6 => AddDate(operands),
        FunctionRows.Concatenate + 7 => Arithmetic(operands, (a, b) => a + b, T.BinaryFloat),
        FunctionRows.Subtract => Arithmetic(operands, (a, b) => a - b, T.NumericAsDouble),
        FunctionRows.Subtract + 1 => Arithmetic(operands, (a, b) => a - b, T.NumericAsDouble),
        FunctionRows.Subtract + 2 => SubtractFromDate(operands),
        FunctionRows.Subtract + 3 => Arithmetic(operands, (a, b) => a - b, T.BinaryFloat),
        FunctionRows.Power or FunctionRows.Power + 1
            => Arithmetic(operands, Math.Pow, T.NumericAsDouble),
        FunctionRows.Multiply => Arithmetic(operands, (a, b) => a * b, T.NumericAsDouble),
        FunctionRows.Multiply + 1 => Arithmetic(operands, (a, b) => a * b, T.BinaryFloat),
        FunctionRows.Divide => Arithmetic(operands, Divide, T.NumericAsDouble),
        FunctionRows.Divide + 1 => Arithmetic(operands, Divide, T.BinaryFloat),

        // ---- comparison ----------------------------------------------------
        >= FunctionRows.FirstComparison and <= FunctionRows.LastComparison
            => Compare(node, operands),
        FunctionRows.Contains => Contains(operands),
        FunctionRows.KeyEqual => KeyEqual(node, operands),

        // ---- the string kernels --------------------------------------------
        FunctionRows.Chr => ExpressionValue.Text([(byte)(long)operands[0].Number]),
        FunctionRows.DeletedFlag => ExpressionValue.Text([context.Deleted ? (byte)'*' : (byte)' ']),
        FunctionRows.SubStr or FunctionRows.Left => Slice(node, operands[0]),
        FunctionRows.Right => SliceFromEnd(node, operands[0]),
        FunctionRows.Space => ExpressionValue.Text(Filled(node.Length, (byte)' ')),
        FunctionRows.Upper => Upper(operands[0]),
        FunctionRows.Trim => TrimEnd(operands[0]),
        FunctionRows.LeftTrim => TrimStart(operands[0]),
        FunctionRows.AllTrim => TrimEnd(TrimStart(operands[0])),
        FunctionRows.PadLeft => Pad(node, operands[0], atStart: true),
        FunctionRows.PadRight => Pad(node, operands[0], atStart: false),
        >= FunctionRows.Conditional and <= FunctionRows.Conditional + 3 => Choose(node, operands),

        // ---- dates and the record ------------------------------------------
        FunctionRows.Str or FunctionRows.Str + 1 => FormatNumber(node, operands[0], zeroFill: false),
        FunctionRows.StrZero => FormatNumber(node, operands[0], zeroFill: true),
        FunctionRows.DateToSortableText or FunctionRows.DateToSortableText + 1
            => SortableDateText(operands[0]),
        FunctionRows.DateToText or FunctionRows.DateToText + 1
            => PictureDateText(node, operands[0], context),
        >= FunctionRows.TimestampToText and <= FunctionRows.TimestampToText + 2
            => TimestampText(operands[0]),
        FunctionRows.Timestamp or FunctionRows.Timestamp + 1 => MakeTimestamp(node, operands),
        FunctionRows.SortableTextToDate => ExpressionValue.Date(FoxDate.ToJulian(operands[0].Bytes)),
        FunctionRows.TextToDate => ExpressionValue.Date(ReadDateByPicture(operands[0].Bytes, context)),
        FunctionRows.Today => ExpressionValue.Date(context.Today),
        FunctionRows.Time => ExpressionValue.Text(TimeText(context.TimeOfDay)),
        FunctionRows.Day or FunctionRows.Day + 1 => DatePart(operands[0], 6, 2),
        FunctionRows.Month or FunctionRows.Month + 1 => DatePart(operands[0], 4, 2),
        FunctionRows.Year or FunctionRows.Year + 1 => DatePart(operands[0], 0, 4),
        FunctionRows.Deleted => ExpressionValue.Logic(context.Deleted),
        FunctionRows.RecordCount => ExpressionValue.Numeric(context.RecordCount),
        FunctionRows.RecordNumber => ExpressionValue.Numeric(context.RecordNumber),
        FunctionRows.Value => ExpressionValue.Numeric(LeadingNumber(operands[0].Bytes)),
        FunctionRows.LongToBinary => LongToBinary(operands[0]),
        >= FunctionRows.Empty and <= FunctionRows.Empty + 4
            => IsEmpty(node, operands[0], expression.Nodes[operandNodes[0]], context),
        // The band Visual FoxPro adds, gated by Visual FoxPro's own answers (ADR-43, ADR-44).
        FunctionRows.Absolute => ExpressionValue.Numeric(Math.Abs(operands[0].Number)),
        FunctionRows.Truncate => ExpressionValue.Numeric(Positive(Math.Truncate(operands[0].Number))),
        FunctionRows.Round => ExpressionValue.Numeric(
            Positive(RoundedTo(operands[0].Number, (int)operands[1].Number))),
        FunctionRows.Modulo => ExpressionValue.Numeric(
            Positive(Modulus(operands[0].Number, operands[1].Number))),
        FunctionRows.Smallest or FunctionRows.Smallest + 1 => Extreme(operands, smallest: true),
        FunctionRows.Largest or FunctionRows.Largest + 1 => Extreme(operands, smallest: false),
        FunctionRows.SquareRoot => ExpressionValue.Numeric(
            operands[0].Number < 0 ? 0 : Math.Sqrt(operands[0].Number)),
        FunctionRows.Sign => ExpressionValue.Numeric(Math.Sign(operands[0].Number)),
        FunctionRows.Length => ExpressionValue.Numeric(operands[0].TrimmedLength),
        FunctionRows.PositionOf => ExpressionValue.Numeric(PositionOf(operands)),
        FunctionRows.CharacterCode => ExpressionValue.Numeric(
            operands[0].Bytes.Length == 0 ? 0 : operands[0].Bytes[0]),
        FunctionRows.Lower => Lower(operands[0]),
        FunctionRows.Repeat => Repeat(node, operands[0]),

        >= FunctionRows.Descend and < FunctionRows.Ascend => KeyForm(node, operands[0], complement: true),
        >= FunctionRows.Ascend and <= FunctionRows.LastAscend => KeyForm(node, operands[0], complement: false),

        _ => throw new CodeBaseException(
            ErrorCode.NotSupported,
            $"The expression '{node.Row}' uses a function this version does not evaluate yet."),
    };

    // ------------------------------------------------------------------ fields

    /// <summary>
    /// Reads the field a loader row names, in the form that row's type calls for.
    /// </summary>
    private static ExpressionValue LoadField(ExpressionNode node, EvaluationContext context)
    {
        FieldDefinition field = node.Field!.Value.Definition;
        RecordBuffer record = context.Record
            ?? throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"Field '{field.Name}' cannot be read without a record.");

        ExpressionValue value = node.Row switch
        {
            FunctionRows.FieldCharacter or FunctionRows.FieldCharacterMaterialized =>
                ExpressionValue.Text(record.Field(field).ToArray()),
            FunctionRows.FieldMemo =>
                ExpressionValue.Text(MemoBytes(node, record, field, context)),
            FunctionRows.FieldNumericAsText =>
                ExpressionValue.NumericText(record.Field(field).ToArray()),
            FunctionRows.FieldDateAsText =>
                ExpressionValue.DateText(record.Field(field).ToArray()),
            FunctionRows.FieldLogical =>
                ExpressionValue.Logic(FieldValueDecoder.Boolean(record, field)),
            FunctionRows.FieldInteger =>
                ExpressionValue.Whole(FieldValueDecoder.Int32(record, field)),
            FunctionRows.FieldCurrency =>
                ExpressionValue.Currency(BinaryPrimitives.ReadInt64LittleEndian(record.Field(field))),
            FunctionRows.FieldTimestamp =>
                MakeTimestamp(FoxDateTime.ToParts(record.Field(field))),
            FunctionRows.FieldDateAsDouble =>
                ExpressionValue.Date(FoxDate.ToJulian(record.Field(field))),
            FunctionRows.FieldBinaryFloat =>
                ExpressionValue.Float(FieldValueDecoder.Double(record, field)),
            FunctionRows.FieldIntegerAsFloat =>
                ExpressionValue.Float(FieldValueDecoder.Int32(record, field)),
            _ => ExpressionValue.Numeric(FieldValueDecoder.Double(record, field)),
        };

        return IsFieldNull(record, field, context) ? value.AsNull() : value;
    }

    /// <summary>
    /// Answers whether the record marks this field absent.
    /// </summary>
    private static bool IsFieldNull(RecordBuffer record, FieldDefinition field, EvaluationContext context)
    {
        if (field.NullBit is not int bit || context.NullFlags is not FieldDefinition flags)
            return false;

        ReadOnlySpan<byte> bitmap = record.Field(flags);
        int index = bit / 8;

        return index < bitmap.Length && (bitmap[index] & (1 << (bit % 8))) != 0;
    }

    /// <summary>
    /// Reads a memo field's contents into the width a memo operand is declared at.
    /// </summary>
    /// <value>
    /// Truncated or [b]zero[/b]-padded to the declared width, because the operand's width is fixed at
    /// compile time and a memo's is not. Zero and not blank: the corpus witnesses it directly, and it
    /// matters, because key time turns a key's trailing zero bytes into blanks and a memo operand
    /// therefore keys as though it had been trimmed.
    /// </value>
    private static byte[] MemoBytes(
        ExpressionNode node,
        RecordBuffer record,
        FieldDefinition field,
        EvaluationContext context)
    {
        byte[] result = new byte[node.Length];
        int block = FieldValueDecoder.MemoBlock(record, field);

        if (block == 0 || context.Memo is null)
            return result;

        ReadOnlySpan<byte> memo = context.Memo(field).Span;
        memo[..Math.Min(memo.Length, result.Length)].CopyTo(result);

        return result;
    }

    // ------------------------------------------------------------------ logic

    private static ExpressionValue AllOf(ReadOnlySpan<ExpressionValue> operands)
    {
        foreach (ExpressionValue operand in operands)
        {
            if (!operand.Logical)
                return ExpressionValue.Logic(false);
        }

        return ExpressionValue.Logic(true);
    }

    private static ExpressionValue AnyOf(ReadOnlySpan<ExpressionValue> operands)
    {
        foreach (ExpressionValue operand in operands)
        {
            if (operand.Logical)
                return ExpressionValue.Logic(true);
        }

        return ExpressionValue.Logic(false);
    }

    /// <summary>
    /// Negates a logical value, unless it is absent.
    /// </summary>
    /// <value>
    /// An absent operand makes the result absent and false, not the opposite of the operand's forced
    /// answer (e4functi.c:2247-2261). Otherwise [c].NOT.(K = 0)[/c] would hold on every record where
    /// [c]K[/c] is null, and a filter meant to leave out the zeros would let the nulls in.
    /// </value>
    private static ExpressionValue Negate(ExpressionValue operand) =>
        operand.IsNull ? ExpressionValue.Logic(false).AsNull() : ExpressionValue.Logic(!operand.Logical);

    // ------------------------------------------------------------- arithmetic

    /// <summary>
    /// Applies a numeric operation, propagating an absent operand.
    /// </summary>
    /// <value>
    /// An absent operand on either side makes the result absent and its value zero — not the value
    /// the operation would have produced (e4functi.c:331-340). So arithmetic over a null does not
    /// merely carry a marking; it discards the arithmetic.
    /// </value>
    private static ExpressionValue Arithmetic(
        ReadOnlySpan<ExpressionValue> operands,
        Func<double, double, double> operation,
        T type)
    {
        ExpressionValue result = type == T.BinaryFloat
            ? ExpressionValue.Float(operation(operands[0].Number, operands[1].Number))
            : ExpressionValue.Numeric(operation(operands[0].Number, operands[1].Number));

        return operands[0].IsNull || operands[1].IsNull
            ? (type == T.BinaryFloat ? ExpressionValue.Float(0) : ExpressionValue.Numeric(0)).AsNull()
            : result;
    }

    /// <summary>
    /// Divides, answering zero rather than failing when the divisor is zero.
    /// </summary>
    /// <value>
    /// The reference returns zero and raises nothing (ADR-45), which is a divergence from every
    /// language's arithmetic and is what a stored key was built with.
    /// </value>
    private static double Divide(double left, double right) => right == 0 ? 0 : left / right;

    /// <summary>
    /// Adds a number of days to a date, whichever side the date is on.
    /// </summary>
    /// <value>
    /// A blank date stays blank: the reference short-circuits rather than treating zero as a day
    /// number, so a blank date plus one is still blank (e4functi.c:394-428).
    /// </value>
    private static ExpressionValue AddDate(ReadOnlySpan<ExpressionValue> operands)
    {
        if (operands[0].IsNull || operands[1].IsNull)
            return ExpressionValue.Date(0).AsNull();

        double julian = operands[0].Type == T.DateAsDouble ? operands[0].Number : operands[1].Number;
        double days = operands[0].Type == T.DateAsDouble ? operands[1].Number : operands[0].Number;

        return ExpressionValue.Date(julian <= 0 ? 0 : julian + days);
    }

    /// <summary>
    /// Subtracts a number of days from a date, with the same blank short-circuit.
    /// </summary>
    private static ExpressionValue SubtractFromDate(ReadOnlySpan<ExpressionValue> operands)
    {
        if (operands[0].IsNull || operands[1].IsNull)
            return ExpressionValue.Date(0).AsNull();

        double julian = operands[0].Number;

        return ExpressionValue.Date(julian <= 0 ? 0 : julian - operands[1].Number);
    }

    // ------------------------------------------------------------- comparison

    /// <summary>
    /// Compares two operands, by the operation the row's code names and the type its parameters take.
    /// </summary>
    /// <value>
    /// Equality and inequality are a byte comparison over the shorter operand's width, whatever the
    /// type — the reference hands the same kernel a string, a logical, a double, a date and a float
    /// (e4functi.c:139-146). Ordering is typed, and on strings it compares the common prefix and then
    /// breaks a tie by which operand was longer (EXPRESSIONS.md section 8).
    /// </value>
    private static ExpressionValue Compare(ExpressionNode node, ReadOnlySpan<ExpressionValue> operands)
    {
        int code = FunctionTable.Row(node.Row).Code;

        // An operand absent on one side only makes the answer absent, and false — except for
        // inequality, where it is true. Both absent compares as usual.
        if (operands[0].IsNull != operands[1].IsNull)
            return ExpressionValue.Logic(code == 50).AsNull();

        T type = FunctionTable.Row(node.Row).Parameter1;

        return ExpressionValue.Logic(code switch
        {
            40 => Equal(type, operands, node.Immediate),
            50 => !Equal(type, operands, node.Immediate),

            // The two inclusive forms are the strict kernels negated, and they run on their own
            // node — where the width tie-break was never set, because overload resolution sets it
            // only on the two strict rows by name (e4parse.c:800-822, e4functi.c:1608-1630,
            // 2012-2034). So they compare the common prefix and stop, while the strict forms go on
            // to break a tie by width. On a field of twenty against a one-character constant that
            // is the whole difference: the strict comparison says the field is greater, and the
            // inclusive one says it is not greater, and both are right about their own node.
            60 => Order(type, operands, node.Immediate, tieOnWidth: false) >= 0,
            70 => Order(type, operands, node.Immediate, tieOnWidth: false) <= 0,
            80 => Order(type, operands, node.Immediate, tieOnWidth: true) > 0,
            _ => Order(type, operands, node.Immediate, tieOnWidth: true) < 0,
        });
    }

    /// <summary>
    /// Answers whether two operands are byte-identical over the shorter one's width.
    /// </summary>
    private static bool Equal(T type, ReadOnlySpan<ExpressionValue> operands, int length) => type switch
    {
        T.Currency => operands[0].Scaled == operands[1].Scaled,
        T.Timestamp or T.TimestampWithMilliseconds => Order(type, operands, length) == 0,
        T.Character or T.NumericAsText or T.DateAsText => EqualBytes(operands, length),
        T.Logical or T.Integer => operands[0].Integer == operands[1].Integer,
        T.BinaryFloat => BitConverter.SingleToInt32Bits((float)operands[0].Number)
            == BitConverter.SingleToInt32Bits((float)operands[1].Number),
        _ => BitConverter.DoubleToInt64Bits(operands[0].Number)
            == BitConverter.DoubleToInt64Bits(operands[1].Number),
    };

    /// <summary>
    /// Answers whether two strings agree over the shorter one's width.
    /// </summary>
    /// <value>
    /// The common prefix and no further, which is why the reference says a four-character string
    /// equals a three-character one and says it in both directions. It is a divergence from Visual
    /// FoxPro, where the comparison runs to the end of the right operand, and ADR-45 keeps the
    /// reference's answer because the stored keys were built with it.
    /// </value>
    private static bool EqualBytes(ReadOnlySpan<ExpressionValue> operands, int length)
    {
        int common = Math.Min(length, Math.Min(operands[0].Bytes.Length, operands[1].Bytes.Length));

        return operands[0].Bytes[..common].SequenceEqual(operands[1].Bytes[..common]);
    }

    /// <summary>
    /// Orders two operands of one type, answering negative, zero or positive.
    /// </summary>
    private static int Order(
        T type,
        ReadOnlySpan<ExpressionValue> operands,
        int length,
        bool tieOnWidth = true)
    {
        switch (type)
        {
            case T.Character:
            {
                ReadOnlySpan<byte> left = operands[0].Bytes;
                ReadOnlySpan<byte> right = operands[1].Bytes;
                int common = Math.Min(length, Math.Min(left.Length, right.Length));
                int order = left[..common].SequenceCompareTo(right[..common]);

                // An equal prefix is decided by width where the tie-break applies, which is what
                // makes a short constant order after a longer string it is a prefix of.
                return order != 0 || !tieOnWidth ? order : left.Length.CompareTo(right.Length);
            }

            case T.Currency:
                return operands[0].Scaled.CompareTo(operands[1].Scaled);

            case T.Timestamp:
            case T.TimestampWithMilliseconds:
            {
                int days = operands[0].TimestampDay.CompareTo(operands[1].TimestampDay);
                return days != 0 ? days : operands[0].TimestampTime.CompareTo(operands[1].TimestampTime);
            }

            case T.Logical:
                return operands[0].Integer.CompareTo(operands[1].Integer);

            default:
                return operands[0].Number.CompareTo(operands[1].Number);
        }
    }

    /// <summary>
    /// Answers whether the left string occurs anywhere in the right one.
    /// </summary>
    /// <value>
    /// The needle is taken only as far as a trim left it reaching, which is the one place the
    /// shortened reach of a trimmed value is read rather than its width (e4functi.c:541-545).
    /// </value>
    private static ExpressionValue Contains(ReadOnlySpan<ExpressionValue> operands)
    {
        ReadOnlySpan<byte> needle = operands[0].Trimmed;
        ReadOnlySpan<byte> haystack = operands[1].Bytes;

        return ExpressionValue.Logic(needle.Length <= haystack.Length && haystack.IndexOf(needle) >= 0);
    }

    /// <summary>
    /// Compares two strings the way a key comparison would, which without a tag is a byte comparison.
    /// </summary>
    private static ExpressionValue KeyEqual(ExpressionNode node, ReadOnlySpan<ExpressionValue> operands)
    {
        if (operands[0].IsNull != operands[1].IsNull)
            return ExpressionValue.Logic(false).AsNull();

        int common = Math.Min(node.Immediate, Math.Min(operands[0].Length, operands[1].Length));

        return ExpressionValue.Logic(operands[0].Bytes[..common].SequenceEqual(operands[1].Bytes[..common]));
    }

    // ---------------------------------------------------------------- strings

    /// <summary>
    /// Joins two strings, each at its full width.
    /// </summary>
    private static ExpressionValue Join(ExpressionNode node, ReadOnlySpan<ExpressionValue> operands)
    {
        byte[] result = new byte[node.Length];
        operands[0].Bytes.CopyTo(result);
        operands[1].Bytes.CopyTo(result.AsSpan(operands[0].Bytes.Length));

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Joins two strings, moving the left one's trailing run of one character to the end
    /// (e4functi.c:444-502).
    /// </summary>
    /// <value>
    /// The character moved is a blank for the subtraction form and a zero byte for the form a trim
    /// produced, which is the same algorithm told which padding to squeeze out. The result stays the
    /// declared width, so the join is length-preserving and a key built from it is fixed-length.
    /// </value>
    private static ExpressionValue JoinMoving(
        ExpressionNode node,
        ReadOnlySpan<ExpressionValue> operands,
        byte moved)
    {
        ReadOnlySpan<byte> left = operands[0].Trimmed;
        int kept = left.Length;
        while (kept > 0 && left[kept - 1] == moved)
            kept--;

        int width = node.Length;
        int second = Math.Min(width - kept, operands[1].TrimmedLength);

        byte[] result = Filled(width, moved);
        left[..kept].CopyTo(result);
        operands[1].Bytes[..second].CopyTo(result.AsSpan(kept));

        return ExpressionValue.Text(result, kept + second);
    }

    /// <summary>
    /// Takes a run of bytes out of a string, starting where the compiled node says.
    /// </summary>
    private static ExpressionValue Slice(ExpressionNode node, ExpressionValue operand)
    {
        byte[] result = new byte[node.Length];
        ReadOnlySpan<byte> from = operand.Bytes;
        int start = Math.Min(node.Immediate, from.Length);
        int take = Math.Min(node.Length, from.Length - start);

        from.Slice(start, take).CopyTo(result);

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Takes a run of bytes from the end of a string.
    /// </summary>
    /// <value>
    /// The offset was worked out at compile time from the operand's width, but a trimmed operand is
    /// narrower than its width, so for one of those the offset is worked out again from how far it
    /// actually reaches (e4functi.c:2832-2843).
    /// </value>
    private static ExpressionValue SliceFromEnd(ExpressionNode node, ExpressionValue operand)
    {
        int start = operand.TrimmedLength < operand.Bytes.Length
            ? Math.Max(0, operand.TrimmedLength - node.Length)
            : node.Immediate;

        byte[] result = new byte[node.Length];
        ReadOnlySpan<byte> from = operand.Bytes;
        start = Math.Min(Math.Max(start, 0), from.Length);
        from.Slice(start, Math.Min(node.Length, from.Length - start)).CopyTo(result);

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Widens a string to the declared width by adding blanks at one end.
    /// </summary>
    private static ExpressionValue Pad(ExpressionNode node, ExpressionValue operand, bool atStart)
    {
        byte[] result = Filled(node.Length, (byte)' ');
        ReadOnlySpan<byte> from = operand.Bytes;
        int take = Math.Min(from.Length, node.Length);

        from[..take].CopyTo(result.AsSpan(atStart ? node.Length - take : 0));

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Upper cases a string, byte by byte over the ASCII letters.
    /// </summary>
    /// <value>
    /// Only the ASCII path is implemented. The reference chooses between that and the Windows ANSI
    /// mapping by the table's code page (e4functi.c:2878-2911), and the corpus witnesses the ASCII
    /// path on a table with no code-page mark — bytes C9 and E8 pass through unchanged. A table
    /// marked 1252 or 1250 needs the other mapping and has no corpus case; see the step's state file.
    /// </value>
    private static ExpressionValue Upper(ExpressionValue operand)
    {
        byte[] result = operand.Bytes.ToArray();

        for (int i = 0; i < result.Length; i++)
        {
            if (result[i] is >= (byte)'a' and <= (byte)'z')
                result[i] -= 32;
        }

        return ExpressionValue.Text(result, operand.TrimmedLength);
    }

    /// <summary>
    /// Removes a string's trailing blanks without narrowing it (e4functi.c:2860-2865).
    /// </summary>
    /// <value>
    /// The width is kept and the freed bytes become zero bytes, which the corpus witnesses directly:
    /// trimming six characters down to three leaves six bytes, three of them zero. What changes is
    /// how far the value reaches, which key time and the containment operator read.
    /// </value>
    private static ExpressionValue TrimEnd(ExpressionValue operand)
    {
        ReadOnlySpan<byte> from = operand.Trimmed;
        int kept = from.Length;
        while (kept > 0 && from[kept - 1] == (byte)' ')
            kept--;

        byte[] result = new byte[operand.Bytes.Length];
        from[..kept].CopyTo(result);

        return ExpressionValue.Text(result, kept);
    }

    /// <summary>
    /// Removes a string's leading blanks, shifting the rest down and zero-filling the tail.
    /// </summary>
    private static ExpressionValue TrimStart(ExpressionValue operand)
    {
        ReadOnlySpan<byte> from = operand.Trimmed;
        int skip = 0;
        while (skip < from.Length && from[skip] == (byte)' ')
            skip++;

        byte[] result = new byte[operand.Bytes.Length];
        from[skip..].CopyTo(result);

        return ExpressionValue.Text(result, from.Length - skip);
    }

    /// <summary>
    /// Chooses one of two values, laid into the width of the longer.
    /// </summary>
    private static ExpressionValue Choose(ExpressionNode node, ReadOnlySpan<ExpressionValue> operands)
    {
        ExpressionValue chosen = operands[0].Logical ? operands[1] : operands[2];

        if (chosen.Type is not T.Character)
            return chosen;

        byte[] result = new byte[node.Length];
        chosen.Bytes.CopyTo(result);

        return ExpressionValue.Text(result, chosen.TrimmedLength);
    }

    // ------------------------------------------------------------------ dates

    /// <summary>
    /// Converts a value into its key bytes, optionally complemented (e4functi.c:2969-3085).
    /// </summary>
    /// <value>
    /// Ascending conversion produces exactly the byte form an index stores, so over machine-collated
    /// character data it is the identity; descending conversion is that form with every byte
    /// complemented, which is why it maps a blank to 0xDF. A tag whose expression uses either is
    /// marked as already collated, so nothing collates the result a second time. The one departure
    /// from the key form is a truth value, which ascends to a digit rather than to a letter.
    /// </value>
    private static ExpressionValue KeyForm(ExpressionNode node, ExpressionValue operand, bool complement)
    {
        byte[] result = new byte[node.Length];

        if (operand.Type == T.Logical)
            result[0] = operand.Logical ? (byte)'1' : (byte)'0';
        else
            ExpressionKey.Write(operand, hasTrim: false, withIndicator: false, result);

        if (complement)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] = (byte)~result[i];
        }

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Turns a negative zero into a positive one.
    /// </summary>
    /// <value>
    /// Not cosmetic. Truncating or rounding a small negative number towards zero leaves a negative
    /// zero, whose bits differ from a positive zero's, and Visual FoxPro's own answer for
    /// [c]INT(-0.5)[/c] is stored as a [b]positive[/b] zero — witnessed in the oracle's stored bits
    /// rather than in its formatting, which cannot tell the two apart. A value that becomes an index
    /// key has to have the bits the engine that wrote the index had.
    /// </value>
    private static double Positive(double value) => value == 0 ? 0 : value;

    /// <summary>
    /// Rounds a number to a number of decimal places, which may be negative.
    /// </summary>
    /// <value>
    /// Half away from zero and on the [i]decimal[/i] digits, the same rule formatting a number
    /// follows: rounding 0.135 to two places gives 0.14 although the nearest double to 0.135 is
    /// below it. Visual FoxPro's own answers witness both, so the two share one route through a
    /// decimal rather than agreeing by accident. A negative number of places rounds to tens,
    /// hundreds and so on, which the oracle also covers.
    /// </value>
    private static double RoundedTo(double value, int places)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value is > 7.9e28 or < -7.9e28)
            return value;

        if (places >= 0)
            return (double)Math.Round((decimal)value, Math.Min(places, 28), MidpointRounding.AwayFromZero);

        decimal scale = 1;
        for (int i = 0; i < -places && i < 28; i++)
            scale *= 10;

        return (double)(Math.Round((decimal)value / scale, 0, MidpointRounding.AwayFromZero) * scale);
    }

    /// <summary>
    /// The remainder of a division, taking its sign from the divisor.
    /// </summary>
    /// <value>
    /// Floored rather than truncated, which is where it differs from the remainder operator of most
    /// languages: five modulo minus three is minus one, not two. Visual FoxPro's answers for all four
    /// sign combinations are in the oracle, which is what settles it. A zero divisor answers zero,
    /// like division does, and Visual FoxPro raises there instead -- unwitnessed and recorded as such.
    /// </value>
    private static double Modulus(double value, double divisor) =>
        divisor == 0 ? 0 : value - (divisor * Math.Floor(value / divisor));

    /// <summary>
    /// The smallest or largest of a variadic call's operands.
    /// </summary>
    /// <value>
    /// Two rows each, because Visual FoxPro compares strings this way as well as numbers, and
    /// overload resolution picks between them by what the operands turned out to be. Strings compare
    /// by their bytes, which is what a machine-collated key does too.
    /// </value>
    private static ExpressionValue Extreme(ReadOnlySpan<ExpressionValue> operands, bool smallest)
    {
        int chosen = 0;

        for (int i = 1; i < operands.Length; i++)
        {
            int order = operands[0].Type == T.Character
                ? operands[i].Bytes.SequenceCompareTo(operands[chosen].Bytes)
                : operands[i].Number.CompareTo(operands[chosen].Number);

            if (smallest ? order < 0 : order > 0)
                chosen = i;
        }

        return operands[chosen];
    }

    /// <summary>
    /// Where one string occurs inside another, counting from one, and zero where it does not.
    /// </summary>
    /// <value>
    /// The third operand says which occurrence is wanted, and the parser supplies a literal one where
    /// a caller left it out. The needle is taken only as far as a trim left it reaching, as the
    /// containment operator does.
    /// </value>
    private static int PositionOf(ReadOnlySpan<ExpressionValue> operands)
    {
        ReadOnlySpan<byte> needle = operands[0].Trimmed;
        ReadOnlySpan<byte> haystack = operands[1].Bytes;
        int wanted = (int)operands[2].Number;

        if (needle.Length == 0 || wanted < 1)
            return 0;

        int seen = 0;
        for (int at = 0; at + needle.Length <= haystack.Length; at++)
        {
            if (!haystack.Slice(at, needle.Length).SequenceEqual(needle))
                continue;

            if (++seen == wanted)
                return at + 1;
        }

        return 0;
    }

    /// <summary>
    /// Lower cases a string, byte by byte over the ASCII letters.
    /// </summary>
    /// <value>
    /// The mirror of upper casing, and it has the same gap: Visual FoxPro maps by the table's code
    /// page, and only the ASCII path is implemented here. The oracle's one case above 0x7F is
    /// excluded by name in the gate rather than passed by accident.
    /// </value>
    private static ExpressionValue Lower(ExpressionValue operand)
    {
        byte[] result = operand.Bytes.ToArray();

        for (int i = 0; i < result.Length; i++)
        {
            if (result[i] is >= (byte)'A' and <= (byte)'Z')
                result[i] += 32;
        }

        return ExpressionValue.Text(result, operand.TrimmedLength);
    }

    /// <summary>
    /// A string repeated as many times as the compiled node says.
    /// </summary>
    private static ExpressionValue Repeat(ExpressionNode node, ExpressionValue operand)
    {
        byte[] result = new byte[node.Length];

        for (int at = 0; at + operand.Bytes.Length <= result.Length; at += operand.Bytes.Length)
        {
            if (operand.Bytes.Length == 0)
                break;

            operand.Bytes.CopyTo(result.AsSpan(at));
        }

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Formats a number as right-justified text of the declared width (e4functi.c:2675-2690).
    /// </summary>
    /// <value>
    /// Two things here are not what a C library's own formatting would give, and both are witnessed
    /// rather than read, because the routine behind them has no defining file in this source drop.
    /// It rounds [b]half away from zero[/b] and it rounds the [b]decimal[/b] digits rather than the
    /// exact binary value, so 0.135 at two decimals is 0.14 even though the nearest double to 0.135
    /// is below it. Going through a decimal is what reproduces that. And a result too wide for the
    /// field does not truncate or overflow into it: the whole field is filled with asterisks.
    ///
    /// The zero-filling form is the same text with its leading blanks replaced, which is why a
    /// negative number keeps its sign where the digits start rather than in front of the zeros.
    /// </value>
    private static ExpressionValue FormatNumber(ExpressionNode node, ExpressionValue operand, bool zeroFill)
    {
        int width = node.Length;
        int decimals = node.Immediate;
        byte[] result = Filled(width, (byte)' ');

        string text = Rounded(operand.Number, decimals);

        if (text.Length > width)
        {
            result.AsSpan().Fill((byte)'*');
            return ExpressionValue.Text(result);
        }

        for (int i = 0; i < text.Length; i++)
            result[width - text.Length + i] = (byte)text[i];

        if (zeroFill)
        {
            for (int i = 0; i < width - text.Length; i++)
                result[i] = (byte)'0';
        }

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Rounds a number to a fixed number of decimals, half away from zero, on its decimal digits.
    /// </summary>
    private static string Rounded(double value, int decimals)
    {
        // A decimal carries the shortest decimal form of the double, which is the form the reference
        // rounds. Outside its range there is no such form, and a number that large overflows any
        // field width the caller could have asked for.
        if (value is > 7.9e28 or < -7.9e28 || double.IsNaN(value) || double.IsInfinity(value))
            return new string('*', decimals + 32);

        decimal rounded = Math.Round((decimal)value, decimals, MidpointRounding.AwayFromZero);

        return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Renders a date through the configured picture (e4parse.c:500-509, D4DATE.C:436-445).
    /// </summary>
    /// <value>
    /// A blank date renders as the picture with blanks where its digits would be, which is not the
    /// same as eight blanks and is what the corpus shows. The picture's own length is the result's
    /// length, which is why it is byte-affecting for a stored key.
    /// </value>
    private static ExpressionValue PictureDateText(
        ExpressionNode node,
        ExpressionValue operand,
        EvaluationContext context)
    {
        Span<byte> stored = stackalloc byte[FoxDate.Length];

        if (operand.Type == T.DateAsText)
            operand.Bytes[..FoxDate.Length].CopyTo(stored);
        else
            FoxDate.FromJulian((long)operand.Number, stored);

        string picture = context.Options.DatePicture;
        byte[] result = new byte[node.Length];
        int century = 0;

        for (int i = 0; i < result.Length; i++)
        {
            char at = i < picture.Length ? picture[i] : ' ';

            result[i] = at switch
            {
                'C' => stored[century++],
                'Y' => stored[picture.Contains('C', StringComparison.Ordinal) ? century++ : 2 + century++],
                'M' => stored[4 + Count(picture, 'M', i)],
                'D' => stored[6 + Count(picture, 'D', i)],
                _ => (byte)at,
            };
        }

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Counts how many of one picture letter appear before a position, which is that letter's index
    /// into the stored form's field.
    /// </summary>
    private static int Count(string picture, char letter, int before)
    {
        int seen = 0;
        for (int i = 0; i < before; i++)
        {
            if (picture[i] == letter)
                seen++;
        }

        return seen;
    }

    /// <summary>
    /// Renders a timestamp as fourteen characters (e4functi.c:821-876).
    /// </summary>
    /// <value>
    /// The date in its stored form and then the time of day as six digits. A date operand has no
    /// time, so its time reads as zeros; a blank timestamp is eight blanks and six zeros, which the
    /// corpus shows and which no rule about blank dates would have predicted.
    /// </value>
    private static ExpressionValue TimestampText(ExpressionValue operand)
    {
        byte[] result = new byte[14];
        Span<byte> stored = result.AsSpan(0, FoxDate.Length);

        int milliseconds = 0;

        switch (operand.Type)
        {
            case T.DateAsText:
                operand.Bytes[..FoxDate.Length].CopyTo(stored);
                break;

            case T.Timestamp:
            case T.TimestampWithMilliseconds:
                FoxDate.FromJulian(operand.TimestampDay, stored);
                milliseconds = operand.TimestampTime;
                break;

            default:
                FoxDate.FromJulian((long)operand.Number, stored);
                break;
        }

        int seconds = milliseconds / 1000;
        result[8] = (byte)('0' + (seconds / 36000 % 10));
        result[9] = (byte)('0' + (seconds / 3600 % 10));
        result[10] = (byte)('0' + (seconds / 600 % 6));
        result[11] = (byte)('0' + (seconds / 60 % 10));
        result[12] = (byte)('0' + (seconds / 10 % 6));
        result[13] = (byte)('0' + (seconds % 10));

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Renders a date as its eight stored characters.
    /// </summary>
    private static ExpressionValue SortableDateText(ExpressionValue operand)
    {
        if (operand.Type == T.DateAsText)
            return ExpressionValue.Text(operand.Bytes.ToArray());

        byte[] result = new byte[FoxDate.Length];
        FoxDate.FromJulian((long)operand.Number, result);

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Builds a timestamp from a year, a month, a day and the folded time of day.
    /// </summary>
    private static ExpressionValue MakeTimestamp(ExpressionNode node, ReadOnlySpan<ExpressionValue> operands)
    {
        long julian = FoxDate.ToJulian(
            (int)operands[0].Number,
            (int)operands[1].Number,
            (int)operands[2].Number);

        return ExpressionValue.Timestamp((int)julian, node.Immediate);
    }

    private static ExpressionValue MakeTimestamp((int Julian, int Milliseconds) parts) =>
        ExpressionValue.Timestamp(parts.Julian, parts.Milliseconds);

    /// <summary>
    /// Reads a date out of text, by the configured picture.
    /// </summary>
    /// <value>
    /// The picture drives the read and nothing else does, so text longer than the picture has its
    /// tail discarded and a two-digit year is completed only from a configured century. With no
    /// century configured a two-digit year is refused, which is ADR-42's whole point: the reference
    /// takes it off the system clock, and a stored key must not depend on when it was built.
    /// </value>
    private static double ReadDateByPicture(ReadOnlySpan<byte> text, EvaluationContext context)
    {
        string picture = context.Options.DatePicture;
        int year = 0;
        int month = 0;
        int day = 0;
        int yearDigits = 0;

        for (int i = 0; i < picture.Length; i++)
        {
            if (i >= text.Length)
                return FoxDate.Illegal;

            int digit = text[i] - (byte)'0';
            bool numeric = digit is >= 0 and <= 9;

            switch (picture[i])
            {
                case 'C':
                case 'Y':
                    if (!numeric)
                        return FoxDate.Illegal;

                    year = (year * 10) + digit;
                    yearDigits++;
                    break;

                case 'M':
                    if (!numeric)
                        return FoxDate.Illegal;

                    month = (month * 10) + digit;
                    break;

                case 'D':
                    if (!numeric)
                        return FoxDate.Illegal;

                    day = (day * 10) + digit;
                    break;

                default:
                    if (text[i] != (byte)picture[i])
                        return FoxDate.Illegal;

                    break;
            }
        }

        if (yearDigits < 4)
        {
            if (context.Options.Century is not int century)
                return FoxDate.Illegal;

            year += century * 100;
        }

        return FoxDate.ToJulian(year, month, day);
    }

    /// <summary>
    /// Reads one field out of a date, as a number.
    /// </summary>
    /// <value>
    /// A blank date answers zero for all three, which the corpus witnesses. The date is rendered to
    /// its stored characters first, so one routine answers whichever form the operand arrived in.
    /// </value>
    private static ExpressionValue DatePart(ExpressionValue operand, int offset, int count)
    {
        Span<byte> text = stackalloc byte[FoxDate.Length];

        if (operand.Type == T.DateAsText)
            operand.Bytes[..FoxDate.Length].CopyTo(text);
        else
            FoxDate.FromJulian((long)operand.Number, text);

        int value = 0;
        for (int i = offset; i < offset + count; i++)
        {
            if (text[i] is < (byte)'0' or > (byte)'9')
                return ExpressionValue.Numeric(0);

            value = (value * 10) + (text[i] - (byte)'0');
        }

        return ExpressionValue.Numeric(value);
    }

    /// <summary>
    /// Renders a time of day as eight characters.
    /// </summary>
    private static byte[] TimeText(int milliseconds)
    {
        int seconds = milliseconds / 1000;
        byte[] result =
        [
            (byte)('0' + (seconds / 36000 % 10)), (byte)('0' + (seconds / 3600 % 10)), (byte)':',
            (byte)('0' + (seconds / 600 % 6)), (byte)('0' + (seconds / 60 % 10)), (byte)':',
            (byte)('0' + (seconds / 10 % 6)), (byte)('0' + (seconds % 10)),
        ];

        return result;
    }

    // ----------------------------------------------------------------- others

    /// <summary>
    /// Reads the leading number out of text, stopping at the first byte that is not part of one.
    /// </summary>
    /// <value>
    /// It does not fail and does not answer zero for text that merely starts with digits: the corpus
    /// witnesses that reading a date's text answers the first field's digits, so this is a prefix
    /// read and not a parse.
    /// </value>
    private static double LeadingNumber(ReadOnlySpan<byte> text)
    {
        int at = 0;
        bool negative = false;

        if (at < text.Length && (text[at] == (byte)'-' || text[at] == (byte)'+'))
        {
            negative = text[at] == (byte)'-';
            at++;
        }

        double whole = 0;
        while (at < text.Length && text[at] is >= (byte)'0' and <= (byte)'9')
            whole = (whole * 10) + (text[at++] - (byte)'0');

        if (at < text.Length && text[at] == (byte)'.')
        {
            at++;
            double scale = 0.1;
            while (at < text.Length && text[at] is >= (byte)'0' and <= (byte)'9')
            {
                whole += (text[at++] - (byte)'0') * scale;
                scale /= 10;
            }
        }

        return negative ? -whole : whole;
    }

    /// <summary>
    /// Writes a number as the four bytes of a little-endian binary long.
    /// </summary>
    private static ExpressionValue LongToBinary(ExpressionValue operand)
    {
        byte[] result = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(result, unchecked((int)(long)operand.Number));

        return ExpressionValue.Text(result);
    }

    /// <summary>
    /// Answers whether a value counts as empty, by the type the row takes (e4functi.c:3111-3190).
    /// </summary>
    /// <value>
    /// Five rows and four answers. A string is empty when every byte is a blank, so an all-blank
    /// string is empty and the corpus says so. A memo is empty when it has no block, which is read
    /// out of the record rather than out of the memo. A number and a julian date are empty at zero,
    /// a timestamp when its day is zero, and a logical when it is false.
    /// </value>
    private static ExpressionValue IsEmpty(
        ExpressionNode node,
        ExpressionValue operand,
        ExpressionNode source,
        EvaluationContext context)
    {
        if (node.Row != FunctionRows.Empty)
        {
            return ExpressionValue.Logic(node.Row switch
            {
                FunctionRows.Empty + 2 => operand.TimestampDay == 0,
                FunctionRows.Empty + 4 => !operand.Logical,
                _ => operand.Number == 0,
            });
        }

        if (context.Record is RecordBuffer record
            && source.Field?.Definition is FieldDefinition field
            && FieldValueDecoder.IsMemo(field))
        {
            return ExpressionValue.Logic(FieldValueDecoder.MemoBlock(record, field) == 0);
        }

        return ExpressionValue.Logic(!operand.Bytes.ContainsAnyExcept((byte)' '));
    }

    private static byte[] Filled(int length, byte value)
    {
        byte[] result = new byte[length];
        result.AsSpan().Fill(value);

        return result;
    }
}
