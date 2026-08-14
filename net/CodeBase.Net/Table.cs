using CodeBase.Net.Cdx;
using System.Text;
using CodeBase.Net.Dbf;
using CodeBase.Net.Memo;

namespace CodeBase.Net;

/// <summary>
/// An open table: its shape, and a cursor over its records.
///
/// The shape is what the header says about itself, all of it available without reading a record:
/// how many records there are and how wide they are, what fields exist and where each sits, whether
/// a memo file accompanies it, and what code page its text is in.
///
/// The cursor is one record deep. Moving it reads that record into a buffer the field accessors
/// then read out of, so a table holds one record at a time and moving is what changes it. A table
/// starts on no record: call [c]Top[/c], [c]Bottom[/c] or [c]Go[/c] before reading a field.
///
/// Closing a table closes the files it opened. Closing the engine that opened it does the same, so
/// a caller that keeps the engine in a using statement cannot leak a handle by forgetting a table.
/// </summary>
public sealed class Table : IDisposable
{
    private readonly OpenedTable opened;
    private readonly Encoding? defaultEncoding;
    private readonly Action<Table> onClosed;
    private readonly RecordReader reader;
    private readonly RecordBuffer record;
    private readonly RecordPosition position = new();
    private readonly byte[] blankRecord;
    private readonly MemoReader? memoReader;
    private readonly Dictionary<Tag, TableTagCursor> tagCursors = [];
    private Encoding? textEncoding;
    private TableTagCursor? selected;
    private bool closed;

    internal Table(string path, OpenedTable opened, Encoding? defaultEncoding, Action<Table> onClosed)
    {
        Path = path;
        this.opened = opened;
        this.defaultEncoding = defaultEncoding;
        this.onClosed = onClosed;

        Fields = new FieldCollection(opened.Fields.Fields);
        Tags = new TagCollection(
            opened.Index is null ? [] : [.. opened.Index.Tags.Select(t => new Tag(t))]);
        CodePage = CodePageMap.Resolve(opened.Header.CodePage);

        reader = new RecordReader(opened.Data, opened.Header.HeaderLength, opened.Header.RecordLength);
        record = new RecordBuffer(opened.Header.RecordLength);
        blankRecord = BlankRecord.Build(opened.Fields.Fields, opened.Header.RecordLength);

        if (opened.Memo is not null && opened.MemoHeader is not null)
        {
            memoReader = new MemoReader(
                opened.Memo,
                opened.MemoHeader.Value.BlockSize,
                opened.Header.Flags.MayHaveCompressedMemos);
        }

        // A table opens on no record at all, with a blank buffer rather than whatever the array
        // happened to hold. Reading a field before positioning then answers blank, not zeros.
        record.Blank(blankRecord);
    }

    /// <summary>
    /// Gets the path the table was opened from.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the version byte, exactly as the file stores it.
    /// </summary>
    /// <value>
    /// Reported rather than normalized, so a caller can tell a table using CodeBase extensions from
    /// a plain one even though both are read the same way.
    /// </value>
    public byte Version => opened.Header.Version;

    /// <summary>
    /// Gets the date the table was last written, as the three numbers the file stores.
    /// </summary>
    /// <value>
    /// Not a date, because the year is stored as two digits and the century is not recoverable from
    /// the file. Reporting a guess as a date would be a lie the type system could not undo.
    /// </value>
    public (int Year, int Month, int Day) LastUpdate =>
        (opened.Header.LastUpdateYear, opened.Header.LastUpdateMonth, opened.Header.LastUpdateDay);

    /// <summary>
    /// Gets the number of records the table holds, including those flagged as deleted.
    /// </summary>
    public int RecordCount => opened.Header.RecordCount;

    /// <summary>
    /// Gets the number of bytes in one record, including its leading deletion flag.
    /// </summary>
    public int RecordLength => opened.Header.RecordLength;

    /// <summary>
    /// Gets the number of bytes before the first record.
    /// </summary>
    public int HeaderLength => opened.Header.HeaderLength;

    /// <summary>
    /// Gets the table's index tags, empty when it declares no production index.
    /// </summary>
    public TagCollection Tags { get; }

    /// <summary>
    /// Gets a value indicating whether the table has a production index open.
    /// </summary>
    /// <value>
    /// True when the header declared one and it was opened with the table. A declared index whose file is
    /// missing is an error at open rather than a table that quietly navigates in record order.
    /// </value>
    public bool HasIndex => opened.Index is not null;

    /// <summary>
    /// Gets the tag whose order the cursor follows, or null when it follows record order.
    /// </summary>
    public Tag? SelectedTag => selected?.Tag;

    /// <summary>
    /// Gets the code page the table names for its text.
    /// </summary>
    public CodePage CodePage { get; }

    /// <summary>
    /// Gets the code page mark exactly as stored, whether or not it names a known code page.
    /// </summary>
    /// <value>
    /// This is the value that round-trips. A mark this library does not recognize is still a mark the
    /// file owns, and writing the table back preserves it rather than replacing it with something
    /// derived.
    /// </value>
    public byte CodePageByte => opened.Header.CodePage;

