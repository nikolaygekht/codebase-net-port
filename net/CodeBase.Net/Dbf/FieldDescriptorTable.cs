namespace CodeBase.Net.Dbf;

/// <summary>
/// Reads the run of field descriptors that follows a DBF header.
///
/// The region has no count. Descriptors run one after another until a byte of 0x0D appears where
/// the next one would have begun, and for a Visual FoxPro table 263 reserved bytes follow that
/// terminator, inside the header length but past anything read here.
///
/// Governing specification: DBF-FORMAT.md sections 2.4 and 4.
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
    /// or declares no fields at all. Also when the long-name layout is declared, which this library
    /// does not read.
    /// </exception>
    public static IReadOnlyList<FieldDescriptor> Parse(ReadOnlySpan<byte> region, bool usesLongFieldNames)
    {
        if (usesLongFieldNames)
        {
            // A CodeBase extension that FoxPro itself cannot read, and one no corpus case covers.
            // Decoding it would mean asserting this port's reading of a specification against
            // itself, so it is refused until a generated case exists to gate it.
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                "The table stores long field names, a descriptor layout this library does not read.");
        }

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

        if (descriptors.Count == 0)
            throw new CodeBaseException(ErrorCode.Data, "The table declares no fields.");

        return descriptors;
    }
}
