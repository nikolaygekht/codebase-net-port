namespace CodeBase.Net.Dbf;

/// <summary>
/// The blank record a table hands back when its cursor is not on a real one.
///
/// Built once when a table opens and copied whenever a record has to be blanked, which is what the
/// C library does with its own prepared blank (D4OPEN.C:440-457, d4data.c:198-205). Blanking is
/// per-field rather than a single fill, because a blank is not one byte for every type: the fixed
/// binary types blank to zero and everything else blanks to spaces (f4blank, F4FIELD.C:135-169).
///
/// This is what makes reading at end of file safe. The cursor sits one past the last record with no
/// bytes behind it, and a reader that left the previous record in the buffer would answer with the
/// most plausible wrong data available.
/// </summary>
internal static class BlankRecord
{
    /// <summary>
    /// Types whose blank value is zero bytes rather than spaces.
    /// </summary>
    /// <value>
    /// The fixed-width binary types, taken from [c]f4blank[/c]'s own list. Note that a plain memo
    /// and a general field are not among them and do blank to spaces, while the binary memo and
    /// binary character variants blank to zero.
    /// </value>
    private static readonly char[] ZeroBlankTypes = ['I', 'Y', 'T', 'B', 'X', 'Z'];

    /// <summary>
    /// Builds the blank record for a table's layout.
    /// </summary>
    /// <param name="fields">The table's fields, in file order.</param>
    /// <param name="recordLength">Record width in bytes, including the leading deletion flag.</param>
    /// <returns>A record of that width holding each field's blank value.</returns>
    public static byte[] Build(IReadOnlyList<FieldDefinition> fields, int recordLength)
    {
        byte[] blank = new byte[recordLength];

        // Spaces first, then the zero-blanking fields over the top. This also covers the deletion
        // flag, which a blank record leaves clear, and any padding the fields do not account for.
        blank.AsSpan().Fill((byte)' ');

        foreach (FieldDefinition field in fields)
        {
            if (!ZeroBlankTypes.Contains(field.Type))
                continue;

            // A descriptor that does not fit the record is the opener's to refuse, not this one's;
            // clamping here would hide it.
            int length = Math.Min(field.Length, recordLength - field.RecordOffset);
            if (length > 0)
                blank.AsSpan(field.RecordOffset, length).Clear();
        }

        return blank;
    }
}
