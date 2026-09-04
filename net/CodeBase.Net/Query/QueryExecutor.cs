namespace CodeBase.Net.Query;

/// <summary>
/// Walks a plan, seeks each of its leaves, and combines what they reach.
///
/// A leaf's records come from one tag; a branch's come from folding its children, intersecting them
/// where every term must hold and uniting them where any may. That is the whole of it, and the
/// smallness is worth noticing: the reference has four routines here, chosen by whether the leaf sits
/// under an and or an or, and this has one.
///
/// It can have one because a leaf's set does not depend on the connective above it. Every leaf in the
/// reference starts from a freshly zeroed bitmap (m4map.c:2328-2333), so its four routines are four
/// ways of computing the same thing -- an exclusion under an or is reached by flagging the value and
/// inverting, and the same exclusion under an and by walking the whole range and dropping it, and
/// they agree. The connective decides how leaves combine, which happens here, and nothing else.
///
/// Governing specification: QUERY-OPTIMIZER.md section 6.4.
/// </summary>
internal sealed class QueryExecutor
{
    private readonly Func<QueryTag, ITagProbe> open;
    private readonly Dictionary<int, ITagProbe> probes = [];
    private readonly int recordCount;

    /// <summary>
    /// Builds an executor over a table.
    /// </summary>
    /// <param name="open">How to reach one tag's keys.</param>
    /// <param name="recordCount">How many records the table holds.</param>
    public QueryExecutor(Func<QueryTag, ITagProbe> open, int recordCount)
    {
        this.open = open;
        this.recordCount = recordCount;
    }

    /// <summary>
    /// Works out which records a plan selects.
    /// </summary>
    /// <param name="plan">The decomposed filter.</param>
    /// <returns>The records the plan reaches.</returns>
    /// <value>
    /// A plan with no tree reaches every record, because nothing about the filter could be answered
    /// by seeking and so every record is a candidate. That is not a failure and needs no special case
    /// anywhere above: the reference arrives at the same answer by leaving its bitmap unbuilt and
    /// treating an unbuilt one as admitting everything.
    /// </value>
    public RecordSet Evaluate(QueryPlan plan) =>
        plan.Root is null ? RecordSet.All(recordCount) : Reach(plan.Root);

    /// <summary>
    /// Works out which records one node reaches.
    /// </summary>
    private RecordSet Reach(ConstraintNode node)
    {
        if (node.IsLeaf)
            return ConstraintSeeker.Evaluate(node.Constraint!, ProbeFor(node.Tag!.Value), recordCount);

        RecordSet? combined = null;

        foreach (ConstraintNode child in node.Children)
        {
            RecordSet reached = Reach(child);

            if (combined is null)
            {
                combined = reached;
                continue;
            }

            if (node.Combination == ConstraintCombination.All)
                combined.KeepBoth(reached);
            else
                combined.KeepEither(reached);
        }

        // Decomposition never builds a branch without children, so this is unreachable rather than
        // defensive -- but an empty conjunction admits everything and an empty disjunction nothing,
        // and picking the wrong one silently would be worse than saying which.
        return combined ?? RecordSet.All(recordCount);
    }

    /// <summary>
    /// Reaches one tag's keys, opening the tag at most once however many leaves name it.
    /// </summary>
    /// <value>
    /// Before leaves merge, two terms over one tag are two leaves, and each seeks from the root. They
    /// share the descent that opening costs; what they cannot share is a position, and neither needs
    /// to, because every seek here starts from a value rather than from where the last one stopped.
    /// </value>
    private ITagProbe ProbeFor(in QueryTag tag)
    {
        if (!probes.TryGetValue(tag.Ordinal, out ITagProbe? probe))
        {
            probe = open(tag);
            probes[tag.Ordinal] = probe;
        }

        return probe;
    }
}
