using AwesomeAssertions;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Depth 2 of the query gate, and what the decomposed constraints mean.
///
/// Depth 2 on its own is one bit per filter: whether the reference considered the constraint tree to
/// answer the whole of it. That is worth asserting -- it is the fall-back boundary, and getting it
/// wrong means either scanning when a seek would have done or trusting a tree that does not cover
/// the question -- but it cannot see which way round a bound went. The record sets that can see that
/// arrive two sub-steps later; until then the checks below say what each constraint [i]means[/i], in
/// both operand orders, which is the part a swapped bound fails immediately.
///
/// Note that depth 2 is asserted unconditionally, unlike the record sets. The reference's
/// range-inverting defect changes which slot a constant lands in, not which terms were optimized, so
/// it agrees about being fully mapped even on the one filter where its records are wrong.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class DecompositionGoldenTests
{
    public static TheoryData<string> Tables => ["CDXEXPR", "CDXBASE", "CDXDEEP"];

    /// <summary>
    /// The gate.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void FullyMappedAgreesWithTheReferenceOnEveryFilter(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases)
        {
            harness.Plan(filter.Filter).FullyMapped
                .Should().Be(filter.FullyMapped, "fullyMapped for {0}", filter.Filter);
        }
    }

    /// <summary>
    /// A filter the reference built no bitset for must decompose to no tree, and one it did must
    /// decompose to a tree. These are recorded independently of fullyMapped, so it is a second check
    /// and not the same one twice.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void ATreeIsBuiltForExactlyTheFiltersTheReferenceOptimized(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases)
        {
            (harness.Plan(filter.Filter).Root is not null)
                .Should().Be(filter.Optimized_Anything, "a tree for {0}", filter.Filter);
        }
    }

    /// <summary>
    /// Every leaf is attributed to a term of the filter, and every attributed term is one the plan
    /// then vouches for. That pairing is what the residual filter reads, so a leaf whose source node
    /// went astray would silently make a term be checked twice or not at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryLeafIsAttributedToATermThePlanThenAnswers(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);

        foreach (DumpQueryCase filter in harness.Dump.Cases)
        {
            QueryPlan plan = harness.Plan(filter.Filter);
            if (plan.Root is null)
                continue;

            foreach (ConstraintNode leaf in plan.Root.Walk().Where(node => node.IsLeaf))
            {
                leaf.SourceNodes.Should().NotBeEmpty();

                foreach (int term in leaf.SourceNodes)
                {
                    term.Should().BeInRange(0, filter.Nodes.Count - 1);
                    plan.Answers(term).Should().BeTrue();
                }
            }
        }
    }

    /// <summary>
    /// The six operators, each written both ways round, must constrain the same tag the same way.
    /// This is where a swapped reflection dies: the reference itself gets this wrong when the
    /// comparison is nested, and the port implements what was meant instead.
    /// </summary>
    [Theory]
    [InlineData("K_NUM > 0", "0 < K_NUM")]
    [InlineData("K_NUM >= 0", "0 <= K_NUM")]
    [InlineData("K_NUM < 0", "0 > K_NUM")]
    [InlineData("K_NUM <= 0", "0 >= K_NUM")]
    [InlineData("K_NUM = 0", "0 = K_NUM")]
    [InlineData("K_NUM <> 0", "0 <> K_NUM")]
    public void WritingAComparisonBothWaysRoundConstrainsTheTagIdentically(string tagFirst, string constantFirst)
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        LeafConstraint direct = harness.OnlyLeaf(tagFirst).Constraint!;
        LeafConstraint reflected = harness.OnlyLeaf(constantFirst).Constraint!;

        reflected.ToString().Should().Be(direct.ToString());
    }

    /// <summary>
    /// And what those constraints mean, stated as the records they admit rather than as the fields
    /// they set. A constraint that bounded the wrong way would still be a well-formed constraint.
    /// </summary>
    [Theory]
    [InlineData("K_NUM > 2.5", 3.0, true)]
    [InlineData("K_NUM > 2.5", 2.5, false)]
    [InlineData("K_NUM >= 2.5", 2.5, true)]
    [InlineData("K_NUM < 2.5", 2.0, true)]
    [InlineData("K_NUM < 2.5", 2.5, false)]
    [InlineData("K_NUM <= 2.5", 2.5, true)]
    [InlineData("K_NUM = 2.5", 2.5, true)]
    [InlineData("K_NUM = 2.5", 2.6, false)]
    [InlineData("K_NUM <> 2.5", 2.5, false)]
    [InlineData("K_NUM <> 2.5", 2.6, true)]
    [InlineData("2.5 < K_NUM", 3.0, true)]
    [InlineData("2.5 < K_NUM", 2.5, false)]
    [InlineData("2.5 > K_NUM", 2.0, true)]
    [InlineData("2.5 > K_NUM", 3.0, false)]
    [InlineData("2.5 <= K_NUM", 2.5, true)]
    [InlineData("2.5 >= K_NUM", 2.5, true)]
    public void AConstraintAdmitsExactlyWhatItsTermDoes(string filter, double value, bool admitted)
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.OnlyLeaf(filter).Constraint!
            .Accepts(QueryHarness.NumericValue(value))
            .Should().Be(admitted, "{0} at {1}", filter, value);
    }

    /// <summary>
    /// The mistake worth naming: 3.0 must be inside "greater than 2.5" and outside "less than 2.5".
    /// A reflection swapped the wrong way passes every structural check and fails this one.
    /// </summary>
    [Fact]
    public void AReflectedComparisonBoundsTheTagTheWayItReads()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        // "2.5 > K_NUM" reads as K_NUM below 2.5.
        harness.OnlyLeaf("2.5 > K_NUM").Constraint!.Upper.Should().NotBeNull();
        harness.OnlyLeaf("2.5 > K_NUM").Constraint!.Lower.Should().BeNull();

        // "2.5 < K_NUM" reads as K_NUM above 2.5.
        harness.OnlyLeaf("2.5 < K_NUM").Constraint!.Lower.Should().NotBeNull();
        harness.OnlyLeaf("2.5 < K_NUM").Constraint!.Upper.Should().BeNull();
    }

    /// <summary>
    /// A short character constant widens an exclusive lower bound, and only a lower one, because only
    /// a lower bound can step past the group of keys that begin with it. The widening is exact: the
    /// two forms admit the same values.
    /// </summary>
    [Fact]
    public void AShortCharacterConstantWidensOnlyALowerBound()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.OnlyLeaf("UPPER(K_TEXT) > \"S\"").Constraint!.Lower!.Value.Inclusive.Should().BeTrue();
        harness.OnlyLeaf("\"S\" < UPPER(K_TEXT)").Constraint!.Lower!.Value.Inclusive.Should().BeTrue();
        harness.OnlyLeaf("UPPER(K_TEXT) < \"S\"").Constraint!.Upper!.Value.Inclusive.Should().BeFalse();
        harness.OnlyLeaf("\"S\" > UPPER(K_TEXT)").Constraint!.Upper!.Value.Inclusive.Should().BeFalse();
    }

    /// <summary>
    /// A constant as long as the key is not a prefix of anything, so nothing is widened. Twenty
    /// characters against a twenty-byte key is the boundary the rule turns on.
    /// </summary>
    [Fact]
    public void AFullWidthCharacterConstantIsNotWidened()
    {
        using QueryHarness harness = QueryHarness.For("CDXEXPR");

        harness.OnlyLeaf("UPPER(K_TEXT) > \"ABCDEFGHIJKLMNOPQRST\"")
            .Constraint!.Lower!.Value.Inclusive.Should().BeFalse();
    }

    /// <summary>
    /// Every constant a filter can hold is exactly as wide as its node declares it to be.
    ///
    /// This pins a rule that is currently invisible. A constraint takes the [b]declared[/b] length of
    /// the constant's node rather than the width of the value once evaluated, because that is what
    /// decides whether a character constant is a prefix -- and the reference copies exactly that many
    /// bytes out of the result. Across all 93 constants the corpus reaches, the two numbers are
    /// always the same, so taking the wrong one changes nothing today.
    ///
    /// The reason they agree is that only six kinds of node are constants at all: a number, a string,
    /// the four logical words, and the two functions that read a date out of text. Each is compiled
    /// at its own natural width, so none of them truncates. Should that ever stop being true, this
    /// goes red and the distinction stops being theoretical -- which is the whole point of asserting
    /// something that cannot currently fail.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void AConstantIsAsWideAsItsNodeSaysItIs(string table)
    {
        using QueryHarness harness = QueryHarness.For(table);
        List<string> divergent = [];
        int seen = 0;

        foreach (DumpQueryCase filter in harness.Dump.Cases)
        {
            CompiledExpression compiled = harness.Compile(filter.Filter);
            ExpressionEvaluator evaluator = new(compiled);

            for (int node = 0; node < compiled.Nodes.Count; node++)
            {
                if (!evaluator.IsConstant(node))
                    continue;

                seen++;
                int declared = compiled.Nodes[node].Length;
                int evaluated = evaluator.Evaluate(node, new EvaluationContext()).ToRaw().Length;

                if (declared != evaluated)
                    divergent.Add($"{filter.Filter} node {node}: declared {declared}, evaluated {evaluated}");
            }
        }

        divergent.Should().BeEmpty();
        seen.Should().BeGreaterThan(0, "the check needs constants to have looked at");
    }
}
