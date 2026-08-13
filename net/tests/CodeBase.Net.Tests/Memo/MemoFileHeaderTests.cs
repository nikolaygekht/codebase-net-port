using AwesomeAssertions;
using CodeBase.Net.Memo;
using Xunit;

namespace CodeBase.Net.Tests.Memo;

/// <summary>
/// The eight bytes at the front of an FPT file, at the values the corpus does not hold.
///
/// Every corpus memo file uses a 512-byte block, so the boundaries of the two stored fields are
/// only reachable here. The block size is a [b]signed[/b] 16-bit quantity (d4data.h:2858), which
/// matters above 32767 and nowhere else.
///
/// Hand-built header bytes are legitimate test [i]input[/i]. See DEV_APPROACH.md section 4.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class MemoFileHeaderTests
{
    [Fact]
    public void Parse_ReadsBothFieldsBigEndian()
    {
        MemoFileHeader header = MemoFileHeader.Parse([0, 0, 0x01, 0x2C, 0, 0, 0x02, 0x00]);

        header.NextBlock.Should().Be(300);
        header.BlockSize.Should().Be(512);
    }

    [Fact]
    public void Parse_ABlockSizeWithTheHighBitSet_IsNegative()
    {
        // The C library holds this field in a short, so 0x8000 is -32768 there and must be -32768
        // here. Reading it unsigned would give 32768, and the offset arithmetic would then address a
        // position the reference never would.
        MemoFileHeader header = MemoFileHeader.Parse([0, 0, 0, 0, 0, 0, 0x80, 0x00]);

        header.BlockSize.Should().Be(-32768);
    }

    [Fact]
    public void Parse_ABlockSizeOfZero_StaysZero()
    {
        // Legal, and it means byte granularity rather than a corrupt file. The signedness change
        // must not disturb it.
        MemoFileHeader.Parse([0, 0, 0, 0, 0, 0, 0, 0]).BlockSize.Should().Be(0);
    }

    [Fact]
    public void Parse_OfFewerThanEightBytes_IsRefused()
    {
        Action act = () => MemoFileHeader.Parse([0, 0, 0, 0]);

        act.Should().Throw<CodeBaseException>()
           .Which.Code.Should().Be(ErrorCode.Data);
    }
}
