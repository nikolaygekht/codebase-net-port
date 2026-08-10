namespace CodeBase.Net;

/// <summary>
/// The code page a table records for its text, named by its header mark.
///
/// A member's value is the mark stored in header byte 29, not the code page number: [c]Cp1251[/c] is
/// [c]0xC9[/c]. The [c]CodePageNumber[/c] property of [clink=CodeBase.Net.Table]Table[/clink] reports
/// the number itself. The set is the twenty-six marks Visual FoxPro documents, which is wider and in one
/// place different from the six values the original C library defines. The code page governs how
/// record text is read back as characters; it has nothing to do with index keys, which are ordered by
/// their stored bytes and never by a culture. Governing specification: DBF-FORMAT.md section 8.1.
/// </summary>
public enum CodePage
{
    /// <summary>
    /// The table names a code page this library does not recognize.
    /// </summary>
    /// <value>
    /// Not an error. The byte is kept as it was found and text is read using the engine's default,
    /// because refusing to open a readable table over how its text displays would be the greater
    /// harm. Writing the table back preserves the mark rather than replacing it.
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
    /// The standard Macintosh code page.
    /// </summary>
    /// <value>
    /// The one mark where the two authorities disagree: the original C library calls it an unknown
    /// placeholder kept for backward compatibility, while Visual FoxPro documents it as code page
    /// 10000. Visual FoxPro is the compatibility target, so it wins. See ADR-19.
    /// </value>
    Cp10000 = 0x04,

    /// <summary>
    /// The DOS Eastern European code page.
    /// </summary>
    Cp852 = 0x64,

    /// <summary>
    /// The DOS Russian code page.
    /// </summary>
    Cp866 = 0x65,

    /// <summary>
    /// The DOS Nordic code page.
    /// </summary>
    Cp865 = 0x66,

    /// <summary>
    /// The DOS Icelandic code page.
    /// </summary>
    Cp861 = 0x67,

    /// <summary>
    /// The Kamenicky Czech code page for DOS.
    /// </summary>
    /// <value>
    /// One of the two marks whose code page .NET cannot supply at all, registered provider or not:
    /// it is a FoxPro-era DOS code page that Windows never defined. Asking for the text encoding of
    /// such a table fails; asking for its number does not.
    /// </value>
    Cp895 = 0x68,

    /// <summary>
    /// The Mazovia Polish code page for DOS.
    /// </summary>
    /// <value>
    /// The other mark with no .NET encoding behind it. See [c]Cp895[/c].
    /// </value>
    Cp620 = 0x69,

    /// <summary>
    /// The DOS Greek code page.
    /// </summary>
    Cp737 = 0x6A,

    /// <summary>
    /// The DOS Turkish code page.
    /// </summary>
    Cp857 = 0x6B,

    /// <summary>
    /// The Windows Traditional Chinese code page, used in Hong Kong SAR and Taiwan.
    /// </summary>
    /// <value>
    /// A multi-byte code page, so a character field's declared length counts bytes and not
    /// characters: a character can be cut in half at the field boundary, and the bytes of one
    /// character can include values that are ASCII characters in their own right.
    /// </value>
    Cp950 = 0x78,

    /// <summary>
    /// The Windows Korean code page.
    /// </summary>
    /// <value>
    /// Multi-byte. See [c]Cp950[/c].
    /// </value>
    Cp949 = 0x79,

    /// <summary>
    /// The Windows Simplified Chinese code page, used in the PRC and Singapore.
    /// </summary>
    /// <value>
    /// Multi-byte. See [c]Cp950[/c].
    /// </value>
    Cp936 = 0x7A,

    /// <summary>
    /// The Windows Japanese code page.
    /// </summary>
    /// <value>
    /// Multi-byte. See [c]Cp950[/c].
    /// </value>
    Cp932 = 0x7B,

    /// <summary>
    /// The Windows Thai code page.
    /// </summary>
    Cp874 = 0x7C,

    /// <summary>
    /// The Windows Hebrew code page.
    /// </summary>
    Cp1255 = 0x7D,

    /// <summary>
    /// The Windows Arabic code page.
    /// </summary>
    Cp1256 = 0x7E,

    /// <summary>
    /// The Russian Macintosh code page.
    /// </summary>
    Cp10007 = 0x96,

    /// <summary>
    /// The Eastern European Macintosh code page.
    /// </summary>
    Cp10029 = 0x97,

    /// <summary>
    /// The Greek Macintosh code page.
    /// </summary>
    Cp10006 = 0x98,

    /// <summary>
    /// The Windows Central European code page.
    /// </summary>
    Cp1250 = 0xC8,

    /// <summary>
    /// The Windows Cyrillic code page.
    /// </summary>
    Cp1251 = 0xC9,

    /// <summary>
    /// The Windows Turkish code page.
    /// </summary>
    Cp1254 = 0xCA,

    /// <summary>
    /// The Windows Greek code page.
    /// </summary>
    Cp1253 = 0xCB,
}
