namespace CodeBase.Net.IO;

/// <summary>
/// Opens a named file for reading.
/// </summary>
internal interface IRandomAccessSourceFactory
{
    /// <summary>
    /// Opens a file.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <returns>A source reading that file, which the caller owns and must dispose.</returns>
    IRandomAccessSource Open(string path);
}
