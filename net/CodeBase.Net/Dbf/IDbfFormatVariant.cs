namespace CodeBase.Net.Dbf;

/// <summary>
/// Answers the questions whose answer depends on which version of the format a file uses.
///
/// Resolved once when a table is opened, so that no later code has to test the version byte again.
/// Scattering those tests is how a format reader acquires a version rule in fifteen places and then
/// gains a sixteenth that disagrees.
///
/// The two questions that look alike are deliberately separate. Whether Visual FoxPro field types
/// may appear is decided by the version reaching 0x30; whether the descriptor flag byte means
/// anything is decided by it being exactly 0x30. A Visual FoxPro 9 table falls on opposite sides of
/// those two, and reproducing that is the point of asking them separately.
/// </summary>
internal interface IDbfFormatVariant
{
    /// <summary>
    /// Gets the version this file is read as, which is not always the version it stores.
    /// </summary>
    /// <value>
    /// A table marked as using CodeBase extensions is read as a plain Visual FoxPro table, its
    /// extensions having been recorded in the header flags instead.
    /// </value>
    byte NormalizedVersion { get; }

    /// <summary>
    /// Gets a value indicating whether field types introduced by Visual FoxPro may appear.
    /// </summary>
    /// <value>
    /// True once the version reaches 0x30, so a Visual FoxPro 9 table is included even though it is
    /// not otherwise read as a Visual FoxPro one.
    /// </value>
    bool AllowsVisualFoxProTypes { get; }

    /// <summary>
    /// Gets a value indicating whether the flag byte of a field descriptor carries meaning.
    /// </summary>
    /// <value>
    /// True only for a version of exactly 0x30. On any other table the byte is present but is never
    /// read, so nullability, the binary marking, auto-increment and auto-timestamp are all absent
    /// however the bytes are set.
    /// </value>
    bool InterpretsDescriptorFlags { get; }

    /// <summary>
    /// Decides whether the table has a companion memo file.
    /// </summary>
    /// <param name="tableFlags">The header byte recording which companion files exist.</param>
    /// <returns>True when a memo file accompanies the table and must be opened with it.</returns>
    bool HasMemo(byte tableFlags);

    /// <summary>
    /// Chooses how a file of the given version is read.
    /// </summary>
    /// <param name="version">The version byte the file stores.</param>
    /// <returns>The variant that answers the version-dependent questions for that file.</returns>
    static IDbfFormatVariant Resolve(byte version) =>
        version is DbfVersion.VisualFoxPro or DbfVersion.VisualFoxProExtended
            ? VisualFoxProVariant.Instance
            : new LegacyVariant(version);
}
