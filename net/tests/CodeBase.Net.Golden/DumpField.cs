using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One line of the dump's field section, which is what the C library reports after opening a table.
///
/// This view differs from the stored descriptor in ways the port has to reproduce: the name is
/// upper-cased, the binary memo and binary character types reappear under the letters they were
/// created with rather than the ones they are stored under, and the null-flags field is absent
/// altogether.
/// </summary>
/// <param name="Ordinal">Position in the field list, counting from one.</param>
/// <param name="Name">The reported name, upper-cased.</param>
/// <param name="Type">The reported type letter.</param>
/// <param name="Length">The field length.</param>
/// <param name="Decimals">The decimal count.</param>
/// <param name="IsNullable">Whether the field accepts null.</param>
internal sealed record DumpField(
    int Ordinal,
    string Name,
    char Type,
    int Length,
    int Decimals,
    bool IsNullable)
{
    /// <summary>
    /// Reads one field line.
    /// </summary>
    /// <param name="line">The line, without its newline.</param>
    /// <returns>The values the line records.</returns>
    public static DumpField Parse(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> values = DumpTokens.NamedValues(tokens);

        return new DumpField(
            int.Parse(tokens[0], CultureInfo.InvariantCulture),
            tokens[1],
            values["type"][0],
            int.Parse(values["len"], CultureInfo.InvariantCulture),
            int.Parse(values["dec"], CultureInfo.InvariantCulture),
            // Written only when true, so its absence is the answer rather than a gap. See ADR-16.
            values.ContainsKey("nullable"));
    }
}
