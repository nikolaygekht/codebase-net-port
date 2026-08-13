using System.Buffers.Binary;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// Walking a tag: the ends, the steps, and crossing between blocks.
///
/// Layer two. What these catch that the corpus cannot is the state machine at its edges — stepping off
/// each end and staying there, a tag with no keys at all, an empty block in the middle of a chain — and
/// they catch it in milliseconds instead of after a tree has been built to provoke it.
/// </summary>
[Trait("Layer", "Component")]
public sealed class TagCursorTests
{
    private static byte Spaces(IndexHeader header) => KeyPadding.Space;

    [Fact]
    public void Walk_VisitsEveryKeyOfASingleBlockTagInOrder()
    {
        using IndexFileReader index = OneLeaf([("ALPHA", 1), ("BRAVO", 2), ("CHARLIE", 3)]);
        TagCursor cursor = index.Tags[0].OpenCursor();

        Keys(cursor).Should().Equal("ALPHA   ", "BRAVO   ", "CHARLIE ");
    }

    [Fact]
    public void Walk_ACursorReportsNothingUntilItIsPositioned()
    {
        using IndexFileReader index = OneLeaf([("ALPHA", 1)]);
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.IsOnKey.Should().BeFalse();
        cursor.Eof.Should().BeTrue();
        cursor.Bof.Should().BeTrue();

        Action act = () => _ = cursor.Current;
        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Info);
    }

    [Fact]
    public void Walk_SteppingOffTheEndSetsEndOfFileAndStaysThere()
    {
        using IndexFileReader index = OneLeaf([("ALPHA", 1), ("BRAVO", 2)]);
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Top().Should().BeTrue();
        cursor.Next().Should().BeTrue();
        cursor.Next().Should().BeFalse();

        cursor.Eof.Should().BeTrue();
        cursor.Bof.Should().BeFalse();
        cursor.Next().Should().BeFalse("a cursor at the end stays there");
    }

    [Fact]
    public void Walk_SteppingOffTheStartSetsBeginningOfFileAndStaysThere()
    {
        using IndexFileReader index = OneLeaf([("ALPHA", 1), ("BRAVO", 2)]);
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Bottom().Should().BeTrue();
        cursor.Previous().Should().BeTrue();
        cursor.Previous().Should().BeFalse();

        cursor.Bof.Should().BeTrue();
        cursor.Eof.Should().BeFalse();
    }

    [Fact]
    public void Walk_ATagWithNoKeysHasNoEnds()
    {
        using IndexFileReader index = OneLeaf([]);
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Top().Should().BeFalse();
        cursor.Bottom().Should().BeFalse();
        cursor.Eof.Should().BeTrue();
        cursor.Bof.Should().BeTrue();
    }

    [Fact]
    public void Walk_CrossesFromOneLeafToTheNextAndBack()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        Keys(cursor).Should().Equal("ALPHA   ", "BRAVO   ", "CHARLIE ", "DELTA   ");

        // And backwards over the same boundary, which uses the left pointers rather than the right ones.
        List<string> backwards = [];
        for (bool any = cursor.Bottom(); any; any = cursor.Previous())
            backwards.Add(System.Text.Encoding.ASCII.GetString(cursor.Current.Key));

        backwards.Should().Equal("DELTA   ", "CHARLIE ", "BRAVO   ", "ALPHA   ");
    }

    [Fact]
    public void Walk_ADescendingTagStartsAtItsGreatestKey()
    {
        // Keys are stored ascending either way: only traversal inverts. This is also the case the C
        // library's own key counter gets wrong (I4TAG.C:1000-1019).
        using IndexFileReader index = TwoLeaves(descending: true);
        TagCursor cursor = index.Tags[0].OpenCursor();

        Keys(cursor).Should().Equal("DELTA   ", "CHARLIE ", "BRAVO   ", "ALPHA   ");
    }

    [Fact]
    public void Walk_ADescendingTagsEndsAreItsOwnEndsAndNotThePhysicalOnes()
    {
        using IndexFileReader index = TwoLeaves(descending: true);
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Top().Should().BeTrue();
        System.Text.Encoding.ASCII.GetString(cursor.Current.Key).Should().Be("DELTA   ");

        cursor.Previous().Should().BeFalse("the greatest key is where a descending tag begins");
        cursor.Bof.Should().BeTrue();
        cursor.Eof.Should().BeFalse();
    }

    [Fact]
    public void Skip_MovesAsFarAsItCanAndSaysHowFarThatWas()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Top();
        cursor.Skip(2).Should().Be(2);
        System.Text.Encoding.ASCII.GetString(cursor.Current.Key).Should().Be("CHARLIE ");

        cursor.Skip(10).Should().Be(1, "there is only one key left");
        cursor.Eof.Should().BeTrue();

        cursor.Skip(0).Should().Be(0);
    }

    [Fact]
    public void Skip_BackwardsIsNegativeAndCrossesBlocks()
    {
        using IndexFileReader index = TwoLeaves();
        TagCursor cursor = index.Tags[0].OpenCursor();

        cursor.Bottom();
        cursor.Skip(-3).Should().Be(-3);
        System.Text.Encoding.ASCII.GetString(cursor.Current.Key).Should().Be("ALPHA   ");
    }

    [Fact]
    public void Walk_ATreeWithABranchIsWalkedThroughItsLeaves()
    {
        using IndexFileReader index = TwoLevelTree();
        TagCursor cursor = index.Tags[0].OpenCursor();

        Keys(cursor).Should().Equal("ALPHA   ", "BRAVO   ", "CHARLIE ", "DELTA   ");
    }

    [Fact]
    public void Walk_AnEmptyBlockInTheChainIsSteppedOverRatherThanTakenForTheEnd()
    {
        // A delete path in the reference implementation can leave a keyless block behind, and treating
        // one as the end of the tag would silently lose every key past it (CDX-FORMAT.md section 14).
        using IndexFileReader index = ChainWithEmptyMiddleBlock();
        TagCursor cursor = index.Tags[0].OpenCursor();

        Keys(cursor).Should().Equal("ALPHA   ", "ZULU    ");
    }

    [Trait("Layer", "Fault")]
    [Fact(Timeout = 10000)]
    public void Walk_ALeafChainThatCyclesThroughEmptyBlocksIsRefusedRatherThanFollowedForever()
    {
        // Empty blocks are stepped over, so a cycle made of them is a loop with no exit condition.
        // The bound cannot be a constant -- a delete path can leave a long legitimate run behind --
        // so it is the file's own size: more blocks than exist means one has been seen twice.
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [], attribute: 2, left: IndexImage.NodeOf(0), right: IndexImage.NodeOf(2))
            .WithLeaf(8, [], attribute: 2, left: IndexImage.NodeOf(1), right: IndexImage.NodeOf(1))
            .Build();

        using IndexFileReader index = IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
        TagCursor cursor = index.Tags[0].OpenCursor();
        cursor.Top();

        Action act = () => cursor.Skip(1);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Walk_ALeafChainThatReachesAnInteriorNodeIsRefused()
    {
        // A right pointer into a branch means the tree is not shaped like a tree, and following it would
        // read interior entries as bit-packed keys.
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1)], right: IndexImage.NodeOf(1))
            .WithBranch(8, [("ZULU", 1, IndexImage.NodeOf(0))])
            .Build();

        using IndexFileReader index = IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
        TagCursor cursor = index.Tags[0].OpenCursor();
        cursor.Top();

        Action act = () => cursor.Next();

        act.Should().Throw<CodeBaseException>().WithMessage("*interior node*");
    }

    [Fact]
    public void Walk_ATreeThatDoesNotReachALeafIsRefused()
    {
        // A branch whose child is itself. Without a guard this is an unbounded descent rather than an
        // error, which is the worst way for a corrupt file to fail.
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithBranch(8, [("ZULU", 1, IndexImage.NodeOf(0))])
            .Build();

        using IndexFileReader index = IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
        TagCursor cursor = index.Tags[0].OpenCursor();

        Action act = () => cursor.Top();

        act.Should().Throw<CodeBaseException>().WithMessage("*cycle*");
    }

    [Fact]
    public void Walk_TwoCursorsOverOneTagDoNotDisturbEachOther()
    {
        using IndexFileReader index = TwoLeaves();
        CdxTag tag = index.Tags[0];

        TagCursor first = tag.OpenCursor();
        TagCursor second = tag.OpenCursor();

        first.Top();
        second.Bottom();

        System.Text.Encoding.ASCII.GetString(first.Current.Key).Should().Be("ALPHA   ");
        System.Text.Encoding.ASCII.GetString(second.Current.Key).Should().Be("DELTA   ");
    }

    private static List<string> Keys(TagCursor cursor)
    {
        List<string> keys = [];

        for (bool any = cursor.Top(); any; any = cursor.Next())
            keys.Add(System.Text.Encoding.ASCII.GetString(cursor.Current.Key));

        return keys;
    }

    private static IndexFileReader OneLeaf((string Key, uint Record)[] keys)
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, keys)
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }

    /// <summary>
    /// Two leaves joined by their sibling pointers, with a root branch above them.
    /// </summary>
    private static IndexFileReader TwoLeaves(bool descending = false)
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(2), descending: descending)
            .WithLeaf(8, [("ALPHA", 1), ("BRAVO", 2)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [("CHARLIE", 3), ("DELTA", 4)], attribute: 2, left: IndexImage.NodeOf(0))
            .WithBranch(8, [("BRAVO", 2, IndexImage.NodeOf(0)), ("DELTA", 4, IndexImage.NodeOf(1))])
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }

    /// <summary>
    /// The same tree with a level in between, so a descent goes down twice.
    /// </summary>
    private static IndexFileReader TwoLevelTree()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(4))
            .WithLeaf(8, [("ALPHA", 1), ("BRAVO", 2)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [("CHARLIE", 3), ("DELTA", 4)], attribute: 2, left: IndexImage.NodeOf(0))
            .WithBranch(8, [("BRAVO", 2, IndexImage.NodeOf(0))], attribute: 0)
            .WithBranch(8, [("DELTA", 4, IndexImage.NodeOf(1))], attribute: 0)
            .WithBranch(8, [("BRAVO", 2, IndexImage.NodeOf(2)), ("DELTA", 4, IndexImage.NodeOf(3))])
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }

    /// <summary>
    /// Three leaves where the middle one holds no keys.
    /// </summary>
    private static IndexFileReader ChainWithEmptyMiddleBlock()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(3))
            .WithLeaf(8, [("ALPHA", 1)], attribute: 2, right: IndexImage.NodeOf(1))
            .WithLeaf(8, [], attribute: 2, left: IndexImage.NodeOf(0), right: IndexImage.NodeOf(2))
            .WithLeaf(8, [("ZULU", 9)], attribute: 2, left: IndexImage.NodeOf(1))
            .WithBranch(8, [("ALPHA", 1, IndexImage.NodeOf(0)), ("ZULU", 9, IndexImage.NodeOf(2))])
            .Build();

        return IndexFileReader.Open(new InMemorySource(image), "T.IDX", Spaces);
    }
}
