using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What decoding one field descriptor promises: the stored view, uninterpreted and uncorrected.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FieldDescriptorTests
{
    [Fact]
    public void Parse_ReportsTheValuesTheBytesGive()
    {
        byte[] bytes = DescriptorBytes.Build(
            name: "PRICE",
            type: 'N',
            storedOffset: 37,
            length: 10,
            decimals: 2,
            flags: FieldFlags.Nullable,
            hasTag: 1);

        FieldDescriptor descriptor = FieldDescriptor.Parse(bytes);

        descriptor.Name.Should().Be("PRICE");
        descriptor.Type.Should().Be('N');
        descriptor.StoredOffset.Should().Be(37);
        descriptor.Length.Should().Be(10);
        descriptor.Decimals.Should().Be(2);
        descriptor.Flags.Should().Be(FieldFlags.Nullable);
        descriptor.HasTag.Should().Be(1);
    }

    [Fact]
    public void Parse_ReportsSeveralFlagsAtOnce()
    {
        // A nullable field of a binary type carries both, which is the common combination in a
        // table with nullable integers or datetimes.
        byte[] bytes = DescriptorBytes.Build(flags: FieldFlags.Nullable | FieldFlags.Binary);

        FieldDescriptor.Parse(bytes).Flags
            .Should().Be(FieldFlags.Nullable | FieldFlags.Binary);
    }

    [Fact]
    public void Parse_PreservesTheCaseOfTheStoredName()
    {
        // Ordinary names are upper-cased when the table is created, but the null-flags field is
        // written mixed-case and is later recognized by comparing these bytes exactly.
        FieldDescriptor.Parse(DescriptorBytes.Build(name: "_NullFlags")).Name
            .Should().Be("_NullFlags");
    }

    [Fact]
    public void Parse_NameEndsAtTheFirstZeroByte()
    {
        byte[] bytes = DescriptorBytes.Build(name: "CODE");
        bytes[8] = (byte)'X';   // rubbish past the terminating zero

        FieldDescriptor.Parse(bytes).Name.Should().Be("CODE");
    }

    [Fact]
    public void Parse_NameFillingEveryByte_IsReadWhole()
    {
        // Nothing guarantees a corrupt file leaves room for the zero byte.
        FieldDescriptor.Parse(DescriptorBytes.Build(name: "ABCDEFGHIJK")).Name
            .Should().Be("ABCDEFGHIJK");
    }

    [Fact]
    public void Parse_ReportsTheStoredTypeLetterWithoutUpperCasingIt()
    {
        // The engine upper-cases the type when it opens the table; this is the stored view.
        FieldDescriptor.Parse(DescriptorBytes.Build(type: 'c')).Type.Should().Be('c');
    }

    [Fact]
    public void Parse_KeepsTheLengthAndDecimalBytesSeparate()
    {
        // For a character field the decimal byte is the high byte of a 16-bit length. Combining
        // them belongs to the reader that knows the type, not to the stored view.
        FieldDescriptor descriptor = FieldDescriptor.Parse(
            DescriptorBytes.Build(type: 'C', length: 0x20, decimals: 0x01));

        descriptor.Length.Should().Be(0x20);
        descriptor.Decimals.Should().Be(0x01);
    }

    [Fact]
    public void Parse_ReportsAStoredOffsetEvenWhenItIsWrong()
    {
        // The engine recomputes offsets and ignores this one, so a nonsensical value is reported
        // rather than refused.
        FieldDescriptor.Parse(DescriptorBytes.Build(storedOffset: -99)).StoredOffset
            .Should().Be(-99);
    }

    [Fact]
    public void Parse_ReadsOnlyTheFirstThirtyTwoBytes()
    {
        byte[] bytes = [.. DescriptorBytes.Build(name: "FIRST"), .. DescriptorBytes.Build(name: "SECOND")];

        FieldDescriptor.Parse(bytes).Name.Should().Be("FIRST");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Parse_FewerThanThirtyTwoBytes_IsRejectedAsCorruptData(int available)
    {
        byte[] bytes = DescriptorBytes.Build()[..available];

        Action act = () => FieldDescriptor.Parse(bytes);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Data);
    }
}
