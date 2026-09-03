using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// Overload resolution, over a field list built in memory.
///
/// Layer one, and what it is for is the promotion loop: six conversions, each firing on one pair of
/// types, each rewriting the node that produced the value rather than adding one. The corpus gates
/// which row every expression in it resolves to, which is the stronger check; what it cannot do is
/// say [i]why[/i], or fail in a way that names the promotion that went wrong. These can.
///
/// The invariant worth stating on its own is that a promotion adds no node. Two things depend on it:
/// the resolution gate compares node arrays as sequences, and the query optimizer matches a
/// subexpression to a tag by comparing node counts first. A conversion node would break both, and it
/// is the shape an obvious implementation would reach for.
/// </summary>
public sealed class TypeResolverTests
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
            Field("K_CUR", 'Y', 8, decimals: 4),
            Field("K_DBL", 'B', 8),
            Field("K_FLT", 'H', 4),
            Field("K_MEMO", 'M', 10),
        ],
        "CDXEXPR");

    [Theory]
    // The five that retarget a field loader, and the one that retargets a literal.
    [InlineData("K_DATE + 1", 0, FunctionRows.FieldDateAsDouble)]
    [InlineData("K_NUM + 1", 0, FunctionRows.FieldNumericAsDouble)]
    [InlineData("K_INT + 1", 0, FunctionRows.FieldIntegerAsDouble)]
    [InlineData("K_CUR + 1", 0, FunctionRows.FieldCurrencyAsDouble)]
    [InlineData("K_FLT + 1", 1, FunctionRows.FloatConstant)]
    [InlineData("K_FLT * K_INT", 1, FunctionRows.FieldIntegerAsFloat)]
    public void APromotionRewritesTheRowOfTheNodeThatProducedTheValue(string expression, int producer, int row)
    {
        Compile(expression).Nodes[producer].Row.Should().Be(row);
    }

    [Theory]
    [InlineData("K_DATE + 1")]
    [InlineData("K_NUM + 1")]
    [InlineData("K_INT + 1")]
    [InlineData("K_CUR + 1")]
    [InlineData("K_FLT + 1")]
    [InlineData("K_FLT * K_INT")]
    [InlineData("K_NUM > 0")]
    public void APromotionAddsNoNode(string expression)
    {
        // The property the resolution gate's sequence equality and the query optimizer's node-count
        // comparison both rest on. Wrapping the producer in a conversion node would pass every test
        // that only looked at values.
        ParsedExpression parsed = ExpressionParser.Parse(expression, Fields);

        TypeResolver.Resolve(parsed).Nodes.Should().HaveCount(parsed.Nodes.Count);
    }

    [Fact]
    public void APromotedNodeKeepsItsOwnDeclaredLength()
    {
        // The reference updates the length on its simulated operand stack and never the length it
        // already wrote onto the node, so a numeric field promoted to a double still reports the
        // field's width. Its own dump shows this, and the query optimizer reads that width.
        CompiledExpression compiled = Compile("K_NUM + 1");

        compiled.Nodes[0].Row.Should().Be(FunctionRows.FieldNumericAsDouble);
        compiled.Nodes[0].Length.Should().Be(12);
        compiled.Nodes[^1].Length.Should().Be(sizeof(double));
    }

    [Fact]
    public void ABareFieldIsNotPromotedAtAll()
    {
        // Nothing asks it for a double, so it keeps the loader the parser chose. This is the other
        // half of the pair the design warns against collapsing: the same field is two types
        // depending on what reads it, and both key differently.
        Compile("K_NUM").Nodes[0].Row.Should().Be(FunctionRows.FieldNumericAsText);
        ((char)Compile("K_NUM").ResultType).Should().Be('N');
        ((char)Compile("K_NUM + 0").ResultType).Should().Be('n');
    }

    [Fact]
    public void TheLiteralPromotionFiresOnlyForALiteral()
    {
        // It is the one promotion guarded on what the producer already is: a numeric that reached the
        // slot any other way is not narrowed to a float, so the row advances instead.
        Compile("K_FLT + 1").Nodes[1].Row.Should().Be(FunctionRows.FloatConstant);

        Action act = () => Compile("K_FLT + K_DBL");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.TypeMismatch);
    }

    [Theory]
    [InlineData("K_TEXT", 'C', 20)]
    [InlineData("K_NUM", 'N', 12)]
    [InlineData("K_DATE", 'D', 8)]
    [InlineData("K_INT", 'I', 4)]
    [InlineData("K_LOG", 'L', 4)]
    [InlineData("K_CUR", 'Y', 8)]
    [InlineData("K_TS", 'T', 8)]
    [InlineData("K_DBL", 'n', 8)]
    [InlineData("K_FLT", 'H', 4)]
    [InlineData("K_MEMO", 'C', 1024)]
    public void TheTenTypesAnExpressionCanProduceEachHaveTheirOwnLength(
        string expression,
        char type,
        int length)
    {
        // Exactly the closure of section 1: a value enters through a field, a literal or a function
        // over those, and these ten are what that closes to. The letters are the reference's own
        // type codes, which is what the enumeration is valued as.
        CompiledExpression compiled = Compile(expression);

        ((char)compiled.ResultType).Should().Be(type);
        compiled.Length.Should().Be(length);
    }

    [Theory]
    [InlineData("UPPER(K_TEXT)", 20)]
    [InlineData("TRIM(K_TEXT)", 20)]
    [InlineData("K_TEXT + K_CITY", 35)]
    [InlineData("K_TEXT - K_CITY", 35)]
    [InlineData("SUBSTR(K_TEXT,1,5)", 5)]
    [InlineData("SUBSTR(K_TEXT,18,5)", 3)]
    [InlineData("LEFT(K_TEXT,30)", 20)]
    [InlineData("RIGHT(K_TEXT,5)", 5)]
    [InlineData("PADR(K_CITY,25)", 25)]
    [InlineData("PADR(K_TEXT,5)", 5)]
    [InlineData("STR(K_NUM,12,3)", 12)]
    [InlineData("STR(K_INT)", 10)]
    [InlineData("SPACE(7)", 7)]
    [InlineData("CHR(65)", 1)]
    [InlineData("DTOS(K_DATE)", 8)]
    [InlineData("DTOC(K_DATE)", 8)]
    [InlineData("TTOC(K_TS)", 14)]
    [InlineData("IIF(K_LOG,K_TEXT,K_CITY)", 20)]
    [InlineData("IIF(K_LOG,K_CITY,K_TEXT)", 20)]
    [InlineData("L2BIN(K_INT)", 4)]
    public void AStringResultsLengthComesFromTheRowAndItsOperands(string expression, int length) =>
        Compile(expression).Length.Should().Be(length);

    [Fact]
    public void AConditionalKeepsTheLengthOfTheBranchNotTaken()
    {
        // Needed at evaluation time, because the result is as wide as the longer branch and the
        // shorter one has to be padded into it.
        CompiledExpression compiled = Compile("IIF(K_LOG,K_TEXT,K_CITY)");

        compiled.Nodes[^1].Immediate.Should().Be(20);
        compiled.Nodes[^1].Immediate2.Should().Be(15);
        compiled.Length.Should().Be(20);
    }

    [Fact]
    public void AConcatenationRemembersWhereTheSecondOperandStarts()
    {
        Compile("K_TEXT + K_CITY").Nodes[^1].Immediate.Should().Be(20);
    }

    [Theory]
    [InlineData("K_TEXT = \"SM\"", 2)]
    [InlineData("\"SM\" = K_TEXT", 2)]
    [InlineData("K_TEXT = K_CITY", 15)]
    [InlineData("K_TEXT > K_TEXT", 20)]
    public void AComparisonComparesTheShorterOfItsTwoOperands(string expression, int length)
    {
        // What makes a short character constant behave as a prefix against a longer key, and what
        // the query optimizer turns into a range rather than an equality.
        Compile(expression).Nodes[^1].Immediate.Should().Be(length);
    }

    [Theory]
    [InlineData("K_LOG")]
    [InlineData(".NOT. K_LOG")]
    [InlineData("EMPTY(K_TEXT)")]
    [InlineData("K_LOG .AND. K_LOG")]
    [InlineData(".T.")]
    public void ALogicalThatIsNotAComparisonHasNoCompareLength(string expression) =>
        Compile(expression).Nodes[^1].Immediate.Should().Be(0);

    [Theory]
    [InlineData("K_TEXT", false)]
    [InlineData("TRIM(K_TEXT)", true)]
    [InlineData("LTRIM(K_TEXT)", true)]
    [InlineData("ALLTRIM(K_TEXT)", true)]
    [InlineData("UPPER(TRIM(K_TEXT))", true)]
    [InlineData("TRIM(K_TEXT) + K_CITY", true)]
    [InlineData("K_TEXT + K_CITY", false)]
    public void ATrimAnywhereMarksTheWholeExpression(string expression, bool trimmed) =>
        Compile(expression).HasTrim.Should().Be(trimmed);

    [Fact]
    public void APlainConcatenationAfterATrimBecomesTheOverloadThatMovesBlanks()
    {
        // The flag runs over the whole expression rather than over a subtree, which is why a trim in
        // the left operand changes which row the concatenation resolves to.
        Compile("K_TEXT + K_CITY").Nodes[^1].Row.Should().Be(FunctionRows.Concatenate);
        Compile("TRIM(K_TEXT) + K_CITY").Nodes[^1].Row.Should().Be(FunctionRows.ConcatenateAfterTrim);
        Compile("K_TEXT - K_CITY").Nodes[^1].Row.Should().Be(FunctionRows.ConcatenateMovingBlanks);
    }

    [Fact]
    public void ACharacterOperandUnderAKernelIsPointedAtTheLoaderThatMaterializesIt()
    {
        // A bare field is read where it lies; a field a kernel has to work on is laid down first. The
        // rows differ, and the reference chooses between them here rather than at parse time.
        Compile("K_TEXT").Nodes[0].Row.Should().Be(FunctionRows.FieldCharacter);
        Compile("UPPER(K_TEXT)").Nodes[0].Row.Should().Be(FunctionRows.FieldCharacterMaterialized);
    }

    [Theory]
    [InlineData("UPPER(K_INT)")]
    [InlineData(".T. > .F.")]
    [InlineData("K_TEXT + 1")]
    [InlineData("K_LOG .AND. K_INT")]
    public void AnOperationWithNoOverloadForItsOperandsIsATypeMismatch(string expression)
    {
        // The row advances through its siblings until the operation code changes, and that is where
        // the run ends. Nothing knows how long a run is; the code change is the signal.
        Action act = () => Compile(expression);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.TypeMismatch);
    }

    [Fact]
    public void AnExpressionNeedingMoreOperandSlotsThanTheEngineHasIsRefused()
    {
        // Right-nested addition holds every term on the stack before the first operator pops any, so
        // this is the shape that reaches the limit. Left-nested addition never gets past two slots.
        string deep = string.Concat(Enumerable.Repeat("1+(", 70)) + "1" + new string(')', 70);

        Action act = () => Compile(deep);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Overflow);
    }

    [Fact]
    public void AnOrdinaryExpressionIsNowhereNearTheLimit()
    {
        string wide = string.Join(" + ", Enumerable.Repeat("1", 60));

        Compile(wide).Nodes.Should().HaveCount(119);
    }

    private static CompiledExpression Compile(string expression) =>
        TypeResolver.Resolve(ExpressionParser.Parse(expression, Fields));

    private static FieldDefinition Field(string name, char type, int length, int decimals = 0) =>
        new(name, type, type, length, decimals, 1, false, false, false, false, null);
}