    /// <summary>
    /// Gets the number of the code page the table names, or null where it names none.
    /// </summary>
    /// <value>
    /// The number, such as 1251, rather than the mark stored in the header, which for that code page
    /// is [c]0xC9[/c]. Null covers both an unmarked table and a mark outside the twenty-six Visual
    /// FoxPro documents; [clink=CodeBase.Net.CodePage]CodePage[/clink] tells the two apart.
    /// Reading this needs no encoding provider registered, and answers even for the two marks whose
    /// code page .NET cannot supply.
    /// </value>
    public int? CodePageNumber => CodePageMap.NumberFor(CodePage);

    /// <summary>
    /// Gets the encoding record text should be read with.
    /// </summary>
    /// <value>
    /// Resolved when first asked for rather than when the table is opened, so that reading a
    /// table's shape needs no encoding provider registered. Reading its text does. See ADR-17.
    /// </value>
    /// <exception cref="CodeBaseException">
    /// The encoding is unavailable because no provider for the legacy code pages is registered.
    /// </exception>
    public Encoding TextEncoding => textEncoding ??= CodePageMap.EncodingFor(CodePage, defaultEncoding);

    /// <summary>
    /// Gets a value indicating whether a memo file accompanies the table.
    /// </summary>
    public bool HasMemo => opened.Memo is not null;

    /// <summary>
    /// Gets the number of bytes in a memo block.
    /// </summary>
    /// <value>
    /// Null when the table has no memo file. Zero is a legal value meaning byte granularity, so it
    /// is not the same answer as no memo at all.
    /// </value>
    public int? MemoBlockSize => opened.MemoHeader?.BlockSize;

    /// <summary>
    /// Gets the table's fields, in file order.
    /// </summary>
    /// <value>
    /// Excludes the hidden field holding the null bitmap, matching the field list the C library
    /// reports. That field is available on its own property.
    /// </value>
    public FieldCollection Fields { get; }

    /// <summary>
    /// Gets the hidden field holding the bitmap of which nullable fields are null.
    /// </summary>
    /// <value>Null when the table has no nullable field, which is the usual case.</value>
    public FieldDefinition? NullFlags => opened.Fields.NullFlags;

    /// <summary>
    /// Gets the field descriptors exactly as the file stores them.
    /// </summary>
    /// <value>
    /// The stored view, for tooling and diagnostics. It differs from the field list in ways that
    /// matter when inspecting a file rather than using it: it includes the null-flags field, keeps
    /// names in their stored case, and reports the stored type letters.
    /// </value>
    public IReadOnlyList<FieldDescriptor> Descriptors => opened.Descriptors;

    /// <summary>
    /// Gets the record the cursor is on.
    /// </summary>
    /// <value>
    /// One past the record count when the cursor is past the last record, and -1 when it is on no
    /// record at all, which is where a freshly opened table starts and where a failed positioning
    /// leaves it.
    /// </value>
    public int RecordNumber => position.Number;

    /// <summary>
    /// Gets a value indicating whether the cursor is past the last record.
    /// </summary>
    /// <value>
    /// An empty table is past its last record and at its beginning at the same time, so this and
    /// [c]Bof[/c] can both be true.
    /// </value>
    public bool Eof => position.Eof;

    /// <summary>
    /// Gets a value indicating whether the cursor is at the beginning of the table.
    /// </summary>
    /// <value>
    /// True after a backwards skip ran out of records, which leaves the cursor on record one rather
    /// than before it. So this being true does not mean nothing can be read.
    /// </value>
    public bool Bof => position.Bof;

    /// <summary>
    /// Gets a value indicating whether the current record is flagged as deleted.
    /// </summary>
    /// <value>
    /// True for any first byte other than a space, which is what the C library reports. A blank
    /// record, which is what the cursor sees when it is on no record, is not deleted.
    /// </value>
    public bool Deleted => record.Deleted;

    /// <summary>
    /// Moves the cursor to a record by number.
    /// </summary>
    /// <param name="recordNumber">The record, counting from one.</param>
    /// <returns>
    /// Whether the record was there. Past the end is not an error: the record is blanked, the cursor
    /// is left on nothing, and the answer says so.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The number is zero or negative. The C library accepts zero and reads the bytes before the
    /// first record, which are the tail of the header; that is a bug to refuse rather than a
    /// behaviour to reproduce. See Decision 5.
    /// </exception>
    /// <exception cref="CodeBaseException">The file is shorter than its header says.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult Go(int recordNumber)
    {
        EnsureOpen();
        selected?.ForgetSearch();
        ArgumentOutOfRangeException.ThrowIfLessThan(recordNumber, 1);

        if (recordNumber > RecordCount)
        {
            record.Blank(blankRecord);
            position.Invalidate();
            return GoResult.NoRecord;
        }

        Fetch(recordNumber);
        return GoResult.Ok;
    }

