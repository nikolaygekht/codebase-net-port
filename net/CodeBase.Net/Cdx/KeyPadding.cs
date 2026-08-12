namespace CodeBase.Net.Cdx;

/// <summary>
/// The byte a key's trailing-pad count stands for, and where that byte comes from.
///
/// Reconstructing a stored key needs it: a leaf entry records how many trailing bytes were dropped,
/// not what they were. It is a space for a machine-collated character key and a NUL for everything
/// else (i4init.c:557-604).
///
/// The file does not store the key's type, so for a machine-collated tag the pad byte cannot be
/// derived from the header at all: the C library parses the key expression and asks its type, which
/// needs the expression engine. Until that exists, a machine-collated tag is told its pad byte by
/// its caller. A collated tag is not, because any collation other than machine order fixes the pad
/// byte at NUL by itself.
///
/// Governing specification: KEY-COLLATION.md section 3.7, and ADR-26 with ADR-27.
/// </summary>
internal static class KeyPadding
{
    /// <summary>
    /// The pad byte of a machine-collated character key, and of the tag directory.
    /// </summary>
    /// <value>
    /// Character fields are stored space-padded, so their keys are too (i4init.c:591). The tag
    /// directory's keys are tag names padded the same way (i4init.c:520-525).
    /// </value>
    public const byte Space = 0x20;

    /// <summary>
    /// The pad byte of every key that is not a machine-collated character key.
    /// </summary>
    /// <value>
    /// Numeric, date, currency and collated keys all use it (i4init.c:563-604). For a collated key it
    /// applies even though the key is character data, which is the case a reader is most likely to
    /// get wrong.
    /// </value>
    public const byte Nul = 0x00;
}

/// <summary>
/// Supplies the pad byte for a tag whose collation does not settle it.
///
/// Called only for a machine-collated tag, and only at open. The implementation this port will
/// eventually use asks the key expression for its type; a test supplies the value the reference
/// implementation recorded instead.
/// </summary>
/// <param name="header">The tag header, which carries the expression text and the key length.</param>
/// <returns>
/// [c]KeyPadding.Space[/c] for a character key, [c]KeyPadding.Nul[/c] for a numeric, date or
/// currency one.
/// </returns>
internal delegate byte PadByteResolver(IndexHeader header);
