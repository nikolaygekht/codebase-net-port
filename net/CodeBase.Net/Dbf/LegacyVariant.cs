namespace CodeBase.Net.Dbf;

/// <summary>
/// Reads any table that is not a Visual FoxPro 3.0 one, from dBase III to Visual FoxPro 9.
///
/// A memo file is announced by the version byte itself rather than by the companion-file byte, so a
/// FoxPro 2 table with a memo declares it by being version 0xF5. Nothing here reads the descriptor
/// flag byte, exactly as the C library does not.
///
/// Visual FoxPro 9 is read by this variant, which is not the obvious choice but is what the C
/// library does. It gets the newer field types, because that is decided by the version reaching
/// 0x30, but it is not otherwise treated as a Visual FoxPro table: its descriptor flags go unread,
/// and its memo file goes unopened, because the memo bit it sets is not the one consulted. That
/// last point is a defect in the original rather than a design, but reading a table differently
/// from the reference implementation is worse than reproducing its blind spot.
/// </summary>
internal sealed class LegacyVariant : IDbfFormatVariant
{
    /// <summary>
    /// The bit of the version byte that marks a table as having a memo file.
    /// </summary>
    private const byte MemoBit = 0x80;

    private readonly byte version;

    /// <summary>
    /// Initializes a new instance for a stored version byte.
    /// </summary>
    /// <param name="version">The version byte the file stores.</param>
    public LegacyVariant(byte version) => this.version = version;

    /// <inheritdoc/>
    public byte NormalizedVersion => version;

    /// <summary>
    /// Gets a value indicating whether field types introduced by Visual FoxPro may appear.
    ///
    /// The comparison is signed, which is not a detail that can be dropped. The C library holds the
    /// version in a plain char, signed on the compilers it is built with, so every byte from 0x80
    /// upwards compares as negative and falls below 0x30. That covers 0xF5, the ordinary version of
    /// a FoxPro 2 table with a memo: comparing these bytes as unsigned would admit datetime,
    /// currency and double fields to tables the reference implementation refuses them on.
    /// </summary>
    public bool AllowsVisualFoxProTypes => (sbyte)version >= DbfVersion.VisualFoxPro;

    /// <inheritdoc/>
    public bool InterpretsDescriptorFlags => false;

    /// <inheritdoc/>
    public bool HasMemo(byte tableFlags) => (version & MemoBit) != 0;
}