    /// <summary>
    /// Moves the cursor to the first record.
    ///
    /// With a tag selected this is the first record in that tag's order, which is not record one.
    /// </summary>
    /// <returns>Whether there was one. An empty table has none, and ends up at both of its ends.</returns>
    /// <exception cref="CodeBaseException">The file is shorter than its header says.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult Top()
    {
        EnsureOpen();
        selected?.ForgetSearch();

        if (selected is not null)
            return Reached(selected.First(RecordCount, out int first), first);

        if (RecordCount < 1)
        {
            MovePastEnd();
            return GoResult.NoRecord;
        }

        Fetch(1);
        return GoResult.Ok;
    }

    /// <summary>
    /// Moves the cursor to the last record.
    ///
    /// With a tag selected this is the last record in that tag's order.
    /// </summary>
    /// <returns>Whether there was one.</returns>
    /// <exception cref="CodeBaseException">The file is shorter than its header says.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult Bottom()
    {
        EnsureOpen();
        selected?.ForgetSearch();

        if (selected is not null)
            return Reached(selected.Last(RecordCount, out int last), last);

        if (RecordCount < 1)
        {
            MovePastEnd();
            return GoResult.NoRecord;
        }

        Fetch(RecordCount);
        return GoResult.Ok;
    }

    /// <summary>
    /// Moves the cursor forwards or backwards by a number of records.
    ///
    /// With a tag selected the step is a step in that tag's order, taken from wherever the record cursor
    /// is — so a caller may move by record number and carry on stepping in the tag.
    /// </summary>
    /// <param name="count">
    /// How far to move. Negative moves backwards; zero re-reads the current record, which is what
    /// the C library does.
    /// </param>
    /// <returns>Whether the cursor moved, and which end it stopped at if it did not.</returns>
    /// <exception cref="CodeBaseException">
    /// The cursor is on no record, so there is nothing to skip from. The C library reports the same
    /// condition as an error rather than as a silent no-op (d4skip.c:1115-1122). Also raised when
    /// the file is shorter than its header says.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public SkipResult Skip(int count)
    {
        EnsureOpen();

        if (position.Number < 1)
        {
            throw new CodeBaseException(
                ErrorCode.Info,
                "Skipping needs a position to skip from, and the cursor is on no record. Call Go, " +
                "Top or Bottom first.");
        }

        // Cleared before anything else, exactly as d4skip does it, so a skip that lands on a record
        // always leaves the flag clear.
        position.ClearBof();

        if (selected is not null)
            return SkipInTagOrder(selected, count);

        long target = (long)position.Number + count;

        if (target >= 1 && target <= RecordCount)
        {
            Fetch((int)target);
            return SkipResult.Moved;
        }

        if (RecordCount < 1 || target > RecordCount)
        {
            // An empty table ends up at both of its ends whichever way the skip was headed, and only
            // the answer differs. The C raises the beginning flag explicitly here and again below;
            // both are redundant, because the end-of-file position of an empty table is record one
            // and moving there raises the flag already. A negative record count, the one input that
            // would make them differ, is refused when the table is opened.
            MovePastEnd();

            return count < 0 ? SkipResult.Bof : SkipResult.Eof;
        }

        // Ran off the front. The cursor stops on record one, which stays readable, and the
        // end-of-file flag is put back as it was found.
        bool endOfFileBefore = position.Eof;
        Fetch(1);
        position.MovedBeforeStart(endOfFileBefore);
        return SkipResult.Bof;
    }

    /// <summary>
    /// Makes the cursor follow a tag's order instead of record order.
    ///
    /// Selecting a tag does not move the cursor: the next [c]Top[/c], [c]Bottom[/c] or [c]Skip[/c] is what
    /// re-positions, exactly as in the C library. A caller that selects a tag and then reads a field gets
    /// the record it already had.
    /// </summary>
    /// <param name="tag">The tag to follow, or null to go back to record order.</param>
    /// <exception cref="CodeBaseException">
    /// The tag belongs to another table, or its key expression is not a plain field name of this one — in
    /// which case its keys cannot be padded correctly and reading them would be wrong rather than slow.
    /// The table's other tags are unaffected (ADR-28).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public void SelectTag(Tag? tag)
    {
        EnsureOpen();
        selected?.ForgetSearch();

        selected = tag is null ? null : CursorFor(tag);
    }

    /// <summary>
    /// Moves to the first record in a tag's order, without changing the selection.
    ///
    /// The same navigation the selected-tag form performs, said at the call site instead. Neither is a
    /// wrapper around the other: both drive one cursor per tag, so a walk started here can be continued
    /// with [c]GoNextIndexed[/c] and the selection is left alone. A move that cannot be made reports no
    /// record and leaves the cursor past the end, which a selected-tag [c]Skip[/c] does not (ADR-29).
    /// </summary>
    /// <param name="tag">The tag whose order to follow for this call.</param>
    /// <returns>Whether the tag reached a record.</returns>
    /// <exception cref="CodeBaseException">The tag is not usable on this table.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult GoFirstIndexed(Tag tag)
    {
        EnsureOpen();
        return Reached(CursorFor(tag).First(RecordCount, out int record), record);
    }

