using CodeBase.Net.Memo;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// A table's memo entries as the reference implementation read them without decompressing.
///
/// The ordinary dump records every memo decompressed, which is what a caller sees, and so cannot say
/// how an entry was stored. This one can: the generator reads each entry back with decompression off
/// and records its type and the length on disk. Only a table created with compressed memos has one.
///
/// As with every other dump reader here, a section this reader has never heard of is refused rather
/// than skipped.
/// </summary>
internal sealed class CorpusMemoDump
{
    private readonly Dictionary<(int Record, string Field), DumpMemoEntry> entries;

    private CorpusMemoDump(Dictionary<(int Record, string Field), DumpMemoEntry> entries)
    {
        this.entries = entries;
    }

    /// <summary>Gets every entry the dump lists, a record without a memo included.</summary>
    public IReadOnlyCollection<DumpMemoEntry> Entries => entries.Values;

    /// <summary>
    /// Reads a table's memo dump out of the corpus.
    /// </summary>
    /// <param name="tableName">The table, without extension.</param>
    /// <returns>The parsed dump, or an empty one where the table has none.</returns>
    public static CorpusMemoDump Load(string tableName)
    {
        string fileName = tableName + ".memo.dump.txt";
        string path = Path.Combine(Corpus.Root, fileName);
        Dictionary<(int, string), DumpMemoEntry> entries = [];
        if (!File.Exists(path))
            return new CorpusMemoDump(entries);

        string section = string.Empty;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                section = line;
                if (section != "[entries]")
                    throw new InvalidDataException($"{fileName} holds an unknown section {section}.");
                continue;
            }

            if (section.Length == 0)
                continue;

            DumpMemoEntry entry = DumpMemoEntry.Parse(line);
            entries.Add((entry.Record, entry.Field), entry);
        }

        return new CorpusMemoDump(entries);
    }

    /// <summary>
    /// Gets the type a memo was stored as.
    /// </summary>
    /// <param name="record">The record number.</param>
    /// <param name="field">The field name.</param>
    /// <returns>
    /// The stored type; text where the record has no memo in the field, which is what the library
    /// answers for an absent memo, and text for every memo of a table with no dump, which is all the
    /// reference ever writes unless compression was switched on at create.
    /// </returns>
    public MemoType TypeOf(int record, string field) =>
        entries.TryGetValue((record, field), out DumpMemoEntry? entry) && entry.Block > 0
            ? entry.Type
            : MemoType.Text;
}
