using System.Text;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// Builds a whole small table in memory and opens it, for the tables the corpus does not hold.
///
/// Every corpus table has thirty-two records, so an empty table and a one-record table exist only
/// here. Hand-built bytes are legitimate test [i]input[/i]; what a record decodes [i]to[/i] still
/// comes from the corpus. See DEV_APPROACH.md section 4.
/// </summary>
internal static class TableImage
{
    /// <summary>
    /// Header length for the layout below: the 32-byte header, one descriptor, and the terminator.
    /// </summary>
    private const int HeaderLength = 32 + 32 + 1;

    /// <summary>
    /// Record width for the layout below: the deletion flag and one six-byte character field.
    /// </summary>
    private const int RecordLength = 1 + 6;

    /// <summary>
    /// Opens a table holding the given records, each of which must fit the single field.
    /// </summary>
    /// <param name="records">
    /// The records, deletion flag included, for example [c]" REC001"[/c]. Six characters after the
    /// flag.
    /// </param>
    /// <returns>The open table, and the engine that owns it.</returns>
    public static (CodeBaseEngine Engine, Table Table) Open(params string[] records)
    {
        byte[] image = Build(records);
        CodeBaseEngine engine = new(new StubFactory(new InMemorySource(image)), new NoCompanions());

        return (engine, engine.OpenTable("memory.dbf"));
    }

    /// <summary>
    /// Builds the bytes of a table holding the given records.
    /// </summary>
    /// <param name="records">The records, deletion flag included.</param>
    /// <returns>The whole file.</returns>
    public static byte[] Build(params string[] records)
    {
        byte[] header = HeaderBytes.Build(
            recordCount: records.Length,
            headerLength: HeaderLength,
            recordLength: RecordLength);

        byte[] descriptor = DescriptorBytes.Build("TEXT", 'C', storedOffset: 1, length: 6);

        byte[] image = new byte[HeaderLength + (records.Length * RecordLength)];
        header.CopyTo(image.AsSpan());
        descriptor.CopyTo(image.AsSpan(32));
        image[64] = 0x0D;

        for (int i = 0; i < records.Length; i++)
        {
            if (records[i].Length != RecordLength)
            {
                throw new ArgumentException(
                    $"Record {i + 1} is '{records[i]}', which is {records[i].Length} characters " +
                    $"rather than {RecordLength}.",
                    nameof(records));
            }

            Encoding.ASCII.GetBytes(records[i]).CopyTo(image.AsSpan(HeaderLength + (i * RecordLength)));
        }

        return image;
    }
}
