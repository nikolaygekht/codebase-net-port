using System.Buffers.Binary;
using System.Text;
using CodeBase.Net.Dbf;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// Builds field descriptors and the region that holds them, including malformed ones.
///
/// Hand-built bytes are legitimate test [i]input[/i]. They are never a source of expected values,
/// which come from the corpus. See DEV_APPROACH.md section 4.
/// </summary>
internal static class DescriptorBytes
{
    /// <summary>
    /// Builds one 32-byte descriptor.
    /// </summary>
    public static byte[] Build(
        string name = "FIELD",
        char type = 'C',
        int storedOffset = 1,
        byte length = 10,
        byte decimals = 0,
        FieldFlags flags = FieldFlags.None,
        byte hasTag = 0)
    {
        byte[] bytes = new byte[FieldDescriptor.Size];

        // Truncated rather than rejected: the stored name field is 11 bytes and a corrupt file can
        // fill every one of them.
        byte[] encoded = Encoding.ASCII.GetBytes(name);
        encoded.AsSpan(0, Math.Min(encoded.Length, 11)).CopyTo(bytes);

        bytes[11] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), storedOffset);
        bytes[16] = length;
        bytes[17] = decimals;
        bytes[18] = (byte)flags;
        bytes[31] = hasTag;

        return bytes;
    }

    /// <summary>
    /// Builds a descriptor region: the given descriptors, a terminator, then reserved padding.
    /// </summary>
    /// <param name="reservedBytes">
    /// How many zero bytes follow the terminator. A Visual FoxPro table has 263 of them, counted
    /// inside the header length, and nothing should read into them.
    /// </param>
    /// <param name="descriptors">The descriptors, in order.</param>
    public static byte[] Region(int reservedBytes, params byte[][] descriptors)
    {
        List<byte> region = [];

        foreach (byte[] descriptor in descriptors)
            region.AddRange(descriptor);

        region.Add(FieldDescriptor.Terminator);
        region.AddRange(new byte[reservedBytes]);

        return [.. region];
    }

    /// <summary>
    /// Builds a descriptor region with no reserved padding after the terminator.
    /// </summary>
    /// <param name="descriptors">The descriptors, in order.</param>
    public static byte[] Region(params byte[][] descriptors) => Region(0, descriptors);

    /// <summary>
    /// Builds one descriptor in the long-name layout: the 32-byte one's last 21 bytes, a length, a name.
    /// </summary>
    /// <param name="name">The name, written whole and followed by a zero byte.</param>
    /// <param name="type">The type letter.</param>
    /// <param name="storedOffset">The stored record offset.</param>
    /// <param name="length">The length byte.</param>
    /// <param name="decimals">The decimal-count byte.</param>
    /// <param name="flags">The field flags.</param>
    /// <param name="declaredLength">
    /// The length to store before the name, when it should differ from the name's length plus one.
    /// </param>
    public static byte[] Long(
        string name = "FIELD",
        char type = 'C',
        int storedOffset = 1,
        byte length = 10,
        byte decimals = 0,
        FieldFlags flags = FieldFlags.None,
        short? declaredLength = null)
    {
        byte[] tail = Build("", type, storedOffset, length, decimals, flags)[11..];
        byte[] encoded = Encoding.ASCII.GetBytes(name);
        byte[] nameLength = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(nameLength, declaredLength ?? (short)(encoded.Length + 1));

        return [.. tail, .. nameLength, .. encoded, 0];
    }

    /// <summary>
    /// Builds a long-name descriptor region as the C library writes one, ending in 0x0D and 0x1A.
    /// </summary>
    /// <param name="descriptors">The descriptors, in order.</param>
    public static byte[] LongRegion(params byte[][] descriptors) =>
        [.. descriptors.SelectMany(d => d), FieldDescriptor.Terminator, 0x1A];
}
