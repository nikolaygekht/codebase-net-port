using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Performance;

/// <summary>
/// That turning the cache on is still worth having.
///
/// Layer five, and the only gate that notices this step becoming dead weight. Every correctness test
/// in the suite passes on a cache that is perfectly implemented and never hits, and none of them
/// notices a runtime or a machine on which read syscalls have become cheap enough that caching them
/// stops paying. Those are the two ways an optimization quietly turns into complexity for nothing, and
/// this is what reports them.
///
/// **The floor is 2x, and what it is applied to was chosen by measuring.** A walk is 7.4 times faster
/// cached and [c]Go(n)[/c]-then-[c]Skip(1)[/c] 23 times, so both clear the floor by a wide margin; a
/// seek is only 2.13 times, which would put a timed assertion inside noise of its own threshold. So a
/// seek is gated on its **read count** instead, which is exact and unbounded, and the clock is applied
/// where the cache's win is large. The floor itself is never lowered to fit — see the note beside the
/// tests.
///
/// **Two of the three assertions need no clock at all.** A read count and an allocation figure are
/// exact and machine-independent, and between them they catch every *code* regression the timed one
/// would. The timed one is kept for the single thing they cannot see: the day the syscalls get cheap.
/// If it ever flakes it gets a trait and an opt-out — **never a lower threshold**, because a floor that
/// moves to match the code is not a gate.
/// </summary>
[Trait("Layer", "Performance")]
public sealed class BlockCacheSpeedupTests
{
    /// <summary>How much cheaper the cache has to make things before this test is content.</summary>
    private const double Floor = 2.0;

    /// <summary>Seeks per timed pass, enough to swamp the cost of positioning once.</summary>
    private const int Seeks = 200;

    /// <summary>Records per timed walk, bounded so the test stays quick on a slow filesystem.</summary>
    private const int Walk = 1000;

    /// <summary>Timed passes per configuration; the *minimum* of these is what is compared.</summary>
    private const int Passes = 5;

    /// <summary>How long to run untimed first, by the clock and never by a pass count.</summary>
    private static readonly TimeSpan WarmUp = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void TheCacheCutsTheReadsASeekStormCostsByAtLeastTwice()
    {
        // Exact, machine-independent, and the assertion that catches a cache which is correct and
        // never hits -- which every other test in the suite would pass.
        long uncached = CountedReads(BlockCacheMode.Off, SeekStorm);
        long cached = CountedReads(BlockCacheMode.Always, SeekStorm);

        Ratio(uncached, cached).Should().BeGreaterThanOrEqualTo(
            Floor,
            "a seek storm issued {0} reads uncached and {1} cached",
            uncached,
            cached);
    }

    [Fact]
    public void TheCacheCutsTheReadsAWalkCostsByAtLeastTwice()
    {
        long uncached = CountedReads(BlockCacheMode.Off, TagWalk);
        long cached = CountedReads(BlockCacheMode.Always, TagWalk);

        Ratio(uncached, cached).Should().BeGreaterThanOrEqualTo(
            Floor,
            "a walk issued {0} reads uncached and {1} cached",
            uncached,
            cached);
    }

    [Fact]
    public void TheCacheMakesAWalkAtLeastTwiceAsFast()
    {
        AssertTwiceAsFast(TagWalk, "tag walk");
    }

    [Fact]
    public void TheCacheMakesPositioningByRecordNumberAtLeastTwiceAsFast()
    {
        AssertTwiceAsFast(GoThenSkip, "Go(n) then Skip(1)");
    }

    // There is deliberately NO timed assertion on a seek storm, and the reason is a measurement rather
    // than a convenience. Measured on 2026-08-21 in Release, the cache makes a seek 2.13 times faster
    // (9 386 ns to 4 400 ns) against 7.4 times for a walk and 23 times for Go(n)-then-Skip(1) -- so a
    // 2x floor on a seek would sit within noise of its own threshold and flake, while the same floor on
    // the other two has three to eleven times the room it needs.
    //
    // The floor is not lowered to fit: WHAT IS MEASURED changes instead. A seek's win is gated by the
    // read count above, which is exact and whose ratio is unbounded because a cached seek storm reaches
    // the file zero times. What the timed assertions add is the one thing a read count cannot see, and
    // that signal is just as visible in a walk.
    //
    // Why a seek gains so much less than a walk is a finding of its own and not the cache's fault: a
    // cached seek costs 4 400 ns while the index descent it contains is about 530 ns from RAM, so most
    // of a seek is CPU in the Table layer above the tree -- which nothing has measured yet.

