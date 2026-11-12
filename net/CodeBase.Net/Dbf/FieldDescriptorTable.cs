using System.Buffers.Binary;

namespace CodeBase.Net.Dbf;

/// <summary>
/// Reads the run of field descriptors that follows a DBF header.
///
/// The region has no count. Descriptors run one after another until a byte of 0x0D appears where
/// the next one would have begun, and for a Visual FoxPro table 263 reserved bytes follow that
/// terminator, inside the header length but past anything read here.
///
/// A table in CodeBase's long-name layout stores each descriptor as its last 21 bytes, a two-byte
/// length and a name of that length, so the run is walked descriptor by descriptor rather than in
/// 32-byte steps. FoxPro cannot read that layout; the C library can.
///
/// Governing specification: DBF-FORMAT.md sections 2.4, 4 and 4.2.
/// </summary>
internal static class FieldDescriptorTable
{
    /// <summary>
    /// Reads every descriptor in the region.
    /// </summary>
    /// <param name="region">
    /// The bytes between the end of the 32-byte header and the end of the header as its length
    /// declares it.
    /// </param>
    /// <param name="usesLongFieldNames">
    /// Whether the header declares the variable-length descriptor layout that carries names longer
    /// than ten characters.
    /// </param>
    /// <returns>The descriptors, in the order the file stores them.</returns>
    /// <exception cref="CodeBaseException">
    /// The region ends without a terminator, holds a descriptor cut short by the end of the region,
    /// holds a long-layout name whose length is below one, or declares no fields at all.
    /// </exception>
    public static IReadOnlyList<FieldDescriptor> Parse(ReadOnlySpan<byte> region, bool usesLongFieldNames)
    {
        List<FieldDescriptor> descriptors = usesLongFieldNames ? ParseLong(region) : ParseShort(region);

        if (descriptors.Count == 0)
            throw new CodeBaseException(ErrorCode.Data, "The table declares no fields.");

        return descriptors;
    }

    /// <summary>
    /// Reads the run of fixed 32-byte descriptors.
    /// </summary>
    private static List<FieldDescriptor> ParseShort(ReadOnlySpan<byte> region)
    {
        List<FieldDescriptor> descriptors = [];

        for (int offset = 0; ; offset += FieldDescriptor.Size)
        {
            if (offset >= region.Length)
            {
                throw new CodeBaseException(
                    ErrorCode.Data,
                    $"The field descriptors run to the end of the header without a " +
                    $"0x{FieldDescriptor.Terminator:X2} terminator.");
            }

            if (region[offset] == FieldDescriptor.Terminator)
                break;

            if (offset + FieldDescriptor.Size > region.Length)
            {
                throw new CodeBaseException(
                    ErrorCode.Data,
                    $"Field descriptor {descriptors.Count + 1} is cut short by the end of the header.");
            }

            descriptors.Add(FieldDescriptor.Parse(region.Slice(offset, FieldDescriptor.Size)));
        }

        return descriptors;
    }

    /// <summary>
    /// Reads the run of long-layout descriptors, each a 21-byte tail, a length and a name.
    /// </summary>
    private static List<FieldDescriptor> ParseLong(ReadOnlySpan<byte> region)
    {
        List<FieldDescriptor> descriptors = [];
        int offset = 0;

        // The C library stops at a terminator or at the region's last two bytes, where its writer
        // puts 0x0D 0x1A (D4OPEN.C:2709-2731). The second test lets a run whose terminator was lost
        // there still end, as the reference lets it.
        while (offset != region.Length - 2)
        {
            if (offset >= region.Length)
            {
                throw new CodeBaseException(
                    ErrorCode.Data,
                    $"The field descriptors run to the end of the header without a " +
                    $"0x{FieldDescriptor.Terminator:X2} terminator.");
            }

            if (region[offset] == FieldDescriptor.Terminator)
                break;

            int nameStart = offset + FieldDescriptor.TailSize + sizeof(short);
            if (nameStart > region.Length)
            {
                throw new CodeBaseException(
                    ErrorCode.Data,
                    $"Field descriptor {descriptors.Count + 1} is cut short by the end of the header.");
            }

            // The stored length counts the name's closing zero byte (D4CREATE.C:1641).
            int nameLength = BinaryPrimitives.ReadInt16LittleEndian(region[(nameStart - sizeof(short))..]);
            if (nameLength < 1 || nameStart + nameLength > region.Length)
            {
                throw new CodeBaseException(
                    ErrorCode.Data,
                    $"Field descriptor {descriptors.Count + 1} declares a name of {nameLength} bytes, " +
                    "which does not fit the header.");
            }

            descriptors.Add(FieldDescriptor.FromParts(
                region.Slice(nameStart, nameLength),
                region.Slice(offset, FieldDescriptor.TailSize)));

            offset = nameStart + nameLength;
        }

        return descriptors;
    }
}
