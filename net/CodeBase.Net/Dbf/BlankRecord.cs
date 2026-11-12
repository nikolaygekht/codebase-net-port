using CodeBase.Net.Memo;

namespace CodeBase.Net.Dbf;

/// <summary>
/// The blank record a table hands back when its cursor is not on a real one.
///
/// Built once when a table opens and copied whenever a record has to be blanked, which is what the
/// C library does with its own prepared blank (D4OPEN.C:440-457, d4data.c:198-205). Blanking is
/// per-field rather than a single fill, because a blank is not one byte for every type: the fixed
/// binary types blank to zero, the wide strings to a zero and a space in each pair, and everything
/// else to spaces (f4blank, F4FIELD.C:92-169).
///
/// This is what makes reading at end of file safe. The cursor sits one past the last record with no
/// bytes behind it, and a reader that left the previous record in the buffer would answer with the
/// most plausible wrong data available.
/// </summary>
internal static class BlankRecord
{
    /// <summary>
    /// Types whose blank value is zero bytes rather than spaces, whatever their width.
    /// </summary>
    /// <value>The fixed-width binary types, taken from [c]f4blank[/c]'s own list.</value>
    private static readonly char[] ZeroBlankTypes = ['I', 'Y', 'T', '7', 'B', 'X', 'Z'];

    /// <summary>
    /// Types whose blank value depends on which reference encoding the field uses.
    /// </summary>
    /// <value>
    /// A memo reference blanks to zeros in its four-byte binary form and to spaces in its ten-byte
    /// text form, because the blank has to parse back as "no memo" in whichever encoding is in use.
    /// Both are witnessed by the corpus: every empty reference in the Visual FoxPro tables is four
    /// zero bytes and every one in the FoxPro 2.x table is ten spaces. See FPT-MEMO.md section 3.4.
    /// </value>
    private static readonly char[] ReferenceTypes = ['M', 'G', 'X'];

    /// <summary>
    /// Types whose blank value is a zero and a space in each pair of bytes.
    /// </summary>
    /// <value>
    /// CodeBase's UTF-16 strings (F4FIELD.C:92-108). Read little-endian, as the field is, each pair is
    /// U+2000 rather than a space; the reference wrote it as though the field were big-endian.
    /// </value>
    private static readonly char[] WideTypes = ['W', 'O'];

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
            // A descriptor that does not fit the record is the opener's to refuse, not this one's;
            // clamping here would hide it.
            int length = Math.Min(field.Length, recordLength - field.RecordOffset);
            if (length <= 0)
                continue;

            if (BlanksToZero(field))
            {
                blank.AsSpan(field.RecordOffset, length).Clear();
            }
            else if (WideTypes.Contains(field.Type))
            {
                // Whole pairs only: an odd last byte keeps the fill, as f4blank's loop to len / 2 does.
                for (int at = field.RecordOffset; at + 1 < field.RecordOffset + length; at += 2)
                    blank[at] = 0x00;
            }
        }

        return blank;
    }

    /// <summary>
    /// Returns whether a field's blank value is zero bytes rather than spaces.
    /// </summary>
    private static bool BlanksToZero(FieldDefinition field) =>
        ZeroBlankTypes.Contains(field.Type) ||
        (ReferenceTypes.Contains(field.Type) && field.Length == MemoReference.BinaryWidth);
}
