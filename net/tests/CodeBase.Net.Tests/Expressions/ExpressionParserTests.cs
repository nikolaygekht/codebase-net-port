using System.Globalization;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// The parse pass, over a field list built in memory.
///
/// Layer one, where the grammar is exhausted: every precedence level, both associativity cases, the
/// variadic collapse, the prefix operator's grouping against a tighter operator, and each of the nine
/// functions whose constant parameters are read while parsing. None of it needs a file, a record or a
/// cursor, because a field's loader row is chosen from the field's declaration and nothing else.
///
/// Shape is asserted by rendering the postfix array back out as text — a field by its name, a literal
/// by its value, everything else by the name the function table gives its row. That reads as the
/// postfix form of the expression, and it is not a restatement of the parser's internals: the labels
/// come out of the ported table, so a row chosen wrongly prints the wrong name.
/// </summary>
public sealed class ExpressionParserTests
{
    private static readonly IFieldMetadata Fields = new TableFieldMetadata(
        [
            Field("K_TEXT", 'C', 20),
            Field("K_CITY", 'C', 15),
            Field("K_NUM", 'N', 12, decimals: 3),
            Field("K_INT", 'I', 4),
            Field("K_DATE", 'D', 8),
            Field("K_TS", 'T', 8),
            Field("K_LOG", 'L', 1),
        ],
        "CDXEXPR");

    [Theory]
    // The seven precedence levels, and that equal priority groups from the left.
    [InlineData("1 + 2 * 3", "1 2 3 * +")]
    [InlineData("1 * 2 + 3", "1 2 * 3 +")]
    [InlineData("1 - 2 - 3", "1 2 - 3 -")]
    [InlineData("1 / 2 / 4", "1 2 / 4 /")]
    [InlineData("2 ^ 3 ^ 2", "2 3 ^ 2 ^")]
    [InlineData("2 ** 3 * 4", "2 3 ** 4 *")]
    [InlineData("1 + 2 = 3", "1 2 + 3 =")]
    [InlineData("(1 + 2) * 3", "1 2 + 3 *")]
    [InlineData("1 + (2 * 3)", "1 2 3 * +")]
    [InlineData("((1))", "1")]
    // The comparison family binds tighter than the logical operators, and .AND. tighter than .OR.
    [InlineData("K_INT = 1 .AND. K_INT = 2", "K_INT 1 = K_INT 2 = .AND.")]
    [InlineData("K_INT > 1 .OR. K_INT < 0 .AND. K_LOG", "K_INT 1 > K_INT 0 < K_LOG .AND. .OR.")]
    // A prefix negation is read before its operand is taken away from it by a tighter operator.
    [InlineData(".NOT. K_LOG", "K_LOG .NOT.")]
    [InlineData(".NOT. K_INT = 1", "K_INT 1 = .NOT.")]
    [InlineData("! K_INT = 1", "K_INT 1 = !")]
    [InlineData(".NOT. K_LOG .AND. K_LOG", "K_LOG .NOT. K_LOG .AND.")]
    [InlineData(".NOT. .NOT. K_LOG", "K_LOG .NOT. .NOT.")]
    // Calls and the operators that are not arithmetic.
    [InlineData("UPPER(K_TEXT)", "K_TEXT UPPER")]
    [InlineData("K_TEXT + K_CITY", "K_TEXT K_CITY +")]
    [InlineData("K_TEXT - K_CITY", "K_TEXT K_CITY -")]
    [InlineData("'A' $ K_TEXT", "'A' K_TEXT $")]
    [InlineData("UPPER(TRIM(K_TEXT)) + K_CITY", "K_TEXT TRIM UPPER K_CITY +")]
    [InlineData("IIF(K_LOG, K_TEXT, K_CITY)", "K_LOG K_TEXT K_CITY IIF")]
    [InlineData("DESCEND(UPPER(K_TEXT))", "K_TEXT UPPER DESCEND")]
    [InlineData(".T. .AND. .F.", ".T. .F. .AND.")]
    [InlineData(".TRUE. .OR. .FALSE.", ".TRUE. .FALSE. .OR.")]
    public void ThePostfixShapeIsTheGrammarsOwn(string expression, string shape) =>
        Shape(Parse(expression)).Should().Be(shape);

