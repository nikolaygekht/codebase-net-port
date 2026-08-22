using System.Diagnostics;
using System.Text;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Profiling;

/// <summary>
/// Runs one read-path arm in one process for as long as it is asked to, for a profiler to watch.
/// </summary>
internal static class Program
{
    /// <summary>Queries per pass, and the number every rate below is divided by.</summary>
    private const int Queries = 2000;

    /// <summary>Milliseconds of untimed work before the counters are read.</summary>
    private const int WarmUpMilliseconds = 1000;

    /// <summary>
    /// Runs the arm named on the command line.
    /// </summary>
    /// <param name="args">The arm to run, and how many seconds to run it for.</param>
    /// <returns>Zero, or two when the arm is not one this knows.</returns>
    private static int Main(string[] args)
    {
        string arm = args.Length > 0 ? args[0] : "cached-descent";
        int seconds = args.Length > 1 && int.TryParse(args[1], out int given) ? given : 20;

        if (arm is "-h" or "--help" or "help")
        {
            Usage();
            return 0;
        }

        Console.WriteLine($"arm '{arm}', {seconds}s, {(Debugger.IsAttached ? "DEBUGGER ATTACHED" : "no debugger")}");
        Console.WriteLine(
            $"runtime {Environment.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")}, " +
            $"server GC {System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine();

