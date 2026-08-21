namespace CodeBase.Net.IO;

/// <summary>
/// Opens files, and decides one file at a time whether its blocks may be kept.
///
/// The decision is made **at open**, from the mode as it stands then, which is what the reference does
/// — [c]file4optimizeLow[/c] runs from the open path ([c]f4opt.c:492-501[/c]). So changing the mode
/// affects files opened afterwards and not files already open, and that is documented rather than left
/// to be discovered.
///
/// The pool is built on the first open that qualifies and not before, so an engine whose mode never
/// permits caching — which is every engine today, the default being
/// [c]BlockCacheMode.WhenExclusive[/c] and no file being exclusive — allocates nothing at all.
/// </summary>
internal sealed class CachingSourceFactory : IRandomAccessSourceFactory
{
    private readonly IRandomAccessSourceFactory inner;
    private BlockCache? cache;

    /// <summary>
    /// Wraps a factory.
    /// </summary>
    /// <param name="inner">Where the files actually come from.</param>
    public CachingSourceFactory(IRandomAccessSourceFactory inner) => this.inner = inner;

    /// <summary>Gets or sets which files may have their blocks kept.</summary>
    public BlockCacheMode Mode { get; set; } = BlockCacheMode.WhenExclusive;

    /// <summary>Gets or sets how many windows may be held at once, zero meaning no pool at all.</summary>
    public int Windows { get; set; } = 2048;

    /// <summary>Gets or sets how many bytes one window is.</summary>
    public int WindowSize { get; set; } = BlockCacheWindow.Default;

    /// <summary>Gets the cache, or null when nothing has ever qualified.</summary>
    public BlockCache? Cache => cache;

    /// <inheritdoc/>
    public IRandomAccessSource Open(string path)
    {
        IRandomAccessSource source = inner.Open(path);

        if (!Permits(source))
            return source;

        cache ??= new BlockCache(new BlockPool(WindowSize, Windows));

        // Priority by what the file is, as the reference does: an index is worth keeping over a table
        // so that a scan cannot cost a query its tree. The finer split — an interior node above a leaf
        // — needs to know what a window holds, which nothing does at this level, so it waits for the
        // caller that parses the block.
        return new CachingRandomAccessSource(source, cache, PriorityFor(path));
    }

    /// <summary>
    /// Drops everything the cache believes, for every file.
    /// </summary>
    /// <returns>How many windows were given up.</returns>
    /// <remarks>[c]code4optSuspend[/c]: the whole pool, not one file's share of it.</remarks>
    public int InvalidateAll() => cache?.InvalidateAll() ?? 0;

    private static BlockPriority PriorityFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cdx" or ".idx" or ".mdx" => BlockPriority.IndexLow,
            ".dbf" => BlockPriority.DataLow,
            _ => BlockPriority.Other,
        };

    private bool Permits(IRandomAccessSource source)
    {
        if (Windows <= 0)
            return false;

        return Mode switch
        {
            BlockCacheMode.Off => false,
            BlockCacheMode.Always => true,

            // The reference's own rule, and the reason the middle value exists:
            // optFlag = (file->lowAccessMode != OPEN4DENY_NONE). A source that cannot say is shared.
            BlockCacheMode.WhenExclusive =>
                source is IExclusiveAccess { ExcludesOtherWriters: true },

            _ => false,
        };
    }
}

/// <summary>
/// The window size the cache uses when nothing says otherwise.
/// </summary>
internal static class BlockCacheWindow
{
    /// <summary>
    /// 512 bytes, which is a CDX node and the reference's own block size.
    /// </summary>
    /// <remarks>
    /// Whether a wider window pays — one read serving several adjacent nodes against the bytes it
    /// wastes on a random descent — is a measurement this step owes and not a choice to make here.
    /// </remarks>
    public const int Default = 512;
}
