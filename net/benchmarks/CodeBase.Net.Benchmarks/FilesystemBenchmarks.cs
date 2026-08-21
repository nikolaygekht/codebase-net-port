using BenchmarkDotNet.Attributes;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// The same reads from two filesystems, to price the one this repository lives on.
///
/// Every figure this step produced had to be taken natively on Windows, because the same benchmark run
/// from WSL against the repository reported numbers 25 to 200 times larger — a walked record at 194
/// microseconds against 1.15, a seek at 1.58 milliseconds against 9.4. That was attributed to the
/// [c]/mnt[/c] bridge on the strength of the pattern alone. This measures it instead: the identical
/// workload, in one process, from the repository and from a copy on the Linux filesystem.
///
/// It answers a question about the machine as much as about the port, which is why it is small: what is
/// being timed is one record read with as little of this library in the way as possible. Each place is
/// measured **both cached and uncached**, because what a cache is worth is exactly the cost of the read
/// it avoids — the two figures are the same measurement seen from two sides, and either alone misleads.
///
/// Where to run it: [c]CODEBASE_BENCHMARK_PLACES[/c] names the directories, so the same build can
/// compare a Linux filesystem, a Windows one, a second physical disk, or the same disk reached across
/// the WSL bridge.
/// </summary>
public class FilesystemBenchmarks
{
    /// <summary>Records positioned on per invocation, so the figure is per read.</summary>
    private const int Reads = 200;

    /// <summary>Coprime with the record count, so the reads are scattered rather than sequential.</summary>
    private const int Stride = 3571;

    private CodeBaseEngine engine = null!;
    private Table table = null!;
    private FieldDefinition id = null!;

    /// <summary>
    /// Gets the directories to read the same workload from.
    /// </summary>
    /// <returns>
    /// The copy in the home directory first when it exists, then the one in the repository. Only one
    /// runs when the other is absent, which is what happens on Windows.
    /// </returns>
    public static IEnumerable<string> Places()
    {
        // Named explicitly when there is something worth comparing that this cannot guess -- a second
        // physical disk, a different medium -- as a list separated by the platform's path separator.
        string? named = Environment.GetEnvironmentVariable("CODEBASE_BENCHMARK_PLACES");

        if (named is { Length: > 0 })
        {
            foreach (string place in named.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(place, "PERF10K.DBF")))
                    yield return place;
            }

            yield break;
        }

        string home = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "codebase-bench");

        if (File.Exists(Path.Combine(home, "PERF10K.DBF")))
            yield return home;

        yield return Path.Combine(Corpus.Root, "benchmark");
    }

    /// <summary>Gets or sets which directory this run reads from.</summary>
    [ParamsSource(nameof(Places))]
    public string Place { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether blocks are kept, so each place is measured both ways.
    /// </summary>
    /// <value>
    /// What the cache is worth depends on what a read costs, and what a read costs is the whole point
    /// of this class — so the two have to be measured together or neither figure means anything.
    /// </value>
    [Params(BlockCacheMode.Off, BlockCacheMode.Always)]
    public BlockCacheMode Cache { get; set; } = BlockCacheMode.Off;

    /// <summary>
    /// Opens the table in the directory under test, with no caching at all.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        // The override Corpus reads, set before anything asks it where the data is.
        Environment.SetEnvironmentVariable("CODEBASE_BENCHMARK_DATA", Place);

        engine = new CodeBaseEngine
        {
            BlockCache = Cache,
            DefaultEncoding = System.Text.Encoding.Latin1,
        };

        table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));
        id = table.Fields["ID"];
    }

    /// <summary>
    /// Closes the table.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        table.Dispose();
        engine.Dispose();
    }

    /// <summary>
    /// Positions on scattered records, which is one read each and almost no other work.
    /// </summary>
    /// <returns>The sum of the ID field, so no read can be discarded.</returns>
    [Benchmark(OperationsPerInvoke = Reads, Description = "position on a record, per read")]
    public long RecordRead()
    {
        long checksum = 0;

        for (int i = 0; i < Reads; i++)
        {
            if (table.Go(((i * Stride) % 10000) + 1) == GoResult.Ok)
                checksum += table.GetInt32(id);
        }

        return checksum;
    }
}
