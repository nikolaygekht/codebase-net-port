using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// That a block decodes the same wherever it sits in whatever buffer.
///
/// This is the invariant [c]Block[/c] exists for, and it was not true before it. A leaf's key text
/// grows *downwards from the end of the block*, so the block's end decides where every key is rebuilt
/// from — and that end used to be taken from the array's length. A buffer holding more than the block,
/// which is what any pool or any multi-block window hands out, would have moved it and produced wrong
/// keys with nothing raising an error.
///
/// The oversized buffer here is the index file itself, which needs no construction: a block inside it
/// is a real block at a real non-zero offset, in a buffer hundreds of times its length.
///
/// Layer two, over the real corpus, asserting a round-trip rather than an invented expectation
/// (DEV_APPROACH.md section 4).
/// </summary>
[Trait("Layer", "Component")]
public sealed class BlockSlicingTests
{
    /// <summary>Every indexed corpus file, so no tag shape is left out.</summary>
    public static TheoryData<string> IndexFiles() => new()
    {
        "CDXBASE.cdx",
        "CDXDEEP.cdx",
        "CDXCOLL.cdx",
        "CDXTIME.cdx",
        "IDXONE.cdx",
    };

    [Theory]
    [MemberData(nameof(IndexFiles))]
    public void ALeafDecodesTheSameFromAWholeFileBufferAsFromItsOwnArray(string file)
    {
        byte[] whole = Corpus.ReadAllBytes(file);
        int blockSize = BlockAddressing.StandardBlockSize;
        int leaves = 0;

        foreach ((CdxTag tag, uint node) in LeavesOf(file, whole))
        {
            int offset = (int)BlockAddressing.Standard.OffsetOf(node);

            // The same bytes twice: once as a block sitting deep inside the whole file, once copied
            // out into an array of exactly its own size — which is what the reader used to require.
            Block inWholeFile = new(whole, offset, blockSize);
            Block onItsOwn = new(whole.AsSpan(offset, blockSize).ToArray());

            List<byte[]> fromFile = KeysOf(tag, inWholeFile, node);
            List<byte[]> fromArray = KeysOf(tag, onItsOwn, node);

            fromFile.Should().HaveCount(fromArray.Count, $"{file} tag {tag.Name} node {node}");
            for (int i = 0; i < fromArray.Count; i++)
                fromFile[i].Should().Equal(fromArray[i], $"{file} tag {tag.Name} node {node} key {i}");

            leaves++;
        }

        leaves.Should().BeGreaterThan(0, "a run over no leaves proves nothing");
    }

    [Theory]
    [MemberData(nameof(IndexFiles))]
    public void ALeafToldItRunsToTheEndOfTheFileDoesNotDecodeTheSame(string file)
    {
        // The vacuity guard, and the shape of the bug Block prevents: a length taken from the buffer
        // rather than from the block. The keys must come out different, or throw — either is the
        // reader noticing. If this ever passes quietly, the test above is not testing anything.
        byte[] whole = Corpus.ReadAllBytes(file);
        int blockSize = BlockAddressing.StandardBlockSize;
        bool anyLeafChecked = false;

        foreach ((CdxTag tag, uint node) in LeavesOf(file, whole))
        {
            int offset = (int)BlockAddressing.Standard.OffsetOf(node);
            if (offset + blockSize >= whole.Length)
                continue;   // the last block of the file has nothing past it to be wrong about

            List<byte[]> right = KeysOf(tag, new Block(whole, offset, blockSize), node);

            bool differs;
            try
            {
                List<byte[]> wrong = KeysOf(tag, new Block(whole, offset, whole.Length - offset), node);
                differs = wrong.Count != right.Count
                    || wrong.Zip(right).Any(pair => !pair.First.SequenceEqual(pair.Second));
            }
            catch (CodeBaseException)
            {
                differs = true;
            }

            differs.Should().BeTrue($"{file} tag {tag.Name} node {node} must not decode the same");
            anyLeafChecked = true;
        }

        anyLeafChecked.Should().BeTrue("no leaf had anything after it, so nothing was checked");
    }

    private static List<byte[]> KeysOf(CdxTag tag, Block block, uint node)
    {
        LeafBlock leaf = LeafBlock.Parse(
            block, NodeHeader.Parse(block.Span, node), tag.KeyLength, tag.PadByte, node);

        List<byte[]> keys = [];
        for (int i = 0; i < leaf.Count; i++)
            keys.Add(leaf.EntryAt(i).Key);

        return keys;
    }

    /// <summary>
    /// Every leaf of every tag of a file, found by descending rather than by assuming a shape.
    /// </summary>
    private static IEnumerable<(CdxTag Tag, uint Node)> LeavesOf(string file, byte[] whole)
    {
        using IndexFileReader index = IndexFileReader.Open(
            new InMemorySource(whole), file, _ => KeyPadding.Space);

        foreach (CdxTag tag in index.Tags)
        {
            foreach (uint node in Descend(tag, tag.Header.Root))
                yield return (tag, node);
        }
    }

    private static IEnumerable<uint> Descend(CdxTag tag, uint node)
    {
        TreeBlock block = tag.ReadBlock(node);

        if (block.IsLeaf)
        {
            yield return node;
            yield break;
        }

        for (int i = 0; i < block.Count; i++)
        {
            foreach (uint found in Descend(tag, block.Branch!.ChildAt(i)))
                yield return found;
        }
    }
}
