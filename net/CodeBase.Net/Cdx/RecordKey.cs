using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;

namespace CodeBase.Net.Cdx;

/// <summary>
/// Where the value behind a tag's key comes from, for one record.
///
/// Deriving a key is two steps — find the value, then weigh it — and only the first depends on what
/// the key expression says. The value is handed over [b]typed[/b] rather than as stored bytes,
/// because an expression result has no field to look its interpretation up from: the bytes of
/// [c]UPPER(NAME)+STR(ID)[/c] are not any field's bytes, and the same eight bytes mean a different
/// key depending on whether they are a number, a date or text.
/// </summary>
internal interface IKeyValueSource
{
    /// <summary>
    /// Returns the value the key is built from, for a record.
    /// </summary>
    /// <param name="record">The record to read.</param>
    /// <param name="recordNumber">The number of the record the buffer holds.</param>
    /// <returns>The value.</returns>
    ExpressionValue Read(RecordBuffer record, int recordNumber);
}

/// <summary>
/// The value behind a key, evaluated from the tag's compiled expression.
///
/// One implementation serves every tag, a bare field name included, because a bare field name is an
/// expression the engine compiles to a single loader node. Keeping a separate path for it would mean
/// two ways of reading the same thing and two places for them to disagree.
/// </summary>
internal sealed class ExpressionValueSource : IKeyValueSource
{
    private readonly ExpressionEvaluator evaluator;
    private readonly EvaluationContext context;

    /// <summary>
    /// Initializes a new instance over a compiled expression.
    /// </summary>
    /// <param name="compiled">The tag's compiled key expression.</param>
    /// <param name="context">
    /// What the evaluation needs besides the record. Its record is replaced per call, so one of
    /// these serves a whole scan.
    /// </param>
    public ExpressionValueSource(CompiledExpression compiled, EvaluationContext context)
    {
        evaluator = new ExpressionEvaluator(compiled);
        this.context = context;
    }

    /// <inheritdoc/>
    /// <value>
    /// The deletion flag and the record number come with the record rather than from the context,
    /// because they are the record-dependent things a key may legally read: each is the same answer
    /// every time for the same record, which is what a stored key needs. The reference sets the
    /// number before it builds each key (R4REINDX.C:391), so a RECNO() key is the record's own.
    /// </value>
    public ExpressionValue Read(RecordBuffer record, int recordNumber) =>
        evaluator.Evaluate(context with { Record = record, Deleted = record.Deleted, RecordNumber = recordNumber });
}

/// <summary>
/// Rebuilds the key a tag stored for a record.
///
/// The reverse of a seek, and needed for the same reason the C library needs it: after moving by
/// record number, the tag has to be told where that record sits before it can step from there. The
/// reference re-derives the record's key and seeks it (d4seekSynchToCurrentPos, D4SEEK.C:1141), and
/// so does this — which turns a walk of the whole tag into a descent.
///
/// This is the [i]build[/i] form of a key, not the seek form. For a collated tag the two genuinely
/// differ: a partial seek drops the tail weights and cuts the key back to its heads, while a stored
/// key always carries both halves. Reconstructing an existing key needs the whole thing.
/// </summary>
internal static class RecordKey
{
    /// <summary>
    /// Writes the key a tag holds for a record.
    /// </summary>
    /// <param name="converter">The tag's resolved converter.</param>
    /// <param name="source">Where the value comes from.</param>
    /// <param name="record">The record to read.</param>
    /// <param name="recordNumber">The number of the record the buffer holds.</param>
    /// <param name="destination">Where to write, at least the tag's key length.</param>
    /// <returns>How many bytes were written.</returns>
    /// <exception cref="CodeBaseException">The stored value cannot be read as its declared type.</exception>
    public static int Write(
        SeekConverter converter,
        IKeyValueSource source,
        RecordBuffer record,
        int recordNumber,
        Span<byte> destination) =>
        WriteValue(converter, source.Read(record, recordNumber), destination);

    /// <summary>
    /// Writes the key a tag holds for an evaluated value.
    /// </summary>
    /// <param name="converter">The tag's resolved converter.</param>
    /// <param name="value">The value the tag's expression produced.</param>
    /// <param name="destination">Where to write, at least the tag's key length.</param>
    /// <returns>How many bytes were written.</returns>
    /// <value>
    /// The build form, for every tag. The value can be absent, which spends a leading byte; a trimmed
    /// result carries zero bytes where its trailing characters were, which become blanks here; and
    /// what transform to use comes from the value's own type rather than from a field's declared one.
    /// </value>
    public static int WriteValue(
        SeekConverter converter, ExpressionValue value, Span<byte> destination)
    {
        Span<byte> key = destination[..converter.KeyLength];

        if (converter.HasNullIndicator)
        {
            if (value.IsNull)
            {
                key.Clear();
                return key.Length;
            }

            key[0] = 0x80;
            key = key[1..];
        }

        if (converter.Kind == KeyKind.CollatedCharacter)
        {
            return CollatedKey.Write(
                value.Bytes,
                CollationWeights.TableFor(converter.Collation, converter.CodePage),
                CollationWeights.ExpansionsFor(converter.Collation, converter.CodePage),
                includeTails: true,
                key);
        }

        return ExpressionKey.Write(
            value, converter.Compiled.HasTrim, withIndicator: false, key)
            + (converter.HasNullIndicator ? 1 : 0);
    }
}