        switch (arm)
        {
            case "cached-descent": return Descent(cached: true, seconds);
            case "cached-descent-fixedlength": return Descent(cached: true, seconds, fixedLength: true);
            case "array-descent": return Descent(cached: false, seconds);
            case "seek": return ThroughTheTable(seconds, walk: false);
            case "walk": return ThroughTheTable(seconds, walk: true);
            default:
                Console.Error.WriteLine($"There is no arm called '{arm}'.");
                Usage();
                return 2;
        }
    }

    private static void Usage()
    {
        Console.WriteLine("CodeBase.Net.Profiling <arm> [seconds]");
        Console.WriteLine();
        Console.WriteLine("  cached-descent               the index descent through the block cache  (default)");
        Console.WriteLine("  cached-descent-fixedlength   the same, with the file's length asked for once");
        Console.WriteLine("  array-descent                the same descent, the whole file in one array");
        Console.WriteLine("  seek                         Table.Seek end to end, cache on");
        Console.WriteLine("  walk                         a tag-order walk, cache on");
        Console.WriteLine();
        Console.WriteLine("The pair to compare is cached-descent against array-descent: identical work,");
        Console.WriteLine("differing only in where the bytes are copied from.");
    }

    /// <summary>
    /// Runs the bare descent loop, over the cache or over an array.
    /// </summary>
    /// <param name="cached">True to read through the block cache, false to read from one array.</param>
    /// <param name="seconds">How long to keep going.</param>
    /// <param name="fixedLength">True to ask the file its length once instead of per read.</param>
    /// <returns>Zero.</returns>
    /// <remarks>
    /// The loop is the one in [c]SeekLayersBenchmarks[/c] arms 0 and 0a, deliberately unchanged, so a
    /// profile of this is a profile of the arms whose 545 against 4 214 nanoseconds raised the
    /// question. Everything above the block reads is left out: that layer was measured at three per
    /// cent and is not what is being looked for.
    /// </remarks>
    private static int Descent(bool cached, int seconds, bool fixedLength = false)
    {
        string indexPath = Corpus.BenchmarkPathOf("PERF10K.cdx");

        CountingFileSystem files = new();
        CachingSourceFactory caching = new(files) { Mode = BlockCacheMode.Always };

        IRandomAccessSource source = cached
            ? caching.Open(indexPath)
            : new InMemorySource(File.ReadAllBytes(indexPath));

        if (fixedLength)
            source = new FixedLengthSource(source);

        using IndexFileReader index = IndexFileReader.Open(source, indexPath, _ => KeyPadding.Space);
        CdxTag tag = index.Tag("T_NAME");
        NodeReader nodes = new(source, BlockAddressing.Standard);

        byte[][] keys = KeysFromTheTable(tag.KeyLength);
        byte[] buffer = new byte[tag.KeyLength];

        Outcome outcome = Run(seconds, () => OneDescentPass(tag, nodes, keys, buffer), files, caching);

        Report(outcome, files);
        return 0;
    }

    /// <summary>
    /// One pass of the descent over every query key.
    /// </summary>
    /// <param name="tag">The tag to descend.</param>
    /// <param name="nodes">Where blocks come from.</param>
    /// <param name="keys">The keys to look for.</param>
    /// <param name="buffer">The search buffer, reused as the library reuses it.</param>
    /// <returns>The sum of the record numbers found, so nothing can be optimised away.</returns>
    private static long OneDescentPass(CdxTag tag, NodeReader nodes, byte[][] keys, byte[] buffer)
    {
        long sum = 0;

        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, buffer.Length, tag.KeyLength, tag.PadByte);
            uint node = tag.Header.Root;

            for (;;)
            {
                Block block = nodes.Read(node);
                NodeHeader header = NodeHeader.Parse(block.Span, node);

                if (header.IsLeaf)
                {
                    LeafBlock leaf = LeafBlock.Parse(block, header, tag.KeyLength, tag.PadByte, node);

                    if (leaf.Seek(search, out int at))
                        sum += leaf.PackedAt(at).Record;

                    break;
                }

                BranchBlock branch = BranchBlock.Parse(block, header, tag.KeyLength, node);
                node = branch.ChildAt(branch.Seek(search));
            }
        }

        return sum;
    }

    /// <summary>
    /// Runs a whole seek or a whole walk through the public surface, cache on.
    /// </summary>
    /// <param name="seconds">How long to keep going.</param>
    /// <param name="walk">True to walk the tag, false to seek in it.</param>
    /// <returns>Zero.</returns>
    /// <remarks>
    /// Read counts only. The engine builds its own caching factory inside itself, so its hit and miss
    /// counts are not reachable from out here — which is a thing worth knowing rather than working
    /// around, since it is the descent arms above that the question is about.
    /// </remarks>
    private static int ThroughTheTable(int seconds, bool walk)
    {
        CountingFileSystem files = new();

        using CodeBaseEngine engine = new(files, FileSystem.Instance)
        {
            BlockCache = BlockCacheMode.Always,
            DefaultEncoding = Encoding.Latin1,
        };

        using Table table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));
        FieldDefinition name = table.Fields["NAME"];
        table.SelectTag(table.Tags["T_NAME"]);

        string[] values = StringKeysFrom(table, name);

        Outcome outcome = walk
            ? Run(seconds, () => OneWalkPass(table, name), files, null)
            : Run(seconds, () => OneSeekPass(table, values), files, null);

        Report(outcome, files);
        return 0;
    }

    private static long OneSeekPass(Table table, string[] values)
    {
        long found = 0;

        foreach (string value in values)
        {
            if (table.Seek(value) == GoResult.Ok)
                found++;
        }

        return found;
    }

    private static long OneWalkPass(Table table, FieldDefinition name)
    {
        long records = 0;
        long checksum = 0;

        // Records, not characters. Counting the characters read makes a pass stop after a hundred rows
        // and every per-operation figure meaningless, which is what the first version of this did.
        for (GoResult go = table.Top(); go == GoResult.Ok && records < Queries; records++)
        {
            checksum += table.GetString(name).Length;
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return checksum;
    }

    /// <summary>
    /// Warms a pass up by wall clock, then repeats it until the time is up.
    /// </summary>
    /// <param name="seconds">How long the measured part runs.</param>
    /// <param name="pass">One pass over the query set.</param>
    /// <param name="files">The read counter, reset after the warm-up.</param>
    /// <param name="caching">The cache, whose counters are reset with it, or null when there is none.</param>
    /// <returns>What the measured part did.</returns>
    /// <remarks>
    /// By wall clock and never by pass count: this step's own first measurement was four times wrong
    /// because five passes never gave the JIT the time it needs to reach tier one
    /// (009-performance-audit/ANALYSIS.md section 8).
    /// </remarks>
    private static Outcome Run(
        int seconds, Func<long> pass, CountingFileSystem files, CachingSourceFactory? caching)
    {
        long sink = 0;

        Stopwatch warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < WarmUpMilliseconds)
            sink += pass();

        // The counters are read as differences rather than reset, so the library needs no method that
        // exists only for this driver.
        files.Reset();
        BlockCache? cache = caching?.Cache;
        long hitsBefore = cache?.Hits ?? 0;
        long missesBefore = cache?.Misses ?? 0;

        Console.WriteLine($"warmed up in {warm.ElapsedMilliseconds} ms; measuring for {seconds}s");

        long passes = 0;
        Stopwatch timing = Stopwatch.StartNew();
        while (timing.Elapsed.TotalSeconds < seconds)
        {
            sink += pass();
            passes++;
        }

        timing.Stop();
        GC.KeepAlive(sink);

        return new Outcome(
            passes,
            timing.Elapsed,
            cache is null ? null : cache.Hits - hitsBefore,
            cache is null ? null : cache.Misses - missesBefore);
    }

    /// <summary>What one measured run did.</summary>
    /// <param name="Passes">How many passes over the query set ran.</param>
    /// <param name="Elapsed">How long they took.</param>
    /// <param name="Hits">Windows served without a read, or null when no cache was reachable.</param>
    /// <param name="Misses">Windows that had to be read, or null when no cache was reachable.</param>
    private readonly record struct Outcome(long Passes, TimeSpan Elapsed, long? Hits, long? Misses);

    private static void Report(Outcome outcome, CountingFileSystem files)
    {
        long operations = Math.Max(outcome.Passes * Queries, 1);

        Console.WriteLine();
        Console.WriteLine($"passes                   {outcome.Passes:N0}  ({operations:N0} operations)");
        Console.WriteLine($"per operation            {outcome.Elapsed.TotalNanoseconds / operations:N0} ns   (rough; BenchmarkDotNet is the authority)");
        Console.WriteLine($"reads reaching the file  {files.Reads:N0}   ({(double)files.Reads / operations:N3} per operation)");
        Console.WriteLine($"Length asked of the file {files.LengthQueries:N0}   ({(double)files.LengthQueries / operations:N3} per operation)");

        if (outcome.Hits is long hits && outcome.Misses is long misses)
        {
            long served = hits + misses;

            Console.WriteLine($"cache hits               {hits:N0}");
            Console.WriteLine($"cache misses             {misses:N0}");
            Console.WriteLine($"hit rate                 {(served == 0 ? 0 : 100.0 * hits / served):N2}%   ({(double)served / operations:N3} windows served per operation)");
        }
        else
        {
            Console.WriteLine("cache counters           not reachable from here for this arm");
        }
    }

    /// <summary>
    /// Reads the query keys out of the table, so no expected value is invented.
    /// </summary>
    /// <param name="keyLength">The tag's key width, which the keys are padded to.</param>
    /// <returns>The keys, in their stored form.</returns>
    private static byte[][] KeysFromTheTable(int keyLength)
    {
        using CodeBaseEngine engine = new() { DefaultEncoding = Encoding.Latin1 };
        using Table table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));

        FieldDefinition name = table.Fields["NAME"];
        table.SelectTag(table.Tags["T_NAME"]);

        return [.. StringKeysFrom(table, name).Select(value =>
        {
            byte[] key = new byte[keyLength];
            key.AsSpan().Fill((byte)' ');
            Encoding.Latin1.GetBytes(value).AsSpan(0, Math.Min(value.Length, keyLength)).CopyTo(key);

            return key;
        })];
    }

    private static string[] StringKeysFrom(Table table, FieldDefinition name)
    {
        List<string> values = [];

        for (GoResult go = table.Top(); go == GoResult.Ok && values.Count < Queries;)
        {
            values.Add(table.GetString(name));
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return [.. values];
    }
}
