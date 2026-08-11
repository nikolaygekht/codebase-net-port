using System.Globalization;

namespace CodeBase.Net.Golden;

/// <summary>
/// One record of a corpus table, as the dump records it.
///
/// The generator walks the table from top to end of file and writes every field of every record, so
/// a record here is the whole row and not a selection from it. The null-flags bitmap follows the
/// fields when the table has one; it is not a field, which is why it sits beside them rather than
/// among them.
/// </summary>
internal sealed class DumpRecord
{
    private readonly List<DumpValue> values = [];

    private DumpRecord(int number, bool deleted)
    {
        Number = number;
        Deleted = deleted;
    }

    /// <summary>Gets the record number, counting from one.</summary>
    public int Number { get; }

    /// <summary>Gets a value indicating whether the record is flagged deleted.</summary>
    public bool Deleted { get; }

    /// <summary>Gets the fields of the record, in the order the C library reports them.</summary>
    public IReadOnlyList<DumpValue> Values => values;

    /// <summary>
    /// Gets the stored bytes of the null-flags bitmap, or null where the table has no nullable field.
    /// </summary>
    /// <value>
    /// The bitmap itself rather than the flags read out of it. Without it the port's null decoding
    /// would be gated only through the per-field marks and never against the bytes those marks come
    /// from.
    /// </value>
    public byte[]? NullFlags { get; private set; }

    /// <summary>
    /// Gets the field of the given name.
    /// </summary>
    /// <param name="name">The field name, upper-cased as the dump writes it.</param>
    /// <returns>That field's expected values.</returns>
    public DumpValue this[string name] =>
        values.SingleOrDefault(v => v.Name == name)
        ?? throw new InvalidDataException($"Record {Number} has no field named '{name}'.");

    /// <summary>
    /// The name the C library reports for the hidden null-flags field.
    /// </summary>
    private const string NullFlagsName = "_NULLFLAGS";

    /// <summary>
    /// Reads the line that opens a record, or reports that the line is not one.
    /// </summary>
    /// <param name="line">The line, without its newline.</param>
    /// <param name="record">The record the line opens, or null when it opens none.</param>
    /// <returns>True when the line opened a record.</returns>
    /// <exception cref="InvalidDataException">
    /// The line begins a record but does not say whether it is deleted. A record whose deletion flag
    /// went unread would be gated in neither state.
    /// </exception>
    public static bool TryParseHeader(string line, out DumpRecord? record)
    {
        record = null;
        if (!line.StartsWith("rec ", StringComparison.Ordinal))
            return false;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, string> named = DumpTokens.NamedValues(parts);

        if (parts.Length < 3 || !named.TryGetValue("deleted", out string? deleted))
            throw new InvalidDataException($"A record line has no deletion flag: {line}");

        record = new DumpRecord(
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            deleted == "1");

        return true;
    }

    /// <summary>
    /// Adds the field, or the null-flags bitmap, that the line describes.
    /// </summary>
    /// <param name="line">A line belonging to this record.</param>
    public void Add(string line)
    {
        DumpValue value = DumpValue.Parse(line);

        if (value.Name == NullFlagsName)
            NullFlags = value.Bytes;
        else
            values.Add(value);
    }
}
