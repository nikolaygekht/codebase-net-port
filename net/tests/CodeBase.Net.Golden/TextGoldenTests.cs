using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Reading a character field as text, under the code page the table names.
///
/// The dump records a character field's raw bytes and no decoded string, because the C library
/// transcodes nothing and so cannot say what the text is. The expected strings here therefore come
/// from the generator's own documented test data, which DEV_APPROACH.md section 4 allows and which
/// the comments beside every byte array in [c]case-cp1251.cpp[/c] and [c]case-cp936.cpp[/c] name.
/// The bytes themselves are still gated against the dump, by the record suite.
///
/// Everything asserted here was decided in ADR-21: cp437 for an unmarked table, best-effort
/// decoding that never throws, and trailing blanks kept.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class TextGoldenTests
{
    [Fact]
    public void ASingleByteCodePage_DecodesToTheTextTheGeneratorWrote()
    {
        using Reader reader = new("CP1251");
        reader.Go(1);

        reader.Text("TEXT").Should().Be("Привет, мир" + new string(' ', 9));
        reader.Text("EXACT").Should().Be("Компьютеры");
    }

    [Fact]
    public void AMultiByteCodePage_DecodesToTheTextTheGeneratorWrote()
    {
        using Reader reader = new("CP936");
        reader.Go(1);

        reader.Text("TEXT").Should().Be("中文测试" + new string(' ', 12));
        reader.Text("EXACT").Should().Be("中文测试");
    }

    [Fact]
    public void AMultiByteCodePage_DecodesTrailBytesThatAreThemselvesAscii()
    {
        // GBK trail bytes overlap ASCII, so a decoder that scanned for a backslash or a pipe byte by
        // byte would find one inside a character. The field is twelve bytes and six characters.
        using Reader reader = new("CP936");
        reader.Go(1);

        reader.Text("TRAIL").Should().Be("乗亅丄亊丂俓");
        reader.Bytes("TRAIL").Should().HaveCount(12);
    }

    [Fact]
    public void ACharacterCutInHalfByTheFieldBoundary_KeepsWhatSurvivedAndMarksTheRest()
    {
        // A field width is a byte count, so CP936's CUT is seven bytes holding eight bytes of text
        // and always ends on a lead byte with no trail byte. Decoding recovers the three whole
        // characters and marks the damage rather than refusing the field. See ADR-21.
        using Reader reader = new("CP936");
        reader.Go(1);

        reader.Text("CUT").Should().Be("中文测�");
    }

    [Theory]
    [MemberData(nameof(EveryRecordNumber))]
    public void TheCutFieldIsCutInEveryRecord_AndNeverThrows(int recordNumber)
    {
        // Not a corner case in this table: every one of the thirty-two records ends on a dangling
        // lead byte, which is what makes the behaviour worth deciding rather than inheriting.
        using Reader reader = new("CP936");
        reader.Go(recordNumber);

        string cut = reader.Text("CUT");

        cut.Should().EndWith("�");
        cut.Should().HaveLength(4);
    }

    [Fact]
    public void AByteTheCodePageLeavesUndefined_PassesThroughRatherThanBecomingAReplacement()
    {
        // Record 2 of CP1251 sweeps 0x90 to 0x9F. Windows-1251 defines all but 0x98, which .NET maps
        // to the C1 control at that position. Recovering it is worth more than flagging it, and the
        // rest of the row proves the sweep is otherwise decoded properly. See ADR-21.
        using Reader reader = new("CP1251");
        reader.Go(2);

        string sweep = reader.Text("SWEEP");

        sweep.Should().HaveLength(16);
        sweep[0].Should().Be('ђ', "0x90 is a defined character");
        sweep[8].Should().Be('', "0x98 is undefined and passes through");
        sweep[9].Should().Be('™', "0x99 is a defined character");
        sweep.Should().NotContain("�");
    }

    [Fact]
    public void AnUnmarkedTable_DecodesAsCodePage437()
    {
        // ADR-21 closed this in favour of 437. The table is unmarked, so nothing in the file decides
        // it and the default is the whole answer.
        using Reader reader = new("VFPTYPE");

        reader.Table.CodePageByte.Should().Be(0x00);
        reader.Table.CodePageNumber.Should().BeNull();
        reader.Table.TextEncoding.CodePage.Should().Be(437);

        reader.Go(1);
        reader.Text("F_C").Should().Be("ALPHA" + new string(' ', 15));
    }

    [Fact]
    public void GetString_KeepsTrailingBlanksBecauseTheFieldIsAFixedWidth()
    {
        // What f4str does: it copies the declared width and trims nothing. Trimming is one call at
        // the call site; un-trimming is impossible. See ADR-21 and Decision 16.
        using Reader reader = new("VFPTYPE");
        reader.Go(1);

        FieldDefinition field = reader.Table.Fields["F_C"];
        string value = reader.Text("F_C");

        value.Should().HaveLength(field.Length);
        value.Should().EndWith(" ");
        value.TrimEnd().Should().Be("ALPHA");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryCharacterFieldOfEveryRecord_DecodesToAsManyCharactersAsItsBytesAllowAndNeverThrows(
        string tableName)
    {
        // The property that has to hold everywhere, as opposed to the values checked above: decoding
        // is total. No field of any record in any code page raises, whatever its bytes are.
        CorpusDump dump = CorpusDump.Load(tableName);

        using Reader reader = new(tableName);
        int decoded = 0;

        foreach (DumpRecord record in dump.Records)
        {
            reader.Go(record.Number);

            foreach (DumpField field in dump.Fields.Where(f => f.Type == 'C'))
            {
                string value = reader.Text(field.Name);

                value.Should().NotBeNull();
                value.Length.Should().BeLessThanOrEqualTo(field.Length);
                decoded++;
            }
        }

        decoded.Should().Be(dump.Fields.Count(f => f.Type == 'C') * dump.RecordCount);
    }

    public static TheoryData<int> EveryRecordNumber()
    {
        TheoryData<int> data = [];
        for (int i = 1; i <= 32; i++)
            data.Add(i);
        return data;
    }

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }

    /// <summary>
    /// A corpus table open for reading, closed with the test.
    /// </summary>
    private sealed class Reader : IDisposable
    {
        private readonly CodeBaseEngine engine = new();

        public Reader(string tableName) =>
            Table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        public Table Table { get; }

        public void Go(int recordNumber) => Table.Go(recordNumber).Should().Be(GoResult.Ok);

        public string Text(string fieldName) => Table.GetString(Table.Fields[fieldName]);

        public byte[] Bytes(string fieldName) => Table.GetRawBytes(Table.Fields[fieldName]);

        public void Dispose() => engine.Dispose();
    }
}
