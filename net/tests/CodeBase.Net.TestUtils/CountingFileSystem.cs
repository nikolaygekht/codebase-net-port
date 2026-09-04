using CodeBase.Net.IO;

namespace CodeBase.Net.TestUtils;

/// <summary>
/// The real filesystem, counting the reads that reach each file.
///
/// The one measurement that says whether an optimizer optimized. Every check of a record set says
/// only that the answer was right, and an optimizer that seeks nothing and reads the whole table is
/// perfectly right -- so what has to be counted is the work, and the honest place to count it is
/// where the work actually happens.
///
/// Counted per file rather than in total, because the interesting figures separate: a query that
/// answers a count from its index alone reads the index and must not touch the table at all, and
/// saying so needs the two apart.
/// </summary>
internal sealed class CountingFileSystem : IRandomAccessSourceFactory, ICompanionFileResolver
{
    private readonly Dictionary<string, long> reads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets how many reads have reached a file since the last reset.
    /// </summary>
    /// <param name="fileName">The file's name, without its directory.</param>
    /// <returns>The count.</returns>
    public long ReadsOf(string fileName) => reads.TryGetValue(fileName, out long count) ? count : 0;

    /// <summary>
    /// Gets how many reads have reached every file since the last reset.
    /// </summary>
    public long TotalReads => reads.Values.Sum();

    /// <summary>
    /// Forgets what has been counted, so a measurement can start after a table is open.
    /// </summary>
    public void Reset() => reads.Clear();

    /// <inheritdoc/>
    public IRandomAccessSource Open(string path) =>
        new Counted(FileSystem.Instance.Open(path), Path.GetFileName(path), this);

    /// <inheritdoc/>
    public string? Resolve(string tablePath, string extension) =>
        FileSystem.Instance.Resolve(tablePath, extension);

    private void Count(string fileName) =>
        reads[fileName] = reads.TryGetValue(fileName, out long count) ? count + 1 : 1;

    private sealed class Counted(IRandomAccessSource inner, string fileName, CountingFileSystem owner)
        : IRandomAccessSource
    {
        public long Length => inner.Length;

        public int Read(long position, Span<byte> into)
        {
            owner.Count(fileName);
            return inner.Read(position, into);
        }

        public void Dispose() => inner.Dispose();
    }
}
