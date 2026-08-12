using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The gate: every corpus table opened through the public API, against what the C library reports.
///
/// The other golden tests call the decoders directly. This one goes the way a caller does, so it is
/// the only thing that covers opening a real file, finding the memo file beside it, and the surface
/// a caller actually holds. A decoder that is right while the API around it is wrong would pass
/// everything else in the suite and fail here.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class TableMetadataGoldenTests
{
    [Fact]
    public void TheGateCoversEveryTableInTheCorpus()
    {
        // Part of the gate rather than commentary. A data-driven suite that silently discovers
        // nothing reports success having asserted nothing, which is the most likely way this step
        // could pass while proving nothing.
        AllTables().Should().HaveCount(11);
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Table_ReportsTheHeaderTheCLibraryReports(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(expected.FileName));

        table.Version.Should().Be(expected.Version);
        table.LastUpdate.Should().Be(expected.LastUpdate);
        table.RecordCount.Should().Be(expected.RecordCount);
        table.RecordLength.Should().Be(expected.RecordLength);
        table.HeaderLength.Should().Be(expected.HeaderLength);
        table.CodePageByte.Should().Be(expected.CodePage);
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Table_ReportsTheDescriptorsTheCLibraryWrote(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(expected.FileName));

        table.Descriptors.Should().HaveCount(expected.Descriptors.Count);

        foreach ((FieldDescriptor actual, DumpDescriptor want) in table.Descriptors.Zip(expected.Descriptors))
        {
            actual.Name.Should().Be(want.Name);
            actual.Type.Should().Be(want.Type);
            actual.StoredOffset.Should().Be(want.StoredOffset);
            actual.Length.Should().Be((byte)want.Length);
            actual.Decimals.Should().Be((byte)want.Decimals);
            actual.Flags.Should().Be((FieldFlags)want.Flags);
            actual.HasTag.Should().Be((byte)want.HasTag);
        }
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Table_ReportsTheFieldsTheCLibraryReports(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(expected.FileName));

        table.Fields.Should().HaveCount(expected.Fields.Count);

        foreach ((FieldDefinition actual, DumpField want) in table.Fields.Zip(expected.Fields))
        {
            actual.Name.Should().Be(want.Name);
            actual.Type.Should().Be(want.Type);
            actual.Length.Should().Be(want.Length);
            actual.Decimals.Should().Be(want.Decimals);
            actual.IsNullable.Should().Be(want.IsNullable);
        }
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Table_FieldsAccountForTheRecordExactly(string tableName)
    {
        // The containment guarantee, checked at the surface a caller sees.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        int width = 1 + table.Fields.Sum(f => f.Length) + (table.NullFlags?.Length ?? 0);

        width.Should().Be(table.RecordLength);
        table.Fields.Should().OnlyContain(f => f.RecordOffset >= 1);
        table.Fields.Should().OnlyContain(f => f.RecordOffset + f.Length <= table.RecordLength);
    }

    [Theory]
    [InlineData("F2XMEMO")]   // version 0xF5 declares the memo, the companion byte does not
    [InlineData("VFPMEMO")]
    [InlineData("VFPNULL")]
    public void Table_WithAMemo_OpensTheLowerCaseCompanionBesideIt(string tableName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.HasMemo.Should().BeTrue();
        table.MemoBlockSize.Should().Be(512);
    }

    [Theory]
    [InlineData("DB3TYPE")]
    [InlineData("VFPTYPE")]
    public void Table_WithoutAMemo_ReportsNone(string tableName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.HasMemo.Should().BeFalse();
        table.MemoBlockSize.Should().BeNull();
    }

    [Fact]
    public void Table_ExposesTheNullFlagsFieldApartFromItsFieldList()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("VFPNULL.DBF"));

        table.Fields.Should().HaveCount(13);
        table.Descriptors.Should().HaveCount(14);
        table.NullFlags!.Name.Should().Be("_NULLFLAGS");
        table.Fields.Should().NotContain(f => f.IsSystem);
    }

    [Fact]
    public void Table_WithoutNullableFields_HasNoNullFlagsField()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("VFPTYPE.DBF"));

        table.NullFlags.Should().BeNull();
    }

    [Fact]
    public void Table_FindsFieldsByNameWithoutRegardToCase()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("VFPMEMO.DBF"));

        table.Fields["binmemo"].Type.Should().Be('X');
        table.Fields.TryGet("NoSuchField", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("DB3TYPE", CodePage.Unmarked)]
    [InlineData("VFPTYPE", CodePage.Unmarked)]
    [InlineData("F2XMEMO", CodePage.Unmarked)]
    [InlineData("VFPMEMO", CodePage.Unmarked)]
    [InlineData("VFPNULL", CodePage.Unmarked)]

    // The two marked tables, and the reason the marks are resolved from Visual FoxPro's documented
    // set rather than the six values CodeBase defines (DBF-FORMAT.md §8.1, ADR-19): CodeBase writes
    // byte 29 verbatim (D4CREATE.C:1391) but interprets neither of these, so a port that followed its
    // constants would read both tables as cp437 and produce mojibake from correctly marked files.
    [InlineData("CP1251", CodePage.Cp1251)]
    [InlineData("CP936", CodePage.Cp936)]
    public void Table_ReportsItsCodePageWithoutNeedingAnEncodingProvider(string tableName, CodePage expected)
    {
        // Reading a table's shape must not require the host to have registered anything, and no
        // test in this suite registers a provider. See ADR-17: the encoding is resolved only when
        // text is asked for, and nothing here asks — not even for a table naming a code page .NET
        // has no built-in encoding for.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.CodePage.Should().Be(expected);
    }

    [Theory]
    [InlineData("VFPTYPE", null)]
    [InlineData("CP1251", 1251)]
    [InlineData("CP936", 936)]
    public void Table_ReportsTheCodePageNumberItsMarkNames(string tableName, int? expected)
    {
        // The number is what a caller passes on to an encoding, and it is not the stored mark: 1251
        // is stamped as 0xC9. Null where the header names no code page, which the mark tells apart
        // from a code page this library does not recognize.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        table.CodePageNumber.Should().Be(expected);
    }

    [Fact]
    public void Engine_ClosesTheTablesItStillHoldsOpen()
    {
        CodeBaseEngine engine = new();
        engine.OpenTable(Corpus.PathOf("VFPTYPE.DBF"));
        engine.OpenTable(Corpus.PathOf("VFPMEMO.DBF"));

        engine.OpenTables.Should().HaveCount(2);

        engine.Dispose();

        engine.OpenTables.Should().BeEmpty();
    }

    [Fact]
    public void Engine_ForgetsATableThatClosesItself()
    {
        using CodeBaseEngine engine = new();
        Table table = engine.OpenTable(Corpus.PathOf("VFPTYPE.DBF"));

        table.Dispose();

        engine.OpenTables.Should().BeEmpty();

        // Closing twice is how a using statement behaves around a table already closed by hand.
        Action act = table.Dispose;
        act.Should().NotThrow();
    }

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
