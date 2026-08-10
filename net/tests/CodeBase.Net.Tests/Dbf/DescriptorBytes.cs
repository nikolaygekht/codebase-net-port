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
}
