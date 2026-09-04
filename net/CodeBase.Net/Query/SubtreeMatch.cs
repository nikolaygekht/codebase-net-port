using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;

namespace CodeBase.Net.Query;

/// <summary>
/// Whether a tag's key expression is the same expression as a subexpression of a filter.
///
/// This is the hinge of the whole optimizer: it is what decides that the tag built over
/// [c]UPPER(NAME)[/c] can answer the term [c]UPPER(NAME) = "SM"[/c]. It compares two compiled node
/// arrays and never their text, because the same expression can be written several ways and two
/// different expressions can be spelled alike once a table's aliases are resolved.
///
/// The reference does the field half of this with a memcmp over a record layout, which is why one of
/// its structure fields carries a comment ordering it last. There is no such trick here, so what
/// makes two field references the same is stated: the same name, width, decimals, type and offset.
///
/// Governing specification: QUERY-OPTIMIZER.md section 3.3.
/// </summary>
internal static class SubtreeMatch
{
    /// <summary>
    /// Decides whether a tag's key expression equals the subexpression rooted at a node.
    /// </summary>
    /// <param name="filter">The compiled filter.</param>
    /// <param name="node">The index of the subexpression's root within it.</param>
    /// <param name="key">The tag's compiled key expression.</param>
    /// <returns>True when the two are the same expression.</returns>
    public static bool Matches(CompiledExpression filter, int node, CompiledExpression key)
    {
        IReadOnlyList<ExpressionNode> left = filter.Nodes;
        IReadOnlyList<ExpressionNode> right = key.Nodes;
        int keyRoot = right.Count - 1;
        int span = left[node].SubtreeSize;

        if (span != right[keyRoot].SubtreeSize || span > node + 1)
            return false;

        for (int back = 0; back < span; back++)
        {
            if (!Same(left[node - back], right[keyRoot - back]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decides whether two nodes are the same node.
    /// </summary>
    private static bool Same(in ExpressionNode a, in ExpressionNode b)
    {
        if (a.Length != b.Length || a.SubtreeSize != b.SubtreeSize || a.ParameterCount != b.ParameterCount)
            return false;

        // A differing row is tolerated only when both nodes load a field, and that tolerance is the
        // whole reason a numeric tag is usable at all. Comparing a field against a number promotes
        // the field's loader to the row that yields a double, so the same field reaches row 8 inside
        // a filter and row 9 inside the tag that indexes it -- and without this the two would never
        // match. What keeps it honest is the field identity below, not the row.
        if (a.Row != b.Row && !(IsFieldLoader(a.Row) && IsFieldLoader(b.Row)))
            return false;

        if ((a.Field is null) != (b.Field is null))
            return false;

        if (a.Field is not null && !SameField(a.Field.Value.Definition, b.Field!.Value.Definition))
            return false;

        if ((a.Constant is null) != (b.Constant is null))
            return false;

        if (a.Constant is not null && !SameConstant(a.Constant.Value, b.Constant!.Value))
            return false;

        // The number the parser folded into the node -- a substring's offset, a formatted number's
        // decimals. Two nodes carrying different ones compute different things from the same
        // operands, so SUBSTR(NAME,1,5) and SUBSTR(NAME,2,5) are not the same expression.
        return a.Immediate == b.Immediate;
    }

    /// <summary>
    /// The rows that load a field, which are the only rows the row check is relaxed for.
    /// </summary>
    private static bool IsFieldLoader(int row) => row <= FunctionRows.FieldMemo;

    /// <summary>
    /// Decides whether two field references name the same field of the same table.
    /// </summary>
    /// <value>
    /// The table is not part of it because this port opens one table per expression, so two field
    /// definitions that agree on all five of these came from the same descriptor. The reference has
    /// to check the owning file as well, because a filter there may name a field of a related table
    /// and a tag over that table's field must not be allowed to answer it.
    /// </value>
    private static bool SameField(FieldDefinition a, FieldDefinition b) =>
        a.Length == b.Length
        && a.Decimals == b.Decimals
        && a.Type == b.Type
        && a.RecordOffset == b.RecordOffset
        && string.Equals(a.Name, b.Name, StringComparison.Ordinal);

    /// <summary>
    /// Decides whether two literals are the same literal.
    /// </summary>
    /// <value>
    /// The reference compares the two expressions' constant pools byte for byte at the offset each
    /// node records. Here a node carries its literal rather than an offset into a pool, so the
    /// comparison is of the values themselves -- which is the same question asked directly.
    /// </value>
    private static bool SameConstant(ExpressionConstant a, ExpressionConstant b)
    {
        if (a.IsText != b.IsText)
            return false;

        return a.IsText
            ? a.Text.SequenceEqual(b.Text)
            : a.Number.Equals(b.Number);
    }
}
