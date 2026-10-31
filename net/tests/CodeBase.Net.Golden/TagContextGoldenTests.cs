using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Overload resolution for a tag, against what the reference compiled with that tag at hand.
///
/// A tag changes two lengths and nothing else: a memo field that is the whole expression is capped at
/// the key size, and an ascending or descending conversion of a string is widened by a collation that
/// adds bytes per character. Each case sits beside one the rule must leave alone, so a rule applied
/// too widely fails here as surely as one not applied at all.
///
/// The context is taken from the port's own reading of the tag's header, not from the number the dump
/// prints, so the path from a tag to its context is gated too.
///
/// Then the tags themselves. TAGCTX holds a bare memo, a nullable one, and ASCEND and DESCEND of a
/// string in machine order; TAGGEN holds the same conversions under GENERAL, where the operand is
/// collated inside the conversion and the key is not collated again. Every stored key is rebuilt from
/// its record, and every conversion tag's key is also reached from the record's plain value, which is
/// how a seek through the conversion is expected to find it.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class TagContextGoldenTests
{
    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    [Fact]
    public void EveryTagContextExpressionResolvesToTheShapeTheReferenceGaveIt()
    {
        List<string> failures = [];
        using CodeBaseEngine engine = new();

        foreach (DumpTagExpression tagged in Oracle.TagContextExpressions)
        {
            using Table table = engine.OpenTable(Corpus.PathOf(tagged.Table + ".DBF"));
            TagContext context = TagContext.For(
                table.Tags[tagged.Tag].Inner.Header.Collation, table.CodePageNumber ?? CodePageMap.UnmarkedCodePage);
            DumpExpression expected = tagged.Compiled;

            CompiledExpression resolved = TypeResolver.Resolve(
                ExpressionParser.Parse(expected.Expression, new TableFieldMetadata([.. table.Fields], tagged.Table)),
                context);

            string where = $"{expected.Expression} for {tagged.Table}.{tagged.Tag}";
            if ((char)resolved.ResultType != expected.Type || resolved.Length != expected.Length)
            {
                failures.Add($"{where}: '{(char)resolved.ResultType}'/{resolved.Length}, "
                    + $"expected '{expected.Type}'/{expected.Length}");
            }

            for (int i = 0; i < Math.Min(resolved.Nodes.Count, expected.Nodes.Count); i++)
            {
                if (resolved.Nodes[i].Row != expected.Nodes[i].Row || resolved.Nodes[i].Length != expected.Nodes[i].Length)
                {
                    failures.Add($"{where} node {i}: row {resolved.Nodes[i].Row} length {resolved.Nodes[i].Length}, "
                        + $"expected row {expected.Nodes[i].Row} length {expected.Nodes[i].Length}");
                }
            }

            if (resolved.Nodes.Count != expected.Nodes.Count)
                failures.Add($"{where}: {resolved.Nodes.Count} nodes, expected {expected.Nodes.Count}");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    /// <summary>The tag-context tables, with how many keys each holds across its tags.</summary>
    public static TheoryData<string, int> TagTables() => new()
    {
        { "TAGCTX", 40 },
        { "TAGGEN", 40 },
    };

    /// <summary>The tags whose whole expression is ASCEND or DESCEND of the text field.</summary>
    public static TheoryData<string, string> ConversionTags() => new()
    {
        { "TAGCTX", "T_ASC" },
        { "TAGCTX", "T_DESC" },
        { "TAGGEN", "G_ASC" },
        { "TAGGEN", "G_DESC" },
    };

    [Theory]
    [MemberData(nameof(TagTables))]
    public void EveryKeyOfEveryTagContextTagIsRebuiltFromTheRecordItNames(string tableName, int keys)
    {
        // The rules above, in stored keys: a bare memo cut at 240, a nullable one cut at 239 behind its
        // null byte, and ASCEND and DESCEND of a string in machine order and under GENERAL -- each key
        // rebuilt through the converter the tag resolves at open, so a length or a collation the tag's
        // context gets wrong shows up as a key that does not match.
        CorpusIndexDump index = CorpusIndexDump.Load(tableName + ".cdx");
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        List<string> failures = [];
        int compared = 0;

        foreach (DumpIndexTag expected in index.RealTags)
        {
            SeekConverter converter = Converter(table, tableName, expected.Name.TrimEnd());
            ExpressionEvaluator evaluator = new(converter.Compiled);

            foreach (DumpIndexKey stored in expected.Keys)
            {
                table.Go((int)stored.Record);

                byte[] built = new byte[converter.KeyLength];
                RecordKey.WriteValue(converter, evaluator.Evaluate(Context(table)), built);

                compared++;
                if (!built.AsSpan().SequenceEqual(stored.Key))
                {
                    failures.Add($"{expected.Name.TrimEnd()} record {stored.Record}: "
                        + $"{Convert.ToHexString(built)}, expected {Convert.ToHexString(stored.Key)}");
                }
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        compared.Should().Be(keys, "every key of every tag compared");
    }

    [Theory]
    [MemberData(nameof(ConversionTags))]
    public void EveryRecordsTextSoughtThroughTheConversionIsThatRecordsKey(string tableName, string tagName)
    {
        // The search for a value is the stored key of the record holding it -- the expectation the
        // reference cannot give, since its own seek never finds a DESCEND key by value (ADR-64). The
        // prefix form must be a prefix of the stored key as well, or a partial seek would miss it.
        DumpIndexTag expected = CorpusIndexDump.Load(tableName + ".cdx").RealTags.Single(t => t.Name.TrimEnd() == tagName);
        CorpusDump records = CorpusDump.Load(tableName);
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));

        SeekConverter converter = Converter(table, tableName, tagName);
        List<string> failures = [];

        foreach (DumpIndexKey stored in expected.Keys)
        {
            byte[] text = records.Records[(int)stored.Record - 1]["K_TEXT"].Bytes;

            byte[] whole = new byte[converter.KeyLength];
            int length = ConversionSearch.Write(converter, text, prefix: false, whole);
            if (!whole.AsSpan(0, length).SequenceEqual(stored.Key))
            {
                failures.Add($"record {stored.Record} whole: {Convert.ToHexString(whole, 0, length)}, "
                    + $"expected {Convert.ToHexString(stored.Key)}");
            }

            byte[] opening = new byte[converter.KeyLength];
            int written = ConversionSearch.Write(converter, text.AsSpan(0, 3), prefix: true, opening);
            if (written == 0 || !stored.Key.AsSpan().StartsWith(opening.AsSpan(0, written)))
                failures.Add($"record {stored.Record} prefix: {Convert.ToHexString(opening, 0, written)}");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void TheSectionHoldsBothRulesAndTheCasesBesideThem()
    {
        // A reader that found nothing would pass the gate above, so the size and the two tags are
        // pinned: one machine-order tag of CDXEXPR, one GENERAL tag of CDXCOLL.
        Oracle.TagContextExpressions.Should().HaveCount(7);
        Oracle.TagContextExpressions.Select(t => $"{t.Table}.{t.Tag}").Distinct()
            .Should().Equal("CDXEXPR.T_NUM", "CDXCOLL.C_GEN");
    }

    private static SeekConverter Converter(Table table, string tableName, string tagName) =>
        SeekConverter.For(
            table.Tags[tagName].Inner.Header,
            [.. table.Fields],
            table.CodePageNumber ?? CodePageMap.UnmarkedCodePage,
            ExpressionOptions.Invariant,
            tableName);

    private static EvaluationContext Context(Table table) => new()
    {
        Record = table.CurrentRecord,
        NullFlags = table.NullFlags,
        RecordNumber = table.RecordNumber,
        Deleted = table.Deleted,
        RecordCount = table.RecordCount,
        Options = ExpressionOptions.Invariant,
        Memo = field => table.GetMemoBytes(field),
    };
}
