using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Overload resolution against the reference implementation's own compiled node arrays.
///
/// Layer four, and the gate that matters most in this step, because it is the one thing layers one to
/// three cannot tell us. A wrong overload row does not produce an error or an obviously wrong answer;
/// it produces a [i]plausible[/i] value through a neighbouring row and a key of the wrong shape. The
/// only way to know is to compare the row index the reference chose, which is what the dump records.
///
/// The comparison is a [b]sequence[/b] equality over the whole postfix array, not a set or a
/// per-node lookup. That is deliberate: a promotion implemented as a wrapper node rather than as a
/// row rewrite would lengthen the array, and only a sequence comparison notices.
///
/// The row carries the result type with it, because a row is one overload and its return type is the
/// table's, and the table has already been gated row for row. So there is nothing further to compare
/// per node; the whole expression's type is compared on its own, from the line the dump states it on.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class ExpressionResolutionGoldenTests
{
    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    private static readonly IFieldMetadata Fields = LoadFields();

    [Fact]
    public void EveryExpressionResolvesToTheRowsAndLengthsTheReferenceChose()
    {
        List<string> failures = [];

        foreach (DumpExpression compiled in Oracle.AcceptedExpressions)
        {
            try
            {
                CompiledExpression resolved = Compile(compiled.Expression);
                failures.AddRange(Compare(compiled, resolved));
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{compiled.Expression}: refused with {(int)error.Code}, expected to compile");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void EveryExpressionsResultTypeAndLengthAreTheOnesTheReferenceReports()
    {
        List<string> failures = [];

        foreach (DumpExpression compiled in Oracle.AcceptedExpressions)
        {
            CompiledExpression resolved = Compile(compiled.Expression);

            if ((char)resolved.ResultType != compiled.Type || resolved.Length != compiled.Length)
            {
                failures.Add($"{compiled.Expression}: '{(char)resolved.ResultType}'/{resolved.Length}, "
                    + $"expected '{compiled.Type}'/{compiled.Length}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void ATrimIsMarkedWhereverTheReferenceMarksOne()
    {
        // A property of the whole expression rather than of the node that trimmed, and it changes the
        // key rather than the value, so nothing on the value side would notice it being wrong.
        List<string> failures = [];

        foreach (DumpExpression compiled in Oracle.AcceptedExpressions)
        {
            if (Compile(compiled.Expression).HasTrim != compiled.HasTrim)
                failures.Add($"{compiled.Expression}: hasTrim should be {compiled.HasTrim}");
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Theory]
    [InlineData("UPPER(K_INT)")]
    [InlineData(".T. > .F.")]
    [InlineData(".T. >= .F.")]
    public void TheRefusalsOverloadResolutionOwnsCarryTheCodeTheReferenceGave(string expression)
    {
        // Each is recorded in the dump as -460, and each is invisible to the parser: the run of rows
        // the name resolved to simply has no member that accepts the operands' types.
        DumpRejection recorded = Oracle.Rejections.Single(rejection => rejection.Expression == expression);
        recorded.Code.Should().Be((int)ErrorCode.TypeMismatch);

        Action act = () => Compile(expression);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.TypeMismatch);
    }

    /// <summary>
    /// The rows no expression can reach, and why each group cannot be reached.
    /// </summary>
    /// <value>
    /// Declared rather than computed, because "the corpus happens not to cover it" and "nothing can
    /// cover it" are different claims and only the second one is safe to stop at. Three groups.
    ///
    /// Thirty-nine rows name a type outside what a table this library opens can hold — wide strings,
    /// the OLE-DB structs, the sized integers, the millisecond timestamp. Not one of those field
    /// types is admitted one layer below this engine, so no expression can produce one and code for
    /// one would be dead on arrival.
    ///
    /// Six rows are the currency comparisons, and they are dead in the reference itself: a currency
    /// operand promotes to a double before the run reaches its own exact-type row, so the row that
    /// looks like the right one is never selected however the comparison is written. EXPRESSIONS.md
    /// section 7.7 records it.
    ///
    /// Four rows belong to the report writer this build compiles out, plus the terminator that ends
    /// every scan.
    /// </value>
    private static readonly int[] UnreachableRows =
    [
        1, 2, 4, 13, 14, 15, 17, 20, 21, 22, 23, 39, 40, 58, 66, 74, 82, 89, 96, 108, 133,
        163, 164, 166, 167, 168, 169, 170, 171, 172, 174, 175, 184, 185, 186, 187, 188, 189, 190,
        56, 64, 72, 80, 87, 94,
        148, 149, 150, 192,
    ];

    [Fact]
    public void EveryRowOfTheTableIsEitherGatedByTheOracleOrDeclaredUnreachable()
    {
        // This is what makes the resolution gate a gate rather than a sample. A row nothing reaches
        // is a row a wrong overload could be selected into without any test noticing, so each one is
        // either exercised by an expression the reference compiled or named above with a reason.
        HashSet<int> gated = [.. Oracle.AcceptedExpressions
            .SelectMany(compiled => compiled.Nodes)
            .Select(node => node.Row)];

        IEnumerable<int> ungated = Enumerable.Range(0, FunctionTable.Rows.Count)
            .Where(row => !gated.Contains(row) && !UnreachableRows.Contains(row));

        ungated.Should().BeEmpty("every row must be reached by the oracle or declared unreachable");

        // And the other direction, so the declared list cannot rot into a list of excuses: a row the
        // oracle does reach must not also be claimed unreachable.
        UnreachableRows.Where(gated.Contains).Should().BeEmpty();

        gated.Should().HaveCount(FunctionTable.Rows.Count - UnreachableRows.Length);
    }

    [Fact]
    public void TheRowCoverageIsThreeCountsAndNotOne()
    {
        // The three the plan asks for, said out loud. Mixing them into one number would hide the
        // difference between a row the reference's dump proves we resolve correctly, a row nothing
        // can reach, and a row the reference has no equivalent of at all.
        HashSet<int> gated = [.. Oracle.AcceptedExpressions
            .SelectMany(compiled => compiled.Nodes)
            .Select(node => node.Row)];

        gated.Should().HaveCount(144, "rows the reference's own dump gates");
        UnreachableRows.Should().HaveCount(49, "rows nothing can reach, each with a stated reason");
        FunctionTable.VisualFoxProRows.Should().HaveCount(15,
            "rows Visual FoxPro adds, which the reference has none of and which its own oracle gates");

        (gated.Count + UnreachableRows.Length).Should().Be(FunctionTable.Rows.Count);
        (FunctionTable.Rows.Count + FunctionTable.VisualFoxProRows.Count).Should().Be(FunctionTable.Count);
    }

    [Fact]
    public void EveryRowVisualFoxProAddsIsReachedByItsOwnOracle()
    {
        // The third count's own gate. These have no reference row, so the transcription gate cannot
        // see them and the resolution gate has nothing to compare; what makes them right is Visual
        // FoxPro's answers, and what makes that meaningful is that every row is actually exercised.
        IFieldMetadata fields = new TableFieldMetadata([], "VFPFVAL");

        string[] reaching =
        [
            "ABS(-1)", "INT(1.5)", "ROUND(1.5,0)", "MOD(5,3)", "MIN(1,2)", "MIN(\"A\",\"B\")",
            "MAX(1,2)", "MAX(\"A\",\"B\")", "SQRT(4)", "SIGN(-3)", "LEN(\"AB\")",
            "AT(\"B\",\"AB\")", "ASC(\"A\")", "LOWER(\"AB\")", "REPLICATE(\"A\",3)",
        ];

        HashSet<int> reached = [];

        foreach (string expression in reaching)
        {
            CompiledExpression compiled = TypeResolver.Resolve(ExpressionParser.Parse(expression, fields));

            foreach (ExpressionNode node in compiled.Nodes)
            {
                if (FunctionTable.IsVisualFoxPro(node.Row))
                    reached.Add(node.Row);
            }
        }

        Enumerable.Range(FunctionTable.FirstVisualFoxProRow, FunctionTable.VisualFoxProRows.Count)
            .Where(row => !reached.Contains(row))
            .Should().BeEmpty("every row Visual FoxPro adds must be exercised by some expression");
    }

    /// <summary>
    /// Reports every way one compiled expression differs from what the reference compiled.
    /// </summary>
    private static IEnumerable<string> Compare(DumpExpression compiled, CompiledExpression resolved)
    {
        if (resolved.Nodes.Count != compiled.Nodes.Count)
        {
            yield return $"{compiled.Expression}: {resolved.Nodes.Count} nodes, expected {compiled.Nodes.Count}";
            yield break;
        }

        for (int i = 0; i < compiled.Nodes.Count; i++)
        {
            DumpNode expected = compiled.Nodes[i];
            ExpressionNode actual = resolved.Nodes[i];

            if (actual.Row != expected.Row || actual.Length != expected.Length
                || actual.Immediate2 != expected.Immediate2)
            {
                yield return $"{compiled.Expression}: node {i} is "
                    + $"row {actual.Row} len {actual.Length} i2 {actual.Immediate2}, expected "
                    + $"row {expected.Row} len {expected.Length} i2 {expected.Immediate2}";
            }
        }
    }

    private static CompiledExpression Compile(string expression) =>
        TypeResolver.Resolve(ExpressionParser.Parse(expression, Fields));

    private static IFieldMetadata LoadFields()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        return new TableFieldMetadata([.. table.Fields], "CDXEXPR");
    }
}
