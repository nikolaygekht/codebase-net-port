using System.Text;
using AwesomeAssertions;
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
    private static readonly IFieldMetadata None = new TableFieldMetadata([], "T");

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
            "SIGN", "LEN", "AT", "ASC", "LOWER", "REPLICATE"])
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
}
