// ===========================================================================
//  Micro.cs -- why is the port's CPU work per seek so much larger than the C's?
//
//  The RAM-backed run put the port at 3.10 us of pure CPU per character seek
//  against the C library's 0.40. Reading both implementations, one difference is
//  systematic: the port materialises a fresh byte[keyLength] for EVERY key
//  comparison -- BranchBlock.EntryAt does `entry[..keyLength].ToArray()`, and
//  LeafBlock.EntryAt does `key.AsSpan().ToArray()` -- while the C compares in
//  place against the block it already holds.
//
//  This isolates and prices that. Three variants of the same descent over the
//  same real blocks of the same file, all three required to return identical
//  record numbers:
//
//    shipped   the library's BranchBlock.Seek / LeafBlock.Seek as they are
//    spans     the identical algorithm with the per-comparison array removed
//    dupskip   spans, plus the duplicate-count skip the C's b4leafSeek uses
//              (b4block.c:2232) -- suspect P7
//
//  KeySearch.Compare already takes a ReadOnlySpan<byte>, so `spans` changes no
//  algorithm and no result: it deletes copies the comparison never needed.
// ===========================================================================

using System.Diagnostics;
using System.Globalization;

using CodeBase.Net.Cdx;
using CodeBase.Net.IO;

namespace Bench;

internal static class Micro
{
    public static void Run(string dir, string tagName, byte padByte, string[] queries, double[]? numbers, int reps)
    {
        var files = new Program.RamFileSystem();
        using IRandomAccessSource source = files.Open(Path.Combine(dir, "PERF10K.cdx"));

        // The pad byte for a bare-field tag is derived from the table's descriptors (ADR-28), which
        // needs the table; here it is supplied per tag instead -- 0x20 for the C(20) tag, 0x00 for the
        // N(12,2) one. Getting this wrong silently breaks the seek: KeySearch.Into strips trailing pad
        // from the value, so a numeric key trimmed as though it padded with spaces misses every time.
        using IndexFileReader index = IndexFileReader.Open(source, "PERF10K.cdx", _ => padByte);
        CdxTag tag = index.Tag(tagName);

        var addressing = BlockAddressing.Standard;
        var nodes = new NodeReader(source, addressing);

        int keyLength = tag.KeyLength;
        byte pad = tag.PadByte;
        uint root = tag.Header.Root;

        Console.WriteLine(
            $"micro    tag={tagName} keyLength={keyLength} pad=0x{pad:X2} root={root} " +
            $"blocks={nodes.BlockCount} queries={queries.Length}");

        // Each query as the key bytes the tag actually stores: characters copied straight through for
        // a C(20) tag, and t4dblToFox for an N(12,2) one -- otherwise a numeric tag would be handed
        // text, every seek would miss, and the measurement would be of the miss path only.
        int count = numbers?.Length ?? queries.Length;
        byte[][] keys = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            byte[] k = new byte[keyLength];
            if (numbers is null)
            {
                int len = Math.Min(queries[i].Length, keyLength);
                for (int j = 0; j < len; j++)
                    k[j] = (byte)queries[i][j];
                for (int j = len; j < keyLength; j++)
                    k[j] = pad;
            }
            else
            {
                KeyTransform.FromDouble(numbers[i], k);
            }

            keys[i] = k;
        }

        Measure("shipped", reps, keys, keyLength, pad,
            (byte[] q, int len, out long cmp) => Shipped(nodes, root, keyLength, pad, q, len, out cmp));
        Measure("spans", reps, keys, keyLength, pad,
            (byte[] q, int len, out long cmp) => Spans(nodes, root, keyLength, pad, q, len, out cmp, false));
        Measure("dupskip", reps, keys, keyLength, pad,
            (byte[] q, int len, out long cmp) => Spans(nodes, root, keyLength, pad, q, len, out cmp, true));

