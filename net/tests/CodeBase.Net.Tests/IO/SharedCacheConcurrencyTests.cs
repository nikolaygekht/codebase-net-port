using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// Two tables of one engine, read from several threads at once.
///
/// Layer two, and it is the scenario the threading contract deliberately does **not** forbid. A table
/// and its cursors are the caller's to serialise; two *different* tables are not, and opening two and
/// querying them in parallel is an ordinary thing to do. They meet at the engine's shared cache, which
/// is the one place in this library that locks.
///
/// What this catches that nothing else can: the shared bookkeeping torn by two threads — a slot retired
/// by one while the other is registering it, two windows claiming one slot, a pin count that drifts. The
/// symptom would be a reader served another file's bytes, which is why the assertion is that every
/// thread's results equal what one thread alone reads.
///
/// A stress test rather than a proof. It cannot show the absence of a race, and it is not claimed to:
/// what it does is fail loudly on the races that are actually reachable, which is more than nothing and
/// is what the C library's own suite has for the same code.
/// </summary>
[Trait("Layer", "Component")]
public sealed class SharedCacheConcurrencyTests
{
    /// <summary>Readers per table, so several threads contend on every operation.</summary>
    private const int Readers = 4;

    /// <summary>
    /// Passes per reader: enough to make an interleaving likely rather than lucky, and no more. Verified
    /// by removing the lock and watching this class fail, which is the only thing that says the number
    /// is high enough.
    /// </summary>
    private const int Passes = 6;

    [Fact]
    public void TwoTablesOfOneEngineReadTheSameThingUnderContentionAsAlone()
    {
        string[] tables = ["CDXDEEP", "CDXBASE"];

        // What one thread reads, with the same engine and the same cache, is the answer every thread
        // must get. Taken first and alone, so contention cannot influence the expectation.
        Dictionary<string, string> alone = [];
        using (CodeBaseEngine quiet = Engine())
        {
            foreach (string name in tables)
            {
                using Table table = quiet.OpenTable(Corpus.PathOf(name + ".DBF"));
                alone[name] = Read(table);
            }
        }

        using CodeBaseEngine engine = Engine();
        Table[] open = [.. tables.Select(name => engine.OpenTable(Corpus.PathOf(name + ".DBF")))];
        List<Exception> failures = [];
        List<Thread> threads = [];

        for (int reader = 0; reader < Readers; reader++)
        {
            foreach ((string name, Table table) in tables.Zip(open))
            {
                // One thread per table per reader. Each table is touched by several threads, which the
                // contract forbids for a *table* -- so each thread gets its own, and what they share is
                // the engine's cache, which is exactly what is under test.
                string path = Corpus.PathOf(name + ".DBF");
                string expected = alone[name];

                threads.Add(new Thread(() =>
                {
                    try
                    {
                        for (int pass = 0; pass < Passes; pass++)
                        {
                            using Table mine = engine.OpenTable(path);
                            Read(mine).Should().Be(expected, $"{name} pass {pass}");
                        }
                    }
                    catch (Exception failed)
                    {
                        lock (failures)
                        {
                            failures.Add(failed);
                        }
                    }
                }));
            }
        }

        threads.ForEach(thread => thread.Start());
        threads.ForEach(thread => thread.Join());

        foreach (Table table in open)
            table.Dispose();

        failures.Should().BeEmpty(
            "every reader must see what a lone reader sees: {0}",
            failures.Count > 0 ? failures[0].Message : string.Empty);
    }

    [Fact]
    public void DroppingBlocksWhileOthersAreReadingChangesNoAnswer()
    {
        // Invalidation is the operation most likely to tear the bookkeeping, because it removes entries
        // while other threads are claiming and releasing them.
        using CodeBaseEngine engine = Engine();
        using Table first = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));
        string expected = Read(first);

        List<Exception> failures = [];
        List<Thread> threads = [];

        for (int reader = 0; reader < Readers; reader++)
        {
            threads.Add(new Thread(() =>
            {
                try
                {
                    for (int pass = 0; pass < Passes; pass++)
                    {
                        using Table mine = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));
                        Read(mine).Should().Be(expected, $"pass {pass}");
                    }
                }
                catch (Exception failed)
                {
                    lock (failures)
                    {
                        failures.Add(failed);
                    }
                }
            }));
        }

        Thread dropper = new(() =>
        {
            for (int pass = 0; pass < Passes * 4; pass++)
            {
                engine.DropCachedBlocks();
                Thread.Yield();
            }
        });

        threads.ForEach(thread => thread.Start());
        dropper.Start();
        threads.ForEach(thread => thread.Join());
        dropper.Join();

        failures.Should().BeEmpty(
            "dropping blocks under readers must cost re-reads and nothing else: {0}",
            failures.Count > 0 ? failures[0].Message : string.Empty);
    }

    private static CodeBaseEngine Engine() => new()
    {
        BlockCache = BlockCacheMode.Always,
        DefaultEncoding = System.Text.Encoding.Latin1,

        // Small on purpose: eviction is where the shared bookkeeping is written, so a cache too big to
        // evict would leave the interesting half of this class untested.
        BlockCacheBlocks = 16,
    };

    /// <summary>
    /// Reads a table whole, through its tags as well as in record order, as one comparable string.
    /// </summary>
    private static string Read(Table table)
    {
        List<string> parts = [];

        // One tag, not all of them: contention is what is being tested, and a second tag multiplies the
        // running time without adding an interleaving the first does not already produce.
        if (table.Tags.Count > 0)
        {
            table.SelectTag(table.Tags[0]);
            parts.Add($"--{table.Tags[0].Name}");

            for (GoResult go = table.Top(); go == GoResult.Ok;)
            {
                parts.Add(Convert.ToHexString(table.GetRawBytes(table.Fields[0])));
                if (table.Skip(1) != SkipResult.Moved)
                    break;
            }
        }

        table.SelectTag(null);
        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            parts.Add($"#{table.RecordNumber}:{Convert.ToHexString(table.GetRawBytes(table.Fields[0]))}");
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return string.Join('|', parts);
    }
}
