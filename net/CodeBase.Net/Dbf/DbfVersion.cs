namespace CodeBase.Net.Dbf;

/// <summary>
/// The version bytes that decide how a DBF file is read.
///
/// Only the two that change behaviour by identity are named. Everything else is decided by
/// comparison, not by equality: whether Visual FoxPro field types are allowed is whether the
/// version reaches 0x30, which is why a Visual FoxPro 9 table needs no case of its own.
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
}
