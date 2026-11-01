using System.Text;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using CodeBase.Net.Tests.Dbf;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// Opens a small table together with an index, for the shapes the corpus does not hold.
///
/// Every corpus index is consistent with its table, so the case that matters most here — an index entry
/// naming a record the table does not have — is only reachable by building the pair deliberately. The same
/// goes for a table of two records, an index whose tag cannot be typed, and a header that declares an index
/// with no file behind it.
///
/// Hand-built bytes are legitimate test [i]input[/i]; what a record decodes [i]to[/i] still comes from the
/// corpus. See DEV_APPROACH.md section 4.
/// </summary>
internal static class IndexedTableImage
{
    /// <summary>The single character field every table built here holds.</summary>
    public const string FieldName = "TEXT";

    /// <summary>Its width, and so the key length of a tag over it.</summary>
    public const int FieldWidth = 6;

    /// <summary>
    /// Opens a table whose header declares a production index, with that index beside it.
    /// </summary>
    /// <param name="values">The value of the one field in each record, space-padded to fit.</param>
    /// <param name="entries">The index entries, in key order, as key text and record number.</param>
    /// <param name="expression">The tag's key expression. Defaults to the field's own name.</param>
    /// <param name="descending">Whether the tag is descending.</param>
    /// <param name="encoding">The encoding to read text with, for a host with no provider registered.</param>
    /// <returns>The open table and the engine that owns it.</returns>
    public static (CodeBaseEngine Engine, Table Table) Open(
        string[] values,
        (string Key, uint Record)[] entries,
        string expression = FieldName,
        bool descending = false,
        System.Text.Encoding? encoding = null)
    {
        CodeBaseEngine engine = new(
            new PairFactory(Table(values), Index(entries, expression, descending)),
            new AlwaysCompanion())
        {
            // Read when the table is opened, so it has to be set before rather than after. A test
            // that seeks by text needs it: this project registers no code-page provider (ADR-17
            // leaves that to the host), and the image's unmarked table asks for cp437.
            DefaultEncoding = encoding,
        };

        return (engine, engine.OpenTable("memory.dbf"));
    }

    /// <summary>
    /// Opens a table of one field of any type, with a single tag over it holding the given keys.
    /// </summary>
    /// <param name="field">The field, which a nullable one follows with its null-flags field.</param>
    /// <param name="values">Each record's stored bytes for the field, or null where it is absent.</param>
    /// <param name="keys">The tag's keys, in key order, as stored bytes and record number.</param>
    /// <param name="expression">The tag's key expression. Defaults to the field's own name.</param>
    /// <param name="collation">The tag's collation, empty for machine order.</param>
    /// <param name="encoding">The encoding to read text with.</param>
    /// <returns>The open table and the engine that owns it.</returns>
    /// <value>
    /// The keys are the test's input, not its expectation, so a test may build them with the key
    /// transforms, which the golden suite holds to stored keys on their own. What the test expects is
    /// which record a seek reaches.
    /// </value>
    public static (CodeBaseEngine Engine, Table Table) Open(
        KeyField field,
        byte[]?[] values,
        (byte[] Key, uint Record)[] keys,
        string? expression = null,
        string collation = "",
        System.Text.Encoding? encoding = null)
    {
        int keyLength = keys.Length > 0 ? keys[0].Key.Length : field.Length + (field.Nullable ? 1 : 0);
        byte padByte = field.Type == 'C' && collation.Length == 0 ? (byte)' ' : (byte)0;

        byte[] index = IndexImage.SingleTag(
                keyLength,
                IndexImage.NodeOf(0),
                typeCode: (byte)(field.Nullable ? 0x22 : 0x20),
                expression: expression ?? field.Name,
                collation: collation)
            .WithLeaf(keyLength, [.. keys.Select(k => (Encoding.Latin1.GetString(k.Key), k.Record))], padByte)
            .Build();

        CodeBaseEngine engine = new(new PairFactory(Table(field, values), index), new AlwaysCompanion())
        {
            DefaultEncoding = encoding,
        };

        return (engine, engine.OpenTable("memory.dbf"));
    }

    /// <summary>
    /// The one field a typed table holds.
    /// </summary>
    /// <param name="Name">The field's name.</param>
    /// <param name="Type">Its type letter.</param>
    /// <param name="Length">Its stored width.</param>
    /// <param name="Decimals">Its decimal places.</param>
    /// <param name="Nullable">Whether it may be absent.</param>
    public sealed record KeyField(string Name, char Type, int Length, int Decimals = 0, bool Nullable = false);

