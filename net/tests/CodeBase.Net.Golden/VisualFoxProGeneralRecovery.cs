using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;

namespace CodeBase.Net.Golden;

/// <summary>
/// Recovers Visual FoxPro's GENERAL weights for a code page from the keys Visual FoxPro wrote for case VFPG.
///
/// [c]vfpgen.prg[/c] writes, on one code page, every byte alone (records 1-256), every byte followed by
/// each of two partners (the next 256 records each), then runs of twelve, under one GENERAL tag. The
/// key layout is the one CodeBase reproduces for cp1252 (KEY-COLLATION.md section 3.4): the heads, one
/// per character and two for an expanding one, each at least sixteen; then the tails of the characters
/// that have one, packed; then zeros.
///
/// A byte's heads and tails are read from its pair with a partner: the heads are the leading bytes of
/// sixteen and up, the last of them the partner's; the byte's tails are what comes before the
/// partner's tail, which is the one tail known to be nonzero. That tells a tail of zero from no tail,
/// which a key of the byte alone cannot -- trailing zeros look the same either way -- and it reads the
/// blank, whose key alone is empty. Both partners must agree, and the byte's own key must be what they
/// say. An expanding byte is then written as CodeBase writes one: a head of 0xFF and the index of a
/// pair of bytes whose weights are its two halves.
///
/// The same holds for VFP's named collations (ADR-87, ADR-88), whose tables the script writes the
/// same way under the collation's name, followed by rows aimed at their contractions; what CodeBase's
/// form cannot hold comes back as weighed sequences.
///
/// Governing decisions: ADR-86, ADR-87, ADR-88. The result is the library's table, and the library's table must be it.
/// </summary>
internal static class VisualFoxProGeneralRecovery
{
    /// <summary>The weights recovered for one code page, in CodeBase's own two-table form, and the sequences it cannot hold.</summary>
    /// <param name="Table">One head and one tail per byte value, 0xFF for no tail or an expansion's head.</param>
    /// <param name="Expansions">Two bytes per expansion, the characters whose weights it is made of.</param>
    /// <param name="Sequences">Eight bytes per sequence -- its length, three bytes, two heads, two tails -- longest first.</param>
    internal sealed record Weights(byte[] Table, byte[] Expansions, byte[] Sequences);

    private const int SequenceEntry = 8;

    private const int Bytes = 256;

    /// <summary>
    /// Reads the keys of an oracle table and recovers its weights.
    /// </summary>
    public static Weights Recover(string dbfPath)
    {
        Dictionary<int, byte[]> keys = Keys(dbfPath, out byte[] partners, out List<(byte[] Text, byte[] Key)> extras);

        var heads = new byte[Bytes][];
        var tails = new byte[Bytes][];

        if (partners.Length == 0)
        {
            WithoutPartners(keys, heads, tails);
            return ToCodeBaseForm(heads, tails, Contractions(extras, heads, []));
        }

        (byte Head, byte Tail)[] partnerWeights = [.. partners.Select(p => PartnerWeight(keys[p + 1], p))];

        for (int b = 0; b < Bytes; b++)
        {
            (byte[] H, byte[] T)? agreed = null;

            for (int p = 0; p < partners.Length; p++)
            {
                (byte[] h, byte[] t) = FromPair(keys[Bytes + (p * Bytes) + b + 1], partnerWeights[p], b, partners[p]);

                if (agreed is null)
                    agreed = (h, t);
                else if (!agreed.Value.H.SequenceEqual(h) || !agreed.Value.T.SequenceEqual(t))
                    throw new InvalidDataException($"byte 0x{b:X2}: the partners disagree");
            }

            heads[b] = agreed!.Value.H;
            tails[b] = agreed.Value.T;

            if (b != 0x20)
                CheckAlone(keys[b + 1], heads[b], tails[b], b);
        }

        return ToCodeBaseForm(heads, tails, Contractions(extras, heads, [.. partners.Zip(partnerWeights)]));
    }

