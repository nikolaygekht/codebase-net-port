namespace CodeBase.Net;

/// <summary>
/// What skipping forwards or backwards did.
///
/// The two failures to move are not the same, which is why they are separate values: running off
/// the end leaves the cursor past the last record with nothing readable, while running off the
/// front leaves it on the first record, which is still readable.
/// </summary>
public enum SkipResult
{
    /// <summary>
    /// The cursor moved and is on a record.
    /// </summary>
    Moved,

    /// <summary>
    /// The skip ran past the last record, so the cursor is at end of file and the record is blank.
    /// </summary>
    Eof,

    /// <summary>
    /// The skip ran back past the first record, so the cursor is on record one and marked as at the beginning.
    /// </summary>
    /// <value>
    /// Record one is still readable. This is not a position before the table: the C library moves to
    /// record one and raises the flag there (d4skip.c:1195-1203).
    /// </value>
    Bof,
}
