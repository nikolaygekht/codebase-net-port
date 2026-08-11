using System.Text;
using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Guards the half of the dump reader that step 002's gate rests on.
///
/// Everything the record gate compares against is read here, so a reader that dropped a token or
/// mis-unescaped a byte would move the gate rather than fail it. The checks are against exact counts
/// and known values out of the real dumps, never against the reader's own output.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CorpusDumpRecordTests
{
    [Fact]
    public void Load_ReadsEveryRecordOfEveryFieldOfATable()
    {
        CorpusDump dump = CorpusDump.Load("VFPTYPE");

        dump.Records.Should().HaveCount(32);
        dump.Records.Should().OnlyContain(r => r.Values.Count == 9);
        dump.Records[0].Number.Should().Be(1);
        dump.Records[^1].Number.Should().Be(32);
    }

    [Fact]
    public void Load_ReadsTheDeletionFlagOfEveryRecord()
    {
        // Gated in one state only: the generator writes no deleted record. Named as a gap in the
        // step's SUMMARY.md rather than left to be discovered.
        CorpusDump.Load("VFPTYPE").Records.Should().OnlyContain(r => !r.Deleted);
    }

    [Fact]
    public void Load_ReadsAValueAsBytesRatherThanAsText()
    {
        DumpValue value = CorpusDump.Load("VFPTYPE").Records[0]["F_C"];

        value.Bytes.Should().HaveCount(20);
        Encoding.ASCII.GetString(value.Bytes).Should().Be("ALPHA               ");
    }

    [Fact]
    public void Load_UnescapesAHexEscape()
    {
        // The datetime of record 1: a Julian day and a zero time, none of it printable.
        DumpValue value = CorpusDump.Load("VFPTYPE").Records[0]["F_T"];

        value.Bytes.Should().Equal(0xAD, 0xD9, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00);
    }

    [Fact]
    public void Load_UnescapesAByteThatIsPrintableInsideAValueThatIsNot()
    {
        // The 0x24 above is a dollar sign, written verbatim in the middle of hex escapes. A reader
        // that unescaped only whole values, or split the line on spaces, gets this wrong.
        Corpus.ReadDump("VFPTYPE").Should().Contain("\"\\xAD\\xD9$\\x00");
    }

    [Theory]
    [InlineData("\"A\"", new byte[] { 0x41 })]
    [InlineData("\"\\x00\"", new byte[] { 0x00 })]
    [InlineData("\"\\xFF\"", new byte[] { 0xFF })]
    [InlineData("\"\\\\\"", new byte[] { 0x5C })]
    [InlineData("\"\\\"\"", new byte[] { 0x22 })]
    [InlineData("\"a b\"", new byte[] { 0x61, 0x20, 0x62 })]
    [InlineData("\"\"", new byte[0])]
    public void ReadQuoted_UnescapesEveryFormTheGeneratorWrites(string text, byte[] expected)
    {
        int index = 0;

        DumpEscape.ReadQuoted(text, ref index).Should().Equal(expected);
        index.Should().Be(text.Length);
    }

    [Theory]
    [InlineData("A\"")]
    [InlineData("\"unclosed")]
    [InlineData("\"\\q\"")]
    [InlineData("\"\\x0\"")]
    public void ReadQuoted_TextItCannotRead_IsRefused(string text)
    {
        int index = 0;

        Action act = () => DumpEscape.ReadQuoted(text, ref index);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Load_ReadsTheDecodedDoubleThatAccompaniesANumericField()
    {
        DumpValue value = CorpusDump.Load("VFPTYPE").Records[1]["F_N"];

        Encoding.ASCII.GetString(value.Bytes).Should().Be("-9999999.999");
        value.Number.Should().Be(-9999999.9989999998);
        value.Integer.Should().BeNull();
        value.Text.Should().BeNull();
    }

    [Fact]
    public void Load_ReadsNegativeZeroAsNegativeZero()
    {
        // Written as "dbl=-0", and the sign is the whole point: a parse that normalized it would
        // make the gate unable to see the port normalizing it too. See Decision 8.
        double? value = CorpusDump.Load("VFPTYPE").Records[1]["F_B"].Number;

        value.Should().NotBeNull();
        double.IsNegative(value!.Value).Should().BeTrue();
        value.Value.Should().Be(0.0);
    }

    [Fact]
    public void Load_ReadsTheDecodedIntegerThatAccompaniesAnIntegerField()
    {
        IReadOnlyList<DumpRecord> records = CorpusDump.Load("VFPTYPE").Records;

        records.Select(r => r["F_I"].Integer).Should().Contain([-2147483648L, 2147483647L]);
    }

    [Fact]
    public void Load_ReadsABracketedStringThatHoldsSpaces()
    {
        // The blank date and the blank datetime, both of record 3. Splitting the line on whitespace
        // would truncate these to "[" and the gate would then assert nothing about either form.
        DumpRecord record = CorpusDump.Load("VFPTYPE").Records.Single(r => r.Number == 3);

        record["F_T"].Text.Should().Be("        00:00:00");
        record["F_D"].Text.Should().Be("        ");
    }

    [Fact]
    public void Load_ReadsTheDateStringOfADateField()
    {
        CorpusDump.Load("VFPTYPE").Records[0]["F_D"].Text.Should().Be("19000101");
    }

    [Fact]
    public void Load_ReadsTheNullFlagsBitmapAndTheMarksItStandsBehind()
    {
        DumpRecord record = CorpusDump.Load("VFPNULL").Records[0];

        record.NullFlags.Should().Equal(0xFF, 0x03);
        record.Values.Count(v => v.IsNull).Should().Be(10);
        record["N_C"].IsNull.Should().BeTrue();
        record["PLAIN"].IsNull.Should().BeFalse();
    }

    [Fact]
    public void Load_ReadsAFieldThatIsNullAsStillHoldingItsBytes()
    {
        // Nulling a field does not undo the assignment, which is why the value accessors must be
        // unaffected by it. See Decision 11.
        DumpValue value = CorpusDump.Load("VFPNULL").Records[0]["N_C"];

        value.IsNull.Should().BeTrue();
        Encoding.ASCII.GetString(value.Bytes).Should().Be("ALPHA     ");
    }

    [Fact]
    public void Load_ReportsNoNullFlagsForATableThatHasNoNullableField()
    {
        CorpusDump.Load("VFPTYPE").Records.Should().OnlyContain(r => r.NullFlags == null);
    }

    [Fact]
    public void Load_KeepsTheMemoLinesRatherThanSkippingThem()
    {
        // Step 003 asserts against these. Parsing them now means that step adds assertions instead
        // of parsing, and that a memo line cannot quietly stop being read in between.
        DumpValue value = CorpusDump.Load("VFPMEMO").Records[1]["NOTES"];

        value.IsMemo.Should().BeTrue();
        value.Bytes.Should().HaveCount(4);
        value.MemoLength.Should().Be(value.MemoBytes!.Length);
        value.MemoBytes.Should().NotBeEmpty();
    }

    [Fact]
    public void Load_ReadsTheTenByteAsciiMemoReferenceOfAFoxPro2Table()
    {
        DumpValue value = CorpusDump.Load("F2XMEMO").Records[1]["NOTES"];

        value.IsMemo.Should().BeTrue();
        value.Bytes.Should().HaveCount(10);
    }

    [Fact]
    public void Parse_ARecordLineItCannotRead_IsRefused()
    {
        string text = Corpus.ReadDump("VFPTYPE")
                            .Replace("  F_L        \"T\"", "  F_L        surprise", StringComparison.Ordinal);

        Action act = () => CorpusDump.Parse(text, "a modified VFPTYPE");

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_ATokenItHasNeverHeardOf_IsRefused()
    {
        string text = Corpus.ReadDump("VFPTYPE")
                            .Replace("  F_L        \"T\"", "  F_L        \"T\" mood=7", StringComparison.Ordinal);

        Action act = () => CorpusDump.Parse(text, "a modified VFPTYPE");

        act.Should().Throw<InvalidDataException>().WithMessage("*mood*");
    }

    [Fact]
    public void Parse_ARecordShorterThanTheFieldList_IsRefused()
    {
        // A dropped field line would shrink what the gate compares without changing its result,
        // which is the failure a data-driven suite cannot see by itself.
        string text = Corpus.ReadDump("VFPTYPE")
                            .Replace("  F_L        \"T\"\n", string.Empty, StringComparison.Ordinal);

        Action act = () => CorpusDump.Parse(text, "a modified VFPTYPE");

        act.Should().Throw<InvalidDataException>().WithMessage("*9 fields*");
    }

    [Fact]
    public void Parse_AFieldLineBeforeAnyRecordBegins_IsRefused()
    {
        string text = Corpus.ReadDump("VFPTYPE")
                            .Replace("[records]\nrec 1 ", "[records]\n  STRAY      \"x\"\nrec 1 ", StringComparison.Ordinal);

        Action act = () => CorpusDump.Parse(text, "a modified VFPTYPE");

        act.Should().Throw<InvalidDataException>().WithMessage("*before any record*");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Load_ReadsTheRecordsOfEveryTableInTheCorpus(string tableName)
    {
        CorpusDump dump = CorpusDump.Load(tableName);

        dump.Records.Should().HaveCount(dump.RecordCount);
        dump.Records.Select(r => r.Number).Should().Equal(Enumerable.Range(1, dump.RecordCount));
        dump.Records.Should().OnlyContain(r => r.Values.Count == dump.Fields.Count);
    }

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
