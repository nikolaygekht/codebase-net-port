using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Performance;

/// <summary>
/// What a seek allocates, as a ceiling rather than as a benchmark.
///
/// Layer five, and the half of it that needs no clock: allocation is exact, machine-independent, and
/// unaffected by how loaded the machine is or by whether the file is reached across a filesystem
/// bridge. The seek path is the port's hot path, and it allocated 3 956 bytes per seek while every
/// key was copied once per comparison — a cost that gets quietly re-added by anyone who reaches for
/// [c]EntryAt[/c] inside a descent, because doing so breaks no correctness test anywhere. That is the
/// whole reason this file exists: the mistake it guards against is invisible to every other layer.
/// The same is true of a message formatted on the way down into a read: it is discarded on every read
/// that succeeds, and no other test can see it happen.
///
/// Deliberately a ceiling and not an equality. It is a tripwire for a regression most of a kilobyte
/// wide, not a pin on an implementation detail: adding a field to a search value must not fail a
/// test, and re-introducing a per-comparison copy must.
/// </summary>
[Trait("Layer", "Performance")]
public sealed class SeekAllocationTests
{
    /// <summary>Seeks per measurement, enough that a one-off allocation cannot dominate.</summary>
    private const int Seeks = 500;

    /// <summary>Untimed seeks before the counter is read, so nothing one-off is counted per seek.</summary>
    private const int WarmUp = 50;

    /// <summary>Keys read out of the table, which is more than the descent needs and bounds the read.</summary>
    private const int Keys = 600;

    /// <summary>
    /// Bytes a seek over the four-level character tag may allocate.
    ///
    /// Three measurements sit behind it, the first two in
    /// claude/dev/009-performance-audit/results-compare-in-place.txt: **3 956** bytes per seek while
    /// every compared key was copied, **2 760** once they were not, and **2 568** once the shared
    /// reader stopped being handed a description of what it was reading. 2 700 leaves the current
    /// figure five per cent of room, which is enough because there is no variance to leave room for:
    /// Debug and Release measure the same bytes, and so does every machine. That is the property that
    /// makes this a gate rather than a benchmark.
    ///
    /// It is deliberately tight enough to catch the smaller of the two regressions it guards. A
    /// per-comparison key copy would add most of a kilobyte and be caught by any ceiling; formatting
    /// a message per block read costs 56 bytes a level and would fit under a looser one.
    /// </summary>
    private const long CharacterSeekCeiling = 2700;

    /// <summary>
    /// Bytes a seek over the three-level numeric tag may allocate, on the same reasoning.
    ///
    /// Its own number because its tree is a level shallower and its key less than half the width, so
    /// it copied less to begin with: 3 808 bytes, then 2 128, now **1 984**.
    /// </summary>
    private const long NumericSeekCeiling = 2100;

    [Fact]
    public void ASeekOverACharacterTagStaysUnderItsCeiling()
    {
        using CodeBaseEngine engine = NewEngine();
        using Table table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));

        string[] names = CharacterKeys(table);
        table.SelectTag(table.Tags["T_NAME"]);

        long perSeek = PerOperation(i => table.Seek(names[i % names.Length]));

        perSeek.Should().BeLessThan(
            CharacterSeekCeiling,
            "a per-comparison key copy in the descent would put this back over 3 900 bytes");
    }

    [Fact]
    public void ASeekOverANumericTagStaysUnderItsCeiling()
    {
        using CodeBaseEngine engine = NewEngine();
        using Table table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));

        double[] amounts = NumericKeys(table);
        table.SelectTag(table.Tags["T_AMT"]);

        long perSeek = PerOperation(i => table.Seek(amounts[i % amounts.Length]));

        perSeek.Should().BeLessThan(NumericSeekCeiling);
    }

    [Fact]
    public void EveryMeasuredSeekActuallyFindsItsRecord()
    {
        // Without this, the ceilings could be met by a seek that failed early and allocated nothing.
        // The keys come out of the table, so every one of them is there to be found.
        using CodeBaseEngine engine = NewEngine();
        using Table table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));

        string[] names = CharacterKeys(table);
        double[] amounts = NumericKeys(table);

        table.SelectTag(table.Tags["T_NAME"]);
        foreach (string name in names)
            table.Seek(name).Should().Be(GoResult.Ok, name);

        table.SelectTag(table.Tags["T_AMT"]);
        foreach (double amount in amounts)
            table.Seek(amount).Should().Be(GoResult.Ok, $"{amount}");
    }

    /// <summary>
    /// Opens an engine that needs no encoding provider registered in the process.
    /// </summary>
    /// <returns>The engine, which the caller owns.</returns>
    /// <remarks>
    /// PERF10K names no code page, and the library reads an unmarked table as cp437, which no runtime
    /// supplies unregistered. Registering a provider is the host's job and a process-wide side effect
    /// (ADR-17), and layers one to three do not do it — so this asks for the encoding directly, which
    /// is the other thing the library documents. The table's text is ASCII, so Latin1 reproduces its
    /// bytes exactly and a key sought is a key stored.
    /// </remarks>
    private static CodeBaseEngine NewEngine() => new() { DefaultEncoding = Encoding.Latin1 };

    /// <summary>
    /// Runs an operation many times and gives the bytes it allocated per run.
    /// </summary>
    /// <param name="operation">What to measure, given the iteration number.</param>
    /// <returns>Allocated bytes divided by the number of runs.</returns>
    private static long PerOperation(Action<int> operation)
    {
        // Everything that allocates once — the tag's cursor, its search buffer, the JIT's own
        // work — happens before the counter is read, or it would be charged to every seek.
        for (int i = 0; i < WarmUp; i++)
            operation(i);

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < Seeks; i++)
            operation(i);

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Seeks;
    }

    /// <summary>
    /// Reads character keys out of the table, so no expected value is invented.
    /// </summary>
    private static string[] CharacterKeys(Table table)
    {
        FieldDefinition name = table.Fields["NAME"];
        List<string> keys = [];

        table.SelectTag(table.Tags["T_NAME"]);
        for (GoResult go = table.Top(); go == GoResult.Ok && keys.Count < Keys;)
        {
            keys.Add(table.GetString(name));
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return [.. keys];
    }

    /// <summary>
    /// Reads numeric keys out of the same table.
    /// </summary>
    private static double[] NumericKeys(Table table)
    {
        FieldDefinition amount = table.Fields["AMOUNT"];
        List<double> keys = [];

        table.SelectTag(table.Tags["T_AMT"]);
        for (GoResult go = table.Top(); go == GoResult.Ok && keys.Count < Keys;)
        {
            keys.Add(table.GetDouble(amount));
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return [.. keys];
    }
}
