namespace CodeBase.Net.Cdx;

/// <summary>
/// Which collation a tag's keys were built with, as the header names it.
///
/// The header holds an eight-byte name: empty means machine order, [c]GENERAL[/c] means the Visual
/// FoxPro weight tables for the table's code page, and [c]CBnnnnn[/c] names one of CodeBase's own
/// collations by its ordinal, whatever the table's code page (ADR-84). Anything else the C library
/// refuses (i4init.c:372-418).
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

    /// <summary>GENERAL's cp1252 weights by name, [c]CB00002[/c], whatever the table's code page.</summary>
    GeneralCp1252,

    /// <summary>GENERAL's cp437 weights by name, [c]CB00003[/c].</summary>
    GeneralCp437,

    /// <summary>GENERAL's cp850 weights by name, [c]CB00007[/c].</summary>
    GeneralCp850,

    /// <summary>Croatian on cp1250, case-sensitive, [c]CB00005[/c]: a simple, one-for-one mapping.</summary>
    CroatianCp1250,

    /// <summary>Croatian on cp1250, case-insensitive, [c]CB00006[/c]: a simple mapping.</summary>
    CroatianUpperCp1250,

    /// <summary>Avaya's cp1252 weights, [c]CB00008[/c]: heads, tails and cp1252's expansions, as GENERAL.</summary>
    Avaya1252,

    /// <summary>Spanish on cp1252, [c]CB00009[/c]: a simple mapping.</summary>
    SpanishCp1252,

    /// <summary>Spanish on cp850, [c]CB00010[/c]: a simple mapping.</summary>
    SpanishCp850,
}