    [Theory]
    [InlineData("K_LOG .AND. K_LOG", 2)]
    [InlineData("K_LOG .AND. K_LOG .AND. K_LOG", 3)]
    [InlineData("K_LOG .AND. K_LOG .AND. K_LOG .AND. K_LOG", 4)]
    [InlineData("K_LOG .OR. K_LOG .OR. K_LOG", 3)]
    public void ARunOfOneVariadicOperatorCollapsesIntoOneNode(string expression, int operands)
    {
        // The reference collapses a run so that the relation optimizer sees one branch with as many
        // children as the run had terms, rather than a chain of two-operand nodes it would have to
        // flatten itself.
        ParsedExpression parsed = Parse(expression);

        parsed.Nodes.Should().HaveCount(operands + 1);
        parsed.Nodes[^1].ParameterCount.Should().Be(operands);
        parsed.Nodes[^1].SubtreeSize.Should().Be(operands + 1);
    }

    [Fact]
    public void TwoDifferentVariadicOperatorsDoNotCollapseIntoEachOther()
    {
        ParsedExpression parsed = Parse("K_LOG .AND. K_LOG .OR. K_LOG .OR. K_LOG");

        Shape(parsed).Should().Be("K_LOG K_LOG .AND. K_LOG K_LOG .OR.");
        parsed.Nodes[^1].ParameterCount.Should().Be(3);
    }

    [Fact]
    public void APrefixNegationIsNeverCollapsed()
    {
        // It shares neither the variadic flag nor the priority of the two operators that do, so a
        // repeat of it is two nodes and each takes one operand.
        ParsedExpression parsed = Parse(".NOT. .NOT. K_LOG");

        parsed.Nodes.Should().HaveCount(3);
        parsed.Nodes[1].ParameterCount.Should().Be(1);
        parsed.Nodes[2].ParameterCount.Should().Be(1);
    }

    [Fact]
    public void EachNodesSubtreeReachesBackOverItsOperands()
    {
        ParsedExpression parsed = Parse("1 + 2 * 3");

        parsed.Nodes.Select(node => node.SubtreeSize).Should().Equal(1, 1, 1, 3, 5);
    }

    [Theory]
    [InlineData('C', FunctionRows.FieldCharacter)]
    [InlineData('Z', FunctionRows.FieldCharacter)]
    [InlineData('N', FunctionRows.FieldNumericAsText)]
    [InlineData('F', FunctionRows.FieldNumericAsText)]
    [InlineData('D', FunctionRows.FieldDateAsText)]
    [InlineData('L', FunctionRows.FieldLogical)]
    [InlineData('M', FunctionRows.FieldMemo)]
    [InlineData('X', FunctionRows.FieldMemo)]
    [InlineData('G', FunctionRows.FieldMemo)]
    [InlineData('I', FunctionRows.FieldInteger)]
    [InlineData('B', FunctionRows.FieldDouble)]
    [InlineData('Y', FunctionRows.FieldCurrency)]
    [InlineData('T', FunctionRows.FieldTimestamp)]
    [InlineData('H', FunctionRows.FieldBinaryFloat)]
    public void AFieldsLoaderRowComesFromItsType(char type, int row)
    {
        // The two binary types arrive under their own letters and load where their unmarked forms
        // load, which is what the reference's conversion of them back to character and memo means.
        IFieldMetadata one = new TableFieldMetadata([Field("F", type, 8)], "T");

        ExpressionParser.Parse("F", one).Nodes[0].Row.Should().Be(row);
    }

    [Fact]
    public void AFieldCarriesItsPositionInTheTable()
    {
        Parse("K_DATE").Nodes[0].FieldNumber.Should().Be(5);
        Parse("K_DATE").Nodes[0].Field!.Value.Definition.Length.Should().Be(8);
    }

    [Fact]
    public void AFieldNameIsMatchedWithoutRegardToCase() =>
        Parse("k_text").Nodes[0].FieldNumber.Should().Be(1);

    [Fact]
    public void AFieldMayBeQualifiedByTheTableItBelongsTo()
    {
        ParsedExpression parsed = ExpressionParser.Parse("CDXEXPR->K_TEXT", Fields, ["CDXEXPR"]);

        parsed.Nodes.Should().HaveCount(1);
        parsed.Nodes[0].FieldNumber.Should().Be(1);
    }

