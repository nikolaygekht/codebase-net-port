namespace CodeBase.Net.Dbf;

/// <summary>
/// Reads a Visual FoxPro 3.0 table, stored as version 0x30 or as 0x31 with CodeBase extensions.
///
/// This is the one variant that reads descriptor flags, so nullable fields, binary fields,
/// auto-increment and auto-timestamp exist only on a table read this way.
/// </summary>
internal sealed class VisualFoxProVariant : IDbfFormatVariant
{
    /// <summary>
    /// The bit of the header's companion-file byte that marks an attached memo file.
    /// </summary>
    private const byte MemoBit = 0x02;

    private VisualFoxProVariant()
    {
    }

    /// <summary>
    /// Gets the single instance, the variant having no state to keep.
    /// </summary>
    public static VisualFoxProVariant Instance { get; } = new();

    /// <inheritdoc/>
    public byte NormalizedVersion => DbfVersion.VisualFoxPro;

    /// <inheritdoc/>
    public bool AllowsVisualFoxProTypes => true;

    /// <inheritdoc/>
    public bool InterpretsDescriptorFlags => true;

    /// <inheritdoc/>
    public bool HasMemo(byte tableFlags) => (tableFlags & MemoBit) != 0;
}
