using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Tests.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What a table holding one of CodeBase's UTF-16 fields promises once open.
///
/// The table is built in memory, its bytes laid out as CodeBase writes them; what the reference
/// writes and reads is the corpus case WSTR's to say. These tests catch what the decoder tests cannot:
/// the public accessors reaching the wide path rather than the code page's.
/// </summary>
[Trait("Layer", "Component")]
public sealed class WideStringTableTests
{
    [Fact]
    public void GetString_ReturnsTheStoredCharactersWithTheirBlankTail()
    {
        // "AB" assigned to a four-character field: CodeBase blanks the rest to U+2000 (ADR-76).
        (CodeBaseEngine engine, Table table) = Open(new("K_W", 'W', 8), Wide("AB\u2000\u2000"));
        using (engine)
        using (table)
        {
            table.Top();
            table.GetString(table.Fields["K_W"]).Should().Be("AB\u2000\u2000");
        }
    }

    [Fact]
    public void GetString_OfAnO_LeavesOutItsLengthBytes()
    {
        (CodeBaseEngine engine, Table table) = Open(new("K_O", 'O', 6), [.. Wide("AB"), 0x02, 0x00]);
        using (engine)
        using (table)
        {
            table.Top();
            table.GetString(table.Fields["K_O"]).Should().Be("AB");
        }
    }

    [Fact]
    public void GetString_IgnoresTheTableCodePage()
    {
        // UTF-16 whatever the table says it holds: a Cyrillic letter comes back as itself.
        (CodeBaseEngine engine, Table table) = Open(
            new("K_W", 'W', 4), Wide("\u0416\u2000"), Encoding.Latin1);
        using (engine)
        using (table)
        {
            table.Top();
            table.GetString(table.Fields["K_W"]).Should().Be("\u0416\u2000");
        }
    }

    [Fact]
    public void GetDouble_OfAWideString_IsRefused()
    {
        (CodeBaseEngine engine, Table table) = Open(new("K_W", 'W', 4), Wide("12"));
        using (engine)
        using (table)
        {
            table.Top();
            Action act = () => table.GetDouble(table.Fields["K_W"]);

            act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.FieldType);
        }
    }

    [Fact]
    public void AFilterOverStrOfAWideFieldNarrowsByTheTablesCodePage()
    {
        // The table reads cp1251, so STR narrows a Cyrillic letter to its cp1251 byte, and the blank
        // tail to spaces, which is what the literal is compared against (ADR-77).
        IndexedTableImage.KeyField field = new("K_W", 'W', 4);
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            field,
            [Wide("\u0416\u2000"), Wide("A\u2000"), Wide("\u0416\u0416")],
            [],
            encoding: Encoding.GetEncoding(1251));
        using (engine)
        using (table)
        {
            table.Query("STR(K_W) = " + '"' + "\u0416 " + '"').Count.Should().Be(1);
            table.Query("STR(K_W) = " + '"' + "A " + '"').Count.Should().Be(1);
        }
    }

    [Fact]
    public void AWideTagIsSoughtByTheTextAsUtf16()
    {
        // The key is the text's code units swapped (i4conv.c:184-189); a whole seek pads with the
        // blank CodeBase assigns, U+2000, and a prefix seek does not pad at all.
        string[] values = ["AB", "A\u2000", "\u0416\u2000"];
        (CodeBaseEngine engine, Table table) = IndexedTableImage.Open(
            new IndexedTableImage.KeyField("K_W", 'W', 4),
            [.. values.Select(Wide)],
            [.. values
                .Select((v, i) => (Key: Encoding.BigEndianUnicode.GetBytes(v), Record: (uint)(i + 1)))
                .OrderBy(k => k.Key, Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)))]);
        using (engine)
        using (table)
        {
            table.SelectTag(table.Tags[0]);

            table.Seek("A").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(2);

            table.Seek("\u0416").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(3);

            table.SeekPrefix("A").Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be(1, "B sorts below U+2000, so AB is the first key beginning with A");

            table.Seek("B").Should().Be(GoResult.NoRecord);
        }
    }

    private static byte[] Wide(string text) => Encoding.Unicode.GetBytes(text);

    private static (CodeBaseEngine Engine, Table Table) Open(
        IndexedTableImage.KeyField field, byte[] value, Encoding? encoding = null) =>
        IndexedTableImage.Open(field, [value], [], encoding: encoding);
}
