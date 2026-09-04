using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;

namespace CodeBase.Net;

/// <summary>
/// A filter over a table, answered by seeking its indexes rather than by reading every record.
///
/// The filter is decomposed once, when the query is first moved: each term that some tag's key
/// expression can answer becomes a seek, the record numbers those seeks reach are combined, and only
/// the terms no tag could answer are evaluated -- on the candidates that survive, not on the table.
/// A filter nothing can be seeked for still works and simply reads every record, which is what a
/// table scan is.
///
/// The order records come out in is the table's: its selected tag if it has one, and record number if
/// it does not. Those are genuinely different orders and neither is a detail, so a caller chooses by
/// selecting a tag before moving the query.
///
/// A query moves the table's cursor, so it obeys the same rule every other operation on a table does:
/// one at a time, from one thread.
/// </summary>
public sealed class Query
{
    private readonly Table table;
    private readonly QueryTagScope scope;

    private CompiledExpression? compiled;
    private QueryPlan? plan;
    private RecordSet? candidates;
    private ResidualFilter? residual;

    /// <summary>
    /// Builds a query over a table.
    /// </summary>
    /// <param name="table">The table to filter.</param>
    /// <param name="filter">The filter expression.</param>
    /// <param name="scope">Which of the table's tags may answer it.</param>
    internal Query(Table table, string filter, QueryTagScope scope)
    {
        this.table = table;
        this.scope = scope;
        Filter = filter;
    }

    /// <summary>
    /// Gets the filter expression the query was built from.
    /// </summary>
    public string Filter { get; }

    /// <summary>
    /// Gets the record the query is on, or zero when it is on none.
    /// </summary>
    public int RecordNumber => Eof || Bof ? 0 : table.RecordNumber;

