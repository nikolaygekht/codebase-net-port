using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// One corpus table, its tags projected once, and its own compiler for a filter.
///
/// Shared by the query gates so they all decompose a filter the same way. Opening the table and
/// projecting its tags is done once per harness rather than per filter, because the projection is
/// what a real caller does once when a query is planned.
/// </summary>
internal sealed class QueryHarness : IDisposable
{
    private readonly CodeBaseEngine engine;
    private readonly Table table;
    private readonly IReadOnlyList<QueryTag> tags;
    private readonly QueryTagScope scope;

    private readonly CorpusQueryDump? dump;

    private QueryHarness(
        CodeBaseEngine engine,
        Table table,
        CorpusQueryDump? dump,
        IReadOnlyList<QueryTag> tags,
        QueryTagScope scope)
    {
        this.engine = engine;
        this.table = table;
        this.dump = dump;
        this.tags = tags;
        this.scope = scope;
    }

    /// <summary>Gets the reference's own answers for this table.</summary>
    /// <value>
    /// Absent for a table the generator records no filters for, which is legitimate: a check whose
    /// expectation is a scan rather than a recorded answer needs no dump at all.
    /// </value>
    public CorpusQueryDump Dump => dump ?? throw new InvalidOperationException(
        "This table has no recorded query answers; only checks computing their own expectation apply.");

    /// <summary>
    /// Opens a corpus table and takes what planning a filter over it needs.
    /// </summary>
    /// <param name="table">The table's name, without extension.</param>
    /// <param name="scope">Which tags the optimizer may use.</param>
    /// <returns>The harness.</returns>
    public static QueryHarness For(string table, QueryTagScope scope = QueryTagScope.Compatible)
    {
        var engine = new CodeBaseEngine();
        Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));

        return new QueryHarness(
            engine,
            opened,
            File.Exists(Path.Combine(Corpus.Root, table + ".query.dump.txt"))
                ? CorpusQueryDump.Load(table)
                : null,
            opened.QueryTags(),
            scope);
    }

    /// <summary>Compiles a filter against the table's fields.</summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>The compiled filter.</returns>
    /// <value>
    /// Through the table rather than by assembling the parser and the resolver here, so that these
    /// checks compile a filter exactly as a caller's query does. A harness with its own copy of that
    /// path is how a suite stays green while the real one is broken -- which is what happened when
    /// the table learned to pass its own name to the scanner and this did not.
    /// </value>
    public CompiledExpression Compile(string filter) => table.CompileFilter(filter);

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

    /// <summary>Works out which records a filter's plan reaches.</summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>The records.</returns>
    public RecordSet Reach(string filter) =>
        new QueryExecutor(tag => table.OpenProbe(tag), table.RecordCount).Evaluate(Plan(filter));

    /// <summary>Builds the residual filter a plan leaves behind.</summary>
    /// <param name="filter">The filter text.</param>
    /// <returns>The residual filter.</returns>
    public ResidualFilter Residual(string filter)
    {
        CompiledExpression compiled = Compile(filter);

        return new ResidualFilter(
            compiled, new QueryDecomposer(tags, scope).Decompose(compiled));
    }

    /// <summary>Gets how many records the table holds.</summary>
    public int RecordCount => table.RecordCount;

    /// <summary>Opens a probe over one of the table's tags.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The probe.</returns>
    public ITagProbe Probe(in QueryTag tag) => table.OpenProbe(tag);

    /// <summary>Closes the table.</summary>
    public void Dispose()
    {
        table.Dispose();
        engine.Dispose();
    }

    /// <summary>
    /// Builds a numeric value to test a constraint against, at the width a numeric constant has.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static ConstraintValue NumericValue(double value) =>
        ConstraintValue.From(ExpressionValue.Numeric(value), 8);
}
