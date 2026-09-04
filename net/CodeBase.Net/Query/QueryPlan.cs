namespace CodeBase.Net.Query;

/// <summary>
/// What a filter decomposed into: which tags can answer parts of it, and whether that is all of it.
///
/// A plan with no root is not a failure. It means no term could be answered by seeking, so every
/// record is a candidate and the filter is checked one record at a time -- which is always correct
/// and only ever slower.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 4 and 7.
/// </summary>
internal sealed class QueryPlan
{
    private readonly HashSet<int> trusted;

    private QueryPlan(ConstraintNode? root, bool fullyMapped, HashSet<int> trusted)
    {
        Root = root;
        FullyMapped = fullyMapped;
        this.trusted = trusted;
    }

    /// <summary>Gets the constraint tree, or null where nothing could be optimized.</summary>
    public ConstraintNode? Root { get; }

    /// <summary>
    /// Gets a value indicating whether the constraint tree answers the whole filter.
    /// </summary>
    /// <value>
    /// When it does, the records the tree selects are the answer and no record needs to be read to
    /// check them -- which is what lets a count be answered without touching the table at all. When
    /// it does not, they are candidates and the terms no tag answered are checked per record.
    /// </value>
    public bool FullyMapped { get; }

    /// <summary>
    /// Gets how many leaves the tree holds, which is how many times a tag will be seeked.
    /// </summary>
    public int LeafCount => Root?.Walk().Count(node => node.IsLeaf) ?? 0;

    /// <summary>
    /// Decides whether a term of the filter has already been answered by the tree.
    /// </summary>
    /// <param name="node">The index of the term's root node in the compiled filter.</param>
    /// <returns>True when no further check of that term is needed.</returns>
    /// <value>
    /// A property of the plan rather than something re-derived later, which is the point. The
    /// reference asks the question of two fixed positions beside the term's root, and those are the
    /// term's operands only when the right one occupies a single node -- so it sometimes re-checks a
    /// term its own bitmap already answered. Asking the plan instead is both simpler and exact.
    /// </value>
    public bool Answers(int node) => trusted.Contains(node);

    /// <summary>
    /// Builds a plan from a decomposed tree.
    /// </summary>
    /// <param name="root">The tree, or null where nothing could be optimized.</param>
    /// <param name="fullyMapped">Whether the tree answers the whole filter.</param>
    /// <returns>The plan.</returns>
    /// <value>
    /// The trusted terms are collected from the tree that [i]survived[/i], not from every term that
    /// was ever matched to a tag: a disjunction that turned out to be only partly optimizable is
    /// thrown away whole, and the terms inside it go back to being checked per record.
    /// </value>
    public static QueryPlan For(ConstraintNode? root, bool fullyMapped)
    {
        HashSet<int> trusted = [];

        if (root is not null)
        {
            foreach (ConstraintNode node in root.Walk().Where(node => node.IsLeaf))
            {
                foreach (int term in node.SourceNodes)
                    trusted.Add(term);
            }
        }

        return new QueryPlan(root, fullyMapped, trusted);
    }
}
