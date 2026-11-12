namespace CodeBase.Net.Expressions;

/// <summary>
/// Answers what an expression's names refer to.
///
/// The parser needs this because a field's loader row is chosen from the field's type, not decided
/// later: the reference implementation maps the type to a row while it is still scanning
/// (e4parse.c:2276-2347), so a name that resolves to nothing cannot be given a node at all and is
/// refused there and then. Behind this interface there is a table; in front of it there is a parser
/// that has no file, no record and no cursor.
/// </summary>
internal interface IFieldMetadata
{
    /// <summary>
    /// Finds the field a name refers to.
    /// </summary>
    /// <param name="alias">The table the name was qualified by, or null where it was not.</param>
    /// <param name="name">The field name, as the expression spelled it.</param>
    /// <returns>The field, or null where there is no such field.</returns>
    FieldReference? Find(string? alias, string name);

    /// <summary>
    /// Gets a value indicating whether the table stores its field names in CodeBase's long-name layout.
    /// </summary>
    /// <value>
    /// Only then may an expression name a field longer than ten characters; otherwise such a name is
    /// refused while it is being scanned, before any field is looked for (e4parse.c:2404-2417). It is
    /// the metadata's to say, so every compile path over a table gets it from the table.
    /// </value>
    bool LongFieldNames { get; }
}