    /// <summary>
    /// Times a workload cached and uncached, in both orders, and requires the floor of both.
    /// </summary>
    /// <param name="work">What to run.</param>
    /// <param name="what">What to call it when the assertion fails.</param>
    /// <remarks>
    /// The method is this step's own scar written down (ANALYSIS.md section 8): warm up **by wall
    /// clock** and never by a pass count, because a fast pass can complete before the JIT has reached
    /// tier one and a figure four times wrong is the result; compare the **minima** rather than the
    /// means, because noise only ever adds time; and run the pair **in both orders**, because a figure
    /// that moves when only its position moves is measuring the runtime and not the code.
    /// </remarks>
    private static void AssertTwiceAsFast(Action<Table> work, string what)
    {
        double uncachedFirst = Ratio(Fastest(BlockCacheMode.Off, work), Fastest(BlockCacheMode.Always, work));
        double cachedFirst = 1.0 / Ratio(Fastest(BlockCacheMode.Always, work), Fastest(BlockCacheMode.Off, work));

        // Both orderings, and the failure message says what the three possible causes are, because a
        // red here is triaged and not simply fixed.
        foreach ((double ratio, string order) in new[]
                 {
                     (uncachedFirst, "uncached first"),
                     (cachedFirst, "cached first"),
                 })
        {
            ratio.Should().BeGreaterThanOrEqualTo(
                Floor,
                "the cache made a {0} only {1:F2} times faster ({2}). Three things do this, and they " +
                "are triaged in this order: the read-count tests in this class are exact, so if they " +
                "still pass then the cache is working and this is not a code regression; then either " +
                "read syscalls on this machine have become cheap enough that caching them no longer " +
                "pays, which is a finding worth writing down rather than a bug; or the machine is too " +
                "loaded to measure, which the minimum-of-{3} method is built to rule out first. " +
                "Do not lower the floor.",
                what,
                ratio,
                order,
                Passes);
        }
    }

    private static void SeekStorm(Table table)
    {
        Tag byName = table.Tags["T_NAME"];
        table.SelectTag(byName);

        FieldDefinition name = table.Fields["NAME"];
        string first = table.Top() == GoResult.Ok ? table.GetString(name) : string.Empty;

        for (int i = 0; i < Seeks; i++)
            table.Seek(first);
    }

    private static void GoThenSkip(Table table)
    {
        table.SelectTag(null);
        FieldDefinition id = table.Fields["ID"];

        for (int record = 1; record <= Walk; record++)
        {
            if (table.Go(record) != GoResult.Ok)
                break;

            _ = table.GetInt32(id);
            table.Skip(1);
        }
    }

    private static void TagWalk(Table table)
    {
        table.SelectTag(table.Tags["T_NAME"]);
        FieldDefinition id = table.Fields["ID"];
        int seen = 0;

        for (GoResult go = table.Top(); go == GoResult.Ok && seen < Walk; seen++)
        {
            _ = table.GetInt32(id);
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }
    }

    /// <summary>
    /// Runs a workload once and gives the reads that reached the file.
    /// </summary>
    private static long CountedReads(BlockCacheMode mode, Action<Table> work)
    {
        Counting files = new();
        using CodeBaseEngine engine = new(files, FileSystem.Instance)
        {
            BlockCache = mode,
            DefaultEncoding = Encoding.Latin1,
        };

        using Table table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));

        work(table);          // once to open and position, so opening is not what is counted
        files.Reset();
        work(table);

        return files.Reads;
    }

    /// <summary>
    /// Runs a workload many times and gives the quickest pass.
    /// </summary>
    private static TimeSpan Fastest(BlockCacheMode mode, Action<Table> work)
    {
        using CodeBaseEngine engine = new() { BlockCache = mode, DefaultEncoding = Encoding.Latin1 };
        using Table table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));

        Stopwatch warming = Stopwatch.StartNew();
        while (warming.Elapsed < WarmUp)
            work(table);

        TimeSpan best = TimeSpan.MaxValue;

        for (int pass = 0; pass < Passes; pass++)
        {
            Stopwatch timing = Stopwatch.StartNew();
            work(table);
            timing.Stop();

            if (timing.Elapsed < best)
                best = timing.Elapsed;
        }

        return best;
    }

    private static double Ratio(long slower, long faster) => faster == 0 ? double.MaxValue : (double)slower / faster;

    private static double Ratio(TimeSpan slower, TimeSpan faster) =>
        faster <= TimeSpan.Zero ? double.MaxValue : slower.TotalNanoseconds / faster.TotalNanoseconds;

    /// <summary>
    /// Opens real files and counts the reads that reach them.
    /// </summary>
    private sealed class Counting : IRandomAccessSourceFactory
    {
        private int reads;

        /// <summary>Gets how many reads reached a file since the last reset.</summary>
        public long Reads => reads;

        /// <summary>Starts counting again.</summary>
        public void Reset() => reads = 0;

        public IRandomAccessSource Open(string path) => new Source(FileSystem.Instance.Open(path), this);

        private sealed class Source(IRandomAccessSource inner, Counting counter) : IRandomAccessSource
        {
            public long Length => inner.Length;

            public int Read(long offset, Span<byte> buffer)
            {
                counter.reads++;
                return inner.Read(offset, buffer);
            }

            public void Dispose() => inner.Dispose();
        }
    }
}
