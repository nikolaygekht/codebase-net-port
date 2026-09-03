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
}
