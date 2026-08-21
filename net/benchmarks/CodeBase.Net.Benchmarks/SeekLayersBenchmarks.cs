using BenchmarkDotNet.Attributes;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// Where a cached seek's time actually goes, measured rather than subtracted.
///
/// Sub-step 5 found a cached [c]Table.Seek[/c] costing 4.1 microseconds while the index descent inside
/// it costs about 0.53 from RAM, and concluded that the difference is the [c]Table[/c] layer above the
/// tree. That was a subtraction across two benchmarks of different shapes, which is exactly the kind of
/// reasoning this step has twice caught itself getting wrong — so here the layers are timed one on top
/// of another, in one process, over one cached engine.
///
/// Each arm adds what the one above it does not do:
///
/// **descent** reads and searches the tree and stops, holding the entry it landed on. **plus record**
/// adds fetching and decoding the record that entry names, which is what a caller actually wanted.
/// **plus the search value** adds turning a string into a key the way [c]Table[/c] does — encoding it,
/// padding it to the key width, deciding the comparison rule. **whole Seek** is the public call.
///
/// The gaps between the arms are the answer. Nothing is subtracted across runs.
/// </summary>
[MemoryDiagnoser]
public class SeekLayersBenchmarks
{
    /// <summary>Seeks per invocation, so every figure is per seek.</summary>
    private const int Queries = 2000;

    private CodeBaseEngine engine = null!;
    private Table table = null!;
    private Tag byName = null!;
    private FieldDefinition id = null!;
    private string[] names = null!;
    private byte[][] keys = null!;
    private byte[] buffer = null!;
    private IndexFileReader index = null!;
    private CdxTag tag = null!;
    private TagCursor cursor = null!;
    private NodeReader nodes = null!;
    private NodeReader ram = null!;
    private IRandomAccessSource memory = null!;
    private int tagKeyLength;

    /// <summary>
    /// Opens the table cached, warms the cache, and collects the values every arm will look for.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        engine = new CodeBaseEngine
        {
            BlockCache = BlockCacheMode.Always,
            DefaultEncoding = System.Text.Encoding.Latin1,
        };

        table = engine.OpenTable(Corpus.BenchmarkPathOf("PERF10K.DBF"));
        byName = table.Tags["T_NAME"];
        id = table.Fields["ID"];
        tagKeyLength = 20;   // the C(20) field this tag is over

        FieldDefinition name = table.Fields["NAME"];
        List<string> values = [];

        table.SelectTag(byName);
        for (GoResult go = table.Top(); go == GoResult.Ok && values.Count < Queries;)
        {
            values.Add(table.GetString(name));
            if (table.Skip(1) != SkipResult.Moved)
                break;
        }

        names = [.. values];

        // The same values as the key bytes a tag stores, so the lower arms do no conversion at all and
        // the cost of converting shows up where it belongs -- in the arm that adds it.
        keys = [.. names.Select(value =>
        {
            byte[] key = new byte[tagKeyLength];
            key.AsSpan().Fill((byte)' ');
            System.Text.Encoding.Latin1.GetBytes(value).AsSpan(0, Math.Min(value.Length, key.Length))
                .CopyTo(key);
            return key;
        })];

        buffer = new byte[tagKeyLength];

        // A second, separate reader over the same index, so the descent arms do not disturb the cursor
        // the Table arms use. It reads through its own cached source, so it is measuring cache hits too.
        CachingSourceFactory files = new(FileSystem.Instance) { Mode = BlockCacheMode.Always };
        IRandomAccessSource cached = files.Open(Corpus.BenchmarkPathOf("PERF10K.cdx"));
        index = IndexFileReader.Open(cached, "PERF10K.cdx", _ => KeyPadding.Space);
        tag = index.Tag("T_NAME");
        cursor = tag.OpenCursor();
        nodes = new NodeReader(cached, BlockAddressing.Standard);

        // The same file, in one array, read by the same NodeReader shape.
        memory = new InMemorySource(File.ReadAllBytes(Corpus.BenchmarkPathOf("PERF10K.cdx")));
        ram = new NodeReader(memory, BlockAddressing.Standard);

