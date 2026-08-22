namespace CodeBase.Net.IO;

/// <summary>
/// A file gave fewer bytes than a reader insisted on having.
///
/// Internal, and raised only by [c]SourceReader[/c]. It carries the numbers and no prose, because the
/// only thing worth saying about a short read is which structure was being read, and the shared reader
/// is the one place that cannot know. Every caller catches this and raises a
/// [clink=CodeBase.Net.CodeBaseException]CodeBaseException[/clink] that names what it was reading and
/// classifies the failure its own way.
///
/// **It is caught on the way up rather than described on the way down**, and that is the point: a
/// description handed downwards is built on every successful read and thrown away, which on the index
/// path is one formatted string per block read. Deriving from
/// [clink=CodeBase.Net.CodeBaseException]CodeBaseException[/clink] is the safety net for a caller that
/// forgets to translate — the failure is then still typed the way this library's failures are, rather
/// than escaping as something a caller has no reason to expect.
/// </summary>
internal sealed class ShortReadException : CodeBaseException
{
    /// <summary>
    /// Initializes a new instance from what was asked for and what arrived.
    /// </summary>
    /// <param name="offset">Where the read started, counted from the start of the file.</param>
    /// <param name="needed">How many bytes were wanted.</param>
    /// <param name="got">How many bytes arrived.</param>
    public ShortReadException(long offset, int needed, int got)
        : base(ErrorCode.Data, $"A read of {needed} bytes at offset {offset} got {got}.")
    {
        Offset = offset;
        Needed = needed;
        Got = got;
    }

    /// <summary>Gets where the read started, counted from the start of the file.</summary>
    /// <value>The offset the caller asked to read from.</value>
    public long Offset { get; }

    /// <summary>Gets how many bytes were wanted.</summary>
    /// <value>The length the caller asked for.</value>
    public int Needed { get; }

    /// <summary>Gets how many bytes arrived.</summary>
    /// <value>The length the file actually gave, always less than what was needed.</value>
    public int Got { get; }
}
