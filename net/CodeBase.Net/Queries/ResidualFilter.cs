using CodeBase.Net.Expressions;

namespace CodeBase.Net.Queries;

/// <summary>
/// Decides whether a candidate record really satisfies the filter.
///
/// A plan's record set is candidates, not answers, whenever some term of the filter had no tag to
/// answer it. Those terms are evaluated here, once per candidate, and a record that fails any of them
/// is dropped. Terms a tag did answer are not evaluated at all -- that is the whole saving, and the
/// rule deciding which is which is the one thing keeping the result correct.
///
/// Only the outermost conjunction is split. A filter joined at the top by an or is one term and is
/// evaluated whole, because no part of it can be trusted separately: it was either optimized entirely
/// or not at all.
///
/// Governing specification: QUERY-OPTIMIZER.md section 7.3.
/// </summary>
internal sealed class ResidualFilter
{
    private readonly CompiledExpression filter;
    private readonly QueryPlan plan;
    private readonly ExpressionEvaluator evaluator;
    private readonly int[] terms;

    /// <summary>
    /// Builds a filter over a plan.
    /// </summary>
    /// <param name="filter">The compiled filter.</param>
    /// <param name="plan">What decomposition made of it.</param>
    public ResidualFilter(CompiledExpression filter, QueryPlan plan)
    {
        this.filter = filter;
        this.plan = plan;
        evaluator = new ExpressionEvaluator(filter);
        terms = [.. Terms()];
    }

    /// <summary>
    /// Gets a value indicating whether any term still has to be checked per record.
    /// </summary>
    /// <value>
    /// False when the plan answers all of them, which is when its record set is the answer rather
    /// than a candidate list -- and when a count can be taken from the set without reading anything.
    /// </value>
    public bool HasWork => terms.Length > 0;

    /// <summary>
    /// Decides whether the record the context holds satisfies the filter.
    /// </summary>
    /// <param name="context">The record and what evaluating over it needs.</param>
    /// <returns>True when the record belongs in the result.</returns>
    public bool Admits(EvaluationContext context)
    {
        foreach (int term in terms)
        {
            ExpressionValue value = evaluator.Evaluate(term, context);

            if (value.IsNull || !value.Logical)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Works out which terms still have to be checked.
    /// </summary>
    /// <value>
    /// A term is trusted when decomposition produced a leaf for it, which is a property of the plan
    /// rather than something re-derived here. The reference asks the same question of two fixed
    /// positions beside each term's root, and those are its operands only when the right one occupies
    /// a single node -- so it sometimes re-checks a term its own bitmap already answered. Asking the
    /// plan is both simpler and exact.
    ///
    /// A plan that answers the whole filter leaves nothing at all, which is checked first because the
    /// per-term rule alone would not see it: a term that is itself a connective has its leaves
    /// recorded against the comparisons inside it rather than against the connective, so asking
    /// whether that one term is answered says no even when every part of it is. Whole-filter and
    /// per-term are the same question asked at two scales, and the coarse one has to be asked first.
    ///
    /// What remains conservative is a fully optimized connective sitting inside a filter that is
    /// [i]not[/i] fully mapped: it is re-evaluated. That costs time and never records, because
    /// evaluating a term the bitmap already answered can only agree with it.
    /// </value>
    private IEnumerable<int> Terms()
    {
        if (plan.FullyMapped)
            yield break;

        IReadOnlyList<ExpressionNode> nodes = filter.Nodes;
        int root = nodes.Count - 1;
        int count = 1;

        if (nodes[root].Row == FunctionRows.And)
        {
            count = nodes[root].ParameterCount;
            root--;
        }

        // Backwards, because the operands of a node sit before it and the last one is nearest. Which
        // order a conjunction is checked in changes nothing but how soon a false one is found.
        for (int term = 0; term < count; term++)
        {
            if (!plan.Answers(root))
                yield return root;

            root -= nodes[root].SubtreeSize;
        }
    }
}
