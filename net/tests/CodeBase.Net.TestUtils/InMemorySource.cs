using CodeBase.Net.IO;

namespace CodeBase.Net.TestUtils;

/// <summary>
/// A file held in memory, usually the bytes of a real corpus table.
///
/// Preferred over a mock wherever a test needs [i]data[/i]: it is clearer than a stack of
/// expectations and it does not break when an unrelated call is added. Mocks earn their place for
/// behaviour that is hard to produce for real, which is a different set of tests. See
/// DEV_APPROACH.md section 5.
/// </summary>
internal sealed class InMemorySource : IRandomAccessSource
{
    private readonly byte[] bytes;

    /// <summary>
    /// Initializes a new source over some bytes.
    /// </summary>
    /// <param name="bytes">The file contents.</param>
    public InMemorySource(byte[] bytes) => this.bytes = bytes;

    /// <summary>
    /// Gets a value indicating whether the source has been closed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public long Length => bytes.Length;

    /// <inheritdoc/>
    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= bytes.Length)
            return 0;

        int available = Math.Min(buffer.Length, (int)(bytes.Length - offset));
        bytes.AsSpan((int)offset, available).CopyTo(buffer);
        return available;
    }

    /// <inheritdoc/>
    public void Dispose() => IsDisposed = true;
}