    /// <summary>
    /// Gets a value indicating whether the query has run off its end.
    /// </summary>
    public bool Eof { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the query has run off its beginning.
    /// </summary>
    public bool Bof { get; private set; }

    /// <summary>
    /// Gets how many records match the filter.
    /// </summary>
    /// <value>
    /// Where every term was answered by a tag, this is the size of the record set and no record is
    /// read to produce it -- which is the sharpest thing the optimizer buys and the reason the
    /// distinction is tracked at all. Otherwise the candidates are walked and counted, which still
    /// reads only the candidates.
    /// </value>
    /// <exception cref="CodeBaseException">The filter is not a valid logical expression.</exception>
    public int Count
    {
        get
        {
            Prepare();

            if (!residual!.HasWork)
                return candidates!.Count;

            int found = 0;

            for (int record = candidates!.NextFrom(1); record != 0; record = candidates.NextFrom(record + 1))
            {
                if (table.Go(record) == GoResult.Ok && Admits())
                    found++;
            }

            return found;
        }
    }

    /// <summary>
    /// Moves to the first record that matches.
    /// </summary>
    /// <returns>Ok with the query on it, or NoRecord when nothing matches.</returns>
    /// <exception cref="CodeBaseException">The filter is not a valid logical expression.</exception>
    public GoResult Top()
    {
        Prepare();
        Bof = false;
        Eof = false;

        return Settle(SelectedTag is null ? StepByRecord(0, forwards: true) : StepByTag(null, forwards: true), forwards: true);
    }

    /// <summary>
    /// Moves to the last record that matches.
    /// </summary>
    /// <returns>Ok with the query on it, or NoRecord when nothing matches.</returns>
    /// <exception cref="CodeBaseException">The filter is not a valid logical expression.</exception>
    public GoResult Bottom()
    {
        Prepare();
        Bof = false;
        Eof = false;

        return Settle(
            SelectedTag is null ? StepByRecord(table.RecordCount + 1, forwards: false) : StepByTag(null, forwards: false),
            forwards: false);
    }

    /// <summary>
    /// Moves to the next record that matches.
    /// </summary>
    /// <returns>Ok with the query on it, or NoRecord when there is none.</returns>
    public GoResult Next() => Move(forwards: true);

    /// <summary>
    /// Moves to the previous record that matches.
    /// </summary>
    /// <returns>Ok with the query on it, or NoRecord when there is none.</returns>
    public GoResult Previous() => Move(forwards: false);

    /// <summary>
    /// Moves by a number of matching records.
    /// </summary>
    /// <param name="count">How far to move. Negative moves backwards; zero stays put.</param>
    /// <returns>Whether it moved that far, and which end it stopped at if it did not.</returns>
    public SkipResult Skip(int count)
    {
        if (count == 0)
            return SkipResult.Moved;

        for (int step = 0; step < Math.Abs(count); step++)
        {
            if (Move(count > 0) != GoResult.Ok)
                return count > 0 ? SkipResult.Eof : SkipResult.Bof;
        }

        return SkipResult.Moved;
    }

    private Tag? SelectedTag => table.SelectedTag;

    /// <summary>
    /// Compiles and decomposes the filter, and builds the record set, once.
    /// </summary>
    /// <value>
    /// Eagerly and at the first movement rather than when the query was made, which is what the
    /// reference does and what makes a count answerable without reading a record. Seeking lazily per
    /// step would seek a tag once per record and lose the point.
    /// </value>
    private void Prepare()
    {
        if (plan is not null)
            return;

        compiled = table.CompileFilter(Filter);

        if (compiled.ResultType != ExpressionType.Logical)
        {
            throw new CodeBaseException(
                ErrorCode.Relate,
                $"A query filter must be a logical expression; '{Filter}' produces {compiled.ResultType}.");
        }

        plan = new QueryDecomposer(table.QueryTags(), scope, table.ExpressionOptions).Decompose(compiled);
        candidates = new QueryExecutor(tag => table.OpenProbe(tag), table.RecordCount).Evaluate(plan);
        residual = new ResidualFilter(compiled, plan);
    }

    /// <summary>
    /// Moves one matching record in either direction, from wherever the query is.
    /// </summary>
    private GoResult Move(bool forwards)
    {
        Prepare();

        if (forwards ? Eof : Bof)
            return GoResult.NoRecord;

        int from = Bof || Eof ? (forwards ? 0 : table.RecordCount + 1) : table.RecordNumber;

        return Settle(
            SelectedTag is null ? StepByRecord(from, forwards) : StepByTag(table.RecordNumber, forwards),
            forwards);
    }

    /// <summary>
    /// Walks the record set itself, which is where the speed is.
    /// </summary>
    /// <value>
    /// A run of records the set does not hold is passed over without reading any of them, so a filter
    /// matching three records out of ten thousand reads three. Results come out in record-number
    /// order, which is what a table with no selected tag means by order.
    /// </value>
    private bool StepByRecord(int from, bool forwards)
    {
        if (forwards)
        {
            for (int record = candidates!.NextFrom(from + 1); record != 0; record = candidates.NextFrom(record + 1))
            {
                if (table.Go(record) == GoResult.Ok && Admits())
                    return true;
            }

            return false;
        }

        for (int record = Math.Min(from - 1, table.RecordCount); record >= 1; record--)
        {
            if (candidates!.Contains(record) && table.Go(record) == GoResult.Ok && Admits())
                return true;
        }

        return false;
    }

    /// <summary>
    /// Walks the selected tag, taking the records the set holds.
    /// </summary>
    /// <value>
    /// Slower than walking the set, because every entry of the tag is stepped over whether its record
    /// is wanted or not -- and the only way to get the results in the tag's order, which is the order
    /// a caller asked for by selecting it. Both paths exist because they answer different questions.
    /// </value>
    private bool StepByTag(int? from, bool forwards)
    {
        Tag tag = SelectedTag!;
        GoResult go = from is null
            ? (forwards ? table.GoFirstIndexed(tag) : table.GoLastIndexed(tag))
            : (forwards ? table.GoNextIndexed(tag) : table.GoPreviousIndexed(tag));

        while (go == GoResult.Ok)
        {
            if (candidates!.Contains(table.RecordNumber) && Admits())
                return true;

            go = forwards ? table.GoNextIndexed(tag) : table.GoPreviousIndexed(tag);
        }

        return false;
    }

    /// <summary>
    /// Records which end the query stopped at, when it stopped.
    /// </summary>
    private GoResult Settle(bool found, bool forwards)
    {
        if (found)
            return GoResult.Ok;

        if (forwards)
            Eof = true;
        else
            Bof = true;

        return GoResult.NoRecord;
    }

    /// <summary>
    /// Checks the terms no tag answered against the record the table is on.
    /// </summary>
    private bool Admits() => !residual!.HasWork || residual.Admits(table.EvaluationContext());
}
