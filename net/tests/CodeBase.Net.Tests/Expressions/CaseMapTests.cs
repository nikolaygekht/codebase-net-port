using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Expressions;

/// <summary>
/// The case maps, for every code page a table can be marked with, against their own definition.
/// </summary>
/// <value>
/// There is no reference to copy (ADR-65): CodeBase maps through the host's ANSI code page or ASCII,
/// Visual FoxPro through the host's. The expectation is the rule itself, rebuilt here from the
/// encoding and Unicode's invariant mapping — a derivation, not bytes typed in. The Visual FoxPro
/// oracle confirms the one code page where its host and its table agree, cp1251, in the golden suite.
/// </value>
[Trait("Layer", "Unit")]
public sealed class CaseMapTests
{
    /// <summary>The code pages behind every mark this library knows, except the two .NET has no encoding for.</summary>
    private static readonly int[] MarkedCodePages = [.. Enum.GetValues<CodePage>()
        .Select(CodePageMap.NumberFor)
        .OfType<int>()
        .Where(number => number is not (620 or 895))
        .Distinct()
        .Order()];

    public static TheoryData<int> SingleBytePages() => [.. MarkedCodePages.Where(n => Strict(n).IsSingleByte)];

    public static TheoryData<int> MultiBytePages() => [.. MarkedCodePages.Where(n => !Strict(n).IsSingleByte)];

    [Fact]
    public void TheMarksNameTwentySingleByteAndFourMultiByteCodePages()
    {
        SingleBytePages().Should().HaveCount(20);
        MultiBytePages().Select(row => row.Data).Should().Equal(932, 936, 949, 950);
    }

    [Theory]
    [MemberData(nameof(SingleBytePages))]
    public void EveryByteMapsAsTheDefinitionSays(int codePage)
    {
        Encoding encoding = Strict(codePage);
        CaseMap map = CaseMap.For(() => encoding);
        List<string> wrong = [];

        for (int b = 0; b < 256; b++)
        {
            byte upper = Mapped(map, (byte)b, upper: true);
            byte lower = Mapped(map, (byte)b, upper: false);

            if (upper != Definition(encoding, (byte)b, upper: true) || lower != Definition(encoding, (byte)b, upper: false))
                wrong.Add($"0x{b:X2}: upper 0x{upper:X2}, lower 0x{lower:X2}");
        }

        string.Join(", ", wrong).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(SingleBytePages))]
    public void MappingTwiceIsMappingOnce(int codePage)
    {
        CaseMap map = CaseMap.For(() => Strict(codePage));

        for (int b = 0; b < 256; b++)
        {
            byte upper = Mapped(map, (byte)b, upper: true);
            byte lower = Mapped(map, (byte)b, upper: false);

            Mapped(map, upper, upper: true).Should().Be(upper, "upper of 0x{0:X2}", b);
            Mapped(map, lower, upper: false).Should().Be(lower, "lower of 0x{0:X2}", b);
        }
    }

    [Theory]
    [MemberData(nameof(SingleBytePages))]
    [MemberData(nameof(MultiBytePages))]
    public void BelowTheHighHalfOnlyTheAsciiLettersChange(int codePage)
    {
        CaseMap map = CaseMap.For(() => Strict(codePage));

        for (int b = 0; b < 0x80; b++)
        {
            Mapped(map, (byte)b, upper: true).Should().Be(b is >= 'a' and <= 'z' ? (byte)(b - 32) : (byte)b);
            Mapped(map, (byte)b, upper: false).Should().Be(b is >= 'A' and <= 'Z' ? (byte)(b + 32) : (byte)b);
        }
    }

    [Fact]
    public void TheAsciiMapChangesTheAsciiLettersAndNothingElse()
    {
        for (int b = 0; b < 256; b++)
        {
            Mapped(CaseMap.Ascii, (byte)b, upper: true).Should().Be(b is >= 'a' and <= 'z' ? (byte)(b - 32) : (byte)b);
            Mapped(CaseMap.Ascii, (byte)b, upper: false).Should().Be(b is >= 'A' and <= 'Z' ? (byte)(b + 32) : (byte)b);
        }
    }

    [Fact]
    public void AMultiByteCharacterIsMappedWholeAndItsTrailByteIsNotReadAsALetter()
    {
        // Cyrillic a is 84 70 in Shift-JIS, and 70 is the ASCII p. Mapped whole it becomes 84 40,
        // Cyrillic A; byte by byte the trail would become 50 and the character another one.
        Encoding shiftJis = Strict(932);
        byte[] value = shiftJis.GetBytes("pа");

        Upper(CaseMap.For(() => shiftJis), value).Should().Equal(shiftJis.GetBytes("PА"));
        Upper(CaseMap.Ascii, value).Should().NotEqual(shiftJis.GetBytes("PА"));
    }

    [Theory]
    // GBK has a with a grave accent and cp949 a d with a stroke, and neither has the capital. In
    // .NET's invariant mapping no letter of the four multi-byte pages changes length when cased, so
    // the length half of the rule guards an encoding a host might supply, not these.
    [InlineData(936, "à")]
    [InlineData(949, "đ")]
    public void AMultiByteCharacterWithNoSameLengthCapitalIsLeftAsItIs(int codePage, string text)
    {
        Encoding encoding = Strict(codePage);
        byte[] value = encoding.GetBytes(text);

        Upper(CaseMap.For(() => encoding), value).Should().Equal(value);
    }

    [Fact]
    public void ALeadByteWithNoTrailIsCopiedAsItStands()
    {
        // A field cut in the middle of a two-byte character ends in a lone lead byte.
        Upper(CaseMap.For(() => Strict(932)), [(byte)'a', 0x84]).Should().Equal((byte)'A', 0x84);
    }

    [Fact]
    public void TheMapIsBuiltOnItsFirstUseAndNotBefore()
    {
        // A table whose encoding is unavailable still opens and selects its tags.
        CaseMap map = CaseMap.For(() => throw new InvalidOperationException("not available"));

        map.Invoking(m => m.ToUpper("a"u8, new byte[1])).Should().Throw<InvalidOperationException>();
    }

    private static Encoding Strict(int codePage) =>
        Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);

    /// <summary>
    /// The rule, stated once more: decode, change the case invariantly, encode back, and keep it only
    /// when it is one byte that decodes to the changed letter.
    /// </summary>
    private static byte Definition(Encoding encoding, byte b, bool upper)
    {
        string text = encoding.GetString([b]);
        if (text.Length != 1)
            return b;

        char changed = upper ? char.ToUpperInvariant(text[0]) : char.ToLowerInvariant(text[0]);
        if (changed == text[0])
            return b;

        byte[] encoded;
        try
        {
            encoded = encoding.GetBytes([changed]);
        }
        catch (EncoderFallbackException)
        {
            return b;
        }

        return encoded.Length == 1 && encoding.GetString(encoded) == changed.ToString() ? encoded[0] : b;
    }

    private static byte Mapped(CaseMap map, byte b, bool upper)
    {
        byte[] result = new byte[1];
        if (upper)
            map.ToUpper([b], result);
        else
            map.ToLower([b], result);
        return result[0];
    }

    private static byte[] Upper(CaseMap map, byte[] value)
    {
        byte[] result = new byte[value.Length];
        map.ToUpper(value, result);
        return result;
    }
}
