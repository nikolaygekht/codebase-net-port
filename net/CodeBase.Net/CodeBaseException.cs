namespace CodeBase.Net;

/// <summary>
/// Reports a failure in a database operation, carrying the error number of the original library.
///
/// Every failure this library raises for itself is this type or a type derived from it, and each
/// carries a [clink=CodeBase.Net.ErrorCode]ErrorCode[/clink] so a caller can distinguish a corrupt
/// file from an unsupported one without matching on the message text. Failures raised by the
/// runtime, such as a missing file or a denied read, are allowed through unchanged, because
/// wrapping them would hide diagnostics the caller already knows how to handle.
///
/// Unlike the C library, an error does not become sticky. A failed call throws and nothing is
/// recorded that alters the behaviour of the next one.
/// </summary>
public class CodeBaseException : Exception
{
    /// <summary>
    /// Initializes a new instance with an error code and a message.
    /// </summary>
    /// <param name="code">Why the operation failed.</param>
    /// <param name="message">What failed, in terms the caller can act on.</param>
    public CodeBaseException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Initializes a new instance with an error code, a message, and the failure that caused it.
    /// </summary>
    /// <param name="code">Why the operation failed.</param>
    /// <param name="message">What failed, in terms the caller can act on.</param>
    /// <param name="innerException">The underlying failure, usually one raised by the runtime.</param>
    public CodeBaseException(ErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the error number, matching the constant the original C library uses for this failure.
    /// </summary>
    /// <value>The reason the operation failed.</value>
    public ErrorCode Code { get; }
}
