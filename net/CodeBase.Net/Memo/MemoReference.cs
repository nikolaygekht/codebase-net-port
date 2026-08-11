using System.Buffers.Binary;

namespace CodeBase.Net.Memo;

/// <summary>
/// The block number a memo field holds in the record, in either of the two encodings.
///
/// Which encoding is in use is decided by the field's declared width and by nothing else. Four bytes
/// means a little-endian signed integer; any other width means the number written as right-aligned
/// ASCII digits. That is the test the C library makes — [c]f4len(field) == 4[/c] chooses the binary
/// path and everything else falls through to the text conversion (F4LONG.C:365-368, 343) — so a
/// Visual FoxPro table created at an older compatibility level still reads correctly.
///
/// A number of zero or less means the record has no memo. That is not an error and not a distinct
/// state from an empty one: the format cannot tell them apart.
///
/// Governing specification: FPT-MEMO.md section 3.4.
/// </summary>
internal static class MemoReference
{
    /// <summary>
    /// The field width at which a reference is stored as a binary integer rather than as digits.
    /// </summary>
    public const int BinaryWidth = 4;

    /// <summary>
    /// The block number meaning that the record holds no memo.
    /// </summary>
    public const int None = 0;

    /// <summary>
    /// Reads the block number a memo field refers to.
    /// </summary>
    /// <param name="bytes">The field's bytes as they sit in the record.</param>
    /// <returns>
    /// The block number, or zero where the field is blank or holds nothing that is a number. Zero
    /// and negative both mean no memo, and negative is normalized to zero so that callers have one
    /// thing to test.
    /// </returns>
    public static int Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return None;

        int block = bytes.Length == BinaryWidth
            ? BinaryPrimitives.ReadInt32LittleEndian(bytes)
            : ReadDigits(bytes);

        return block > None ? block : None;
    }

    /// <summary>
    /// Reads a right-aligned ASCII number, the way the reference implementation's own conversion does.
    /// </summary>
    /// <param name="bytes">The field's bytes.</param>
    /// <returns>The number, or zero where the field holds nothing that is one.</returns>
    private static int ReadDigits(ReadOnlySpan<byte> bytes)
    {
        int i = 0;
        while (i < bytes.Length && bytes[i] == (byte)' ')
            i++;

        bool negative = i < bytes.Length && (bytes[i] == (byte)'-' || bytes[i] == (byte)'+');
        if (negative)
            negative = bytes[i++] == (byte)'-';

        long value = 0;
        int digits = 0;

        for (; i < bytes.Length && bytes[i] >= (byte)'0' && bytes[i] <= (byte)'9'; i++)
        {
            value = (value * 10) + (bytes[i] - (byte)'0');
            digits++;

            // A reference that cannot be a block number is no reference. Stopping here rather than
            // wrapping keeps a corrupt field from addressing a real block.
            if (value > int.MaxValue)
                return None;
        }

        // Blank is the ordinary "no memo" for this encoding, and so is anything else that holds no
        // digits at all. The conversion the C library uses for this is not in the source drop, so
        // this is the reading its call sites imply rather than a port of it.
        if (digits == 0)
            return None;

        return negative ? None : (int)value;
    }
}
