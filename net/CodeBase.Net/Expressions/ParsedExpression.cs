namespace CodeBase.Net.Expressions;

/// <summary>
/// An expression the parser has read but nothing has typed yet.
///
/// This is the parse pass's whole output: the postfix node array and the text it came from. It is
/// not a compiled expression, because no node knows what type it produces and several do not yet
/// know how long their result is. What it does know is final, though, and that is what makes it worth
/// naming: overload resolution rewrites rows and lengths in place, so the node count, each node's
/// operand count, each node's subtree size and each field reference are already what the compiled
/// expression will report.
/// </summary>
internal sealed class ParsedExpression
{
    private readonly ExpressionNode[] nodes;

    /// <summary>
    /// Builds the result of a parse.
    /// </summary>
    /// <param name="source">The text that was parsed.</param>
    /// <param name="nodes">The nodes, in postfix order with the root last.</param>
    internal ParsedExpression(string source, ExpressionNode[] nodes)
    {
        Source = source;
        this.nodes = nodes;
    }

    /// <summary>
    /// Gets the text the expression was parsed from.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the nodes, in postfix order with the root last.
    /// </summary>
    public IReadOnlyList<ExpressionNode> Nodes => nodes;
}
