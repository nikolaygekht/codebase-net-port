using System.Text;
using CodeBase.Net.Dbf;

namespace CodeBase.Net;

/// <summary>
/// An open table, and everything its header says about itself.
///
/// Reading records is not part of this yet. What a table can tell you now is its shape: how many
/// records it holds and how wide they are, what fields it has and where each sits, whether it has a
/// memo file, and what code page its text is in.
///
/// Closing a table closes the files it opened. Closing the engine that opened it does the same, so
/// a caller that keeps the engine in a using statement cannot leak a handle by forgetting a table.
/// </summary>
public sealed class Table : IDisposable
{
    private readonly OpenedTable opened;
    private readonly Encoding? defaultEncoding;
    private readonly Action<Table> onClosed;
    private Encoding? textEncoding;
    private bool closed;

    internal Table(string path, OpenedTable opened, Encoding? defaultEncoding, Action<Table> onClosed)
    {
        Path = path;
        this.opened = opened;
        this.defaultEncoding = defaultEncoding;
        this.onClosed = onClosed;

        Fields = new FieldCollection(opened.Fields.Fields);
        CodePage = CodePageMap.Resolve(opened.Header.CodePage);
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
    /// Gets the code page the table names for its text.
    /// </summary>
    public CodePage CodePage { get; }

    /// <summary>
    /// Gets the language-driver byte exactly as stored, whether or not it names a known code page.
    /// </summary>
    public byte CodePageByte => opened.Header.CodePage;

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
    /// Gets a value indicating whether the table declares a production index.
    /// </summary>
    /// <value>Reading that index is not implemented yet; this only reports the claim.</value>
    public bool HasProductionIndex => (opened.Header.TableFlags & 0x01) != 0;

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
    /// Closes the table and the files opened for it.
    /// </summary>
    public void Dispose()
    {
        if (closed)
            return;

        closed = true;
        opened.Memo?.Dispose();
        opened.Data.Dispose();
        onClosed(this);
    }
}
