namespace CodeBase.Net;

/// <summary>
/// The code page a table records for its text, named by its language-driver byte.
///
/// The code page governs how record text is read back as characters. It has nothing to do with
/// index keys, which are ordered by their stored bytes and never by a culture. Governing
/// specification: DBF-FORMAT.md section 8.
/// </summary>
public enum CodePage
{
    /// <summary>
    /// The table names a code page this library does not recognize.
    /// </summary>
    /// <value>
    /// Not an error. The byte is kept as it was found and text is read using the engine's default,
    /// because refusing to open a readable table over how its text displays would be the greater
    /// harm.
    /// </value>
    Unknown = -1,

    /// <summary>
    /// The table names no code page, which is the usual case.
    /// </summary>
    Unmarked = 0x00,

    /// <summary>
    /// The original DOS United States code page.
    /// </summary>
    Cp437 = 0x01,

    /// <summary>
    /// The DOS international code page.
    /// </summary>
    Cp850 = 0x02,

    /// <summary>
    /// The Windows Western European code page.
    /// </summary>
    Cp1252 = 0x03,

    /// <summary>
    /// A placeholder kept for backward compatibility, naming no actual code page.
    /// </summary>
    Reserved = 0x04,

    /// <summary>
    /// The Windows Central European code page.
    /// </summary>
    Cp1250 = 0xC8,
}
