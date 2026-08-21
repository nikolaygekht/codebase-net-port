using AwesomeAssertions;
using CodeBase.Net.IO;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// The cache against the source it decorates, over a counting fake instead of a disk.
///
/// Layer two, and what only it can catch: that a hit really is a hit, that a read straddling two
/// windows or ending inside one answers the same bytes as an uncached read, that a failed fetch leaves
/// nothing behind, and that **two files sharing one pool cannot be served each other's bytes** — which
/// is the one way this design can produce a wrong record set, and it is unreachable from any corpus
/// file because it needs eviction to happen at a chosen moment.
///
/// Every assertion is against what the uncached source answers, so nothing here is a hand-written
/// expectation.
/// </summary>
[Trait("Layer", "Component")]
public sealed class CachingSourceTests
{
    private const int Window = 512;

    [Fact]
    public void ASecondReadOfTheSameWindowDoesNotReachTheFile()
    {
        CountingSource file = new(Bytes(2000));
        using CachingRandomAccessSource cached = Cache(file, out _);

        byte[] first = Read(cached, 0, 64);
        int afterFirst = file.Reads;
        byte[] second = Read(cached, 100, 64);

        second.Should().Equal(Read(new CountingSource(file.Snapshot), 100, 64));
        file.Reads.Should().Be(afterFirst, "the window was already held");
        first.Should().Equal(Read(new CountingSource(file.Snapshot), 0, 64));
    }

