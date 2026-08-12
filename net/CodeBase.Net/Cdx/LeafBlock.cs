namespace CodeBase.Net.Cdx;

/// <summary>
/// A leaf block, and the reconstruction of the keys packed into it.
///
/// This is the compressed half of the format and the riskiest code in the port. A leaf stores, for
/// each entry, only the bytes that are neither shared with the previous key nor trailing pad; the
/// entry array says how many of each were dropped. The stored bytes themselves grow downwards from
/// the end of the block, so entry zero's bytes sit highest and each following entry's sit just below.
///
/// Reconstruction is therefore sequential: walk from entry zero keeping a key buffer and a position
/// in the text, and each entry overwrites the buffer from its shared prefix onwards. Asking for one
/// key means building every key before it, exactly as the C library does.
///
/// Governing specification: CDX-FORMAT.md section 6.3.
/// </summary>
internal sealed class LeafBlock
{
    private readonly byte[] block;
    private readonly int keyLength;
    private readonly byte padByte;
    private readonly uint node;
    private readonly byte[] key;

    /// <summary>How many entries of the key buffer are currently valid.</summary>
    private int builtIndex = -1;

    /// <summary>Where the next entry's stored bytes end, counting down from the block's end.</summary>
    private int textEnd;

    private LeafBlock(byte[] block, NodeHeader header, LeafGeometry geometry, int keyLength, byte padByte, uint node)
    {
        this.block = block;
        this.keyLength = keyLength;
        this.padByte = padByte;
        this.node = node;
        Header = header;
        Geometry = geometry;
        key = new byte[keyLength];
        textEnd = block.Length;
    }

    /// <summary>Gets the block's common header.</summary>
    public NodeHeader Header { get; }

    /// <summary>Gets how the block packs its entries.</summary>
    public LeafGeometry Geometry { get; }

    /// <summary>Gets the number of keys in the block.</summary>
    public int Count => Header.KeyCount;

    /// <summary>
    /// Reads a leaf block.
    /// </summary>
    /// <param name="block">The block, its whole length.</param>
    /// <param name="header">The block's already-decoded common header.</param>
    /// <param name="keyLength">The tag's key length.</param>
    /// <param name="padByte">The byte a trailing-pad count stands for.</param>
    /// <param name="node">The node the block came from, for messages.</param>
    /// <returns>The block, ready to be asked for keys.</returns>
    /// <exception cref="CodeBaseException">
    /// The geometry is unreadable, or the entry array would not fit in the block alongside its key
    /// text.
    /// </exception>
    public static LeafBlock Parse(byte[] block, NodeHeader header, int keyLength, byte padByte, uint node)
    {
        LeafGeometry geometry = LeafGeometry.Parse(block, node);

        // The entry array and the key text grow towards each other, so the array alone overflowing
        // the block means the count and the geometry disagree. Checked before any entry is unpacked,
        // because unpacking reads InfoLength bytes per entry and would otherwise read past the block.
        long arrayEnd = (long)LeafGeometry.InfoArrayOffset + ((long)header.KeyCount * geometry.InfoLength);
        if (arrayEnd > block.Length)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Leaf node {node} holds {header.KeyCount} entries of {geometry.InfoLength} bytes, " +
                $"which runs {arrayEnd - block.Length} bytes past the block.");
        }

        return new LeafBlock(block, header, geometry, keyLength, padByte, node);
    }

    /// <summary>
    /// Gives the entry at a position, its key rebuilt.
    /// </summary>
    /// <param name="index">Which entry, counting from zero.</param>
    /// <returns>
    /// The key and the record number. The key is a fresh array, so a caller may keep it; the block
    /// reuses its own buffer between calls.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The block has no entry at that position.</exception>
    /// <exception cref="CodeBaseException">
    /// The entry's counts leave a negative number of stored bytes, or its bytes lie outside the
    /// block. Both mean the block is corrupt, and both are refusals rather than a key made of
    /// whatever was nearby.
    /// </exception>
    public IndexEntry EntryAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        // Rebuilding always moves forwards, so going back means starting over. The C library keeps
        // exactly this state (b4block.c:1900-1910) and it is what makes a full walk linear.
        if (builtIndex > index)
        {
            builtIndex = -1;
            textEnd = block.Length;
        }

        while (builtIndex < index)
        {
            builtIndex++;
            Build(builtIndex);
        }

        return new IndexEntry(key.AsSpan().ToArray(), Geometry.Unpack(block, index).Record);
    }

    /// <summary>
    /// Finds the first entry whose key is not less than a search value.
    /// </summary>
    /// <param name="search">What to look for.</param>
    /// <param name="index">
    /// Where the search stopped: the first entry not less than the value, or the count when every entry
    /// sorts before it.
    /// </param>
    /// <returns>True when that entry matches the value.</returns>
    /// <remarks>
    /// A forward scan, because a leaf's keys are stored relative to each other and there is nothing to
    /// binary-search. The C library uses the duplicate counts to skip comparisons it can prove
    /// unnecessary (b4leafSeek, b4block.c:2192-2474); rebuilding each key as we go costs the same walk
    /// and keeps the comparison in one place, which matters more here than the saved byte compares —
    /// the keys are rebuilt anyway for whoever reads the entry.
    /// </remarks>
    public bool Seek(KeySearch search, out int index)
    {
        for (int i = 0; i < Count; i++)
        {
            int comparison = search.Compare(EntryAt(i).Key);

            if (comparison == 0)
            {
                index = i;
                return true;
            }

            if (comparison > 0)
            {
                index = i;
                return false;
            }
        }

        index = Count;
        return false;
    }

    /// <summary>
    /// Gives the entry's packed values without rebuilding its key.
    /// </summary>
    /// <param name="index">Which entry, counting from zero.</param>
    /// <returns>The record number and the two compression counts as stored.</returns>
    public PackedEntry PackedAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        return Geometry.Unpack(block, index);
    }

    private void Build(int index)
    {
        PackedEntry entry = Geometry.Unpack(block, index);
        int stored = keyLength - entry.DupCount - entry.TrailCount;

        // A negative count is how index corruption shows up here, and the C library treats it the same
        // way rather than trying to carry on (b4block.c:1916-1924).
        if (stored < 0)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"Entry {index} of leaf node {node} shares {entry.DupCount} bytes and pads " +
                $"{entry.TrailCount} of a {keyLength}-byte key, which leaves {stored} stored.");
        }

        textEnd -= stored;
        if (textEnd < LeafGeometry.InfoArrayOffset)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"The key text of leaf node {node} runs into its entry array at entry {index}.");
        }

        block.AsSpan(textEnd, stored).CopyTo(key.AsSpan(entry.DupCount));
        key.AsSpan(keyLength - entry.TrailCount).Fill(padByte);
    }
}
