namespace CodeBase.Net.Query;

/// <summary>How a branch combines what its children answer.</summary>
internal enum ConstraintCombination
{
    /// <summary>A record must satisfy every child.</summary>
    All,

    /// <summary>A record must satisfy at least one child.</summary>
    Any,
}

/// <summary>
/// One node of the constraint tree a filter decomposes into.
///
/// A leaf names one tag and what that tag is constrained to; a branch combines its children. This is
/// the reference's bitmap node with its five vestigial fields left out -- they are declared there and
/// read nowhere on the path this port follows.
///
/// A leaf also remembers which node of the filter produced it, which is what makes the residual
/// filter's trust rule a property of the plan rather than an inference: a term is checked again per
/// record exactly when no leaf here came from it.
///
/// Governing specification: QUERY-OPTIMIZER.md sections 2.2 and 4.
/// </summary>
internal sealed class ConstraintNode
{
    private static readonly ConstraintNode[] NoChildren = [];

    private static readonly int[] NoSources = [];

    private ConstraintNode(
        QueryTag? tag,
        LeafConstraint? constraint,
        IReadOnlyList<int> sourceNodes,
        ConstraintCombination combination,
        IReadOnlyList<ConstraintNode> children)
    {
        Tag = tag;
        Constraint = constraint;
        SourceNodes = sourceNodes;
        Combination = combination;
        Children = children;
    }

    /// <summary>Gets the tag a leaf constrains, or null for a branch.</summary>
    public QueryTag? Tag { get; }

    /// <summary>Gets what a leaf constrains its tag to, or null for a branch.</summary>
    public LeafConstraint? Constraint { get; }

    /// <summary>Gets the filter terms a leaf answers, which is empty for a branch.</summary>
    /// <value>
    /// More than one once leaves merge: two terms over one tag become a single range, and both of
    /// them are then answered by it. The residual filter reads this to know which terms it need not
    /// check again, so a merge that forgot one would have that term checked twice -- harmless -- and
    /// a merge that invented one would have a term skipped, which is not.
    /// </value>
    public IReadOnlyList<int> SourceNodes { get; }

    /// <summary>Gets how a branch combines its children; meaningless on a leaf.</summary>
    public ConstraintCombination Combination { get; }

    /// <summary>Gets a branch's children, which is empty on a leaf.</summary>
    public IReadOnlyList<ConstraintNode> Children { get; }

    /// <summary>Gets a value indicating whether this is a leaf.</summary>
    public bool IsLeaf => Constraint is not null;

    /// <summary>
    /// Builds a leaf.
    /// </summary>
    /// <param name="tag">The tag it constrains.</param>
    /// <param name="constraint">What it constrains the tag to.</param>
    /// <param name="sourceNode">The filter node the term was rooted at.</param>
    /// <returns>The leaf.</returns>
    public static ConstraintNode Leaf(QueryTag tag, LeafConstraint constraint, int sourceNode) =>
        new(tag, constraint, [sourceNode], ConstraintCombination.All, NoChildren);

    /// <summary>
    /// Builds a leaf that answers several terms at once, which is what merging produces.
    /// </summary>
    /// <param name="tag">The tag it constrains.</param>
    /// <param name="constraint">What it constrains the tag to.</param>
    /// <param name="sourceNodes">The filter terms it answers.</param>
    /// <returns>The leaf.</returns>
    public static ConstraintNode Leaf(
        QueryTag tag, LeafConstraint constraint, IReadOnlyList<int> sourceNodes) =>
        new(tag, constraint, sourceNodes, ConstraintCombination.All, NoChildren);

    /// <summary>
    /// Builds a branch.
    /// </summary>
    /// <param name="combination">How its children combine.</param>
    /// <param name="children">The children, which must not be empty.</param>
    /// <returns>The branch.</returns>
    public static ConstraintNode Branch(
        ConstraintCombination combination, IReadOnlyList<ConstraintNode> children) =>
        new(null, null, NoSources, combination, children);

    /// <summary>
    /// Walks this node and everything under it.
    /// </summary>
    /// <returns>This node first, then its children in order.</returns>
    public IEnumerable<ConstraintNode> Walk()
    {
        yield return this;

        foreach (ConstraintNode child in Children)
        {
            foreach (ConstraintNode inner in child.Walk())
                yield return inner;
        }
    }
}
