using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The parse pass against the reference implementation's own compiled node arrays.
///
/// Layer four, and it can gate a pass whose output is only half typed because of what overload
/// resolution does not do: it rewrites a node's row and its length, and it never adds a node. So the
/// node count, each node's operand count, each node's subtree size and each field reference are
/// already final the moment the parser returns, and the reference's dump records all four.
///
/// That is a much stronger check than parse shape written out by hand. The parameter folding is where
/// it bites hardest: nine functions read a constant parameter at parse time and remove its node
/// altogether, so a port that dutifully kept the node would show two extra on every formatted number
/// and could not be told from a correct one by looking at values.
///
/// The refusals are split the same way the scanner's are, and for the same reason: refusing at the
/// wrong pass reports the wrong cause, and refusing too early can refuse text the reference accepts.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class ExpressionParserGoldenTests
{
    private static readonly CorpusExpressionDump Oracle = CorpusExpressionDump.Load();

    private static readonly IFieldMetadata Fields = LoadFields();

    /// <summary>
    /// The refusals the parse pass owns, and nothing else.
    /// </summary>
    /// <value>
    /// Declared rather than discovered, so that a refusal arriving from the wrong pass fails instead
    /// of passing. Two are unknown names, which the parser must resolve because a row is chosen from
    /// a field's type and from a function's name. Two are parameters that had to be written-out
    /// numbers, which the parser must read because they decide how long the result is. One is a
    /// parameter count, which the reference checks against the row before any type exists.
    /// </value>
    private static readonly string[] ParserRefusals =
    [
        "TOOLONGFN(\"X\")",
        "STR(K_NUM,K_INT)",
        "LEFT(\"ABC\",K_INT)",
        "SPACE(K_INT)",
        "UPPER(K_TEXT, 2)",
        "1.AND.X",
        "OTHER->K_TEXT",
    ];

    /// <summary>
    /// The refusals that belong to overload resolution, which has to be able to reach them.
    /// </summary>
    private static readonly string[] ResolutionRefusals =
    [
        "UPPER(K_INT)",
        ".T. > .F.",
        ".T. >= .F.",
    ];

    [Fact]
    public void EveryExpressionTheReferenceCompiledParsesToTheSameShape()
    {
        List<string> failures = [];

        foreach (DumpExpression compiled in Oracle.AcceptedExpressions)
        {
            try
            {
                ParsedExpression parsed = ExpressionParser.Parse(compiled.Expression, Fields);
                failures.AddRange(Compare(compiled, parsed));
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{compiled.Expression}: refused with {(int)error.Code}, expected to parse");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void TheRefusalsTheParserOwnsCarryTheCodeTheReferenceGave()
    {
        List<string> failures = [];

        foreach (DumpRejection rejection in Oracle.Rejections.Where(r => ParserRefusals.Contains(r.Expression)))
        {
            try
            {
                ExpressionParser.Parse(rejection.Expression, Fields);
                failures.Add($"{rejection.Expression}: parsed, expected {rejection.Code}");
            }
            catch (CodeBaseException error)
            {
                if ((int)error.Code != rejection.Code)
                    failures.Add($"{rejection.Expression}: {(int)error.Code}, expected {rejection.Code}");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void TheRefusalsOverloadResolutionOwnsStillParse()
    {
        // A type no overload accepts is invisible to a parser: it has chosen the first row of the
        // name's run and knows nothing about what the operands turned out to be. Refusing here would
        // take the answer away from the pass that can give the right one.
        List<string> failures = [];

        foreach (string expression in ResolutionRefusals)
        {
            try
            {
                ExpressionParser.Parse(expression, Fields);
            }
            catch (CodeBaseException error)
            {
                failures.Add($"{expression}: refused with {(int)error.Code}, expected to parse");
            }
        }

        string.Join(Environment.NewLine, failures).Should().BeEmpty();
    }

    [Fact]
    public void TheOneRestrictionTheReferenceDocumentsAndDoesNotEnforceIsNotEnforcedHere()
    {
        // The reference's own comment says a trim result may not feed a substring, and the generator
        // recorded that it compiles the expression anyway. Enforcing the documented rule would refuse
        // text the reference accepts, which is the one thing the front end may not do.
        DumpRejection accepted = Oracle.Rejections.Single(rejection => rejection.Accepted);

        accepted.Expression.Should().Be("SUBSTR(TRIM(K_TEXT),1,2)");
        ExpressionParser.Parse(accepted.Expression, Fields).Nodes.Should().HaveCount(3);
    }

    [Fact]
    public void EveryRefusalTheParserOwnsIsOneTheDumpRecords()
    {
        // Keeps the declared lists honest: a case renamed or dropped from the generator would leave a
        // name here matching nothing, and the two lists would quietly stop meaning the same thing.
        IEnumerable<string> recorded = Oracle.Rejections.Select(rejection => rejection.Expression);

        recorded.Should().Contain(ParserRefusals).And.Contain(ResolutionRefusals);
    }

    [Fact]
    public void TheDumpStatesTheSameNodeCountItThenLists()
    {
        // The reader takes the count off one line and the nodes off the following ones, so this is
        // what stops a reader bug from making every comparison above trivially true.
        foreach (DumpExpression compiled in Oracle.AcceptedExpressions)
            compiled.Nodes.Should().HaveCount(compiled.NodeCount, compiled.Expression);
    }

    /// <summary>
    /// Reports every way one parsed expression differs from the shape the reference compiled.
    /// </summary>
    /// <value>
    /// The whole node array is compared rather than stopping at the first difference, because a
    /// folding that went wrong shifts every node after it and one report per expression says which
    /// function did it.
    /// </value>
    private static IEnumerable<string> Compare(DumpExpression compiled, ParsedExpression parsed)
    {
        if (parsed.Nodes.Count != compiled.Nodes.Count)
        {
            yield return
                $"{compiled.Expression}: {parsed.Nodes.Count} nodes, expected {compiled.Nodes.Count}";
            yield break;
        }

        for (int i = 0; i < compiled.Nodes.Count; i++)
        {
            DumpNode expected = compiled.Nodes[i];
            ExpressionNode actual = parsed.Nodes[i];

            if (actual.ParameterCount != expected.ParameterCount
                || actual.SubtreeSize != expected.SubtreeSize
                || actual.FieldNumber != expected.FieldNumber)
            {
                yield return $"{compiled.Expression}: node {i} is "
                    + $"({actual.ParameterCount}, {actual.SubtreeSize}, {actual.FieldNumber}), expected "
                    + $"({expected.ParameterCount}, {expected.SubtreeSize}, {expected.FieldNumber})";
            }
        }
    }

    /// <summary>
    /// Reads the fields of the table the dump was taken over.
    /// </summary>
    /// <value>
    /// The fields outlive the table they came from, so the table is opened once and closed, and the
    /// parser is handed the field list rather than anything that holds a file open.
    /// </value>
    private static IFieldMetadata LoadFields()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));

        return new TableFieldMetadata([.. table.Fields], "CDXEXPR");
    }
}
