using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// That using one table from two threads is reported rather than tolerated.
///
/// Layer three: hostile *use* rather than hostile input. No corpus file can express it, and no correct
/// caller can either — which is the whole reason it needs a test of its own. The port cannot make
/// concurrent use of a table safe, because the cursor is a protocol spanning several calls and nothing
/// inside one call could serialise that. What it can do is make breaking the contract loud, and the
/// ground rule about wrong record sets is what makes that worth doing: an exception is a better answer
/// than a record that looks right and belongs to somebody else.
/// </summary>
[Trait("Layer", "Fault")]
public sealed class ConcurrentUseDetectorTests
{
    [Fact]
    public void ASecondOperationArrivingMidFlightIsRefused()
    {
        // A source that blocks inside a read is what makes "mid-flight" a place a second thread can
        // reliably arrive at, rather than something to hope for by timing.
        using Gate gate = new();
        using CodeBaseEngine engine = new(new BlockingFiles(gate), FileSystem.Instance)
        {
            DefaultEncoding = System.Text.Encoding.Latin1,
        };
        using Table table = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));
        gate.Arm();

        Exception? refused = null;

        Thread intruder = new(() =>
        {
            gate.WaitUntilReadingStarted();

            try
            {
                table.Go(2);
            }
            catch (Exception thrown)
            {
                refused = thrown;
            }

            gate.LetTheReadFinish();
        });

        intruder.Start();
        table.Go(1);            // holds the table while the intruder tries to enter
        intruder.Join();

        refused.Should().BeOfType<CodeBaseException>();
        ((CodeBaseException)refused!).Code.Should().Be(ErrorCode.Info);
        refused.Message.Should().Contain("two threads at once");
    }

    [Fact]
    public void OneThreadMayReenterBecauseOperationsCallEachOther()
    {
        // Refresh calls DropCachedBlocks, and both are entry points. A detector that counted entries
        // without knowing whose they were would refuse the library's own call.
        using CodeBaseEngine engine = new() { DefaultEncoding = System.Text.Encoding.Latin1 };
        using Table table = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));

        table.Go(3);

        Action act = table.Refresh;

        act.Should().NotThrow();
        table.RecordNumber.Should().Be(3);
    }

    [Fact]
    public void TheTableStaysUsableAfterRefusingAnIntruder()
    {
        // A detector that left the table claimed would turn one misuse into a table nobody can use
        // again, which is a worse failure than the one it reports.
        using Gate gate = new();
        using CodeBaseEngine engine = new(new BlockingFiles(gate), FileSystem.Instance)
        {
            DefaultEncoding = System.Text.Encoding.Latin1,
        };
        using Table table = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));
        gate.Arm();

        Thread intruder = new(() =>
        {
            gate.WaitUntilReadingStarted();

            try
            {
                table.Top();
            }
            catch (CodeBaseException)
            {
                // expected, and the point of the next assertion is what happens afterwards
            }

            gate.LetTheReadFinish();
        });

        intruder.Start();
        table.Go(1);
        intruder.Join();

        table.Go(5).Should().Be(GoResult.Ok);
        table.RecordNumber.Should().Be(5);
        table.Top().Should().Be(GoResult.Ok);
    }

    [Fact]
    public void SequentialUseFromDifferentThreadsIsFine()
    {
        // Thread affinity is not what is forbidden -- concurrency is. This is the shape an await takes:
        // one operation, then another, on a different thread, never both at once.
        using CodeBaseEngine engine = new() { DefaultEncoding = System.Text.Encoding.Latin1 };
        using Table table = engine.OpenTable(Corpus.PathOf("CDXDEEP.DBF"));

        List<Exception> failures = [];

        for (int step = 1; step <= 8; step++)
        {
            int record = step;
            Thread one = new(() =>
            {
                try
                {
                    table.Go(record).Should().Be(GoResult.Ok);
                    table.RecordNumber.Should().Be(record);
                }
                catch (Exception failed)
                {
                    failures.Add(failed);
                }
            });

            one.Start();
            one.Join();
        }

        failures.Should().BeEmpty("passing a table from thread to thread is allowed");
    }

    /// <summary>
    /// Opens real files, with the first data read after arming held open.
    /// </summary>
    private sealed class BlockingFiles(Gate gate) : IRandomAccessSourceFactory
    {
        private int held;

        public IRandomAccessSource Open(string path) =>
            new Source(
                FileSystem.Instance.Open(path),
                gate,
                this,
                Path.GetExtension(path).Equals(".DBF", StringComparison.OrdinalIgnoreCase));

        private sealed class Source(
            IRandomAccessSource inner, Gate gate, BlockingFiles files, bool isData) : IRandomAccessSource
        {
            public long Length => inner.Length;

            public int Read(long offset, Span<byte> buffer)
            {
                if (isData && gate.Armed && Interlocked.Exchange(ref files.held, 1) == 0)
                    gate.HoldHere();

                return inner.Read(offset, buffer);
            }

            public void Dispose() => inner.Dispose();
        }
    }

    /// <summary>
    /// Lets a test hold a table's operation open while another thread tries to enter it.
    /// </summary>
    private sealed class Gate : IDisposable
    {
        private readonly ManualResetEventSlim reading = new(false);
        private readonly ManualResetEventSlim finished = new(false);

        /// <summary>Blocks until the table's own operation is under way.</summary>
        public void WaitUntilReadingStarted() => reading.Wait(TimeSpan.FromSeconds(5));

        /// <summary>Releases the table's operation.</summary>
        public void LetTheReadFinish() => finished.Set();

        /// <summary>Called from inside the read: announces it, then waits to be released.</summary>
        public void HoldHere()
        {
            reading.Set();
            finished.Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Starts holding the next read. Opening a table reads its header, and holding that would block
        /// before there is a table to misuse.
        /// </summary>
        public void Arm() => Armed = true;

        /// <summary>Gets a value indicating whether the next data read should be held.</summary>
        public bool Armed { get; private set; }

        public void Dispose()
        {
            reading.Dispose();
            finished.Dispose();
        }
    }
}
