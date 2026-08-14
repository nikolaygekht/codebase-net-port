using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The gate for building a key from a value: what this port computes against what the C library
/// actually stored.
///
/// This is the only layer that can say the transforms are right. A unit test can prove keys order
/// the way their values do, and every sign rule can be self-consistently wrong while doing it. The
/// corpus holds both halves side by side — each tag's stored keys, and the field values in the same
/// record dump they were computed from — so the comparison needs no expected bytes written down and
/// no new generator case.
///
/// The datetime tag is why CDXTIME exists. Its key is the only one that is not arithmetic: an
/// empirical bitmap decides whether the computed double is nudged down first, and nothing but real
/// keys can check a copy of it.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class KeyTransformGoldenTests
{
    /// <summary>Every indexed corpus table, with the index file beside it.</summary>
    public static TheoryData<string, string> IndexedTables() => new()
    {
        { "CDXBASE", "CDXBASE.cdx" },
        { "CDXCOLL", "CDXCOLL.cdx" },
        { "CDXDEEP", "CDXDEEP.cdx" },
        { "CDXTIME", "CDXTIME.cdx" },
        { "IDXONE", "IDXONE.cdx" },
    };

    [Theory]
    [MemberData(nameof(IndexedTables))]
    public void EveryKeyOfEveryTagIsRebuiltFromTheRecordItNames(string tableName, string indexFile)
    {
        // The gate for the selection table as a whole. Each tag resolves its own converter from the
        // table's fields and code page, and then every key it holds must come back byte for byte
        // from the value in the record it points at -- character, collated, numeric, double, date,
        // integer and datetime keys, ascending and descending, across every indexed case.
        CorpusIndexDump index = CorpusIndexDump.Load(indexFile);
        CorpusDump table = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table open = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        IReadOnlyList<FieldDefinition> fields = [.. open.Fields];
        int codePage = open.CodePageNumber ?? CodePageMap.UnmarkedCodePage;
        int checkedKeys = 0;

        using IndexFileReader reader = IndexFileReader.Open(
            new InMemorySource(Corpus.ReadAllBytes(indexFile)),
            indexFile,
            header => KeyTypeResolver.PadByteFor(header, fields));

        foreach (CdxTag tag in reader.Tags)
        {
            DumpIndexTag expected = index.RealTags.Single(t => t.Name.TrimEnd() == tag.Name.TrimEnd());
            SeekConverter converter = SeekConverter.For(tag.Header, fields, codePage);
            string field = converter.Field!.Name;

            foreach (DumpIndexKey stored in expected.Keys)
            {
                byte[] value = table.Records[(int)stored.Record - 1][field].Bytes;
                byte[] built = new byte[converter.KeyLength];

                RecordKey.WriteValue(converter, value, built).Should().Be(converter.KeyLength);

                built.Should().Equal(
                    stored.Key,
                    $"{tableName}.{tag.Name.TrimEnd()} record {stored.Record} ({converter.Kind})");

                checkedKeys++;
            }
        }

        checkedKeys.Should().Be(
            KeysPerTable[tableName],
            "a data-driven gate that quietly rebuilt fewer keys than the case holds would still pass");
    }

    /// <summary>How many keys each indexed case holds, across all of its tags.</summary>
    /// <value>
    /// Part of the gate rather than commentary. The totals are the tags' own counts from the dumps,
    /// so a case that stopped being read, or a tag that stopped being found, shows up as a number
    /// rather than as a quietly shorter run.
    /// </value>
    private static readonly Dictionary<string, int> KeysPerTable = new()
    {
        ["CDXBASE"] = 283,
        ["CDXCOLL"] = 68,
        ["CDXDEEP"] = 2400,
        ["CDXTIME"] = 512,
        ["IDXONE"] = 300,
    };

    [Fact]
    public void DateTime_EveryStoredKeyOfTheDatetimeTagIsReproducedFromItsFieldValue()
    {
        // 256 datetimes, chosen so that roughly a third land on a set bit of the decrement bitmap
        // and the rest do not, plus the day's edges and the calendar's. A wrong copy of the table,
        // a borrow that carries too far, or a rounding rule applied at the wrong point all show up
        // here as a key that does not match.
        (int Flagged, int Plain) counted = CompareTag("CDXTIME.cdx", "T_TS", "CDXTIME", "TS");

        counted.Flagged.Should().BeGreaterThan(60, "the bitmap's set half must be exercised");
        counted.Plain.Should().BeGreaterThan(60, "and so must its clear half");
    }

    [Fact]
    public void DateTime_TheDescendingTagStoresTheSameKeysAsTheAscendingOne()
    {
        // Descending inverts traversal, not the stored bytes (CDX-FORMAT.md section 7), so the same
        // transform must reproduce this tag too. A port that inverted keys instead would pass the
        // ascending test and fail here.
        CompareTag("CDXTIME.cdx", "T_TSD", "CDXTIME", "TS");
    }

    [Fact]
    public void General_EveryStoredKeyOfTheCollatedTagIsReproducedFromItsFieldValue()
    {
        // The gate for the COLL4ARR tables and the head-and-tail layout together. CDXCOLL indexes
        // one cp1252 C(20) field with GENERAL at keyLen 40, over accented text and the oe, ss and
        // th expansions -- so a wrong weight, a tail written in the wrong half, a trailing blank
        // left in, or an expansion that emits one head instead of two all fail here.
        CorpusIndexDump index = CorpusIndexDump.Load("CDXCOLL.cdx");
        CorpusDump table = CorpusDump.Load("CDXCOLL");
        DumpIndexTag tag = index.RealTags.Single(t => t.Name.TrimEnd() == "C_GEN");

        tag.KeyLength.Should().Be(40, "twice the twenty-byte field");

        foreach (DumpIndexKey stored in tag.Keys)
        {
            byte[] text = table.Records[(int)stored.Record - 1]["K_TEXT"].Bytes;

            byte[] built = new byte[tag.KeyLength];
            CollatedKey.Write(
                text,
                CollationTables.Cp1252General,
                CollationTables.Cp1252Expansions,
                includeTails: true,
                built).Should().Be(40);

            built.Should().Equal(stored.Key, $"record {stored.Record} holds '{Ascii(text)}'");
        }

        tag.Keys.Should().HaveCount(34);
    }

    [Fact]
    public void Machine_TheSameFieldKeyedWithoutCollationIsJustItsBytes()
    {
        // The control case beside it: the same twenty bytes, keyLen 20, no weighing at all. If the
        // GENERAL test passed because the port quietly stored raw text, this one would fail.
        CorpusIndexDump index = CorpusIndexDump.Load("CDXCOLL.cdx");
        CorpusDump table = CorpusDump.Load("CDXCOLL");
        DumpIndexTag tag = index.RealTags.Single(t => t.Name.TrimEnd() == "C_MACH");

        foreach (DumpIndexKey stored in tag.Keys)
            stored.Key.Should().Equal(table.Records[(int)stored.Record - 1]["K_TEXT"].Bytes);
    }

    private static string Ascii(byte[] bytes) =>
        new(bytes.Select(b => b is >= 32 and < 127 ? (char)b : '.').ToArray());

    /// <summary>
    /// Rebuilds every key of a tag from the field value of the record it names.
    /// </summary>
    /// <param name="indexFile">The corpus index file.</param>
    /// <param name="tagName">The tag whose keys to rebuild.</param>
    /// <param name="tableName">The table the records come from.</param>
    /// <param name="fieldName">The field the tag is built on.</param>
    /// <returns>How many keys carried the decrement flag, and how many did not.</returns>
    private static (int Flagged, int Plain) CompareTag(
        string indexFile, string tagName, string tableName, string fieldName)
    {
        CorpusIndexDump index = CorpusIndexDump.Load(indexFile);
        CorpusDump table = CorpusDump.Load(tableName);
        DumpIndexTag tag = index.RealTags.Single(t => t.Name.TrimEnd() == tagName);

        int flagged = 0;
        int plain = 0;

        foreach (DumpIndexKey stored in tag.Keys)
        {
            DumpRecord record = table.Records[(int)stored.Record - 1];
            byte[] raw = record[fieldName].Bytes;

            // The field holds the Julian day and the milliseconds, each a little-endian int32.
            int julian = BitConverter.ToInt32(raw, 0);
            int milliseconds = BitConverter.ToInt32(raw, 4);

            byte[] built = new byte[8];
            KeyTransform.FromDateTime(julian, milliseconds, built).Should().Be(8);

            built.Should().Equal(
                stored.Key,
                $"record {stored.Record} holds julian {julian} and {milliseconds} ms");

            if (DateTimeKeyFlags.NeedsDecrement(RoundedSecond(milliseconds)))
                flagged++;
            else
                plain++;
        }

        return (flagged, plain);
    }

    private static int RoundedSecond(int milliseconds)
    {
        int extra = milliseconds % 1000;
        int rounded = milliseconds - extra;

        if (extra >= 500)
            rounded += 1000;

        return rounded / 1000;
    }
}
