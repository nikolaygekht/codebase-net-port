namespace CodeBase.Net.IO;

/// <summary>
/// Whether a file's blocks may be kept in memory between reads.
///
/// The reference's own three-valued setting, [c]cb-&gt;optimize[/c], with its names spelled out
/// ([c]f4opt.c:492-501[/c]). It is three-valued and not a switch for a reason: the middle value is a
/// **safety** rule rather than a convenience, and it is the one the reference ships.
/// </summary>
public enum BlockCacheMode
{
    /// <summary>
    /// Never keep a block, whatever the file is.
    /// </summary>
    /// <remarks>The reference's [c]OPT4OFF[/c].</remarks>
    Off = 0,

    /// <summary>
    /// Keep blocks only from a file opened so that nobody else can write it.
    /// </summary>
    /// <remarks>
    /// The reference's [c]OPT4EXCLUSIVE[/c] and its shipped default, which this library copies. A
    /// cached block is stale the moment another process writes the file, and xBase files are
    /// classically shared — so caching only what has been closed to other writers makes staleness
    /// *impossible* rather than merely unlikely.
    ///
    /// **Today this caches nothing**, because this library opens every file shared and has no way to
    /// exclude a writer until locking exists. It is the honest default all the same: it keeps the
    /// freshness guarantee the library has now, and it starts caching by itself, for the files where
    /// it is provably safe, when locking arrives.
    /// </remarks>
    WhenExclusive = 1,

    /// <summary>
    /// Keep blocks from every file.
    /// </summary>
    /// <remarks>
    /// The reference's [c]OPT4ALL[/c], and a **documented sharp edge**: on a file another process
    /// writes, a read can return bytes that were true when they were cached and are not true now. In
    /// xBase currency comes from locks, and this library has none yet, so a caller who asks for this
    /// is promising that nothing else is writing — or is prepared to call
    /// [c]DropCachedBlocks[/c] when something might have.
    /// </remarks>
    Always = 2,
}
