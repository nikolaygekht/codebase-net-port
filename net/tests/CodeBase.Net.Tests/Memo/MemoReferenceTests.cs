using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Memo;
using Xunit;

namespace CodeBase.Net.Tests.Memo;

/// <summary>
/// Turning a memo field's in-record bytes into a block number, in both encodings.
///
/// The corpus proves the two encodings against real files. What is left for here is the values no
/// corpus file holds: a reference that is blank, negative, absurdly large, or not a number at all.
/// Every one of those has to mean "no memo" rather than a block that exists.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class MemoReferenceTests
{
    [Fact]
    public void AFourByteReference_IsALittleEndianInteger()
    {
        MemoReference.Read([0x02, 0x00, 0x00, 0x00]).Should().Be(2);
        MemoReference.Read([0x3C, 0x00, 0x00, 0x00]).Should().Be(60);
        MemoReference.Read([0x22, 0x00, 0x00, 0x00]).Should().Be(34);
    }

    [Fact]
    public void AFourByteReferenceOfZeros_IsNoMemo()
    {
        MemoReference.Read([0x00, 0x00, 0x00, 0x00]).Should().Be(MemoReference.None);
    }

    [Fact]
    public void AFourByteReferenceThatIsNegative_IsNoMemo()
    {
        // The reference implementation returns length zero for any id at or below zero rather than
        // seeking to a negative offset.
        MemoReference.Read([0xFF, 0xFF, 0xFF, 0xFF]).Should().Be(MemoReference.None);
    }

    [Theory]
    [InlineData("         1", 1)]
    [InlineData("        10", 10)]
    [InlineData("       500", 500)]
    [InlineData("2147483647", int.MaxValue)]
    public void ATenByteReference_IsRightAlignedAsciiDigits(string stored, int expected)
    {
        // Right-aligned and space-padded, which the corpus witnesses and the C library's own
        // conversion cannot confirm because its body is not in the source drop.
        Read(stored).Should().Be(expected);
    }

    [Theory]
    [InlineData("          ")]
    [InlineData("")]
    [InlineData("      junk")]
    [InlineData("        -5")]
    [InlineData("9999999999")]
    public void ATenByteReferenceThatIsNotAPositiveNumber_IsNoMemo(string stored)
    {
        // Blank is the ordinary case and means no memo. The others are damage, and answering "no
        // memo" keeps a corrupt field from addressing a real block.
        Read(stored).Should().Be(MemoReference.None);
    }

    [Fact]
    public void TheWidthDecidesTheEncoding_NotTheContent()
    {
        // Four bytes of ASCII digits are a binary number, because that is the test the C library
        // makes. A table created at an older compatibility level therefore still reads correctly.
        Read("   1").Should().NotBe(1, "four bytes are always the binary encoding");
        Read("         1").Should().Be(1, "ten bytes are always the text encoding");
    }

    [Fact]
    public void AnEmptyFieldIsNoMemo()
    {
        MemoReference.Read([]).Should().Be(MemoReference.None);
    }

    private static int Read(string stored) => MemoReference.Read(Encoding.ASCII.GetBytes(stored));
}
