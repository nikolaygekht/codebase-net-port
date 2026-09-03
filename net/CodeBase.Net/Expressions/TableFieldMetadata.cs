using CodeBase.Net.Dbf;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Answers an expression's names from one open table's fields.
///
/// One table, because that is the whole of what this port indexes and filters over. A name qualified
/// by the table's own alias resolves as an ordinary name; a name qualified by any other alias
/// resolves to nothing, which is how the reference implementation ends up refusing it as an unknown
/// field rather than as an unknown table when there is no second table open (e4parse.c:2452-2456).
/// </summary>
internal sealed class TableFieldMetadata : IFieldMetadata
{
    private readonly IReadOnlyList<FieldDefinition> fields;
    private readonly string alias;

    /// <summary>
    /// Builds the answer set for one table.
    /// </summary>
    /// <param name="fields">The table's fields, in table order.</param>
    /// <param name="alias">The name the table answers to when a field is qualified.</param>
    public TableFieldMetadata(IReadOnlyList<FieldDefinition> fields, string alias)
    {
        this.fields = fields;
        this.alias = alias;
    }

    /// <inheritdoc/>
    public FieldReference? Find(string? aliasUsed, string name)
    {
        if (aliasUsed is not null && !string.Equals(aliasUsed, alias, StringComparison.OrdinalIgnoreCase))
            return null;

        for (int i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return new FieldReference(i + 1, fields[i]);
        }

        return null;
    }
}
