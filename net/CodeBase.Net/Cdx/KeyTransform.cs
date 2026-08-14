using System.Buffers.Binary;

namespace CodeBase.Net.Cdx;

/// <summary>
/// Turns a value into the key bytes an index tag stores for it.
///
/// A stored key is not the value: it is a form of the value that plain unsigned byte comparison
/// sorts correctly. For every numeric type that means big-endian order with the sign bit inverted,
/// so that negatives fall below positives instead of above them, and for floating point it means
/// complementing every byte of a negative so its magnitude counts downwards.
///
/// Every method writes into a destination the caller owns and returns how many bytes it wrote, so a
/// cursor can convert a value into its own buffer without allocating.
///
/// Governing specification: KEY-COLLATION.md section 2.
/// </summary>
internal static class KeyTransform
{
    /// <summary>
    /// Writes the key bytes of a double, which is what numeric, float and date tags store.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="destination">Where to write, at least eight bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>
    /// [c]t4dblToFox[/c] (i4conv.c:2432-2466). Negative zero is the case that makes this fiddly: it
    /// compares as [c]greater than or equal to[/c] zero in the C, so it takes the positive path, and
    /// the byte addition then wraps 0x80 to 0x00. The result sorts below every negative, which is
    /// wrong arithmetically and is what the reference does. Writing this as an or against the sign
    /// bit would give 0x80 and be a different key.
    /// </remarks>
    public static int FromDouble(double value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, BitConverter.DoubleToUInt64Bits(value));

        if (value >= 0)
            destination[0] = unchecked((byte)(destination[0] + 0x80));
        else
            Complement(destination[..8]);

