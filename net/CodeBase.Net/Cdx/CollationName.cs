namespace CodeBase.Net.Cdx;

/// <summary>
/// Which collation a tag's keys were built with, as the header names it.
///
/// The header holds an eight-byte name: empty means machine order, [c]GENERAL[/c] means the Visual
/// FoxPro weight tables for the table's code page, and [c]CBnnnnn[/c] names a CodeBase-only
/// collation loaded from disk. Anything else the C library refuses (i4init.c:372-418).
///
/// Reading a collated tag needs no weight table, because the keys are read rather than computed —
/// only seeking one does, because there the search value has to be translated before it is
/// compared. What the name does decide here is the pad byte: any collation other than machine order
/// makes it a NUL.
///
/// Governing specification: KEY-COLLATION.md section 4, and ADR-27.
/// </summary>
internal enum CollationName
{
    /// <summary>
    /// Unsigned byte order, with no translation at all.
    /// </summary>
    /// <value>
    /// The header's name field is eight zero bytes. Character keys are the field's bytes verbatim and
    /// pad with spaces.
    /// </value>
    Machine,

    /// <summary>
    /// The Visual FoxPro GENERAL collation for the table's code page.
    /// </summary>
    /// <value>
    /// Keys are twice the field width: a block of primary weights followed by a block of secondary
    /// ones, NUL-filled. Which weight table applies is chosen by the code page of the table, not by
    /// anything in the index (i4init.c:378-405).
    /// </value>
    General,
}
