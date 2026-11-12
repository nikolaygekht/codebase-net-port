namespace CodeBase.Net.Dbf;

/// <summary>
/// The version bytes that decide how a DBF file is read.
///
/// Only the ones that change behaviour by identity are named. Everything else is decided by
/// comparison, not by equality: whether Visual FoxPro field types are allowed is whether the
/// version reaches 0x30, which is why a Visual FoxPro 9 table needs no case of its own except for
/// the one type letter it gives a different meaning.
/// Governing specification: DBF-FORMAT.md section 2.1.
/// </summary>
internal static class DbfVersion
{
    /// <summary>
    /// A Visual FoxPro 3.0 table using no CodeBase extension.
    /// </summary>
    public const byte VisualFoxPro = 0x30;

    /// <summary>
    /// A Visual FoxPro table using at least one CodeBase extension, read as though it were 0x30.
    /// </summary>
    /// <value>
    /// The extensions in use are recorded in the header's feature flags. Visual FoxPro itself uses
    /// this byte for a different purpose, so a table carrying it is not necessarily a CodeBase one.
    /// </value>
    public const byte VisualFoxProExtended = 0x31;

    /// <summary>
    /// A Visual FoxPro 9 table, read as an older table is except that W there means a Blob.
    /// </summary>
    /// <value>
    /// CodeBase uses W for its wide string and reads one whatever the version (D4OPEN.C:2555). Visual
    /// FoxPro 9 uses it for a Blob, a reference into the memo file, so on this version the port
    /// refuses the field rather than misread it (ADR-76).
    /// </value>
    public const byte VisualFoxPro9 = 0x32;
}
