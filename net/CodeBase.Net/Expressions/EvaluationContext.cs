using CodeBase.Net.Dbf;

namespace CodeBase.Net.Expressions;

/// <summary>
/// Everything an evaluation needs that the compiled expression does not carry.
///
/// A compiled expression is built once per tag and is immutable; what changes between evaluations is
/// the record, where the cursor is, and how many records there are. Those are here rather than on the
/// evaluator, so that one evaluator can answer for many records and nothing is left over from the
/// last one.
///
/// The record is optional. A subexpression made only of literals has nothing to read, and the query
/// optimizer evaluates exactly those on their own, so a null record is a supported case and not a
/// mistake to guard against.
///
/// Governing specification: EXPRESSIONS.md section 7.
/// </summary>
internal sealed record EvaluationContext
{
    /// <summary>
    /// Gets the record the expression's fields are read out of, or null where it has none to read.
    /// </summary>
    public RecordBuffer? Record { get; init; }

    /// <summary>
    /// Gets the field the table's null markings live in, or null where the table has none.
    /// </summary>
    public FieldDefinition? NullFlags { get; init; }

    /// <summary>
    /// Gets the record number the cursor is on.
    /// </summary>
    public int RecordNumber { get; init; }

    /// <summary>
    /// Gets a value indicating whether the record is marked deleted.
    /// </summary>
    public bool Deleted { get; init; }

    /// <summary>
    /// Gets how many records the table holds.
    /// </summary>
    public int RecordCount { get; init; }

    /// <summary>
    /// Gets the date picture and century policy.
    /// </summary>
    public ExpressionOptions Options { get; init; } = ExpressionOptions.Invariant;

    /// <summary>
    /// Gets today's date, as the julian day a clock read would produce.
    /// </summary>
    /// <value>
    /// Supplied rather than read, for the reason ADR-42 gives about the century: a value that comes
    /// off the host's clock is a value that changes between two runs of the same query. Zero means no
    /// clock, and the two functions that need one answer a blank date.
    /// </value>
    public long Today { get; init; }

    /// <summary>
    /// Gets the time of day, as milliseconds since midnight, for the same reason as the date.
    /// </summary>
    public int TimeOfDay { get; init; }

    /// <summary>
    /// Gets the reader that returns a memo field's contents, where one is available.
    /// </summary>
    /// <value>
    /// A delegate rather than an interface, because one caller supplies it and one kernel reads it.
    /// Without it a memo operand evaluates to blanks, which is what a memo field with no block
    /// holds anyway.
    /// </value>
    public Func<FieldDefinition, ReadOnlyMemory<byte>>? Memo { get; init; }
}
