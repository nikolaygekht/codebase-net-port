using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// CodeBase's UTF-16 fields, W and O, against what the reference wrote and computed for case WSTR.
///
/// The rest of the corpus gates already take WSTR — its records, its four tags' keys rebuilt from
/// the records, the key-level seeks, the queries over STR(K_W). What is left for here is what only
/// this case has: its own expression dump, whose STR values the reference narrowed by the host's
/// ANSI code page — cp1251, the table's own, which is the one host on which the port's rule and the
/// reference's agree (ADR-77) — and the public text paths, reading a wide field and seeking a wide
/// tag by the text a record holds.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class WideStringGoldenTests
{
    private const string TableName = "WSTR";

    /// <summary>What CodeBase fills the unassigned rest of a wide field with.</summary>
    private const char Blank = (char)0x2000;

    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load(TableName);

    [Fact]
    public void TheDumpHoldsTheExpressionsTheCaseWasWrittenFor()
    {
        // A data-driven gate that read nothing would pass having proved nothing.
        Oracle.FieldExpressions.Select(e => e.Expression).Should().Equal(
            "K_W", "K_O", "STR(K_W)", "STR(K_W, 3)", "STR(K_W, 10, 2)");
        Oracle.Rejections.Should().HaveCount(3);
    }

    [Fact]
    public void EveryFieldExpressionCompilesToTheReferencesShape()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));

        foreach (DumpExpression expected in Oracle.FieldExpressions)
        {
            CompiledExpression compiled = Compile(table, expected.Expression);

            ((char)compiled.ResultType).Should().Be(expected.Type, expected.Expression);
            compiled.Length.Should().Be(expected.Length, expected.Expression);
            compiled.Nodes.Select(n => (n.Row, n.Length)).Should().Equal(
                expected.Nodes.Select(n => (n.Row, n.Length)), expected.Expression);
        }
    }

    [Fact]
    public void EveryFieldExpressionHasTheReferencesValueAtEveryRecord()
    {
        // STR's values are the narrowing: U+2000 to a space, the supplement's code points to what
        // Windows best-fits them to, a surrogate pair to two question marks.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));
        List<string> failures = [];

        foreach (DumpExpression expected in Oracle.FieldExpressions)
        {
            ExpressionEvaluator evaluator = new(Compile(table, expected.Expression));

            for (int record = 1; record <= table.RecordCount; record++)
            {
                table.Go(record).Should().Be(GoResult.Ok);
                byte[] value = evaluator.Evaluate(table.EvaluationContext()).ToRaw();

                if (!value.AsSpan().SequenceEqual(expected.FreshValueAt(record)))
                    failures.Add($"{expected.Expression} at record {record}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void EveryExpressionTheReferenceRefusesIsRefused()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));

        foreach (DumpRejection rejected in Oracle.Rejections)
        {
            Action act = () => Compile(table, rejected.Expression);

            act.Should().Throw<CodeBaseException>(rejected.Expression);
        }
    }

    [Fact]
    public void AWideFieldReadsAsTheCodeUnitsTheRecordHolds()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));
        FieldDefinition wide = table.Fields["K_W"];
        FieldDefinition counted = table.Fields["K_O"];

        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record).Should().Be(GoResult.Ok);

            table.GetString(wide).Should().Be(Encoding.Unicode.GetString(table.GetRawBytes(wide)));
            table.GetString(counted).Should().Be(Encoding.Unicode.GetString(table.GetRawBytes(counted)[..^2]));
        }
    }

    [Theory]
    [InlineData("W_ASC", "K_W")]
    [InlineData("W_DESC", "K_W")]
    [InlineData("O_ASC", "K_O")]
    public void EveryRecordIsFoundByTheTextItWasAssigned(string tagName, string fieldName)
    {
        // The text as assigned is the stored text less its U+2000 tail; a whole seek pads it back, so
        // it lands on a key equal to the record's own. Compared as text, because an O field's count
        // is not part of its key and two records can differ only there.
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));
        FieldDefinition field = table.Fields[fieldName];
        table.SelectTag(table.Tags[tagName]);

        for (int record = 1; record <= table.RecordCount; record++)
        {
            table.Go(record).Should().Be(GoResult.Ok);
            string stored = table.GetString(field);
            string assigned = stored.TrimEnd(Blank);

            table.Seek(assigned).Should().Be(GoResult.Ok, $"record {record} holds '{assigned}'");
            table.GetString(field).Should().Be(stored, $"record {record} holds '{assigned}'");
        }
    }

    [Theory]
    [InlineData("W_ASC")]
    [InlineData("W_DESC")]
    [InlineData("O_ASC")]
    [InlineData("S_ASC")]
    public void AStepFromAnyRecordLandsOnTheNextKeyTheIndexHolds(string tagName)
    {
        // Stepping from a record the cursor was not on means building that record's key to find its
        // place, through the tag cursor's own context -- which for S_ASC has to narrow by the table's
        // code page, as the key rebuild's context does.
        IReadOnlyList<DumpIndexKey> keys = CorpusIndexDump.Load(TableName + ".cdx").RealTags
            .Single(t => t.Name.TrimEnd() == tagName).Keys;
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(TableName + ".DBF"));
        Tag tag = table.Tags[tagName];

        for (int i = 0; i + 1 < keys.Count; i++)
        {
            table.Go((int)keys[i].Record).Should().Be(GoResult.Ok);

            table.GoNextIndexed(tag).Should().Be(GoResult.Ok);
            table.RecordNumber.Should().Be((int)keys[i + 1].Record, $"{tagName} after record {keys[i].Record}");
        }
    }

    private static CompiledExpression Compile(Table table, string expression) =>
        TypeResolver.Resolve(
            ExpressionParser.Parse(expression, new TableFieldMetadata([.. table.Fields], TableName, table.LongFieldNames)));
}
