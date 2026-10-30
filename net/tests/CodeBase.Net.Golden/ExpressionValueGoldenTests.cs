using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// What the kernels compute, against the values the reference implementation produced.
///
/// Layer four, and it is the confirming half rather than the deciding one: the semantics are asserted
/// in C# against the specification at layer one, because a table of expression and expected value
/// reads as a specification while a transcript of the reference's answers does not. What this adds is
/// the three things layer one cannot supply — the answers the source does not state, which row
/// actually ran, and the filters, which have no observable output of their own.
///
/// The comparison is on raw result bytes, which is what the reference hands back: a string is its
/// bytes, a number is a little-endian double, a logical is a four-byte int. Comparing a rendered
/// number would hide the low bits, and those are what the key transforms read.
///
/// Two families are deliberately outstanding, and the rule that names them is computed from the rows
/// rather than written as a list of expressions, so that finishing a family empties it by itself.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class ExpressionValueGoldenTests
{
    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    /// <summary>
    /// The rows whose kernels later sub-steps own, and why they are not here.
    /// </summary>
    /// <value>
    /// Formatting a number and formatting a date or timestamp by a picture are the text-formatting
    /// family, which is its own sub-step because it is where the source yields no answer and the
    /// corpus has to lead. Ascending and descending conversion turns a value into key bytes, which
    /// is the other axis and is driven from these recorded values rather than from what the
    /// evaluator produces, so it lands with the key rules.
    /// </value>
    private static bool Outstanding(DumpExpression compiled) => compiled.Nodes.Any(node =>
        node.Row is >= FunctionRows.Descend and <= FunctionRows.LastAscend);

    [Fact]
    public void EveryConstantExpressionEvaluatesToTheBytesTheReferenceProduced()
    {
        List<string> failures = [];

        foreach (DumpExpression compiled in Oracle.ConstantExpressions.Where(e => !Outstanding(e)))
        {
            try
            {
                ExpressionValue value = Evaluate(compiled.Expression, Context());

                if (!value.ToRaw().AsSpan().SequenceEqual(compiled.Value))
                    failures.Add($"{compiled.Expression}: {Show(value.ToRaw())}, expected {Show(compiled.Value!)}");
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{compiled.Expression}: {(int)error.Code} {error.Message}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void EveryFieldExpressionEvaluatesToTheBytesTheReferenceProducedAtEveryRecord()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        IFieldMetadata fields = new TableFieldMetadata([.. table.Fields], "CDXEXPR");
        List<string> failures = [];

        foreach (DumpExpression compiled in Oracle.FieldExpressions.Where(e => !Outstanding(e)))
        {
            CompiledExpression expression;
            try
            {
                expression = TypeResolver.Resolve(ExpressionParser.Parse(compiled.Expression, fields));
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{compiled.Expression}: refused with {(int)error.Code}");
                continue;
            }

            ExpressionEvaluator evaluator = new(expression);

            foreach (int number in compiled.RecordValues.Keys)
            {
                byte[] expected = compiled.FreshValueAt(number);
                table.Go(number);
                EvaluationContext context = Context(table);

                try
                {
                    ExpressionValue value = evaluator.Evaluate(context);
                    if (!value.ToRaw().AsSpan().SequenceEqual(expected))
                    {
                        failures.Add($"{compiled.Expression} at {number}: "
                            + $"{Show(value.ToRaw())}, expected {Show(expected)}");
                    }
                }
                catch (CodeBaseException error)
                {
                    failures.Add($"{compiled.Expression} at {number}: {(int)error.Code} {error.Message}");
                }
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void TheReferenceCarriesANullForwardOnlyWhereAComputedNullIsRead()
    {
        // The reference never clears a computed node's null flag, so one compiled expression run
        // record after record answers differently from the same text parsed afresh — but only where
        // a parent reads a computed child's null, which takes a nullable field under two operators.
        // Every other expression in the dump agrees with itself, which is what makes the fresh
        // reading a correction and not a second oracle (ADR-61).
        Oracle.FieldExpressions.Where(e => e.FreshValues.Count > 0)
            .Select(e => e.Expression)
            .Should().Equal(".NOT.(K_NNUM = 0)", "K_NNUM + 1 > 0");
    }

    [Fact]
    public void TheClockDependentExpressionsHaveNoRecordedValueAndAreNotCompared()
    {
        // Recorded for shape only, on purpose: a value read off the clock would make the corpus stop
        // regenerating identically, which is the project's oldest standing invariant.
        Oracle.ClockDependentExpressions.Should().OnlyContain(compiled => compiled.Value == null);
    }

    [Fact]
    public void TheOutstandingFamiliesAreNamedByRowAndStillPresentInTheOracle()
    {
        // Keeps the deferral honest in both directions: something is being put off, and the oracle
        // does hold the cases that will settle it.
        Oracle.ConstantExpressions.Concat(Oracle.FieldExpressions)
            .Where(Outstanding)
            .Should().NotBeEmpty();
    }

    [Fact]
    public void TheDumpSaysWhichPictureAndCenturyItWasTakenWith()
    {
        // Both are settings the reference takes implicitly and this port takes explicitly, so a gate
        // that assumed them would be asserting against whatever machine last ran the generator.
        Oracle.DatePicture.Should().Be("MM/DD/YY");
        Oracle.Century.Should().Be(20);
    }

    private static ExpressionValue Evaluate(string expression, EvaluationContext context)
    {
        CompiledExpression compiled = TypeResolver.Resolve(
            ExpressionParser.Parse(expression, EmptyFields.Instance));

        return new ExpressionEvaluator(compiled).Evaluate(context);
    }

    /// <summary>
    /// Builds the settings the dump was taken under, so nothing is assumed about the host.
    /// </summary>
    private static EvaluationContext Context(Table? table = null) => new()
    {
        Record = table?.CurrentRecord,
        NullFlags = table?.NullFlags,
        RecordNumber = table?.RecordNumber ?? 0,
        Deleted = table?.Deleted ?? false,
        RecordCount = table?.RecordCount ?? 0,
        Options = ExpressionOptions.CodeBase.WithCentury(Oracle.Century),
        Memo = table is null ? null : field => table.GetMemoBytes(field),
    };

    private static string Show(byte[] bytes) => Convert.ToHexString(bytes);

    /// <summary>
    /// Answers no field, for the expressions that name none.
    /// </summary>
    private sealed class EmptyFields : IFieldMetadata
    {
        public static EmptyFields Instance { get; } = new();

        public FieldReference? Find(string? alias, string name) => null;
    }
}
