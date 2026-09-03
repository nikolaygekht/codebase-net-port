using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One row of the reference implementation's function table, as the dump records it.
/// </summary>
internal sealed class DumpFunctionRow
{
    private DumpFunctionRow(
        int index,
        string name,
        int code,
        int nameLength,
        int priority,
        int returnType,
        int parameterCount,
        IReadOnlyList<int> parameterTypes)
    {
        Index = index;
        Name = name;
        Code = code;
        NameLength = nameLength;
        Priority = priority;
        ReturnType = returnType;
        ParameterCount = parameterCount;
        ParameterTypes = parameterTypes;
    }

    /// <summary>Gets the row's position in the table.</summary>
    public int Index { get; }

    /// <summary>Gets the row's name, empty where the row has none.</summary>
    public string Name { get; }

    /// <summary>Gets the operation code the row is an overload of.</summary>
    public int Code { get; }

    /// <summary>Gets the name length the row records.</summary>
    public int NameLength { get; }

    /// <summary>Gets the operator binding power.</summary>
    public int Priority { get; }

    /// <summary>Gets the return type, as its character code.</summary>
    public int ReturnType { get; }

    /// <summary>Gets the parameter count, minus one for a variadic row.</summary>
    public int ParameterCount { get; }

    /// <summary>Gets the four parameter types, as their character codes.</summary>
    public IReadOnlyList<int> ParameterTypes { get; }

    /// <summary>
    /// Parses one dump line.
    /// </summary>
    /// <param name="line">The line, trimmed.</param>
    /// <returns>The row it describes.</returns>
    public static DumpFunctionRow Parse(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> values = DumpTokens.NamedValues(tokens);

        return new DumpFunctionRow(
            int.Parse(tokens[0], CultureInfo.InvariantCulture),
            tokens[1].Trim('"'),
            int.Parse(values["code"], CultureInfo.InvariantCulture),
            int.Parse(values["nameLen"], CultureInfo.InvariantCulture),
            int.Parse(values["prio"], CultureInfo.InvariantCulture),
            TypeCode(values["return"]),
            int.Parse(values["numParms"], CultureInfo.InvariantCulture),
            [.. values["parms"].Split(',').Select(TypeCode)]);
    }

    /// <summary>
    /// Reads a type out of the form the dump writes it in, which carries the character and its code.
    /// </summary>
    private static int TypeCode(string text)
    {
        int open = text.IndexOf('(', StringComparison.Ordinal);
        int close = text.IndexOf(')', StringComparison.Ordinal);
        return int.Parse(text[(open + 1)..close], CultureInfo.InvariantCulture);
    }
}
