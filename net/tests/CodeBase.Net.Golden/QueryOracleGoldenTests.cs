using AwesomeAssertions;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The query oracle is readable, self-consistent, and says what step 011 was built to ask it.
///
/// This is sub-step 1's whole gate and it asserts nothing about the port, because no line of the
/// optimizer exists yet. What it does is make the oracle trustworthy before anything is measured
/// against it: that the dump parses into typed cases, that the reader agrees with the generator's
/// own verdicts rather than merely producing some answer, and that the reference's decisions are
/// internally consistent -- a filter with no bitset must not claim to be fully mapped, a matched tag
/// must be a tag the table actually has.
///
/// It also pins the four findings the run produced, because each of them was a prediction in
/// [c]QUERY-OPTIMIZER.md[/c] that nothing had ever checked, and a prediction that is confirmed once
/// and then left unpinned is a prediction that quietly stops being true.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class QueryOracleGoldenTests
{
    private static readonly CorpusQueryDump Expr = CorpusQueryDump.Load("CDXEXPR");
    private static readonly CorpusQueryDump Base = CorpusQueryDump.Load("CDXBASE");
    private static readonly CorpusQueryDump Deep = CorpusQueryDump.Load("CDXDEEP");

    public static TheoryData<string> Tables => ["CDXEXPR", "CDXBASE", "CDXDEEP", "QRYGATE", "TAGCTX", "TAGGEN"];

    private static CorpusQueryDump Dump(string table) => table switch
    {
        "CDXEXPR" => Expr,
        "CDXBASE" => Base,
        "CDXDEEP" => Deep,
        _ => CorpusQueryDump.Load(table),
    };

    [Theory]
    [MemberData(nameof(Tables))]
    public void EveryDumpParsesIntoTheNumberOfCasesItDeclares(string table)
    {
        CorpusQueryDump dump = Dump(table);

        dump.Table.Should().Be(table + ".DBF");
        dump.RecordCount.Should().BeGreaterThan(0);
        dump.Cases.Should().NotBeEmpty();
        dump.Cases.Should().OnlyContain(c => c.Filter.Length > 0);
    }

    /// <summary>
    /// The reader recomputes each verdict from the two record lists; the generator wrote its own.
    /// They must agree, and that is what says the record lists were parsed rather than merely read.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void TheReaderReachesTheSameVerdictTheGeneratorRecorded(string table)
    {
        foreach (DumpQueryCase c in Dump(table).Cases)
            c.Agrees.Should().Be(c.DeclaredAgrees, "the dump's own verdict on {0}", c.Filter);
    }

    /// <summary>
    /// Open question 1 of the design, answered by the dump rather than by reasoning: the candidate
    /// bitset is not disturbed by walking the result, so reading it once is reading it correctly.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void TheCandidateBitsetSurvivesTheWalk(string table)
    {
        foreach (DumpQueryCase c in Dump(table).Cases)
            c.SetStable.Should().BeTrue("the bitset for {0} must not change as it is consumed", c.Filter);
    }

    /// <summary>
    /// A filter the optimizer could do nothing with cannot claim the bitmap answers it. The two are
    /// recorded independently, so this is a real consistency check on the oracle and not a tautology.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void NothingIsFullyMappedWithoutABitset(string table)
    {
        foreach (DumpQueryCase c in Dump(table).Cases.Where(c => !c.Optimized_Anything))
            c.FullyMapped.Should().BeFalse("{0} built no bitset", c.Filter);
    }

    /// <summary>
    /// Having no bitmap and having given up on bitmapping are different states, and the corpus shows
    /// they are: ten filters here optimize nothing at all and not one of them sets bitmapsFreed.
    ///
    /// Worth pinning, because the flag reads like "there is no bitmap" and is not. It means
    /// bitmapping FAILED -- the memory pool, the record-count guard, or the test-only kill switch --
    /// and a filter simply holding nothing optimizable leaves it clear. Correctness on that path is
    /// carried by the per-term tag test instead, which is the rule the port makes explicit.
    /// </summary>
    [Fact]
    public void GivingUpOnBitmappingIsNotTheSameAsHavingNoBitmap()
    {
        DumpQueryCase[] all = [.. Expr.Cases, .. Base.Cases, .. Deep.Cases];

        all.Where(c => !c.Optimized_Anything).Should().NotBeEmpty();
        all.Should().OnlyContain(c => !c.BitmapsFreed);
    }

    /// <summary>
    /// A bitset that is fully mapped needs no residual filtering, so the records it selects are
    /// exactly the records that came back. Where it is not fully mapped the set is a superset.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void TheBitsetSelectsTheRecordsThatCameBack(string table)
    {
        CorpusQueryDump dump = Dump(table);

        foreach (DumpQueryCase c in dump.Cases.Where(c => c.Optimized_Anything))
        {
            IReadOnlyList<int> selected = c.Set!.Effective(dump.RecordCount);

            if (c.FullyMapped)
                selected.Should().Equal(c.Optimized, "{0} is fully mapped", c.Filter);
            else
                c.Optimized.Should().BeSubsetOf(selected, "{0} is a candidate set", c.Filter);
        }
    }

    /// <summary>
    /// Every tag the optimizer matched is a tag the table has, and the tag-ordered run returns the
    /// same records as the record-ordered one -- a different order of the same set, never a
    /// different set.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void SelectingATagChangesTheOrderAndNotTheSet(string table)
    {
        foreach (DumpQueryCase c in Dump(table).Cases.Where(c => c.OrderingTag is not null))
            c.ByTag.Should().BeEquivalentTo(c.Optimized, "{0} in tag order", c.Filter);
    }

    /// <summary>
    /// The count the reference reports is the number of records it returns. Worth pinning because
    /// when a plan is fully mapped the reference answers it from the bitset without reading a
    /// record, which is a different code path from the one that produced the records.
    /// </summary>
    [Theory]
    [MemberData(nameof(Tables))]
    public void TheCountMatchesTheRecordsReturned(string table)
    {
        foreach (DumpQueryCase c in Dump(table).Cases)
            c.Count.Should().Be(c.Optimized.Count, "the count for {0}", c.Filter);
    }

    /// <summary>
    /// The reference disagrees with itself on exactly one filter in the corpus, and it is the one
    /// written to provoke it: a reflected comparison nested inside an .AND., where m4map.c:216's
    /// assignment discards the half of the condition that would have reflected the operator.
    ///
    /// Un-nested, the same comparison is correct -- which is the evidence that the defect is what
    /// the spec says it is rather than something else in the neighbourhood.
    /// </summary>
    [Fact]
    public void TheReferenceContradictsItselfOnlyWhereTheDefectPredicts()
    {
        DumpQueryCase[] disagreeing =
            [.. Expr.Cases.Concat(Base.Cases).Concat(Deep.Cases).Where(c => !c.Agrees)];

        disagreeing.Should().ContainSingle();
        disagreeing[0].Filter.Should().Be("K_INT > 0 .AND. 4 > K_NUM");

        // The two records sets are not merely different, they are disjoint: the bound was inverted,
        // not loosened. And the residual filter never corrected it, because the term's operand had
        // matched a tag and was therefore trusted.
        disagreeing[0].Optimized.Should().NotIntersectWith(disagreeing[0].Scanned);

        DumpQueryCase unnested = Expr.Cases.Single(c => c.Filter == "4 > K_NUM");
        unnested.Agrees.Should().BeTrue();

        DumpQueryCase tagFirst = Expr.Cases.Single(c => c.Filter == "K_INT > 0 .AND. K_NUM < 4");
        tagFirst.Agrees.Should().BeTrue();
        disagreeing[0].Scanned.Should().Equal(tagFirst.Optimized);
    }

    /// <summary>
    /// A comparison against an H field is never optimizable, which section 3.2 predicted from
    /// E4FLOAT being absent from e4isConstant and nothing had ever checked. The refusal comes from
    /// the constant side, so it holds even though an r4floatBin tag is not itself refused.
    /// </summary>
    [Fact]
    public void AComparisonAgainstABinaryFloatIsNeverOptimized()
    {
        DumpQueryCase c = Expr.Cases.Single(q => q.Filter == "K_FLT > 0");

        c.Optimized_Anything.Should().BeFalse();
        c.Nodes.Should().OnlyContain(n => n.Tag == null);
    }

    /// <summary>
    /// The four eligibility refusals, each seen on a tag that exists and is skipped anyway.
    ///
    /// The unique one is the interesting case: CDXBASE has both a plain and a unique tag over K_DUP,
    /// so a match on the plain one shows the optimizer walking PAST the unique tag rather than
    /// stopping at it -- which a table with only one tag over the field could never distinguish.
    /// </summary>
    [Fact]
    public void TagsAreRefusedByTypeAndTheWalkContinuesPastThem()
    {
        Base.Cases.Single(q => q.Filter == "K_I > 0").Optimized_Anything.Should().BeFalse();
        Expr.Cases.Single(q => q.Filter == "K_CUR > 0").Optimized_Anything.Should().BeFalse();

        DumpQueryCase dup = Base.Cases.Single(q => q.Filter == "K_DUP = \"CHERRY\"");
        dup.Nodes.Should().Contain(n => n.Tag == "T_DUP");
        dup.Nodes.Should().NotContain(n => n.Tag == "T_UNIQ");
    }

    /// <summary>
    /// A short character constant widens an exclusive lower bound to an inclusive one, and the
    /// widening is exact rather than a safety margin: the same filter written with the inclusive
    /// operator returns the same records, and so does the reflected form that reaches the same slot.
    ///
    /// That is what lets a widened leaf be trusted without a residual re-check, which the port's
    /// trust rule depends on.
    /// </summary>
    [Fact]
    public void TheShortConstantWideningChangesNoRecords()
    {
        IReadOnlyList<int> strict = Expr.Cases.Single(q => q.Filter == "UPPER(K_TEXT) > \"S\"").Optimized;
        IReadOnlyList<int> inclusive = Expr.Cases.Single(q => q.Filter == "UPPER(K_TEXT) >= \"S\"").Optimized;
        IReadOnlyList<int> reflected = Expr.Cases.Single(q => q.Filter == "\"S\" < UPPER(K_TEXT)").Optimized;

        strict.Should().Equal(inclusive);
        strict.Should().Equal(reflected);
        strict.Should().NotBeEmpty();
    }

    /// <summary>
    /// An .AND. survives losing a conjunct it cannot optimize and an .OR. does not. The asymmetry is
    /// the correctness core of decomposition: a dropped conjunct widens the candidate set, which the
    /// residual filter narrows again, while a dropped disjunct would lose records nothing recovers.
    /// </summary>
    [Fact]
    public void AnAndSurvivesAnUnoptimizableTermAndAnOrDoesNot()
    {
        DumpQueryCase and = Expr.Cases.Single(q => q.Filter == "K_NUM > 0 .AND. K_LOG");
        DumpQueryCase or = Expr.Cases.Single(q => q.Filter == "K_NUM > 0 .OR. K_LOG");

        and.Optimized_Anything.Should().BeTrue();
        and.FullyMapped.Should().BeFalse();
        or.Optimized_Anything.Should().BeFalse();
    }
}
