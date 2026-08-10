using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One line of the dump's stored-descriptor section.
///
/// The format is an ordinal, a name, then named values: [c]type[/c], [c]offset[/c], [c]len[/c],
/// [c]dec[/c], [c]flags[/c] and [c]hasTag[/c].
/// </summary>
/// <param name="Ordinal">Position in the file, counting from one.</param>
/// <param name="Name">The stored name, with its case as written.</param>
/// <param name="Type">The stored type letter.</param>
/// <param name="StoredOffset">The offset the descriptor records, which the engine ignores.</param>
/// <param name="Length">The stored length byte.</param>
/// <param name="Decimals">The stored decimal-count byte.</param>
/// <param name="Flags">The stored flag byte.</param>
/// <param name="HasTag">The production-index marker byte.</param>
internal sealed record DumpDescriptor(
    int Ordinal,
    string Name,
    char Type,
    int StoredOffset,
    int Length,
    int Decimals,
    byte Flags,
    int HasTag)
{
    /// <summary>
    /// Reads one descriptor line.
    /// </summary>
    /// <param name="line">The line, without its newline.</param>
    /// <returns>The values the line records.</returns>
    public static DumpDescriptor Parse(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> values = DumpTokens.NamedValues(tokens);

        return new DumpDescriptor(
            int.Parse(tokens[0], CultureInfo.InvariantCulture),
            tokens[1],
            values["type"][0],
            int.Parse(values["offset"], CultureInfo.InvariantCulture),
            int.Parse(values["len"], CultureInfo.InvariantCulture),
            int.Parse(values["dec"], CultureInfo.InvariantCulture),
            CorpusDump.ParseByte(values["flags"]),
            int.Parse(values["hasTag"], CultureInfo.InvariantCulture));
    }
}
