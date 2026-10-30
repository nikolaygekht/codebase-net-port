using System.Globalization;
using CodeBase.Net.Memo;

namespace CodeBase.Net.Golden;

/// <summary>
/// One memo entry as stored, from a table's memo dump.
/// </summary>
internal sealed record DumpMemoEntry(int Record, string Field, int Block, MemoType Type, int StoredLength)
{
    /// <summary>
    /// Parses one entry line.
    /// </summary>
    /// <param name="line">A line such as [c]rec 5 NOTES block=10 type=3 stored=69[/c].</param>
    /// <returns>The entry.</returns>
    public static DumpMemoEntry Parse(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6 || parts[0] != "rec")
            throw new InvalidDataException($"Not a memo entry line: {line}");

        return new DumpMemoEntry(
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            parts[2],
            Value(parts[3], "block"),
            (MemoType)Value(parts[4], "type"),
            Value(parts[5], "stored"));
    }

    private static int Value(string pair, string name)
    {
        if (!pair.StartsWith(name + "=", StringComparison.Ordinal))
            throw new InvalidDataException($"Expected {name}= in a memo entry line, found {pair}.");
        return int.Parse(pair[(name.Length + 1)..], CultureInfo.InvariantCulture);
    }
}