    /// <summary>
    /// Moves to the last record in a tag's order, without changing the selection.
    /// </summary>
    /// <param name="tag">The tag whose order to follow for this call.</param>
    /// <returns>Whether the tag reached a record.</returns>
    /// <exception cref="CodeBaseException">The tag is not usable on this table.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult GoLastIndexed(Tag tag)
    {
        EnsureOpen();
        return Reached(CursorFor(tag).Last(RecordCount, out int record), record);
    }

    /// <summary>
    /// Moves to the next record in a tag's order, whatever its key.
    ///
    /// Steps unconditionally, stopping only at the end of the tag. Stepping that stops where a key stops
    /// matching a value is a different operation and belongs with seeking.
    /// </summary>
    /// <param name="tag">The tag whose order to follow for this call.</param>
    /// <returns>Whether there was one.</returns>
    /// <exception cref="CodeBaseException">The tag is not usable on this table.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult GoNextIndexed(Tag tag) => StepIndexed(tag, 1);

    /// <summary>
    /// Moves to the previous record in a tag's order, whatever its key.
    /// </summary>
    /// <param name="tag">The tag whose order to follow for this call.</param>
    /// <returns>Whether there was one.</returns>
    /// <exception cref="CodeBaseException">The tag is not usable on this table.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult GoPreviousIndexed(Tag tag) => StepIndexed(tag, -1);

    /// <summary>
    /// Skips in a selected tag's order, following d4skip's tag path step for step.
    /// </summary>
    private SkipResult SkipInTagOrder(TableTagCursor cursor, int count)
    {
        // Past the end of the table, a forward skip stays there and a backward one comes back in through
        // the tag's last record, which then counts as one of the steps (d4skip.c:1208-1225).
        if (position.Eof)
        {
            if (count >= 0)
            {
                MovePastEnd();
                return SkipResult.Eof;
            }

            if (Bottom() != GoResult.Ok)
                return SkipResult.Bof;

            count++;
        }

        // A skip of zero stays where it is without consulting the tag at all (d4skip.c:1240). Unlike the
        // record path it does not even re-read the record, which is the C library's own asymmetry.
        if (count == 0)
            return SkipResult.Moved;

        if (!cursor.Synchronize((uint)position.Number, this.record))
            return AwayFromTheTag(count);

        count = Adjusted(count, cursor);

        bool endOfFileBefore = position.Eof;

        switch (cursor.Step(count, RecordCount, out int record))
        {
            case TagLanding.Moved:
                Fetch(record);
                return SkipResult.Moved;

            case TagLanding.Stopped:
                // Ran off the front of the tag. The record it stopped on stays readable and only the flag
                // says what happened, exactly as a backwards skip in record order does.
                Fetch(record);
                position.MovedBeforeStart(endOfFileBefore);
                return SkipResult.Bof;

            case TagLanding.AtEnd:
                // Ran out of entries going forward, which is the end of the table and nothing more
                // (d4skip.c:1277-1279).
                MovePastEnd();
                return SkipResult.Eof;

            default:
                // The tag got where it was going and every entry there names a record the table does not
                // have. The C library reports that as the end of the file whichever way the skip was
                // headed, and raises the beginning flag as well (d4skip.c:1303-1307).
                MovePastEnd();
                position.MovedBeforeStart(endOfFileBefore: true);
                return SkipResult.Eof;
        }
    }

    /// <summary>
    /// Moves one step in a tag's order, synchronizing the tag with the record cursor first.
    /// </summary>
    private GoResult StepIndexed(Tag tag, int count)
    {
        EnsureOpen();

        TableTagCursor cursor = CursorFor(tag);

        if (position.Number < 1)
        {
            throw new CodeBaseException(
                ErrorCode.Info,
                $"Stepping in tag '{tag.Name}' needs a position to step from, and the cursor is on no " +
                "record. Call GoFirstIndexed, GoLastIndexed or Go first.");
        }

        // Past the end of the table there is nothing to step forward from, and stepping back re-enters
        // through the tag's last record — the same asymmetry the selected-tag path has.
        if (position.Eof)
            return count < 0 ? Reached(cursor.Last(RecordCount, out int last), last) : Reached(false, 0);

        // A caller may have moved by record number since the last indexed move, which would leave the tag
        // pointing somewhere else. Stepping from where the *record* cursor is, rather than from where the
        // tag was left, is what makes the two surfaces interchangeable.
        if (!cursor.Synchronize((uint)position.Number, this.record))
            return Reached(false, 0);

        count = Adjusted(count, cursor);

        // Unlike the selected-tag Skip, these four are positioning calls: a move that could not be made
        // reports no record and leaves the cursor past the end, the way Top and Bottom do on an empty
        // table. The records they visit are the same ones; only the shape of the failure differs.
        return Reached(cursor.Step(count, RecordCount, out int record) == TagLanding.Moved, record);
    }

