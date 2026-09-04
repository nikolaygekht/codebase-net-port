using CodeBase.Net.Cdx;
using CodeBase.Net.Queries;

namespace CodeBase.Net.TestUtils;

/// <summary>
/// A tag's keys as a sorted list in memory, standing in for a real one.
///
/// A hand-written stand-in is a second implementation of a contract, and nothing makes it agree with
/// the first by itself, so it is held to the same promises by a shared contract test. What it buys is
/// that the whole of leaf evaluation -- the seek positions, the range walk, the exclusions, the empty
/// range -- can be exercised over keys chosen to sit exactly on a boundary, which a corpus tag only
/// obliges by accident.
///
/// Keys are held in stored order, which is ascending whatever a tag's traversal flag says, and the
/// list is sorted on construction so a caller cannot make it disagree with a real tag by handing them
/// over in the wrong order.
/// </summary>
internal sealed class SortedKeyProbe : ITagProbe
{
    private readonly List<IndexEntry> entries;
    private readonly byte padByte;
    private int at = -1;

    /// <summary>
    /// Builds a probe over a list of keys.
    /// </summary>
    /// <param name="keyLength">How many bytes each key occupies.</param>
    /// <param name="padByte">What a key shorter than the width is filled with.</param>
    /// <param name="entries">The keys and the records they point at, in any order.</param>
    internal SortedKeyProbe(int keyLength, byte padByte, IEnumerable<IndexEntry> entries)
    {
        KeyLength = keyLength;
        this.padByte = padByte;
        this.entries =
        [
            .. entries.OrderBy(entry => entry.Key, ByteOrder.Instance).ThenBy(entry => entry.Record),
        ];
    }

    /// <summary>
    /// Builds a probe over text keys, one record per key in the order given.
    /// </summary>
    /// <param name="keyLength">How many bytes each key occupies.</param>
    /// <param name="keys">The key text, blank-padded to the width.</param>
    /// <returns>The probe, whose records number from one in the order the keys were given.</returns>
    internal static SortedKeyProbe OfText(int keyLength, params string[] keys) =>
        new(keyLength, (byte)' ', keys.Select((key, index) => new IndexEntry(
            System.Text.Encoding.Latin1.GetBytes(key.PadRight(keyLength)), (uint)(index + 1))));

    /// <inheritdoc/>
    public int KeyLength { get; }

    /// <inheritdoc/>
    public IndexEntry Current => entries[at];

    /// <inheritdoc/>
    public KeySearch KeyFor(ConstraintValue value)
    {
        byte[] into = new byte[KeyLength];
        int length = Math.Min(value.Length, KeyLength);

        value.Bytes[..length].CopyTo(into);
        return KeySearch.Into(into, length, KeyLength, padByte);
    }

    /// <inheritdoc/>
    public bool First()
    {
        at = entries.Count == 0 ? -1 : 0;
        return at >= 0;
    }

    /// <inheritdoc/>
    public bool SeekAtOrAfter(KeySearch key)
    {
        for (int index = 0; index < entries.Count; index++)
        {
            if (key.Compare(entries[index].Key) >= 0)
            {
                at = index;
                return true;
            }
        }

        at = -1;
        return false;
    }

    /// <inheritdoc/>
    public bool Step()
    {
        if (at < 0 || at + 1 >= entries.Count)
            return false;

        at++;
        return true;
    }

    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static ByteOrder Instance { get; } = new();

        public int Compare(byte[]? left, byte[]? right) =>
            left.AsSpan().SequenceCompareTo(right.AsSpan());
    }
}
