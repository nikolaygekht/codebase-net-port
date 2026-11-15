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
    /// Writes the key a collation stores for a string, choosing the transform the collation names.
    /// </summary>
    /// <param name="value">The characters to convert, as stored bytes.</param>
    /// <param name="collation">The tag's collation.</param>
    /// <param name="codePage">The table's code page, which chooses GENERAL's weights.</param>
    /// <param name="includeTails">Whether tail weights are written; a simple collation has none.</param>
    /// <param name="destination">Where to write, at least as long as the key.</param>
    /// <returns>How many bytes were written: the value's length for a simple collation, twice it otherwise.</returns>
    public static int Write(
        ReadOnlySpan<byte> value, CollationName collation, int codePage, bool includeTails, Span<byte> destination) =>
        CollationWeights.IsSimple(collation)
            ? WriteSimple(value, CollationWeights.TableFor(collation, codePage), destination)
            : WriteWeighed(
                value,
                CollationWeights.TableFor(collation, codePage),
                CollationWeights.ExpansionsFor(collation, codePage),
                CollationWeights.SequencesFor(collation, codePage),
                includeTails,
                destination);

    /// <summary>
    /// Writes the key to search a collated tag for a value, choosing the transform the collation names.
    /// </summary>
    /// <param name="value">The characters to look for.</param>
    /// <param name="collation">The tag's collation.</param>
    /// <param name="codePage">The table's code page, which chooses GENERAL's weights.</param>
    /// <param name="keyLength">The tag's key length.</param>
    /// <param name="destination">Where to write, at least as long as the key.</param>
    /// <returns>How many bytes of the destination the search covers.</returns>
    /// <value>
    /// A simple collation's search is the value translated, short or not: the reference skips
    /// GENERAL's tail suppression and its scan for where the heads end when the mapping is simple
    /// (D4SEEK.C:104-140), because one byte in is one byte out.
    /// </value>
    public static int WriteSearch(
        ReadOnlySpan<byte> value, CollationName collation, int codePage, int keyLength, Span<byte> destination) =>
        CollationWeights.IsSimple(collation)
            ? WriteSimple(value, CollationWeights.TableFor(collation, codePage), destination)
            : WriteSearch(
                value,
                CollationWeights.TableFor(collation, codePage),
                CollationWeights.ExpansionsFor(collation, codePage),
                CollationWeights.SequencesFor(collation, codePage),
                keyLength,
                destination);

    /// <summary>
    /// Writes the key a head-and-tail collation stores for UTF-16 text.
    /// </summary>
    /// <param name="value">The text, little-endian UTF-16, as a wide field stores it.</param>
    /// <param name="collation">The tag's collation; not a simple one.</param>
    /// <param name="codePage">The table's code page, which chooses GENERAL's weights.</param>
    /// <param name="destination">Where to write, at least twice the value's length.</param>
    /// <returns>How many bytes were written, which is always twice the value's length.</returns>
    /// <value>
    /// [c]t4convertSubSortCompressUnicode[/c] (i4conv.c:484-583) over the table
    /// [c]collate4setupUnicodeFromChar[/c] builds (u4util.c:2700-2793), computed from the byte table
    /// rather than built: a code unit below 256 is weighed as the byte of that number, whatever
    /// character the byte is in the code page; a unit from 256 up weighs as itself with no tail.
    /// Heads are 16-bit big-endian, tails are written as a zero and the tail, as many as fit after the
    /// heads, then zeros. Only trailing U+0020 is dropped, so the U+2000 CodeBase blanks a wide field
    /// with is weighed like any other character; and unlike the byte transform the tails have no
    /// count guard (ADR-85).
    /// </value>
    public static int WriteWide(ReadOnlySpan<byte> value, CollationName collation, int codePage, Span<byte> destination) =>
        Wide(value, collation, codePage, includeTails: true, destination);

    /// <summary>
    /// Writes the search for a prefix of UTF-16 text in a collated wide tag: the heads of the given code units.
    /// </summary>
    /// <param name="value">The text, little-endian UTF-16.</param>
    /// <param name="collation">The tag's collation; not a simple one.</param>
    /// <param name="codePage">The table's code page, which chooses GENERAL's weights.</param>
    /// <param name="destination">Where to write, at least twice the value's length.</param>
    /// <returns>How many bytes the search covers: the heads, two per unit and four per expanding one.</returns>
    /// <value>
    /// Not the reference's. Its partial seek looks for the end of the heads by the first byte below
    /// sixteen, and every head of a Unicode key begins with a zero byte, so it searches nothing and
    /// lands on the tag's first key (D4SEEK.C:104-140). The heads alone are what every stored key
    /// beginning with these characters begins with (ADR-85).
    /// </value>
    public static int WriteWideSearch(ReadOnlySpan<byte> value, CollationName collation, int codePage, Span<byte> destination) =>
        Wide(value, collation, codePage, includeTails: false, destination);

    private static int Wide(
        ReadOnlySpan<byte> value, CollationName collation, int codePage, bool includeTails, Span<byte> destination)
    {
        ReadOnlySpan<byte> table = CollationWeights.TableFor(collation, codePage);
        ReadOnlySpan<byte> expansions = CollationWeights.ExpansionsFor(collation, codePage);

        int units = value.Length / 2;
        int written = 2 * value.Length;

        // Only U+0020 comes off the end (i4conv.c:517-524).
        int content = units;
        while (content > 0 && Unit(value, content - 1) == ' ')
            content--;

        Span<byte> tails = written <= 512 ? stackalloc byte[512] : new byte[written];
        int head = 0;
        int tail = 0;

        for (int i = 0; i < content; i++)
        {
            int unit = Unit(value, i);

            if (unit >= 256)
            {
                destination[head++] = (byte)(unit >> 8);
                destination[head++] = (byte)unit;
                continue;
            }

            (byte headWeight, byte tailWeight) = CollationTables.Weigh(table, (byte)unit);

            if (headWeight != CollationTables.Expands)
            {
                WideWeight(headWeight, tailWeight, destination, ref head, tails, ref tail);
                continue;
            }

            (byte first, byte second) = CollationTables.Expand(expansions, tailWeight);

            foreach (byte expanded in stackalloc[] { first, second })
            {
                (byte expandedHead, byte expandedTail) = CollationTables.Weigh(table, expanded);
                WideWeight(expandedHead, expandedTail, destination, ref head, tails, ref tail);
            }
        }

        if (!includeTails)
            return head;

        int room = written - head;
        int copied = Math.Min(room, tail);

        tails[..copied].CopyTo(destination[head..]);
        destination.Slice(head + copied, room - copied).Clear();

        return written;
    }

    private static int Unit(ReadOnlySpan<byte> value, int index) => value[2 * index] | (value[(2 * index) + 1] << 8);

    private static void WideWeight(byte headWeight, byte tailWeight, Span<byte> heads, ref int head, Span<byte> tails, ref int tail)
    {
        heads[head++] = 0;
        heads[head++] = headWeight;

        if (tailWeight != CollationTables.NoTail && tail + 2 <= tails.Length)
        {
            tails[tail++] = 0;
            tails[tail++] = tailWeight;
        }
    }

    /// <summary>
    /// Writes a simple collation's key: each byte replaced by its head weight.
    /// </summary>
    /// <value>
    /// [c]t4convertSimpleChar[/c] (u4util.c:2366-2425). Trailing blanks are weighed like any other
    /// byte -- the reference stopped stripping them for simple mappings (u4util.c:2411-2413) -- and
    /// the key is exactly as long as the value.
    /// </value>
    private static int WriteSimple(ReadOnlySpan<byte> value, ReadOnlySpan<byte> table, Span<byte> destination)
    {
        for (int i = 0; i < value.Length; i++)
            destination[i] = CollationTables.Weigh(table, value[i]).Head;

        return value.Length;
    }

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
        Span<byte> destination) =>
        WriteSearch(value, table, expansions, [], keyLength, destination);

    private static int WriteSearch(
        ReadOnlySpan<byte> value,
        ReadOnlySpan<byte> table,
        ReadOnlySpan<byte> expansions,
        ReadOnlySpan<byte> sequences,
        int keyLength,
        Span<byte> destination)
    {
        bool partial = keyLength > 2 * value.Length;
        int written = WriteWeighed(value, table, expansions, sequences, includeTails: !partial, destination);

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
        Span<byte> destination) =>
        WriteWeighed(value, table, expansions, [], includeTails, destination);

    /// <summary>
    /// The head-and-tail transform, with a collation's weighed sequences.
    /// </summary>
    /// <value>
    /// A sequence is one to three bytes keyed as given heads and tails, which only Visual FoxPro's
    /// named collations have and CodeBase's table form cannot hold. Two or three bytes with one head
    /// are a contraction -- Czech CH, Hungarian DZS -- which CodeBase never coded ([c]compress4[/c]
    /// is "not coded yet", u4util.c:2314-2319); one byte with two heads is an expansion no pair of
    /// characters spells, such as SWEFIN's AE ligature, which has one tail where A and E have two.
    /// At each position the longest sequence that matches is taken first, so DDZS weighs as D and
    /// then DZS, as VFP's keys show (ADR-88). Each entry is eight bytes: the length, three bytes
    /// (zero past the length), two heads and two tails, 0xFF for one that is not there. Its tails
    /// take no count guard: there are never more than two a byte, which the key's room holds.
    /// </value>
    private static int WriteWeighed(
        ReadOnlySpan<byte> value,
        ReadOnlySpan<byte> table,
        ReadOnlySpan<byte> expansions,
        ReadOnlySpan<byte> sequences,
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
            int matched = Sequence(value[i..content], sequences, out ReadOnlySpan<byte> weights);
            if (matched > 0)
            {
                // Two heads at most and two tails at most, so a key of sequences still fits the
                // twice-the-width it is given.
                i += matched - 1;

                foreach (byte sequenceHead in weights[..2])
                {
                    if (sequenceHead != CollationTables.NoTail)
                        destination[head++] = sequenceHead;
                }

                foreach (byte sequenceTail in weights[2..])
                {
                    if (includeTails && sequenceTail != CollationTables.NoTail)
                        tails[tail++] = sequenceTail;
                }

                continue;
            }

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

    /// <summary>The bytes one sequence entry takes: its length, three bytes, two heads and two tails.</summary>
    private const int SequenceEntry = 8;

    /// <summary>
    /// Finds the longest sequence the text begins with.
    /// </summary>
    /// <returns>How many bytes it spans, or zero where none matches.</returns>
    private static int Sequence(ReadOnlySpan<byte> text, ReadOnlySpan<byte> sequences, out ReadOnlySpan<byte> weights)
    {
        int best = 0;
        weights = [];

        for (int entry = 0; entry + SequenceEntry <= sequences.Length; entry += SequenceEntry)
        {
            int length = sequences[entry];
            if (length <= best || length > text.Length || !text[..length].SequenceEqual(sequences.Slice(entry + 1, length)))
                continue;

            best = length;
            weights = sequences.Slice(entry + 4, 4);
        }

        return best;
    }
}
