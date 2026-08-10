using AwesomeAssertions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Guards the reader that every other golden test takes its expectations from.
///
/// A dump reader that quietly skipped a section, or read a section as empty, would let the suite
/// compare nothing and report success. So it is checked against exact counts and known values from
/// the real files, not merely for parsing without complaint.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CorpusDumpTests
{
    [Fact]
    public void Load_ReadsTheHeaderValues()
    {
        CorpusDump dump = CorpusDump.Load("VFPNULL");

        dump.FileName.Should().Be("VFPNULL.DBF");
        dump.Version.Should().Be(0x30);
        dump.LastUpdate.Should().Be((26, 1, 1));
        dump.RecordCount.Should().Be(32);
        dump.HeaderLength.Should().Be(744);
        dump.RecordLength.Should().Be(87);
        dump.TableFlags.Should().Be(0x02);
        dump.CodePage.Should().Be(0x00);
    }

    [Fact]
    public void Load_ReadsMoreDescriptorsThanFieldsWhenATableHasNullFlags()
    {
        // The stored descriptor for the null-flags bitmap has no counterpart in the field list,
        // which is the asymmetry the port has to reproduce.
        CorpusDump dump = CorpusDump.Load("VFPNULL");

        dump.Descriptors.Should().HaveCount(14);
        dump.Fields.Should().HaveCount(13);
        dump.Descriptors[^1].Name.Should().Be("_NullFlags");
        dump.Descriptors[^1].Type.Should().Be('0');
    }

    [Fact]
    public void Load_ReadsDescriptorsAndFieldsInEqualNumberWhenATableHasNone()
    {
        CorpusDump dump = CorpusDump.Load("VFPTYPE");

        dump.Descriptors.Should().HaveCount(9);
        dump.Fields.Should().HaveCount(9);
    }

    [Fact]
    public void Load_ReadsEveryValueOfADescriptorLine()
    {
        DumpDescriptor descriptor = CorpusDump.Load("VFPNULL").Descriptors[8];

        descriptor.Should().Be(new DumpDescriptor(9, "N_B", 'B', 56, 8, 6, 0x06, 0));
    }

    [Fact]
    public void Load_ReadsNullabilityFromATokenThatAppearsOnlyWhenTrue()
    {
        IReadOnlyList<DumpField> fields = CorpusDump.Load("VFPNULL").Fields;

        fields.Single(f => f.Name == "N_C").IsNullable.Should().BeTrue();
        fields.Single(f => f.Name == "PLAIN").IsNullable.Should().BeFalse();
    }

    [Fact]
    public void Load_ReportsNoFieldAsNullableInATableThatHasNoNullFlags()
    {
        CorpusDump.Load("VFPTYPE").Fields.Should().OnlyContain(f => !f.IsNullable);
    }

    [Fact]
    public void Load_ReadsTheCreationTypeOfAFieldStoredUnderAnotherLetter()
    {
        // Stored as a memo and a character field respectively, reported under the letters they were
        // created with.
        IReadOnlyList<DumpField> fields = CorpusDump.Load("VFPMEMO").Fields;

        fields.Single(f => f.Name == "BINMEMO").Type.Should().Be('X');
        fields.Single(f => f.Name == "BINCHAR").Type.Should().Be('Z');
    }

    [Fact]
    public void Parse_ReadsPastTheOneSectionItDeliberatelyDoesNotRead()
    {
        // Record values belong to the step that decodes them, so the section is declared unread
        // rather than unknown. Every dump already carries one.
        Corpus.ReadDump("VFPTYPE").Should().Contain("[records]");

        CorpusDump.Load("VFPTYPE").Fields.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_ASectionItHasNeverHeardOf_IsRefused()
    {
        // The alternative is skipping it, which leaves a golden test comparing an empty list and
        // reporting success. The dump format will grow an index half, and it must not arrive
        // unnoticed.
        string text = Corpus.ReadDump("VFPTYPE") + "\n[tags]\nsomething 1\n";

        Action act = () => CorpusDump.Parse(text, "a modified VFPTYPE");

        act.Should().Throw<InvalidDataException>().WithMessage("*[tags]*");
    }

    [Theory]
    [InlineData("[descriptors]")]
    [InlineData("[fields]")]
    public void Parse_ADumpMissingASectionItNeeds_IsRefused(string section)
    {
        string text = Corpus.ReadDump("VFPTYPE").Replace(section, "[records]", StringComparison.Ordinal);

        Action act = () => CorpusDump.Parse(text, "a truncated VFPTYPE");

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_ADumpMissingAHeaderValue_IsRefused()
    {
        string text = Corpus.ReadDump("VFPTYPE").Replace("recordLen", "recordLength", StringComparison.Ordinal);

        Action act = () => CorpusDump.Parse(text, "a modified VFPTYPE");

        act.Should().Throw<InvalidDataException>().WithMessage("*recordLen*");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Load_ReadsEveryTableInTheCorpus(string tableName)
    {
        CorpusDump dump = CorpusDump.Load(tableName);

        dump.FileName.Should().Be(tableName + ".DBF");
        dump.RecordCount.Should().Be(32);
        dump.Fields.Should().NotBeEmpty();
        dump.Descriptors.Should().HaveCountGreaterThanOrEqualTo(dump.Fields.Count);
    }

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
