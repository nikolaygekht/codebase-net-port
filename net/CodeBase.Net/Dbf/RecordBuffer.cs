namespace CodeBase.Net.Dbf;

/// <summary>
/// The bytes of one record, and the only way to reach a field inside them.
///
/// Every decoder reads through here, so containment is a property of this type rather than a rule
/// each decoder has to remember. A field's bytes are handed out as a span that is checked against
/// the record's own length: a descriptor that would reach past the end of the record is refused,
/// not truncated, because a truncated field decodes into a plausible wrong value.
///
/// The check is against the record and not against the field, deliberately. Several fixed-width
/// types read their natural width and ignore the declared length, so a short descriptor makes a
/// decoder read into the field that follows. That is what the C library does, and it stays inside
/// the record; only leaving the record is an error.
/// </summary>
internal sealed class RecordBuffer
{
    /// <summary>
    /// The byte a record's first position holds when the record is not deleted.
    /// </summary>
    private const byte NotDeleted = (byte)' ';

    private readonly byte[] bytes;

    /// <summary>
    /// Creates a buffer for records of the given width.
    /// </summary>
    /// <param name="recordLength">Record width in bytes, including the leading deletion flag.</param>
    /// <exception cref="ArgumentOutOfRangeException">The width is not positive.</exception>
    public RecordBuffer(int recordLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordLength, 1);
        bytes = new byte[recordLength];
    }

    /// <summary>Gets the record width in bytes, including the leading deletion flag.</summary>
    public int Length => bytes.Length;

    /// <summary>
    /// Gets a value indicating whether the record is flagged as deleted.
    /// </summary>
    /// <value>
    /// True for any first byte other than a space, which is what [c]d4deleted[/c] reports
    /// (d4data.c:344). A file carrying some third value there reads as deleted, matching the
    /// reference rather than second-guessing it.
    /// </value>
    public bool Deleted => bytes[0] != NotDeleted;

    /// <summary>
    /// Gets the whole record for a reader to fill.
    /// </summary>
    internal Span<byte> Raw => bytes;

    /// <summary>
    /// Gets the bytes of a field.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>That field's bytes, as they sit in the record.</returns>
    /// <exception cref="CodeBaseException">The field does not lie inside the record.</exception>
    public ReadOnlySpan<byte> Field(FieldDefinition field) =>
        Slice(field.RecordOffset, field.Length, field.Name);

    /// <summary>
    /// Gets a run of bytes from inside the record.
    /// </summary>
    /// <param name="offset">Where the run starts, counting the deletion flag as zero.</param>
    /// <param name="length">How many bytes to take.</param>
    /// <param name="what">What is being read, for the message if it does not fit.</param>
    /// <returns>Exactly those bytes.</returns>
    /// <exception cref="CodeBaseException">The run does not lie inside the record.</exception>
    public ReadOnlySpan<byte> Slice(int offset, int length, string what)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"Reading {what} wants {length} bytes at offset {offset} of a record {bytes.Length} " +
                "bytes wide. The field does not lie inside its record, so the descriptors and the " +
                "record length disagree.");
        }

        return bytes.AsSpan(offset, length);
    }

    /// <summary>
    /// Replaces the record with a prepared blank one.
    /// </summary>
    /// <param name="blank">The blank record, which must be the same width.</param>
    /// <exception cref="ArgumentException">The blank record is a different width.</exception>
    public void Blank(ReadOnlySpan<byte> blank)
    {
        if (blank.Length != bytes.Length)
        {
            throw new ArgumentException(
                $"A blank record {blank.Length} bytes wide cannot fill a record {bytes.Length} " +
                "bytes wide.",
                nameof(blank));
        }

        blank.CopyTo(bytes);
    }
}
