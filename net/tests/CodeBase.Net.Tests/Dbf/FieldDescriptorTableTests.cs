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
    public void Parse_LongFieldNameLayout_IsRejectedAsNotSupported()
    {
        // Refused rather than guessed at: no corpus case covers this layout, so decoding it would
        // assert this port's reading of a specification against itself.
        byte[] region = DescriptorBytes.Region(DescriptorBytes.Build());

        Action act = () => FieldDescriptorTable.Parse(region, usesLongFieldNames: true);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
    }

    private static CodeBaseException Rejection(byte[] region)
    {
        Action act = () => FieldDescriptorTable.Parse(region, usesLongFieldNames: false);
        return act.Should().Throw<CodeBaseException>().Which;
    }
}
