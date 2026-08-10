using System.Collections;
using CodeBase.Net.Dbf;

namespace CodeBase.Net;

/// <summary>
/// A table's fields, in the order the file stores them, addressable by position or by name.
///
/// Names are matched without regard to case, because the engine upper-cases every field name when
/// it opens a table and a caller should not have to know that. Where a table has two fields of the
/// same name, which a corrupt file can produce, the first wins, as it does in the C library.
/// </summary>
public sealed class FieldCollection : IReadOnlyList<FieldDefinition>
{
    private readonly IReadOnlyList<FieldDefinition> fields;
    private readonly Dictionary<string, FieldDefinition> byName;

    internal FieldCollection(IReadOnlyList<FieldDefinition> fields)
    {
        this.fields = fields;
        byName = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (FieldDefinition field in fields)
            byName.TryAdd(field.Name, field);
    }

    /// <summary>
    /// Gets the number of fields.
    /// </summary>
    public int Count => fields.Count;

    /// <summary>
    /// Gets a field by its position in the file, counting from zero.
    /// </summary>
    /// <param name="index">The position.</param>
    public FieldDefinition this[int index] => fields[index];

    /// <summary>
    /// Gets a field by name, without regard to case.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <exception cref="KeyNotFoundException">The table has no field of that name.</exception>
    public FieldDefinition this[string name] =>
        byName.TryGetValue(name, out FieldDefinition? field)
            ? field
            : throw new KeyNotFoundException($"The table has no field named '{name}'.");

    /// <summary>
    /// Finds a field by name, without regard to case.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <param name="field">The field, when there is one.</param>
    /// <returns>True when the table has a field of that name.</returns>
    public bool TryGet(string name, out FieldDefinition? field) => byName.TryGetValue(name, out field);

    /// <summary>
    /// Returns an enumerator over the fields, in file order.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<FieldDefinition> GetEnumerator() => fields.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
