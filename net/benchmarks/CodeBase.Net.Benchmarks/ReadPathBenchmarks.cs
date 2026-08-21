using BenchmarkDotNet.Attributes;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// Times the four read-path operations a query engine is built out of.
///
/// The workload is PERF10K, the same 10 000-record table step 009's audit measured, so the numbers
/// printed here are comparable with the ones in its SUMMARY.md rather than merely similar. Every
/// benchmark reports per operation, not per invocation, through [c]OperationsPerInvoke[/c].
///
/// Each returns a checksum built from the records it reached. That is not decoration: without a
/// value leaving the loop, a seek storm whose results are never read is free to be optimized away,
/// and the benchmark would measure nothing while looking healthy. The checksums also make two runs
/// comparable — a change that speeds a benchmark up and alters its checksum did not speed anything
/// up, it stopped doing the work.
/// </summary>
[MemoryDiagnoser]
public class ReadPathBenchmarks
{
    /// <summary>Records in PERF10K, and therefore operations in a full walk.</summary>
    private const int Records = 10000;

    /// <summary>
    /// Queries per storm, one per record so every stored key is asked for exactly once.
    /// </summary>
    private const int Queries = Records;

    /// <summary>
    /// Stride the queries are reordered by, coprime with the count so it is a permutation.
    ///
    /// The order a value is asked for in has to be unrelated to both record order and key order, or
    /// the storm measures sequential access and calls it seeking. This is the audit's own stride.
    /// </summary>
    private const int QueryStride = 3571;

    /// <summary>
    /// Whether the engine keeps blocks between reads, so every arm is measured both ways.
    /// </summary>
    [Params(BlockCacheMode.Off, BlockCacheMode.Always)]
    public BlockCacheMode Cache { get; set; } = BlockCacheMode.Off;

    private CodeBaseEngine engine = null!;
    private Table table = null!;
    private Tag byName = null!;
    private Tag byAmount = null!;
    private FieldDefinition id = null!;
    private string[] names = null!;
    private string[] shortNames = null!;
    private double[] amounts = null!;
    private int[] recordNumbers = null!;

    /// <summary>
    /// Opens the table and derives the query sets from what is stored in it.
    ///
    /// The audit wrote its query sets to files because two languages had to provably ask the same
    /// questions. With the C side gone the queries come from the table itself, which cannot drift
    /// from the data it was built out of and saves 600 KB of committed text.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        engine = new CodeBaseEngine { BlockCache = Cache };

        // A pool smaller than the working set is the only way to make the eviction policy matter: with
        // the default 2048 windows, PERF10K's ~1788 fit whole and nothing is ever retired, so every
        // figure taken that way says nothing about which block would have been given up.
        if (Environment.GetEnvironmentVariable("CODEBASE_BENCHMARK_CACHE_BLOCKS") is { Length: > 0 } size
            && int.TryParse(size, out int blocks))
        {
            engine.BlockCacheBlocks = blocks;
        }
        table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));

        byName = table.Tags["T_NAME"];
        byAmount = table.Tags["T_AMT"];
        id = table.Fields["ID"];

        FieldDefinition name = table.Fields["NAME"];
        FieldDefinition amount = table.Fields["AMOUNT"];

        // Collected in key order, then reordered by a coprime stride, so neither the seek storms nor
        // the Go-then-Skip run reads the file in the order it is laid out in.
        List<string> keys = [];
        List<double> values = [];

        table.SelectTag(byName);
        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            keys.Add(table.GetString(name));
            values.Add(table.GetDouble(amount));
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        if (keys.Count != Records)
            throw new InvalidOperationException($"PERF10K holds {keys.Count} records, expected {Records}.");

        names = new string[Queries];
        shortNames = new string[Queries];
        amounts = new double[Queries];
        recordNumbers = new int[Queries];

        for (int i = 0; i < Queries; i++)
        {
            int scrambled = (i * QueryStride) % Queries;
            names[i] = keys[scrambled];
            shortNames[i] = keys[scrambled][..12];
            amounts[i] = values[scrambled];
            recordNumbers[i] = scrambled + 1;      // record numbers are one-based
        }
    }

    /// <summary>
    /// Closes the table and the engine.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        table.Dispose();
        engine.Dispose();
    }

    /// <summary>
    /// Walks every record in a character tag's order, which is what building a bitmap does.
    /// </summary>
    /// <returns>The sum of the ID field over every record reached.</returns>
    [Benchmark(OperationsPerInvoke = Records, Description = "tag walk, C(20) tag, per record")]
    public long TagWalk()
    {
        long checksum = 0;

        table.SelectTag(byName);
        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            checksum += table.GetInt32(id);
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return checksum;
    }

    /// <summary>
    /// Seeks every stored character key once, in an order unrelated to how they are stored.
    /// </summary>
    /// <returns>The sum of the ID field over every record found.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "seek, C(20) key, 4 levels")]
    public long SeekCharacter()
    {
        long checksum = 0;

        table.SelectTag(byName);
        for (int i = 0; i < Queries; i++)
        {
            if (table.Seek(names[i]) == GoResult.Ok)
                checksum += table.GetInt32(id);
        }

        return checksum;
    }

    /// <summary>
    /// Seeks with values narrower than the key, which is the comparison rule the audit never measured.
    /// </summary>
    /// <returns>The sum of the ID field over every record found.</returns>
    /// <remarks>
    /// A non-prefix character value is padded out to the key width before the search runs
    /// ([c]TableTagCursor.SearchFor[/c]), so a short value takes [c]KeySearch[/c]'s padded rule — the
    /// one every query in the audit avoided, because PERF10K's names fill their field exactly. These
    /// values are the first twelve bytes of stored names, so they find nothing: the padded rule runs
    /// over the whole key width either way, and a miss is a real workload the audit measured too.
    /// </remarks>
    [Benchmark(OperationsPerInvoke = Queries, Description = "seek, short C(20) value, padded rule")]
    public long SeekShortCharacter()
    {
        long checksum = 0;

        table.SelectTag(byName);
        for (int i = 0; i < Queries; i++)
        {
            if (table.Seek(shortNames[i]) == GoResult.Ok)
                checksum += table.GetInt32(id);
        }

        return checksum;
    }

    /// <summary>
    /// Seeks every stored numeric key once, over the shallower of the two trees.
    /// </summary>
    /// <returns>The sum of the ID field over every record found.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "seek, N(12,2) key, 3 levels")]
    public long SeekNumeric()
    {
        long checksum = 0;

        table.SelectTag(byAmount);
        for (int i = 0; i < Queries; i++)
        {
            if (table.Seek(amounts[i]) == GoResult.Ok)
                checksum += table.GetInt32(id);
        }

        return checksum;
    }

    /// <summary>
    /// Positions on a record by number and then steps one on, in natural order.
    /// </summary>
    /// <returns>The sum of the ID field over both records of every pair.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "Go(n) then Skip(1), natural order, per pair")]
    public long GoThenSkip()
    {
        long checksum = 0;

        table.SelectTag(null);
        for (int i = 0; i < Queries; i++)
        {
            if (table.Go(recordNumbers[i]) != GoResult.Ok)
                continue;

            checksum += table.GetInt32(id);
            if (table.Skip(1) == SkipResult.Moved)
                checksum += table.GetInt32(id);
        }

        return checksum;
    }
}
