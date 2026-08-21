using Microsoft.Win32.SafeHandles;

namespace CodeBase.Net.IO;

/// <summary>
/// Reads a file on disk.
///
/// The only place in this library that opens a file. Everything above it works through the
/// interface, which is what lets the rest be tested without a disk.
/// </summary>
internal sealed class FileRandomAccessSource : IRandomAccessSource, IExclusiveAccess
{
    private readonly SafeFileHandle handle;

    /// <summary>
    /// Opens a file for reading, leaving it readable and writable by others.
    /// </summary>
    /// <param name="path">The file to open.</param>
    public FileRandomAccessSource(string path)
    {
        // Read-only and shared: this library does not write yet, and holding an exclusive handle
        // would stop a table being read while another process has it open, which is normal for
        // these files. Locking arrives with the capability that needs it.
        handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    /// <inheritdoc/>
    /// <value>
    /// Always false: the handle above is opened [c]FileShare.ReadWrite[/c], so another process may be
    /// writing at any moment. It becomes answerable when locking gives this library a way to exclude
    /// one, and the cache's default mode starts caching by itself when it does.
    /// </value>
    public bool ExcludesOtherWriters => false;

    /// <inheritdoc/>
    public long Length => RandomAccess.GetLength(handle);

    /// <inheritdoc/>
    public int Read(long offset, Span<byte> buffer) => RandomAccess.Read(handle, buffer, offset);

    /// <inheritdoc/>
    public void Dispose() => handle.Dispose();
}
