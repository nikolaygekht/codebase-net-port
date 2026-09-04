using System.Text;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// The query-optimizer oracle, read back out of the dump the generator took.
///
/// Every filter in it was set on a real relation over a real table and the reference's own optimizer
/// was asked what it made of it: which tag it matched to each node, whether it considered the bitmap
/// to answer the whole filter, the candidate bitset it built before any residual filtering, and the
/// records it finally returned. Then the same filter was run again with the optimizer disabled, so
/// the dump also carries what the filter means to the reference with no bitmap at all.
///
/// That second reading is what makes the first usable. The reference has a defect that can invert a
/// range bound on a reflected comparison nested inside a connective (m4map.c:216), and the port
/// implements the intent instead -- so on such a filter a correct port is expected to disagree with
/// the reference's optimized answer. Rather than carry a hand-written list of exceptions, which
/// could silently widen, each case says whether the reference's own two readings agreed, and the
/// expectation follows from that.
///
/// An unknown section is refused rather than skipped, so that a dump which grows one cannot leave a
/// golden test quietly comparing an empty list.
/// </summary>
internal sealed class CorpusQueryDump
{
    private CorpusQueryDump(string table, int recordCount, IReadOnlyList<DumpQueryCase> cases)
    {
        Table = table;
        RecordCount = recordCount;
        Cases = cases;
    }

    /// <summary>Gets the table the filters were run against.</summary>
    public string Table { get; }

    /// <summary>Gets how many records that table holds.</summary>
    public int RecordCount { get; }

    /// <summary>Gets the filters, in dump order.</summary>
    public IReadOnlyList<DumpQueryCase> Cases { get; }

    /// <summary>
    /// Reads one table's query dump out of the corpus.
    /// </summary>
    /// <param name="tableName">The table's name, without extension.</param>
    /// <returns>The parsed dump.</returns>
    public static CorpusQueryDump Load(string tableName)
    {
        string fileName = tableName + ".query.dump.txt";
        List<DumpQueryCase> cases = [];
        string table = string.Empty;
        int recordCount = 0;
        int declared = -1;
        bool inCases = false;
        bool ended = false;

        Builder? open = null;

        void Close()
        {
            if (open is not null)
                cases.Add(open.Build());
            open = null;
        }

        foreach (string raw in File.ReadLines(Corpus.PathOf(fileName)))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith('['))
            {
                Close();
                switch (line)
                {
                    case "[query cases]": inCases = true; break;
                    case "[end]": inCases = false; ended = true; break;
                    default:
                        throw new InvalidDataException($"{fileName} holds an unknown section {line}.");
                }

                continue;
            }

            if (!inCases)
            {
                if (line.StartsWith("table ", StringComparison.Ordinal))
                    table = Word(line, "table ");
                else if (line.StartsWith("numRecs ", StringComparison.Ordinal))
                    recordCount = Number(line, "numRecs ");

                continue;
            }

            if (line.StartsWith("count ", StringComparison.Ordinal) && open is null)
            {
                declared = Number(line, "count ");
                continue;
            }

            if (line.StartsWith("query ", StringComparison.Ordinal))
            {
                Close();
                open = Builder.Start(line);
                continue;
            }