    [Theory]
    // A formatted number's width and decimals are read here because they decide the result's length,
    // and the constants that carried them stop being nodes.
    [InlineData("STR(K_NUM,12,3)", 2, 12, 3)]
    [InlineData("STR(1)", 2, 10, 0)]
    [InlineData("STR(1,6)", 2, 6, 0)]
    [InlineData("STR(1,5,4)", 2, 5, 3)]
    [InlineData("STR(1,1,3)", 2, 1, 0)]
    [InlineData("STR(1,-1)", 2, 10, 0)]
    [InlineData("STRZERO(1,5)", 2, 5, 0)]
    // A run of blanks keeps its count without losing the node that carried it.
    [InlineData("SPACE(5)", 2, 5, 0)]
    // A substring's length is what is left of the operand after its start, from either kind of operand.
    [InlineData("SUBSTR('ABCDEF',2,3)", 2, 3, 1)]
    [InlineData("SUBSTR('ABCDEF',3)", 2, 4, 2)]
    [InlineData("SUBSTR(K_TEXT,3)", 2, 18, 2)]
    [InlineData("LEFT('ABC',2)", 2, 2, 0)]
    // Characters from the end carry both the count and where they start.
    [InlineData("RIGHT(K_TEXT,5)", 2, 5, 15)]
    [InlineData("RIGHT('ABCDEF',2)", 2, 2, 4)]
    [InlineData("PADL(K_TEXT,25)", 2, 25, 0)]
    [InlineData("PADR('AB',5)", 2, 5, 0)]
    public void AConstantParameterThatDecidesALengthIsReadWhileParsing(
        string expression,
        int nodes,
        int length,
        int immediate)
    {
        ParsedExpression parsed = Parse(expression);

        parsed.Nodes.Should().HaveCount(nodes);
        parsed.Nodes[^1].Length.Should().Be(length);
        parsed.Nodes[^1].Immediate.Should().Be(immediate);
        parsed.Nodes[^1].ParameterCount.Should().Be(1);
    }

    [Theory]
    [InlineData("DATETIME(2026,1,2)", 0)]
    [InlineData("DATETIME(2026,1,2,3)", 3 * 3600000)]
    [InlineData("DATETIME(2026,1,2,3,4)", (3 * 3600000) + (4 * 60000))]
    [InlineData("DATETIME(2026,1,2,3,4,5)", (3 * 3600000) + (4 * 60000) + 5000)]
    [InlineData("DATETIME(2026,1,2,3,4,5,6)", (3 * 3600000) + (4 * 60000) + 5000 + 6)]
    public void ATimestampsTimeOfDayBecomesOneCountOfMilliseconds(string expression, int milliseconds)
    {
        ParsedExpression parsed = Parse(expression);

        parsed.Nodes.Should().HaveCount(4);
        parsed.Nodes[^1].ParameterCount.Should().Be(3);
        parsed.Nodes[^1].Immediate.Should().Be(milliseconds);
    }

    [Fact]
    public void ATimestampCarryingMillisecondsResolvesToTheOverloadThatKeepsThem()
    {
        Parse("DATETIME(2026,1,2,3,4,5)").Nodes[^1].Row.Should().Be(FunctionRows.Timestamp);
        Parse("DATETIME(2026,1,2,3,4,5,6)").Nodes[^1].Row.Should().Be(FunctionRows.Timestamp + 1);
    }

    [Fact]
    public void ADateAskedToFormatItselfWithAPictureBecomesTheSortableFormInstead()
    {
        // The reference rewrites rather than refusing, and it is the only place the parse pass
        // changes a row after choosing it.
        ParsedExpression parsed = Parse("DTOC(K_DATE,1)");

        parsed.Nodes.Should().HaveCount(2);
        parsed.Nodes[^1].Row.Should().Be(FunctionRows.DateToSortableText);
        parsed.Nodes[^1].ParameterCount.Should().Be(1);

        Parse("DTOC(K_DATE)").Nodes[^1].Row.Should().Be(FunctionRows.DateToText);
    }

    [Fact]
    public void ATimestampAskedToFormatItselfWithAPictureKeepsItsRowAndLosesThePicture()
    {
        ParsedExpression parsed = Parse("TTOC(K_TS,1)");

        parsed.Nodes.Should().HaveCount(2);
        parsed.Nodes[^1].Row.Should().Be(FunctionRows.TimestampToText);
        parsed.Nodes[^1].ParameterCount.Should().Be(1);
    }

    [Theory]
    [InlineData("RECNO()")]
    [InlineData("DATE()")]
    [InlineData("DELETED()")]
    public void AFunctionThatTakesNothingParsesToOneNodeTakingNothing(string expression)
    {
        ParsedExpression parsed = Parse(expression);

        parsed.Nodes.Should().HaveCount(1);
        parsed.Nodes[0].ParameterCount.Should().Be(0);
    }

