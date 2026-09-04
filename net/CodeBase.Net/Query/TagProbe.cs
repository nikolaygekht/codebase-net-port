using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;

namespace CodeBase.Net.Query;

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
    private readonly byte[] buffer;

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
        buffer = new byte[converter.KeyLength];
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
    /// </value>
    public KeySearch KeyFor(ConstraintValue value)
    {
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

        int written = ExpressionKey.Write(
            value.Source, converter.Compiled.HasTrim, converter.HasNullIndicator, buffer);

        int length = converter.Kind == KeyKind.Character
            ? Math.Min(value.Length, written)
            : written;

        return KeySearch.Into(buffer, length, converter.KeyLength, converter.PadByte);
    }

    /// <inheritdoc/>
    public bool First() => descending ? cursor.Bottom() : cursor.Top();

    /// <inheritdoc/>
    public bool SeekAtOrAfter(KeySearch key) => cursor.SeekNearestByBytes(key);

    /// <inheritdoc/>
    public bool Step() => cursor.Skip(descending ? -1 : 1) != 0 && cursor.IsOnKey;
}
