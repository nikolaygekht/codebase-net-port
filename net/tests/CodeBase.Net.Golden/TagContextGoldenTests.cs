using AwesomeAssertions;
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
            TagContext context = TagContext.For(table.Tags[tagged.Tag].Inner.Header.Collation);
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

    [Fact]
    public void TheSectionHoldsBothRulesAndTheCasesBesideThem()
    {
        // A reader that found nothing would pass the gate above, so the size and the two tags are
        // pinned: one machine-order tag of CDXEXPR, one GENERAL tag of CDXCOLL.
        Oracle.TagContextExpressions.Should().HaveCount(7);
        Oracle.TagContextExpressions.Select(t => $"{t.Table}.{t.Tag}").Distinct()
            .Should().Equal("CDXEXPR.T_NUM", "CDXCOLL.C_GEN");
    }
}
