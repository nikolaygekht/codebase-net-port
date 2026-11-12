using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// Whether a caller chose the date picture, which decides whether a tag keyed through it may be used.
/// </summary>
/// <value>
/// The index records a key's length and not the picture it was built with, so only the caller can say
/// which picture that was. Options nobody set are marked as such; anything a caller sets is a choice,
/// the invariant settings included (ADR-42, ADR-74).
/// </value>
[Trait("Layer", "Unit")]
public sealed class ExpressionOptionsTests
{
    [Fact]
    public void AnEnginesOptionsAreUnchosenUntilSet()
    {
        using CodeBaseEngine engine = new();

        engine.ExpressionOptions.IsChosen.Should().BeFalse();
        engine.ExpressionOptions.DatePicture.Should().Be(ExpressionOptions.Invariant.DatePicture);
        engine.ExpressionOptions.Century.Should().Be(ExpressionOptions.Invariant.Century);

        engine.ExpressionOptions = ExpressionOptions.Invariant;

        engine.ExpressionOptions.IsChosen.Should().BeTrue();
    }

    [Fact]
    public void EveryPublicInstanceAndEveryDerivedOneIsChosen()
    {
        ExpressionOptions.Invariant.IsChosen.Should().BeTrue();
        ExpressionOptions.CodeBase.IsChosen.Should().BeTrue();
        ExpressionOptions.Unchosen.WithCentury(20).IsChosen.Should().BeTrue();
        ExpressionOptions.Unchosen.WithDatePicture("DD.MM.YY").IsChosen.Should().BeTrue();
    }
}
