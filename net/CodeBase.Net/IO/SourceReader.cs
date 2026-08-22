namespace CodeBase.Net.IO;

/// <summary>
/// Reads from a source, insisting on getting everything asked for.
/// </summary>
internal static class SourceReader
{
    /// <summary>
    /// Reads a fixed number of bytes, treating anything less as a corrupt file.
    /// </summary>
    /// <param name="source">Where to read from.</param>
    /// <param name="offset">Where to read from, counted from the start of the file.</param>
    /// <param name="count">How many bytes are needed.</param>
    /// <returns>Exactly the bytes asked for.</returns>
    /// <exception cref="ShortReadException">
    /// The file gave fewer bytes than were asked for. A short read is never quietly padded: the
    /// bytes that did not arrive would otherwise decode as zeros, and a header of zeros is a
    /// plausible-looking header.
    /// </exception>
    public static byte[] ReadExactly(this IRandomAccessSource source, long offset, int count)
    {
        byte[] buffer = new byte[count];

        source.ReadExactly(offset, buffer);

        return buffer;
    }

    /// <summary>
    /// Reads exactly as many bytes as a buffer has room for, into that buffer.
    /// </summary>
    /// <param name="source">The file to read.</param>
    /// <param name="offset">Where to read from.</param>
    /// <param name="buffer">Where to put them; its length is how many are wanted.</param>
    /// <exception cref="ShortReadException">
    /// The file gave fewer bytes than were asked for. A short read is never quietly padded, for the
    /// same reason as the array-returning form above.
    /// </exception>
    /// <remarks>
    /// The form that allocates nothing, and the one to prefer: a caller reading a fixed small header
    /// can hand it a [c]stackalloc[/c] span, and a caller reading a block can hand it a buffer it
    /// keeps or rents. The array-returning overload is this one plus an allocation.
    ///
    /// **Neither form takes a description of what is being read, deliberately.** The caller catches
    /// [c]ShortReadException[/c] and says what it was reading there, so nothing is formatted on the
    /// path that succeeds.
    /// </remarks>
    public static void ReadExactly(this IRandomAccessSource source, long offset, Span<byte> buffer)
    {
        int read = 0;

        while (read < buffer.Length)
        {
            int step = source.Read(offset + read, buffer[read..]);
            if (step <= 0)
                throw new ShortReadException(offset, buffer.Length, read);

            read += step;
        }
    }
}