    /// <summary>
    /// Finds the contractions among the extra rows: two or three bytes whose key has one head.
    /// </summary>
    /// <value>
    /// A row the script wrote as a contraction alone keys to a single head where its bytes would key
    /// to two or three; a doubled or extended spelling (CCS, CHCH) keys to two heads and is only
    /// there to be rebuilt. A contraction's tail is read from the row that puts the first partner
    /// after it, as a byte's is; with no partner on the page there is no tail.
    /// </value>
    private static List<(byte[] Text, byte Head, byte Tail)> Contractions(
        List<(byte[] Text, byte[] Key)> extras, byte[][] heads, (byte Partner, (byte Head, byte Tail) Weight)[] partners)
    {
        List<(byte[] Text, byte Head, byte Tail)> found = [];

        foreach ((byte[] text, byte[] key) in extras)
        {
            if (text.Length is < 2 or > 3 || key[0] < 16 || key[1] >= 16)
                continue;

            byte[] separate = [.. text.SelectMany(b => heads[b])];
            if (separate.Length == 1 && separate[0] == key[0])
                continue;

            byte tail = CollationTables.NoTail;
            if (partners.Length > 0)
            {
                (byte partner, (byte Head, byte Tail) weight) = partners[0];
                byte[] withPartner = extras.Single(e => e.Text.SequenceEqual([.. text, partner])).Key;
                (byte[] h, byte[] t) = FromPair(withPartner, weight, text[0], partner);

                if (h.Length != 1 || h[0] != key[0] || t.Length > 1)
                    throw new InvalidDataException($"contraction {Convert.ToHexString(text)}: its row with a partner disagrees");

                tail = t.Length == 0 ? CollationTables.NoTail : t[0];
            }
            else if (key[1..].Any(k => k != 0))
            {
                throw new InvalidDataException($"contraction {Convert.ToHexString(text)}: a tail where the page has none");
            }

            found.Add((text, key[0], tail));
        }

        return found;
    }

    /// <summary>One sequence entry: the length, three bytes zero-padded, two heads and two tails, 0xFF for none.</summary>
    private static byte[] Entry(byte[] text, byte[] heads, byte[] tails)
    {
        byte[] entry = [(byte)text.Length, 0, 0, 0, CollationTables.NoTail, CollationTables.NoTail, CollationTables.NoTail, CollationTables.NoTail];
        text.CopyTo(entry, 1);
        heads.CopyTo(entry, 4);
        tails.CopyTo(entry, 6);
        return entry;
    }

    /// <summary>
    /// Recovers a collation with no tail anywhere, where the script was given no partner: RUSSIAN on cp866.
    /// </summary>
    /// <value>
    /// With no nonzero tail on the page, a tail of zero and none key alike, so every byte is taken to
    /// have none -- and any byte whose own key shows something after its heads is refused, because
    /// then the premise is false. The blank's own key is empty; its head is the first of the first
    /// run, which begins with it.
    /// </value>
    private static void WithoutPartners(Dictionary<int, byte[]> keys, byte[][] heads, byte[][] tails)
    {
        for (int b = 0; b < Bytes; b++)
        {
            byte[] key = b == 0x20 ? keys[Bytes + 1][..1] : keys[b + 1];

            int headCount = 0;
            while (headCount < key.Length && key[headCount] >= 16)
                headCount++;

            if (headCount == 0 || key[headCount..].Any(k => k != 0))
                throw new InvalidDataException($"byte 0x{b:X2}: a tail where the page was taken to have none");

            heads[b] = key[..headCount];
            tails[b] = [];
        }
    }

    /// <summary>The runs of twelve the script writes after the pairs.</summary>
    private const int Runs = 19;

    /// <summary>Every key of tag G by record number, the partners the script was given, and its extra rows.</summary>
    private static Dictionary<int, byte[]> Keys(string dbfPath, out byte[] partners, out List<(byte[] Text, byte[] Key)> extras)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(dbfPath);
        FieldDefinition text = table.Fields["K"];

        Dictionary<int, byte[]> keys = [];
        TagCursor cursor = table.Tags["G"].Inner.OpenCursor();
        for (bool on = cursor.Top(); on; on = cursor.Next())
            keys[(int)cursor.Current.Record] = cursor.Current.Key.ToArray();

        // The partners are the second byte of each block of pairs.
        List<byte> found = [];
        for (int record = Bytes + 1; record + Bytes - 1 <= table.RecordCount; record += Bytes)
        {
            table.Go(record);
            ReadOnlySpan<byte> raw = table.GetRawBytes(text);
            if (raw[0] != 0 || raw[1] == (byte)' ')
                break;
            found.Add(raw[1]);
        }

        partners = [.. found];

        // The extra rows follow the runs: contractions alone and beside the partners, and the longer
        // spellings that check them. A row of letter pairs from a survey would follow too and is
        // read the same way.
        extras = [];
        for (int record = Bytes + (partners.Length * Bytes) + Runs + 1; record <= table.RecordCount; record++)
        {
            table.Go(record);
            byte[] raw = table.GetRawBytes(text).ToArray();
            int length = raw.Length;
            while (length > 0 && raw[length - 1] == (byte)' ')
                length--;
            extras.Add((raw[..length], keys[record]));
        }

