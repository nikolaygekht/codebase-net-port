using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// The rules of the thirteen functions Visual FoxPro adds, stated rather than transcribed.
///
/// Visual FoxPro's own answers are the gate, at layer four. What these add is the four rules a reader
/// would otherwise have to infer from a table of numbers, and they are the four a sensible
/// implementation gets wrong: the remainder takes its sign from the divisor rather than from the
/// dividend, truncation goes towards zero on both signs, rounding is half away from zero on the
/// decimal digits, and a search counts from one and answers zero for absent.
///
/// Governing decisions: ADR-43, ADR-44.
/// </summary>
public sealed class VisualFoxProFunctionTests
{
    private static readonly IFieldMetadata None = new TableFieldMetadata([], "T", longFieldNames: false);

    [Theory]
    [InlineData("ABS(-1)", 1)]
    [InlineData("ABS(-0.001)", 0.001)]
    [InlineData("ABS(0)", 0)]
    [InlineData("SIGN(-3)", -1)]
    [InlineData("SIGN(0)", 0)]
    [InlineData("SIGN(3)", 1)]
    [InlineData("SQRT(4)", 2)]
    [InlineData("SQRT(0)", 0)]
    public void TheOrdinaryNumericOnes(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    // Towards zero on both signs, which is what makes it truncation and not a floor.
    [InlineData("INT(1.5)", 1)]
    [InlineData("INT(-1.5)", -1)]
    [InlineData("INT(-0.5)", 0)]
    [InlineData("INT(0.9)", 0)]
    public void TruncationGoesTowardsZero(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Fact]
    public void TruncatingASmallNegativeGivesAPositiveZero()
    {
        // Not cosmetic: the bits differ, Visual FoxPro's stored answer is a positive zero, and a
        // value that becomes an index key has to carry the bits the engine that wrote it had.
        Value("INT(-0.5)").ToRaw().Should().Equal(new byte[8]);
    }

    [Theory]
    // Half away from zero, and on the decimal digits rather than on the exact binary value -- so
    // 0.135 rounds up although the nearest double to it is below it. The same rule formatting
    // follows, and the reason both go through a decimal.
    [InlineData("ROUND(1.5,0)", 2)]
    [InlineData("ROUND(2.5,0)", 3)]
    [InlineData("ROUND(-2.5,0)", -3)]
    [InlineData("ROUND(0.125,2)", 0.13)]
    [InlineData("ROUND(0.135,2)", 0.14)]
    [InlineData("ROUND(12345.6789,3)", 12345.679)]
    // A negative number of places rounds to tens, hundreds and so on.
    [InlineData("ROUND(1.5,-1)", 0)]
    [InlineData("ROUND(15,-1)", 20)]
    public void RoundingIsHalfAwayFromZeroOnTheDecimalDigits(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    // The sign follows the divisor, which is where it differs from the remainder operator of most
    // languages: five modulo minus three is minus one there and two here.
    [InlineData("MOD(5,3)", 2)]
    [InlineData("MOD(-5,3)", 1)]
    [InlineData("MOD(5,-3)", -1)]
    [InlineData("MOD(-5,-3)", -2)]
    [InlineData("MOD(5.5,2)", 1.5)]
    public void TheRemainderTakesItsSignFromTheDivisor(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    // VFPBAND.vfp.dump.txt, section const: a zero divisor raises whatever its sign or spelling, and a
    // tiny one does not; below zero raises however close to it, and a negative zero is not below it.
    // VFP's probes use 1E-300, which this syntax cannot spell; the smallest decimal stands in.
    [InlineData("MOD(5,0)", 1307)]
    [InlineData("MOD(0,0)", 1307)]
    [InlineData("MOD(-5,0)", 1307)]
    [InlineData("MOD(5.5,0)", 1307)]
    [InlineData("MOD(5,0.0)", 1307)]
    [InlineData("MOD(5,-0)", 1307)]
    [InlineData("SQRT(-1)", 61)]
    [InlineData("SQRT(-0.5)", 61)]
    [InlineData("SQRT(-0.0000000001)", 61)]
    public void ARemainderByZeroAndTheRootOfANegativeRaiseAsVisualFoxProDoes(string expression, int error)
    {
        Action act = () => Value(expression);

        ExpressionEvaluationException raised = act.Should().Throw<ExpressionEvaluationException>().Which;
        raised.Code.Should().Be(ErrorCode.Result);
        raised.VisualFoxProError.Should().Be(error);
    }

    [Theory]
    [InlineData("MOD(5,0.0000000001)")]
    [InlineData("SQRT(-0)")]
    [InlineData("SQRT(0)")]
    public void NearlyNoAnswerIsStillAnAnswer(string expression) =>
        Value(expression).Number.Should().BeApproximately(0, 1e-9);

    [Theory]
    // VFPBAND.vfp.dump.txt, section null-band: a null in any argument of any of the thirteen makes
    // the answer null, of the function's own type.
    [InlineData("ABS(N_)")]
    [InlineData("INT(N_)")]
    [InlineData("ROUND(N_,2)")]
    [InlineData("ROUND(5.25,I_)")]
    [InlineData("MOD(N_,3)")]
    [InlineData("MOD(5,N_)")]
    [InlineData("MIN(N_,1)")]
    [InlineData("MIN(1,N_)")]
    [InlineData("MIN(1,2,N_)")]
    [InlineData("MAX(N_,1)")]
    [InlineData("MAX(1,N_)")]
    [InlineData("SQRT(N_)")]
    [InlineData("SIGN(N_)")]
    [InlineData("LEN(C_)")]
    [InlineData("AT(C_,\"abc\")")]
    [InlineData("AT(\"a\",C_)")]
    [InlineData("AT(\"a\",\"abc\",I_)")]
    [InlineData("ASC(C_)")]
    public void ANullOperandMakesANumericAnswerNullAndZero(string expression)
    {
        ExpressionValue value = OverNulls(expression);

        value.IsNull.Should().BeTrue();
        value.Number.Should().Be(0);
    }

    [Theory]
    [InlineData("MIN(C_,\"a\")")]
    [InlineData("MAX(\"a\",C_)")]
    [InlineData("LOWER(C_)")]
    [InlineData("REPLICATE(C_,2)")]
    // REPLICATE("a",I_) has no row here: the count sizes the result, so it must be written out, and a
    // field there is refused when the expression is read (ADR-43).
    public void AndATextAnswerNullAndBlank(string expression)
    {
        ExpressionValue value = OverNulls(expression);

        value.IsNull.Should().BeTrue();
        value.Type.Should().Be(ExpressionType.Character);
        value.Bytes.ToArray().Should().AllSatisfy(b => b.Should().Be((byte)' '));
    }

    [Fact]
    public void AZeroDivisorRaisesBeforeANullDividendIsLookedAt()
    {
        // VFP checks the divisor first: MOD(N_,0) raises over a null N_, where MOD(5,N_) is null.
        Action act = () => OverNulls("MOD(N_,0)");

        act.Should().Throw<ExpressionEvaluationException>().Which.VisualFoxProError.Should().Be(1307);
    }

    [Fact]
    public void ANullComparedIsNotEqualToZero()
    {
        // The wrong record set this closes: ABS of a null read as zero, so ABS(N_) = 0 held.
        ExpressionValue value = OverNulls("ABS(N_) = 0");

        value.IsNull.Should().BeTrue();
        value.Logical.Should().BeFalse();
    }

    [Theory]
    [InlineData("MIN(1,2)", 1)]
    [InlineData("MAX(1,2)", 2)]
    [InlineData("MIN(1,2,3)", 1)]
    [InlineData("MAX(3,1,2)", 3)]
    [InlineData("MIN(-1,1)", -1)]
    public void TheSmallestAndLargestTakeAsManyOperandsAsTheyAreGiven(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    [InlineData("MIN(\"A\",\"B\")", "A")]
    [InlineData("MAX(\"A\",\"B\")", "B")]
    [InlineData("MIN(\"B\",\"A\",\"C\")", "A")]
    public void AndCompareStringsToo(string expression, string expected)
    {
        // A second row each, chosen by overload resolution from what the operands turned out to be.
        Text(expression).Should().Be(expected);
    }

    [Theory]
    [InlineData("LEN(\"AB\")", 2)]
    [InlineData("LEN(\"\")", 0)]
    [InlineData("LEN(\"  \")", 2)]
    // A trim shortens how far a value reaches without shortening its width, and this reads the reach,
    // which is what Visual FoxPro's own shortened string would have measured.
    [InlineData("LEN(TRIM(\"abc   \"))", 3)]
    [InlineData("ASC(\"A\")", 65)]
    [InlineData("ASC(\"AB\")", 65)]
    public void MeasuringAndCodingAString(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    // Counting from one, and zero for absent -- not minus one, and not an index.
    [InlineData("AT(\"B\",\"AB\")", 2)]
    [InlineData("AT(\"A\",\"AB\")", 1)]
    [InlineData("AT(\"Z\",\"AB\")", 0)]
    [InlineData("AT(\"\",\"AB\")", 0)]
    // The third operand says which occurrence, and one is meant where it is left out.
    [InlineData("AT(\"B\",\"ABAB\")", 2)]
    [InlineData("AT(\"B\",\"ABAB\",2)", 4)]
    [InlineData("AT(\"B\",\"ABAB\",3)", 0)]
    public void SearchingCountsFromOneAndAnswersZeroForAbsent(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    [InlineData("LOWER(\"AB\")", "ab")]
    [InlineData("LOWER(\"aB1c\")", "ab1c")]
    [InlineData("REPLICATE(\"A\",3)", "AAA")]
    [InlineData("REPLICATE(\"AB\",2)", "ABAB")]
    [InlineData("REPLICATE(\"AB\",0)", "")]
    public void LoweringAndRepeating(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    [Theory]
    // VFPPROP.vfp.dump.txt: a word begins at the start and after white space, and after nothing
    // else -- not an apostrophe, a hyphen, a full stop, a digit or a bracket. The rest is lowered.
    [InlineData("PROPER(\"john smith\")", "John Smith")]
    [InlineData("PROPER(\"JOHN SMITH\")", "John Smith")]
    [InlineData("PROPER(\"  leading blanks\")", "  Leading Blanks")]
    [InlineData("PROPER(\"o'neil\")", "O'neil")]
    [InlineData("PROPER(\"mary-jane\")", "Mary-jane")]
    [InlineData("PROPER(\"mr.smith\")", "Mr.smith")]
    [InlineData("PROPER(\"1st place\")", "1st Place")]
    [InlineData("PROPER(\"(paren) word\")", "(paren) Word")]
    [InlineData("PROPER(\"McDonald\")", "Mcdonald")]
    [InlineData("PROPER(\"\")", "")]
    public void ProperCaseBeginsAWordOnlyAfterWhiteSpace(string expression, string expected) =>
        Text(expression).Should().Be(expected);

    [Fact]
    public void ProperCaseKeepsTheOperandsWidthAndReach()
    {
        // Visual FoxPro keys PROPER over a C(30) to 30, as UPPER and LOWER keep their operand's width.
        Compile("PROPER(\"ab   \")").Length.Should().Be(5);
        ExpressionValue value = Value("PROPER(TRIM(\"ab   \"))");

        value.Bytes.Length.Should().Be(5);
        value.TrimmedLength.Should().Be(2);
    }

    [Fact]
    public void ProperCaseOfANullIsNull()
    {
        ExpressionValue value = OverNulls("PROPER(C_)");

        value.IsNull.Should().BeTrue();
        value.Type.Should().Be(ExpressionType.Character);
    }

    [Fact]
    public void RepeatingSizesItsResultByTheCountTimesTheOperand()
    {
        // Which is what makes it keyable: Visual FoxPro sizes such a tag's key the same way, so a
        // C(20) field repeated twice keys to forty.
        Compile("REPLICATE(\"ABCDE\",4)").Length.Should().Be(20);
        ExpressionKey.Length(Compile("REPLICATE(\"ABCDE\",4)")).Should().Be(20);
    }

    [Fact]
    public void LoweringLeavesAByteBeyondAsciiAlone()
    {
        // The gap Visual FoxPro does not have: it maps by the table's code page, and only the ASCII
        // path is implemented. Stated here so the limit is visible in the tests rather than only in
        // the state file, and the one oracle case above 0x7F is excluded by name in the gate.
        Bytes("LOWER(\"A\" + CHR(201))").Should().Equal((byte)'a', (byte)0xC9);
    }

    [Fact]
    public void TheReferenceItselfHasNoneOfThemAndTheirRowsSaySo()
    {
        foreach (string name in (string[])["ABS", "INT", "ROUND", "MOD", "MIN", "MAX", "SQRT",
            "SIGN", "LEN", "AT", "ASC", "LOWER", "REPLICATE", "PROPER"])
        {
            int row = FunctionTable.FindByName(name);

            row.Should().BeGreaterThanOrEqualTo(0, name);
            FunctionTable.IsVisualFoxPro(row).Should().BeTrue($"{name} is not a row the reference has");
        }
    }

    [Fact]
    public void ANameTheReferenceDoesHaveIsStillFoundInItsOwnBand()
    {
        // The second band is searched after the first and never instead of it, so nothing shadows a
        // reference row.
        FunctionTable.IsVisualFoxPro(FunctionTable.FindByName("UPPER")).Should().BeFalse();
        FunctionTable.IsVisualFoxPro(FunctionTable.FindByName("STR")).Should().BeFalse();
        FunctionTable.FindByName("TOOLONGFN").Should().Be(-1, "nine characters, and no row has nine");
    }

    private static CompiledExpression Compile(string expression) =>
        TypeResolver.Resolve(ExpressionParser.Parse(expression, None));

    private static ExpressionValue Value(string expression) =>
        new ExpressionEvaluator(Compile(expression)).Evaluate(new EvaluationContext());

    private static string Text(string expression)
    {
        ExpressionValue value = Value(expression);
        return Encoding.Latin1.GetString(value.Trimmed);
    }

    private static byte[] Bytes(string expression) => Value(expression).Bytes.ToArray();

    // N_ N(8,2) at 1, C_ C(10) at 9, I_ I at 19, the null flags at 23: VFPBAND's fields, plus the
    // integer its null-band probes take a null count or place from.
    private static readonly FieldDefinition NullFlags =
        new("_NullFlags", '0', '0', 1, 0, 23, false, false, false, false, null);

    private static readonly IFieldMetadata Nullable = new TableFieldMetadata(
        [
            new FieldDefinition("N_", 'N', 'N', 8, 2, 1, false, false, false, false, 0),
            new FieldDefinition("C_", 'C', 'C', 10, 0, 9, false, false, false, false, 1),
            new FieldDefinition("I_", 'I', 'I', 4, 0, 19, false, false, false, false, 2),
            NullFlags,
        ],
        "T", longFieldNames: false);

    /// <summary>
    /// Evaluates over a record whose three fields are all null, and hold zero and blanks beneath.
    /// </summary>
    /// <value>
    /// Zero beneath the null on purpose: a null divisor read as its stored zero would raise, and the
    /// answer has to be null instead.
    /// </value>
    private static ExpressionValue OverNulls(string expression)
    {
        RecordBuffer record = new(24);
        Span<byte> raw = record.Raw;
        raw.Fill((byte)' ');
        Encoding.ASCII.GetBytes("    0.00").CopyTo(raw[1..]);
        raw[19..23].Clear();
        raw[23] = 0b111;

        return new ExpressionEvaluator(TypeResolver.Resolve(ExpressionParser.Parse(expression, Nullable)))
            .Evaluate(new EvaluationContext { Record = record, NullFlags = NullFlags });
    }
}
