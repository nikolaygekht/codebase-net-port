using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Resolves every corpus table's fields from its real bytes, against what the C library reports.
///
/// This section of the dump is where the stored view and the engine's view visibly differ, so it is
/// the one that catches a resolution rule read wrongly: the upper-cased names, the binary variants
/// reappearing under their own letters, the per-type length and decimal rules, and the null-flags
/// field being absent from the list.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class FieldGoldenTests
{
    [Theory]
    [MemberData(nameof(AllTables))]
    public void Fields_ResolveToWhatTheCLibraryReports(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        ResolvedFields actual = Resolve(expected);

        actual.Fields.Should().HaveCount(expected.Fields.Count);

        foreach ((FieldDefinition field, DumpField want) in actual.Fields.Zip(expected.Fields))
        {
            field.Name.Should().Be(want.Name);
            field.Type.Should().Be(want.Type);
            field.Length.Should().Be(want.Length);
            field.Decimals.Should().Be(want.Decimals);
            field.IsNullable.Should().Be(want.IsNullable);
        }
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Fields_LieWhollyInsideTheRecord(string tableName)
    {
        // The guarantee every later decode rests on, checked on real files rather than assumed.
        CorpusDump expected = CorpusDump.Load(tableName);

        ResolvedFields actual = Resolve(expected);

        foreach (FieldDefinition field in actual.Fields)
        {
            field.RecordOffset.Should().BeGreaterThanOrEqualTo(1);
            (field.RecordOffset + field.Length).Should().BeLessThanOrEqualTo(expected.RecordLength);
        }
    }

    [Fact]
    public void Fields_ExcludeTheNullFlagsFieldThatTheDescriptorsInclude()
    {
        CorpusDump expected = CorpusDump.Load("VFPNULL");

        ResolvedFields actual = Resolve(expected);

        actual.Fields.Should().HaveCount(13);
        expected.Descriptors.Should().HaveCount(14);
        actual.NullFlags.Should().NotBeNull();
        actual.NullFlags!.Name.Should().Be("_NULLFLAGS", "the engine upper-cases every field name");
        actual.NullFlags.Length.Should().Be(2, "ten nullable fields need two bytes of bitmap");
    }

    [Fact]
    public void Fields_NumberNullBitsOverTheNullableFieldsRatherThanAllOfThem()
    {
        // The rule that an implementation using the field's position would get wrong: N_I is the
        // eighth field but the sixth nullable one, because two ordinary fields precede it.
        ResolvedFields actual = Resolve(CorpusDump.Load("VFPNULL"));

        actual.Fields.Single(f => f.Name == "N_C").NullBit.Should().Be(0);
        actual.Fields.Single(f => f.Name == "N_I").NullBit.Should().Be(5);
        actual.Fields.Single(f => f.Name == "N_M").NullBit.Should().Be(9);
        actual.Fields.Single(f => f.Name == "PLAIN").NullBit.Should().BeNull();
        actual.Fields.Single(f => f.Name == "TAIL").NullBit.Should().BeNull();
    }

    [Fact]
    public void Fields_ReportTheCreatedTypeWhileKeepingTheStoredOne()
    {
        ResolvedFields actual = Resolve(CorpusDump.Load("VFPMEMO"));

        FieldDefinition binaryMemo = actual.Fields.Single(f => f.Name == "BINMEMO");
        binaryMemo.Type.Should().Be('X');
        binaryMemo.StoredType.Should().Be('M');

        FieldDefinition binaryChar = actual.Fields.Single(f => f.Name == "BINCHAR");
        binaryChar.Type.Should().Be('Z');
        binaryChar.StoredType.Should().Be('C');

        // A plain memo is stored and reported alike, even though its data is binary too.
        actual.Fields.Single(f => f.Name == "NOTES").Type.Should().Be('M');

        // ...and so is a general field, which is never restored to another letter.
        actual.Fields.Single(f => f.Name == "GEN").Type.Should().Be('G');
    }

    [Fact]
    public void Fields_OfALegacyTableAreNeverNullableOrBinary()
    {
        // Version 0xF5 does not have its descriptor flags read at all, so a memo field there is
        // reported as plain, not binary.
        ResolvedFields actual = Resolve(CorpusDump.Load("F2XMEMO"));

        actual.Fields.Should().OnlyContain(f => !f.IsNullable && !f.IsBinary);
        actual.NullFlags.Should().BeNull();
    }

    /// <summary>
    /// Reads a corpus table's real bytes and resolves its fields.
    /// </summary>
    private static ResolvedFields Resolve(CorpusDump dump)
    {
        byte[] file = Corpus.ReadAllBytes(dump.FileName);
        DbfHeader header = DbfHeader.Parse(file);

        IReadOnlyList<FieldDescriptor> descriptors = FieldDescriptorTable.Parse(
            file.AsSpan(DbfHeader.Size, header.HeaderLength - DbfHeader.Size),
            header.Flags.UsesLongFieldNames);

        return FieldResolver.Resolve(
            descriptors,
            IDbfFormatVariant.Resolve(header.Version),
            header.RecordLength);
    }

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
