namespace CodeBase.Net.IO;

/// <summary>
/// Finds the file that accompanies a table, whatever case its extension is written in.
///
/// This exists because CodeBase writes a lower-case memo extension beside an upper-case table name,
/// which no one notices on Windows and everyone notices on a case-sensitive filesystem.
/// </summary>
internal interface ICompanionFileResolver
{
    /// <summary>
    /// Finds a companion file.
    /// </summary>
    /// <param name="tablePath">The table whose companion is wanted.</param>
    /// <param name="extension">The companion's extension, including its leading dot.</param>
    /// <returns>The companion's path, or null when there is no such file.</returns>
    string? Resolve(string tablePath, string extension);
}
