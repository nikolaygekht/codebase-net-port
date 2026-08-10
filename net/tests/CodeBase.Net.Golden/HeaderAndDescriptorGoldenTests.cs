using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Reads every corpus table's header and stored descriptors from the real bytes, against the dump.
///
/// This is the layer that can disagree with the original. A unit test feeds a decoder bytes written
/// at the offsets the test itself chose, so it proves consistency and nothing more; a whole set of
/// them can pass on a misread specification. Here the bytes are the ones the C library wrote and the
/// expectations are what the C library says is in them.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class HeaderAndDescriptorGoldenTests
{
    [Theory]
    [MemberData(nameof(AllTables))]
    public void Header_DecodesToTheValuesTheCLibraryReports(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);

        DbfHeader header = DbfHeader.Parse(Corpus.ReadAllBytes(expected.FileName));

        header.Version.Should().Be(expected.Version);
        header.LastUpdateYear.Should().Be((byte)expected.LastUpdate.Year);
        header.LastUpdateMonth.Should().Be((byte)expected.LastUpdate.Month);
        header.LastUpdateDay.Should().Be((byte)expected.LastUpdate.Day);
        header.RecordCount.Should().Be(expected.RecordCount);
        header.HeaderLength.Should().Be(expected.HeaderLength);
        header.RecordLength.Should().Be(expected.RecordLength);
        header.TableFlags.Should().Be(expected.TableFlags);
        header.CodePage.Should().Be(expected.CodePage);
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Descriptors_DecodeToTheBytesTheCLibraryWrote(string tableName)
    {
        CorpusDump expected = CorpusDump.Load(tableName);
        byte[] file = Corpus.ReadAllBytes(expected.FileName);
        DbfHeader header = DbfHeader.Parse(file);

        IReadOnlyList<FieldDescriptor> descriptors = FieldDescriptorTable.Parse(
            file.AsSpan(DbfHeader.Size, header.HeaderLength - DbfHeader.Size),
            header.Flags.UsesLongFieldNames);

        descriptors.Should().HaveCount(expected.Descriptors.Count);

        foreach ((FieldDescriptor actual, DumpDescriptor want) in descriptors.Zip(expected.Descriptors))
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

    [Fact]
    public void Descriptors_KeepTheStoredCaseOfTheNullFlagsField()
    {
        // Written mixed-case while every other name is upper-cased, and matched byte-exact when the
        // engine looks for it. The corpus is the only thing that proves this is real.
        CorpusDump.Load("VFPNULL").Descriptors[^1].Name.Should().Be("_NullFlags");
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void Descriptors_AccountForEveryByteOfTheRecord(string tableName)
    {
        // The invariant containment rests on: the fields, plus the leading deletion flag, are
        // exactly the record. Nothing later has to trust a stored offset because of this.
        CorpusDump expected = CorpusDump.Load(tableName);

        int width = 1 + expected.Descriptors.Sum(DescriptorWidth);

        width.Should().Be(expected.RecordLength);
    }

    /// <summary>
    /// The bytes a descriptor occupies, allowing for the character type keeping a 16-bit length.
    /// </summary>
    private static int DescriptorWidth(DumpDescriptor descriptor) =>
        descriptor.Type is 'C' ? descriptor.Length + (descriptor.Decimals << 8) : descriptor.Length;

    public static TheoryData<string> AllTables()
    {
        TheoryData<string> data = [];
        foreach (string table in Corpus.TableNames)
            data.Add(table);
        return data;
    }
}
