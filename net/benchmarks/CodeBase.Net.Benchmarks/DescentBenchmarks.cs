using BenchmarkDotNet.Attributes;
using CodeBase.Net.Cdx;
using CodeBase.Net.TestUtils;

namespace CodeBase.Net.Benchmarks;

/// <summary>
/// The comparison itself, with the file taken out of the picture.
///
/// [c]ReadPathBenchmarks[/c] measures a seek as a caller sees it, and on a file-backed table roughly
/// three fifths of that is read syscalls — so removing work from the descent moves the total by a
/// couple of per cent and says nothing about whether the work went away. These two pairs answer that
/// directly: the same search over the same real block, once through the copying path
/// ([c]EntryAt[/c], which every caller that keeps an entry still uses) and once through the narrow one
/// ([c]CompareAt[/c] and [c]ChildAt[/c]).
///
/// The blocks come from PERF10K and are loaded once in setup, so nothing here reads a file or a cache
/// while the clock runs. What is left is the comparison, the rebuild a compressed leaf costs, and —
/// in one of each pair — a key copied per comparison.
/// </summary>
[MemoryDiagnoser]
public class DescentBenchmarks
{
    private IndexFileReader index = null!;
    private LeafBlock leaf = null!;
    private BranchBlock branch = null!;
    private KeySearch search;

    /// <summary>
    /// Loads one real leaf and one real interior block out of the benchmark index.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        index = IndexFileReader.Open(
            new InMemorySource(File.ReadAllBytes(Corpus.BenchmarkPathOf("PERF10K.cdx"))),
            "PERF10K.cdx",
            _ => KeyPadding.Space);

        CdxTag tag = index.Tags.First(t => t.Name == "T_NAME");

        // The root is interior on this tag, four levels deep; its first child leads to a leaf.
        TreeBlock root = tag.ReadBlock(tag.Header.Root);
        branch = root.Branch!;

        TreeBlock down = tag.ReadBlock(branch.ChildAt(0));
        while (!down.IsLeaf)
            down = tag.ReadBlock(down.Branch!.ChildAt(0));

        leaf = down.Leaf!;

        // A key from the middle of that leaf, so a forward scan compares most of the block before it
        // stops rather than stopping at the first entry.
        search = KeySearch.For(leaf.EntryAt(leaf.Count / 2).Key, tag.KeyLength, tag.PadByte);
    }

    /// <summary>
    /// Closes the index.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup() => index.Dispose();

    /// <summary>
    /// Scans a leaf comparing against a key copied out for each entry — what the port did before.
    /// </summary>
    /// <returns>Where the scan stopped, so the loop cannot be discarded.</returns>
    [Benchmark(Baseline = true, Description = "leaf scan, key copied per comparison")]
    public int LeafScanCopying()
    {
        for (int i = 0; i < leaf.Count; i++)
        {
            if (search.Compare(leaf.EntryAt(i).Key) >= 0)
                return i;
        }

        return leaf.Count;
    }

    /// <summary>
    /// The same scan comparing in place.
    /// </summary>
    /// <returns>Where the scan stopped.</returns>
    [Benchmark(Description = "leaf scan, compared in place")]
    public int LeafScanInPlace()
    {
        for (int i = 0; i < leaf.Count; i++)
        {
            if (leaf.CompareAt(i, search) >= 0)
                return i;
        }

        return leaf.Count;
    }

    /// <summary>
    /// Binary-searches an interior block and reads the child, copying each key it compares.
    /// </summary>
    /// <returns>The child node, so the search cannot be discarded.</returns>
    [Benchmark(Description = "branch search + child, keys copied")]
    public uint BranchSearchCopying()
    {
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

        return branch.EntryAt(low).Child;
    }

    /// <summary>
    /// The same search over the block's own bytes, reading four bytes for the child.
    /// </summary>
    /// <returns>The child node.</returns>
    [Benchmark(Description = "branch search + child, in place")]
    public uint BranchSearchInPlace() => branch.ChildAt(branch.Seek(search));
}
