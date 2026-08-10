using AwesomeAssertions;
using CodeBase.Net.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What each version of the format promises about how its files are read.
///
/// The interesting rows are Visual FoxPro 9, version 0x32, which answers differently to two
/// questions that look like one, and FoxPro 2 with a memo, version 0xF5, whose memo file is
/// announced by the version rather than by the companion-file byte.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class DbfFormatVariantTests
{
    [Theory]
    [InlineData(0x30, 0x30)]
    [InlineData(0x31, 0x30)]   // extensions are recorded in the flags; the table reads as 0x30
    [InlineData(0x32, 0x32)]
    [InlineData(0x03, 0x03)]
    [InlineData(0xF5, 0xF5)]
    public void Resolve_NormalizesOnlyTheExtendedVisualFoxProVersion(byte stored, byte normalized)
    {
        IDbfFormatVariant.Resolve(stored).NormalizedVersion.Should().Be(normalized);
    }

    [Theory]
    [InlineData(0x30, true)]
    [InlineData(0x31, true)]
    [InlineData(0x32, true)]    // Visual FoxPro 9 keeps the newer types
    [InlineData(0x7F, true)]    // the top of the range that compares as positive
    [InlineData(0x03, false)]
    [InlineData(0x83, false)]
    [InlineData(0xF5, false)]   // FoxPro 2 with a memo, and the case that makes the rule matter
    public void AllowsVisualFoxProTypes_ComparesTheVersionToThirtyHexAsASignedByte(byte version, bool allowed)
    {
        // Signed, because the C library keeps the version in a plain char. Every byte from 0x80 up
        // is therefore negative and below 0x30, which is why 0xF5 — an everyday FoxPro 2 table with
        // a memo — cannot carry a datetime, currency or double field. Comparing as unsigned would
        // admit those fields to tables the reference implementation refuses them on.
        IDbfFormatVariant.Resolve(version).AllowsVisualFoxProTypes.Should().Be(allowed);
    }

    [Theory]
    [InlineData(0x30, true)]
    [InlineData(0x31, true)]
    [InlineData(0x32, false)]   // ...but not the meaning of its descriptor flags
    [InlineData(0x03, false)]
    [InlineData(0xF5, false)]
    public void InterpretsDescriptorFlags_IsDecidedByTheVersionBeingExactlyThirtyHex(byte version, bool interprets)
    {
        // Not the same question as the one above, and 0x32 is where they part company. A Visual
        // FoxPro 9 table may hold a datetime field, yet none of its fields can be nullable, because
        // nothing reads the byte that would say so.
        IDbfFormatVariant.Resolve(version).InterpretsDescriptorFlags.Should().Be(interprets);
    }

    [Theory]
    [InlineData(0x30, 0x02, true)]    // the companion-file byte decides
    [InlineData(0x30, 0x00, false)]
    [InlineData(0x30, 0x01, false)]   // that bit is the production index, not the memo
    [InlineData(0x31, 0x02, true)]    // normalized first, so it decides the same way
    [InlineData(0xF5, 0x00, true)]    // the version decides, and the companion byte is not read
    [InlineData(0x03, 0x02, false)]   // ...so a set memo bit means nothing here
    [InlineData(0x32, 0x02, false)]   // Visual FoxPro 9: its memo file is never opened
    public void HasMemo_AsksTheVersionFirstAndTheCompanionByteOnlyForVisualFoxPro(
        byte version, byte tableFlags, bool hasMemo)
    {
        IDbfFormatVariant.Resolve(version).HasMemo(tableFlags).Should().Be(hasMemo);
    }

    [Fact]
    public void Resolve_ReturnsTheSameInstanceForVisualFoxPro()
    {
        // Stateless, so there is nothing to allocate per table.
        IDbfFormatVariant.Resolve(0x30).Should().BeSameAs(IDbfFormatVariant.Resolve(0x31));
    }

    [Fact]
    public void Resolve_AnUnheardOfVersion_IsReadAsALegacyTableRatherThanRefused()
    {
        // Any version byte is structurally acceptable, as it is to the C library. What an unknown
        // one costs a caller is the meaning of the descriptor flags, not the ability to open.
        IDbfFormatVariant variant = IDbfFormatVariant.Resolve(0x8B);

        variant.NormalizedVersion.Should().Be(0x8B);
        variant.AllowsVisualFoxProTypes.Should().BeFalse();
        variant.InterpretsDescriptorFlags.Should().BeFalse();
        variant.HasMemo(0x00).Should().BeTrue("0x8B has the memo bit set in the version byte");
    }
}
