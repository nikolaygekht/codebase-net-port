using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One tag of an index dump: its header, every block of its tree, and every key in navigation order.
/// </summary>
internal sealed class DumpIndexTag
{
    private DumpIndexTag(
        string name,
        Dictionary<string, string> header,
        Dictionary<string, string> text,
        byte[] expression,
        byte[] filter,
        int count,
        IReadOnlyList<DumpIndexBlock> blocks,
        IReadOnlyList<DumpIndexKey> keys)
    {
        Name = name;
        Header = header;
        Text = text;
        ExpressionBytes = expression;
        FilterBytes = filter;
        Count = count;
        Blocks = blocks;
        Keys = keys;
    }

    /// <summary>Gets the tag's name, or [c]*directory*[/c] for the hidden tag-name tree.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether this is the hidden tag-name tree.</summary>
    public bool IsDirectory => Name == "*directory*";

    /// <summary>Gets the named values of the header line, as written.</summary>
    public Dictionary<string, string> Header { get; }

    /// <summary>Gets the named values of the text line, which carries the four length fields.</summary>
    public Dictionary<string, string> Text { get; }

    /// <summary>Gets the key expression's bytes.</summary>
    public byte[] ExpressionBytes { get; }

    /// <summary>Gets the FOR clause's bytes, empty when the tag has none.</summary>
    public byte[] FilterBytes { get; }

    /// <summary>Gets the number of keys the tag holds.</summary>
    public int Count { get; }

    /// <summary>Gets every block of the tag's tree, in the order a full walk reached it.</summary>
    /// <value>
    /// The order is documentation, not a promise: a port is free to read blocks in any order, so tests
    /// compare this as a set keyed by node number.
    /// </value>
    public IReadOnlyList<DumpIndexBlock> Blocks { get; }

    /// <summary>Gets every key with its record number, in the tag's own navigation order.</summary>
    /// <value>Reversed for a descending tag, because that is what its order is.</value>
    public IReadOnlyList<DumpIndexKey> Keys { get; }

    /// <summary>Gets the tag's key length.</summary>
    public int KeyLength => (int)Number("keyLen");

    /// <summary>Gets the tag's option byte.</summary>
    public byte TypeCode => (byte)CorpusIndexDump.ParseHex(Header["typeCode"]);

    /// <summary>Gets the byte the C library declares unused.</summary>
    public byte Signature => (byte)CorpusIndexDump.ParseHex(Header["signature"]);

    /// <summary>Gets a value indicating whether the tag is walked from its greatest key down.</summary>
    public bool Descending => Number("descending") != 0;

    /// <summary>Gets the pad byte the C library used when it rebuilt this tag's keys.</summary>
    public byte PadByte => (byte)CorpusIndexDump.ParseHex(Header["pChar"]);

    /// <summary>Gets the node of the tree's root.</summary>
    public uint Root => (uint)Number("root");

    /// <summary>Gets the head of the free chain as this header holds it.</summary>
    public uint FreeList => (uint)Number("freeList");

    /// <summary>Gets the change counter as this header holds it.</summary>
    public uint Version => (uint)Number("version");

    /// <summary>Gets the node the tag's own header block sits at.</summary>
    public uint HeaderNode => (uint)Number("headerNode");

    /// <summary>Gets the collation name the header spells, empty for machine order.</summary>
    public string SortSequence { get; private set; } = string.Empty;

