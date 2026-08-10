using System.Text;
using AwesomeAssertions;
using Xunit;

namespace CodeBase.Net.Tests;

/// <summary>
/// The code-page mark table: header byte 29 to a code page, and a code page to an encoding.
///
/// The marks are the twenty-six Visual FoxPro documents (DBF-FORMAT.md section 8.1, ADR-19), not the
/// six the original C library defines — a distinction that decides whether an ordinary Cyrillic or
/// CJK table reads as text or as mojibake. These tests deliberately never register an encoding
/// provider: resolving a mark to a number must not need one.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class CodePageMapTests
{
    /// <summary>Every mark Visual FoxPro documents, with the code page it names.</summary>
    private static readonly (byte Mark, CodePage CodePage, int Number)[] Marks =
    [
        (0x01, CodePage.Cp437, 437),
        (0x02, CodePage.Cp850, 850),
        (0x03, CodePage.Cp1252, 1252),
        (0x04, CodePage.Cp10000, 10000),
        (0x64, CodePage.Cp852, 852),
        (0x65, CodePage.Cp866, 866),
        (0x66, CodePage.Cp865, 865),
        (0x67, CodePage.Cp861, 861),
        (0x68, CodePage.Cp895, 895),
        (0x69, CodePage.Cp620, 620),
        (0x6A, CodePage.Cp737, 737),
        (0x6B, CodePage.Cp857, 857),
        (0x78, CodePage.Cp950, 950),
        (0x79, CodePage.Cp949, 949),
        (0x7A, CodePage.Cp936, 936),
        (0x7B, CodePage.Cp932, 932),
        (0x7C, CodePage.Cp874, 874),
        (0x7D, CodePage.Cp1255, 1255),
        (0x7E, CodePage.Cp1256, 1256),
        (0x96, CodePage.Cp10007, 10007),
        (0x97, CodePage.Cp10029, 10029),
        (0x98, CodePage.Cp10006, 10006),
        (0xC8, CodePage.Cp1250, 1250),
        (0xC9, CodePage.Cp1251, 1251),
        (0xCA, CodePage.Cp1254, 1254),
        (0xCB, CodePage.Cp1253, 1253),
    ];

    public static TheoryData<byte, CodePage, int> DocumentedMarks()
    {
        TheoryData<byte, CodePage, int> data = [];
        foreach ((byte mark, CodePage codePage, int number) in Marks)
            data.Add(mark, codePage, number);
        return data;
    }

    [Fact]
    public void TheTableCoversEveryMarkVisualFoxProDocuments()
    {
        // Part of the gate rather than commentary: a mark dropped from the data above would take its
        // assertions with it silently. Twenty-six is the documented count.
        Marks.Should().HaveCount(26);
    }

    [Theory]
    [MemberData(nameof(DocumentedMarks))]
    public void Resolve_NamesEveryDocumentedMark(byte mark, CodePage expected, int number)
    {
        _ = number;

        CodePageMap.Resolve(mark).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(DocumentedMarks))]
    public void NumberFor_ReportsTheCodePageNumberRatherThanTheMark(byte mark, CodePage codePage, int number)
    {
        _ = mark;

        CodePageMap.NumberFor(codePage).Should().Be(number);
    }

    [Fact]
    public void EveryMarkNamesADistinctCodePage()
    {
        // One-to-one, unlike the wider "xBase" table in circulation, which maps many marks onto the
        // same code page and would push a lossy normalization into the write path.
        Marks.Select(m => m.Number).Should().OnlyHaveUniqueItems();
        Marks.Select(m => m.Mark).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Resolve_ReportsAnUnmarkedTableAsUnmarkedRatherThanUnknown()
    {
        CodePageMap.Resolve(0x00).Should().Be(CodePage.Unmarked);
        CodePageMap.NumberFor(CodePage.Unmarked).Should().BeNull();
    }

    [Theory]
    [InlineData((byte)0x05)]   // between the low group and the DOS group
    [InlineData((byte)0x13)]   // Shift-JIS in the wider xBase table, undocumented by Visual FoxPro
    [InlineData((byte)0x57)]   // Windows ANSI in the wider table
    [InlineData((byte)0xCC)]   // Baltic in the wider table
    [InlineData((byte)0xFF)]
    public void Resolve_ReportsAMarkOutsideTheDocumentedSetAsUnknown(byte mark)
    {
        CodePageMap.Resolve(mark).Should().Be(CodePage.Unknown);
        CodePageMap.NumberFor(CodePage.Unknown).Should().BeNull();
    }

    [Fact]
    public void Resolve_NamesTheMacintoshMarkWhereTheCLibraryLeavesItUnnamed()
    {
        // 0x04 is the one mark the two authorities disagree on: the C library calls it an unknown
        // back-compatibility placeholder and refuses it for collation, Visual FoxPro documents it as
        // code page 10000. Visual FoxPro is the compatibility target, so it wins (ADR-19).
        CodePageMap.Resolve(0x04).Should().Be(CodePage.Cp10000);
        CodePageMap.NumberFor(CodePage.Cp10000).Should().Be(10000);
    }

    [Theory]
    [MemberData(nameof(DocumentedMarks))]
    public void NumberFor_NeedsNoEncodingProvider(byte mark, CodePage codePage, int number)
    {
        _ = number;

        // Including the two marks no provider can satisfy. Reading a table's shape must not depend on
        // what the host registered (ADR-17), and a code page number is shape, not text.
        Func<int?> read = () => CodePageMap.NumberFor(CodePageMap.Resolve(mark));

        read.Should().NotThrow();
        read().Should().Be(CodePageMap.NumberFor(codePage));
    }

    [Fact]
    public void EncodingFor_UsesTheFallbackWhereTheTableNamesNoCodePage()
    {
        Encoding fallback = Encoding.ASCII;

        CodePageMap.EncodingFor(CodePage.Unmarked, fallback).Should().BeSameAs(fallback);
        CodePageMap.EncodingFor(CodePage.Unknown, fallback).Should().BeSameAs(fallback);
    }

    [Theory]
    [InlineData(CodePage.Cp620)]
    [InlineData(CodePage.Cp895)]
    public void EncodingFor_ExplainsTheTwoCodePagesNoProviderCanSupply(CodePage codePage)
    {
        // Mazovia and Kamenicky are FoxPro-era DOS code pages Windows never defined, so registering
        // a provider does not help. They are still recognized marks with a number, which is why the
        // failure has to explain itself rather than look like a missing provider.
        Action act = () => CodePageMap.EncodingFor(codePage, null);

        act.Should().Throw<CodeBaseException>()
           .Where(e => e.Code == ErrorCode.NotSupported)
           .WithMessage("*620 and 895*");
    }
}
