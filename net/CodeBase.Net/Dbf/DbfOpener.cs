using CodeBase.Net.Cdx;
using CodeBase.Net.IO;
using CodeBase.Net.Memo;

namespace CodeBase.Net.Dbf;

/// <summary>
/// Opens a table: decides what to read, in what order, and what to refuse.
///
/// Deliberately ignorant of byte layouts. It reads spans and hands them to the types that
/// understand them, which is what keeps the risky decoding testable without a file and the file
/// handling testable without a disk.
/// </summary>
internal sealed class DbfOpener
{
    /// <summary>
    /// The extension of the memo file that accompanies a table, as CodeBase writes it.
    /// </summary>
    private const string MemoExtension = ".fpt";

    /// <summary>
    /// The extension of the production index, as CodeBase writes it.
    /// </summary>
    /// <value>
    /// Lower case beside an upper-case table, the same asymmetry the memo file has (d4defs.h:2578,
    /// 2609). Companion resolution is case-insensitive, so this is the name we ask for rather than the
    /// name we insist on.
    /// </value>
    private const string IndexExtension = ".cdx";

    /// <summary>
    /// The bit of the table flags byte that says a production index was created.
    /// </summary>
    /// <value>Set by i4create when the index file is the table's own (i4create.c:1404-1418).</value>
    private const byte ProductionIndexFlag = 0x01;

    private readonly IRandomAccessSourceFactory factory;
    private readonly ICompanionFileResolver companions;

    /// <summary>
    /// Initializes a new instance over the boundaries it reads through.
    /// </summary>
    /// <param name="factory">Opens files.</param>
    /// <param name="companions">Finds the files that accompany a table.</param>
    public DbfOpener(IRandomAccessSourceFactory factory, ICompanionFileResolver companions)
    {
        this.factory = factory;
        this.companions = companions;
    }

    /// <summary>
    /// Opens a table and everything it declares.
    /// </summary>
    /// <param name="path">The table file.</param>
    /// <returns>Everything read, ready for a table to be built from.</returns>
    /// <exception cref="CodeBaseException">
    /// The file contradicts itself, contradicts its own length, names a field this library cannot
    /// read, or declares a memo file that is not there.
    /// </exception>
    public OpenedTable Open(string path)
    {
        IRandomAccessSource data = factory.Open(path);
        IRandomAccessSource? memo = null;
        IndexFileReader? index = null;

        try
        {
            DbfHeader header = DbfHeader.Parse(data.ReadExactly(0, DbfHeader.Size, "header"));
            CheckAgainstFileLength(header, data.Length);

            IDbfFormatVariant variant = IDbfFormatVariant.Resolve(header.Version);

            IReadOnlyList<FieldDescriptor> descriptors = FieldDescriptorTable.Parse(
                data.ReadExactly(DbfHeader.Size, header.HeaderLength - DbfHeader.Size, "field descriptors"),
                header.Flags.UsesLongFieldNames);

            ResolvedFields fields = FieldResolver.Resolve(descriptors, variant, header.RecordLength);

            MemoFileHeader? memoHeader = null;
            if (variant.HasMemo(header.TableFlags))
            {
                memo = OpenMemo(path);
                memoHeader = MemoFileHeader.Parse(memo.ReadExactly(0, MemoFileHeader.Size, "memo file header"));
            }

            // The index is opened last, and only when the header says there is one. A table that
            // declared an index and opens without it would navigate in record order and answer
            // differently, silently — the same reason a declared memo file is an error when missing.
            if ((header.TableFlags & ProductionIndexFlag) != 0)
                index = OpenIndex(path, fields.Fields);

            return new OpenedTable(data, memo, index, header, variant, descriptors, fields, memoHeader);
        }
        catch
        {
            // Whatever was opened before the failure is closed here. Handing a caller an exception
            // and an open file handle is its own kind of defect.
            index?.Dispose();
            memo?.Dispose();
            data.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Refuses a header that describes more file than there is.
    /// </summary>
    private static void CheckAgainstFileLength(DbfHeader header, long fileLength)
    {
        if (header.HeaderLength > fileLength)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"The header claims to be {header.HeaderLength} bytes but the file is {fileLength}.");
        }

        // In 64-bit arithmetic, so a record count that would overflow a 32-bit product cannot wrap
        // into a plausible-looking one.
        long capacity = (fileLength - header.HeaderLength) / header.RecordLength;
        if (header.RecordCount > capacity)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"The header claims {header.RecordCount} records but the file holds room for " +
                $"{capacity}.");
        }
    }

    /// <summary>
    /// Opens the memo file a table declares, which has to be there if it says so.
    /// </summary>
    private IRandomAccessSource OpenMemo(string tablePath)
    {
        string? memoPath = companions.Resolve(tablePath, MemoExtension)
            ?? throw new CodeBaseException(
                ErrorCode.Data,
                $"The table declares a memo file but no '{MemoExtension}' file accompanies " +
                $"'{tablePath}'.");

        try
        {
            return factory.Open(memoPath);
        }
        catch (IOException failure)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"The memo file '{memoPath}' could not be opened.",
                failure);
        }
    }

    /// <summary>
    /// Opens the production index a table declares, which has to be there if it says so.
    /// </summary>
    /// <param name="tablePath">The table file.</param>
    /// <param name="fields">The table's fields, which settle each tag's pad byte (ADR-28).</param>
    /// <returns>The open index.</returns>
    /// <exception cref="CodeBaseException">
    /// The header declares an index and no file accompanies the table, or the file cannot be read.
    /// </exception>
    private IndexFileReader OpenIndex(string tablePath, IReadOnlyList<FieldDefinition> fields)
    {
        string? indexPath = companions.Resolve(tablePath, IndexExtension)
            ?? throw new CodeBaseException(
                ErrorCode.Data,
                $"The table declares a production index but no '{IndexExtension}' file accompanies " +
                $"'{tablePath}'.");

        IRandomAccessSource source;

        try
        {
            source = factory.Open(indexPath);
        }
        catch (IOException failure)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"The index file '{indexPath}' could not be opened.",
                failure);
        }

        // The resolver is asked only for a machine-collated tag, and only when its tag is opened, so a
        // tag whose expression this library cannot type does not stop the file from opening. It fails
        // when that tag is selected instead.
        return IndexFileReader.Open(source, indexPath, header => KeyTypeResolver.PadByteFor(header, fields));
    }
}

/// <summary>
/// Everything reading a table's header produced, including the files still open for it.
/// </summary>
/// <param name="Data">The open table file.</param>
/// <param name="Memo">The open memo file, or null when the table has none.</param>
/// <param name="Index">The open production index, or null when the table declares none.</param>
/// <param name="Header">The decoded file header.</param>
/// <param name="Variant">How this version of the format is read.</param>
/// <param name="Descriptors">The field descriptors as stored.</param>
/// <param name="Fields">The resolved fields, with the null-flags field held apart.</param>
/// <param name="MemoHeader">The memo file's header, or null when the table has no memo.</param>
internal sealed record OpenedTable(
    IRandomAccessSource Data,
    IRandomAccessSource? Memo,
    IndexFileReader? Index,
    DbfHeader Header,
    IDbfFormatVariant Variant,
    IReadOnlyList<FieldDescriptor> Descriptors,
    ResolvedFields Fields,
    MemoFileHeader? MemoHeader);
