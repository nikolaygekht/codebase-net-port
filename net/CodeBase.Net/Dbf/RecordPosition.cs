namespace CodeBase.Net.Dbf;

/// <summary>
/// Where a table's cursor is, as the three separate pieces of state the C library keeps.
///
/// The record number and the two flags are stored, not derived. The C library returns stored flags
/// from [c]d4bof[/c] and [c]d4eof[/c] rather than computing them (d4data.c:263, d4data.c:350), and
/// they genuinely do not follow from the record number: an empty table is at the beginning and at
/// the end at the same time, and a table is at the beginning while its first record is readable.
/// A port that derived them would be wrong in both places.
///
/// Nothing here reads a file, so every transition is a unit test.
/// </summary>
internal sealed class RecordPosition
{
    /// <summary>
    /// The number reported when the cursor is not on a record.
    /// </summary>
    /// <value>
    /// What the C library stores after a failed positioning (d4go.c:236, d4go.c:317). It is a fourth
    /// state rather than a flavour of end of file, and skipping from it is an error.
    /// </value>
    public const int Invalid = -1;

    /// <summary>
    /// Gets the record the cursor is on.
    /// </summary>
    /// <value>
    /// One past the record count at end of file, and [c]Invalid[/c] where a positioning failed.
    /// Zero is never a record number: records count from one.
    /// </value>
    public int Number { get; private set; } = Invalid;

    /// <summary>Gets a value indicating whether the cursor is past the last record.</summary>
    public bool Eof { get; private set; }

    /// <summary>Gets a value indicating whether the cursor is at the beginning of the table.</summary>
    /// <value>
    /// True while record one is still readable, because that is where the C library leaves the
    /// cursor when a backwards skip runs out of records.
    /// </value>
    public bool Bof { get; private set; }

    /// <summary>Gets a value indicating whether the cursor is on a record that can be read.</summary>
    public bool IsOnRecord => Number >= 1 && !Eof;

    /// <summary>
    /// Records that a read of the given record succeeded.
    /// </summary>
    /// <param name="recordNumber">The record now under the cursor, counting from one.</param>
    public void MovedTo(int recordNumber)
    {
        // Both flags clear together (d4go.c:326). The only other thing that clears either is a
        // skip, which clears the beginning flag before deciding where to go.
        Number = recordNumber;
        Eof = false;
        Bof = false;
    }

    /// <summary>
    /// Records that the cursor ran off the end of the table.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <returns>The record number the cursor now reports.</returns>
    public int MovedPastEnd(int recordCount)
    {
        Number = recordCount + 1;
        Eof = true;

        // An empty table is at both ends at once: its end-of-file position is record one. The flag
        // is set here and never cleared here, exactly as d4goEof does it (d4go.c:475-478).
        if (Number == 1)
            Bof = true;

        return Number;
    }

    /// <summary>
    /// Clears the beginning-of-table flag without moving the cursor.
    ///
    /// A skip does this before it works out where it is going (d4skip.c:1149), and then sets the
    /// flag again if it runs out of records. So a skip that lands on a record always leaves the flag
    /// clear, even a skip of zero.
    /// </summary>
    public void ClearBof() => Bof = false;

    /// <summary>
    /// Records that a positioning failed and left the cursor nowhere.
    ///
    /// The flags are deliberately untouched. Falling off the end through a direct positioning does
    /// not put the cursor at end of file, it puts it nowhere, and the two are different states.
    /// </summary>
    public void Invalidate() => Number = Invalid;

    /// <summary>
    /// Records that a backwards skip ran out of records and stopped on the first one.
    /// </summary>
    /// <param name="endOfFileBefore">
    /// Whether the cursor was past the end before the skip. Restored, because the C library saves
    /// and puts it back around the move to record one (d4skip.c:1197-1202).
    /// </param>
    public void MovedBeforeStart(bool endOfFileBefore)
    {
        Bof = true;
        Eof = endOfFileBefore;
    }

    /// <summary>
    /// Returns the position rendered as its three pieces, for diagnostics and failure messages.
    /// </summary>
    /// <returns>A short description of where the cursor is.</returns>
    public override string ToString() => $"record {Number} (bof {Bof}, eof {Eof})";
}
