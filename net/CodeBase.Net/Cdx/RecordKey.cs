using System.Buffers.Binary;
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
    /// <returns>The value.</returns>
    ExpressionValue Read(RecordBuffer record);
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
    /// The deletion flag comes off the record rather than from the context, because it is the one
    /// record-dependent thing a key may legally read: it is the same answer every time for the same
    /// record, which is what a stored key needs.
    /// </value>
    public ExpressionValue Read(RecordBuffer record) =>
        evaluator.Evaluate(context with { Record = record, Deleted = record.Deleted });
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
    /// <param name="destination">Where to write, at least the tag's key length.</param>
    /// <returns>How many bytes were written.</returns>
    /// <exception cref="CodeBaseException">The stored value cannot be read as its declared type.</exception>
    public static int Write(
        SeekConverter converter, IKeyValueSource source, RecordBuffer record, Span<byte> destination) =>
        WriteValue(converter, source.Read(record), destination);

    /// <summary>
    /// Writes the key a tag holds for an evaluated value.
    /// </summary>
    /// <param name="converter">The tag's resolved converter.</param>
    /// <param name="value">The value the tag's expression produced.</param>
    /// <param name="destination">Where to write, at least the tag's key length.</param>
    /// <returns>How many bytes were written.</returns>
    /// <value>
    /// The build form, and the one an expression tag needs. It differs from the byte form below in
    /// three ways, and each is something a bare field never had: the value can be absent, which
    /// spends a leading byte; a trimmed result carries zero bytes where its trailing characters were,
    /// which become blanks here; and what transform to use comes from the value's own type rather
    /// than from a field's declared one.
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

    /// <summary>
    /// Writes the key a tag holds for a value already in its stored form.
    /// </summary>
    /// <param name="converter">The tag's resolved converter.</param>
    /// <param name="value">The value's stored bytes.</param>
    /// <param name="destination">Where to write, at least the tag's key length.</param>
    /// <returns>How many bytes were written.</returns>
    /// <exception cref="CodeBaseException">The value cannot be read as the key's declared type.</exception>
    /// <remarks>
    /// Split out from the record form because it is where the key actually gets built, and because
    /// it can be compared against a stored key without a table open around it.
    /// </remarks>
    public static int WriteValue(
        SeekConverter converter, ReadOnlySpan<byte> value, Span<byte> destination)
    {
        Span<byte> key = destination[..converter.KeyLength];

        switch (converter.Kind)
        {
            case KeyKind.Character:
                // Machine order keeps the bytes, cut or padded to the tag's width.
                int copied = Math.Min(value.Length, key.Length);
                value[..copied].CopyTo(key);
                key[copied..].Fill(converter.PadByte);
                return key.Length;

            case KeyKind.CollatedCharacter:
                return CollatedKey.Write(
                    value,
                    CollationWeights.TableFor(converter.Collation, converter.CodePage),
                    CollationWeights.ExpansionsFor(converter.Collation, converter.CodePage),
                    includeTails: true,
                    key);

            case KeyKind.Double:
                return KeyTransform.FromDouble(DoubleOf(converter, value), key);

            case KeyKind.Int32:
                return KeyTransform.FromInt32(BinaryPrimitives.ReadInt32LittleEndian(value), key);

            case KeyKind.Currency:
                return KeyTransform.FromInt64(BinaryPrimitives.ReadInt64LittleEndian(value), key);

            case KeyKind.Date:
                return KeyTransform.FromDouble(FoxDate.ToJulian(value), key);

            default:
                return KeyTransform.FromDateTime(
                    BinaryPrimitives.ReadInt32LittleEndian(value),
                    BinaryPrimitives.ReadInt32LittleEndian(value[4..]),
                    key);
        }
    }

    /// <summary>
    /// Reads the double behind a numeric, float or true-double field.
    /// </summary>
    /// <remarks>
    /// A numeric or float field holds its value as text and a double field holds it as bytes, and
    /// both key through the same transform (KEY-COLLATION.md section 2.11).
    /// </remarks>
    private static double DoubleOf(SeekConverter converter, ReadOnlySpan<byte> value) =>
        converter.Field!.Type == 'B'
            ? BitConverter.ToDouble(value)
            : FoxNumeric.ToDouble(value);
}
