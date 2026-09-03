using AwesomeAssertions;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The four things the query optimizer needs from a compiled expression.
///
/// [c]QUERY-OPTIMIZER.md[/c] section 3 is a contract on this step's output, and it is the one part of
/// the gate with no oracle: the reference exposes none of this through a dump, because it is the
/// shape of a data structure rather than an answer. So the corpus supplies the [i]cases[/i] here and
/// not the expectations — every expression the reference compiled, used as a population to check
/// properties over.
///
/// What keeps that honest is that the properties are not restatements of the implementation. The
/// is-constant predicate is checked against the evaluator's actual behaviour rather than against a
/// second copy of its own rule: whatever it calls constant must evaluate with no record at all, and
/// whatever reads a field must not be called constant. Those two can disagree, and if they did the
/// optimizer would either evaluate something it cannot or refuse to optimize what it could.
///
/// The reason this matters now rather than later is that assertion 2 of the step's gate froze the
/// node shape against the reference's own arrays. Getting the shape wrong would not fail anything
/// here; it would surface in step 011 as "no tag ever matches", which is a silent fall-back to
/// scanning and never an error.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class OptimizerContractGoldenTests
{
    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    private static readonly CorpusIndexDump Index = CorpusIndexDump.Load("CDXEXPR.cdx");

    /// <summary>
    /// The rows that load a field, which are the only rows a field reference may sit on.
    /// </summary>
    private static bool IsLoader(int row) => row
        is FunctionRows.FieldCharacter or FunctionRows.FieldCharacterMaterialized
        or FunctionRows.FieldLogical or FunctionRows.FieldDateAsDouble or FunctionRows.FieldDateAsText
        or FunctionRows.FieldNumericAsDouble or FunctionRows.FieldNumericAsText
        or FunctionRows.FieldCurrency or FunctionRows.FieldDouble or FunctionRows.FieldInteger
        or FunctionRows.FieldTimestamp or FunctionRows.FieldIntegerAsDouble
        or FunctionRows.FieldCurrencyAsDouble or FunctionRows.FieldBinaryFloat
        or FunctionRows.FieldIntegerAsFloat or FunctionRows.FieldMemo;

    // ---- 1. a node table, retained and ordered ------------------------------

    [Fact]
    public void EveryNodeCarriesAllSixFieldsTheOptimizerReads()
    {
        List<string> failures = [];

        foreach ((string text, CompiledExpression compiled) in Every())
        {
            for (int i = 0; i < compiled.Nodes.Count; i++)
            {
                ExpressionNode node = compiled.Nodes[i];

                // Row and result type: the optimizer classifies an operator by which band of row
                // indexes it falls in, and reads the type to know what it is comparing.
                if (node.Row < 0 || node.Row >= FunctionTable.Rows.Count)
                    failures.Add($"{text}: node {i} has row {node.Row}");

                if (node.Type == ExpressionType.None)
                    failures.Add($"{text}: node {i} has no result type");

                // Declared length, operand count and subtree size.
                if (node.Length < 0)
                    failures.Add($"{text}: node {i} has length {node.Length}");

                if (node.ParameterCount < 0)
                    failures.Add($"{text}: node {i} takes {node.ParameterCount} operands");

                if (node.SubtreeSize < 1 || node.SubtreeSize > i + 1)
                    failures.Add($"{text}: node {i} claims a subtree of {node.SubtreeSize}");

                // The field it loads, and only where it loads one. This is the field half of the
                // comparison the optimizer makes, so a loader without a field would make a tag
                // unmatchable and a non-loader with one would make it match wrongly.
                if (IsLoader(node.Row) != (node.Field is not null))
                    failures.Add($"{text}: node {i} on row {node.Row} has field {node.Field is not null}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void EachNodesSubtreeSizeSpansExactlyItsOperands()
    {
        // What makes a subexpression addressable by the index of its root, which every other clause
        // of the contract depends on: one plus the sizes of the operands immediately before it.
        List<string> failures = [];

        foreach ((string text, CompiledExpression compiled) in Every())
        {
            for (int i = 0; i < compiled.Nodes.Count; i++)
            {
                ExpressionNode node = compiled.Nodes[i];
                int at = i - 1;
                int spanned = 1;

                for (int operand = 0; operand < node.ParameterCount; operand++)
                {
                    if (at < 0)
                        break;

                    spanned += compiled.Nodes[at].SubtreeSize;
                    at -= compiled.Nodes[at].SubtreeSize;
                }

                if (spanned != node.SubtreeSize)
                    failures.Add($"{text}: node {i} spans {spanned} but claims {node.SubtreeSize}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void AComparisonIsFoundByItsRowBandAndEachOperatorByItsSubRange()
    {
        // The classification the optimizer gets free from the row index, and it works only because
        // the table is transcribed in row order.
        CompiledExpression compiled = Compile("K_TEXT = \"SM\"", Fields());

        compiled.Nodes[^1].Row.Should()
            .BeInRange(FunctionRows.FirstComparison, FunctionRows.LastComparison);

        FunctionTable.Row(compiled.Nodes[^1].Row).Code.Should().Be(40);
    }

    // ---- 2. evaluate a subexpression, at a node, without a record -----------

    [Fact]
    public void EvaluatingAtTheRootIsTheSameAsEvaluatingTheWholeExpression()
    {
        using CodeBaseEngine engine = Engine();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));
        table.Go(1);

        List<string> failures = [];

        foreach ((string text, CompiledExpression compiled) in Every())
        {
            ExpressionEvaluator evaluator = new(compiled);
            EvaluationContext context = Context(table);

            byte[] whole = evaluator.Evaluate(context).ToRaw();
            byte[] atRoot = evaluator.Evaluate(compiled.Nodes.Count - 1, context).ToRaw();

            if (!whole.AsSpan().SequenceEqual(atRoot))
                failures.Add(text);
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void AConstantOperandOfEveryComparisonEvaluatesWithNoRecord()
    {
        // The optimizer's actual use: it reads a comparison's constant side to turn the comparison
        // into a range over a tag, and it has no record when it does (C4CONST.C:971-996).
        List<string> failures = [];
        int read = 0;

        foreach ((string text, CompiledExpression compiled) in Every())
        {
            ExpressionEvaluator evaluator = new(compiled);

            for (int i = 0; i < compiled.Nodes.Count; i++)
            {
                if (!evaluator.IsConstant(i))
                    continue;

                read++;
                try
                {
                    evaluator.Evaluate(i, new EvaluationContext { Options = Options });
                }
                catch (CodeBaseException error)
                {
                    failures.Add($"{text}: node {i} is called constant but needs a record: {error.Message}");
                }
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        read.Should().BeGreaterThan(100, "the population has to contain constants for this to mean anything");
    }

    // ---- 3. the is-constant predicate --------------------------------------

    [Fact]
    public void NothingThatReadsAFieldIsEverCalledConstant()
    {
        // The direction that matters for correctness: calling a subtree constant when it reads a
        // field would have the optimizer evaluate it with no record and get an answer that belongs to
        // no row. Checked over every node of every expression the reference compiled.
        List<string> failures = [];

        foreach ((string text, CompiledExpression compiled) in Every())
        {
            ExpressionEvaluator evaluator = new(compiled);

            for (int i = 0; i < compiled.Nodes.Count; i++)
            {
                bool reads = false;
                for (int at = i - compiled.Nodes[i].SubtreeSize + 1; at <= i; at++)
                    reads |= compiled.Nodes[at].Field is not null;

                if (reads && evaluator.IsConstant(i))
                    failures.Add($"{text}: node {i} reads a field and is called constant");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void ThePredicateIsNarrowerThanTheTruthAndDeliberatelySo()
    {
        // The reference's predicate answers from the row alone, so plenty of subtrees that plainly
        // cannot change are not called constant. Two are worth pinning because they are the ones the
        // optimizer will visibly not optimize: an expression over literals, and the narrowed float
        // literal that a comparison against a binary float field produces.
        IFieldMetadata fields = Fields();

        ExpressionEvaluator arithmetic = new(Compile("1+1", fields));
        arithmetic.IsConstant(2).Should().BeFalse("its row is the addition, and no row says constant");

        CompiledExpression compiled = Compile("K_FLT > 0", fields);
        compiled.Nodes[1].Row.Should().Be(FunctionRows.FloatConstant);
        new ExpressionEvaluator(compiled).IsConstant(1).Should()
            .BeFalse("e4isConstant has no case for the narrowed float literal, so this is predicted "
                + "never optimizable and narrowing it here would disagree with the reference");

        // And it is not evaluated wrongly, only classified narrowly: it still answers with no record.
        new ExpressionEvaluator(compiled).Evaluate(1, new EvaluationContext()).Number.Should().Be(0);
    }

    [Fact]
    public void ReadingADateOutOfTextIsConstantOnlyWhenItsOwnOperandIs()
    {
        // The one row the predicate does not answer from the row alone: it walks the subtree looking
        // for a field load or a calculation, so the same function is constant over a literal and not
        // over a field.
        IFieldMetadata fields = Fields();

        new ExpressionEvaluator(Compile("CTOD(\"01/15/00\")", fields)).IsConstant(1).Should().BeTrue();
        new ExpressionEvaluator(Compile("STOD(\"20000115\")", fields)).IsConstant(1).Should().BeTrue();
        new ExpressionEvaluator(Compile("CTOD(K_TEXT)", fields)).IsConstant(1).Should().BeFalse();
    }

    // ---- 4. a tag keeps its compiled expression ----------------------------

    [Fact]
    public void EveryTagKeepsItsCompiledExpressionAfterItsKindHasBeenTaken()
    {
        using CodeBaseEngine engine = Engine();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        foreach (Tag tag in table.Tags)
        {
            // Nothing has needed it yet, which is ADR-28's shape: one unreadable tag must not make a
            // table unopenable, so nothing is compiled until a tag is used.
            tag.Compiled.Should().BeNull($"{tag.Name} has not been used yet");
        }

        List<string> failures = [];

        foreach (Tag tag in table.Tags)
        {
            CompiledExpression compiled = table.CompiledFor(tag);

            if (tag.Compiled is null)
                failures.Add($"{tag.Name} discarded its compiled expression");
            else if (!ReferenceEquals(tag.Compiled, compiled))
                failures.Add($"{tag.Name} recompiled rather than kept what it had");
            else if (tag.Compiled.Source.Trim() != tag.Expression.Trim())
                failures.Add($"{tag.Name} kept '{tag.Compiled.Source}' for '{tag.Expression}'");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
        table.Tags.Should().HaveCount(22);
    }

    [Fact]
    public void ATagsCompiledExpressionIsTheSameOneTheKeysAreBuiltFrom()
    {
        // Not merely retained but the same instance the key path uses, so the optimizer compares
        // against what the index is actually keyed by rather than against a second reading of the
        // header text.
        using CodeBaseEngine engine = Engine();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        Tag tag = table.Tags[Index.RealTags.First().Name.TrimEnd()];

        CompiledExpression first = table.CompiledFor(tag);
        table.SelectTag(tag);
        table.Top().Should().Be(GoResult.Ok);

        table.CompiledFor(tag).Should().BeSameAs(first);
        tag.Compiled.Should().BeSameAs(first);
    }

    // ---- plumbing ----------------------------------------------------------

    private static ExpressionOptions Options => ExpressionOptions.CodeBase.WithCentury(Oracle.Century);

    private static CodeBaseEngine Engine() => new() { ExpressionOptions = Options };

    /// <summary>
    /// Compiles every expression the reference compiled, as a population to check properties over.
    /// </summary>
    private static IEnumerable<(string Text, CompiledExpression Compiled)> Every()
    {
        IFieldMetadata fields = Fields();

        foreach (DumpExpression compiled in Oracle.AcceptedExpressions)
            yield return (compiled.Expression, Compile(compiled.Expression, fields));
    }

    private static CompiledExpression Compile(string expression, IFieldMetadata fields) =>
        TypeResolver.Resolve(ExpressionParser.Parse(expression, fields));

    private static EvaluationContext Context(Table table) => new()
    {
        Record = table.CurrentRecord,
        NullFlags = table.NullFlags,
        RecordNumber = table.RecordNumber,
        Deleted = table.Deleted,
        RecordCount = table.RecordCount,
        Options = Options,
        Today = 2451559,
        Memo = field => table.GetMemoBytes(field),
    };

    private static IFieldMetadata Fields()
    {
        using CodeBaseEngine engine = Engine();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        return new TableFieldMetadata([.. table.Fields], "CDXEXPR");
    }
}