    /// <summary>
    /// Gives the cursor that follows a tag, refusing a tag this table cannot use.
    /// </summary>
    /// <summary>
    /// Moves to the record whose key is exactly a value, in the selected tag.
    /// </summary>
    /// <param name="value">The value to look for, as text.</param>
    /// <returns>Ok with the cursor on it, or NoRecord with the cursor on no record.</returns>
    /// <exception cref="CodeBaseException">
    /// No tag is selected, or the selected tag's keys cannot be built from a value of this type.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    /// <remarks>
    /// A whole-key match: the value is padded to the tag's key width, so seeking SMITH finds SMITH
    /// and not SMITHSON. Use [clink=CodeBase.Net.Table.SeekPrefix]SeekPrefix[/clink] for that.
    ///
    /// A miss leaves the cursor on no record rather than on a neighbour, which is what
    /// [clink=CodeBase.Net.Table.Go]Go[/clink] already does past the end of the table. A caller that
    /// wants the neighbour asks for it by name, with
    /// [clink=CodeBase.Net.Table.SeekAtOrAfter]SeekAtOrAfter[/clink].
    /// </remarks>
    public GoResult Seek(string value) => Exact(Search(value, prefix: false));

    /// <summary>
    /// Moves to the first record whose key begins with a value, in the selected tag.
    /// </summary>
    /// <param name="value">The prefix to look for.</param>
    /// <returns>Ok with the cursor on the first match, or NoRecord when there is none.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not text.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    /// <remarks>
    /// Seeking the prefix SMITH finds SMITH, SMITHSON and SMITHERS alike, and
    /// [clink=CodeBase.Net.Table.SeekNext]SeekNext[/clink] walks the rest of them.
    ///
    /// This is a separate method rather than a shorter argument to
    /// [clink=CodeBase.Net.Table.Seek]Seek[/clink] on purpose. The reference decides between the two
    /// meanings by whether the caller's value carried trailing blanks, which makes one call mean
    /// opposite things depending on invisible whitespace.
    /// </remarks>
    public GoResult SeekPrefix(string value) => Exact(Search(value, prefix: true));

    /// <summary>
    /// Moves to the first record at or after a value, in the selected tag.
    /// </summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>Found, After with the cursor on the next record up, or Eof when there is none.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not text.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    /// <remarks>The start of a forward scan, and one end of a range.</remarks>
    public SeekResult SeekAtOrAfter(string value) => Position(Search(value, prefix: false), forwards: true);

    /// <summary>
    /// Moves to the last record at or before a value, in the selected tag.
    /// </summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>Found, Before with the cursor on the last record short of it, or Bof.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not text.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    /// <remarks>A range's other end, walked back to with Skip.</remarks>
    public SeekResult SeekAtOrBefore(string value) => Position(Search(value, prefix: false), forwards: false);

    /// <summary>Moves to the record whose key is exactly a number.</summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>Ok, or NoRecord with the cursor on no record.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not numbers.</exception>
    public GoResult Seek(double value) => Exact(Seeking().SearchForDouble(value));

    /// <summary>Moves to the record whose key is exactly an integer.</summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>Ok, or NoRecord with the cursor on no record.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not integers.</exception>
    public GoResult Seek(int value) => Exact(Seeking().SearchForInt32(value));

    /// <summary>Moves to the record whose key is exactly a date.</summary>
    /// <param name="value">The date to look for.</param>
    /// <returns>Ok, or NoRecord with the cursor on no record.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not dates.</exception>
    public GoResult Seek(DateOnly value) => Exact(Seeking().SearchForDate(value));

    /// <summary>Moves to the first record at or after a number.</summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>Found, After, or Eof.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not numbers.</exception>
    public SeekResult SeekAtOrAfter(double value) =>
        Position(Seeking().SearchForDouble(value), forwards: true);

    /// <summary>Moves to the last record at or before a number.</summary>
    /// <param name="value">The value to look for.</param>
    /// <returns>Found, Before, or Bof.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not numbers.</exception>
    public SeekResult SeekAtOrBefore(double value) =>
        Position(Seeking().SearchForDouble(value), forwards: false);

    /// <summary>Moves to the first record at or after a date.</summary>
    /// <param name="value">The date to look for.</param>
    /// <returns>Found, After, or Eof.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not dates.</exception>
    public SeekResult SeekAtOrAfter(DateOnly value) =>
        Position(Seeking().SearchForDate(value), forwards: true);

    /// <summary>Moves to the last record at or before a date.</summary>
    /// <param name="value">The date to look for.</param>
    /// <returns>Found, Before, or Bof.</returns>
    /// <exception cref="CodeBaseException">No tag is selected, or its keys are not dates.</exception>
    public SeekResult SeekAtOrBefore(DateOnly value) =>
        Position(Seeking().SearchForDate(value), forwards: false);

