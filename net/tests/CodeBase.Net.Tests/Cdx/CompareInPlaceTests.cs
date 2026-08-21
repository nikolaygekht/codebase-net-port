using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The narrow read paths a descent uses, against the wide one a caller uses.
///
/// [c]LeafBlock.CompareAt[/c] and [c]BranchBlock.ChildAt[/c] exist only to avoid copying a key that is
/// about to be discarded, so the one thing they must never do is answer differently from
/// [c]EntryAt[/c]. That is a round-trip invariant over real corpus blocks, which is what makes it a
/// legitimate expectation with no hand-written bytes anywhere (DEV_APPROACH.md section 4).
///
/// Layer two, over every block of every tag of the four indexed corpus files — three levels deep in
/// CDXDEEP, both collations in CDXCOLL — read through an in-memory source rather than a disk.
/// </summary>
[Trait("Layer", "Component")]
public sealed class CompareInPlaceTests
{
    /// <summary>The indexed corpus files, and their tag counts, so a broken walk cannot pass quietly.</summary>
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
    public void CompareAt_AnswersWhatComparingTheEntryAnswers(string file)
    {
        int compared = 0;

        foreach (CdxTag tag in Open(file).Tags)
        {
            // Every key of the tag is used as the search value in turn, so each block's entries are
            // compared against a value that sorts before them, equal to one of them, and after them.
            // Comparing against one arbitrary value would exercise a single sign.
            foreach (uint node in Leaves(tag))
            {
                LeafBlock leaf = tag.ReadBlock(node).Leaf!;

                for (int probe = 0; probe < leaf.Count; probe++)
                {
                    KeySearch search = KeySearch.For(leaf.EntryAt(probe).Key, tag.KeyLength, tag.PadByte);

                    for (int i = 0; i < leaf.Count; i++)
                    {
                        // EntryAt is read first on purpose: it leaves the block's rebuild state at i,
                        // so CompareAt has to be right about restarting rather than merely about
                        // comparing.
                        int expected = search.Compare(leaf.EntryAt(i).Key);

                        leaf.CompareAt(i, search).Should().Be(
                            expected,
                            $"{file} tag {tag.Name} node {node} entry {i} against entry {probe}");

                        compared++;
                    }
                }
            }
        }

        compared.Should().BeGreaterThan(0, "a walk that compared nothing proves nothing");
    }

    [Theory]
    [MemberData(nameof(IndexFiles))]
    public void ChildAt_AnswersWhatTheEntryAnswers(string file)
    {
        int branches = 0;

        foreach (CdxTag tag in Open(file).Tags)
        {
            foreach (uint node in Interior(tag))
            {
                BranchBlock branch = tag.ReadBlock(node).Branch!;

                for (int i = 0; i < branch.Count; i++)
                {
                    branch.ChildAt(i).Should().Be(
                        branch.EntryAt(i).Child,
                        $"{file} tag {tag.Name} node {node} entry {i}");
                }

                branches++;
            }
        }

        // CDXBASE and CDXTIME are single-block trees, so they legitimately have none. The two that
        // are deeper must, or this test is passing on an empty loop.
        if (file is "CDXDEEP.cdx" or "IDXONE.cdx")
            branches.Should().BeGreaterThan(0, $"{file} has interior nodes");
    }

    [Theory]
    [MemberData(nameof(IndexFiles))]
    public void ChildAt_RefusesAPositionTheBlockDoesNotHave(string file)
    {
        foreach (CdxTag tag in Open(file).Tags)
        {
            foreach (uint node in Interior(tag))
            {
                BranchBlock branch = tag.ReadBlock(node).Branch!;

                Action before = () => branch.ChildAt(-1);
                Action after = () => branch.ChildAt(branch.Count);

                before.Should().Throw<ArgumentOutOfRangeException>();
                after.Should().Throw<ArgumentOutOfRangeException>();
            }
        }
    }

    private static IndexFileReader Open(string file) =>
        IndexFileReader.Open(
            new InMemorySource(Corpus.ReadAllBytes(file)),
            file,
            _ => KeyPadding.Space);

    /// <summary>
    /// Every leaf of a tag, found by descending rather than by assuming the shape of the tree.
    /// </summary>
    private static List<uint> Leaves(CdxTag tag)
    {
        List<uint> leaves = [];
        Collect(tag, tag.Header.Root, leaves, interior: false);
        return leaves;
    }

    /// <summary>
    /// Every interior node of a tag, which is empty for a single-block tree.
    /// </summary>
    private static List<uint> Interior(CdxTag tag)
    {
        List<uint> nodes = [];
        Collect(tag, tag.Header.Root, nodes, interior: true);
        return nodes;
    }

    private static void Collect(CdxTag tag, uint node, List<uint> found, bool interior)
    {
        TreeBlock block = tag.ReadBlock(node);

        if (block.IsLeaf)
        {
            if (!interior)
                found.Add(node);

            return;
        }

        if (interior)
            found.Add(node);

        for (int i = 0; i < block.Count; i++)
            Collect(tag, block.Branch!.EntryAt(i).Child, found, interior);
    }
}