    /// <summary>
    /// Builds a typed table's bytes: the field, then a null-flags field when it may be absent.
    /// </summary>
    private static byte[] Table(KeyField field, byte[]?[] values)
    {
        int fieldCount = field.Nullable ? 2 : 1;
        int headerLength = 32 + (32 * fieldCount) + 1;
        int recordLength = 1 + field.Length + (field.Nullable ? 1 : 0);

        byte[] header = HeaderBytes.Build(
            recordCount: values.Length,
            headerLength: headerLength,
            recordLength: recordLength,
            tableFlags: 0x01);

        byte[] image = new byte[headerLength + (values.Length * recordLength)];
        header.CopyTo(image.AsSpan());
        DescriptorBytes.Build(
                field.Name,
                field.Type,
                storedOffset: 1,
                length: (byte)field.Length,
                decimals: (byte)field.Decimals,
                flags: field.Nullable ? FieldFlags.Nullable : FieldFlags.None)
            .CopyTo(image.AsSpan(32));

        if (field.Nullable)
        {
            DescriptorBytes.Build("_NullFlags", '0', storedOffset: 1 + field.Length, length: 1, flags: FieldFlags.System)
                .CopyTo(image.AsSpan(64));
        }

        image[headerLength - 1] = 0x0D;

        for (int i = 0; i < values.Length; i++)
        {
            Span<byte> record = image.AsSpan(headerLength + (i * recordLength), recordLength);
            record.Fill((byte)' ');
            values[i]?.CopyTo(record[1..]);

            if (field.Nullable)
                record[^1] = values[i] is null ? (byte)0x01 : (byte)0x00;
        }

        return image;
    }

    /// <summary>
    /// Builds an engine over a table whose header declares a production index that is not there.
    /// </summary>
    /// <param name="values">The value of the one field in each record.</param>
    /// <returns>The engine, so the caller can attempt the open itself.</returns>
    public static CodeBaseEngine WithoutTheIndexFile(params string[] values) =>
        new(new PairFactory(Table(values), index: null), new AlwaysCompanion());

    /// <summary>
    /// Builds the table's bytes, with the production-index bit set in its header.
    /// </summary>
    private static byte[] Table(string[] values)
    {
        const int headerLength = 32 + 32 + 1;
        const int recordLength = 1 + FieldWidth;

        byte[] header = HeaderBytes.Build(
            recordCount: values.Length,
            headerLength: headerLength,
            recordLength: recordLength,
            tableFlags: 0x01);

        byte[] image = new byte[headerLength + (values.Length * recordLength)];
        header.CopyTo(image.AsSpan());
        DescriptorBytes.Build(FieldName, 'C', storedOffset: 1, length: FieldWidth).CopyTo(image.AsSpan(32));
        image[64] = 0x0D;

        for (int i = 0; i < values.Length; i++)
        {
            int at = headerLength + (i * recordLength);
            image.AsSpan(at, recordLength).Fill((byte)' ');
            Encoding.ASCII.GetBytes(values[i].PadRight(FieldWidth)[..FieldWidth]).CopyTo(image.AsSpan(at + 1));
        }

        return image;
    }

    /// <summary>
    /// Builds a single-tag index over the field, holding the given entries in one leaf.
    /// </summary>
    private static byte[] Index((string Key, uint Record)[] entries, string expression, bool descending) =>
        IndexImage.SingleTag(FieldWidth, IndexImage.NodeOf(0), expression: expression, descending: descending)
            .WithLeaf(FieldWidth, entries)
            .Build();

    /// <summary>
    /// Hands out the table for the first open and the index for the second, which is the order
    /// [c]DbfOpener[/c] opens them in.
    /// </summary>
    private sealed class PairFactory(byte[] table, byte[]? index) : IRandomAccessSourceFactory
    {
        private bool tableOpened;

        public IRandomAccessSource Open(string path)
        {
            if (!tableOpened)
            {
                tableOpened = true;
                return new InMemorySource(table);
            }

            return index is null
                ? throw new IOException("The index file is not there.")
                : new InMemorySource(index);
        }
    }

    /// <summary>Answers every companion request, so the header's declaration is what decides.</summary>
    private sealed class AlwaysCompanion : ICompanionFileResolver
    {
        public string? Resolve(string tablePath, string extension) => "memory" + extension;
    }
}
