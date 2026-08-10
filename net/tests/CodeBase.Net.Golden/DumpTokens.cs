namespace CodeBase.Net.Golden;

/// <summary>
/// Reads the name-equals-value tokens the dump lines are built from.
/// </summary>
internal static class DumpTokens
{
    /// <summary>
    /// Collects every token of the form name equals value, ignoring the rest.
    /// </summary>
    /// <param name="tokens">The whitespace-separated tokens of one line.</param>
    /// <returns>The named values, keyed by name.</returns>
    public static Dictionary<string, string> NamedValues(IEnumerable<string> tokens)
    {
        Dictionary<string, string> values = [];

        foreach (string token in tokens)
        {
            int separator = token.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
                values[token[..separator]] = token[(separator + 1)..];
        }

        return values;
    }
}