        // Warm both paths, by wall clock rather than by pass count, so nothing below is measuring the
        // first read of a window or a method's first tier of compilation.
        System.Diagnostics.Stopwatch warm = System.Diagnostics.Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 400)
        {
            BlocksFromArray();
            BlocksOnly();
            Descent();
            WholeSeek();
        }
    }

    /// <summary>
    /// Closes what was opened.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        memory.Dispose();
        index.Dispose();
        table.Dispose();
        engine.Dispose();
    }

    /// <summary>
    /// The same bare loop over the whole file held in one array, with no cache in the way.
    /// </summary>
    /// <returns>The sum of the record numbers found.</returns>
    /// <remarks>
    /// The other floor, and the one that says what a cache hit costs. Identical work to the arm below
    /// it — the same parses, the same comparisons, the same [c]byte[512][/c] allocated per block — with
    /// the only difference being where the bytes are copied from: a plain array here, the cache's own
    /// window there.
    /// </remarks>
    [Benchmark(OperationsPerInvoke = Queries, Description = "0a. blocks only, straight from an array")]
    public long BlocksFromArray()
    {
        long sum = 0;

        for (int i = 0; i < Queries; i++)
        {
            keys[i].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, buffer.Length, tag.KeyLength, tag.PadByte);
            uint node = tag.Header.Root;

            for (;;)
            {
                Block block = ram.Read(node);
                NodeHeader header = NodeHeader.Parse(block.Span, node);

                if (header.IsLeaf)
                {
                    LeafBlock leaf = LeafBlock.Parse(block, header, tagKeyLength, (byte)' ', node);

                    if (leaf.Seek(search, out int at))
                        sum += leaf.PackedAt(at).Record;

                    break;
                }

                BranchBlock branch = BranchBlock.Parse(block, header, tagKeyLength, node);
                node = branch.ChildAt(branch.Seek(search));
            }
        }

        return sum;
    }

    /// <summary>
    /// The blocks only: read, parse and search them by hand, with no cursor above.
    /// </summary>
    /// <returns>The sum of the record numbers found.</returns>
    /// <remarks>
    /// The floor. It reads through the same cached source as every other arm, so the gap between this
    /// and the next one is what [c]TagCursor[/c]'s seek protocol costs, and the gap between this and a
    /// descent over a plain in-memory array is what a cache hit costs. Two questions one subtraction
    /// across benchmarks could not tell apart, which is how the first reading of this went wrong.
    /// </remarks>
    [Benchmark(Baseline = true, OperationsPerInvoke = Queries, Description = "0. blocks only, no cursor")]
    public long BlocksOnly()
    {
        long sum = 0;

        for (int i = 0; i < Queries; i++)
        {
            keys[i].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, buffer.Length, tag.KeyLength, tag.PadByte);
            uint node = tag.Header.Root;

            for (;;)
            {
                Block block = nodes.Read(node);
                NodeHeader header = NodeHeader.Parse(block.Span, node);

                if (header.IsLeaf)
                {
                    LeafBlock leaf = LeafBlock.Parse(block, header, tag.KeyLength, tag.PadByte, node);

                    if (leaf.Seek(search, out int at))
                        sum += leaf.PackedAt(at).Record;

                    break;
                }

                BranchBlock branch = BranchBlock.Parse(block, header, tag.KeyLength, node);
                node = branch.ChildAt(branch.Seek(search));
            }
        }

        return sum;
    }

    /// <summary>
    /// The tree only: search for a key already in key form and stop where it lands.
    /// </summary>
    /// <returns>The sum of the record numbers found.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "1. + the cursor's seek protocol")]
    public long Descent()
    {
        long sum = 0;

        for (int i = 0; i < Queries; i++)
        {
            keys[i].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, buffer.Length, tag.KeyLength, tag.PadByte);

            if (cursor.Seek(search) == SeekOutcome.Found)
                sum += cursor.Current.Record;
        }

        return sum;
    }

    /// <summary>
    /// The tree, and then reading the record it named.
    /// </summary>
    /// <returns>The sum of the ID field of every record reached.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "2. + reading the record")]
    public long DescentAndRecord()
    {
        long sum = 0;

        for (int i = 0; i < Queries; i++)
        {
            keys[i].CopyTo(buffer, 0);
            KeySearch search = KeySearch.Into(buffer, buffer.Length, tag.KeyLength, tag.PadByte);

            if (cursor.Seek(search) != SeekOutcome.Found)
                continue;

            if (table.Go((int)cursor.Current.Record) == GoResult.Ok)
                sum += table.GetInt32(id);
        }

        return sum;
    }

    /// <summary>
    /// The same, with the search value built from a string the way the table builds it.
    /// </summary>
    /// <returns>The sum of the ID field of every record reached.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "3. + building the search from a string")]
    public long DescentRecordAndConversion()
    {
        long sum = 0;

        for (int i = 0; i < Queries; i++)
        {
            byte[] text = System.Text.Encoding.Latin1.GetBytes(names[i]);
            buffer.AsSpan().Fill((byte)' ');
            text.AsSpan(0, Math.Min(text.Length, buffer.Length)).CopyTo(buffer);

            KeySearch search = KeySearch.Into(buffer, buffer.Length, tag.KeyLength, tag.PadByte);

            if (cursor.Seek(search) != SeekOutcome.Found)
                continue;

            if (table.Go((int)cursor.Current.Record) == GoResult.Ok)
                sum += table.GetInt32(id);
        }

        return sum;
    }

    /// <summary>
    /// The public call, which is what a caller pays.
    /// </summary>
    /// <returns>The sum of the ID field of every record found.</returns>
    [Benchmark(OperationsPerInvoke = Queries, Description = "4. Table.Seek(string)")]
    public long WholeSeek()
    {
        long sum = 0;

        table.SelectTag(byName);

        for (int i = 0; i < Queries; i++)
        {
            if (table.Seek(names[i]) == GoResult.Ok)
                sum += table.GetInt32(id);
        }

        return sum;
    }
}
