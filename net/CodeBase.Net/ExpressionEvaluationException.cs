namespace CodeBase.Net;

/// <summary>
/// Reports an expression that has no answer for the record it was evaluated over, as Visual FoxPro reports it.
///
/// Two of the functions Visual FoxPro adds have arguments they cannot answer: [c]MOD[/c] by a zero
/// divisor, and [c]SQRT[/c] of a number below zero. Visual FoxPro raises an error for each, and so
/// does this library, at the record where it happens. CodeBase has neither function, so the
/// [clink=CodeBase.Net.ErrorCode]ErrorCode[/clink] is its general [c]e4result[/c];
/// [clink=CodeBase.Net.ExpressionEvaluationException.VisualFoxProError]VisualFoxProError[/clink] tells
/// the two apart, with Visual FoxPro's own number.
///
/// An expression evaluates what Visual FoxPro evaluates and no more: only the branch an [c]IIF[/c]
/// chose, and the operands of [c].AND.[/c] and [c].OR.[/c] from the left up to the first that decides.
/// So a filter that tests [c]N[/c] is not negative before it takes [c]SQRT(N)[/c] raises nothing, and a
/// query raises only on a record where a scan by Visual FoxPro would have raised too. Which records a query reads depends on
/// its tags, so a query whose tags answer the guard may not raise where a scan does.
/// </summary>
public sealed class ExpressionEvaluationException : CodeBaseException
{
    /// <summary>
    /// Visual FoxPro's error number for dividing by zero, 1307.
    /// </summary>
    public const int DivisionByZero = 1307;

    /// <summary>
    /// Visual FoxPro's error number for the square root of a negative number, 61.
    /// </summary>
    public const int NegativeSquareRoot = 61;

    /// <summary>
    /// Initializes a new instance with Visual FoxPro's error number and message.
    /// </summary>
    /// <param name="visualFoxProError">Visual FoxPro's number for the error.</param>
    /// <param name="message">Visual FoxPro's message for it.</param>
    public ExpressionEvaluationException(int visualFoxProError, string message)
        : base(ErrorCode.Result, message)
    {
        VisualFoxProError = visualFoxProError;
    }

    /// <summary>
    /// Gets the number Visual FoxPro reports the same error with.
    /// </summary>
    /// <value>
    /// [clink=CodeBase.Net.ExpressionEvaluationException.DivisionByZero]DivisionByZero[/clink] or
    /// [clink=CodeBase.Net.ExpressionEvaluationException.NegativeSquareRoot]NegativeSquareRoot[/clink].
    /// </value>
    public int VisualFoxProError { get; }
}