    private long Number(string name) =>
        long.Parse(Header[name], CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads one tag section, leaving the position on the line after it.
    /// </summary>
    /// <param name="lines">The dump's lines.</param>
    /// <param name="i">The index of the section's opening line, advanced past the section.</param>
    /// <param name="what">What is being read, for messages.</param>
    /// <returns>The tag.</returns>
    public static DumpIndexTag Parse(string[] lines, ref int i, string what)
    {
        string opening = lines[i].TrimEnd('\r');
        string name = opening["[tag ".Length..].TrimEnd(']');

        Dictionary<string, string> header = [];
        Dictionary<string, string> text = [];
        byte[] expression = [];
        byte[] filter = [];
        string sortSequence = string.Empty;
        int count = -1;
        List<DumpIndexBlock> blocks = [];
        List<DumpIndexKey> keys = [];
        int declaredBlocks = -1;

        for (i++; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');

            if (line.Length == 0)
                continue;

            if (line.StartsWith("[tag ", StringComparison.Ordinal))
            {
                i--;
                break;
            }

            if (line.StartsWith("header ", StringComparison.Ordinal))
            {
                header = DumpTokens.NamedValues(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                continue;
            }

            if (line.StartsWith("text ", StringComparison.Ordinal))
            {
                text = DumpTokens.NamedValues(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                int quote = line.IndexOf('"', StringComparison.Ordinal);
                sortSequence = System.Text.Encoding.ASCII.GetString(DumpEscape.ReadQuoted(line, ref quote));
                continue;
            }

            if (line.StartsWith("expr ", StringComparison.Ordinal))
            {
                int at = line.IndexOf('"', StringComparison.Ordinal);
                expression = DumpEscape.ReadQuoted(line, ref at);
                continue;
            }

            if (line.StartsWith("filter ", StringComparison.Ordinal))
            {
                int at = line.IndexOf('"', StringComparison.Ordinal);
                filter = DumpEscape.ReadQuoted(line, ref at);
                continue;
            }

            if (line.StartsWith("count ", StringComparison.Ordinal))
            {
                count = int.Parse(line["count ".Length..].Trim(), CultureInfo.InvariantCulture);
                continue;
            }

            if (line == "[blocks]" || line == "[keys]")
                continue;

            if (line.StartsWith("blocks ", StringComparison.Ordinal))
            {
                declaredBlocks = int.Parse(line["blocks ".Length..].Trim(), CultureInfo.InvariantCulture);
                continue;
            }

            if (line.StartsWith("node=", StringComparison.Ordinal))
            {
                blocks.Add(DumpIndexBlock.Parse(line));
                continue;
            }

            // Everything left is indented: a block's entry, or a key. A key line starts with a quoted
            // value, an entry line with its index.
            string body = line.TrimStart();
            if (body.StartsWith('"'))
            {
                keys.Add(DumpIndexKey.Parse(body));
                continue;
            }

            if (blocks.Count == 0)
                throw new InvalidDataException($"Entry line '{line}' of {what} precedes its block.");

            blocks[^1].AddEntry(body);
        }

        if (count < 0)
            throw new InvalidDataException($"Tag {name} of {what} has no key count.");
        if (keys.Count != count)
            throw new InvalidDataException($"Tag {name} of {what} counts {count} keys but lists {keys.Count}.");
        if (declaredBlocks != blocks.Count)
        {
            throw new InvalidDataException(
                $"Tag {name} of {what} counts {declaredBlocks} blocks but lists {blocks.Count}.");
        }

        foreach (DumpIndexBlock block in blocks)
            block.CheckEntryCount(name, what);

        return new DumpIndexTag(name, header, text, expression, filter, count, blocks, keys)
        {
            SortSequence = sortSequence,
        };
    }
}

/// <summary>
/// One key of a tag, as the dump records it.
/// </summary>
/// <param name="Key">The stored key bytes, rebuilt to the tag's key length.</param>
/// <param name="Record">The record number the key points at.</param>
internal readonly record struct DumpIndexKey(byte[] Key, uint Record)
{
    /// <summary>
    /// Reads a key line.
    /// </summary>
    /// <param name="body">The line, without its leading whitespace.</param>
    /// <returns>The key and its record number.</returns>
    public static DumpIndexKey Parse(string body)
    {
        int at = 0;
        byte[] key = DumpEscape.ReadQuoted(body, ref at);
        uint record = uint.Parse(body[at..].Trim(), CultureInfo.InvariantCulture);

        return new DumpIndexKey(key, record);
    }
}
