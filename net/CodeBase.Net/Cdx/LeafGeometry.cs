using System.Buffers.Binary;

namespace CodeBase.Net.Cdx;

/// <summary>
/// How a leaf block packs an entry's record number and its two compression counts into bits.
///
/// The twelve bytes after the block header say how wide each of the three fields is, and give the
/// masks to pull them out with. Widths vary from block to block: they are chosen when the block is
/// first written, from the key length and the table's record count, and a block whose record numbers
/// outgrow their field is rewritten wider.
///
/// The masks are read from the block rather than derived from the widths, because that is what the C
/// library does. Reproducing a reader means reproducing what it reads, not what it could have
/// worked out.
///
/// Governing specification: CDX-FORMAT.md sections 6.1 and 6.2.
/// </summary>
internal readonly struct LeafGeometry
{
    /// <summary>
    /// The number of bytes the leaf geometry occupies, after the block header.
    /// </summary>
    public const int Size = 12;

    /// <summary>The offset of the packed entry array within a leaf block.</summary>
    public const int InfoArrayOffset = NodeHeader.Size + Size;

    /// <summary>The widest packed entry this port reads.</summary>
    /// <value>
    /// Six bytes covers a 32-bit record number with 16-bit counts. Anything wider would mean a key
    /// longer than this library reads at all.
    /// </value>
    private const int MaxInfoLength = 6;

    private LeafGeometry(
        int freeSpace,
        uint recordMask,
        int dupMask,
        int trailMask,
        int recordBits,
        int dupBits,
        int trailBits,
        int infoLength)
    {
        FreeSpace = freeSpace;
        RecordMask = recordMask;
        DupMask = dupMask;
        TrailMask = trailMask;
        RecordBits = recordBits;
        DupBits = dupBits;
        TrailBits = trailBits;
        InfoLength = infoLength;
    }

    /// <summary>Gets the unused bytes between the entry array and the key text.</summary>
    public int FreeSpace { get; }

    /// <summary>Gets the mask that pulls a record number out of a packed entry.</summary>
    public uint RecordMask { get; }

    /// <summary>Gets the mask that pulls a duplicate count out of a packed entry.</summary>
    public int DupMask { get; }

    /// <summary>Gets the mask that pulls a trailing-pad count out of a packed entry.</summary>
    public int TrailMask { get; }

    /// <summary>Gets how many bits of a packed entry hold the record number.</summary>
    public int RecordBits { get; }

    /// <summary>Gets how many bits of a packed entry hold the duplicate count.</summary>
    public int DupBits { get; }

    /// <summary>Gets how many bits of a packed entry hold the trailing-pad count.</summary>
    public int TrailBits { get; }

    /// <summary>Gets the number of bytes each packed entry occupies.</summary>
    public int InfoLength { get; }

    /// <summary>
    /// Reads the geometry of a leaf block.
    /// </summary>
    /// <param name="block">The block, from its start.</param>
    /// <param name="node">The node the block came from, for the message if it is unreadable.</param>
    /// <returns>The decoded geometry.</returns>
    /// <exception cref="CodeBaseException">
    /// The block is short, the free space is impossible, the entry width does not match the field
    /// widths, or the entry is wider than this library reads.
    /// </exception>
    public static LeafGeometry Parse(ReadOnlySpan<byte> block, uint node)
    {
        if (block.Length < InfoArrayOffset)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Leaf node {node} needs {InfoArrayOffset} bytes of headers; only {block.Length} were " +
                $"available.");
        }

        ReadOnlySpan<byte> geometry = block.Slice(NodeHeader.Size, Size);

        int freeSpace = BinaryPrimitives.ReadInt16LittleEndian(geometry);
        uint recordMask = BinaryPrimitives.ReadUInt32LittleEndian(geometry[2..]);
        int dupMask = geometry[6];
        int trailMask = geometry[7];
        int recordBits = geometry[8];
        int dupBits = geometry[9];
        int trailBits = geometry[10];
        int infoLength = geometry[11];

        if (freeSpace < 0 || freeSpace > block.Length)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Leaf node {node} reports {freeSpace} free bytes in a block of {block.Length}.");
        }

        if (infoLength < 1 || infoLength > MaxInfoLength)
        {
            throw new CodeBaseException(
                infoLength < 1 ? ErrorCode.Index : ErrorCode.NotSupported,
                $"Leaf node {node} packs its entries into {infoLength} bytes; this library reads 1 to " +
                $"{MaxInfoLength}.");
        }

        // The three fields are packed end to end and the entry is a whole number of bytes, so their
        // widths have to add up to it. A block where they do not is not a block this decoder can read
        // an entry out of, and reading one anyway would produce a plausible record number.
        if (recordBits + dupBits + trailBits != infoLength * 8)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Leaf node {node} packs {recordBits} + {dupBits} + {trailBits} bits into " +
                $"{infoLength} bytes.");
        }

        return new LeafGeometry(
            freeSpace, recordMask, dupMask, trailMask, recordBits, dupBits, trailBits, infoLength);
    }

    /// <summary>
    /// Unpacks one entry of the array.
    /// </summary>
    /// <param name="block">The whole leaf block.</param>
    /// <param name="index">Which entry, counting from zero.</param>
    /// <returns>The record number and the two compression counts.</returns>
    /// <remarks>
    /// The entry is a little-endian integer over exactly its own bytes: the record number occupies
    /// the low bits, then the duplicate count, then the trailing-pad count. The C library reads a
    /// 32-bit word and masks, which over-reads a three-byte entry into its neighbour and discards
    /// the excess, and for an entry wider than four bytes it shifts its base pointer along by two and
    /// takes sixteen off the shift. Assembling only this entry's bytes gives the same answer, because
    /// the widths sum to the entry and the masks are the stored ones.
    /// </remarks>
    public PackedEntry Unpack(ReadOnlySpan<byte> block, int index)
    {
        int offset = InfoArrayOffset + (index * InfoLength);
        ulong packed = 0;

        for (int i = 0; i < InfoLength; i++)
            packed |= (ulong)block[offset + i] << (i * 8);

        return new PackedEntry(
            (uint)(packed & RecordMask),
            (int)(packed >> RecordBits) & DupMask,
            (int)(packed >> (RecordBits + DupBits)) & TrailMask);
    }
}

/// <summary>
/// What one packed leaf entry says: which record, and how the key was shortened.
/// </summary>
/// <param name="Record">The record number the key points at.</param>
/// <param name="DupCount">
/// How many leading bytes this key shares with the one before it in the block. Zero for the first
/// entry, and zero for a key that is entirely pad bytes however its neighbour looks.
/// </param>
/// <param name="TrailCount">
/// How many trailing pad bytes were dropped from the stored key. What byte they were is not
/// recorded, which is what [c]KeyPadding[/c] is about.
/// </param>
internal readonly record struct PackedEntry(uint Record, int DupCount, int TrailCount);