    /// <summary>
    /// Moves to the next record still matching the search that is running.
    /// </summary>
    /// <returns>Ok with the cursor on it, or NoRecord when the matches have run out.</returns>
    /// <exception cref="CodeBaseException">No search is running, or no tag is selected.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    /// <remarks>
    /// Continues whichever seek last ran, so a run of equal keys or a prefix's matches are walked
    /// without repeating the value. Any move that is not a seek ends the search, so this cannot
    /// quietly continue one the caller has stepped away from.
    /// </remarks>
    public GoResult SeekNext() => Continue(forwards: true);

    /// <summary>
    /// Moves to the previous record still matching the search that is running.
    /// </summary>
    /// <returns>Ok with the cursor on it, or NoRecord when the matches have run out.</returns>
    /// <exception cref="CodeBaseException">No search is running, or no tag is selected.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public GoResult SeekPrevious() => Continue(forwards: false);

    private TableTagCursor Seeking()
    {
        EnsureOpen();

        return selected ?? throw new CodeBaseException(
            ErrorCode.Info,
            "Seeking needs a tag to seek in, and none is selected. Call SelectTag first.");
    }

    private KeySearch Search(string value, bool prefix) =>
        Seeking().SearchFor(TextEncoding.GetBytes(value), prefix);

    private GoResult Exact(KeySearch search)
    {
        TableTagCursor cursor = Seeking();

        return cursor.SeekAtOrAfter(search, RecordCount, out int found) == SeekOutcome.Found
            ? Reached(true, found)
            : NoRecordFound();
    }

    private SeekResult Position(KeySearch search, bool forwards)
    {
        TableTagCursor cursor = Seeking();

        SeekOutcome outcome = forwards
            ? cursor.SeekAtOrAfter(search, RecordCount, out int found)
            : cursor.SeekAtOrBefore(search, RecordCount, out found);

        if (outcome is SeekOutcome.Eof or SeekOutcome.Bof or SeekOutcome.NoEntry)
        {
            MovePastEnd();
            return forwards ? SeekResult.Eof : SeekResult.Bof;
        }

        Fetch(found);

        return outcome == SeekOutcome.Found
            ? SeekResult.Found
            : forwards ? SeekResult.After : SeekResult.Before;
    }

    private GoResult Continue(bool forwards)
    {
        TableTagCursor cursor = Seeking();

        if (!cursor.HasSearch)
        {
            throw new CodeBaseException(
                ErrorCode.Info,
                "There is no search to continue. Call Seek, SeekPrefix or one of the positioning " +
                "seeks first.");
        }

        SeekOutcome outcome = forwards
            ? cursor.SeekNext(RecordCount, out int found)
            : cursor.SeekPrevious(RecordCount, out found);

        return outcome == SeekOutcome.Found ? Reached(true, found) : NoRecordFound();
    }

    private GoResult NoRecordFound()
    {
        record.Blank(blankRecord);
        position.Invalidate();

        return GoResult.NoRecord;
    }

    /// <summary>
    /// Takes one off a step that landing near an absent record has already covered.
    /// </summary>
    /// <param name="count">The steps the caller asked for.</param>
    /// <param name="cursor">The tag cursor, just synchronized.</param>
    /// <returns>The steps still to take.</returns>
    /// <remarks>
    /// When the tag does not list the record, the cursor is left on the nearest key **in byte
    /// order** — [c]tfile4go[/c] clears the descending flag before seeking (I4TAG.C:1285-1290), so
    /// the landing does not depend on the tag's direction. That entry is the one the caller would
    /// have reached by stepping forwards on an ascending tag and backwards on a descending one, so
    /// exactly that step has already been taken and the count owes one fewer.
    ///
    /// **A divergence from the reference, deliberately.** The C adjusts the same two cases but
    /// subtracts in both (d4skip.c:1262-1272), which on a descending tag makes a negative count
    /// larger. Read together with [c]tfile4dskip[/c], which negates the count for a descending tag
    /// (I4TAG.C:65-88), that steps further away from the entry it just landed on rather than
    /// stopping there. This port moves toward zero in both cases, which is what the ascending half
    /// of the C does and what the tag's own order demands.
    ///
    /// Settling it needs a reference run: a descending filtered tag in the corpus with the skips the
    /// C library itself takes recorded beside it. Until then the ascending half matches the
    /// reference exactly and the descending half matches the order the tag stores.
    /// </remarks>
    private static int Adjusted(int count, TableTagCursor cursor)
    {
        if (!cursor.AtNearest)
            return count;

        if (cursor.Tag.Descending)
            return count < 0 ? count + 1 : count;

        return count > 0 ? count - 1 : count;
    }

    /// <summary>
    /// Answers a skip taken from a record whose key sorts past everything the tag holds.
    /// </summary>
    /// <param name="count">The steps the caller asked for.</param>
    /// <returns>Which end the cursor ended at.</returns>
    /// <remarks>
    /// The record is not in the tag and there is no nearer key to carry on from, so the step runs
    /// out of tag the way it would have from the record's own entry.
    /// </remarks>
    private SkipResult AwayFromTheTag(int count)
    {
        MovePastEnd();

        return count < 0 ? SkipResult.Bof : SkipResult.Eof;
    }