            open?.Read(line);
        }

        Close();

        if (!ended)
            throw new InvalidDataException($"{fileName} has no [end] marker; it may be truncated.");
        if (declared != cases.Count)
        {
            throw new InvalidDataException(
                $"{fileName} declares {declared} cases and holds {cases.Count}.");
        }

        return new CorpusQueryDump(table, recordCount, cases);
    }

    private static string Word(string line, string key)
    {
        int at = line.IndexOf(key, StringComparison.Ordinal) + key.Length;
        while (at < line.Length && line[at] == ' ')
            at++;

        int end = at;
        while (end < line.Length && line[end] != ' ')
            end++;

        return line[at..end];
    }

    private static int Number(string line, string key)
    {
        string word = Word(line, key);
        return int.TryParse(word, out int value) ? value : 0;
    }

    private static string? Quoted(string line)
    {
        int open = line.IndexOf('"', StringComparison.Ordinal);
        if (open < 0)
            return null;

        int close = line.LastIndexOf('"');
        return close > open ? line[(open + 1)..close] : null;
    }

    /// <summary>
    /// Accumulates one case, whose lines follow its header rather than sitting on it.
    /// </summary>
    private sealed class Builder
    {
        private readonly List<DumpQueryNode> nodes = [];
        private readonly List<int> optimized = [];
        private readonly List<int> byTag = [];
        private readonly List<int> scanned = [];
        private readonly List<int> setBits = [];
        private readonly List<int> walkBits = [];

        private string filter = string.Empty;
        private string note = string.Empty;
        private bool fullyMapped;
        private bool bitmapsFreed;
        private bool setStable;
        private int count;
        private string? orderingTag;
        private bool declaredAgrees;
        private DumpFlagSet? set;
        private DumpFlagSet? afterWalk;
        private List<int>? bits;
        private List<int>? recs;

        public static Builder Start(string line)
        {
            // The generator escapes the filter with the same writer it escapes every other recorded
            // text with, so it comes back off with the shared reader rather than a second set of
            // rules here -- which is the whole reason DumpEscape exists.
            int quote = line.IndexOf('"', StringComparison.Ordinal);
            byte[] text = DumpEscape.ReadQuoted(line, ref quote);
            int end = line.IndexOf("   ; ", StringComparison.Ordinal);

            return new Builder
            {
                filter = Encoding.Latin1.GetString(text),
                note = end > 0 ? line[(end + 5)..] : string.Empty,
            };
        }

        public void Read(string line)
        {
            if (line.StartsWith("recs ", StringComparison.Ordinal))
            {
                recs?.AddRange(Numbers(line, "recs ".Length));
                return;
            }

            if (line.StartsWith("bits ", StringComparison.Ordinal))
            {
                bits?.AddRange(Numbers(line, "bits ".Length));
                return;
            }

            if (line.StartsWith("node ", StringComparison.Ordinal))
            {
                string tag = Word(line, "tag=");
                nodes.Add(new DumpQueryNode(
                    Number(line, "row="),
                    Number(line, "len="),
                    Number(line, "numParms="),
                    Number(line, "numEntries="),
                    Number(line, "fieldNo="),
                    tag == "-" ? null : tag.Trim('"')));
                return;
            }

            string head = Word(line, string.Empty);
            switch (head)
            {
                case "fullyMapped": fullyMapped = Number(line, head) != 0; break;
                case "bitmapsFreed": bitmapsFreed = Number(line, head) != 0; break;
                case "setStable": setStable = Word(line, head) == "yes"; break;
                case "count": count = Number(line, head); break;
                case "byTagName": orderingTag = Quoted(line); break;
                case "set": set = ReadFlags(line, setBits); bits = setBits; break;
                case "setAfterWalk": afterWalk = ReadFlags(line, walkBits); bits = walkBits; break;
                case "optimized": recs = optimized; break;
                case "byTag": recs = byTag; break;
                case "scan": recs = scanned; break;
                case "agree": declaredAgrees = Word(line, head) == "yes"; break;
                default: break;
            }
        }

        public DumpQueryCase Build() =>
            new(filter, note, fullyMapped, bitmapsFreed, nodes, set, afterWalk, setStable,
                optimized, count, orderingTag, byTag, scanned,
                // Recomputed rather than taken from the dump's own "agree" line, which is parsed
                // separately: the two must match, and a test asserts they do. That is the round
                // trip -- a reader that mis-parsed either record list would disagree with the
                // generator's own verdict instead of failing silently.
                optimized.SequenceEqual(scanned), declaredAgrees);

        // The list handed in is still empty here and is filled by the "bits" lines that follow;
        // the record keeps the same instance, so it sees them.
        private static DumpFlagSet ReadFlags(string line, List<int> into) =>
            line.Contains("absent", StringComparison.Ordinal)
                ? new DumpFlagSet(false, 0, false, into)
                : new DumpFlagSet(true, Number(line, "numFlags="), Number(line, "isFlip=") != 0, into);

        private static IEnumerable<int> Numbers(string line, int from) =>
            line[from..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse);

    }
}
