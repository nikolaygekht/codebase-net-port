namespace CodeBase.Net.Expressions;

/// <summary>
/// One node of a compiled expression, in postfix order.
///
/// A compiled expression is an array of these with the root last, which is the shape the reference
/// implementation compiles to (d4data.h:3587-3610) and the shape the query optimizer walks. It is
/// not a tree with child pointers: a node's operands are the subtrees immediately before it, and
/// [c]SubtreeSize[/c] is how far back each one reaches.
///
/// The parser settles [c]ParameterCount[/c], [c]SubtreeSize[/c], [c]Field[/c] and [c]Constant[/c],
/// and settles [c]Row[/c], [c]Length[/c] and [c]Immediate[/c] for as much as parse time can know.
/// Overload resolution then finishes the row and the length, and it does so by rewriting this node
/// rather than by adding one, so the count and the shape recorded here are final.
///
/// Governing specification: EXPRESSIONS.md sections 2.1 and 6.
/// </summary>
internal readonly struct ExpressionNode
{
    /// <summary>
    /// Builds a node.
    /// </summary>
    /// <param name="row">The function table row this node evaluates.</param>
    /// <param name="parameterCount">How many operands it takes.</param>
    /// <param name="subtreeSize">How many nodes this node and its operands occupy.</param>
    /// <param name="length">The declared length of the value it produces, where that is known yet.</param>
    /// <param name="immediate">The number parse time folded into this node, where it folded one.</param>
    /// <param name="field">The field this node loads, or null where it loads none.</param>
    /// <param name="constant">The literal this node loads, or null where it loads none.</param>
    /// <param name="type">The type of the value it produces, once overload resolution has said.</param>
    /// <param name="immediate2">The second folded number, which only the conditional uses.</param>
    public ExpressionNode(
        int row,
        int parameterCount,
        int subtreeSize = 1,
        int length = 0,
        int immediate = 0,
        FieldReference? field = null,
        ExpressionConstant? constant = null,
        ExpressionType type = ExpressionType.None,
        int immediate2 = 0)
    {
        Row = row;
        ParameterCount = parameterCount;
        SubtreeSize = subtreeSize;
        Length = length;
        Immediate = immediate;
        Field = field;
        Constant = constant;
        Type = type;
        Immediate2 = immediate2;
    }

    /// <summary>
    /// Gets the function table row this node evaluates.
    /// </summary>
    /// <value>
    /// The row and not the name, because a row is one overload and the choice between a name's
    /// overloads is the whole of overload resolution. The query optimizer classifies an operator by
    /// which band of row indexes it falls in, which is why the table is transcribed in row order.
    /// </value>
    public int Row { get; }

    /// <summary>
    /// Gets how many operands this node takes.
    /// </summary>
    /// <value>
    /// The actual count, not the row's declared one, because logical and and logical or are variadic:
    /// a run of the same operator is one node with as many operands as the run had terms.
    /// </value>
    public int ParameterCount { get; }

    /// <summary>
    /// Gets how many nodes this node and its operands occupy together.
    /// </summary>
    /// <value>
    /// One for a leaf, and one plus the sizes of its operands otherwise. This is what lets a
    /// subexpression be addressed by the index of its root, which is how a constant subtree is
    /// evaluated on its own and how the optimizer compares a subexpression against a tag.
    /// </value>
    public int SubtreeSize { get; }

    /// <summary>
    /// Gets the declared length of the value this node produces.
    /// </summary>
    /// <value>
    /// Declared, not measured: it is the width the compiled expression reserves, not the width a
    /// particular value happens to occupy. The distinction decides how a short character constant
    /// compares against a longer key, so the two are never conflated. Zero on the nodes whose length
    /// only overload resolution can work out.
    /// </value>
    public int Length { get; }

    /// <summary>
    /// Gets the number parse time folded into this node.
    /// </summary>
    /// <value>
    /// The reference implementation's [c]i1[/c], which is one integer used for one purpose per row:
    /// the decimals of a formatted number, the offset a substring starts at, the count a pad reaches,
    /// the time of day of a timestamp. It is set here only where the parser folds a constant
    /// parameter away; the rows that use it for something overload resolution works out get it later.
    /// </value>
    public int Immediate { get; }

    /// <summary>
    /// Gets the field this node loads, or null where it loads none.
    /// </summary>
    public FieldReference? Field { get; }

    /// <summary>
    /// Gets the literal this node loads, or null where it loads none.
    /// </summary>
    public ExpressionConstant? Constant { get; }

    /// <summary>
    /// Gets the type of the value this node produces.
    /// </summary>
    /// <value>
    /// None until overload resolution has run, because the parser cannot know it: a name's overloads
    /// differ by parameter type, and which one a call resolves to is what decides the result type.
    /// </value>
    public ExpressionType Type { get; }

    /// <summary>
    /// Gets the second number parse time or overload resolution folded into this node.
    /// </summary>
    /// <value>
    /// The reference implementation's [c]i2[/c], and only the conditional uses it at compile time,
    /// for the length of the branch not taken. Zero everywhere else.
    /// </value>
    public int Immediate2 { get; }

    /// <summary>
    /// Gets the position of the field this node loads, counted from one, or zero where it loads none.
    /// </summary>
    public int FieldNumber => Field?.Number ?? 0;

    /// <summary>
    /// Returns this node reading a different row of the function table.
    /// </summary>
    /// <param name="row">The row to read instead.</param>
    /// <returns>The rewritten node.</returns>
    /// <value>
    /// This is what an implicit promotion is, and the whole of what it is: the producer node's row is
    /// replaced by the row that produces the wanted type, and [b]nothing else about the node changes[/b]
    /// -- not its declared length, not its operand count, and above all not the node count
    /// (e4parse.c:227-289). A promotion that wrapped the producer in a conversion node would lengthen
    /// the array, and both the resolution gate and the query optimizer's tag matching count nodes.
    /// </value>
    public ExpressionNode WithRow(int row) =>
        new(row, ParameterCount, SubtreeSize, Length, Immediate, Field, Constant, Type, Immediate2);
}
