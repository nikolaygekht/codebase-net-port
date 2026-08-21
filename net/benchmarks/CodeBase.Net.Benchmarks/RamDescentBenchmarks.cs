using BenchmarkDotNet.Attributes;
using CodeBase.Net.Cdx;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// The audit's own measurement, reproduced so its claim can be checked rather than believed.
///
/// [c]ANALYSIS.md[/c] section 6 priced compare-in-place at **1.7 to 1.9 times the descent** — 0.695 to
/// 0.401 microseconds per character seek and 1.061 to 0.550 per numeric one — from
/// [c]bench.exe out N micro[/c], a throwaway harness outside the solution. Sub-step 2 landed the change
/// and the end-to-end seek moved half a per cent, so one of the two numbers needs explaining.
///
/// This is that harness's shape, which is neither of the two things sub-step 2 first measured: a
/// **whole descent** — read the block, parse it, search it, descend — over a **RAM-backed** source, so
/// there are no syscalls to drown the result and no [c]Table[/c] above it adding a record read and a
/// string to encode. Both variants run in one process over the same blocks and must return the same
/// checksum, so the only difference between them is the copy.
///
/// The copying variant is written out here rather than reached through the library, because the library
/// no longer has that path — which is the point of keeping it: a claim about what was removed can only
/// be checked against something that still does it.
/// </summary>
[MemoryDiagnoser]
public class RamDescentBenchmarks
{
    /// <summary>Seeks per invocation, so the reported figure is microseconds per seek.</summary>
    private const int Queries = 10000;

    /// <summary>Coprime with the count, so the order asked is unrelated to the order stored.</summary>
    private const int QueryStride = 3571;

    private IndexFileReader index = null!;
    private IRandomAccessSource source = null!;
    private NodeReader nodes = null!;
    private byte[][] keys = null!;
    private byte[] buffer = null!;
    private uint root;
    private int keyLength;
    private byte pad;

    /// <summary>
    /// Which tag to descend: the four-level character tree or the three-level numeric one.
    /// </summary>
    [Params("T_NAME", "T_AMT")]
    public string TagName { get; set; } = "T_NAME";

    /// <summary>
    /// Loads the index into memory and collects every stored key of the tag under test.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        // The pad byte a bare-field tag uses is derived from the table's descriptors (ADR-28), which
        // would mean opening the table. Supplied per tag instead, exactly as the audit's harness did:
        // 0x20 for the C(20) tag, 0x00 for the N(12,2) one. Getting it wrong does not fail loudly — it
        // makes every seek miss, and measures the miss path.
        pad = TagName == "T_NAME" ? (byte)0x20 : (byte)0x00;

        source = new InMemorySource(File.ReadAllBytes(Corpus.BenchmarkPathOf("PERF10K.cdx")));
        index = IndexFileReader.Open(source, "PERF10K.cdx", _ => pad);

        CdxTag tag = index.Tag(TagName);
        keyLength = tag.KeyLength;
        root = tag.Header.Root;
        nodes = new NodeReader(source, BlockAddressing.Standard);
        buffer = new byte[keyLength];

        // The queries are the tag's own keys, read out of the tree by walking it, so no value has to
        // be transformed into key form here and none of them can miss.
        List<byte[]> stored = [];
        TagCursor cursor = tag.OpenCursor();
        for (bool more = cursor.Top(); more; more = cursor.Next())
            stored.Add(cursor.Current.Key);

        if (stored.Count != Queries)
            throw new InvalidOperationException($"{TagName} holds {stored.Count} keys, expected {Queries}.");

