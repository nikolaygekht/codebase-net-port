using AwesomeAssertions;
using CodeBase.Net.Queries;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// Gate C: the optimizer actually optimizes, counted rather than timed.
///
/// Every other gate in this suite says the answer is right, and an optimizer that seeks nothing and
/// reads the whole table is perfectly right. So the thing that has to be checked here is the work,
/// and the only honest place to count it is where it happens -- reads reaching the file. Four of the
/// five figures below are exact numbers rather than thresholds, so they cannot drift, and the one
/// ratio has enough headroom that it will not flap while still failing an inert optimizer by two
/// orders of magnitude.
///
/// Counted and not timed, for the reason the performance audit established: a figure that moves when
/// only its position moves is measuring the runtime rather than the code. A read count is identical
/// on every machine, inside WSL and out.
///
/// Governing specification: the step's DESIGN.md, Decision 13.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class QueryWorkGoldenTests
{
    private const string Table = "CDXDEEP";
    private const int Records = 600;

    /// <summary>
    /// The headline claim: a query whose plan answers the whole filter can say how many records match
    /// without reading one.
    /// </summary>
    [Theory]
    [InlineData("K_PFX = \"CUSTOMER-ACCT-0042\"", 1)]
    [InlineData("K_N > 100000 .AND. K_N < 200000", 60)]
    [InlineData("K_DUP = \"DELTA\"", 60)]
    [InlineData("K_N > -400000", 599)]
    public void CountingAFullyMappedQueryReadsNoRecordAtAll(string filter, int expected)
    {
        using Measured measured = Measured.Of(Table);

        measured.Table.Query(filter).Count.Should().Be(expected);

        measured.RecordReads.Should().Be(0, "{0} is answered by its tags alone", filter);
        measured.IndexReads.Should().BeGreaterThan(0, "it still has to seek");
    }

    /// <summary>
    /// And walking one reads exactly the records it returns -- not the table, and not the candidates.
    /// </summary>
    [Theory]
    [InlineData("K_PFX = \"CUSTOMER-ACCT-0042\"", 1)]
    [InlineData("K_N > 100000 .AND. K_N < 200000", 60)]
    [InlineData("K_DUP = \"DELTA\"", 60)]
    public void WalkingAFullyMappedQueryReadsExactlyWhatItReturns(string filter, int expected)
    {
        using Measured measured = Measured.Of(Table);
        Query query = measured.Table.Query(filter);
        int walked = 0;

        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
            walked++;

        walked.Should().Be(expected);
        measured.RecordReads.Should().Be(expected, "{0} reads only what it returns", filter);
    }

    /// <summary>
    /// A selective query costs a small fraction of a scan. The threshold has two orders of magnitude
    /// of headroom below the measured figure, which is what keeps it from flapping while still being
    /// far out of reach of an optimizer that reads the table.
    /// </summary>
    [Fact]
    public void ASelectiveQueryCostsAFractionOfAScan()
    {
        using Measured measured = Measured.Of(Table);

        measured.Table.Query("K_PFX = \"CUSTOMER-ACCT-0042\"").Count.Should().Be(1);
        long optimized = measured.TotalReads;

        measured.Reset();
        for (int record = 1; record <= Records; record++)
            measured.Table.Go(record);

        long scanned = measured.TotalReads;

        scanned.Should().Be(Records, "a scan reads every record");
        optimized.Should().BeLessThan(scanned / 10, "the whole point is to read far less than a scan");
    }

    /// <summary>
    /// And the counter is measuring something: a filter no tag can answer reads every record, which
    /// is the fall-back working rather than the optimizer failing.
    /// </summary>
    [Fact]
    public void AFilterNothingCanSeekForReadsTheWholeTable()
    {
        using Measured measured = Measured.Of("CDXEXPR");

        // No tag indexes K_DBL, so every record has to be looked at.
        measured.Table.Query("K_DBL > 0").Count.Should().BeGreaterThan(0);
        measured.RecordReads.Should().Be(measured.Table.RecordCount);
    }

    /// <summary>
    /// The widened scope buys reads, not just permission: an integer tag the reference refuses turns
    /// a whole-table read into a seek.
    /// </summary>
    /// <value>
    /// The concrete payoff of the one gate ADR-52 lifts, and the only place it is measured. Under the
    /// default the term has no tag and every record is read; widened, only the matching ones are.
    /// </value>
    [Fact]
    public void WideningTheScopeTurnsAScanIntoASeek()
    {
        using Measured narrow = Measured.Of("CDXBASE");
        using Measured wide = Measured.Of("CDXBASE", QueryTagScope.AllSeekable);

        int matched = narrow.Table.Query("K_I > 0").Count;
        long readWithout = narrow.RecordReads;

        wide.Table.Query("K_I > 0").Count.Should().Be(matched, "the answer must not change");
        wide.RecordReads.Should().Be(0, "an integer tag answers the count on its own");
        readWithout.Should().Be(narrow.Table.RecordCount, "without it, every record is read");
    }

    /// <summary>
    /// One open table, with the reads reaching each of its files counted from the moment it is open.
    /// </summary>
    private sealed class Measured : IDisposable
    {
        private readonly CodeBaseEngine engine;
        private readonly CountingFileSystem counter;
        private readonly string stem;

        private Measured(CodeBaseEngine engine, CountingFileSystem counter, Table table, string stem)
        {
            this.engine = engine;
            this.counter = counter;
            this.stem = stem;
            Table = table;
        }

        public Table Table { get; }

        /// <summary>Gets how many reads have reached the table's own file.</summary>
        public long RecordReads => counter.ReadsOf(stem + ".DBF");

        /// <summary>Gets how many reads have reached its index.</summary>
        public long IndexReads => counter.ReadsOf(stem + ".cdx");

        /// <summary>Gets how many reads have reached anything.</summary>
        public long TotalReads => counter.TotalReads;

        public static Measured Of(string table, QueryTagScope scope = QueryTagScope.Compatible)
        {
            CountingFileSystem counter = new();
            CodeBaseEngine engine = new(counter, counter) { QueryTagScope = scope };
            Table opened = engine.OpenTable(Corpus.PathOf(table + ".DBF"));

            opened.SelectTag(null);

            // Opening read the header and the tag directory; the measurement starts here.
            counter.Reset();
            return new Measured(engine, counter, opened, table);
        }

        public void Reset() => counter.Reset();

        public void Dispose()
        {
            Table.Dispose();
            engine.Dispose();
        }
    }
}
