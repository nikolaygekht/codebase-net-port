using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// Refreshing a table: what it forgets, what it re-reads, and what it leaves alone.
///
/// Layer two, over files this test rewrites underneath the reader. That is the whole subject — a caller
/// who chose [c]BlockCacheMode.Always[/c] and wants to know whether the file moved — and it is
/// unreachable from the corpus, whose files never change while they are open.
///
/// The interesting half is not the record. It is that this port holds blocks in **two** places: the
/// engine's pool, and the block a parked tag cursor is sitting on, which is its own copy. Forgetting
/// only one of them would leave an indexed walk reading stale bytes while the record path read fresh
/// ones, which is worse than forgetting neither.
/// </summary>
[Trait("Layer", "Component")]
public sealed class TableRefreshTests
{
    [Fact]
    public void AWriteBehindTheCacheIsInvisibleUntilTheTableIsRefreshed()
    {
        using Rewritable files = new("CDXDEEP");
        using CodeBaseEngine engine = files.Engine();
        using Table table = engine.OpenTable(files.TablePath);

        FieldDefinition wide = table.Fields["K_WIDE"];
        table.Go(1).Should().Be(GoResult.Ok);
        string before = table.GetString(wide);

        // The first byte of the field itself, not of the record: RecordOffset counts the deletion
        // flag as offset zero, so a write at HeaderLength alone would land on that flag.
        files.Overwrite(files.TablePath, table.HeaderLength + wide.RecordOffset, (byte)'Z');

        table.Go(1);
        table.GetString(wide).Should().Be(before, "the cache still believes what it read");

        table.Refresh();

        table.GetString(wide).Should().NotBe(before, "the file changed and the cache was told");
        table.GetString(wide).Should().StartWith("Z");
    }

    [Fact]
    public void RefreshingLeavesTheCursorOnTheSameRecord()
    {
        using Rewritable files = new("CDXDEEP");
        using CodeBaseEngine engine = files.Engine();
        using Table table = engine.OpenTable(files.TablePath);

        table.Go(7).Should().Be(GoResult.Ok);
        int deleted = table.Deleted ? 1 : 0;

        table.Refresh();

        table.RecordNumber.Should().Be(7);
        table.Eof.Should().BeFalse();
        table.Bof.Should().BeFalse();
        (table.Deleted ? 1 : 0).Should().Be(deleted);
    }

    [Fact]
    public void RefreshingPastTheEndDoesNotTryToReadARecord()
    {
        using Rewritable files = new("CDXDEEP");
        using CodeBaseEngine engine = files.Engine();
        using Table table = engine.OpenTable(files.TablePath);

        table.Bottom();
        while (table.Skip(1) == SkipResult.Moved)
        {
        }

        table.Eof.Should().BeTrue();

        table.Refresh();

        table.Eof.Should().BeTrue("there is nothing at the end to re-read");
    }

    [Fact]
    public void AParkedCursorStopsBelievingItsOwnBlockToo()
    {
        // The half a pool-only invalidation would miss, and it has to be observed as a READ rather than
        // as a value. A cursor parked mid-leaf holds its own copy of that block, so stepping within the
        // leaf normally touches no file at all; after being told to forget, the same step must reach the
        // index again. Asserting on what the step *returns* proves nothing, because a re-descent to the
        // same place answers the same thing -- which is how the first version of this test managed to
        // pass while covering nothing.
        using Rewritable files = new("CDXDEEP");
        Counting reads = new();
        using CodeBaseEngine engine = new(reads, FileSystem.Instance)
        {
            BlockCache = BlockCacheMode.Always,
            DefaultEncoding = System.Text.Encoding.Latin1,
        };
        using Table table = engine.OpenTable(files.TablePath);

        table.SelectTag(table.Tags["D_WIDE"]);
        table.Top().Should().Be(GoResult.Ok);
        table.Skip(1).Should().Be(SkipResult.Moved);

        // Only the INDEX file's reads are the question. A tag-order walk visits scattered record
        // numbers, so the record's window misses most of the time however good the cache is; that says
        // nothing about the block the cursor is holding.
        reads.Reset();
        table.Skip(1).Should().Be(SkipResult.Moved);
        reads.Index.Should().Be(0, "the cursor holds the leaf, so stepping inside it reads no index");

        table.DropCachedBlocks().Should().BeGreaterThan(0, "the walk had put blocks in the cache");

        reads.Reset();
        table.Skip(1).Should().Be(SkipResult.Moved);
        reads.Index.Should().BeGreaterThan(
            0, "having forgotten its block, the cursor has to read the index again");
    }

