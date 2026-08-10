namespace CodeBase.Net.IO;

/// <summary>
/// Reads bytes at an absolute position in a file.
///
/// The whole of this library's contact with stored data. Keeping it to two members means a test can
/// stand a byte array in for a file in three lines, and that the awkward cases a real disk produces
/// only rarely, such as a read returning fewer bytes than asked for, can be produced on demand.
/// </summary>
internal interface IRandomAccessSource : IDisposable
{
    /// <summary>
    /// Gets the length of the file in bytes.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Reads into a buffer from a position in the file.
    /// </summary>
    /// <param name="offset">Where to read from, counted from the start of the file.</param>
    /// <param name="buffer">Where to put the bytes. Its length is how many are wanted.</param>
    /// <returns>
    /// How many bytes were read, which may be fewer than were asked for and may be zero at the end
    /// of the file. A caller that needs all of them has to say so.
    /// </returns>
    int Read(long offset, Span<byte> buffer);
}
