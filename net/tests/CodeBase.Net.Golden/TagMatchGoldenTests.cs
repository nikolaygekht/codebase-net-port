using AwesomeAssertions;
using CodeBase.Net.Expressions;
using CodeBase.Net.Query;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Depth 1 of the query gate: for every filter, every node and every tag, this port matches a tag to
/// a subexpression exactly where the reference implementation did.
///
/// The oracle for it is the parallel array the reference builds beside a compiled filter, one entry
/// per node, holding the tag whose key expression could replace the subexpression rooted there. It
/// is a strong gate for a reason that is easy to miss: a null entry means two different things. Most
/// nodes are never candidates at all, because only an operand of a comparison whose other operand is
/// a constant is ever tested -- so reproducing the column means reproducing which nodes the walk
/// reaches, not merely which subexpressions some tag happens to equal.
///
/// The pair that makes that concrete is [c]K_NUM &gt; 0 .OR. K_LOG[/c] and [c]K_LOG .OR. K_NUM &gt; 0[/c].
/// They select the same fourteen records and neither is optimized at all, but their arrays differ:
/// an or's operands are visited last to first, and the whole branch is abandoned the moment one of
/// them cannot be optimized, so in one filter the comparison is tested against the tags and in the
/// other it is never reached. Nothing but the walk order gets both right.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class TagMatchGoldenTests
{
    public static TheoryData<string> Tables => ["CDXEXPR", "CDXBASE", "CDXDEEP"];

    /// <summary>
    /// The gate. Every node of every filter, against the reference's own answer.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryNodeMatchesTheTagTheReferenceMatched(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases)
        {
            QueryTag?[] ours = harness.Match(filter.Filter);
            string[] mine = [.. ours.Select(t => t?.Name ?? "-")];
            string[] theirs = [.. filter.Nodes.Select(n => n.Tag ?? "-")];

            mine.Should().Equal(theirs, "the tags matched for {0}", filter.Filter);
        }
    }

    /// <summary>
    /// The oracle has to contain a case where a tag was matched, or the gate above would pass on a
    /// port that matches nothing at all. It does, and this says so out loud.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void TheGateHasSomethingToProve(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        harness.Dump.Cases
            .SelectMany(c => c.Nodes)
            .Where(n => n.Tag is not null)
            .Should().NotBeEmpty();
    }

    /// <summary>
    /// Two filters that select the same records, and whose tag arrays differ because an or abandons
    /// its whole branch at the first operand it cannot use -- and visits operands backwards, so which
    /// operand that is depends on the order they were written in.
    /// </summary>
    [Fact]
    public void AnOrAbandonsItsBranchBeforeReachingAnOperandWrittenFirst()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        DumpQueryCase written = harness.Dump.Cases.Single(c => c.Filter == "K_NUM > 0 .OR. K_LOG");
        DumpQueryCase mirrored = harness.Dump.Cases.Single(c => c.Filter == "K_LOG .OR. K_NUM > 0");

        // The same records, and neither is optimized.
        written.Optimized.Should().Equal(mirrored.Optimized);
        written.Optimized_Anything.Should().BeFalse();
        mirrored.Optimized_Anything.Should().BeFalse();

        // The comparison is written first, so it is visited last, so the failing operand kills the
        // branch before it is ever tested.
        harness.Match(written.Filter).Should().AllSatisfy(t => t.Should().BeNull());

        // Written second, it is visited first and matched -- on a branch then thrown away.
        harness.Match(mirrored.Filter).Should().Contain(t => t != null && t.Value.Name == "T_NUM");
    }

    /// <summary>
    /// A tag the optimizer refuses does not end the search. CDXBASE carries a plain tag and a unique
    /// tag over one field, and the plain one answers.
    /// </summary>
    [Fact]
    public void ARefusedTagDoesNotStopTheSearch()
    {
        using QueryHarness harness = QueryHarness.For("CDXBASE");
        QueryTag?[] matched = harness.Match("K_DUP = \"CHERRY\"");

        matched.Should().Contain(t => t != null && t.Value.Name == "T_DUP");
        matched.Should().NotContain(t => t != null && t.Value.Name == "T_UNIQ");
    }

    /// <summary>
    /// The type gate is a blacklist of four and not a shortlist of three, so a date tag and a logical
    /// tag are both usable -- which the reference's own comment beside the code would deny.
    /// </summary>
    [Fact]
    public void DateAndLogicalTagsAreEligible()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.Match("K_DATE = STOD(\"20200101\")")
            .Should().Contain(t => t != null && t.Value.Name == "T_DATE");
        harness.Match("EMPTY(K_TEXT) = .T.")
            .Should().Contain(t => t != null && t.Value.Name == "T_EMPTY");
    }

    /// <summary>
    /// Widening the scope adds the tags the reference refuses by type and by order, and takes nothing
    /// away: every tag matched under the default is still matched.
    /// </summary>
    [Fact]
    public void WideningTheScopeOnlyEverAddsTags()
    {
        using QueryHarness compatible = QueryHarness.For("CDXEXPR");
        using QueryHarness wide = QueryHarness.For("CDXEXPR", QueryTagScope.AllSeekable);

        foreach (DumpQueryCase filter in compatible.Dump.Cases)
        {
            QueryTag?[] narrow = compatible.Match(filter.Filter);
            QueryTag?[] broad = wide.Match(filter.Filter);

            for (int node = 0; node < narrow.Length; node++)
            {
                if (narrow[node] is QueryTag matched)
                    broad[node]?.Name.Should().Be(matched.Name, "widening must not change a match");
            }
        }

        // And it does add: an integer tag is refused by default and usable when widened.
        compatible.Match("K_INT > 0").Should().AllSatisfy(t => t.Should().BeNull());
        wide.Match("K_INT > 0").Should().Contain(t => t != null && t.Value.Name == "T_INT");
    }
}
