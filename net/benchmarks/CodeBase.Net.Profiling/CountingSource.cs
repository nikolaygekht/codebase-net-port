using CodeBase.Net.IO;

namespace CodeBase.Net.Profiling;

/// <summary>
/// A file that counts the reads that reach it.
///
/// The one number that settles whether a cache is working, and no benchmark prints it. A cached arm
/// that still reaches the file thousands of times is not measuring a hit path at all, and the whole
/// reading of "a cache hit costs 730 nanoseconds" would then rest on a miss.
/// </summary>
internal sealed class CountingSource : IRandomAccessSource
{
    private readonly IRandomAccessSource inner;

    /// <summary>
    /// Wraps a source.
    /// </summary>
    /// <param name="inner">The file, which this takes ownership of.</param>
    public CountingSource(IRandomAccessSource inner) => this.inner = inner;

    /// <summary>Gets how many reads reached the file.</summary>
    public long Reads { get; private set; }

    /// <summary>Gets how many bytes those reads asked for.</summary>
    public long Bytes { get; private set; }

    /// <summary>Gets how many times the file was asked its length.</summary>
    /// <remarks>
    /// Counted separately because it is not a read and so does not appear in
    /// [c]GetProcessIoCounters[/c], which is how the audit counted syscalls — a metadata call was
    /// invisible to it.
    /// </remarks>
    public long LengthQueries { get; private set; }

    /// <inheritdoc/>
    public long Length
    {
        get
        {
            LengthQueries++;
            return inner.Length;
        }
    }

    /// <summary>
    /// Forgets the counts, so a warm-up is not charged to the run that follows it.
    /// </summary>
    public void Reset()
    {
        Reads = 0;
        Bytes = 0;
        LengthQueries = 0;
    }

    /// <inheritdoc/>
    public int Read(long offset, Span<byte> buffer)
    {
        Reads++;
        Bytes += buffer.Length;

        return inner.Read(offset, buffer);
    }

    /// <inheritdoc/>
    public void Dispose() => inner.Dispose();
}

/// <summary>
/// A factory that hands out counting sources over the real filesystem.
/// </summary>
internal sealed class CountingFileSystem : IRandomAccessSourceFactory
{
    private readonly List<CountingSource> opened = [];

    /// <summary>Gets how many reads reached any file this factory opened.</summary>
    public long Reads => opened.Sum(source => source.Reads);

    /// <summary>Gets how many times any file this factory opened was asked its length.</summary>
    public long LengthQueries => opened.Sum(source => source.LengthQueries);

    /// <inheritdoc/>
    public IRandomAccessSource Open(string path)
    {
        CountingSource source = new(FileSystem.Instance.Open(path));
        opened.Add(source);

        return source;
    }

    /// <summary>
    /// Forgets the counts of every file opened so far.
    /// </summary>
    public void Reset()
    {
        foreach (CountingSource source in opened)
            source.Reset();
    }
}

/// <summary>
/// A file whose length is asked for once.
///
/// The experiment behind the "one syscall per block read" reading: [c]FileRandomAccessSource.Length[/c]
/// is a live [c]RandomAccess.GetLength[/c] call, and [c]NodeReader.ReadAt[/c] reads it on every block
/// read to bounds-check the node. Nothing in the library caches it — the block cache caches reads.
/// Putting this under the block cache changes nothing else, so the difference it makes is the cost of
/// that call.
/// </summary>
internal sealed class FixedLengthSource : IRandomAccessSource
{
    private readonly IRandomAccessSource inner;

    /// <summary>
    /// Wraps a source, taking its length once.
    /// </summary>
    /// <param name="inner">The file, which this takes ownership of.</param>
    public FixedLengthSource(IRandomAccessSource inner)
    {
        this.inner = inner;
        Length = inner.Length;
    }

    /// <inheritdoc/>
    public long Length { get; }

    /// <inheritdoc/>
    public int Read(long offset, Span<byte> buffer) => inner.Read(offset, buffer);

    /// <inheritdoc/>
    public void Dispose() => inner.Dispose();
}
