using AwesomeAssertions;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.IO;

/// <summary>
/// The three-valued policy, as a truth table.
///
/// Layer one, and the only place [c]BlockCacheMode.WhenExclusive[/c] can be proved at all: no file this
/// library opens excludes another writer, so the middle value's caching branch is unreachable from any
/// real file and would otherwise ship untested. A fake that claims to be exclusive is what makes it
/// provable now rather than whenever locking arrives.
///
/// The rule being ported is one line of the reference:
/// [c]optFlag = ( file-&gt;lowAccessMode != OPEN4DENY_NONE ) ? 1 : 0[/c] ([c]f4opt.c:492-501[/c]).
/// </summary>
[Trait("Layer", "Unit")]
public sealed class BlockCacheModeTests
{
    [Theory]
    [InlineData(BlockCacheMode.Off, false, false)]
    [InlineData(BlockCacheMode.Off, true, false)]
    [InlineData(BlockCacheMode.WhenExclusive, false, false)]
    [InlineData(BlockCacheMode.WhenExclusive, true, true)]
    [InlineData(BlockCacheMode.Always, false, true)]
    [InlineData(BlockCacheMode.Always, true, true)]
    public void ThePolicyDecidesByModeAndAccess(BlockCacheMode mode, bool exclusive, bool cached)
    {
        CachingSourceFactory factory = new(new Files(exclusive)) { Mode = mode };

        using IRandomAccessSource source = factory.Open("t.dbf");

        (source is CachingRandomAccessSource).Should().Be(cached);
    }

    [Fact]
    public void TheDefaultIsTheReferencesOwnAndCachesNothingToday()
    {
        // Both halves matter: that the default is WhenExclusive, and that a source of the kind this
        // library actually opens is not cached under it.
        CachingSourceFactory factory = new(new Files(exclusive: false));

        factory.Mode.Should().Be(BlockCacheMode.WhenExclusive);

        using IRandomAccessSource source = factory.Open("t.dbf");

        source.Should().NotBeOfType<CachingRandomAccessSource>();
        factory.Cache.Should().BeNull("nothing qualified, so no pool was ever allocated");
    }

    [Fact]
    public void TheRealFileSourceReportsItselfShared()
    {
        // The reason the default caches nothing: the handle is opened FileShare.ReadWrite because these
        // files are normally open elsewhere. When locking can exclude a writer, this answer changes and
        // the default starts caching by itself.
        using FileRandomAccessSource source = new(Corpus.PathOf("DB3TYPE.DBF"));

        source.ExcludesOtherWriters.Should().BeFalse();
    }

    [Fact]
    public void NoPoolIsAllocatedUntilSomethingQualifies()
    {
        CachingSourceFactory factory = new(new Files(exclusive: false)) { Mode = BlockCacheMode.Always };

        factory.Cache.Should().BeNull();

        using IRandomAccessSource source = factory.Open("t.dbf");

        factory.Cache.Should().NotBeNull("the first qualifying open builds it");
    }

    [Fact]
    public void AZeroSizedCacheNeverCachesWhateverTheModeSays()
    {
        CachingSourceFactory factory = new(new Files(exclusive: true))
        {
            Mode = BlockCacheMode.Always,
            Windows = 0,
        };

        using IRandomAccessSource source = factory.Open("t.dbf");

        source.Should().NotBeOfType<CachingRandomAccessSource>();
        factory.Cache.Should().BeNull();
    }

    [Fact]
    public void TheDecisionIsTakenWhenTheFileIsOpenedAndNotAfterwards()
    {
        // As file4optimizeLow does, from the open path. Changing the mode later affects the next file,
        // not the one already open, and that is documented rather than left to be discovered.
        CachingSourceFactory factory = new(new Files(exclusive: false)) { Mode = BlockCacheMode.Off };

        using IRandomAccessSource before = factory.Open("t.dbf");
        factory.Mode = BlockCacheMode.Always;
        using IRandomAccessSource after = factory.Open("t.dbf");

        before.Should().NotBeOfType<CachingRandomAccessSource>("it was opened under Off");
        after.Should().BeOfType<CachingRandomAccessSource>("it was opened under Always");
    }

    [Fact]
    public void AnIndexsWindowsOutliveAScanOverATable()
    {
        // The priority split, end to end through the factory rather than at the pool: the extension
        // decides what a file's windows are worth, so a table scan cannot cost a query its tree.
        CachingSourceFactory factory = new(new Files(exclusive: false))
        {
            Mode = BlockCacheMode.Always,
            Windows = 4,
        };

        using IRandomAccessSource index = factory.Open("t.cdx");
        using IRandomAccessSource data = factory.Open("t.dbf");
        byte[] buffer = new byte[16];

        index.Read(0, buffer);
        int afterIndex = factory.Cache!.Misses;

        // Far more table windows than the cache can hold, so something must be given up every time.
        for (int window = 0; window < 40; window++)
            data.Read(window * 512, buffer);

        index.Read(0, buffer);

        factory.Cache!.Misses.Should().Be(
            afterIndex + 40,
            "the index window was still held, so re-reading it cost no read");
    }

    /// <summary>
    /// Opens in-memory files that say whether they exclude other writers.
    /// </summary>
    private sealed class Files(bool exclusive) : IRandomAccessSourceFactory
    {
        public IRandomAccessSource Open(string path) => new Source(exclusive);

        private sealed class Source(bool exclusive) : IRandomAccessSource, IExclusiveAccess
        {
            private readonly byte[] bytes = new byte[64 * 1024];

            public bool ExcludesOtherWriters => exclusive;

            public long Length => bytes.Length;

            public int Read(long offset, Span<byte> buffer)
            {
                if (offset >= bytes.Length)
                    return 0;

                int take = (int)Math.Min(buffer.Length, bytes.Length - offset);
                bytes.AsSpan((int)offset, take).CopyTo(buffer);
                return take;
            }

            public void Dispose()
            {
            }
        }
    }
}
