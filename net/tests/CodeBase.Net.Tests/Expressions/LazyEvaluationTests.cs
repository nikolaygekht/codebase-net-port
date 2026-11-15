using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// Which operands an expression evaluates: all of them, except where Visual FoxPro is lazy.
///
/// An expression has no side effects, so the order and the laziness of its evaluation were invisible
/// until a function could raise. Visual FoxPro evaluates the branch an [c]IIF[/c] chose and not the
/// other, and stops an [c]AND[/c] or [c]OR[/c] at the first operand that decides, left to right; every
/// other function evaluates every argument. A guard written the way Visual FoxPro's users write one
/// therefore has to hold here too. Every case is a row of the [c]guard[/c] section of
/// VFPBAND.vfp.dump.txt, in this port's syntax.
///
/// Governing decision: ADR-81.
/// </summary>
public sealed class LazyEvaluationTests
{
    private static readonly IFieldMetadata None = new TableFieldMetadata([], "T", longFieldNames: false);

    [Theory]
    [InlineData("IIF(.T., 1, SQRT(-1))", 1)]
    [InlineData("IIF(.F., SQRT(-1), 1)", 1)]
    public void TheBranchAConditionalDidNotChooseIsNotEvaluated(string expression, double expected) =>
        Value(expression).Number.Should().Be(expected);

    [Theory]
    [InlineData("IIF(.T., SQRT(-1), 1)", 61)]
    [InlineData("IIF(.F., 1, MOD(5,0))", 1307)]
    public void TheBranchItChoseIs(string expression, int error) =>
        Raises(expression).Should().Be(error);

    [Theory]
    [InlineData(".F. .AND. SQRT(-1) > 0", false)]
    [InlineData(".T. .OR. SQRT(-1) > 0", true)]
    [InlineData(".NOT.(.F. .AND. SQRT(-1) > 0)", true)]
    [InlineData(".T. .AND. .F. .AND. SQRT(-1) > 0", false)]
    [InlineData(".F. .OR. .T. .OR. SQRT(-1) > 0", true)]
    public void AConnectiveStopsAtTheFirstOperandThatDecides(string expression, bool expected) =>
        Value(expression).Logical.Should().Be(expected);

    [Theory]
    [InlineData("SQRT(-1) > 0 .AND. .F.", 61)]
    [InlineData("SQRT(-1) > 0 .OR. .T.", 61)]
    [InlineData(".T. .AND. MOD(5,0) = 0", 1307)]
    [InlineData(".T. .AND. SQRT(-1) > 0 .AND. .F.", 61)]
    public void AndEvaluatesItsOperandsLeftToRight(string expression, int error) =>
        Raises(expression).Should().Be(error);

    [Fact]
    public void AnOrdinaryFunctionEvaluatesEveryArgument() =>
        Raises("MIN(1, SQRT(-1))").Should().Be(61);

    [Fact]
    public void AConnectiveThatDoesNotStopStillAnswersAsBefore()
    {
        // The laziness changes which operands are computed, never what a computed one answers.
        Value(".T. .AND. .T. .AND. .T.").Logical.Should().BeTrue();
        Value(".F. .OR. .F. .OR. .F.").Logical.Should().BeFalse();
        Value("IIF(1 = 1, 2, 3)").Number.Should().Be(2);
        Value("IIF(1 = 2, 2, 3)").Number.Should().Be(3);
    }

    private static ExpressionValue Value(string expression) =>
        new ExpressionEvaluator(TypeResolver.Resolve(ExpressionParser.Parse(expression, None)))
            .Evaluate(new EvaluationContext());

    private static int Raises(string expression)
    {
        Action act = () => Value(expression);
        ExpressionEvaluationException raised = act.Should().Throw<ExpressionEvaluationException>().Which;

        raised.Code.Should().Be(ErrorCode.Result);
        return raised.VisualFoxProError;
    }
}
