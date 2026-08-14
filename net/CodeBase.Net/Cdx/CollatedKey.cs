namespace CodeBase.Net.Cdx;

/// <summary>
/// Builds the key a collated string tag stores, from the weight tables and the text.
///
/// A collated key is twice the length of its input and comes in two halves. The first is the
/// [i]head[/i] weights, one per character, which decide the primary order; the second is the
/// [i]tail[/i] weights of only those characters that have one, packed together and zero-filled.
/// Sorting on the whole thing therefore orders by base letter first and by accent afterwards,
/// which is what makes the collation case- and accent-insensitive at the primary level.
///
/// Trailing blanks are removed before any of it. A blank sorts after some characters and before
/// most, so leaving it in would order text by its padding (u4util.c:2242-2250).
///
/// Governing specification: KEY-COLLATION.md section 3.4, from
/// [c]t4convertSubSortCompressChar[/c] (u4util.c:2201-2360).
/// </summary>
internal static class CollatedKey
{
    /// <summary>
    /// Writes the collated key to search a tag for a value, which is not always the key it stores.
    /// </summary>
    /// <param name="value">The characters to look for.</param>
    /// <param name="table">The collation's weight table.</param>
    /// <param name="expansions">The collation's expansion table.</param>
    /// <param name="keyLength">The tag's key length.</param>
    /// <param name="destination">Where to write, at least twice the value's length.</param>
    /// <returns>How many bytes of the destination the search covers.</returns>
    /// <remarks>
    /// A value narrower than the tag's key is a [i]partial[/i] seek, and two things change
    /// (tfile4stok, D4SEEK.C:39-142).
    ///
    /// The tail weights are left out. A short value's tails would sit where the stored key still has
    /// head weights, so comparing them would fail on characters the caller never supplied.
    ///
    /// Then the key is cut back to its heads alone. The reference finds where they end by scanning
    /// for the first byte below sixteen, which works because no character's head weight is ever that
    /// low — the same property the weight tables are checked for. What is left is a true prefix of
    /// every stored key that begins with the same characters.
    ///
    /// A consequence worth expecting: trailing blanks make no difference at all here. They are
    /// stripped before weighing, so seeking "A" and seeking "A   " build the same search
    /// (D4SEEK.C:106-116 says so at length).
    /// </remarks>
    public static int WriteSearch(
        ReadOnlySpan<byte> value,
        ReadOnlySpan<byte> table,
        ReadOnlySpan<byte> expansions,
        int keyLength,
        Span<byte> destination)
    {
        bool partial = keyLength > 2 * value.Length;
        int written = Write(value, table, expansions, includeTails: !partial, destination);

        if (!partial)
            return written;

        int heads = 0;
        while (heads < written && destination[heads] >= MinimumHeadWeight)
            heads++;

        return heads;
    }

    /// <summary>
    /// The lowest weight a real character's head can carry.
    /// </summary>
    /// <value>
    /// Sixteen. Nothing below it appears as a head in any collation table, which is what lets a
    /// partial seek find where the heads stop by looking at the bytes (D4SEEK.C:117,134).
    /// </value>
    private const byte MinimumHeadWeight = 16;

    /// <summary>
    /// Writes the collated key of a string.
    /// </summary>
    /// <param name="value">The characters to convert, as stored bytes.</param>
    /// <param name="table">The collation's weight table.</param>
    /// <param name="expansions">The collation's expansion table.</param>
    /// <param name="includeTails">
    /// Whether the tail weights are written. False only for a partial seek, where a truncated value
    /// would contribute tails that the stored key does not have in the same place, and the
    /// comparison would fail on characters the caller never supplied.
    /// </param>
    /// <param name="destination">Where to write, at least twice the value's length.</param>
    /// <returns>How many bytes were written, which is always twice the value's length.</returns>
    /// <exception cref="CodeBaseException">
    /// The value asks for a compression, which no collation this library reads defines and which the
    /// C library itself refuses (u4util.c:2314-2319).
    /// </exception>
    public static int Write(
        ReadOnlySpan<byte> value,
        ReadOnlySpan<byte> table,
        ReadOnlySpan<byte> expansions,
        bool includeTails,
        Span<byte> destination)
    {
        int length = value.Length;
        int written = 2 * length;

        // The blanks come off the end before anything is weighed, and the output stays the width the
        // untrimmed value asked for. Both matter: the first decides the order, the second keeps every
        // key in a tag the same size.
        int content = length;
        while (content > 0 && value[content - 1] == (byte)' ')
            content--;

        Span<byte> tails = written <= 512 ? stackalloc byte[512] : new byte[written];
        int head = 0;
        int tail = 0;

        for (int i = 0; i < content; i++)
        {
            (byte headWeight, byte tailWeight) = CollationTables.Weigh(table, value[i]);

            if (headWeight != CollationTables.Expands)
            {
                destination[head++] = headWeight;

                // The guard is the reference's, and it counts against the *untrimmed* length
                // (u4util.c:2331-2336). Kept for fidelity, though it can never change the result:
                // it only bites once the tails outnumber the field width, and by then the copy
                // below is already clipping them to the room the heads left, which is smaller.
                // Checked exhaustively over every width and mix of expanding characters.
                if (includeTails && tailWeight != CollationTables.NoTail && tail < length)
                    tails[tail++] = tailWeight;

                continue;
            }

            // An expanding character stands for two others, so it writes two heads -- and both of
            // their tails, without the length guard the ordinary path has. That asymmetry is the C
            // library's (u4util.c:2296-2311) and it lets a key of expansions carry more tails than
            // it has characters.
            (byte first, byte second) = CollationTables.Expand(expansions, tailWeight);

            foreach (byte expanded in stackalloc[] { first, second })
            {
                (byte expandedHead, byte expandedTail) = CollationTables.Weigh(table, expanded);

                destination[head++] = expandedHead;

                if (includeTails && expandedTail != CollationTables.NoTail)
                    tails[tail++] = expandedTail;
            }
        }

        // Whatever the heads left is filled with tails, as many as fit, then zeros. When the tails
        // are suppressed the whole remainder is zeros, which is what makes a partial seek's key a
        // prefix of the stored one rather than a different key.
        int room = written - head;
        int copied = Math.Min(room, tail);

        tails[..copied].CopyTo(destination[head..]);
        destination.Slice(head + copied, room - copied).Clear();

        return written;
    }
}
