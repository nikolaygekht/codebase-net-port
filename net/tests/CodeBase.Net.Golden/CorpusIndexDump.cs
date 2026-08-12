using System.Globalization;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// The expected values for one corpus index file, read from the dump the C library produced.
///
/// The dump is a sibling of the table's own dump rather than more sections inside it (ADR-24), and
/// every value in it came from the reference implementation's own structures: keys and record numbers
/// from its navigation, block structure from its live block objects, and each entry's compression
/// counts from its own extraction macros. Nothing in it was produced by re-reading the bytes, which
/// is the point — a bit-packed leaf is the one thing a generator must not interpret itself.
///
/// As with the table dump, a section this reader has never heard of is refused rather than skipped.
/// The dump format is ours and will grow a seek half; a section that arrives unnoticed would let a
/// golden test compare an empty list and report success.
/// </summary>
internal sealed class CorpusIndexDump
{
    private CorpusIndexDump(
        string fileName,
        string tableName,
        string shape,
        int blockSize,
        int multiplier,
        uint codeBaseNote,
        string check,
        IReadOnlyList<DumpIndexTag> tags)
    {
        FileName = fileName;
        TableName = tableName;
        Shape = shape;
        BlockSize = blockSize;
        Multiplier = multiplier;
        CodeBaseNote = codeBaseNote;
        Check = check;
        Tags = tags;
    }

    /// <summary>Gets the index file's name, as the dump records it.</summary>
    public string FileName { get; }

    /// <summary>Gets the name of the table the index belongs to.</summary>
    public string TableName { get; }

    /// <summary>Gets the file's shape, either compound or single-tag.</summary>
    public string Shape { get; }

    /// <summary>Gets a value indicating whether the file holds a tag directory.</summary>
    public bool IsCompound => Shape == "compound";

    /// <summary>Gets the number of bytes in a tree block.</summary>
    public int BlockSize { get; }

    /// <summary>Gets what a node number is multiplied by to reach its offset.</summary>
    public int Multiplier { get; }

    /// <summary>Gets the marker that says whether the block geometry was written to the header.</summary>
    public uint CodeBaseNote { get; }

    /// <summary>Gets what the reference implementation's own consistency check said.</summary>
    /// <value>
    /// [c]ok[/c], or [c]skipped-single-tag[/c] for a single-tag file — which d4check cannot check at
    /// all, because it flags the tag directory's header blocks and then flags every tag's header, and
    /// in such a file those are the same block (i4check.c:889-914).
    /// </value>
    public string Check { get; }

    /// <summary>Gets the tags, in the order the dump lists them.</summary>
    /// <value>
    /// A compound file's tag directory is first, under the name [c]*directory*[/c], because it is a
    /// tag like any other and the port has to read it as one.
    /// </value>
    public IReadOnlyList<DumpIndexTag> Tags { get; }

    /// <summary>Gets the tags other than the tag directory.</summary>
    public IEnumerable<DumpIndexTag> RealTags => Tags.Where(t => t.Name != "*directory*");

    /// <summary>
    /// Reads the dump beside a corpus index file.
    /// </summary>
    /// <param name="indexFile">The index file's name, for example [c]CDXBASE.cdx[/c].</param>
    /// <returns>The expected values.</returns>
    public static CorpusIndexDump Load(string indexFile)
    {
        string stem = Path.GetFileNameWithoutExtension(indexFile);
        string extension = Path.GetExtension(indexFile).TrimStart('.').ToLowerInvariant();
        string name = $"{stem}.{extension}.dump.txt";

        return Parse(File.ReadAllText(Corpus.PathOf(name)), name);
    }

    /// <summary>
    /// Reads an index dump.
    /// </summary>
    /// <param name="text">The whole dump.</param>
    /// <param name="what">What is being read, for the message if it cannot be.</param>
    /// <returns>The expected values.</returns>
    /// <exception cref="InvalidDataException">
    /// A value is missing, a section is unknown, or a count does not match what follows it.
    /// </exception>
    public static CorpusIndexDump Parse(string text, string what)
    {
        string[] lines = text.Split('\n');
        Dictionary<string, string> file = [];
        List<DumpIndexTag> tags = [];

        int i = 0;
        for (; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith('['))
                break;

            int space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space < 0)
                throw new InvalidDataException($"Line '{line}' of {what} is not a name and a value.");

            file[line[..space]] = line[space..].Trim();
        }

        for (; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (!line.StartsWith("[tag ", StringComparison.Ordinal))
                throw new InvalidDataException($"Unknown section '{line}' in {what}.");

            tags.Add(DumpIndexTag.Parse(lines, ref i, what));
        }

        return new CorpusIndexDump(
            Required(file, "file", what),
            Required(file, "table", what),
            Required(file, "shape", what),
            int.Parse(Required(file, "blockSize", what), CultureInfo.InvariantCulture),
            int.Parse(Required(file, "multiplier", what), CultureInfo.InvariantCulture),
            ParseHex(Required(file, "codeBaseNote", what)),
            Required(file, "check", what),
            tags);
    }

    internal static string Required(Dictionary<string, string> values, string name, string what) =>
        values.TryGetValue(name, out string? value)
            ? value
            : throw new InvalidDataException($"{what} has no '{name}' value.");

    internal static uint ParseHex(string value) =>
        uint.Parse(
            value.StartsWith("0x", StringComparison.Ordinal) ? value[2..] : value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
}
