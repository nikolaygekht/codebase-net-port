using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// How STR of a wide field turns UTF-16 into a code page's bytes, which is Windows' best-fit.
///
/// The reference calls WideCharToMultiByte with the host's code page; the port uses the table's
/// (ADR-77). What .NET's encodings already do the same way is taken from them; the few code points
/// Windows best-fits and .NET does not come from the generated supplement, whose own premise is
/// checked here. The corpus case WSTR holds the reference's answers on cp1251.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class WideNarrowingTests
{
    [Theory]
    [InlineData(1251)]
    [InlineData(1252)]
    [InlineData(866)]
    public void TheBlankOfAWideFieldBecomesASpace(int codePage)
    {
        // U+2000 is what CodeBase fills a wide field with. On cp1251 and cp866 .NET gives a question
        // mark for it and the supplement supplies Windows' space; on cp1252 .NET already agrees.
        Narrow(codePage, "\u2000").Should().Equal((byte)' ');
    }

    [Fact]
    public void ACharacterTheCodePageHoldsBecomesItsByte()
    {
        string text = "\u0416\u044F";

        Narrow(1251, text).Should().Equal(Encoding.GetEncoding(1251).GetBytes(text));
    }

    [Fact]
    public void ACharacterTheCodePageLacksIsBestFitted()
    {
        // A with a macron has no place in cp1251 and is best-fitted to A, by Windows and .NET alike.
        Narrow(1251, "\u0100").Should().Equal((byte)'A');
    }

    [Fact]
    public void ACharacterWithNoNearOneBecomesAQuestionMark() =>
        Narrow(1251, "\u4E2D").Should().Equal((byte)'?');

    [Fact]
    public void EachHalfOfASurrogatePairBecomesAQuestionMark()
    {
        // One byte per code unit, as WideCharToMultiByte gives on a single-byte code page.
        Narrow(1251, "a\uD83D\uDE00b").Should().Equal((byte)'a', (byte)'?', (byte)'?', (byte)'b');
    }

    [Fact]
    public void OnlyTheRequestedWidthIsNarrowed()
    {
        byte[] wide = Encoding.Unicode.GetBytes("abcd");
        byte[] destination = new byte[2];

        WideNarrowing.For(() => 1251).Narrow(wide, destination);

        destination.Should().Equal((byte)'a', (byte)'b');
    }

    [Theory]
    [InlineData(936)]
    [InlineData(932)]
    public void AMultiByteCodePageIsRefused(int codePage)
    {
        Action act = () => Narrow(codePage, "a");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
    }

    [Fact]
    public void ACodePageNoProviderSuppliesIsRefused()
    {
        // 620 is a FoxPro-era DOS code page that Windows never defined.
        Action act = () => Narrow(620, "a");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
    }

    [Fact]
    public void TheCodePageIsAskedForOnlyWhenFirstUsed()
    {
        // A table whose code page is unavailable still opens and compiles; only narrowing fails.
        int asked = 0;
        WideNarrowing narrowing = WideNarrowing.For(() => { asked++; return 1251; });

        asked.Should().Be(0);
        narrowing.Narrow(Encoding.Unicode.GetBytes("a"), new byte[1]);
        narrowing.Narrow(Encoding.Unicode.GetBytes("b"), new byte[1]);
        asked.Should().Be(1);
    }

    [Fact]
    public void EverySupplementEntryIsOneDotNetGetsWrong()
    {
        // The supplement's premise: for each listed code point .NET's encoding answers a question
        // mark and Windows something else. An entry .NET had come to map would be redundant; one it
        // mapped differently would mean the tool's comparison no longer holds.
        foreach ((int codePage, IReadOnlyDictionary<char, byte> entries) in WindowsBestFitSupplement.ByCodePage)
        {
            Encoding encoding = Encoding.GetEncoding(codePage);

            foreach ((char codePoint, byte value) in entries)
            {
                encoding.GetBytes(codePoint.ToString()).Should().Equal(
                    [(byte)'?'], $"cp{codePage} U+{(int)codePoint:X4} is listed as one .NET does not map");
                value.Should().NotBe((byte)'?');
            }
        }
    }

    private static byte[] Narrow(int codePage, string text)
    {
        byte[] destination = new byte[text.Length];
        WideNarrowing.For(() => codePage).Narrow(Encoding.Unicode.GetBytes(text), destination);
        return destination;
    }
}
