using CodeBase.Net.Expressions;

namespace CodeBase.Net.Query;

/// <summary>
/// Turns a compiled filter into the constraint tree an optimizer can answer it from.
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
/// Nothing here merges. Two terms over one tag become two leaves that will seek that tag twice and
/// intersect, which is the right answer reached the slow way -- turning them into one range is a
/// later and separate concern, and keeping it separate is what lets the record sets be gated before
/// any merging exists to change them.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 3 and 4.
/// </summary>
internal sealed class QueryDecomposer
{
    private readonly IReadOnlyList<QueryTag> tags;
    private readonly QueryTagScope scope;
    private readonly ExpressionOptions options;

    /// <summary>
    /// Builds a decomposer over a table's tags.
    /// </summary>
    /// <param name="tags">The table's tags, in the order they should be considered.</param>
    /// <param name="scope">Which of them the caller allows to be used.</param>
    /// <param name="options">The date policy a constant is read under.</param>
    /// <value>
    /// The date policy is here because a constant can be a date read out of text, and what such a
    /// text means depends on it: the reference takes the century from the system clock, and this
    /// library takes it from what the caller asked for. A filter's constant is evaluated once when
    /// the plan is built, so the policy has to be known then.
    /// </value>
    public QueryDecomposer(
        IReadOnlyList<QueryTag> tags, QueryTagScope scope, ExpressionOptions? options = null)
    {
        this.tags = tags;
        this.scope = scope;
        this.options = options ?? ExpressionOptions.Invariant;
    }

    /// <summary>
    /// Decomposes a filter into a plan.
    /// </summary>
    /// <param name="filter">The compiled filter.</param>
    /// <returns>The plan, whose tree is null where nothing could be optimized.</returns>
    public QueryPlan Decompose(CompiledExpression filter)
    {
        var walk = new Walk(this, filter);
        int cursor = filter.Nodes.Count - 1;
        ConstraintNode? root = walk.Visit(ref cursor);

        // A whole filter that yields nothing is not fully mapped, whatever the walk decided on the
        // way down -- the reference sets the same flag at the same point (m4map.c:375-377).
        return QueryPlan.For(root, root is not null && walk.FullyMapped);
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
        var walk = new Walk(this, filter);
        int cursor = filter.Nodes.Count - 1;

        walk.Visit(ref cursor);
        return walk.Matches;
    }

    /// <summary>
    /// One decomposition, holding the state a single walk accumulates.
    /// </summary>
    private sealed class Walk
    {
        private readonly QueryDecomposer owner;
        private readonly CompiledExpression filter;
        private readonly ExpressionEvaluator evaluator;

        public Walk(QueryDecomposer owner, CompiledExpression filter)
        {
            this.owner = owner;
            this.filter = filter;
            evaluator = new ExpressionEvaluator(filter);
            Matches = new QueryTag?[filter.Nodes.Count];
        }

        /// <summary>Gets the tag matched at each node of the filter.</summary>
        public QueryTag?[] Matches { get; }

        /// <summary>Gets a value indicating whether every term was answered by a tag.</summary>
        public bool FullyMapped { get; private set; } = true;

        /// <summary>
        /// Visits the subexpression at the cursor, and moves the cursor past the whole of it.
        /// </summary>
        public ConstraintNode? Visit(ref int cursor)
        {
            IReadOnlyList<ExpressionNode> nodes = filter.Nodes;
            int root = cursor;
            ExpressionNode node = nodes[root];

            // The caller's cursor steps over this whole subexpression; a local one walks inside it.
            cursor -= node.SubtreeSize;

            if (node.Row is FunctionRows.And or FunctionRows.Or)
                return VisitConnective(root, node);

            if (node.Row is >= FunctionRows.FirstComparison and <= FunctionRows.LastComparison)
                return VisitComparison(root);

            // Anything else at all -- a bare logical field, a negation, a function call at the top of
            // a term -- is the whole of the "cannot be optimized" rule (m4map.c:288-291).
            FullyMapped = false;
            return null;
        }

        /// <summary>
        /// Visits an and or an or, which differ only in what a failing operand does to the rest.
        /// </summary>
        private ConstraintNode? VisitConnective(int root, in ExpressionNode node)
        {
            bool isAny = node.Row == FunctionRows.Or;
            int inner = root - 1;
            List<ConstraintNode> children = [];

            for (int operand = 0; operand < node.ParameterCount; operand++)
            {
                ConstraintNode? child = Visit(ref inner);

                if (child is not null)
                {
                    children.Add(child);
                    continue;
                }

                FullyMapped = false;

                // A disjunction is only as good as its weakest term: dropping one would narrow the
                // candidate set and lose records nothing downstream can recover. Dropping a conjunct
                // only widens it, and the residual check narrows it again.
                if (isAny)
                    return null;
            }

            if (children.Count == 0)
                return null;

            return children.Count == 1
                ? children[0]
                : ConstraintNode.Branch(
                    isAny ? ConstraintCombination.Any : ConstraintCombination.All, children);
        }

