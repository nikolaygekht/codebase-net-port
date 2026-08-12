using CodeBase.Net.Cdx;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Golden;

/// <summary>
/// Shared helpers for the seek gate: opening a corpus index, and deciding from the recorded key sequence
/// what a seek ought to find.
///
/// The two lookups here are the *definitions* of the operations the C library does not have. They read
/// the dump's own key list, which the C library wrote, so an assertion built on them compares our
/// implementation against the reference's data even where it cannot compare against the reference's
/// behaviour.
/// </summary>
internal static class SeekCorpus
{
    /// <summary>
    /// Opens a corpus index file, supplying each machine-collated tag's pad byte from the dump.
    /// </summary>
    /// <param name="indexFile">The index file's name.</param>
    /// <param name="dump">Its dump, which records the pad byte the C library used.</param>
    /// <returns>The open index.</returns>
    public static IndexFileReader Open(string indexFile, CorpusIndexDump dump)
    {
        Dictionary<(string, int), byte> padBytes = [];

        foreach (DumpIndexTag tag in dump.RealTags)
            padBytes[(System.Text.Encoding.ASCII.GetString(tag.ExpressionBytes), tag.KeyLength)] = tag.PadByte;

        return IndexFileReader.Open(
            new InMemorySource(Corpus.ReadAllBytes(indexFile)),
            indexFile,
            header => padBytes[(header.Expression, header.KeyLength)]);
    }

    /// <summary>
    /// Gives the tag a dump section describes, the directory included.
    /// </summary>
    /// <param name="index">The open index file.</param>
    /// <param name="tag">The dumped tag.</param>
    /// <returns>The tag to drive.</returns>
    public static CdxTag TagOf(IndexFileReader index, DumpIndexTag tag) =>
        tag.IsDirectory ? index.Directory! : index.Tag(tag.Name);

    /// <summary>
    /// Finds where an entry sits in the recorded sequence.
    /// </summary>
    /// <param name="tag">The dumped tag.</param>
    /// <param name="entry">The entry a cursor is on.</param>
    /// <returns>Its position, counting from zero.</returns>
    /// <exception cref="InvalidOperationException">
    /// The cursor is on an entry the dump does not record, which would mean the walk and the seek
    /// disagree about what the tag holds.
    /// </exception>
    public static int PositionOf(DumpIndexTag tag, IndexEntry entry)
    {
        for (int i = 0; i < tag.Keys.Count; i++)
        {
            if (tag.Keys[i].Record == entry.Record && tag.Keys[i].Key.AsSpan().SequenceEqual(entry.Key))
                return i;
        }

        throw new InvalidOperationException(
            $"Tag {tag.Name} has no recorded entry for record {entry.Record}.");
    }

    /// <summary>
    /// Finds the first entry of the recorded sequence that is not less than a search value.
    /// </summary>
    /// <param name="tag">The dumped tag, whose keys are in the tag's own order.</param>
    /// <param name="search">The value to place.</param>
    /// <returns>Its position in the recorded sequence, or -1 when every entry sorts before it.</returns>
    /// <remarks>
    /// "Less" is in the tag's own order, so for a descending tag the comparison is inverted — which is
    /// how the operations themselves are defined, and the reason this helper takes the tag rather than
    /// just its keys.
    /// </remarks>
    public static int FirstNotLessThan(DumpIndexTag tag, KeySearch search)
    {
        for (int i = 0; i < tag.Keys.Count; i++)
        {
            if (NotBefore(tag, search, tag.Keys[i].Key))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Finds the last entry of the recorded sequence that is not greater than a search value.
    /// </summary>
    /// <param name="tag">The dumped tag, whose keys are in the tag's own order.</param>
    /// <param name="search">The value to place.</param>
    /// <returns>Its position in the recorded sequence, or -1 when every entry sorts after it.</returns>
    public static int LastNotGreaterThan(DumpIndexTag tag, KeySearch search)
    {
        for (int i = tag.Keys.Count - 1; i >= 0; i--)
        {
            if (NotAfter(tag, search, tag.Keys[i].Key))
                return i;
        }

        return -1;
    }

    /// <summary>Says whether a key is at or after the value, in the tag's order.</summary>
    private static bool NotBefore(DumpIndexTag tag, KeySearch search, byte[] key)
    {
        int comparison = search.Compare(key);
        return tag.Descending ? comparison <= 0 : comparison >= 0;
    }

    /// <summary>Says whether a key is at or before the value, in the tag's order.</summary>
    private static bool NotAfter(DumpIndexTag tag, KeySearch search, byte[] key)
    {
        int comparison = search.Compare(key);
        return tag.Descending ? comparison >= 0 : comparison <= 0;
    }
}
