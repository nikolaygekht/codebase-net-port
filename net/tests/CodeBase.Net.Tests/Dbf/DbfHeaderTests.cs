using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What a header decode promises a caller: these bytes yield these values, and these contradictions are refused.
///
/// The bytes here are hand-built [i]input[/i], which invents nothing. The offsets they are written
/// at, however, encode this port's reading of the format, so every test below could pass on a
/// misreading. Only the golden layer, comparing against dumps the C library produced, can say the
/// offsets are right. See DEV_APPROACH.md section 4.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class DbfHeaderTests
{
    [Fact]
    public void Parse_ReportsTheValuesTheBytesGive()
    {
        byte[] bytes = HeaderBytes.Build(
            version: 0x30,
            year: 26,
            month: 11,
            day: 31,
            recordCount: 16_909_060,
            headerLength: 584,
            recordLength: 82,
            tableFlags: 0x02,
            codePage: 0x03,
            autoIncrementValue: 42.5);

        DbfHeader header = DbfHeader.Parse(bytes);

        header.Version.Should().Be(0x30);
        header.LastUpdateYear.Should().Be(26);
        header.LastUpdateMonth.Should().Be(11);
        header.LastUpdateDay.Should().Be(31);
        header.RecordCount.Should().Be(16_909_060);
        header.HeaderLength.Should().Be(584);
        header.RecordLength.Should().Be(82);
        header.TableFlags.Should().Be(0x02);
        header.CodePage.Should().Be(0x03);
        header.AutoIncrementValue.Should().Be(42.5);
    }

    [Fact]
    public void Parse_ReadsOnlyTheFirstThirtyTwoBytes()
    {
        byte[] bytes = [.. HeaderBytes.Build(recordLength: 82), .. new byte[500]];

        DbfHeader header = DbfHeader.Parse(bytes);

        header.RecordLength.Should().Be(82);
    }

    [Fact]
    public void Parse_ShorterThanAHeader_IsRejectedAsCorruptData()
    {
        byte[] bytes = HeaderBytes.Build()[..31];

        Rejection(bytes).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_EmptyFile_IsRejectedAsCorruptData()
    {
        Rejection([]).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_RecordLengthOfZero_IsRejectedAsCorruptData()
    {
        // Everything downstream divides or multiplies by this; the C library guards it too
        // (D4OPEN.C:2230-2231).
        Rejection(HeaderBytes.Build(recordLength: 0)).Code.Should().Be(ErrorCode.Data);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Parse_HeaderLengthWithNoRoomForDescriptors_IsRejectedAsCorruptData(int headerLength)
    {
        Rejection(HeaderBytes.Build(headerLength: headerLength)).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_NegativeRecordCount_IsRejectedAsCorruptData()
    {
        // The count is stored signed, so a corrupt file can present one.
        Rejection(HeaderBytes.Build(recordCount: -1)).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_ReportsTheFeatureFlagsOfAnExtendedTable()
    {
        byte[] bytes = HeaderBytes.Build(version: 0x31, flags: [1, 1, 1, 1, 1, 0, 0, 0]);

        DbfHeader header = DbfHeader.Parse(bytes);

        header.Flags.HasAutoIncrementField.Should().BeTrue();
        header.Flags.MayHaveCompressedMemos.Should().BeTrue();
        header.Flags.HasCompressedData.Should().BeTrue();
        header.Flags.HasAutoTimestampField.Should().BeTrue();
        header.Flags.UsesLongFieldNames.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Parse_ExtendedTableWhoseFlagIsNeitherZeroNorOne_IsRejectedAsCorruptData(int index)
    {
        byte[] flags = new byte[8];
        flags[index] = 2;

        Rejection(HeaderBytes.Build(version: 0x31, flags: flags)).Code.Should().Be(ErrorCode.Data);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Parse_ExtendedTableDeclaringAnUnknownFeature_IsRejectedAsCorruptData(int index)
    {
        byte[] flags = new byte[8];
        flags[index] = 1;

        Rejection(HeaderBytes.Build(version: 0x31, flags: flags)).Code.Should().Be(ErrorCode.Data);
    }

    [Theory]
    [InlineData(0x30)]
    [InlineData(0x03)]
    [InlineData(0xF5)]
    [InlineData(0x32)]
    public void Parse_TableThatIsNotExtended_IgnoresTheFlagBytesEntirely(byte version)
    {
        // Only version 0x31 declares that the flags mean anything. The C library reads these bytes
        // for no other version, so refusing a file over them would refuse files it opens.
        byte[] bytes = HeaderBytes.Build(version: version, flags: [9, 9, 9, 9, 9, 9, 9, 9]);

        DbfHeader header = DbfHeader.Parse(bytes);

        header.Version.Should().Be(version);
        header.Flags.UsesLongFieldNames.Should().BeFalse();
    }

    [Fact]
    public void Parse_LongFieldNameFlag_IsReportedRatherThanRefused()
    {
        // Whether that descriptor layout can be read is not a question the header answers.
        byte[] bytes = HeaderBytes.Build(version: 0x31, flags: [0, 0, 0, 0, 1, 0, 0, 0]);

        DbfHeader.Parse(bytes).Flags.UsesLongFieldNames.Should().BeTrue();
    }

    private static CodeBaseException Rejection(byte[] bytes)
    {
        Action act = () => DbfHeader.Parse(bytes);
        return act.Should().Throw<CodeBaseException>().Which;
    }
}