    private TableTagCursor CursorFor(Tag tag)
    {
        if (!ReferenceEquals(Tags.TryGet(tag.Name, out Tag? mine) ? mine : null, tag))
        {
            throw new CodeBaseException(
                ErrorCode.Info,
                $"Tag '{tag.Name}' does not belong to the table at '{Path}'.");
        }

        if (!tagCursors.TryGetValue(tag, out TableTagCursor? cursor))
        {
            // Constructing it is what asks for the pad byte, so a tag whose expression cannot be typed
            // fails here — at the first attempt to use it, and not when the table was opened.
            cursor = new TableTagCursor(tag, [.. Fields], CodePageNumber ?? CodePageMap.UnmarkedCodePage);
            tagCursors[tag] = cursor;
        }

        return cursor;
    }

    /// <summary>
    /// Reads the record an indexed move reached, or reports that it reached none.
    /// </summary>
    private GoResult Reached(bool moved, int record)
    {
        if (!moved)
        {
            // Running out of entries puts the cursor at both ends, which is the shape d4skip's tag path
            // produces (d4skip.c:1281-1285) and the shape an empty table already has.
            MovePastEnd();
            position.MovedBeforeStart(endOfFileBefore: true);
            return GoResult.NoRecord;
        }

        Fetch(record);
        return GoResult.Ok;
    }