        return keys;
    }

    private static (byte Head, byte Tail) PartnerWeight(byte[] alone, byte partner)
    {
        if (alone[0] < 16 || alone[1] is 0 or >= 16 || alone[2] != 0)
            throw new InvalidDataException($"partner 0x{partner:X2} is not one head with a nonzero tail");

        return (alone[0], alone[1]);
    }

    private static (byte[] Heads, byte[] Tails) FromPair(byte[] key, (byte Head, byte Tail) partner, int b, byte p)
    {
        int headCount = 0;
        while (headCount < key.Length && key[headCount] >= 16)
            headCount++;

        if (headCount is < 2 or > 3 || key[headCount - 1] != partner.Head)
            throw new InvalidDataException($"byte 0x{b:X2} with 0x{p:X2}: {headCount} heads, the last not the partner's");

        int last = key.Length - 1;
        while (last >= headCount && key[last] == 0)
            last--;

        if (last < headCount || key[last] != partner.Tail)
            throw new InvalidDataException($"byte 0x{b:X2} with 0x{p:X2}: the partner's tail is not the last");

        return (key[..(headCount - 1)], key[headCount..last]);
    }

    private static void CheckAlone(byte[] key, byte[] heads, byte[] tails, int b)
    {
        byte[] expected = new byte[key.Length];
        heads.CopyTo(expected, 0);
        tails.CopyTo(expected, heads.Length);

        if (!expected.AsSpan().SequenceEqual(key))
            throw new InvalidDataException($"byte 0x{b:X2}: its own key is not what its pairs say");
    }

    /// <summary>
    /// Writes the weights in CodeBase's form, with sequences for what that form cannot hold.
    /// </summary>
    /// <value>
    /// A byte with two heads is an expansion when two single-head bytes rebuild it, heads and tails
    /// both; where none do -- SWEFIN's AE ligature has one tail where A and E have one each -- it
    /// becomes a sequence of its one byte, and its table entry is 0xFF twice.
    /// </value>
    private static Weights ToCodeBaseForm(byte[][] heads, byte[][] tails, List<(byte[] Text, byte Head, byte Tail)> contractions)
    {
        byte[] table = new byte[2 * Bytes];
        List<byte> expansions = [];
        List<(byte[] Text, byte[] Heads, byte[] Tails)> sequences =
            [.. contractions.Select(c => (c.Text, new[] { c.Head }, c.Tail == CollationTables.NoTail ? Array.Empty<byte>() : [c.Tail]))];

        for (int b = 0; b < Bytes; b++)
        {
            if (heads[b].Length == 1)
            {
                if (tails[b].Length > 1 || heads[b][0] == CollationTables.Expands)
                    throw new InvalidDataException($"byte 0x{b:X2}: one head, {tails[b].Length} tails");

                table[2 * b] = heads[b][0];
                table[(2 * b) + 1] = tails[b].Length == 0 ? CollationTables.NoTail : tails[b][0];
                continue;
            }

            table[2 * b] = CollationTables.Expands;

            if (Halves(heads, tails, b) is { } halves)
            {
                table[(2 * b) + 1] = (byte)IndexOf(expansions, (byte)halves.First, (byte)halves.Second);
                continue;
            }

            if (heads[b].Length != 2 || tails[b].Length > 2)
                throw new InvalidDataException($"byte 0x{b:X2}: {heads[b].Length} heads and {tails[b].Length} tails, no two characters weigh as it");

            table[(2 * b) + 1] = CollationTables.NoTail;
            sequences.Add(([(byte)b], heads[b], tails[b]));
        }

        byte[] entries =
        [
            .. sequences
                .OrderByDescending(s => s.Text.Length)
                .ThenBy(s => Convert.ToHexString(s.Text), StringComparer.Ordinal)
                .SelectMany(s => Entry(s.Text, s.Heads, s.Tails)),
        ];

        return new Weights(table, [.. expansions], entries);
    }

    /// <summary>Finds two single-head bytes whose heads and tails, put together, are an expansion's, or null.</summary>
    private static (int First, int Second)? Halves(byte[][] heads, byte[][] tails, int b)
    {
        for (int first = 0; first < Bytes; first++)
        {
            if (heads[first].Length != 1 || heads[first][0] != heads[b][0])
                continue;

            for (int second = 0; second < Bytes; second++)
            {
                if (heads[second].Length != 1 || heads[second][0] != heads[b][1])
                    continue;

                if (tails[first].Concat(tails[second]).SequenceEqual(tails[b]))
                    return (first, second);
            }
        }

        return null;
    }

    private static int IndexOf(List<byte> expansions, byte first, byte second)
    {
        for (int i = 0; i < expansions.Count; i += 2)
        {
            if (expansions[i] == first && expansions[i + 1] == second)
                return i / 2;
        }

        expansions.Add(first);
        expansions.Add(second);
        return (expansions.Count / 2) - 1;
    }
}
