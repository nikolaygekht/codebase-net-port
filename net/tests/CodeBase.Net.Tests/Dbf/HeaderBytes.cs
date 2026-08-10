using System.Buffers.Binary;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// Builds the 32 bytes of a DBF header, including combinations no writer would produce.
///
/// Hand-built bytes are legitimate test [i]input[/i]: feeding a parser a deliberately broken header
/// invents nothing. They are never a source of expected values, which come from the corpus. See
/// DEV_APPROACH.md section 4.
/// </summary>
internal static class HeaderBytes
{
    /// <summary>
    /// Builds a header, valid unless a parameter is given a value that makes it otherwise.
    /// </summary>
    public static byte[] Build(
        byte version = 0x30,
        byte year = 26,
        byte month = 1,
        byte day = 1,
        int recordCount = 32,
        int headerLength = 584,
        int recordLength = 82,
        byte[]? flags = null,
        double autoIncrementValue = 0.0,
        byte tableFlags = 0x00,
        byte codePage = 0x00)
    {
        byte[] bytes = new byte[32];

        bytes[0] = version;
        bytes[1] = year;
        bytes[2] = month;
        bytes[3] = day;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), recordCount);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), (ushort)recordLength);
        (flags ?? new byte[8]).CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(20), autoIncrementValue);
        bytes[28] = tableFlags;
        bytes[29] = codePage;

        return bytes;
    }
}
