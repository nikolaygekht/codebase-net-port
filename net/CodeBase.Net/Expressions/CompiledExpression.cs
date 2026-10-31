namespace CodeBase.Net.Expressions;

/// <summary>
/// An expression compiled and typed, ready to be evaluated or turned into key bytes.
///
/// The postfix node array is [b]retained[/b] rather than consumed. Two things read it and neither can
/// rebuild it: the evaluator, which walks it and can be asked for the value of one node's subtree,
/// and the query optimizer, which decides whether a tag can replace a subexpression by comparing two
/// of these arrays node for node. Discarding the array once a result type had been worked out would
/// make the second impossible.
///
/// Immutable once built. The reference implementation serialises every evaluation through one
/// process-wide lock because its operand stack is global; nothing here is shared between
/// evaluations, so two cursors over one tag do not contend.
///
/// Governing specification: EXPRESSIONS.md sections 2.2 and 6.
/// </summary>
internal sealed class CompiledExpression
{
    private readonly ExpressionNode[] nodes;

    /// <summary>
    /// Builds a compiled expression.
    /// </summary>
    /// <param name="source">The text it was compiled from.</param>
    /// <param name="nodes">The nodes, in postfix order with the root last.</param>
    /// <param name="hasTrim">Whether a trim appears anywhere in it.</param>
    /// <param name="tag">The tag it was resolved for, or null for a filter.</param>
    internal CompiledExpression(string source, ExpressionNode[] nodes, bool hasTrim, TagContext? tag = null)
    {
        Source = source;
        this.nodes = nodes;
        HasTrim = hasTrim;
        Tag = tag;
    }

    /// <summary>
    /// Gets the text the expression was compiled from.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the nodes, in postfix order with the root last.
    /// </summary>
    public IReadOnlyList<ExpressionNode> Nodes => nodes;

    /// <summary>
    /// Gets the type of the value the whole expression produces.
    /// </summary>
    public ExpressionType ResultType => nodes[^1].Type;

    /// <summary>
    /// Gets the declared length of the value the whole expression produces.
    /// </summary>
    public int Length => nodes[^1].Length;

    /// <summary>
    /// Gets a value indicating whether a trim appears anywhere in the expression.
    /// </summary>
    /// <value>
    /// A property of the whole expression and not of the node that trimmed, because it changes how
    /// the [i]key[/i] is written: a trimmed result is padded with zero bytes rather than blanks, and
    /// key time rewrites them. One trim anywhere sets it.
    /// </value>
    public bool HasTrim { get; }

    /// <summary>
    /// Gets the tag the expression was resolved for, or null when it was resolved as a filter.
    /// </summary>
    /// <value>
    /// Kept because it changes evaluation as well as lengths: under a collated tag an ascending or
    /// descending conversion of a string weighs its operand with the tag's collation
    /// (e4functi.c:3042-3062), and the evaluator has no other way to know which.
    /// </value>
    public TagContext? Tag { get; }
}