    [Fact]
    public void APrameterListEndingRightAfterACommaIsOneParameterShort()
    {
        // The reference tests for the closing bracket at the start of every parameter and not only at
        // the start of the first, so a trailing comma ends the list rather than promising a parameter
        // (e4parse.c:1868-1890). Refusing it here would refuse text the reference accepts.
        ParsedExpression parsed = Parse("UPPER(K_TEXT,)");

        Shape(parsed).Should().Be("K_TEXT UPPER");
        parsed.Nodes[^1].ParameterCount.Should().Be(1);
    }

    [Fact]
    public void TextAfterTheExpressionEndsIsIgnored()
    {
        // A closing bracket at the top level ends the expression, and the reference then checks only
        // that its operator stack came back empty, never that the text ran out (e4parse.c:1108-1115).
        Shape(Parse("K_LOG)")).Should().Be("K_LOG");
        Shape(Parse("K_TEXT,K_CITY")).Should().Be("K_TEXT");
        Shape(Parse("K_TEXT + K_CITY) .AND. K_LOG")).Should().Be("K_TEXT K_CITY +");
    }

    [Theory]
    [InlineData("NOSUCH(1)", ErrorCode.UnrecognizedFunction)]
    [InlineData("TOOLONGFN(1)", ErrorCode.UnrecognizedFunction)]
    [InlineData("NOSUCHFLD", ErrorCode.FieldName)]
    [InlineData("OTHER->K_TEXT", ErrorCode.FieldName)]
    [InlineData("UPPER(K_TEXT,2)", ErrorCode.ParameterCount)]
    [InlineData("UPPER()", ErrorCode.ParameterCount)]
    [InlineData("SPACE(K_INT)", ErrorCode.NotConstant)]
    [InlineData("LEFT(K_TEXT,K_INT)", ErrorCode.NotConstant)]
    [InlineData("RIGHT(K_TEXT,K_INT)", ErrorCode.NotConstant)]
    [InlineData("PADL(K_TEXT,K_INT)", ErrorCode.NotConstant)]
    [InlineData("SUBSTR(K_TEXT,K_INT)", ErrorCode.NotConstant)]
    [InlineData("SUBSTR(K_TEXT,1,K_INT)", ErrorCode.NotConstant)]
    [InlineData("DATETIME(2026,1,2,K_INT)", ErrorCode.NotConstant)]
    [InlineData("STR(K_NUM,K_INT)", ErrorCode.TypeMismatch)]
    [InlineData("STR(K_NUM,1,K_INT)", ErrorCode.TypeMismatch)]
    [InlineData("UPPER(K_TEXT", ErrorCode.CommaExpected)]
    [InlineData("(K_LOG", ErrorCode.RightParenthesisMissing)]
    [InlineData("UPPER(K_TEXT,", ErrorCode.RightParenthesisMissing)]
    public void WhatTheReferenceRefusesIsRefusedWithTheCodeItUses(string expression, ErrorCode code)
    {
        Action act = () => Parse(expression);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void AFieldWhoseTypeHasNoLoaderRowIsATypeMismatch()
    {
        // Unreachable through an open table, because the opener admits no such type and hides the
        // null-flags field. It is reachable through this seam, which is what the seam is for.
        IFieldMetadata one = new TableFieldMetadata([Field("F", '0', 1)], "T");

        Action act = () => ExpressionParser.Parse("F", one);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.TypeMismatch);
    }

    private static ParsedExpression Parse(string expression) => ExpressionParser.Parse(expression, Fields);

    /// <summary>
    /// Renders a parsed expression back out as postfix text.
    /// </summary>
    private static string Shape(ParsedExpression parsed) =>
        string.Join(' ', parsed.Nodes.Select(Label));

    private static string Label(ExpressionNode node)
    {
        if (node.Field is { } field)
            return field.Definition.Name;

        if (node.Constant is { } constant)
        {
            return constant.IsText
                ? $"'{System.Text.Encoding.Latin1.GetString(constant.Text)}'"
                : constant.Number.ToString("G", CultureInfo.InvariantCulture);
        }

        return FunctionTable.Row(node.Row).Name ?? $"row{node.Row}";
    }

    private static FieldDefinition Field(string name, char type, int length, int decimals = 0) =>
        new(name, type, type, length, decimals, 1, false, false, false, false, null);
}