        // The same two variants again, in the opposite order. If a variant's number moves when only its
        // position moves, the harness is measuring JIT state rather than the code.
        Measure("spans-2", reps, keys, keyLength, pad,
            (byte[] q, int len, out long cmp) => Spans(nodes, root, keyLength, pad, q, len, out cmp, false));
        Measure("shipped-2", reps, keys, keyLength, pad,
            (byte[] q, int len, out long cmp) => Shipped(nodes, root, keyLength, pad, q, len, out cmp));
    }

    private delegate uint Descend(byte[] query, int length, out long comparisons);

    private static void Measure(
        string name, int reps, byte[][] keys, int keyLength, byte pad, Descend descend)
    {
        byte[] buffer = new byte[keyLength];

        long Pass(out long comparisons)
        {
            long sum = 0, cmp = 0;

            foreach (byte[] k in keys)
            {
                k.CopyTo(buffer, 0);
                sum += descend(buffer, keyLength, out long c);
                cmp += c;
            }

            comparisons = cmp;
            return sum;
        }

        // Wall-clock warm-up -- see the note in Program.Measure. Without it the variant measured
        // first reports ~4x its real cost and the whole comparison inverts.
        long checksum = 0;
        var warm = Stopwatch.StartNew();
        for (int w = 0; w < 5 || (warm.ElapsedMilliseconds < 1500 && w < 400); w++)
            checksum = Pass(out _);
        warm.Stop();

        double[] ms = new double[reps];
        for (int r = 0; r < reps; r++)
        {
            var sw = Stopwatch.StartNew();
            long c = Pass(out _);
            sw.Stop();
            ms[r] = sw.Elapsed.TotalMilliseconds;

            if (c != checksum)
                throw new InvalidOperationException($"{name}: unstable checksum");
        }

        long bytes0 = GC.GetAllocatedBytesForCurrentThread();
        Pass(out long comparisons);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes0;

        Array.Sort(ms);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "variant={0,-8} recsum={1} min_ms={2:F3} us_per_seek={3:F3} " +
            "bytes_per_seek={4:F0} compares_per_seek={5:F2}",
            name, checksum, ms[0], ms[0] * 1000.0 / keys.Length,
            (double)allocated / keys.Length, (double)comparisons / keys.Length));
    }

    // ---- the library as it stands ----------------------------------------

    private static uint Shipped(
        NodeReader nodes, uint root, int keyLength, byte pad,
        byte[] query, int length, out long comparisons)
    {
        long cmp = 0;
        uint node = root;

        for (;;)
        {
            byte[] block = nodes.Read(node);
            NodeHeader header = NodeHeader.Parse(block, node);
            KeySearch search = KeySearch.Into(query, length, keyLength, pad);

            if (header.IsLeaf)
            {
                LeafBlock leaf = LeafBlock.Parse(block, header, keyLength, pad, node);

                // LeafBlock.Seek's own loop, with the comparisons counted. Identical work:
                // EntryAt(i).Key is a fresh byte[keyLength] on every iteration.
                for (int i = 0; i < leaf.Count; i++)
                {
                    cmp++;
                    int c = search.Compare(leaf.EntryAt(i).Key);
                    if (c == 0)
                    {
                        comparisons = cmp;
                        return leaf.EntryAt(i).Record;
                    }

                    if (c > 0)
                        break;
                }

                comparisons = cmp;
                return 0;
            }

            BranchBlock branch = BranchBlock.Parse(block, header, keyLength, node);

            int low = 0, high = branch.Count - 1;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                cmp++;
                if (search.Compare(branch.EntryAt(middle).Key) < 0)
                    low = middle + 1;
                else
                    high = middle;
            }

            node = branch.EntryAt(low).Child;
        }
    }

    // ---- the same algorithm, comparing in place ---------------------------

    private static uint Spans(
        NodeReader nodes, uint root, int keyLength, byte pad,
        byte[] query, int length, out long comparisons, bool dupSkip)
    {
        long cmp = 0;
        uint node = root;
        byte[] key = new byte[keyLength];

        for (;;)
        {
            byte[] block = nodes.Read(node);
            NodeHeader header = NodeHeader.Parse(block, node);
            KeySearch search = KeySearch.Into(query, length, keyLength, pad);

            if (header.IsLeaf)
            {
                LeafBlock leaf = LeafBlock.Parse(block, header, keyLength, pad, node);
                LeafGeometry geometry = leaf.Geometry;
                int textEnd = block.Length;

                // How many leading bytes of `key` are already known to equal the search value. The
                // C tracks exactly this as b4->curDupCnt and uses it to skip whole entries.
                int matched = 0;

                for (int i = 0; i < leaf.Count; i++)
                {
                    PackedEntry entry = geometry.Unpack(block, i);
                    int stored = keyLength - entry.DupCount - entry.TrailCount;
                    textEnd -= stored;

                    // The rebuild has to happen -- a compressed leaf's keys are relative -- but it
                    // goes into one reused buffer, and nothing is copied out of it.
                    block.AsSpan(textEnd, stored).CopyTo(key.AsSpan(entry.DupCount));
                    key.AsSpan(keyLength - entry.TrailCount).Fill(pad);

                    // P7: an entry sharing fewer leading bytes with its predecessor than we have
                    // already matched cannot match, and cannot be the stopping point either.
                    if (dupSkip && i > 0 && entry.DupCount < matched)
                        continue;

                    cmp++;
                    int c = search.Compare(key.AsSpan(0, keyLength));
                    if (c == 0)
                    {
                        comparisons = cmp;
                        return entry.Record;
                    }

                    if (c > 0)
                        break;

                    if (dupSkip)
                    {
                        int same = 0;
                        int width = Math.Min(length, keyLength);
                        while (same < width && key[same] == query[same])
                            same++;
                        matched = same;
                    }
                }

                comparisons = cmp;
                return 0;
            }

            // Branch keys are stored uncompressed at a fixed stride, so there is nothing to rebuild:
            // the comparison can run straight against the block.
            int entrySize = keyLength + 8;
            int low = 0, high = header.KeyCount - 1;

            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                cmp++;
                int offset = NodeHeader.Size + (middle * entrySize);
                if (search.Compare(block.AsSpan(offset, keyLength)) < 0)
                    low = middle + 1;
                else
                    high = middle;
            }

            int chosen = NodeHeader.Size + (low * entrySize) + keyLength + 4;
            node = (uint)((block[chosen] << 24) | (block[chosen + 1] << 16)
                        | (block[chosen + 2] << 8) | block[chosen + 3]);
        }
    }
}
