using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Memo;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Memo payloads read as text, and the two reference encodings, against the real corpus files.
///
/// The record gate already compares every payload byte. What this adds is the part the dump cannot
/// state: what those bytes mean as text, which comes from the generator's own documented input as
/// ADR-21 allows. It also states the geometry facts as assertions rather than leaving them implicit
/// in a payload comparison that would pass for several different wrong reasons.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class MemoGoldenTests
{
    [Fact]
    public void TheGateCoversEveryTableThatHasAMemoFile()
    {
        MemoTables().Should().HaveCount(5);
    }

    [Fact]
    public void ASingleByteCodePage_DecodesAMemoToTheTextTheGeneratorWrote()
    {
        using Reader reader = new("CP1251");
        reader.Go(2);

        reader.MemoText("MEMO").Should().Be("т");
    }

    [Fact]
    public void AMultiByteCodePage_DecodesAMemoToTheTextTheGeneratorWrote()
    {
        using Reader reader = new("CP936");
        reader.Go(2);

        reader.MemoText("MEMO").Should().Be("字");
    }

    [Theory]
    [InlineData(63)]
    [InlineData(401)]
    public void AMemoWhoseLengthCutsACharacterInHalf_KeepsWhatSurvivedAndMarksTheRest(int length)
    {
        // The field-boundary case has a twin on the memo path: these two payload lengths each end on
        // a dangling GBK lead byte. ADR-21 governs both, and neither throws.
        using Reader reader = new("CP936");

        int found = 0;
        for (int record = 1; record <= 32; record++)
        {
            reader.Go(record);
            if (reader.MemoLength("MEMO") != length)
                continue;

            reader.MemoText("MEMO").Should().EndWith("�");
            found++;
        }

        found.Should().BeGreaterThan(0, "the corpus holds a memo of {0} bytes", length);
    }

    [Fact]
    public void ATenByteAsciiReference_ResolvesToTheSameEntryAFourByteOneWould()
    {
        // The FoxPro 2.x table is the only one with the text encoding, so it is the only witness
        // that the width-based choice is right end to end.
        CorpusDump dump = CorpusDump.Load("F2XMEMO");
        using Reader reader = new("F2XMEMO");

        reader.Table.Fields["NOTES"].Length.Should().Be(10);

        int nonEmpty = 0;
        foreach (DumpRecord record in dump.Records)
        {
            reader.Go(record.Number);
            DumpValue value = record["NOTES"];

            reader.Table.GetMemoBytes(reader.Table.Fields["NOTES"]).Should().Equal(value.MemoBytes!);
            if (value.MemoLength > 0)
                nonEmpty++;
        }

        nonEmpty.Should().Be(28, "F2XMEMO holds 28 non-empty memos");
    }

    [Fact]
    public void AMemoThatStraddlesABlockBoundary_ReadsWhole()
    {
        // 505 bytes plus the 8-byte header is 513, so the entry crosses a 512-byte boundary. This is
        // the only straddling length the corpus has.
        using Reader reader = new("VFPMEMO");

        int found = 0;
        for (int record = 1; record <= 32; record++)
        {
            reader.Go(record);
            if (reader.MemoLength("NOTES") == 505)
                found++;
        }

        found.Should().BeGreaterThan(0, "VFPMEMO holds a 505-byte memo");
    }

    [Fact]
    public void ABinaryMemo_RefusesToBeReadAsTextButGivesItsBytes()
    {
        using Reader reader = new("VFPMEMO");
        reader.Go(2);

        FieldDefinition binary = reader.Table.Fields["BINMEMO"];

        reader.Table.GetMemoBytes(binary).Should().NotBeEmpty();
        reader.Table.Invoking(t => t.GetMemoString(binary))
              .Should().Throw<CodeBaseException>().WithMessage("*marked binary*");
    }

    [Fact]
    public void ABinaryCharacterField_RefusesToBeReadAsTextButGivesItsBytes()
    {
        // Z is stored in the record, not in the memo file. Its only memo-ish quality is that it is
        // binary, so it refuses the same way. See Decision 12.
        using Reader reader = new("VFPMEMO");
        reader.Go(1);

        FieldDefinition binary = reader.Table.Fields["BINCHAR"];

        reader.Table.GetRawBytes(binary).Should().HaveCount(8);
        reader.Table.Invoking(t => t.GetString(binary))
              .Should().Throw<CodeBaseException>().WithMessage("*marked binary*");
    }

    [Theory]
    [MemberData(nameof(MemoTables))]
    public void EveryMemoOfEveryRecord_ReportsTheBlockAndLengthTheDumpRecords(string tableName)
    {
        CorpusDump dump = CorpusDump.Load(tableName);
        using Reader reader = new(tableName);

        int asserted = 0;
        int nonEmpty = 0;

        foreach (DumpRecord record in dump.Records)
        {
            reader.Go(record.Number);

            foreach (DumpValue value in record.Values.Where(v => v.IsMemo))
            {
                FieldDefinition field = reader.Table.Fields[value.Name];

                reader.Table.GetMemoBlock(field).Should().Be(MemoReference.Read(value.Bytes));
                reader.Table.GetMemoLength(field).Should().Be((int)value.MemoLength!.Value);
                reader.Table.GetMemoType(field).Should().Be(MemoType.Text);
                asserted++;

                if (value.MemoLength > 0)
                    nonEmpty++;
            }
        }

        asserted.Should().Be(
            dump.Records[0].Values.Count(v => v.IsMemo) * dump.RecordCount,
            "every memo field of every record is asserted");
        nonEmpty.Should().BeGreaterThan(0, "a table with no non-empty memo would prove nothing");
    }

    [Fact]
    public void AMemoFieldAtEndOfFile_ReadsAsAbsent()
    {
        // The blank record blanks the reference, so it resolves to no memo and the payload is empty.
        using Reader reader = new("VFPMEMO");
        reader.Table.Bottom();
        reader.Table.Skip(1).Should().Be(SkipResult.Eof);

        FieldDefinition notes = reader.Table.Fields["NOTES"];

        reader.Table.GetMemoBlock(notes).Should().Be(0);
        reader.Table.GetMemoLength(notes).Should().Be(0);
        reader.Table.GetMemoBytes(notes).Should().BeEmpty();
    }

    [Fact]
    public void AMemoAssignedThenMarkedNull_ComesBackNotNullWithItsContents()
    {
        // Assigning a memo clears the null bit, so a record that was assigned and then nulled reads
        // as not null. VFPNULL record 7 was generated to do exactly this. See FPT-MEMO.md 3.4.
        CorpusDump dump = CorpusDump.Load("VFPNULL");
        DumpValue expected = dump.Records.Single(r => r.Number == 7)["N_M"];

        using Reader reader = new("VFPNULL");
        reader.Go(7);

        FieldDefinition memo = reader.Table.Fields["N_M"];

        reader.Table.IsNull(memo).Should().Be(expected.IsNull);
        reader.Table.GetMemoBytes(memo).Should().Equal(expected.MemoBytes!);
    }

    public static TheoryData<string> MemoTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames.Where(t => File.Exists(Path.Combine(Corpus.Root, t + ".fpt"))))
            data.Add(table);
        return data;
    }

    /// <summary>
    /// A corpus table open for reading, closed with the test.
    /// </summary>
    private sealed class Reader : IDisposable
    {
        private readonly CodeBaseEngine engine = new();

        public Reader(string tableName) => Table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        public Table Table { get; }

        public void Go(int recordNumber) => Table.Go(recordNumber).Should().Be(GoResult.Ok);

        public string MemoText(string fieldName) => Table.GetMemoString(Table.Fields[fieldName]);

        public int MemoLength(string fieldName) => Table.GetMemoLength(Table.Fields[fieldName]);

        public void Dispose() => engine.Dispose();
    }
}
