namespace CodeBase.Net.IO;

/// <summary>
/// Opens files on the real filesystem and finds companions beside them.
///
/// The concrete pair of boundaries, constructed once where a table is opened and nowhere else.
/// </summary>
internal sealed class FileSystem : IRandomAccessSourceFactory, ICompanionFileResolver
{
    /// <summary>
    /// Gets the single instance, there being no state to keep.
    /// </summary>
    public static FileSystem Instance { get; } = new();

    /// <inheritdoc/>
    public IRandomAccessSource Open(string path) => new FileRandomAccessSource(path);

    /// <summary>
    /// Finds a companion file, preferring an exact match and falling back to one differing in case.
    /// </summary>
    /// <param name="tablePath">The table whose companion is wanted.</param>
    /// <param name="extension">The companion's extension, including its leading dot.</param>
    /// <returns>The companion's path, or null when there is no such file.</returns>
    public string? Resolve(string tablePath, string extension)
    {
        string exact = Path.ChangeExtension(tablePath, extension);
        if (File.Exists(exact))
            return exact;

        // Only now the directory scan, and only because CodeBase writes a lower-case extension
        // beside an upper-case name. On a case-sensitive filesystem the obvious path misses.
        string? directory = Path.GetDirectoryName(exact);
        string wanted = Path.GetFileName(exact);

        foreach (string candidate in Directory.EnumerateFiles(
                     string.IsNullOrEmpty(directory) ? "." : directory))
        {
            if (string.Equals(Path.GetFileName(candidate), wanted, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
}
