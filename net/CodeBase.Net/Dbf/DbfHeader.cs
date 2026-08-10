using System.Buffers.Binary;

namespace CodeBase.Net.Dbf;

/// <summary>
/// The 32-byte header that begins every DBF file.
///
/// Decoded from bytes and nothing more: it performs no input and knows nothing about the file the
/// bytes came from. Checks that need the file, such as whether it is long enough to hold the
/// records the header claims, belong to whatever opened it.
///
/// Every multi-byte field here is little-endian. That is true of the whole DBF format, but not of
/// its companions, so each read states its endianness rather than relying on the platform.
/// Governing specification: DBF-FORMAT.md section 2.
/// </summary>
internal readonly struct DbfHeader
{
    /// <summary>
    /// The number of bytes a header occupies.
    /// </summary>
    public const int Size = 32;

    /// <summary>
    /// The smallest header length that leaves room for a descriptor region and its terminator.
    /// </summary>
    private const int MinimumHeaderLength = Size + 1;

    private DbfHeader(
        byte version,
        byte lastUpdateYear,
        byte lastUpdateMonth,
        byte lastUpdateDay,
        int recordCount,
        int headerLength,
        int recordLength,
        FeatureFlags flags,
        double autoIncrementValue,
        byte tableFlags,
        byte codePage)
    {
        Version = version;
        LastUpdateYear = lastUpdateYear;
        LastUpdateMonth = lastUpdateMonth;
        LastUpdateDay = lastUpdateDay;
        RecordCount = recordCount;
        HeaderLength = headerLength;
        RecordLength = recordLength;
        Flags = flags;
        AutoIncrementValue = autoIncrementValue;
        TableFlags = tableFlags;
        CodePage = codePage;
    }

    /// <summary>
    /// Gets the version byte, exactly as stored.
    /// </summary>
    /// <value>Values in use include 0x03, 0x30, 0x31, 0x32 and 0xF5. See DBF-FORMAT.md section 2.1.</value>
    public byte Version { get; }

    /// <summary>
    /// Gets the year of the last update, as the two digits the file stores.
    /// </summary>
    /// <value>
    /// The stored byte, which the FoxPro build writes as the year modulo 100. The century is not
    /// recoverable from the file, so this is reported as written rather than guessed at.
    /// </value>
    public byte LastUpdateYear { get; }

    /// <summary>
    /// Gets the month of the last update, from 1 to 12.
    /// </summary>
    public byte LastUpdateMonth { get; }

    /// <summary>
    /// Gets the day of the last update, from 1 to 31.
    /// </summary>
    public byte LastUpdateDay { get; }

    /// <summary>
    /// Gets the number of records the header claims the file holds, including deleted ones.
    /// </summary>
    public int RecordCount { get; }

    /// <summary>
    /// Gets the length of the header, which is also the file offset at which record data starts.
    /// </summary>
    /// <value>
    /// Counts the terminator byte, and for Visual FoxPro tables the 263 reserved bytes that follow
    /// it, but never a trailing end-of-file marker.
    /// </value>
    public int HeaderLength { get; }

    /// <summary>
    /// Gets the length of one record, including its leading deletion flag.
    /// </summary>
    public int RecordLength { get; }

    /// <summary>
    /// Gets the CodeBase feature flags, which are meaningful only on a version 0x31 table.
    /// </summary>
    /// <value>All clear in a table written by Visual FoxPro itself.</value>
    public FeatureFlags Flags { get; }

    /// <summary>
    /// Gets the running auto-increment counter, a CodeBase extension stored as a double.
    /// </summary>
    /// <value>Zero unless the table declares an auto-increment field.</value>
    public double AutoIncrementValue { get; }

    /// <summary>
    /// Gets the byte recording which companion files exist.
    /// </summary>
    /// <value>
    /// Bit 0x01 marks a production index, bit 0x02 a memo file. Whether the memo bit is the one
    /// that decides is a question of the version, not of this byte alone, so the raw value is kept.
    /// </value>
    public byte TableFlags { get; }

    /// <summary>
    /// Gets the language-driver byte identifying the code page the record text is stored in.
    /// </summary>
    /// <value>Zero means unmarked, which is the usual case. See DBF-FORMAT.md section 8.</value>
    public byte CodePage { get; }

    /// <summary>
    /// Reads a header from the first 32 bytes of a DBF file.
    /// </summary>
    /// <param name="bytes">
    /// The start of the file. Only the first 32 bytes are read; anything beyond them is ignored.
    /// </param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="CodeBaseException">
    /// The bytes are too few, or the header contradicts itself: a record length of zero, a header
    /// length with no room for field descriptors, a negative record count, or a feature flag whose
    /// value cannot be produced by any writer.
    /// </exception>
    public static DbfHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"A DBF header is {Size} bytes; the file holds only {bytes.Length}.");
        }

        byte version = bytes[0];
        int recordCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]);
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]);
        ReadOnlySpan<byte> flagBytes = bytes.Slice(12, FeatureFlags.Count);

        // Rejected before anything divides by it, as the C library does (D4OPEN.C:2230-2231).
        if (recordLength == 0)
            throw new CodeBaseException(ErrorCode.Data, "The header gives a record length of zero.");

        if (headerLength < MinimumHeaderLength)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"The header length of {headerLength} leaves no room for field descriptors.");
        }

        // The count is stored signed, so a corrupt file can present a negative one. Nothing
        // downstream has a sensible reading of that, and no writer produces it.
        if (recordCount < 0)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"The header gives a negative record count of {recordCount}.");
        }

        FeatureFlags flags = FeatureFlags.Parse(
            flagBytes, validate: version == DbfVersion.VisualFoxProExtended);

        return new DbfHeader(
            version,
            bytes[1],
            bytes[2],
            bytes[3],
            recordCount,
            headerLength,
            recordLength,
            flags,
            BinaryPrimitives.ReadDoubleLittleEndian(bytes[20..]),
            bytes[28],
            bytes[29]);
    }
}
