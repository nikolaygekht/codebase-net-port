namespace CodeBase.Net;

/// <summary>
/// Identifies why an operation failed, using the error numbers of the original CodeBase library.
///
/// The values are the [c]e4[/c] constants of the C library, preserved exactly, so a failure here
/// can be compared against the reference implementation and against its documentation. Only the
/// codes the port actually raises are listed; more are added as the capabilities that raise them
/// are ported.
///
/// Codes that describe flow rather than failure, such as end of file or key not found, are not
/// errors and are never reported this way. They are return values.
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// The file could not be opened.
    /// </summary>
    Open = -60,

    /// <summary>
    /// The data file is invalid or corrupt.
    /// </summary>
    Data = -200,

    /// <summary>
    /// A field descriptor names a type this library does not recognize.
    /// </summary>
    FieldType = -220,

    /// <summary>
    /// The index file is invalid or corrupt.
    /// </summary>
    /// <value>
    /// The C library's [c]e4index[/c] (d4defs.h:2018). Raised where a tree contradicts itself: a key
    /// whose compression counts do not fit its length, a node reference outside the file, a header
    /// that describes an index this library cannot read.
    /// </value>
    Index = -310,

    /// <summary>
    /// An operation was asked for in a state that has no meaning for it.
    /// </summary>
    /// <value>
    /// Raised where the caller has not done something first, such as skipping before positioning on
    /// any record. The C library's [c]e4info[/c] (d4defs.h:2040).
    /// </value>
    Info = -910,

    /// <summary>
    /// The file uses a feature this library does not implement.
    /// </summary>
    NotSupported = -1090,
}
