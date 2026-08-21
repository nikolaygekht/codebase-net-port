namespace CodeBase.Net.IO;

/// <summary>
/// One file's reads, served out of memory where they can be.
///
/// A decorator, and that is the whole design: every read in this library goes through
/// [c]IRandomAccessSource[/c] — a record, an index node, a memo block alike — so caching at that one
/// door catches all of them. It is where the reference caches too: [c]file4readInternal[/c] branches
/// on [c]doBuffer[/c] *below* both the index and the table code ([c]f4file.c:2017-2093[/c]), which is
/// why [c]opt4fileRead[/c] serves tag blocks and record bytes from the same pool. An index-only cache
/// would miss the thing that matters most, because a tag walk's reads are 96% table records.
///
/// **The unit is a window, not a file block.** The file is divided into fixed windows from offset
/// zero, and a read is served window by window. Nothing here knows what a block means inside the file
/// — a table record straddles windows routinely, being at
/// [c]32 + headerLen + n * recordLength[/c] and aligned to nothing.
///
/// This class holds no bookkeeping of its own: which windows are where lives in the shared
/// [c]BlockCache[/c], because one pool serves every file and only the owner of both directions can
/// retire a slot without leaving another file pointing at it.
///
/// Not safe against concurrent use, like everything else built on a table.
/// </summary>
internal sealed class CachingRandomAccessSource : IRandomAccessSource
{
    private readonly IRandomAccessSource inner;
    private readonly BlockCache cache;
    private readonly BlockPriority priority;
    private readonly int owner;

    /// <summary>Held so a read does not allocate a delegate, which would defeat half the point.</summary>
    private readonly FillWindow fill;

    private bool disposed;

    /// <summary>
    /// Wraps a source so its reads are served from a cache.
    /// </summary>
    /// <param name="inner">The source to read on a miss, which this takes ownership of.</param>
    /// <param name="cache">Where windows are kept, shared with the engine's other files.</param>
    /// <param name="priority">What this file's windows are worth keeping.</param>
    public CachingRandomAccessSource(IRandomAccessSource inner, BlockCache cache, BlockPriority priority)
    {
        this.inner = inner;
        this.cache = cache;
        this.priority = priority;
        owner = cache.NewOwner();
        fill = inner.Read;
    }

    /// <inheritdoc/>
    public long Length => inner.Length;

    /// <inheritdoc/>
    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (buffer.Length == 0)
            return 0;

        int window = cache.WindowSize;
        long first = Align(offset, window);
        long last = Align(offset + buffer.Length - 1, window);

        // A request wider than the whole cache would evict everything it touched to answer once. The
        // reference declines the same way rather than emptying the pool for one read.
        if ((last - first) / window + 1 > cache.Capacity)
            return inner.Read(offset, buffer);

        int copied = 0;

        for (long start = first; start <= last; start += window)
        {
            int from = (int)Math.Max(0, offset - start);

            if (!cache.TryHold(owner, start, priority, fill, out int slot, out int valid))
            {
                // Either the file has nothing there, or every window is being read out of. Reading
                // the rest straight from the file is right in both cases: it answers correctly, and
                // it does not displace a window somebody is inside.
                return copied + inner.Read(offset + copied, buffer[copied..]);
            }

            try
            {
                if (from >= valid)
                    return copied;                      // the file ends inside this window

                int take = Math.Min(valid - from, buffer.Length - copied);
                cache.At(slot).AsSpan(from, take).CopyTo(buffer[copied..]);
                copied += take;

                if (valid < window)
                    return copied;                      // a short window is the end of the file
            }
            finally
            {
                cache.Release(slot);
            }
        }

        return copied;
    }

    /// <summary>
    /// Forgets every window of this file, so the next read of each reaches the file again.
    /// </summary>
    /// <returns>How many windows were given up.</returns>
    /// <remarks>
    /// [c]d4freeBlocks[/c] at the level this port has: the caller's answer to "I chose
    /// [c]BlockCacheMode.Always[/c] and now I want to know whether the file moved under me".
    /// </remarks>
    public int Invalidate() => cache.Invalidate(owner);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        cache.Invalidate(owner);
        inner.Dispose();
    }

    private static long Align(long position, int window) => position / window * window;
}