    [Fact]
    public void AWindowIsReadOnceHoweverManyTimesItIsAskedFor()
    {
        CountingSource file = new(Bytes(Window));
        using CachingRandomAccessSource cached = Cache(file, out _);

        for (int i = 0; i < 50; i++)
            Read(cached, i, 8);

        file.Reads.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 512)]
    [InlineData(0, 513)]
    [InlineData(500, 24)]
    [InlineData(511, 2)]
    [InlineData(512, 512)]
    [InlineData(1, 1500)]
    [InlineData(1023, 1)]
    public void AReadAnswersWhatTheUncachedSourceAnswers(int offset, int length)
    {
        byte[] content = Bytes(2000);
        CountingSource file = new(content);
        using CachingRandomAccessSource cached = Cache(file, out _);

        // Twice: once cold, once warm, because a hit and a miss must not disagree.
        byte[] cold = Read(cached, offset, length);
        byte[] warm = Read(cached, offset, length);
        byte[] plain = Read(new CountingSource(content), offset, length);

        cold.Should().Equal(plain);
        warm.Should().Equal(plain);
    }

    [Fact]
    public void AnUnalignedReadIsRightAtEveryOffsetInAWindow()
    {
        // A record is at 32 + headerLen + n * recordLength and aligned to nothing, so this is the
        // ordinary case rather than an edge one.
        byte[] content = Bytes(3 * Window);
        CountingSource file = new(content);
        using CachingRandomAccessSource cached = Cache(file, out _);

        for (int offset = 0; offset < 2 * Window; offset++)
        {
            byte[] got = Read(cached, offset, 61);
            got.Should().Equal(Read(new CountingSource(content), offset, 61), $"offset {offset}");
        }
    }

    [Fact]
    public void AReadThatEndsInsideTheLastWindowStopsWhereTheFileDoes()
    {
        byte[] content = Bytes(Window + 100);
        CountingSource file = new(content);
        using CachingRandomAccessSource cached = Cache(file, out _);

        byte[] buffer = new byte[Window];
        int read = cached.Read(Window, buffer);

        read.Should().Be(100);
        buffer.AsSpan(0, 100).ToArray().Should().Equal(content.AsSpan(Window, 100).ToArray());
    }

    [Fact]
    public void AReadPastTheEndOfTheFileGivesNothing()
    {
        CountingSource file = new(Bytes(100));
        using CachingRandomAccessSource cached = Cache(file, out _);

        cached.Read(4096, new byte[16]).Should().Be(0);
    }

    [Fact]
    public void AReadWiderThanTheWholeCacheGoesStraightToTheFile()
    {
        // Otherwise one read would evict everything it touched to answer itself once.
        byte[] content = Bytes(8 * Window);
        CountingSource file = new(content);
        using CachingRandomAccessSource cached = Cache(file, out BlockCache cache, windows: 2);

        byte[] got = Read(cached, 0, 4 * Window);

        got.Should().Equal(Read(new CountingSource(content), 0, 4 * Window));
        cache.Holding.Should().Be(0, "nothing was cached to answer it");
    }

    [Fact]
    public void TwoFilesSharingAPoolAreNeverServedEachOthersBytes()
    {
        // The failure this design has to rule out. A pool of one slot, two files, alternating reads:
        // every read evicts the other file's window, so if a file's record of a slot outlived the
        // eviction it would be served the other file's bytes, silently and every time.
        byte[] left = Filled(Window, 0xAA);
        byte[] right = Filled(Window, 0xBB);

        BlockCache cache = new(new BlockPool(Window, 1));
        using CachingRandomAccessSource one = new(new CountingSource(left), cache, BlockPriority.DataLow);
        using CachingRandomAccessSource two = new(new CountingSource(right), cache, BlockPriority.DataLow);

        for (int round = 0; round < 20; round++)
        {
            Read(one, 0, 16).Should().AllSatisfy(b => b.Should().Be(0xAA), $"round {round}");
            Read(two, 0, 16).Should().AllSatisfy(b => b.Should().Be(0xBB), $"round {round}");
        }
    }

    [Fact]
    public void AFailedFetchLeavesNothingBehind()
    {
        // A half-filled window admitted as a full one would serve the bytes that never arrived as
        // zeros, and a header of zeros is a plausible-looking header.
        CountingSource file = new(Bytes(Window)) { FailFrom = 0 };
        using CachingRandomAccessSource cached = Cache(file, out BlockCache cache);

        Action act = () => Read(cached, 0, 16);

        act.Should().Throw<IOException>();
        cache.Holding.Should().Be(0);

        file.FailFrom = null;
        Read(cached, 0, 16).Should().Equal(Read(new CountingSource(file.Snapshot), 0, 16));
    }

    [Fact]
    public void AWriteBehindTheCacheIsInvisibleUntilTheCacheIsTold()
    {
        // What DropCachedBlocks is for, and the honest shape of BlockCacheMode.Always.
        CountingSource file = new(Filled(Window, 0x11));
        using CachingRandomAccessSource cached = Cache(file, out _);

        Read(cached, 0, 4).Should().AllSatisfy(b => b.Should().Be(0x11));

        file.Overwrite(Filled(Window, 0x22));
        Read(cached, 0, 4).Should().AllSatisfy(
            b => b.Should().Be(0x11), "the cache still believes what it read");

        cached.Invalidate().Should().Be(1);
        Read(cached, 0, 4).Should().AllSatisfy(b => b.Should().Be(0x22));
    }

    [Fact]
    public void DisposingTheSourceGivesItsWindowsBack()
    {
        BlockCache cache = new(new BlockPool(Window, 4));
        CachingRandomAccessSource cached = new(new CountingSource(Bytes(Window)), cache, BlockPriority.Other);

        Read(cached, 0, 8);
        cache.Holding.Should().Be(1);

        cached.Dispose();

        cache.Holding.Should().Be(0);
    }

    [Fact]
    public void ReadingAfterDisposeIsRefused()
    {
        CachingRandomAccessSource cached = Cache(new CountingSource(Bytes(64)), out _);
        cached.Dispose();

        Action act = () => cached.Read(0, new byte[4]);

        act.Should().Throw<ObjectDisposedException>();
    }

    private static CachingRandomAccessSource Cache(
        CountingSource file, out BlockCache cache, int windows = 8)
    {
        cache = new BlockCache(new BlockPool(Window, windows));
        return new CachingRandomAccessSource(file, cache, BlockPriority.DataLow);
    }

    private static byte[] Read(IRandomAccessSource source, long offset, int length)
    {
        byte[] buffer = new byte[length];
        int read = source.Read(offset, buffer);
        return buffer.AsSpan(0, read).ToArray();
    }

    private static byte[] Bytes(int count)
    {
        byte[] content = new byte[count];
        for (int i = 0; i < count; i++)
            content[i] = (byte)((i * 7) + (i / 251));

        return content;
    }

    private static byte[] Filled(int count, byte value)
    {
        byte[] content = new byte[count];
        Array.Fill(content, value);
        return content;
    }

    /// <summary>
    /// A file in memory that counts its reads, can be rewritten behind a reader's back, and can be
    /// told to fail.
    /// </summary>
    private sealed class CountingSource(byte[] content) : IRandomAccessSource
    {
        private byte[] bytes = content;

        /// <summary>Gets how many times the cache came here.</summary>
        public int Reads { get; private set; }

        /// <summary>Gets or sets an offset from which reads throw, for the fault-injection case.</summary>
        public long? FailFrom { get; set; }

        /// <summary>Gets the content as it stands, for building an uncached comparison.</summary>
        public byte[] Snapshot => bytes;

        public long Length => bytes.Length;

        public int Read(long offset, Span<byte> buffer)
        {
            Reads++;

            if (FailFrom is long from && offset >= from)
                throw new IOException($"refusing to read at {offset}");

            if (offset >= bytes.Length)
                return 0;

            int take = (int)Math.Min(buffer.Length, bytes.Length - offset);
            bytes.AsSpan((int)offset, take).CopyTo(buffer);
            return take;
        }

        /// <summary>Replaces the content, as another process writing the file would.</summary>
        public void Overwrite(byte[] replacement) => bytes = replacement;

        public void Dispose()
        {
        }
    }
}
