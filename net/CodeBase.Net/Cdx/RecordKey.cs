using System.Buffers.Binary;
using CodeBase.Net.Dbf;

namespace CodeBase.Net.Cdx;

/// <summary>
/// Where the value behind a tag's key comes from, for one record.
///
/// This is the seam the expression engine plugs into, and the only one it needs. Deriving a key is
/// two steps — find the value, then weigh it — and only the first depends on what the key expression
/// says. Today the expression is always a bare field name, so the value is the field's stored bytes;
/// step 008 adds an implementation that evaluates an expression instead, and nothing above this
/// interface changes.
///
/// The value is handed over in its **stored** form rather than as a decoded number, because that is
/// the form the transforms take and the form an expression result arrives in too.
/// </summary>
internal interface IKeyValueSource
{
    /// <summary>
    /// Returns the bytes the key is built from, for a record.
    /// </summary>
    /// <param name="record">The record to read.</param>
    /// <returns>The value's stored bytes.</returns>
    ReadOnlySpan<byte> Read(RecordBuffer record);
}

/// <summary>
/// The value behind a key when the key expression is a bare field name.
/// </summary>
/// <remarks>
/// The whole implementation, because a bare field reference is not a computation: the value is the
/// bytes already sitting in the record.
/// </remarks>
internal sealed class FieldValueSource : IKeyValueSource
{
    private readonly FieldDefinition field;

    /// <summary>
    /// Initializes a new instance reading one field.
    /// </summary>
    /// <param name="field">The field the tag is built on.</param>
    public FieldValueSource(FieldDefinition field) => this.field = field;

    /// <inheritdoc/>
    public ReadOnlySpan<byte> Read(RecordBuffer record) => record.Field(field);
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