    /// <summary>
    /// Gets the bytes of a field in the current record, exactly as stored.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>A copy of that field's bytes.</returns>
    /// <exception cref="CodeBaseException">The field does not lie inside the record.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public byte[] GetRawBytes(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.Bytes(record, field).ToArray();
    }

    /// <summary>
    /// Gets a field of the current record as text.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>
    /// The field decoded through the table's code page, its trailing blanks kept. A character field
    /// is a fixed width and the padding is part of what the file holds, so a caller who wants it
    /// gone trims it. See ADR-21.
    ///
    /// Trim with [c]TrimEnd(' ')[/c] and not with [c]TrimEnd()[/c]. The padding is spaces, but the
    /// no-argument form removes every kind of trailing whitespace, and a tab or a newline at the end
    /// of a character field is data. See ADR-22.
    /// </returns>
    /// <exception cref="CodeBaseException">
    /// The encoding is unavailable because no provider for the legacy code pages is registered, or
    /// the field does not lie inside the record.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public string GetString(FieldDefinition field)
    {
        EnsureOpen();
        FieldValueDecoder.RefuseIfBinary(field);

        // Decoding recovers as much as the code page allows and never throws: a character the field
        // boundary cut in half yields its complete characters and a replacement, and a byte the code
        // page leaves undefined yields whatever it maps to. GetRawBytes is how a caller tells data
        // from damage. See ADR-21.
        return TextEncoding.GetString(FieldValueDecoder.Bytes(record, field));
    }

    /// <summary>
    /// Gets a field of the current record as a truth value.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>True for the four letters the reference accepts as true, false for anything else.</returns>
    /// <exception cref="CodeBaseException">The field is not a logical one.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public bool GetBoolean(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.Boolean(record, field);
    }

    /// <summary>
    /// Gets a field of the current record as a whole number.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>The value, converted as the reference converts it for this type.</returns>
    /// <exception cref="CodeBaseException">The field's type refuses to be read as a number.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public int GetInt32(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.Int32(record, field);
    }

    /// <summary>
    /// Gets a field of the current record as a number.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>
    /// The value. A date answers with its Julian day number and a currency with its four-decimal
    /// value, both because the reference implementation converts them that way.
    /// </returns>
    /// <exception cref="CodeBaseException">The field's type refuses to be read as a number.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public double GetDouble(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.Double(record, field);
    }

    /// <summary>
    /// Gets a currency field of the current record without the rounding a double would introduce.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>The exact value.</returns>
    /// <exception cref="CodeBaseException">The field is not a currency one.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public decimal GetDecimal(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.Decimal(record, field);
    }

    /// <summary>
    /// Gets a date field of the current record.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>The date, or null where the field is blank or holds no date.</returns>
    /// <exception cref="CodeBaseException">The field is not a date one.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public DateOnly? GetDate(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.Date(record, field);
    }

    /// <summary>
    /// Gets a datetime field of the current record.
    /// </summary>
    /// <param name="field">The field to read.</param>
    /// <returns>The moment, with its milliseconds, or null where the field is blank.</returns>
    /// <exception cref="CodeBaseException">The field is not a datetime one.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public DateTime? GetDateTime(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.DateTime(record, field);
    }

    /// <summary>
    /// Gets the memo block a field of the current record refers to.
    /// </summary>
    /// <param name="field">The memo field to read.</param>
    /// <returns>
    /// The block number, or zero where the record has no memo in this field. For diagnostics: the
    /// value accessors below take the field, not a block.
    /// </returns>
    /// <exception cref="CodeBaseException">The field does not hold a memo reference.</exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public int GetMemoBlock(FieldDefinition field)
    {
        EnsureOpen();
        return FieldValueDecoder.MemoBlock(record, field);
    }

    /// <summary>
    /// Gets the number of bytes in the memo a field of the current record refers to.
    /// </summary>
    /// <param name="field">The memo field to read.</param>
    /// <returns>The payload length, or zero where the record has no memo in this field.</returns>
    /// <exception cref="CodeBaseException">
    /// The field does not hold a memo reference, or the entry it names is unreadable.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public int GetMemoLength(FieldDefinition field) => ReadMemo(field).Payload.Length;

    /// <summary>
    /// Gets the contents of the memo a field of the current record refers to.
    /// </summary>
    /// <param name="field">The memo field to read.</param>
    /// <returns>
    /// The payload, verbatim, or an empty array where the record has no memo in this field. An
    /// absent memo and an empty one are the same thing here because the format cannot tell them
    /// apart.
    /// </returns>
    /// <exception cref="CodeBaseException">
    /// The field does not hold a memo reference, or the entry it names is unreadable.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public byte[] GetMemoBytes(FieldDefinition field) => ReadMemo(field).Payload;

    /// <summary>
    /// Gets what the memo a field of the current record refers to declares itself to hold.
    /// </summary>
    /// <param name="field">The memo field to read.</param>
    /// <returns>
    /// The declared type. A record with no memo answers text, which is what an empty memo would also
    /// answer.
    /// </returns>
    /// <exception cref="CodeBaseException">
    /// The field does not hold a memo reference, or the entry it names is unreadable.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public MemoType GetMemoType(FieldDefinition field) => ReadMemo(field).Type;

    /// <summary>
    /// Gets the memo a field of the current record refers to, as text.
    /// </summary>
    /// <param name="field">The memo field to read.</param>
    /// <returns>
    /// The payload decoded through the table's code page. A memo has no declared width and so no
    /// padding, which is the one way it differs from a character field.
    /// </returns>
    /// <exception cref="CodeBaseException">
    /// The field is a binary memo or a general field, whose bytes are not text; or it holds no memo
    /// reference; or the entry it names is unreadable; or no encoding provider is registered.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public string GetMemoString(FieldDefinition field)
    {
        FieldValueDecoder.RefuseIfBinary(field);

        // Decoded by the same rules as any other text: as much recovered as the code page allows,
        // and never a throw for a byte it cannot map. See ADR-21.
        return TextEncoding.GetString(ReadMemo(field).Payload);
    }

    /// <summary>
    /// Gets whether a field of the current record is marked null.
    /// </summary>
    /// <param name="field">The field to test.</param>
    /// <returns>
    /// Whether the record's bitmap has this field's bit set. Always false for a field the table did
    /// not declare nullable.
    /// </returns>
    /// <value>
    /// Marking a field null does not undo what was assigned to it, so the value accessors are
    /// unaffected by this and still report whatever bytes the field holds. See Decision 11.
    /// </value>
    /// <exception cref="ObjectDisposedException">The table has been closed.</exception>
    public bool IsNull(FieldDefinition field)
    {
        EnsureOpen();

        if (field.NullBit is not int bit || NullFlags is not FieldDefinition flags)
            return false;

        ReadOnlySpan<byte> bitmap = record.Field(flags);
        int byteIndex = bit / 8;

        return byteIndex < bitmap.Length && (bitmap[byteIndex] & (1 << (bit % 8))) != 0;
    }

    /// <summary>
    /// Closes the table and the files opened for it.
    /// </summary>
    public void Dispose()
    {
        if (closed)
            return;

        closed = true;
        opened.Index?.Dispose();
        opened.Memo?.Dispose();
        opened.Data.Dispose();
        onClosed(this);
    }

    /// <summary>
    /// Reads the entry a memo field of the current record points at.
    /// </summary>
    private MemoEntry ReadMemo(FieldDefinition field)
    {
        EnsureOpen();

        int block = FieldValueDecoder.MemoBlock(record, field);
        if (block <= MemoReference.None)
            return MemoEntry.Absent;

        if (memoReader is null)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"Field '{field.Name}' of record {position.Number} refers to memo block {block}, " +
                "but the table declares no memo file. The header and the field descriptors " +
                "disagree with each other.");
        }

        return memoReader.Read(block, $"Field '{field.Name}' of record {position.Number}");
    }

    /// <summary>
    /// Reads a record into the buffer and puts the cursor on it.
    /// </summary>
    private void Fetch(int recordNumber)
    {
        reader.Read(recordNumber, record);
        position.MovedTo(recordNumber);
    }

    /// <summary>
    /// Puts the cursor past the last record, with a blank record behind it.
    /// </summary>
    private void MovePastEnd()
    {
        position.MovedPastEnd(RecordCount);
        record.Blank(blankRecord);
    }

    /// <summary>
    /// Refuses to work on a table whose files have been closed.
    /// </summary>
    private void EnsureOpen() => ObjectDisposedException.ThrowIf(closed, this);
}
