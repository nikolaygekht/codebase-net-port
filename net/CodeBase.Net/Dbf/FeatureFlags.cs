namespace CodeBase.Net.Dbf;

/// <summary>
/// The eight CodeBase feature flags held at offset 12 of a DBF header.
///
/// These are a CodeBase extension, not part of Visual FoxPro. A table written by Visual FoxPro
/// leaves all eight zero, and so does a CodeBase table that uses no extension. They are meaningful
/// only when the version byte is 0x31, which is what declares that the table uses one.
///
/// Each flag occupies a whole byte holding 0 or 1, rather than a bit. Governing specification:
/// DBF-FORMAT.md section 2.2.
/// </summary>
internal readonly struct FeatureFlags
{
    /// <summary>
    /// The number of flag bytes.
    /// </summary>
    public const int Count = 8;

    /// <summary>
    /// The number of flag bytes this library recognizes; the rest must be zero.
    /// </summary>
    private const int KnownCount = 5;

    private FeatureFlags(
        bool hasAutoIncrementField,
        bool mayHaveCompressedMemos,
        bool hasCompressedData,
        bool hasAutoTimestampField,
        bool usesLongFieldNames)
    {
        HasAutoIncrementField = hasAutoIncrementField;
        MayHaveCompressedMemos = mayHaveCompressedMemos;
        HasCompressedData = hasCompressedData;
        HasAutoTimestampField = hasAutoTimestampField;
        UsesLongFieldNames = usesLongFieldNames;
    }

    /// <summary>
    /// Gets a value indicating whether the table has an auto-increment field.
    /// </summary>
    /// <value>When set, the counter in the header is live.</value>
    public bool HasAutoIncrementField { get; }

    /// <summary>
    /// Gets a value indicating whether the memo file may hold compressed entries.
    /// </summary>
    /// <value>Compressed memos are a CodeBase extension that Visual FoxPro cannot read.</value>
    public bool MayHaveCompressedMemos { get; }

    /// <summary>
    /// Gets a value indicating whether the record data itself is compressed.
    /// </summary>
    /// <value>Compressed data is a CodeBase extension that Visual FoxPro cannot read.</value>
    public bool HasCompressedData { get; }

    /// <summary>
    /// Gets a value indicating whether the table has an auto-timestamp field.
    /// </summary>
    public bool HasAutoTimestampField { get; }

    /// <summary>
    /// Gets a value indicating whether field descriptors use the long-name layout.
    /// </summary>
    /// <value>
    /// The long-name layout replaces the fixed 32-byte descriptor with a variable-length one, and
    /// is unreadable by FoxPro. Whether that layout is supported is decided by whoever reads the
    /// descriptor region, not here.
    /// </value>
    public bool UsesLongFieldNames { get; }

    /// <summary>
    /// Reads the eight flag bytes.
    /// </summary>
    /// <param name="bytes">The eight bytes at offset 12 of the header.</param>
    /// <param name="validate">
    /// Whether the values are required to be ones a writer could have produced. The C library
    /// applies this test only to a version 0x31 table, and ignores these bytes entirely otherwise,
    /// so a stray value in a plain Visual FoxPro table must not stop it being read.
    /// </param>
    /// <returns>The decoded flags.</returns>
    /// <exception cref="CodeBaseException">
    /// Validation was asked for and a flag holds a value other than 0 or 1, or a flag beyond the
    /// five this library knows is set. Either means the table declares a feature that cannot be
    /// honoured, and reading it as though it did not would misread the data.
    /// </exception>
    public static FeatureFlags Parse(ReadOnlySpan<byte> bytes, bool validate)
    {
        if (validate)
        {
            // The C library compares all eight bytes against a mask built only from the five it
            // knows, each required to be exactly 1 to survive (D4OPEN.C:2152-2187). So a value of 2
            // in a known flag fails just as a set unknown flag does.
            for (int i = 0; i < KnownCount; i++)
            {
                if (bytes[i] > 1)
                {
                    throw new CodeBaseException(
                        ErrorCode.Data,
                        $"Feature flag {i} holds {bytes[i]}; a flag is a whole byte holding 0 or 1.");
                }
            }

            for (int i = KnownCount; i < Count; i++)
            {
                if (bytes[i] != 0)
                {
                    throw new CodeBaseException(
                        ErrorCode.Data,
                        $"Feature flag {i} is set, so the table uses an extension this library " +
                        "does not implement.");
                }
            }
        }

        return new FeatureFlags(
            bytes[0] == 1,
            bytes[1] == 1,
            bytes[2] == 1,
            bytes[3] == 1,
            bytes[4] == 1);
    }
}
