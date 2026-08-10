using CodeBase.Net.IO;

namespace CodeBase.Net.TestUtils;

/// <summary>
/// A filesystem of named byte arrays, with the case-sensitivity of a Unix one.
///
/// Case-sensitive on purpose. CodeBase writes a lower-case memo extension beside an upper-case
/// table name, so a resolver that only tries the obvious path works on Windows and fails on Linux.
/// A fake that ignored case would hide exactly the bug it is here to catch.
/// </summary>
internal sealed class FakeFileSystem : IRandomAccessSourceFactory, ICompanionFileResolver
{
    private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new filesystem holding the given files.
    /// </summary>
    /// <param name="files">The files, as paths and their contents.</param>
    public FakeFileSystem(params (string Path, byte[] Bytes)[] files)
    {
        foreach ((string path, byte[] bytes) in files)
            this.files[path] = bytes;
    }

    /// <summary>
    /// Gets the paths that have been opened, in order.
    /// </summary>
    public List<string> Opened { get; } = [];

    /// <summary>
    /// Gets every source handed out, so a test can check they were all closed.
    /// </summary>
    public List<InMemorySource> Sources { get; } = [];

    /// <summary>
    /// Gets the paths a companion has been looked for beside, in order.
    /// </summary>
    public List<string> CompanionLookups { get; } = [];

    /// <inheritdoc/>
    public IRandomAccessSource Open(string path)
    {
        if (!files.TryGetValue(path, out byte[]? bytes))
            throw new FileNotFoundException($"No such file: {path}", path);

        Opened.Add(path);
        InMemorySource source = new(bytes);
        Sources.Add(source);
        return source;
    }

    /// <inheritdoc/>
    public string? Resolve(string tablePath, string extension)
    {
        CompanionLookups.Add(tablePath);

        string exact = Path.ChangeExtension(tablePath, extension);
        if (files.ContainsKey(exact))
            return exact;

        foreach (string candidate in files.Keys)
        {
            if (string.Equals(candidate, exact, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
}
