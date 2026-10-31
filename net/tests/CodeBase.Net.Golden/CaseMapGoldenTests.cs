using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// UPPER and LOWER by the table's code page, against the keys Visual FoxPro stored and through the public query surface.
///
/// The Visual FoxPro oracle is the one witness there is (ADR-65): its host's ANSI code page is 1251
/// and its tables are marked 1251, so for it the host's mapping and the table's agree, and its stored
/// keys are the answer. Record 16 of VFPFUNC holds a Cyrillic upper-case letter beside a lower-case
/// one, so T_UPPER and T_LOWER each have a key the ASCII mapping gets wrong.
///
/// An unmarked table maps the ASCII letters only, as CodeBase does, unless the host named an
/// encoding for its unmarked tables; both halves are pinned here on a corpus table with no mark.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class CaseMapGoldenTests
{
    private const string Tags = "vfp/VFPFUNC.DBF";

    [Theory]
    [InlineData("T_UPPER")]
    [InlineData("T_LOWER")]
    public void EveryKeyVisualFoxProStoredForACaseTagIsRebuiltFromItsRecord(string tagName)
    {
        using CodeBaseEngine engine = new() { ExpressionOptions = ExpressionOptions.CodeBase.WithCentury(20) };
        using Table table = engine.OpenTable(Corpus.PathOf(Tags));

        CdxTag tag = table.Tags[tagName].Inner;
        SeekConverter converter = SeekConverter.For(
            tag.Header, [.. table.Fields], table.CodePageNumber!.Value, table.ExpressionOptions, "VFPFUNC");
        ExpressionEvaluator evaluator = new(converter.Compiled);

        List<string> failures = [];
        int compared = 0;
        bool sawTheHighHalf = false;
        TagCursor cursor = tag.OpenCursor();

        for (bool on = cursor.Top(); on; on = cursor.Next())
        {
            IndexEntry stored = cursor.Current;
            table.Go((int)stored.Record);

            byte[] built = new byte[converter.KeyLength];
            RecordKey.WriteValue(converter, evaluator.Evaluate(table.EvaluationContext()), built);

            compared++;
            sawTheHighHalf |= stored.Key.Any(b => b >= 0x80);
            if (!built.AsSpan().SequenceEqual(stored.Key))
                failures.Add($"record {stored.Record}: {Convert.ToHexString(built)}, stored {Convert.ToHexString(stored.Key)}");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        compared.Should().Be(table.RecordCount);
        sawTheHighHalf.Should().BeTrue("record 16's Cyrillic letters are the keys an ASCII mapping gets wrong");
    }

    [Fact]
    public void AQueryCasesItsRecordsAsTheTableIs()
    {
        // UPPER of record 16's "Йи" is "ЙИ" by cp1251 and "Йи" by ASCII, so only the table's own
        // mapping finds it. The comparand is built with CHR because a filter's text is read as
        // Latin1 and cannot spell Cyrillic. Nothing here is folded into a constant -- the optimizer
        // treats only literals and date conversions as constants -- so the per-record check answers.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Tags));
        table.SelectTag(null);

        Query query = table.Query("UPPER(K_TEXT) = CHR(201) + CHR(200)");
        List<int> found = [];
        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
            found.Add(table.RecordNumber);

        found.Should().Equal(16);
    }

    [Theory]
    // A filter's text is the caller's, so its literals are encoded as the table's text is read:
    // cp1251 here, where "Йи" is C9 E8. The second filter is answered by T_UPPER, sought with the
    // literal's bytes, and the scan the gates compare it with must agree.
    [InlineData("K_TEXT = \"Йи\"")]
    [InlineData("UPPER(K_TEXT) = \"ЙИ\"")]
    public void AFiltersTextIsEncodedAsTheTablesTextIs(string filter)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Tags));
        table.SelectTag(null);

        Query query = table.Query(filter);
        List<int> found = [];
        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
            found.Add(table.RecordNumber);

        found.Should().Equal(16);
    }

    [Fact]
    public void AFilterWithACharacterTheCodePageCannotHoldIsRefused()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(Tags));

        table.Invoking(t => t.Query("K_TEXT = \"日本\"").Top())
            .Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.UnrecognizedValue);
    }

    [Fact]
    public void AnUnmarkedTableCasesTheAsciiLettersOnly()
    {
        // 0x82 is e-acute in cp437, whose capital is 0x90. An unmarked table states no encoding, and
        // CodeBase leaves the byte alone there; so does this library.
        Cased("UPPER(CHR(130) + \"a\")", defaultEncoding: null).Should().Equal(0x82, (byte)'A');
    }

    [Fact]
    public void AnUnmarkedTableCasesByTheEncodingTheHostNamedForIt()
    {
        System.Text.Encoding cp437 = System.Text.Encoding.GetEncoding(437);

        Cased("UPPER(CHR(130) + \"a\")", cp437).Should().Equal(cp437.GetBytes("ÉA"));
    }

    private static byte[] Cased(string expression, System.Text.Encoding? defaultEncoding)
    {
        // CDXEXPR carries no code-page mark.
        using CodeBaseEngine engine = new() { DefaultEncoding = defaultEncoding };
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));
        table.CodePageNumber.Should().BeNull();

        table.Go(1);
        return new ExpressionEvaluator(table.CompileFilter(expression))
            .Evaluate(table.EvaluationContext()).Bytes.ToArray();
    }
}
