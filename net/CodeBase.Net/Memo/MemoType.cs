namespace CodeBase.Net.Memo;

/// <summary>
/// What kind of content a memo entry holds, as its block header declares.
///
/// Only text is ever written by the reference implementation for an ordinary memo, and compressed
/// only when that CodeBase extension is switched on. The other two are read back and reported
/// without being validated, so a value outside this list is passed through rather than refused.
///
/// Governing specification: FPT-MEMO.md section 3.2.
/// </summary>
public enum MemoType
{
    /// <summary>
    /// A picture. Never written by the reference implementation.
    /// </summary>
    Picture = 0,

    /// <summary>
    /// Text or arbitrary bytes, which is what every ordinary memo is.
    /// </summary>
    Text = 1,

    /// <summary>
    /// An embedded object. Never written by the reference implementation.
    /// </summary>
    ObjectLinking = 2,

    /// <summary>
    /// A compressed entry, which is a CodeBase extension Visual FoxPro cannot read.
    /// </summary>
    /// <value>
    /// Reading one is not supported yet: no file this project can generate contains one, so a
    /// decoder for it could not be checked against the reference implementation. See ADR-23.
    /// </value>
    Compressed = 3,
}
