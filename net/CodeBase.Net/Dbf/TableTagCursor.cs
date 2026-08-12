using CodeBase.Net.Cdx;

namespace CodeBase.Net.Dbf;

/// <summary>
/// Navigates a table's records in a tag's order, by moving the tag and then reading the record it names.
///
/// This is the part that belongs to neither subsystem. The index knows about keys and record numbers; the
/// table knows about records and a cursor; this couples them, and carries the two behaviours the C library
/// has here that neither half would predict:
///
/// An entry naming a record the table does not have is **skipped**, in the direction of travel, rather
/// than refused (d4skip.c:1296-1308). The C library's own comment says why — another process may have
/// added a key before the record — and a reader that refused would fail on a file it reads.
///
/// Running out of entries sets **both** end flags (d4skip.c:1281-1285), which is the same shape an empty
/// table has. The port already models the two ends separately, so it can be faithful here at no cost.
///
/// Governing specification: CDX-FORMAT.md section 2, DBF-FORMAT.md section 2.3.
/// </summary>
internal sealed class TableTagCursor
{
    private readonly Tag tag;
    private readonly TagCursor cursor;

    /// <summary>
    /// Initializes a new instance over a tag.
    /// </summary>
    /// <param name="tag">The tag whose order to follow.</param>
    /// <exception cref="CodeBaseException">
    /// The tag's key expression is not one this library can type, so its keys cannot be padded correctly.
    /// </exception>
    public TableTagCursor(Tag tag)
    {
        this.tag = tag;

        // Asking for the pad byte here is the point rather than a side effect: it is resolved on first
        // use (ADR-28), and a tag this library cannot type has to be refused when a caller asks to use
        // the tag — not part-way through a walk, when the first leaf block happens to be decoded.
        _ = tag.Inner.PadByte;

        cursor = tag.Inner.OpenCursor();
    }

    /// <summary>Gets the tag being followed.</summary>
    public Tag Tag => tag;

    /// <summary>
    /// Moves to the first record in the tag's order.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <param name="record">The record number to read, valid only when this returns true.</param>
    /// <returns>Whether the tag reached a record the table has.</returns>
    public bool First(int recordCount, out int record) =>
        Land(cursor.Top(), forwards: true, recordCount, out record);

    /// <summary>
    /// Moves to the last record in the tag's order.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <param name="record">The record number to read, valid only when this returns true.</param>
    /// <returns>Whether the tag reached a record the table has.</returns>
    public bool Last(int recordCount, out int record) =>
        Land(cursor.Bottom(), forwards: false, recordCount, out record);

    /// <summary>
    /// Moves by a number of entries in the tag's order.
    /// </summary>
    /// <param name="count">How far to move. Negative moves backwards; zero stays put.</param>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <param name="record">The record number to read, valid unless this returns Nowhere.</param>
    /// <returns>How far it got.</returns>
    public TagLanding Step(int count, int recordCount, out int record)
    {
        long moved = cursor.Skip(count);

        if (moved == count)
            return Land(true, forwards: count >= 0, recordCount, out record) ? TagLanding.Moved : TagLanding.Nowhere;

        // Ran out of entries. Backwards, the C library puts the cursor back on the tag's first entry and
        // leaves that record readable, reporting only the beginning flag (tfile4skip's tfile4top,
        // I4TAG.C:2543-2549, and d4skip.c:1343-1354) — the same shape a backwards skip in record order
        // has. Forwards, the table is simply at its end (d4skip.c:1277-1279).
        if (count < 0 && Land(cursor.Top(), forwards: true, recordCount, out record))
            return TagLanding.Stopped;

        record = 0;
        return count < 0 ? TagLanding.Nowhere : TagLanding.AtEnd;
    }

    /// <summary>
    /// Positions the tag on whichever entry names a given record, so that stepping from it continues in
    /// the tag's order rather than from wherever the tag happened to be.
    /// </summary>
    /// <param name="record">The record the table's cursor is on.</param>
    /// <returns>Whether an entry for that record was found.</returns>
    /// <remarks>
    /// Needed because a caller may move by record number and then step in tag order. Finding the entry
    /// costs a walk, so it is done only when the two have actually drifted apart. The C library solves
    /// the same problem by re-deriving the record's key through the expression
    /// (d4seekSynchToCurrentPos, D4SEEK.C:1141), which needs the expression engine; walking is the
    /// version available now, and the difference is speed rather than answer.
    /// </remarks>
    public bool Synchronize(uint record)
    {
        if (cursor.IsOnKey && cursor.Current.Record == record)
            return true;

        for (bool any = cursor.Top(); any; any = cursor.Next())
        {
            if (cursor.Current.Record == record)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Takes the entry the cursor landed on and skips forward over any that name records the table does
    /// not have.
    /// </summary>
    private bool Land(bool moved, bool forwards, int recordCount, out int record)
    {
        record = 0;

        if (!moved || !cursor.IsOnKey)
            return false;

        while (true)
        {
            uint named = cursor.Current.Record;

            if (named >= 1 && named <= (uint)recordCount)
            {
                record = (int)named;
                return true;
            }

            // An entry pointing past the end of the table: step over it the way the walk was headed.
            if (!(forwards ? cursor.Next() : cursor.Previous()))
                return false;
        }
    }
}

/// <summary>
/// How far a move through a tag got.
/// </summary>
internal enum TagLanding
{
    /// <summary>It moved as far as it was asked to, and named a record the table has.</summary>
    Moved,

    /// <summary>
    /// It ran out of entries backwards and stopped on the tag's first, whose record stays readable.
    /// </summary>
    Stopped,

    /// <summary>It ran out of entries going forwards, so the table is at its end.</summary>
    AtEnd,

    /// <summary>
    /// It got where it was going, but every entry there names a record the table does not have.
    /// </summary>
    Nowhere,
}
