using CodeBase.Net.Expressions;

namespace CodeBase.Net.Query;

/// <summary>
/// Walks a compiled filter and works out what the optimizer can do with it.
///
/// The walk runs over the postfix node array from the root backwards, and its shape is the whole
/// answer to which terms a tag can answer. Two properties of it are load bearing and neither is
/// obvious:
///
/// A connective visits its operands [b]last to first[/b], because they sit before it in the array and
/// the cursor moves backwards over them. And an or is destroyed entirely the moment one of its
/// operands cannot be optimized, without visiting the rest -- so of two filters that select the same
/// records, [c]A .OR. B[/c] and [c]B .OR. A[/c], one may have tested a term against the tags and the
/// other may not. An and is the opposite: it keeps whatever operands it could optimize and lets the
/// ones it could not be checked per record afterwards. Widening a candidate set is safe because a
/// later check narrows it again; narrowing one loses records that nothing recovers.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 3 and 4.
/// </summary>
internal sealed class QueryDecomposer
{
    private readonly IReadOnlyList<QueryTag> tags;
    private readonly QueryTagScope scope;

    /// <summary>
    /// Builds a decomposer over a table's tags.
    /// </summary>
    /// <param name="tags">The table's tags, in the order they should be considered.</param>
    /// <param name="scope">Which of them the caller allows to be used.</param>
    public QueryDecomposer(IReadOnlyList<QueryTag> tags, QueryTagScope scope)
    {
        this.tags = tags;
        this.scope = scope;
    }

    /// <summary>
    /// Works out which tag, if any, can answer the subexpression rooted at each node.
    /// </summary>
    /// <param name="filter">The compiled filter.</param>
    /// <returns>One entry per node of the filter, holding the tag matched there or null.</returns>
    /// <value>
    /// Null at a node means one of two different things and the difference matters: either the node
    /// was tested against every tag and none matched, or the walk never reached it. Only an operand
    /// of a comparison whose other operand is a constant is ever tested, so most nodes are never
    /// candidates at all -- and a term inside a destroyed or, or anywhere under a negation, is not
    /// reached either.
    /// </value>
    public QueryTag?[] MatchTags(CompiledExpression filter)
    {
        QueryTag?[] matches = new QueryTag?[filter.Nodes.Count];
        var evaluator = new ExpressionEvaluator(filter);
        int cursor = filter.Nodes.Count - 1;

        Visit(filter, evaluator, ref cursor, matches);
        return matches;
    }

    /// <summary>
    /// Visits the subexpression at the cursor, and moves the cursor past the whole of it.
    /// </summary>
    /// <returns>True when something about the subexpression can be answered by a tag.</returns>
    private bool Visit(
        CompiledExpression filter,
        ExpressionEvaluator evaluator,
        ref int cursor,
        QueryTag?[] matches)
    {
        IReadOnlyList<ExpressionNode> nodes = filter.Nodes;
        int root = cursor;
        ExpressionNode node = nodes[root];

        // The caller's cursor steps over this whole subexpression; a local one walks inside it.
        cursor -= node.SubtreeSize;

        if (node.Row is FunctionRows.And or FunctionRows.Or)
            return VisitConnective(filter, evaluator, root, node, matches);

        return node.Row is >= FunctionRows.FirstComparison and <= FunctionRows.LastComparison
            && VisitComparison(filter, evaluator, root, matches);
    }

    /// <summary>
    /// Visits an and or an or, which differ only in what a failing operand does to the rest.
    /// </summary>
    private bool VisitConnective(
        CompiledExpression filter,
        ExpressionEvaluator evaluator,
        int root,
        in ExpressionNode node,
        QueryTag?[] matches)
    {
        int inner = root - 1;
        bool anyUsable = false;

        for (int operand = 0; operand < node.ParameterCount; operand++)
        {
            if (Visit(filter, evaluator, ref inner, matches))
                anyUsable = true;
            else if (node.Row == FunctionRows.Or)
                return false;
        }

        return anyUsable;
    }

    /// <summary>
    /// Visits a comparison, which is the only place a tag is ever looked for.
    /// </summary>
    /// <value>
    /// One operand must be a constant and the other must be some tag's key expression. Both orders
    /// are accepted, and only the operand that is not the constant is tested -- so a comparison of
    /// two constants tests the left one against the tags and finds nothing, while a comparison of two
    /// fields is refused before any tag is consulted.
    /// </value>
    private bool VisitComparison(
        CompiledExpression filter,
        ExpressionEvaluator evaluator,
        int root,
        QueryTag?[] matches)
    {
        int right = root - 1;
        int left = right - filter.Nodes[right].SubtreeSize;

        if (left < 0)
            return false;

        if (evaluator.IsConstant(right))
            return TryMatch(filter, left, matches);

        return evaluator.IsConstant(left) && TryMatch(filter, right, matches);
    }

    /// <summary>
    /// Looks for a tag whose key expression is the subexpression rooted at a node, and records it.
    /// </summary>
    /// <value>
    /// Tags are considered in the table's own order and the first eligible match wins, so a tag the
    /// optimizer refuses does not end the search -- the walk carries on to the next one. A table with
    /// a plain tag and a unique tag over the same field finds the plain one.
    /// </value>
    private bool TryMatch(CompiledExpression filter, int node, QueryTag?[] matches)
    {
        foreach (QueryTag tag in tags)
        {
            if (!TagEligibility.Allows(tag, scope) || !SubtreeMatch.Matches(filter, node, tag.Key))
                continue;

            matches[node] = tag;
            return true;
        }

        return false;
    }
}
