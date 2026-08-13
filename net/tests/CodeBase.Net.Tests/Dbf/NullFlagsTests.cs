using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What [c]IsNull[/c] answers when the null bitmap does not cover every nullable field.
///
/// The corpus proves the ordinary case: VFPNULL has seven nullable fields and a bitmap wide enough
/// for them. Nothing in the header format ties the two together, though, so a table can declare
/// more nullable fields than its [c]_NullFlags[/c] field has bits, and no corpus case can say what
/// happens then.
///
/// Hand-built bytes are legitimate test [i]input[/i]. See DEV_APPROACH.md section 4.
/// </summary>
[Trait("Layer", "Component")]
public sealed class NullFlagsTests
{
    [Fact]
    public void IsNull_ForABitPastTheEndOfTheBitmap_IsNotNull()
    {
        // The defensive reading, and the one that keeps a truncated bitmap readable rather than
        // turning it into an error. It is a decision about a field's *value*, not just a guard, so
        // it is stated here: a missing bit means "no null flag set", never "null".
        using TableWithNarrowNullBitmap table = new();

        table.Table.Top();

        table.Table.IsNull(table.Table.Fields["COVERED"]).Should().BeTrue("its bit is set in byte 0");
        table.Table.IsNull(table.Table.Fields["BEYOND"]).Should().BeFalse("its bit is past the bitmap");
    }

    [Fact]
    public void IsNull_ForAFieldTheTableDoesNotDeclareNullable_IsNotNull()
    {
        using TableWithNarrowNullBitmap table = new();

        table.Table.Top();

        table.Table.IsNull(table.Table.Fields["PLAIN"]).Should().BeFalse();
    }

    /// <summary>
    /// A table whose bitmap is one byte while its ninth nullable field needs bit eight.
    ///
    /// Ten fields: one plain, then nine nullable, then the one-byte hidden bitmap. The ninth
    /// nullable field lands on bit 8, which is byte 1 of a bitmap that has only byte 0.
    /// </summary>
    private sealed class TableWithNarrowNullBitmap : IDisposable
    {
        private readonly CodeBaseEngine engine;

        public TableWithNarrowNullBitmap()
        {
            const int NullableCount = 9;
            const int HeaderLength = 32 + ((2 + NullableCount) * 32) + 1;
            const int RecordLength = 1 + 1 + NullableCount + 1;

            List<byte[]> descriptors =
            [
                DescriptorBytes.Build("PLAIN", 'C', storedOffset: 1, length: 1),
            ];

            for (int i = 0; i < NullableCount; i++)
            {
                descriptors.Add(DescriptorBytes.Build(
                    i == 0 ? "COVERED" : i == NullableCount - 1 ? "BEYOND" : $"N{i}",
                    'C',
                    storedOffset: 2 + i,
                    length: 1,
                    flags: FieldFlags.Nullable));
            }

            descriptors.Add(DescriptorBytes.Build(
                "_NullFlags",
                '0',
                storedOffset: 2 + NullableCount,
                length: 1,
                flags: FieldFlags.System | FieldFlags.Nullable));

            byte[] image = new byte[HeaderLength + RecordLength];

            HeaderBytes.Build(
                recordCount: 1,
                headerLength: HeaderLength,
                recordLength: RecordLength).CopyTo(image.AsSpan());

            for (int i = 0; i < descriptors.Count; i++)
                descriptors[i].CopyTo(image.AsSpan(32 + (i * 32)));

            image[32 + (descriptors.Count * 32)] = 0x0D;

            // The record: not deleted, then ten single-character fields. The last is the bitmap, and
            // bit 0 is set, which is COVERED's. BEYOND's bit 8 has no byte to live in.
            image.AsSpan(HeaderLength).Fill((byte)' ');
            image[HeaderLength] = (byte)' ';
            image[^1] = 0b0000_0001;

            engine = new CodeBaseEngine(new StubFactory(new InMemorySource(image)), new NoCompanions());
            Table = engine.OpenTable("narrow.dbf");
        }

        public Table Table { get; }

        public void Dispose()
        {
            Table.Dispose();
            engine.Dispose();
        }
    }
}
