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

    [Fact]
    public void Descriptors_InTheLongNameLayoutKeepTheCaseTheyWereCreatedWith()
    {
        // The long layout stores the caller's string as given (D4CREATE.C:1645), not an upper-cased
        // copy, and open upper-cases it (D4OPEN.C:316). So the stored names and the field names the
        // reference reports differ in case -- the descriptor test above holds the stored ones, the
        // field tests the reported ones.
        CorpusDump dump = CorpusDump.Load("LONGNAME");

        dump.Version.Should().Be(DbfVersion.VisualFoxProExtended);
        dump.Descriptors.Select(d => d.Name).Should().Equal(
            "ID", "Customer_Name", "ORDER_TOTAL", "SHIP_STATE", "Delivery_Date_Requested",
            "Delivery_Instructions_For_The_Courier", "_NullFlags");
        dump.Fields[1].Name.Should().Be("CUSTOMER_NAME");
    }

    [Fact]
    public void Header_OfANonExtendedTableReportsNoFlagFromItsFlagBytes()
    {
        // FLAGS30 is a 0x30 table whose generator set all eight flag bytes to 1 after writing it, and
        // the reference opened and dumped it as an ordinary table. Its bytes are checked here first,
        // because a case that lost them would pass this test proving nothing.
        byte[] file = Corpus.ReadAllBytes("FLAGS30.DBF");
        file[0].Should().Be(DbfVersion.VisualFoxPro);
        file.AsSpan(12, 8).ToArray().Should().OnlyContain(b => b == 1);

        FeatureFlags flags = DbfHeader.Parse(file).Flags;

        flags.HasAutoIncrementField.Should().BeFalse();
        flags.MayHaveCompressedMemos.Should().BeFalse();
        flags.HasCompressedData.Should().BeFalse();
        flags.HasAutoTimestampField.Should().BeFalse();
        flags.UsesLongFieldNames.Should().BeFalse();
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
