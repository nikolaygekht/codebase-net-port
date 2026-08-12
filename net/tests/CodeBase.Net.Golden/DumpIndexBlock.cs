using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One block of a tag's tree, as the C library's own block object reported it.
///
/// A leaf carries its bit widths, its masks and its free space, and one line per entry giving the
/// record number and the two compression counts. Those counts are the encoding itself, which is what
/// makes them worth checking in: a key can come out right from two compensating mistakes — a wrong
/// duplicate count against a wrong position in the key text — and only the counts catch that.
///
/// An interior block carries one line per entry giving the child, the record number and the whole key.
/// </summary>
internal sealed class DumpIndexBlock
{
    private readonly List<DumpLeafEntry> leafEntries = [];
    private readonly List<DumpBranchEntry> branchEntries = [];

    private DumpIndexBlock(Dictionary<string, string> values)
    {
        Values = values;
    }

    /// <summary>Gets the named values of the block's line, as written.</summary>
    public Dictionary<string, string> Values { get; }

    /// <summary>Gets the node the block sits at.</summary>
    public uint Node => (uint)Number("node");

    /// <summary>Gets the attribute word, exactly as stored.</summary>
    public int Attribute => (int)Number("attr");

    /// <summary>Gets the number of entries the block holds.</summary>
    public int KeyCount => (int)Number("nKeys");

    /// <summary>Gets the node of the block to the left, 0xFFFFFFFF when there is none.</summary>
    public uint Left => (uint)Number("left");

    /// <summary>Gets the node of the block to the right, 0xFFFFFFFF when there is none.</summary>
    public uint Right => (uint)Number("right");

    /// <summary>Gets a value indicating whether the block is a leaf.</summary>
    public bool IsLeaf => Number("leaf") != 0;

    /// <summary>Gets the unused bytes between the entry array and the key text.</summary>
    public int FreeSpace => (int)Number("freeSpace");

    /// <summary>Gets how many bits of a packed entry hold the record number.</summary>
    public int RecordBits => (int)Number("recNumLen");

    /// <summary>Gets how many bits of a packed entry hold the duplicate count.</summary>
    public int DupBits => (int)Number("dupCntLen");

    /// <summary>Gets how many bits of a packed entry hold the trailing-pad count.</summary>
    public int TrailBits => (int)Number("trailCntLen");

    /// <summary>Gets the number of bytes each packed entry occupies.</summary>
    public int InfoLength => (int)Number("infoLen");

    /// <summary>Gets the mask that pulls a record number out of a packed entry.</summary>
    public uint RecordMask => CorpusIndexDump.ParseHex(Values["recNumMask"]);

    /// <summary>Gets the mask that pulls a duplicate count out of a packed entry.</summary>
    public int DupMask => (int)CorpusIndexDump.ParseHex(Values["dupByteCnt"]);

    /// <summary>Gets the mask that pulls a trailing-pad count out of a packed entry.</summary>
    public int TrailMask => (int)CorpusIndexDump.ParseHex(Values["trailByteCnt"]);

    /// <summary>Gets the leaf entries, empty for an interior block.</summary>
    public IReadOnlyList<DumpLeafEntry> LeafEntries => leafEntries;

    /// <summary>Gets the interior entries, empty for a leaf.</summary>
    public IReadOnlyList<DumpBranchEntry> BranchEntries => branchEntries;

    /// <summary>
    /// Reads a block line.
    /// </summary>
    /// <param name="line">The line, starting with its node.</param>
    /// <returns>The block, with no entries yet.</returns>
    public static DumpIndexBlock Parse(string line) =>
        new(DumpTokens.NamedValues(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    /// <summary>
    /// Adds one entry line to the block.
    /// </summary>
    /// <param name="body">The line, without its leading whitespace.</param>
    public void AddEntry(string body)
    {
        string[] tokens = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> values = DumpTokens.NamedValues(tokens);
        int index = int.Parse(tokens[0], CultureInfo.InvariantCulture);

        if (IsLeaf)
        {
            leafEntries.Add(new DumpLeafEntry(
                index,
                uint.Parse(values["rec"], CultureInfo.InvariantCulture),
                int.Parse(values["dup"], CultureInfo.InvariantCulture),
                int.Parse(values["trail"], CultureInfo.InvariantCulture)));

            return;
        }

        int quote = body.IndexOf('"', StringComparison.Ordinal);
        byte[] key = DumpEscape.ReadQuoted(body, ref quote);

        branchEntries.Add(new DumpBranchEntry(
            index,
            uint.Parse(values["child"], CultureInfo.InvariantCulture),
            uint.Parse(values["rec"], CultureInfo.InvariantCulture),
            key));
    }

    /// <summary>
    /// Refuses a block whose entry lines do not match the count it declared.
    /// </summary>
    /// <param name="tag">The tag the block belongs to, for the message.</param>
    /// <param name="what">What is being read, for the message.</param>
    /// <exception cref="InvalidDataException">The counts disagree.</exception>
    public void CheckEntryCount(string tag, string what)
    {
        int listed = IsLeaf ? leafEntries.Count : branchEntries.Count;

        if (listed != KeyCount)
        {
            throw new InvalidDataException(
                $"Node {Node} of tag {tag} in {what} says it holds {KeyCount} entries but lists {listed}.");
        }
    }

    private long Number(string name) => long.Parse(Values[name], CultureInfo.InvariantCulture);
}

/// <summary>
/// One packed leaf entry, as the C library unpacked it.
/// </summary>
/// <param name="Index">Its position in the block.</param>
/// <param name="Record">The record number.</param>
/// <param name="DupCount">How many leading bytes it shares with the previous key.</param>
/// <param name="TrailCount">How many trailing pad bytes were dropped.</param>
internal readonly record struct DumpLeafEntry(int Index, uint Record, int DupCount, int TrailCount);

/// <summary>
/// One interior entry, as the C library read it.
/// </summary>
/// <param name="Index">Its position in the block.</param>
/// <param name="Child">The node of the child block.</param>
/// <param name="Record">The record number of the child's greatest key.</param>
/// <param name="Key">The child's greatest key, stored whole.</param>
internal readonly record struct DumpBranchEntry(int Index, uint Child, uint Record, byte[] Key);
