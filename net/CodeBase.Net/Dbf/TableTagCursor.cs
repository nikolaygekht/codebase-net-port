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
    private readonly IReadOnlyList<FieldDefinition> fields;
    private readonly int codePage;

    private SeekConverter? converter;
    private IKeyValueSource? source;
    private byte[]? buffer;
    private KeySearch active;
    private bool searching;

    /// <summary>
    /// Initializes a new instance over a tag.
    /// </summary>
    /// <param name="tag">The tag whose order to follow.</param>
    /// <param name="fields">The fields of the table, which say what the tag's keys are made of.</param>
    /// <param name="codePage">The table's code page, which says which weights a collation means.</param>
    /// <exception cref="CodeBaseException">
    /// The tag's key expression is not one this library can type, so its keys cannot be padded correctly.
    /// </exception>
    public TableTagCursor(Tag tag, IReadOnlyList<FieldDefinition> fields, int codePage)
    {
        this.tag = tag;
        this.fields = fields;
        this.codePage = codePage;

        // Asking for the pad byte here is the point rather than a side effect: it is resolved on first
        // use (ADR-28), and a tag this library cannot type has to be refused when a caller asks to use
        // the tag — not part-way through a walk, when the first leaf block happens to be decoded.
        _ = tag.Inner.PadByte;

        cursor = tag.Inner.OpenCursor();
    }

    /// <summary>Gets the tag being followed.</summary>
    public Tag Tag => tag;


    /// <summary>Gets a value indicating whether a search is running that Next can continue.</summary>
    public bool HasSearch => searching;

    /// <summary>
    /// Forgets the active search, so a later Next cannot continue one the caller has moved away from.
    /// </summary>
    /// <remarks>
    /// Called by every positioning move that is not a seek. A search that outlived the position it
    /// was made for would answer a question nobody asked.
    /// </remarks>
    public void ForgetSearch() => searching = false;

    /// <summary>
    /// Builds a search over this tag's keys, into the cursor's own buffer.
    /// </summary>
    /// <param name="value">The value to look for, in its stored form.</param>
    /// <param name="prefix">Whether a short value stands for a prefix rather than a whole key.</param>
    /// <returns>The search.</returns>
    /// <exception cref="CodeBaseException">The tag's keys cannot be built from a value.</exception>
    public KeySearch SearchFor(ReadOnlySpan<byte> value, bool prefix)
    {
        SeekConverter resolved = Converter;
        byte[] into = Buffer;
        int length;

        switch (resolved.Kind)
        {
            case KeyKind.Character:
                length = prefix ? Math.Min(value.Length, into.Length) : into.Length;
                value[..Math.Min(value.Length, into.Length)].CopyTo(into);
                if (!prefix)
                    into.AsSpan(Math.Min(value.Length, into.Length)).Fill(resolved.PadByte);
                break;

            case KeyKind.CollatedCharacter when prefix:
                length = CollatedKey.WriteSearch(
                    value,
                    CollationWeights.TableFor(resolved.Collation, resolved.CodePage),
                    CollationWeights.ExpansionsFor(resolved.Collation, resolved.CodePage),
                    resolved.KeyLength,
                    into);
                break;

            case KeyKind.CollatedCharacter:
                // A whole-key search weighs the value at the field's full width, padding first, so
                // that its tails land where a stored key's do.
                Span<byte> padded = stackalloc byte[resolved.KeyLength / 2];
                padded.Fill((byte)' ');
                value[..Math.Min(value.Length, padded.Length)].CopyTo(padded);
                length = CollatedKey.Write(
                    padded,
                    CollationWeights.TableFor(resolved.Collation, resolved.CodePage),
                    CollationWeights.ExpansionsFor(resolved.Collation, resolved.CodePage),
                    includeTails: true,
                    into);
                break;

            default:
                // Every other kind is a fixed-width number. There is no such thing as a prefix of
                // one, so the flag does not apply and the whole key is always built.
                length = RecordKey.WriteValue(resolved, value, into);
                break;
        }

        active = KeySearch.Into(into, length, resolved.KeyLength, resolved.PadByte);
        searching = true;

        return active;
    }


    /// <summary>Builds a search for a double-valued key.</summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>The search.</returns>
    /// <exception cref="CodeBaseException">This tag's keys are not numbers.</exception>
    public KeySearch SearchForDouble(double value) =>
        Numeric(KeyKind.Double, "a number", into => KeyTransform.FromDouble(value, into));

    /// <summary>Builds a search for an integer-valued key.</summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>The search.</returns>
    /// <exception cref="CodeBaseException">This tag's keys are not integers.</exception>
    public KeySearch SearchForInt32(int value) =>
        Numeric(KeyKind.Int32, "an integer", into => KeyTransform.FromInt32(value, into));

    /// <summary>Builds a search for a date-valued key.</summary>
    /// <param name="value">The date to look for.</param>
    /// <returns>The search.</returns>
    /// <exception cref="CodeBaseException">This tag's keys are not dates.</exception>
    public KeySearch SearchForDate(DateOnly value) =>
        Numeric(KeyKind.Date, "a date", into => KeyTransform.FromDate(value, into));

    private KeySearch Numeric(KeyKind expected, string what, Func<byte[], int> write)
    {
        SeekConverter resolved = Converter;

        if (resolved.Kind != expected)
        {
            throw new CodeBaseException(
                ErrorCode.Info,
                $"Tag '{tag.Name}' holds {resolved.Kind} keys, so it cannot be searched for {what}.");
        }

        active = KeySearch.Into(Buffer, write(Buffer), resolved.KeyLength, resolved.PadByte);
        searching = true;

        return active;
    }

    /// <summary>
    /// Positions on the first entry at or after a search value.
    /// </summary>
    /// <param name="search">The search.</param>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <param name="record">The record reached, valid when the outcome is not Eof.</param>
    /// <returns>What the seek found.</returns>
    public SeekOutcome SeekAtOrAfter(KeySearch search, int recordCount, out int record) =>
        Landed(cursor.Seek(search), forwards: true, recordCount, out record);

    /// <summary>
    /// Positions on the last entry at or before a search value.
    /// </summary>
    /// <param name="search">The search.</param>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <param name="record">The record reached, valid when the outcome is not Bof.</param>
    /// <returns>What the seek found.</returns>
    public SeekOutcome SeekAtOrBefore(KeySearch search, int recordCount, out int record) =>
        Landed(cursor.SeekAtOrBefore(search), forwards: false, recordCount, out record);

    /// <summary>
    /// Moves to the next entry still matching the active search.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <param name="record">The record reached, valid when the outcome is Found.</param>
    /// <returns>What the step found.</returns>
    public SeekOutcome SeekNext(int recordCount, out int record) =>
        Landed(cursor.SeekNext(active), forwards: true, recordCount, out record);

    /// <summary>
    /// Moves to the previous entry still matching the active search.
    /// </summary>
    /// <param name="recordCount">How many records the table holds.</param>
    /// <param name="record">The record reached, valid when the outcome is Found.</param>
    /// <returns>What the step found.</returns>
    public SeekOutcome SeekPrevious(int recordCount, out int record) =>
        Landed(cursor.SeekPrevious(active), forwards: false, recordCount, out record);

    /// <summary>
    /// Turns a seek's landing into a record number, stepping over entries the table does not have.
    /// </summary>
    private SeekOutcome Landed(SeekOutcome outcome, bool forwards, int recordCount, out int record)
    {
        record = 0;

        if (outcome is SeekOutcome.Eof or SeekOutcome.Bof or SeekOutcome.NoEntry)
            return outcome;

        return Land(true, forwards, recordCount, out record)
            ? outcome
            : forwards ? SeekOutcome.Eof : SeekOutcome.Bof;
    }

    private SeekConverter Converter =>
        converter ??= SeekConverter.For(tag.Inner.Header, fields, codePage);

    private IKeyValueSource Source => source ??= new FieldValueSource(Converter.Field!);

    private byte[] Buffer => buffer ??= new byte[Converter.KeyLength];

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
    /// <param name="current">The record buffer, which the key is rebuilt from.</param>
    /// <returns>Whether an entry for that record was found.</returns>
    /// <remarks>
    /// Needed because a caller may move by record number and then step in tag order. Finding the entry
    /// costs a walk, so it is done only when the two have actually drifted apart. The C library solves
    /// the same problem by re-deriving the record's key through the expression
    /// (d4seekSynchToCurrentPos, D4SEEK.C:1141), which needs the expression engine; walking is the
    /// version available now, and the difference is speed rather than answer.
    /// </remarks>
    public bool Synchronize(uint record, RecordBuffer current)
    {
        if (cursor.IsOnKey && cursor.Current.Record == record)
            return true;

        // Rebuild the key this record has in this tag, then descend to it. The tree is ordered by
        // key and not by record number, so looking for the number itself means reading every entry;
        // deriving the key turns that walk into a descent. It is what the reference does
        // (d4seekSynchToCurrentPos, D4SEEK.C:1141), and it does not need the expression engine here
        // because a selectable tag's expression is always a bare field name (ADR-28).
        byte[] into = Buffer;
        int length = RecordKey.Write(Converter, Source, current, into);
        KeySearch key = KeySearch.Into(into, length, Converter.KeyLength, Converter.PadByte);

        // The record number is what picks this entry out of a run of equal keys, which is exactly
        // what SeekExact takes it for.
        bool exact = cursor.SeekExact(key, record) == SeekOutcome.Found;

        // A record the tag does not list still has a key, and the reference carries on from where
        // that key would sit rather than refusing: tfile4go leaves the cursor at the nearest entry
        // in byte order and reports that it was not the one asked for (d4skip.c:1248-1274,
        // I4TAG.C:1339-1516). A filtered or unique tag omits records for ordinary reasons, so this
        // is a normal path and not a corrupt one.
        AtNearest = !exact;

        if (!exact && !cursor.SeekNearestByBytes(key))
        {
            // Every key in the tag sorts below this record's, so there is nothing to carry on from
            // in that direction. The caller's step then runs off the end, which it would have done
            // from the record's own entry too.
            searching = false;
            return false;
        }

        // The search was built to find a position, not to answer a caller's question, so it must not
        // be left behind for SeekNext to continue.
        searching = false;

        return true;
    }

    /// <summary>
    /// Gets a value indicating whether the last synchronize landed near a record rather than on it.
    /// </summary>
    /// <value>
    /// True when the tag does not list the record the table's cursor is on, so the cursor sits at the
    /// nearest key instead. A step from there has already covered one entry, which is why the count
    /// is adjusted for it.
    /// </value>
    public bool AtNearest { get; private set; }


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
