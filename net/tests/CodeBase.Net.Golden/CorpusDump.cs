using System.Globalization;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// The expected values for one corpus table, read from the dump the C library produced.
///
/// This is where every golden expectation comes from. Nothing in a golden test is written by hand,
/// so a disagreement is always this port against the reference implementation, never against
/// somebody's reading of a specification. See DEV_APPROACH.md section 4.
///
/// Every section is either read or deliberately declared unread. A section this reader has never
/// heard of is refused, not skipped: the DBF format itself is frozen, but the dump format is ours
/// and will grow an index half, and a section that arrives unnoticed is a silent hole in the gate.
/// Skipping one would let a golden test compare an empty list and report success, which is the one
/// failure a golden suite must never have.
///
/// Tokens that appear only when true, such as the nullability marks, are read as optional. See
/// ADR-16.
/// </summary>
internal sealed class CorpusDump
{
    private CorpusDump(
        string fileName,
        byte version,
        (int Year, int Month, int Day) lastUpdate,
        int recordCount,
        int headerLength,
        int recordLength,
        byte tableFlags,
        byte codePage,
        IReadOnlyList<DumpDescriptor> descriptors,
        IReadOnlyList<DumpField> fields)
    {
        FileName = fileName;
        Version = version;
        LastUpdate = lastUpdate;
        RecordCount = recordCount;
        HeaderLength = headerLength;
        RecordLength = recordLength;
        TableFlags = tableFlags;
        CodePage = codePage;
        Descriptors = descriptors;
        Fields = fields;
    }

    /// <summary>Gets the table's file name, as the dump records it.</summary>
    public string FileName { get; }

    /// <summary>Gets the version byte.</summary>
    public byte Version { get; }

    /// <summary>Gets the frozen last-update stamp, as the three stored numbers.</summary>
    public (int Year, int Month, int Day) LastUpdate { get; }

    /// <summary>Gets the record count from the header.</summary>
    public int RecordCount { get; }

    /// <summary>Gets the header length.</summary>
    public int HeaderLength { get; }

    /// <summary>Gets the record length.</summary>
    public int RecordLength { get; }

    /// <summary>Gets the byte recording which companion files exist.</summary>
    public byte TableFlags { get; }

    /// <summary>Gets the language-driver byte.</summary>
    public byte CodePage { get; }

    /// <summary>Gets the field descriptors as stored on disk, including the null-flags field.</summary>
    public IReadOnlyList<DumpDescriptor> Descriptors { get; }

    /// <summary>Gets the fields as the C library reports them, which excludes the null-flags field.</summary>
    public IReadOnlyList<DumpField> Fields { get; }

    /// <summary>
    /// The section holding the field descriptors as they are stored on disk.
    /// </summary>
    private const string DescriptorsSection = "[descriptors]";

    /// <summary>
    /// The section holding the fields as the C library reports them once a table is open.
    /// </summary>
    private const string FieldsSection = "[fields]";

    /// <summary>
    /// Sections that exist and are deliberately not read here.
    /// </summary>
    /// <value>
    /// Record values belong to the step that decodes them. Naming the section rather than ignoring
    /// unknown ones is what keeps the refusal below meaningful.
    /// </value>
    private static readonly string[] DeferredSections = ["[records]"];

    /// <summary>
    /// Header keys every dump carries, each of which a golden test compares against.
    /// </summary>
    private static readonly string[] RequiredHeaderKeys =
        ["file", "version", "lastUpdate", "numRecs", "headerLen", "recordLen", "hasMdxMemo", "codePage"];

    /// <summary>
    /// Reads the dump that accompanies a corpus table.
    /// </summary>
    /// <param name="tableName">Table base name, for example [c]VFPNULL[/c].</param>
    /// <returns>The expected values for that table.</returns>
    public static CorpusDump Load(string tableName) => Parse(Corpus.ReadDump(tableName), tableName);

    /// <summary>
    /// Reads dump text.
    /// </summary>
    /// <param name="text">The whole dump.</param>
    /// <param name="origin">What to name in a failure message, usually the table.</param>
    /// <returns>The expected values the text records.</returns>
    /// <exception cref="InvalidDataException">
    /// The text carries a section this reader does not know, or omits one it needs. Both mean the
    /// expectations a golden test would compare against are not the ones it thinks.
    /// </exception>
    public static CorpusDump Parse(string text, string origin)
    {
        Dictionary<string, string> header = [];
        List<DumpDescriptor> descriptors = [];
        List<DumpField> fields = [];
        HashSet<string> seenSections = [];
        string section = string.Empty;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                section = line[..(line.IndexOf(']') + 1)];
                seenSections.Add(section);

                if (section is not (DescriptorsSection or FieldsSection) &&
                    !DeferredSections.Contains(section))
                {
                    throw new InvalidDataException(
                        $"The dump for {origin} has a section '{section}' this reader does not " +
                        "know. The corpus format has changed, so teach CorpusDump about it: a " +
                        "section that is skipped leaves a golden test comparing nothing and " +
                        "passing.");
                }

                continue;
            }

            switch (section)
            {
                case "":
                    string[] parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
                    header[parts[0]] = parts[1];
                    break;

                case DescriptorsSection:
                    descriptors.Add(DumpDescriptor.Parse(line));
                    break;

                case FieldsSection:
                    // The section opens with a count line, which the field list makes redundant.
                    if (!line.StartsWith("recCount", StringComparison.Ordinal))
                        fields.Add(DumpField.Parse(line));
                    break;

                default:
                    break;   // a declared section that this reader does not read
            }
        }

        Require(header, descriptors, fields, seenSections, origin);

        string[] stamp = header["lastUpdate"].Split(' ')[0].Split('-');

        return new CorpusDump(
            header["file"],
            ParseByte(header["version"]),
            (int.Parse(stamp[0], CultureInfo.InvariantCulture),
             int.Parse(stamp[1], CultureInfo.InvariantCulture),
             int.Parse(stamp[2], CultureInfo.InvariantCulture)),
            int.Parse(header["numRecs"], CultureInfo.InvariantCulture),
            int.Parse(header["headerLen"], CultureInfo.InvariantCulture),
            int.Parse(header["recordLen"], CultureInfo.InvariantCulture),
            ParseByte(header["hasMdxMemo"]),
            ParseByte(header["codePage"]),
            descriptors,
            fields);
    }

    /// <summary>
    /// Refuses a dump that would yield expectations quietly missing something.
    /// </summary>
    private static void Require(
        Dictionary<string, string> header,
        List<DumpDescriptor> descriptors,
        List<DumpField> fields,
        HashSet<string> seenSections,
        string origin)
    {
        string[] missingKeys = [.. RequiredHeaderKeys.Where(k => !header.ContainsKey(k))];
        if (missingKeys.Length > 0)
        {
            throw new InvalidDataException(
                $"The dump for {origin} has no {string.Join(", ", missingKeys)} in its header.");
        }

        foreach (string required in new[] { DescriptorsSection, FieldsSection })
        {
            if (!seenSections.Contains(required))
                throw new InvalidDataException($"The dump for {origin} has no {required} section.");
        }

        // A section present but empty is the same silent hole as a section skipped.
        if (descriptors.Count == 0)
            throw new InvalidDataException($"The dump for {origin} lists no field descriptors.");

        if (fields.Count == 0)
            throw new InvalidDataException($"The dump for {origin} lists no fields.");
    }

    /// <summary>
    /// Reads a byte written as 0x-prefixed hexadecimal.
    /// </summary>
    internal static byte ParseByte(string text) =>
        byte.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
