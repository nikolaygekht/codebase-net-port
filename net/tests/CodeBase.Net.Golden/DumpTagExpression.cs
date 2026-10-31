namespace CodeBase.Net.Golden;

/// <summary>
/// One expression the reference implementation resolved for a tag, and the tag it was resolved for.
/// </summary>
/// <param name="Compiled">The expression and the shape it compiled to under that tag.</param>
/// <param name="Table">The corpus table whose fields the expression names and whose index holds the tag.</param>
/// <param name="Tag">The tag whose context the expression was resolved in.</param>
internal sealed record DumpTagExpression(DumpExpression Compiled, string Table, string Tag);
