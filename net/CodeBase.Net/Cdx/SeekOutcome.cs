namespace CodeBase.Net.Cdx;

/// <summary>
/// What a seek found, as a status rather than an exception.
///
/// Five of these carry a number from the C library's [c]r4[/c] set, preserved so a failure can be
/// compared against the reference. [c]Before[/c] does not: it is the outcome of asking for the last
/// entry at or before a value, which the C library has no operation for, so it is named rather than
/// numbered.
///
/// A miss is never an error here. That is the rule of PORTING-PLAN.md section 4.2, and it is what lets a
/// caller use a miss: the cursor is left on the neighbouring entry, which is where a range scan starts.
/// </summary>
internal enum SeekOutcome
{
    /// <summary>
    /// An entry matching the search value was found, and the cursor is on it.
    /// </summary>
    /// <value>
    /// The C library's [c]r4success[/c] (0). For a run of equal keys, [c]Seek[/c] lands on the first and
    /// [c]SeekLast[/c] on the last.
    /// </value>
    Found,

    /// <summary>
    /// Nothing matched, and the cursor is on the first entry that sorts after the value.
    /// </summary>
    /// <value>The C library's [c]r4after[/c] (2).</value>
    After,

    /// <summary>
    /// Nothing matched, and the cursor is on the last entry that sorts before the value.
    /// </summary>
    /// <value>
    /// No [c]r4[/c] number: the C library cannot report this because it cannot search backwards.
    /// </value>
    Before,

    /// <summary>
    /// Nothing at or after the value exists, and the cursor is past the end of the tag.
    /// </summary>
    /// <value>The C library's [c]r4eof[/c] (3).</value>
    Eof,

    /// <summary>
    /// Nothing at or before the value exists, and the cursor is before the start of the tag.
    /// </summary>
    /// <value>The C library's [c]r4bof[/c] (4).</value>
    Bof,

    /// <summary>
    /// The run of matching entries has ended, and the cursor is on the entry that ended it.
    /// </summary>
    /// <value>
    /// The C library's [c]r4entry[/c] (5), reported by the match-bounded steps. It says "no more of
    /// these", not "nothing here" — the cursor is still on a readable entry.
    /// </value>
    NoEntry,
}
