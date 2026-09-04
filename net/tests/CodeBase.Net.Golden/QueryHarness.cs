using CodeBase.Net.Expressions;
using CodeBase.Net.Query;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// One corpus table, its tags projected once, and a filter compiler over its fields.
///
/// Shared by the query gates so they all decompose a filter the same way. Opening the table and
/// projecting its tags is done once per harness rather than per filter, because the projection is
/// what a real caller does once when a query is planned.
/// </summary>
internal sealed class QueryHarness
{
    private readonly IFieldMetadata fields;
    private readonly IReadOnlyList<QueryTag> tags;
    private readonly QueryTagScope scope;

    private QueryHarness(
        CorpusQueryDump dump, IFieldMetadata fields, IReadOnlyList<QueryTag> tags, QueryTagScope scope)
    {
        Dump = dump;
        this.fields = fields;
        this.tags = tags;
        this.scope = scope;
    }

    /// <summary>Gets the reference's own answers for this table.</summary>
    public CorpusQueryDump Dump { get; }

    /// <summary>
    /// Opens a corpus table and takes what planning a filter over it needs.
    /// </summary>
    /// <param name="table">The table's name, without extension.</param>
    /// <param name="scope">Which tags the optimizer may use.</param>
    /// <returns>The harness.</returns>
    public static QueryHarness For(string table, QueryTagScope scope = QueryTagScope.Compatible)
    {
        using var engine = new CodeBaseEngine();
        using Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));

        return new QueryHarness(
            CorpusQueryDump.Load(table),
            new TableFieldMetadata([.. opened.Fields], table),
            opened.QueryTags(),
            scope);
    }

    /// <summary>Compiles a filter against the table's fields.</summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>The compiled filter.</returns>
    public CompiledExpression Compile(string filter) =>
        TypeResolver.Resolve(ExpressionParser.Parse(filter, fields));

    /// <summary>Works out which tag answers each node of a filter.</summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>One entry per node.</returns>
    public QueryTag?[] Match(string filter) =>
        new QueryDecomposer(tags, scope).MatchTags(Compile(filter));

    /// <summary>Decomposes a filter.</summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>The plan.</returns>
    public QueryPlan Plan(string filter) =>
        new QueryDecomposer(tags, scope).Decompose(Compile(filter));

    /// <summary>
    /// Decomposes a filter that must yield exactly one leaf, and returns it.
    /// </summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>The single leaf.</returns>
    /// <exception cref="InvalidOperationException">The filter yielded none, or more than one.</exception>
    public ConstraintNode OnlyLeaf(string filter)
    {
        QueryPlan plan = Plan(filter);

        return plan.Root is null
            ? throw new InvalidOperationException($"'{filter}' decomposed to nothing.")
            : plan.Root.Walk().Single(node => node.IsLeaf);
    }

    /// <summary>
    /// Builds a numeric value to test a constraint against, at the width a numeric constant has.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static ConstraintValue NumericValue(double value) =>
        ConstraintValue.From(ExpressionValue.Numeric(value), 8);
}
