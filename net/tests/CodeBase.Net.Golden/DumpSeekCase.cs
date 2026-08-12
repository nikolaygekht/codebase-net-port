using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One seek the C library performed, and where it left the cursor.
///
/// The search values are derived by the generator from each tag's own keys, so the name says how — a
/// test that fails can say "the prefix case on T_DUP" rather than quoting twenty bytes. What is
/// asserted is the pair the library reported: its result code, and the entry the cursor ended on.
/// </summary>
/// <param name="What">How the case was derived, for example [c]prefix-half[/c].</param>
/// <param name="Search">The search bytes.</param>
/// <param name="Length">The search length, which is what the comparison is done over.</param>
/// <param name="ResultCode">
/// What [c]tfile4seek[/c] returned: 0 found, 2 landed on a greater key, 3 nothing at or after.
/// </param>
/// <param name="AtEnd">Whether the cursor ended past the end of the tag.</param>
/// <param name="Record">The record number of the landing entry, or 0 when the cursor is past the end.</param>
/// <param name="Key">The landing entry's key, or empty when the cursor is past the end.</param>
internal readonly record struct DumpSeekCase(
    string What,
    byte[] Search,
    int Length,
    int ResultCode,
    bool AtEnd,
    uint Record,
    byte[] Key)
{
    /// <summary>
    /// Reads a seek line.
    /// </summary>
    /// <param name="body">The line, without its leading whitespace.</param>
    /// <returns>The case and its outcome.</returns>
    /// <exception cref="InvalidDataException">The line is not a seek case.</exception>
    public static DumpSeekCase Parse(string body)
    {
        int at = body.IndexOf('"', StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidDataException($"Seek line '{body}' has no search value.");

        string what = body[..at].Trim();
        byte[] search = DumpEscape.ReadQuoted(body, ref at);
        string rest = body[at..];

        string[] tokens = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int length = int.Parse(tokens[0], CultureInfo.InvariantCulture);
        Dictionary<string, string> values = DumpTokens.NamedValues(tokens);
        int resultCode = int.Parse(values["rc"], CultureInfo.InvariantCulture);

        if (rest.Contains(" eof", StringComparison.Ordinal))
            return new DumpSeekCase(what, search, length, resultCode, true, 0, []);

        int keyAt = rest.IndexOf('"', StringComparison.Ordinal);
        byte[] key = DumpEscape.ReadQuoted(rest, ref keyAt);

        return new DumpSeekCase(
            what,
            search,
            length,
            resultCode,
            false,
            uint.Parse(values["rec"], CultureInfo.InvariantCulture),
            key);
    }
}

/// <summary>
/// One seek-and-then-seek-next run the C library performed, as the records it visited.
///
/// Driven through the data file's own API, so it exists only for the tags whose key transform is the
/// identity — a machine-collated character tag, where the value bytes are the key bytes. A tag without
/// one says so in the dump rather than leaving the section out, so its absence is legible.
/// </summary>
/// <param name="What">How the search value was derived.</param>
/// <param name="Search">The search bytes.</param>
/// <param name="Length">The search length.</param>
/// <param name="SeekResultCode">What the initial [c]d4seekN[/c] returned.</param>
/// <param name="Records">Every record the run visited, in order. Empty when nothing matched.</param>
internal readonly record struct DumpSeekNextRun(
    string What,
    byte[] Search,
    int Length,
    int SeekResultCode,
    IReadOnlyList<uint> Records)
{
    /// <summary>
    /// Reads a seek-next line.
    /// </summary>
    /// <param name="body">The line, without its leading whitespace.</param>
    /// <returns>The run and the records it visited.</returns>
    /// <exception cref="InvalidDataException">The line is not a seek-next run.</exception>
    public static DumpSeekNextRun Parse(string body)
    {
        int at = body.IndexOf('"', StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidDataException($"Seek-next line '{body}' has no search value.");

        string what = body[..at].Trim();
        byte[] search = DumpEscape.ReadQuoted(body, ref at);

        string rest = body[at..];
        int arrow = rest.IndexOf("->", StringComparison.Ordinal);
        if (arrow < 0)
            throw new InvalidDataException($"Seek-next line '{body}' has no run.");

        string[] head = rest[..arrow].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int length = int.Parse(head[0], CultureInfo.InvariantCulture);
        int seek = int.Parse(DumpTokens.NamedValues(head)["seek"], CultureInfo.InvariantCulture);

        string tail = rest[(arrow + 2)..].Trim();
        List<uint> records = [];

        if (tail != "none")
        {
            foreach (string token in tail.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                records.Add(uint.Parse(token, CultureInfo.InvariantCulture));
        }

        return new DumpSeekNextRun(what, search, length, seek, records);
    }
}
