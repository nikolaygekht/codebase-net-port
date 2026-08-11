namespace CodeBase.Net;

/// <summary>
/// What positioning on a record by number did.
///
/// A return value rather than an exception, because reading past the end while walking a table is
/// ordinary control flow and not a failure. The C library reports it the same way, returning a flow
/// value from [c]d4go[/c] rather than raising an error (d4go.c:234-243).
/// </summary>
public enum GoResult
{
    /// <summary>
    /// The cursor is on the record and its fields can be read.
    /// </summary>
    Ok,

    /// <summary>
    /// There is no such record, so the cursor is on nothing and the record reads as blank.
    /// </summary>
    /// <value>
    /// Not the same as end of file. The end-of-file and beginning-of-table flags say what they said
    /// before the call, and the record number reports that there is no position at all.
    /// </value>
    NoRecord,
}
