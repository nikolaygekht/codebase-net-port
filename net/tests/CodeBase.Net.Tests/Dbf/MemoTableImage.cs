using System.Buffers.Binary;
using System.Text;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// Opens a two-record table with a memo file beside it, holding a field of each type the typed
/// readers decode: a memo, a logical, a currency, a date and a datetime.
/// </summary>
/// <value>
/// Record 1 holds a value in every field, its memo the text HELLO in block 8; record 2 holds the
/// empty forms -- no memo, a blank date, a zero datetime. The layouts are DBF-FORMAT.md's and
/// FPT-MEMO.md's: the memo file's header and block header are big-endian.
/// </value>
internal static class MemoTableImage
{
    /// <summary>The memo file's block size.</summary>
    public const int BlockSize = 64;

    /// <summary>The block record 1's memo is in.</summary>
    public const int MemoBlock = 8;

    /// <summary>Record 1's memo text.</summary>
    public const string Memo = "HELLO";

    private const int RecordLength = 1 + 4 + 1 + 8 + 8 + 8;

    /// <summary>
    /// Opens the table and its memo file.
    /// </summary>
    /// <param name="declareMemo">
    /// Whether the header declares the memo file; false makes record 1's memo pointer one the table
    /// has nowhere to follow.
    /// </param>
    /// <returns>The open table and the engine that owns it.</returns>
    public static (CodeBaseEngine Engine, Table Table) Open(bool declareMemo = true)
    {
        CodeBaseEngine engine = new(new ByExtension(Table(declareMemo), MemoFile()), new AlwaysCompanion())
        {
            DefaultEncoding = Encoding.Latin1,
        };

        return (engine, engine.OpenTable("memory.dbf"));
    }

    private static byte[] Table(bool declareMemo)
    {
        const int fieldCount = 5;
        const int headerLength = 32 + (32 * fieldCount) + 1;

        byte[] image = new byte[headerLength + (2 * RecordLength)];
        HeaderBytes.Build(recordCount: 2, headerLength: headerLength, recordLength: RecordLength, tableFlags: declareMemo ? (byte)0x02 : (byte)0x00)
            .CopyTo(image.AsSpan());

        DescriptorBytes.Build("NOTE", 'M', storedOffset: 1, length: 4).CopyTo(image.AsSpan(32));
        DescriptorBytes.Build("FLAG", 'L', storedOffset: 5, length: 1).CopyTo(image.AsSpan(64));
        DescriptorBytes.Build("PRICE", 'Y', storedOffset: 6, length: 8, decimals: 4).CopyTo(image.AsSpan(96));
        DescriptorBytes.Build("BORN", 'D', storedOffset: 14, length: 8).CopyTo(image.AsSpan(128));
        DescriptorBytes.Build("STAMP", 'T', storedOffset: 22, length: 8).CopyTo(image.AsSpan(160));
        image[headerLength - 1] = 0x0D;

        Span<byte> first = image.AsSpan(headerLength, RecordLength);
        first[0] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(first[1..], MemoBlock);
        first[5] = (byte)'T';
        BinaryPrimitives.WriteInt64LittleEndian(first[6..], 125000);
        Encoding.ASCII.GetBytes("19870615").CopyTo(first[14..]);
        BinaryPrimitives.WriteInt32LittleEndian(first[22..], 2451559);
        BinaryPrimitives.WriteInt32LittleEndian(first[26..], ((12 * 3600) + (34 * 60) + 56) * 1000);

        Span<byte> second = image.AsSpan(headerLength + RecordLength, RecordLength);
        second[0] = (byte)' ';
        second[5] = (byte)'F';
        BinaryPrimitives.WriteInt64LittleEndian(second[6..], -30000);
        Encoding.ASCII.GetBytes("        ").CopyTo(second[14..]);

        return image;
    }

    private static byte[] MemoFile()
    {
        byte[] file = new byte[(MemoBlock + 1) * BlockSize];

        BinaryPrimitives.WriteUInt32BigEndian(file, MemoBlock + 1);
        BinaryPrimitives.WriteInt16BigEndian(file.AsSpan(6), BlockSize);

        Span<byte> block = file.AsSpan(MemoBlock * BlockSize);
        BinaryPrimitives.WriteUInt32BigEndian(block, 1);
        BinaryPrimitives.WriteUInt32BigEndian(block[4..], (uint)Memo.Length);
        Encoding.ASCII.GetBytes(Memo).CopyTo(block[8..]);

        return file;
    }

    /// <summary>Hands out the table or the memo file by the extension asked for.</summary>
    private sealed class ByExtension(byte[] table, byte[] memo) : IRandomAccessSourceFactory
    {
        public IRandomAccessSource Open(string path) =>
            new InMemorySource(path.EndsWith(".fpt", StringComparison.OrdinalIgnoreCase) ? memo : table);
    }

    /// <summary>Answers every companion request.</summary>
    private sealed class AlwaysCompanion : ICompanionFileResolver
    {
        public string? Resolve(string tablePath, string extension) => "memory" + extension;
    }
}
