using System.Globalization;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// The reference implementation's own function table, read back out of the dump the generator took.
///
/// This is the other half of the transcription gate. The port's table was typed out of the C source
/// text; this one was walked out of the compiled library's [c]v4functions[/c] array. They are two
/// readings of the same thing by two different routes, so a slip in either shows up as a disagreement
/// rather than as a shared assumption.
///
/// As with every other dump reader here, a section this reader has never heard of is refused rather
/// than skipped, so that a dump that grows a section does not quietly compare an empty list.
/// </summary>
internal sealed class CorpusFunctionsDump
{
    private const string FileName = "FUNCTIONS.dump.txt";

    private CorpusFunctionsDump(int count, IReadOnlyList<DumpFunctionRow> rows)
    {
        Count = count;
        Rows = rows;
    }

    /// <summary>Gets the row count the dump's header declares.</summary>
    public int Count { get; }

    /// <summary>Gets the rows, in the order the reference table holds them.</summary>
    public IReadOnlyList<DumpFunctionRow> Rows { get; }

    /// <summary>
    /// Reads the dump out of the corpus.
    /// </summary>
    /// <returns>The parsed dump.</returns>
    public static CorpusFunctionsDump Load()
    {
        int count = -1;
        List<DumpFunctionRow> rows = [];
        string section = string.Empty;

        foreach (string raw in File.ReadLines(Path.Combine(Corpus.Root, FileName)))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                section = line;
                if (section != "[functions]")
                    throw new InvalidDataException($"{FileName} holds an unknown section {section}.");
                continue;
            }

            if (section.Length == 0)
            {
                if (line.StartsWith("count", StringComparison.Ordinal))
                    count = int.Parse(line["count".Length..].Trim(), CultureInfo.InvariantCulture);
                continue;
            }

            rows.Add(DumpFunctionRow.Parse(line));
        }

        if (count < 0)
            throw new InvalidDataException($"{FileName} declares no row count.");

        return new CorpusFunctionsDump(count, rows);
    }
}
