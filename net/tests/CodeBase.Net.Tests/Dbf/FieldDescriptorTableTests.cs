using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What reading the descriptor region promises: every descriptor up to the terminator, or a refusal.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class FieldDescriptorTableTests
{
    [Fact]
    public void Parse_ReadsEveryDescriptorUpToTheTerminator()
    {
        byte[] region = DescriptorBytes.Region(
            DescriptorBytes.Build(name: "CODE"),
            DescriptorBytes.Build(name: "NAME"),
            DescriptorBytes.Build(name: "PRICE"));

        IReadOnlyList<FieldDescriptor> descriptors = FieldDescriptorTable.Parse(region, usesLongFieldNames: false);

        descriptors.Select(d => d.Name).Should().Equal("CODE", "NAME", "PRICE");
    }

    [Fact]
    public void Parse_IgnoresTheReservedBytesAfterTheTerminator()
    {
        // A Visual FoxPro table counts 263 reserved bytes inside its header length. They sit past
        // the terminator and must never be mistaken for descriptors.
        byte[] region = DescriptorBytes.Region(263, DescriptorBytes.Build(name: "ONLY"));

        FieldDescriptorTable.Parse(region, usesLongFieldNames: false)
            .Should().ContainSingle().Which.Name.Should().Be("ONLY");
    }

    [Fact]
    public void Parse_RegionWithoutATerminator_IsRejectedAsCorruptData()
    {
        byte[] region = [.. DescriptorBytes.Build(), .. DescriptorBytes.Build()];

        Rejection(region).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_DescriptorCutShortByTheEndOfTheRegion_IsRejectedAsCorruptData()
    {
        byte[] region = DescriptorBytes.Build()[..20];

        Rejection(region).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_EmptyRegion_IsRejectedAsCorruptData()
    {
        Rejection([]).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_TerminatorBeforeAnyDescriptor_IsRejectedBecauseATableNeedsAField()
    {
        Rejection(DescriptorBytes.Region()).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_LongLayout_ReadsEveryNameWhole()
    {
        // Ten characters is the short layout's limit, eleven the first it cannot hold.
        string forty = new('L', 40);
        byte[] region = DescriptorBytes.LongRegion(
            DescriptorBytes.Long(name: "ID"),
            DescriptorBytes.Long(name: "TEN_CHARSX"),
            DescriptorBytes.Long(name: "ELEVENCHARS"),
            DescriptorBytes.Long(name: forty));

        IReadOnlyList<FieldDescriptor> descriptors = FieldDescriptorTable.Parse(region, usesLongFieldNames: true);

        descriptors.Select(d => d.Name).Should().Equal("ID", "TEN_CHARSX", "ELEVENCHARS", forty);
    }

    [Fact]
    public void Parse_LongLayout_DecodesTheRestOfADescriptorAsTheShortLayoutDoes()
    {
        // The same field in both layouts must come out as the same descriptor.
        byte[] shortRegion = DescriptorBytes.Region(DescriptorBytes.Build(
            "AMOUNT", 'N', storedOffset: 7, length: 12, decimals: 3, flags: FieldFlags.Nullable));
        byte[] longRegion = DescriptorBytes.LongRegion(DescriptorBytes.Long(
            "AMOUNT", 'N', storedOffset: 7, length: 12, decimals: 3, flags: FieldFlags.Nullable));

        FieldDescriptor fromLong = FieldDescriptorTable.Parse(longRegion, usesLongFieldNames: true).Single();

        fromLong.Should().Be(FieldDescriptorTable.Parse(shortRegion, usesLongFieldNames: false).Single());
    }

    [Fact]
    public void Parse_LongLayout_HoldsTheNullFlagsFieldLikeAnyOther()
    {
        // The C library writes the hidden field in the same layout as the rest (D4CREATE.C:1672-1680),
        // keeping the mixed-case name the null-flags check compares.
        byte[] region = DescriptorBytes.LongRegion(
            DescriptorBytes.Long(name: "SHIPPING_DATE", type: 'D', length: 8, flags: FieldFlags.Nullable),
            DescriptorBytes.Long(name: "_NullFlags", type: '0', length: 1, flags: FieldFlags.System | FieldFlags.Binary));

        FieldDescriptor last = FieldDescriptorTable.Parse(region, usesLongFieldNames: true)[^1];

        last.Name.Should().Be("_NullFlags");
        last.Type.Should().Be('0');
    }

    [Fact]
    public void Parse_LongLayout_EndsTwoBytesBeforeTheRegionsEndWithoutATerminator()
    {
        // The C loop stops at a 0x0D or at the region's last two bytes, whichever comes first
        // (D4OPEN.C:2716), so a run with no terminator there is still a complete one.
        byte[] region = [.. DescriptorBytes.Long(name: "ONLY_FIELD_X"), 0x00, 0x00];

        FieldDescriptorTable.Parse(region, usesLongFieldNames: true)
            .Should().ContainSingle().Which.Name.Should().Be("ONLY_FIELD_X");
    }

    [Fact]
    public void Parse_LongLayout_ReadsANameWithNoZeroByteToItsDeclaredLength()
    {
        // A declared length one short of the writer's leaves the zero byte outside the name.
        byte[] region = DescriptorBytes.LongRegion(DescriptorBytes.Long(name: "CUSTOMER_NAME", declaredLength: 13)[..^1]);

        FieldDescriptorTable.Parse(region, usesLongFieldNames: true)
            .Should().ContainSingle().Which.Name.Should().Be("CUSTOMER_NAME");
    }

    [Fact]
    public void Parse_LongLayout_ReadsAnEmptyName()
    {
        // A length of one is the closing zero byte alone. The reader takes it, as the C library
        // does; whether a field may be nameless is not this layer's question.
        byte[] region = DescriptorBytes.LongRegion(DescriptorBytes.Long(name: ""));

        FieldDescriptorTable.Parse(region, usesLongFieldNames: true)
            .Should().ContainSingle().Which.Name.Should().BeEmpty();
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)-1)]
    public void Parse_LongLayout_ANameLengthBelowOne_IsRejectedAsCorruptData(short declared)
    {
        // The terminator follows the length directly, so nothing but the length check can refuse
        // this: a length of zero read as valid would end the run there, cleanly.
        byte[] region = DescriptorBytes.LongRegion(DescriptorBytes.Long(name: "", declaredLength: declared)[..^1]);

        LongRejection(region).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_LongLayout_ANameRunningPastTheRegion_IsRejectedAsCorruptData()
    {
        byte[] region = DescriptorBytes.LongRegion(DescriptorBytes.Long(name: "SHORT", declaredLength: 500));

        LongRejection(region).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_LongLayout_ADescriptorCutShortBeforeItsName_IsRejectedAsCorruptData()
    {
        byte[] region = DescriptorBytes.Long()[..15];

        LongRejection(region).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_LongLayout_RunningToTheRegionsEnd_IsRejectedAsCorruptData()
    {
        // Neither a terminator nor the two closing bytes: the last descriptor ends the region.
        byte[] region = DescriptorBytes.Long(name: "LAST_FIELD_X");

        LongRejection(region).Code.Should().Be(ErrorCode.Data);
    }

    [Fact]
    public void Parse_LongLayout_TerminatorBeforeAnyDescriptor_IsRejectedBecauseATableNeedsAField()
    {
        LongRejection(DescriptorBytes.LongRegion()).Code.Should().Be(ErrorCode.Data);
    }

    private static CodeBaseException Rejection(byte[] region)
    {
        Action act = () => FieldDescriptorTable.Parse(region, usesLongFieldNames: false);
        return act.Should().Throw<CodeBaseException>().Which;
    }

    private static CodeBaseException LongRejection(byte[] region)
    {
        Action act = () => FieldDescriptorTable.Parse(region, usesLongFieldNames: true);
        return act.Should().Throw<CodeBaseException>().Which;
    }
}
