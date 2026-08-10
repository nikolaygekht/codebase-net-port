using System.Reflection;

namespace CodeBase.Net.TestUtils;

/// <summary>
/// Locates the checked-in corpus and reads files out of it.
///
/// The single place any test learns where the corpus is, so that no test hard-codes a path. See
/// DEV_APPROACH.md section 4. The search starts at the directory holding this assembly and walks
/// up, so it does not care where the build put its output or how deep the repository sits.
/// </summary>
public static class Corpus
{
    private const string DirectoryName = "corpus";

    /// <summary>
    /// Directories the upward search will examine but never rise above.
    ///
    /// Without a stop, a missing corpus turns into a scan of every ancestor up to the filesystem
    /// root, on a machine whose home and mount points may be large and slow. The search also stops
    /// at any root, which covers both the Unix root and a Windows drive.
    /// </summary>
    private static readonly string[] SearchBoundaries = ["/home", "/mnt"];

    private static readonly Lazy<string> LazyRoot = new(Locate);

    private static readonly Lazy<IReadOnlyList<string>> LazyTableNames = new(
        () => TableNamesIn(Root));

    /// <summary>
    /// Absolute path of the corpus directory.
    /// </summary>
    public static string Root => LazyRoot.Value;

    /// <summary>
    /// The base names of every table in the corpus, discovered from the directory, sorted.
    ///
    /// Discovered rather than listed, so that adding a generator case makes the golden suite cover
    /// it without an edit. What stops a broken discovery from passing as success is a separate test
    /// asserting this set against the cases the corpus is documented to hold.
    /// </summary>
    public static IReadOnlyList<string> TableNames => LazyTableNames.Value;

    /// <summary>
    /// Full path of a corpus file, whose existence is checked here rather than at first read.
    /// </summary>
    /// <param name="fileName">File name including extension, for example [c]VFPNULL.DBF[/c].</param>
    public static string PathOf(string fileName)
    {
        string path = Path.Combine(Root, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"'{fileName}' is not in the corpus at '{Root}'. Regenerate with test-files-generator.",
                path);
        }

        return path;
    }

    /// <summary>
    /// Reads a corpus file whole.
    /// </summary>
    /// <param name="fileName">File name including extension, for example [c]VFPNULL.DBF[/c].</param>
    public static byte[] ReadAllBytes(string fileName) => File.ReadAllBytes(PathOf(fileName));

    /// <summary>
    /// Reads a corpus dump whole, as text.
    /// </summary>
    /// <param name="tableName">Table base name, for example [c]VFPNULL[/c].</param>
    public static string ReadDump(string tableName) => File.ReadAllText(PathOf(tableName + ".dump.txt"));

    private static string Locate()
    {
        List<string> examined = [];

        for (DirectoryInfo? dir = new(StartDirectory()); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, DirectoryName);
            examined.Add(candidate);

            // A directory named "corpus" is only *the* corpus if it holds tables. An empty one
            // higher up the tree would otherwise shadow the real one and produce an empty suite.
            if (Directory.Exists(candidate) && TableNamesIn(candidate).Count > 0)
                return candidate;

            if (IsSearchBoundary(dir))
                break;
        }

        throw new DirectoryNotFoundException(
            $"No '{DirectoryName}' directory containing .DBF files was found above " +
            $"'{StartDirectory()}'. The corpus is checked in, so a missing one usually means the " +
            $"tests are running outside the repository. Looked in:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", examined));
    }

    /// <summary>
    /// The directory holding this assembly — the one place guaranteed to sit inside the repository
    /// tree, whatever the working directory or output path happens to be.
    /// </summary>
    private static string StartDirectory()
    {
        string location = Assembly.GetExecutingAssembly().Location;

        // Empty for single-file or in-memory assemblies, where the base directory is the best we have.
        return string.IsNullOrEmpty(location)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(location) ?? AppContext.BaseDirectory;
    }

    private static bool IsSearchBoundary(DirectoryInfo dir)
    {
        if (dir.Parent is null)
            return true;

        string path = dir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return SearchBoundaries.Any(b => string.Equals(path, b, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> TableNamesIn(string directory) =>
        Directory.EnumerateFiles(directory)
                 .Where(p => string.Equals(Path.GetExtension(p), ".dbf", StringComparison.OrdinalIgnoreCase))
                 .Select(Path.GetFileNameWithoutExtension)
                 .Where(n => !string.IsNullOrEmpty(n))
                 .Select(n => n!)
                 .OrderBy(n => n, StringComparer.Ordinal)
                 .ToArray();
}