        keys = new byte[Queries][];
        for (int i = 0; i < Queries; i++)
            keys[i] = stored[(i * QueryStride) % Queries];
    }

    /// <summary>
    /// Releases the index.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup() => index.Dispose();

    /// <summary>
    /// Descends for every key, copying each compared key out of its block — the shipped path before
    /// sub-step 2.
    /// </summary>
    /// <returns>The sum of the record numbers found, so no seek can be discarded.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = Queries, Description = "descent, key copied per comparison")]
    public long Copying()
    {
        long sum = 0;

        for (int q = 0; q < Queries; q++)
        {
            keys[q].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, keyLength, keyLength, pad);
            uint node = root;

            for (;;)
            {
                byte[] block = nodes.Read(node);
                NodeHeader header = NodeHeader.Parse(block, node);

                if (header.IsLeaf)
                {
                    LeafBlock leaf = LeafBlock.Parse(block, header, keyLength, pad, node);

                    for (int i = 0; i < leaf.Count; i++)
                    {
                        int c = search.Compare(leaf.EntryAt(i).Key);

                        if (c == 0)
                        {
                            sum += leaf.EntryAt(i).Record;
                            break;
                        }

                        if (c > 0)
                            break;
                    }

                    break;
                }

                BranchBlock branch = BranchBlock.Parse(block, header, keyLength, node);
                int low = 0;
                int high = branch.Count - 1;

                while (low < high)
                {
                    int middle = low + ((high - low) / 2);

                    if (search.Compare(branch.EntryAt(middle).Key) < 0)
                        low = middle + 1;
                    else
                        high = middle;
                }

                node = branch.EntryAt(low).Child;
            }
        }

        return sum;
    }

    /// <summary>
    /// The same descent with the audit's own compare-in-place loop, rebuilt here verbatim.
    /// </summary>
    /// <returns>The sum of the record numbers found, which must equal the other variants'.</returns>
    /// <remarks>
    /// This is what [c]ANALYSIS.md[/c] section 6 actually priced, and it is not what the library does:
    /// the rebuild is **inlined into the scan**, so an entry costs one [c]Unpack[/c], two span writes
    /// and a comparison, with no call through a position-taking method, no argument checks per entry,
    /// and no catch-up loop. On a match it returns the record from the entry it already unpacked.
    ///
    /// Kept as a third variant rather than adopted wholesale, because two of the differences are
    /// checks the library must not drop — a stored length below zero and key text running into the
    /// entry array are how a corrupt leaf shows up, and the audit's harness omitted both.
    /// </remarks>
    [Benchmark(OperationsPerInvoke = Queries, Description = "descent, audit's inlined spans loop")]
    public long AuditSpans()
    {
        long sum = 0;
        byte[] rebuilt = new byte[keyLength];

        for (int q = 0; q < Queries; q++)
        {
            keys[q].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, keyLength, keyLength, pad);
            uint node = root;

            for (;;)
            {
                byte[] block = nodes.Read(node);
                NodeHeader header = NodeHeader.Parse(block, node);

                if (header.IsLeaf)
                {
                    LeafBlock leaf = LeafBlock.Parse(block, header, keyLength, pad, node);
                    LeafGeometry geometry = leaf.Geometry;
                    int textEnd = block.Length;

                    for (int i = 0; i < leaf.Count; i++)
                    {
                        PackedEntry entry = geometry.Unpack(block, i);
                        int stored = keyLength - entry.DupCount - entry.TrailCount;
                        textEnd -= stored;

                        block.AsSpan(textEnd, stored).CopyTo(rebuilt.AsSpan(entry.DupCount));
                        rebuilt.AsSpan(keyLength - entry.TrailCount).Fill(pad);

                        int c = search.Compare(rebuilt.AsSpan(0, keyLength));

                        if (c == 0)
                        {
                            sum += entry.Record;
                            break;
                        }

                        if (c > 0)
                            break;
                    }

                    break;
                }

                BranchBlock branch = BranchBlock.Parse(block, header, keyLength, node);
                int entrySize = keyLength + 8;
                int low = 0;
                int high = header.KeyCount - 1;

                while (low < high)
                {
                    int middle = low + ((high - low) / 2);
                    int offset = NodeHeader.Size + (middle * entrySize);

                    if (search.Compare(block.AsSpan(offset, keyLength)) < 0)
                        low = middle + 1;
                    else
                        high = middle;
                }

                node = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                    block.AsSpan(NodeHeader.Size + (low * entrySize) + keyLength + 4, 4));
            }
        }

        return sum;
    }

    /// <summary>
    /// The same descent through the library as it stands, comparing in place.
    /// </summary>
    /// <returns>The sum of the record numbers found, which must equal the copying variant's.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "descent, compared in place")]
    public long InPlace()
    {
        long sum = 0;

        for (int q = 0; q < Queries; q++)
        {
            keys[q].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, keyLength, keyLength, pad);
            uint node = root;

            for (;;)
            {
                byte[] block = nodes.Read(node);
                NodeHeader header = NodeHeader.Parse(block, node);

                if (header.IsLeaf)
                {
                    LeafBlock leaf = LeafBlock.Parse(block, header, keyLength, pad, node);

                    if (leaf.Seek(search, out int at))
                        sum += leaf.PackedAt(at).Record;

                    break;
                }

                BranchBlock branch = BranchBlock.Parse(block, header, keyLength, node);
                node = branch.ChildAt(branch.Seek(search));
            }
        }

        return sum;
    }
}
