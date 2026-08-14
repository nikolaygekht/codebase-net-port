namespace CodeBase.Net;

/// <summary>
/// What a positioning seek found, and which way it had to look to land.
/// </summary>
/// <value>
/// Returned by the two seeks that position on a neighbour by design. The seeks that ask whether a
/// value is present return [clink=CodeBase.Net.GoResult]GoResult[/clink] instead, because there the
/// answer is only yes or no.
/// </value>
public enum SeekResult
{
    /// <summary>The value is there, and the cursor is on it.</summary>
    Found,

    /// <summary>
    /// The value is not there, and the cursor is on the first record beyond it.
    /// </summary>
    /// <value>Only from a seek that looks forwards.</value>
    After,

    /// <summary>
    /// The value is not there, and the cursor is on the last record short of it.
    /// </summary>
    /// <value>Only from a seek that looks backwards.</value>
    Before,

    /// <summary>Every key in the tag sorts below the value, so there is nothing at or after it.</summary>
    Eof,

    /// <summary>Every key in the tag sorts above the value, so there is nothing at or before it.</summary>
    Bof,
}
