// ===========================================================================
//  Program.cs -- performance-1-experiment: the CodeBase.Net (C#) side.
//
//    bench <dir> [reps]
//
//  Reads the same PERF10K.DBF and the same three query files the C side used,
//  in the same order, and prints result lines in the same format so the two
//  runs can be diffed field by field. The checksums must match the C side's
//  before any timing means anything.
// ===========================================================================

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using CodeBase.Net;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;

namespace Bench;

internal static class Program
{
    private const int Queries = 10000;
    private const int MaxReps = 21;

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: bench <dir> [reps]");
            return 2;
        }

        string dir = args[0];
        int reps = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5;
        reps = Math.Clamp(reps, 1, MaxReps);

        // `ram` swaps the file-backed source for one that holds each file in a byte[]. That is a
        // perfect block cache -- whole file resident, no syscall, no eviction, no coherence problem --
        // so it does not model any cache anyone would ship. It measures the one thing a subtraction
        // cannot: how much of a seek is I/O and how much is this library's own CPU work.
        bool ram = args.Length > 2 && args[2] == "ram";

        // `micro` answers a different question from the rest of this harness -- not "how fast is a
        // seek" but "where does the port's per-seek CPU go". See Micro.cs.
        if (args.Length > 2 && args[2] == "micro")
        {
            Micro.Run(dir, "T_NAME", 0x20, LoadLines(Path.Combine(dir, "queries-name.txt")), null, reps);
            Micro.Run(dir, "T_AMT", 0x00, [],
                LoadLines(Path.Combine(dir, "queries-amount.txt"))
                    .Select(l => double.Parse(l, CultureInfo.InvariantCulture))
                    .ToArray(),
                reps);
            return 0;
        }

        string[] names = LoadLines(Path.Combine(dir, "queries-name.txt"));
        string[] misses = LoadLines(Path.Combine(dir, "queries-name-miss.txt"));
        double[] amounts = LoadLines(Path.Combine(dir, "queries-amount.txt"))
            .Select(l => double.Parse(l, CultureInfo.InvariantCulture))
            .ToArray();

        var ramFiles = new RamFileSystem();
        using var engine = ram
            ? new CodeBaseEngine(ramFiles, ramFiles)
            : new CodeBaseEngine();

        // The generated table carries no code-page mark, which the library reads as cp437 -- and
        // Encoding.GetEncoding(437) needs the CodePages provider the host is expected to register
        // (ADR-17). The keys here are pure ASCII, so Latin1 gives byte-identical results without
        // pulling a NuGet package into an experiment.
        engine.DefaultEncoding = Encoding.Latin1;

        using Table table = engine.OpenTable(Path.Combine(dir, "PERF10K.DBF"));

        FieldDefinition id = table.Fields["ID"];
        Tag byName = table.Tags["T_NAME"];
        Tag byAmount = table.Tags["T_AMT"];

        Console.WriteLine(
            $"side=cs{(ram ? "-ram" : "    ")} records={table.RecordCount} reps={reps} " +
            $"runtime={Environment.Version} source={(ram ? "byte[] (perfect cache)" : "file")}");

        table.SelectTag(byName);
        Measure("name-hit", Queries, reps, () => PassNameSeek(table, id, names));
        Measure("name-miss", Queries, reps, () => PassNameMiss(table, id, misses));

        table.SelectTag(byAmount);
        Measure("amount-hit", Queries, reps, () => PassAmountSeek(table, id, amounts));

        table.SelectTag(byName);
        Measure("tag-walk", table.RecordCount, reps, () => PassWalk(table, id));

        // name-hit again, last instead of first. Its median came out well above its own minimum
        // while every other scenario's did not, and the scenario measured first is the one that
        // pays for tiered JIT promotion. If the repeat is flat, that was the cause and min_ms is
        // the number to read; if it is not, the spread is real and belongs in the findings.
        table.SelectTag(byName);
        Measure("name-hit-2", Queries, reps, () => PassNameSeek(table, id, names));

        return 0;
    }

    // Every query is a hit, and Seek is the whole-key form -- the query values fill C(20)
    // exactly, so no padding rule is involved on either side.
    private static long PassNameSeek(Table table, FieldDefinition id, string[] q)
    {
        long sum = 0;

        for (int j = 0; j < q.Length; j++)
        {
            if (table.Seek(q[j]) == GoResult.Ok)
                sum += table.GetInt32(id);
        }

        return sum;
    }

    // The miss set. SeekAtOrAfter, not Seek: C's d4seek returns r4after and has already
    // positioned on the neighbour, so this is the call that does the same work.
    private static long PassNameMiss(Table table, FieldDefinition id, string[] q)
    {
        long sum = 0;

        for (int j = 0; j < q.Length; j++)
        {
            SeekResult r = table.SeekAtOrAfter(q[j]);
            if (r is SeekResult.Found or SeekResult.After)
                sum += table.GetInt32(id);
        }

        return sum;
    }

    private static long PassAmountSeek(Table table, FieldDefinition id, double[] q)
    {
        long sum = 0;

        for (int j = 0; j < q.Length; j++)
        {
            if (table.Seek(q[j]) == GoResult.Ok)
                sum += table.GetInt32(id);
        }

        return sum;
    }

    private static long PassWalk(Table table, FieldDefinition id)
    {
        long sum = 0;

        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            sum += table.GetInt32(id);
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return sum;
    }

    private static void Measure(string scenario, int ops, int reps, Func<long> pass)
    {
        // Warm up by WALL CLOCK, not by pass count. This harness was wrong for a while because of
        // it: tier-1 promotion happens on a background thread after a time delay, so five passes is
        // plenty when a pass takes 80 ms (the file-backed runs, which did reach tier-1) and nowhere
        // near enough when a pass takes 7 ms (the RAM-backed runs, which never left tier-0 and so
        // measured ~4x their real cost). Warm until both a pass count and a wall-clock floor are met.
        long checksum = 0;
        var warm = Stopwatch.StartNew();
        for (int w = 0; w < 5 || (warm.ElapsedMilliseconds < 1500 && w < 400); w++)
            checksum = pass();
        warm.Stop();

        double[] ms = new double[reps];

        for (int r = 0; r < reps; r++)
        {
            var sw = Stopwatch.StartNew();
            long c = pass();
            sw.Stop();
            ms[r] = sw.Elapsed.TotalMilliseconds;

            if (c != checksum)
                throw new InvalidOperationException($"{scenario}: unstable checksum");
        }

        double[] sorted = (double[])ms.Clone();
        Array.Sort(sorted);

        // One extra pass, measured for allocation rather than time. Suspect P2 of the performance
        // plan -- a key array copied out per entry read -- predicts a large number here, and the
        // gap between min_ms and med_ms above is what paying for it in Gen0 looks like.
        long bytes0 = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0);
        (long reads0, long ioBytes0) = Io();
        pass();
        (long reads1, long ioBytes1) = Io();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        int collections = GC.CollectionCount(0) - gen0;
        long reads = reads1 - reads0;
        long ioBytes = ioBytes1 - ioBytes0;

        Console.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "scenario={0,-12} ops={1} checksum={2} reps={3} " +
                "min_ms={4:F3} med_ms={5:F3} mean_ms={6:F3} us_per_op={7:F3} " +
                "bytes_per_op={8:F0} gen0={9} reads_per_op={10:F3} readbytes_per_op={11:F1}",
                scenario, ops, checksum, reps,
                sorted[0], sorted[reps / 2], ms.Average(), sorted[0] * 1000.0 / ops,
                (double)allocated / ops, collections,
                (double)reads / ops, (double)ioBytes / ops));

        if (Environment.GetEnvironmentVariable("BENCH_REPS_DETAIL") == "1")
        {
            Console.WriteLine(
                "    reps_ms=[" +
                string.Join(" ", ms.Select(m => m.ToString("F1", CultureInfo.InvariantCulture))) + "]");
        }
    }

    /// A file held entirely in memory, standing in for a perfect block cache.
    internal sealed class RamSource : IRandomAccessSource
    {
        private readonly byte[] data;

        public RamSource(string path) => data = File.ReadAllBytes(path);

        public long Length => data.Length;

        public int Read(long offset, Span<byte> buffer)
        {
            if (offset >= data.Length)
                return 0;

            int n = (int)Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }

        public void Dispose() { }
    }

    /// Opens each path once and keeps it, so the table and its index are both resident.
    internal sealed class RamFileSystem : IRandomAccessSourceFactory, ICompanionFileResolver
    {
        private readonly Dictionary<string, RamSource> sources = new(StringComparer.OrdinalIgnoreCase);

        public IRandomAccessSource Open(string path)
        {
            if (!sources.TryGetValue(path, out RamSource? source))
            {
                source = new RamSource(path);
                sources[path] = source;
            }

            return source;
        }

        // Same rule as the library's FileSystem: the obvious path, then a case-insensitive scan,
        // because CodeBase writes a lower-case extension beside an upper-case name.
        public string? Resolve(string tablePath, string extension)
        {
            string exact = Path.ChangeExtension(tablePath, extension);
            if (File.Exists(exact))
                return exact;

            string? directory = Path.GetDirectoryName(exact);
            string wanted = Path.GetFileName(exact);

            foreach (string candidate in Directory.EnumerateFiles(
                         string.IsNullOrEmpty(directory) ? "." : directory))
            {
                if (string.Equals(Path.GetFileName(candidate), wanted, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }
    }

    // Read operations the process has issued, straight from the OS. Not physical disk I/O -- a warm
    // file does none of that -- so this counts exactly the read syscalls a block cache would remove.
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr handle, out IoCounters counters);

    private static (long Reads, long Bytes) Io()
    {
        if (!GetProcessIoCounters(Process.GetCurrentProcess().Handle, out IoCounters c))
            return (-1, -1);

        return ((long)c.ReadOperationCount, (long)c.ReadTransferCount);
    }

    private static string[] LoadLines(string path)
    {
        string[] lines = File.ReadAllLines(path);

        if (lines.Length < Queries)
            throw new InvalidOperationException($"{path} holds {lines.Length} lines, expected {Queries}");

        return lines.Take(Queries).ToArray();
    }
}
