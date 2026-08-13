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
    /// <param name="what">What is being read, for the message if it cannot be.</param>
    /// <param name="code">
    /// Which error a short read is. Defaults to a data-file one; an index caller passes its own, so
    /// that a truncated index is classified the way the rest of the index path classifies its
    /// failures rather than as a data error.
    /// </param>
    /// <returns>Exactly the bytes asked for.</returns>
    /// <exception cref="CodeBaseException">
    /// The file gave fewer bytes than were asked for. A short read is never quietly padded: the
    /// bytes that did not arrive would otherwise decode as zeros, and a header of zeros is a
    /// plausible-looking header.
    /// </exception>
    public static byte[] ReadExactly(
        this IRandomAccessSource source,
        long offset,
        int count,
        string what,
        ErrorCode code = ErrorCode.Data)
    {
        byte[] buffer = new byte[count];
        int read = 0;

        while (read < count)
        {
            int step = source.Read(offset + read, buffer.AsSpan(read));
            if (step <= 0)
            {
                throw new CodeBaseException(
                    code,
                    $"Reading the {what} needed {count} bytes at offset {offset} but the file gave " +
                    $"{read}.");
            }

            read += step;
        }

        return buffer;
    }
}
