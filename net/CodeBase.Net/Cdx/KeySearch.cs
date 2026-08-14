namespace CodeBase.Net.Cdx;

/// <summary>
/// A value being searched for, and how a stored key is compared against it.
///
/// There are **two comparison rules**, and which applies depends on whether the caller supplied trailing
/// pad bytes. That is not an implementation detail; it is the difference between the two things a search
/// value can mean, and the corpus settles it:
///
/// A value with **no trailing pad** is a *prefix*. Only its own length is compared, so
/// [c]"CUSTOMER-A"[/c] finds [c]"CUSTOMER-ACCT-0599  "[/c] and reports it as a match. That is what a
/// partial seek is for.
///
/// A value **with trailing pad** stands for the whole key. It is compared against the key over the full
/// key length, with its pad bytes taking part — so [c]"AB      "[/c] does *not* match
/// [c]"AB\0\0\0\0\0\0"[/c], because at the third byte a NUL sorts below a space. Stripping the pad and
/// comparing two bytes would match it, and the reference implementation does not: it lands on
/// [c]"AB      "[/c] instead. A value that is entirely pad is this same case.
///
/// Comparison is unsigned throughout. Signed bytes would misorder every accented character in a collated
/// key and every complemented negative in a numeric one.
///
/// Governing specification: CDX-FORMAT.md section 7.
/// </summary>
internal readonly struct KeySearch
{
    private readonly byte[] value;
    private readonly byte padByte;
    private readonly int keyLength;

    private KeySearch(byte[] value, int length, int keyLength, byte padByte, bool comparesPadded)
    {
        this.value = value;
        this.padByte = padByte;
        this.keyLength = keyLength;
        Length = length;
        ComparesPadded = comparesPadded;
    }

    /// <summary>Gets the number of bytes of the value that carry content.</summary>
    /// <value>
    /// Trailing pad removed, except for an all-pad value, which keeps its length because stripping it
    /// would leave nothing to compare and match every key in the tag.
    /// </value>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the value stands for a whole key rather than a prefix.</summary>
    /// <value>
    /// True when the caller supplied trailing pad bytes. The comparison then runs over the whole key
    /// length with those pad bytes participating, which is what makes a key continuing below the pad sort
    /// *before* the value.
    /// </value>
    public bool ComparesPadded { get; }

    /// <summary>Gets the bytes that carry content.</summary>
    public ReadOnlySpan<byte> Bytes => value.AsSpan(0, Length);

    /// <summary>
    /// Prepares a search value for a tag.
    /// </summary>
    /// <param name="value">The value to look for, already in key form.</param>
    /// <param name="keyLength">The tag's key length.</param>
    /// <param name="padByte">The byte the tag pads its keys with.</param>
    /// <returns>The search, with its comparison rule settled.</returns>
    /// <remarks>
    /// A value longer than the key length is **clamped**, not refused, which is what the C library does
    /// (I4TAG.C:2233-2234). A caller that passes more has made a mistake, but diverging from the
    /// reference over it would be a worse one.
    /// </remarks>
    public static KeySearch For(ReadOnlySpan<byte> value, int keyLength, byte padByte)
    {
        int length = Math.Min(value.Length, keyLength);

        return Into(value[..length].ToArray(), length, keyLength, padByte);
    }

    /// <summary>
    /// Prepares a search over a buffer the caller already owns and has filled.
    /// </summary>
    /// <param name="buffer">The key bytes, which this search borrows rather than copies.</param>
    /// <param name="length">How many bytes of the buffer the value occupies.</param>
    /// <param name="keyLength">The tag's key length.</param>
    /// <param name="padByte">The byte the tag pads its keys with.</param>
    /// <returns>The search, with its comparison rule settled.</returns>
    /// <remarks>
    /// The allocation-free form, and the one a cursor uses. A cursor owns one buffer sized to its
    /// tag, converts each value straight into it, and keeps the search alive to answer
    /// [c]SeekNext[/c] — so the bytes and the search share a lifetime and the borrow cannot dangle.
    /// The C library reaches the same arrangement from the other side, parking a scratch buffer on
    /// the engine and re-converting per call (D4SEEK.C:1130-1137).
    ///
    /// The caller must not write to the buffer again while the search is in use, which is why this
    /// stays internal.
    /// </remarks>
    public static KeySearch Into(byte[] buffer, int length, int keyLength, byte padByte)
    {
        byte[] copy = buffer;

        int content = length;
        while (content > 0 && copy[content - 1] == padByte)
            content--;

        // An all-pad value keeps its length and compares as a whole key (b4block.c:2211-2216). Any other
        // value that carried pad compares as a whole key too; one that carried none is a prefix.
        bool allPad = length > 0 && content == 0;

        return new KeySearch(
            copy,
            allPad ? length : content,
            keyLength,
            padByte,
            comparesPadded: content < length);
    }

    /// <summary>
    /// Compares a stored key against this value.
    /// </summary>
    /// <param name="key">The stored key, whole.</param>
    /// <returns>
    /// Negative when the key sorts before this value, zero when it matches, positive when it sorts after.
    /// </returns>
    public int Compare(ReadOnlySpan<byte> key)
    {
        // Nothing to compare: an empty search matches every key, which is what a zero-length seek means
        // at this level.
        if (Length == 0)
            return 0;

        if (!ComparesPadded)
        {
            return key.Length >= Length
                ? key[..Length].SequenceCompareTo(Bytes)
                : key.SequenceCompareTo(Bytes);
        }

        // The pad bytes take part, so the comparison runs the whole width of the key.
        int width = Math.Min(key.Length, keyLength);

        for (int i = 0; i < width; i++)
        {
            byte mine = i < Length ? value[i] : padByte;

            if (key[i] != mine)
                return key[i] < mine ? -1 : 1;
        }

        return 0;
    }

    /// <summary>
    /// Says whether a stored key matches this value.
    /// </summary>
    /// <param name="key">The stored key, whole.</param>
    /// <returns>True when the key matches under whichever rule applies.</returns>
    public bool Matches(ReadOnlySpan<byte> key) => Compare(key) == 0;

    /// <summary>
    /// Gives this value with one added to it, for finding what lies just past it.
    /// </summary>
    /// <param name="incremented">The incremented value, valid only when this returns true.</param>
    /// <returns>False when the value cannot be incremented, which means nothing sorts after it.</returns>
    /// <remarks>
    /// The format's own way of adding one, from the descending seek: raise the last byte that is not
    /// 0xFF and zero the 0xFF bytes after it (tfile4seekDescendKey, I4TAG.C:2092-2151). A value of all
    /// 0xFF has no successor, and the C library takes a different path entirely when that happens —
    /// which is why this reports it rather than wrapping.
    ///
    /// The successor is compared as a prefix of its own width: it is a boundary rather than a key, so
    /// padding it out would put pad bytes after the byte that was just raised.
    ///
    /// **Which bytes get incremented matters.** For a value that compares padded, the pad bytes are part
    /// of it and the increment lands on the last of them — the successor of [c]"MIDDLE      "[/c] is
    /// [c]"MIDDLE     !"[/c], not [c]"MIDDLF"[/c]. Incrementing the content instead would step over
    /// [c]"MIDDLE-EARTH"[/c], which sorts *above* the padded value and must not be included. The C
    /// library increments the caller's buffer for the same reason (tfile4seekDescendKey takes both the
    /// stripped and the supplied length, I4TAG.C:2205-2247).
    /// </remarks>
    public bool TryIncrement(out KeySearch incremented)
    {
        // The value as it compares: content alone for a prefix, and the whole key width for a value that
        // compares padded — the successor has to be computed at the width the comparison runs over, or it
        // lands inside the value rather than past it.
        int width = ComparesPadded ? keyLength : Length;
        byte[] next = new byte[width];

        value.AsSpan(0, Math.Min(Length, width)).CopyTo(next);
        for (int i = Length; i < width; i++)
            next[i] = padByte;

        for (int i = next.Length - 1; i >= 0; i--)
        {
            if (next[i] != 0xFF)
            {
                next[i]++;
                incremented = new KeySearch(
                    next, next.Length, keyLength, padByte, comparesPadded: false);
                return true;
            }

            next[i] = 0x00;
        }

        incremented = default;
        return false;
    }
}
