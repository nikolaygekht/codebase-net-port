using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;

namespace CodeBase.Net.Queries;

/// <summary>
/// The keys of a real tag, in stored order.
///
/// It opens a cursor of its own rather than borrowing the table's. A query walks tags while a caller
/// may be part-way through a seek on the same tag, and sharing one cursor would have each quietly
/// move the other -- the reference has the same hazard and works around it by saving and restoring
/// the tag's traversal flag around every leaf. A second cursor costs one descent and removes the
/// question.
///
/// Everything else here is translation and none of it is a decision. The cursor underneath walks in
/// the tag's own order, which for a descending tag runs from the highest key down, so stepping up in
/// stored order means stepping it backwards and reaching the lowest key means going to its far end.
/// The byte-order seek is already in stored order and needs no adjustment.
/// </summary>
internal sealed class TagProbe : ITagProbe
{
    private readonly TagCursor cursor;
    private readonly SeekConverter converter;
    private readonly bool descending;

    /// <summary>
    /// Opens a probe over a tag.
    /// </summary>
    /// <param name="tag">The tag to walk.</param>
    /// <param name="converter">What turns a value into its key bytes.</param>
    public TagProbe(CdxTag tag, SeekConverter converter)
    {
        cursor = tag.OpenCursor();
        this.converter = converter;
        descending = tag.Descending;
    }

    /// <inheritdoc/>
    public int KeyLength => converter.KeyLength;

    /// <inheritdoc/>
    public IndexEntry Current => cursor.Current;

    /// <inheritdoc/>
    /// <value>
    /// The same transform that builds a key from a record, which is what the reference uses here too
    /// -- so a constant and the keys it is compared against are made the same way and cannot disagree
    /// about collation or about how a number becomes sortable bytes.
    ///
    /// A character constant shorter than the key stays short, and that is the whole of what makes it
    /// a prefix: comparing a stored key against it then compares only its own bytes, so it names the
    /// group of keys that begin with it rather than one key padded out to the width.
    ///
    /// Each answer gets its own bytes rather than a buffer reused between calls, because a caller
    /// holds more than one at a time: a range has two ends and any number of struck-out values, and
    /// all of them are compared against the same key as the walk passes it. Sharing a buffer would
    /// have each answer quietly become the last one asked for. It is built once per leaf, not once
    /// per record, so the allocation is not on any path that repeats.
    /// </value>
    public KeySearch KeyFor(ConstraintValue value)
    {
        byte[] buffer = new byte[converter.KeyLength];

        if (converter.Kind == KeyKind.CollatedCharacter && value.Length < converter.Compiled.Length)
        {
            int weighed = CollatedKey.WriteSearch(
                value.Bytes,
                CollationWeights.TableFor(converter.Collation, converter.CodePage),
                CollationWeights.ExpansionsFor(converter.Collation, converter.CodePage),
                converter.KeyLength,
                buffer);

            return KeySearch.Into(buffer, weighed, converter.KeyLength, converter.PadByte);
        }

        int indicator = converter.HasNullIndicator ? 1 : 0;
        int written = ExpressionKey.Write(
            InTheTagsDomain(value), converter.Compiled.HasTrim, converter.HasNullIndicator, buffer);

        // A character value shorter than the key stays short; a number is always its whole width.
        int length = converter.Kind is KeyKind.Character or KeyKind.CollatedCharacter
            ? Math.Min(value.Length + indicator, written)
            : written;

        return KeySearch.Into(buffer, length, converter.KeyLength, converter.PadByte);
    }

    /// <summary>
    /// Restates a constant as a value of the kind the tag's keys are made of.
    /// </summary>
    /// <value>
    /// A key is built from what a value [i]is[/i], so a constant has to arrive as the right kind of
    /// thing before it can become one. The two differ wherever a comparison promoted the field: an
    /// integer field compared against a number promotes to a double, so the constant arrives as a
    /// double while the tag's keys are four bytes of integer, and writing it as what it is would put
    /// eight bytes into four.
    ///
    /// This is exactly where the reference gives up instead. It refuses integer, currency and
    /// datetime tags outright and says why -- the constant "cannot be converted to an int which would
    /// be able to be looked up in the index file". Converting it is the whole of what lets this
    /// library use those tags, and refusing the constants that would not survive the conversion is
    /// what keeps that honest.
    /// </value>
    private ExpressionValue InTheTagsDomain(ConstraintValue value) => converter.Kind switch
    {
        KeyKind.Int32 => ExpressionValue.Whole((int)value.Number),
        KeyKind.Currency => ExpressionValue.Currency(ConstraintValue.AsCurrency(value.Number)),
        KeyKind.Single => ExpressionValue.Float(value.Number),
        KeyKind.Logical => ExpressionValue.Logic(value.Source.Logical),
        KeyKind.Double or KeyKind.Date => ExpressionValue.Numeric(value.Number),
        _ => value.Source,
    };

    /// <inheritdoc/>
    public bool First() => descending ? cursor.Bottom() : cursor.Top();

    /// <inheritdoc/>
    public bool SeekAtOrAfter(KeySearch key) => cursor.SeekNearestByBytes(key);

    /// <inheritdoc/>
    public bool Step() => cursor.Skip(descending ? -1 : 1) != 0 && cursor.IsOnKey;
}
