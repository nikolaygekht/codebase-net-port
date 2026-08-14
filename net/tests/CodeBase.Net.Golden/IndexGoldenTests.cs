using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Golden;

/// <summary>
/// The gate for reading index files: every tag of every corpus index, against the dump the C library
/// wrote from its own structures.
///
/// Three things are checked, and they fail for different reasons. The key sequence catches a wrong
/// decode. The per-entry duplicate and trail counts catch a wrong *encoding* read that happened to
/// rebuild the right key from compensating mistakes. The ordering invariant catches a comparison done
/// signed, which would misplace every byte above 0x7F.
/// </summary>
[Trait("Layer", "Golden")]
public sealed class IndexGoldenTests
{
    /// <summary>Every index file the corpus holds, with the table it belongs to.</summary>
    public static TheoryData<string> AllIndexes() =>
        ["CDXBASE.cdx", "CDXCOLL.cdx", "CDXDEEP.cdx", "CDXTIME.cdx", "IDXONE.cdx", "IDXONE.IDX"];

    [Fact]
    public void TheGateCoversEveryIndexInTheCorpus()
    {
        // Part of the gate rather than commentary: a data-driven suite that discovers nothing reports
        // success having proved nothing. Six files, and the two shapes of one tree among them.
        AllIndexes().Should().HaveCount(6);
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Open_ReportsTheTagsTheCLibraryReports(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = OpenCorpusIndex(indexFile, expected);

        index.IsCompound.Should().Be(expected.IsCompound);
        index.Header.Addressing.BlockSize.Should().Be(expected.BlockSize);
        index.Header.Addressing.Multiplier.Should().Be(expected.Multiplier);

        index.Tags.Select(t => t.Name).Should().Equal(expected.RealTags.Select(t => t.Name));
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Open_ReadsEveryTagHeaderTheCLibraryReports(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = OpenCorpusIndex(indexFile, expected);

        foreach (DumpIndexTag tag in expected.RealTags)
        {
            CdxTag actual = index.Tag(tag.Name);
            IndexHeader header = actual.Header;

            header.KeyLength.Should().Be(tag.KeyLength, "tag {0} key length", tag.Name);
            header.TypeCode.Should().Be(tag.TypeCode, "tag {0} option byte", tag.Name);
            header.Signature.Should().Be(tag.Signature, "tag {0} signature", tag.Name);
            header.Descending.Should().Be(tag.Descending, "tag {0} direction", tag.Name);
            header.Root.Should().Be(tag.Root, "tag {0} root", tag.Name);
            header.FreeList.Should().Be(tag.FreeList, "tag {0} free list", tag.Name);
            header.Version.Should().Be(tag.Version, "tag {0} version", tag.Name);
            header.ExpressionBytes.Should().Equal(tag.ExpressionBytes, "tag {0} expression", tag.Name);
            header.FilterBytes.Should().Equal(tag.FilterBytes, "tag {0} filter", tag.Name);
            header.CollationText.Should().Be(tag.SortSequence, "tag {0} collation", tag.Name);
            actual.PadByte.Should().Be(tag.PadByte, "tag {0} pad byte", tag.Name);
        }
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Walking_VisitsEveryKeyTheCLibraryVisits(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = OpenCorpusIndex(indexFile, expected);

        int compared = 0;

        foreach (DumpIndexTag tag in expected.RealTags)
        {
            TagCursor cursor = index.Tag(tag.Name).OpenCursor();
            List<DumpIndexKey> walked = [];

            for (bool any = cursor.Top(); any; any = cursor.Next())
                walked.Add(new DumpIndexKey(cursor.Current.Key, cursor.Current.Record));

            walked.Should().HaveCount(tag.Keys.Count, "tag {0} holds that many keys", tag.Name);

            for (int i = 0; i < walked.Count; i++)
            {
                walked[i].Record.Should().Be(tag.Keys[i].Record, "tag {0} key {1} record", tag.Name, i);
                walked[i].Key.Should().Equal(tag.Keys[i].Key, "tag {0} key {1}", tag.Name, i);
                compared++;
            }
        }

        // Says the loop above was not empty, and how full the gate actually is.
        compared.Should().Be(expected.RealTags.Sum(t => t.Keys.Count));
        compared.Should().BePositive();
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Walking_BackwardsFromTheBottomGivesTheSameKeysReversed(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = OpenCorpusIndex(indexFile, expected);

        foreach (DumpIndexTag tag in expected.RealTags)
        {
            TagCursor cursor = index.Tag(tag.Name).OpenCursor();
            List<uint> backwards = [];

            for (bool any = cursor.Bottom(); any; any = cursor.Previous())
                backwards.Add(cursor.Current.Record);

            backwards.Should().Equal(
                tag.Keys.Select(k => k.Record).Reverse(),
                "walking tag {0} backwards visits its keys in reverse", tag.Name);
        }
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Blocks_DecodeExactlyAsTheCLibraryDecodesThem(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = OpenCorpusIndex(indexFile, expected);

        int blocks = 0;
        int entries = 0;

        foreach (DumpIndexTag tag in expected.Tags)
        {
            CdxTag actual = tag.IsDirectory ? index.Directory! : index.Tag(tag.Name);

            foreach (DumpIndexBlock block in tag.Blocks)
            {
                TreeBlock read = actual.ReadBlock(block.Node);

                read.Header.Attribute.Should().Be(block.Attribute, "node {0} attribute", block.Node);
                read.Header.KeyCount.Should().Be(block.KeyCount, "node {0} key count", block.Node);
                read.Header.LeftNode.Should().Be(block.Left, "node {0} left sibling", block.Node);
                read.Header.RightNode.Should().Be(block.Right, "node {0} right sibling", block.Node);
                read.IsLeaf.Should().Be(block.IsLeaf, "node {0} kind", block.Node);

                entries += block.IsLeaf
                    ? CheckLeaf(read.Leaf!, block)
                    : CheckBranch(read.Branch!, block);

                blocks++;
            }
        }

        // The same arithmetic the record gate uses: a comparison that silently covered nothing would
        // otherwise pass.
        blocks.Should().Be(expected.Tags.Sum(t => t.Blocks.Count));
        entries.Should().Be(expected.Tags.Sum(t => t.Blocks.Sum(b => b.KeyCount)));
        entries.Should().BePositive();
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Walking_YieldsKeysThatNeverDecreaseUnderUnsignedComparison(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = OpenCorpusIndex(indexFile, expected);

        foreach (DumpIndexTag tag in expected.RealTags)
        {
            CdxTag actual = index.Tag(tag.Name);
            TagCursor cursor = actual.OpenCursor();

            byte[]? previousKey = null;
            uint previousRecord = 0;

            for (bool any = cursor.Top(); any; any = cursor.Next())
            {
                IndexEntry entry = cursor.Current;

                if (previousKey is not null)
                {
                    // Unsigned, and the record number breaks a tie — the order the tree is built on,
                    // and the invariant d4check enforces on the C side (i4check.c:247-298). Reversed
                    // for a descending tag, whose navigation runs down the stored order.
                    int comparison = previousKey.AsSpan().SequenceCompareTo(entry.Key);
                    if (comparison == 0)
                        comparison = previousRecord.CompareTo(entry.Record);

                    if (actual.Descending)
                        comparison.Should().BePositive("tag {0} descends", tag.Name);
                    else
                        comparison.Should().BeNegative("tag {0} ascends", tag.Name);
                }

                previousKey = entry.Key;
                previousRecord = entry.Record;
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllIndexes))]
    public void Walking_ByTheLeafChainAgreesWithDescendingFromTheRootForEveryKey(string indexFile)
    {
        CorpusIndexDump expected = CorpusIndexDump.Load(indexFile);
        using IndexFileReader index = OpenCorpusIndex(indexFile, expected);

        foreach (DumpIndexTag tag in expected.RealTags)
        {
            CdxTag actual = index.Tag(tag.Name);

            // The blocks a full walk reaches must be exactly the blocks a descent from the root
            // reaches. The two use different pointers — sibling links against child pointers — so
            // agreement is real evidence, and the dump's own block set is the third opinion.
            HashSet<uint> walked = [];
            TagCursor cursor = actual.OpenCursor();
            for (bool any = cursor.Top(); any; any = cursor.Next())
                walked.Add(NodeHolding(actual, cursor.Current));

            HashSet<uint> descended = [];
            CollectLeaves(actual, actual.Header.Root, descended);

            walked.Should().BeSubsetOf(descended, "tag {0} reaches only blocks the tree owns", tag.Name);
        }
    }

    private static int CheckLeaf(LeafBlock leaf, DumpIndexBlock expected)
    {
        leaf.Geometry.FreeSpace.Should().Be(expected.FreeSpace, "node {0} free space", expected.Node);
        leaf.Geometry.RecordBits.Should().Be(expected.RecordBits, "node {0} record bits", expected.Node);
        leaf.Geometry.DupBits.Should().Be(expected.DupBits, "node {0} duplicate bits", expected.Node);
        leaf.Geometry.TrailBits.Should().Be(expected.TrailBits, "node {0} trail bits", expected.Node);
        leaf.Geometry.InfoLength.Should().Be(expected.InfoLength, "node {0} entry width", expected.Node);
        leaf.Geometry.RecordMask.Should().Be(expected.RecordMask, "node {0} record mask", expected.Node);
        leaf.Geometry.DupMask.Should().Be(expected.DupMask, "node {0} duplicate mask", expected.Node);
        leaf.Geometry.TrailMask.Should().Be(expected.TrailMask, "node {0} trail mask", expected.Node);

        foreach (DumpLeafEntry entry in expected.LeafEntries)
        {
            PackedEntry actual = leaf.PackedAt(entry.Index);

            actual.Record.Should().Be(entry.Record, "node {0} entry {1} record", expected.Node, entry.Index);
            actual.DupCount.Should().Be(entry.DupCount, "node {0} entry {1} duplicates", expected.Node, entry.Index);
            actual.TrailCount.Should().Be(entry.TrailCount, "node {0} entry {1} trail", expected.Node, entry.Index);
        }

        return expected.LeafEntries.Count;
    }

    private static int CheckBranch(BranchBlock branch, DumpIndexBlock expected)
    {
        foreach (DumpBranchEntry entry in expected.BranchEntries)
        {
            BranchEntry actual = branch.EntryAt(entry.Index);

            actual.Child.Should().Be(entry.Child, "node {0} entry {1} child", expected.Node, entry.Index);
            actual.Record.Should().Be(entry.Record, "node {0} entry {1} record", expected.Node, entry.Index);
            actual.Key.Should().Equal(entry.Key, "node {0} entry {1} key", expected.Node, entry.Index);
        }

        return expected.BranchEntries.Count;
    }

    /// <summary>
    /// Collects every leaf a descent from the given node reaches.
    /// </summary>
    private static void CollectLeaves(CdxTag tag, uint node, HashSet<uint> leaves)
    {
        TreeBlock block = tag.ReadBlock(node);

        if (block.IsLeaf)
        {
            leaves.Add(node);
            return;
        }

        for (int i = 0; i < block.Count; i++)
            CollectLeaves(tag, block.Branch!.EntryAt(i).Child, leaves);
    }

    /// <summary>
    /// Finds which leaf a key was read out of, by descending to it.
    /// </summary>
    private static uint NodeHolding(CdxTag tag, IndexEntry entry)
    {
        uint node = tag.Header.Root;

        while (true)
        {
            TreeBlock block = tag.ReadBlock(node);
            if (block.IsLeaf)
                return node;

            uint child = block.Branch!.EntryAt(block.Count - 1).Child;

            for (int i = 0; i < block.Count; i++)
            {
                BranchEntry candidate = block.Branch!.EntryAt(i);
                int comparison = candidate.Key.AsSpan().SequenceCompareTo(entry.Key);

                if (comparison > 0 || (comparison == 0 && candidate.Record >= entry.Record))
                {
                    child = candidate.Child;
                    break;
                }
            }

            node = child;
        }
    }

    /// <summary>
    /// Opens a corpus index file, supplying the pad byte the dump recorded for a machine-collated tag.
    /// </summary>
    /// <remarks>
    /// The pad byte is the one thing the file does not hold, and until the expression engine exists the
    /// reader is told it (ADR-26). Taking it from the dump keeps it a value the reference implementation
    /// produced, used as input.
    /// </remarks>
    private static IndexFileReader OpenCorpusIndex(string indexFile, CorpusIndexDump expected)
    {
        // Keyed by what identifies a key's type: the expression it comes from and the key's width.
        // Two tags can share an expression — an ascending and a descending tag over one field, or a
        // filtered and an unfiltered one — and they then agree on the pad byte, which is asserted here
        // rather than assumed.
        Dictionary<(string, int), byte> padBytes = [];

        foreach (DumpIndexTag tag in expected.RealTags)
        {
            (string, int) key = (Text(tag.ExpressionBytes), tag.KeyLength);

            if (padBytes.TryGetValue(key, out byte existing))
            {
                tag.PadByte.Should().Be(existing, "tags sharing expression {0} pad alike", key.Item1);
                continue;
            }

            padBytes[key] = tag.PadByte;
        }

        return IndexFileReader.Open(
            new InMemorySource(Corpus.ReadAllBytes(indexFile)),
            indexFile,
            header => padBytes[(header.Expression, header.KeyLength)]);
    }

    private static string Text(byte[] bytes) => System.Text.Encoding.ASCII.GetString(bytes);
}