    [Fact]
    public void AnIndexedWalkSurvivesADropMidWay()
    {
        // Dropping blocks under a walk must change nothing about where it goes, only how much it has to
        // re-read. Half the walk is done cached, the cache is emptied, and the rest must match a walk
        // that was never interrupted.
        using Rewritable files = new("CDXDEEP");
        using CodeBaseEngine engine = files.Engine();
        using Table table = engine.OpenTable(files.TablePath);

        List<int> whole = Walk(table, dropAfter: -1);
        List<int> interrupted = Walk(table, dropAfter: 100);

        interrupted.Should().Equal(whole);
    }

    [Fact]
    public void RefreshingAndDroppingRefuseOnAClosedTable()
    {
        using Rewritable files = new("CDXDEEP");
        using CodeBaseEngine engine = files.Engine();
        Table table = engine.OpenTable(files.TablePath);
        table.Dispose();

        Action refresh = table.Refresh;
        Action drop = () => table.DropCachedBlocks();

        refresh.Should().Throw<ObjectDisposedException>();
        drop.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void DroppingWithNoCacheIsHarmlessAndSaysNothingWasHeld()
    {
        using Rewritable files = new("CDXDEEP");
        using CodeBaseEngine engine = files.Engine(BlockCacheMode.Off);
        using Table table = engine.OpenTable(files.TablePath);

        table.Go(1);
        table.DropCachedBlocks().Should().Be(0);
        table.Refresh();
        table.RecordNumber.Should().Be(1);
    }

    /// <summary>
    /// Opens real files and counts the reads that reach them.
    /// </summary>
    private sealed class Counting : IRandomAccessSourceFactory
    {
        private int all;
        private int index;

        /// <summary>Gets how many reads reached any file since the last reset.</summary>
        public int Count => all;

        /// <summary>Gets how many reached the index file, which is the only one this test asks about.</summary>
        public int Index => index;

        /// <summary>Starts counting again.</summary>
        public void Reset()
        {
            all = 0;
            index = 0;
        }

        public IRandomAccessSource Open(string path) =>
            new Source(
                FileSystem.Instance.Open(path),
                this,
                Path.GetExtension(path).Equals(".cdx", StringComparison.OrdinalIgnoreCase));

        private sealed class Source(IRandomAccessSource inner, Counting counter, bool isIndex)
            : IRandomAccessSource
        {
            public long Length => inner.Length;

            public int Read(long offset, Span<byte> buffer)
            {
                counter.all++;
                if (isIndex)
                    counter.index++;

                return inner.Read(offset, buffer);
            }

            public void Dispose() => inner.Dispose();
        }
    }

    private static List<int> Walk(Table table, int dropAfter)
    {
        table.SelectTag(table.Tags["D_WIDE"]);
        List<int> seen = [];

        for (GoResult go = table.Top(); go == GoResult.Ok;)
        {
            seen.Add(table.RecordNumber);

            if (seen.Count == dropAfter)
                table.DropCachedBlocks();

            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        return seen;
    }

    /// <summary>
    /// A copy of a corpus table and its index, in a directory this test may rewrite.
    /// </summary>
    private sealed class Rewritable : IDisposable
    {
        /// <summary>Where the copy lives, which the test that made it opens.</summary>
        public string TablePath { get; }

        private readonly string directory;

        public Rewritable(string table)
        {
            directory = Path.Combine(Path.GetTempPath(), "codebase-refresh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            foreach (string extension in new[] { ".DBF", ".cdx", ".fpt" })
            {
                string from = Path.Combine(Corpus.Root, table + extension);
                if (File.Exists(from))
                    File.Copy(from, Path.Combine(directory, table + extension));
            }

            TablePath = Path.Combine(directory, table + ".DBF");
        }

        /// <summary>
        /// Opens an engine that caches everything, which is the only mode this subject exists for.
        /// </summary>
        public CodeBaseEngine Engine(BlockCacheMode mode = BlockCacheMode.Always) =>
            new() { BlockCache = mode, DefaultEncoding = System.Text.Encoding.Latin1 };

        /// <summary>
        /// Writes a byte over a file at an offset, as another process would.
        /// </summary>
        public void Overwrite(string path, int offset, byte value)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            stream.Seek(offset, SeekOrigin.Begin);
            stream.WriteByte(value);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A file still held open by a failed test is not this test's business to report.
            }
        }
    }
}
