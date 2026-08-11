using System.Buffers.Binary;

namespace CodeBase.Net.Memo;

/// <summary>
/// The eight bytes that begin a memo entry: what it holds and how much of it there is.
///
/// Both numbers are big-endian, like the file header beside them and unlike everything in the table
/// itself. Endianness in these formats belongs to the field rather than to the file.
///
/// The length counts the payload only. The structure comment in the C library says it includes the
/// header, and for this format that comment is wrong: the writer stores the caller's byte count and
/// the block arithmetic adds the header size separately. Verified against every non-empty entry in
/// the corpus before it was relied on.
///
/// Governing specification: FPT-MEMO.md section 3.2.
/// </summary>
internal readonly struct MemoBlockHeader
{
    /// <summary>
    /// The number of bytes a memo entry header occupies.
    /// </summary>
    public const int Size = 8;

    private MemoBlockHeader(MemoType type, uint length)
    {
        Type = type;
        Length = length;
    }

    /// <summary>
    /// Gets what the entry declares itself to hold.
    /// </summary>
    /// <value>
    /// Reported as stored. A value outside the four the format defines is preserved rather than
    /// rejected, because the reference implementation echoes the type back without validating it.
    /// </value>
    public MemoType Type { get; }

    /// <summary>
    /// Gets the number of payload bytes that follow the header.
    /// </summary>
    /// <value>The payload only. The eight header bytes are not counted in it.</value>
    public uint Length { get; }

    /// <summary>
    /// Reads a memo entry header.
    /// </summary>
    /// <param name="bytes">The first eight bytes of the entry.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="CodeBaseException">Fewer than eight bytes were supplied.</exception>
    public static MemoBlockHeader Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"A memo entry header is {Size} bytes; only {bytes.Length} were available.");
        }

        return new MemoBlockHeader(
            (MemoType)BinaryPrimitives.ReadUInt32BigEndian(bytes),
            BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]));
    }
}