        /// <summary>
        /// Visits a comparison, which is the only shape a leaf is ever built from.
        /// </summary>
        private ConstraintNode? VisitComparison(int root)
        {
            int right = root - 1;
            int left = right - filter.Nodes[right].SubtreeSize;

            if (left < 0)
                return null;

            // One operand must be a constant and the other must be some tag's key expression. Both
            // orders are accepted, and only the operand that is not the constant is tested.
            bool constantOnRight = evaluator.IsConstant(right);
            int tagNode = constantOnRight ? left : right;
            int constantNode = constantOnRight ? right : left;

            if (!constantOnRight && !evaluator.IsConstant(left))
                return null;

            if (!TryMatch(tagNode, out QueryTag tag))
                return null;

            ConstraintValue value = ConstraintValue.From(
                evaluator.Evaluate(constantNode, new EvaluationContext { Options = owner.options }),
                filter.Nodes[constantNode].Length);

            LeafConstraint? constraint = Constrain(filter.Nodes[root].Row, !constantOnRight, value, tag);

            return constraint is null ? null : ConstraintNode.Leaf(tag, constraint, root);
        }

        /// <summary>
        /// Turns a comparison operator and a constant into what the tag is constrained to.
        /// </summary>
        /// <param name="row">The comparison's row, which is what names the operator.</param>
        /// <param name="reflected">Whether the constant was the left operand.</param>
        /// <param name="value">The constant.</param>
        /// <param name="tag">The tag being constrained.</param>
        /// <returns>The constraint, or null where the operator is not one that constrains.</returns>
        /// <value>
        /// The operator is read from the row's band, because a row is one overload of one operator
        /// and the bands are contiguous. When the constant came first the operator is reflected --
        /// [c]4 &gt; K_NUM[/c] bounds K_NUM above by four, not below.
        ///
        /// The reference gets that reflection wrong in a way this port cannot inherit: all four of
        /// its reflecting branches test an assignment rather than a comparison, so the condition it
        /// actually evaluates discards half of what was intended. The two readings agree for a
        /// comparison whose operands are single nodes and diverge for one nested inside a connective,
        /// where the reference can file a bound under its opposite. The intent is implemented here
        /// and the corpus carries the case that tells them apart.
        /// </value>
        private static LeafConstraint? Constrain(
            int row, bool reflected, ConstraintValue value, in QueryTag tag) => row switch
        {
            >= FunctionRows.FirstComparison and < FunctionRows.GreaterOrEqual =>
                LeafConstraint.Excluding(value),

            >= FunctionRows.Equal and < FunctionRows.Greater =>
                LeafConstraint.Exactly(value),

            >= FunctionRows.GreaterOrEqual and < FunctionRows.LessOrEqual => reflected
                ? LeafConstraint.To(value, inclusive: true)
                : LeafConstraint.From(value, inclusive: true),

            >= FunctionRows.LessOrEqual and < FunctionRows.Equal => reflected
                ? LeafConstraint.From(value, inclusive: true)
                : LeafConstraint.To(value, inclusive: true),

            >= FunctionRows.Greater and < FunctionRows.Less => reflected
                ? LeafConstraint.To(value, inclusive: false)
                : LeafConstraint.From(value, inclusive: Widens(value, tag)),

            >= FunctionRows.Less and <= FunctionRows.LastComparison => reflected
                ? LeafConstraint.From(value, inclusive: Widens(value, tag))
                : LeafConstraint.To(value, inclusive: false),

            _ => null,
        };

        /// <summary>
        /// Decides whether an exclusive lower bound has to admit its own value after all.
        /// </summary>
        /// <param name="value">The constant the bound was written at.</param>
        /// <param name="tag">The tag being constrained.</param>
        /// <returns>True when the bound must be inclusive.</returns>
        /// <value>
        /// A character constant shorter than the tag's keys stands for a prefix, and every key that
        /// begins with it is greater than it -- a tie on the common bytes is broken by width, so
        /// "SM" followed by blanks really is greater than "SM". An exclusive seek would step past
        /// that whole group and lose it, so the bound admits its own value and loses nothing: the
        /// widening is exact, not a safety margin.
        ///
        /// It applies to the two paths that produce a lower bound and to no others, which is not an
        /// oversight in the reference but a consequence -- only a lower bound can drop the group.
        /// </value>
        private static bool Widens(ConstraintValue value, in QueryTag tag) =>
            value.Type == ExpressionType.Character && value.Length < tag.KeyLength;

        /// <summary>
        /// Looks for a tag whose key expression is the subexpression rooted at a node.
        /// </summary>
        /// <value>
        /// Tags are considered in the table's own order and the first eligible match wins, so a tag
        /// the optimizer refuses does not end the search -- the walk carries on to the next one.
        /// </value>
        private bool TryMatch(int node, out QueryTag matched)
        {
            foreach (QueryTag tag in owner.tags)
            {
                if (!TagEligibility.Allows(tag, owner.scope) ||
                    !SubtreeMatch.Matches(filter, node, tag.Key))
                {
                    continue;
                }

                Matches[node] = tag;
                matched = tag;
                return true;
            }

            matched = default;
            return false;
        }
    }
}