        return 8;
    }

    /// <summary>
    /// Writes the key bytes of a four-byte float, which is what a binary float tag stores.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="destination">Where to write, at least four bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>[c]t4floatToFox[/c] (i4conv.c:2383-2417): the same rule on four bytes.</remarks>
    public static int FromSingle(float value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, BitConverter.SingleToUInt32Bits(value));

        if (value >= 0)
            destination[0] = unchecked((byte)(destination[0] + 0x80));
        else
            Complement(destination[..4]);

        return 4;
    }

    /// <summary>
    /// Writes the key bytes of a 32-bit integer.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="destination">Where to write, at least four bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>
    /// [c]t4intToFox[/c] (i4conv.c:1012-1035). The reference adds 0x80 to the leading byte of a
    /// positive value and subtracts it otherwise; on a two's-complement machine both are the same
    /// as inverting the sign bit, including at zero where the subtraction wraps
    /// (KEY-COLLATION.md section 2.3 works this through).
    /// </remarks>
    public static int FromInt32(int value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)value ^ 0x8000_0000u);
        return 4;
    }

    /// <summary>
    /// Writes the key bytes of a 64-bit integer.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="destination">Where to write, at least eight bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>[c]t4i8ToFox[/c] (i4conv.c:1096-1115): the 32-bit rule on eight bytes.</remarks>
    public static int FromInt64(long value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64BigEndian(destination, (ulong)value ^ 0x8000_0000_0000_0000ul);
        return 8;
    }

    /// <summary>
    /// Writes the key bytes of a 32-bit unsigned integer.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="destination">Where to write, at least four bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>
    /// [c]t4unsignedIntToFox[/c] (i4conv.c:1041-1051): byte order only. An unsigned value has no
    /// sign bit to invert, and inverting one anyway would sort the top half of the range first.
    /// </remarks>
    public static int FromUInt32(uint value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        return 4;
    }

    /// <summary>
    /// Writes the key bytes of a currency value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="destination">Where to write, at least eight bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <exception cref="CodeBaseException">The value does not fit a currency field.</exception>
    /// <remarks>
    /// [c]t4curToFox[/c] (i4conv.c:1360-1391). A currency is a 64-bit integer of ten-thousandths,
    /// so the key is that integer's, and the scaling is where a value can fail to fit.
    /// </remarks>
    public static int FromCurrency(decimal value, Span<byte> destination) =>
        FromInt64(FoxCurrencyScale(value), destination);

    /// <summary>
    /// Writes the key bytes of a date.
    /// </summary>
    /// <param name="value">The date to convert.</param>
    /// <param name="destination">Where to write, at least eight bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>
    /// [c]t4dtstrToFox[/c] (i4conv.c:1124-1130): the Julian day number as a double. A blank date is
    /// day zero, whose key sorts below every real date.
    /// </remarks>
    public static int FromDate(DateOnly value, Span<byte> destination) =>
        FromDouble(Dbf.FoxDate.ToJulian(value.Year, value.Month, value.Day), destination);

    /// <summary>
    /// Writes the key bytes of a datetime.
    /// </summary>
    /// <param name="julianDay">The Julian day number the field stores.</param>
    /// <param name="milliseconds">The milliseconds since midnight the field stores.</param>
    /// <param name="destination">Where to write, at least eight bytes.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>
    /// [c]t4dateTimeToFox[/c] (i4conv.c:2209-2287), and the one transform here that is not
    /// arithmetic. The milliseconds round to the nearest second, the day and the rounded time become
    /// one double, and then a table decides whether that double is nudged down before it is
    /// converted. See [clink=CodeBase.Net.Cdx.DateTimeKeyFlags]DateTimeKeyFlags[/clink] for why the
    /// table exists.
    /// </remarks>
    public static int FromDateTime(int julianDay, int milliseconds, Span<byte> destination)
    {
        int extra = milliseconds % 1000;
        int rounded = milliseconds - extra;

        if (extra >= 500)
            rounded += 1000;

        double value = julianDay + (rounded / 86400000.0);

        if (DateTimeKeyFlags.NeedsDecrement(rounded / 1000))
            value = BitConverter.UInt64BitsToDouble(DecrementLowByte(BitConverter.DoubleToUInt64Bits(value)));

        return FromDouble(value, destination);
    }

    /// <summary>
    /// Writes the key byte of a logical value.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="destination">Where to write, at least one byte.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>
    /// The stored key is the letter, not a flag (E4EXPR.C:512-536), so false sorts before true
    /// because F precedes T.
    /// </remarks>
    public static int FromLogical(bool value, Span<byte> destination)
    {
        destination[0] = value ? (byte)'T' : (byte)'F';
        return 1;
    }

    /// <summary>
    /// Subtracts one from the lowest byte of a double's bit pattern, borrowing at most once.
    /// </summary>
    /// <param name="bits">The double's bit pattern.</param>
    /// <returns>The adjusted pattern.</returns>
    /// <remarks>
    /// Written the way the C library writes it: borrow into the second byte and stop there, so that
    /// if both low bytes are zero the second wraps to 0xFF and nothing carries further
    /// (i4conv.c:2273-2279).
    ///
    /// That truncated borrow can never actually differ from subtracting one from the whole pattern,
    /// and it is worth recording why rather than leaving it to look like a lurking bug. A real
    /// Julian day sits between 2^21 and 2^22, where the double's step is 2 to the -31, so the low
    /// sixteen mantissa bits vanish only when the second of the day is a multiple of 675 — 128 of
    /// the 86400. None of those 128 carries the decrement flag, so the second byte is never reached
    /// with a zero below it. The shape is kept because it is the reference's; the equivalence is
    /// noted because a test cannot demonstrate a difference that does not exist.
    /// </remarks>
    private static ulong DecrementLowByte(ulong bits)
    {
        byte low = (byte)bits;

        if (low != 0)
            return bits - 1;

        byte second = (byte)(bits >> 8);

        return (bits & ~0xFFFFul) | ((ulong)unchecked((byte)(second - 1)) << 8) | 0xFF;
    }

    private static void Complement(Span<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = unchecked((byte)~bytes[i]);
    }

    private static long FoxCurrencyScale(decimal value)
    {
        // Checked before scaling, not after: multiplying by ten thousand overflows the decimal type
        // itself well before the result would overflow the currency, and that throws something this
        // library does not own.
        const decimal Highest = long.MaxValue / 10000m;
        const decimal Lowest = long.MinValue / 10000m;

        if (value < Lowest || value > Highest)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"{value} is outside the range a currency field holds, which is {Lowest} to " +
                $"{Highest}.");
        }

        return (long)decimal.Round(value * 10000m, 0, MidpointRounding.ToZero);
    }
}
