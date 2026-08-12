using System.Buffers.Binary;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The block layer: the common header, the bit-packing, key reconstruction, and interior entries.
///
/// Layer one, over spans. What these catch that the corpus cannot: an attribute Visual FoxPro 8 writes
/// and CodeBase never does, a packed entry six bytes wide, counts that contradict the key length, and
/// an entry array that overlaps its own key text. Every corpus block is valid, so none of it is
/// reachable from there.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class BlockDecodingTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(2, true, false)]
    [InlineData(3, true, true)]
    [InlineData(4, false, false)]
    [InlineData(5, false, true)]
    [InlineData(6, true, false)]
    [InlineData(7, true, true)]
    public void NodeHeader_ALeafIsDecidedByABitAndNotByAComparison(int attribute, bool leaf, bool root)
    {
        // The case that makes the bit test matter: an attribute of 5 is greater than 2 and is *not* a
        // leaf, because bit 0x02 is clear. Visual FoxPro 8 sets bits of its own, so a reader comparing
        // against two would read such a block as a leaf and unpack an interior node's entries as
        // bit-packed keys (b4block.c:2003-2014).
        byte[] block = new byte[512];
        BinaryPrimitives.WriteInt16LittleEndian(block, (short)attribute);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), 1);

        NodeHeader header = NodeHeader.Parse(block, 1024);

        header.IsLeaf.Should().Be(leaf);
        header.IsRoot.Should().Be(root);
    }

    [Fact]
    public void NodeHeader_SiblingsAreLittleEndianAndAbsentWhenTheyAreAllOnes()
    {
        byte[] block = new byte[512];
        BinaryPrimitives.WriteInt16LittleEndian(block, 2);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(8), 0x0C00);

        NodeHeader header = NodeHeader.Parse(block, 1024);

        header.HasLeft.Should().BeFalse();
        header.RightNode.Should().Be(0x0C00);
        header.HasRight.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public void NodeHeader_AnImpossibleKeyCountIsRefused(int keyCount)
    {
        byte[] block = new byte[512];
        BinaryPrimitives.WriteInt16LittleEndian(block, 2);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), (short)keyCount);

        Action act = () => NodeHeader.Parse(block, 1024);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Leaf_RebuildsEveryKeyFromItsSharedPrefixAndItsPadding()
    {
        (string Key, uint Record)[] keys =
        [
            ("", 5),
            ("AB", 8),
            ("ABC", 9),
            ("ABCDEFGH", 10),
            ("ZEBRA", 7),
        ];

        LeafBlock leaf = ReadLeaf(IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, keys)
            .Build());

        leaf.Count.Should().Be(5);
        Key(leaf, 0).Should().Be("        ");
        Key(leaf, 1).Should().Be("AB      ");
        Key(leaf, 2).Should().Be("ABC     ");
        Key(leaf, 3).Should().Be("ABCDEFGH");
        Key(leaf, 4).Should().Be("ZEBRA   ");
    }

    [Fact]
    public void Leaf_AnEntryCanBeAskedForTwiceAndOutOfOrder()
    {
        // Rebuilding runs forwards, so going back has to start over. A caller that walks and then
        // re-reads must not get a key built from where the last one left off.
        LeafBlock leaf = ReadLeaf(IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1), ("ALPINE", 2), ("BETA", 3)])
            .Build());

        Key(leaf, 2).Should().Be("BETA    ");
        Key(leaf, 0).Should().Be("ALPHA   ");
        Key(leaf, 1).Should().Be("ALPINE  ");
        Key(leaf, 1).Should().Be("ALPINE  ");
        Key(leaf, 0).Should().Be("ALPHA   ");
    }

    [Fact]
    public void Leaf_AllPadKeysRepeatNothingFromTheirNeighbour()
    {
        // The invariant from the format: a key that is entirely pad has no duplicate count even when the
        // key before it is also entirely pad (CDX-FORMAT.md section 6.3).
        LeafBlock leaf = ReadLeaf(IndexImage.SingleTag(6, IndexImage.NodeOf(0))
            .WithLeaf(6, [("", 4), ("", 9), ("A", 2)])
            .Build());

        leaf.PackedAt(0).Should().Be(new PackedEntry(4, 0, 6));
        leaf.PackedAt(1).Should().Be(new PackedEntry(9, 0, 6));
        Key(leaf, 1).Should().Be("      ");
    }

    [Fact]
    public void Leaf_PadIsWhateverByteTheTagPadsWith()
    {
        // A numeric key pads with NUL, and the count says how many bytes were dropped rather than what
        // they were — so the same block decodes differently depending on the tag.
        byte[] image = IndexImage.SingleTag(4, IndexImage.NodeOf(0))
            .WithLeaf(4, [("AB", 1)], padByte: 0x00)
            .Build();

        Key(ReadLeaf(image, padByte: 0x00), 0).Should().Be("AB\0\0");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Leaf_APackedEntryUnpacksAtEveryWidthTheFormatUses(int infoLength)
    {
        // Ungated by the corpus above four bytes: a five-byte entry needs a record number wider than
        // sixteen bits, which needs a table of more than sixty-five thousand records. The arithmetic is
        // the same at every width, and this is where that claim is checked.
        const int Dup = 3;
        const int Trail = 5;

        // The record number fills its field to one below the top, so the widest bit of it is exercised
        // at every width rather than only the low byte.
        int recordBits = (infoLength * 8) - 16;
        uint record = (uint)((1L << recordBits) - 2);
        byte[] block = new byte[512];
        BinaryPrimitives.WriteInt16LittleEndian(block, 2);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(14), (uint)((1L << recordBits) - 1));
        block[18] = 0xFF;
        block[19] = 0xFF;
        block[20] = (byte)recordBits;
        block[21] = 8;
        block[22] = 8;
        block[23] = (byte)infoLength;

        ulong packed = record | ((ulong)Dup << recordBits) | ((ulong)Trail << (recordBits + 8));
        for (int b = 0; b < infoLength; b++)
            block[LeafGeometry.InfoArrayOffset + b] = (byte)(packed >> (b * 8));

        LeafGeometry geometry = LeafGeometry.Parse(block, 1024);

        geometry.InfoLength.Should().Be(infoLength);
        geometry.Unpack(block, 0).Should().Be(new PackedEntry(record, Dup, Trail));
    }

    [Fact]
    public void Leaf_BitWidthsThatDoNotFillTheEntryAreRefused()
    {
        // The three fields are packed end to end into a whole number of bytes, so widths that do not
        // add up mean this decoder cannot take an entry apart — and would otherwise hand back a
        // plausible record number.
        byte[] block = LeafGeometryBlock(recordBits: 12, dupBits: 4, trailBits: 4, infoLength: 3);
        block[20] = 11;

        Action act = () => LeafGeometry.Parse(block, 1024);

        act.Should().Throw<CodeBaseException>().WithMessage("*bits*");
    }

    [Fact]
    public void Leaf_AnEntryArrayThatWouldRunPastTheBlockIsRefused()
    {
        byte[] block = LeafGeometryBlock(recordBits: 16, dupBits: 8, trailBits: 8, infoLength: 4);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), 200);

        Action act = () => LeafBlock.Parse(block, NodeHeader.Parse(block, 1024), 8, 0x20, 1024);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Leaf_CountsThatLeaveNegativeStoredBytesAreRefused()
    {
        // dup plus trail above the key length is how a corrupt leaf shows up here, and it is where a
        // decoder that carried on would start returning bytes of whatever lies nearby.
        byte[] block = LeafGeometryBlock(recordBits: 16, dupBits: 8, trailBits: 8, infoLength: 4);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), 1);

        ulong packed = 1u | (6ul << 16) | (6ul << 24);
        for (int b = 0; b < 4; b++)
            block[LeafGeometry.InfoArrayOffset + b] = (byte)(packed >> (b * 8));

        LeafBlock leaf = LeafBlock.Parse(block, NodeHeader.Parse(block, 1024), keyLength: 8, 0x20, 1024);

        Action act = () => leaf.EntryAt(0);

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.Index)
            .WithMessage("*leaves -4 stored*");
    }

    [Fact]
    public void Branch_ReadsBothNumbersBigEndianAndTheKeyWhole()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithBranch(8, [("ALPHA", 17, 4096), ("ZULU", 42, 8192)])
            .Build();

        BranchBlock branch = ReadBranch(image, keyLength: 8);

        branch.Count.Should().Be(2);

        BranchEntry first = branch.EntryAt(0);
        System.Text.Encoding.ASCII.GetString(first.Key).Should().Be("ALPHA   ");
        first.Record.Should().Be(17);
        first.Child.Should().Be(4096);

        branch.EntryAt(1).Child.Should().Be(8192);
    }

    [Fact]
    public void Branch_EntriesThatWouldRunPastTheBlockAreRefused()
    {
        byte[] block = new byte[512];
        BinaryPrimitives.WriteInt16LittleEndian(block, 1);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), 60);

        Action act = () => BranchBlock.Parse(block, NodeHeader.Parse(block, 1024), keyLength: 40, 1024);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    private static byte[] LeafGeometryBlock(int recordBits, int dupBits, int trailBits, int infoLength)
    {
        byte[] block = new byte[512];
        BinaryPrimitives.WriteInt16LittleEndian(block, 2);
        BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(14), 0xFFFF);
        block[18] = 0xFF;
        block[19] = 0xFF;
        block[20] = (byte)recordBits;
        block[21] = (byte)dupBits;
        block[22] = (byte)trailBits;
        block[23] = (byte)infoLength;

        return block;
    }

    private static LeafBlock ReadLeaf(byte[] image, byte padByte = 0x20)
    {
        byte[] block = image[IndexImage.FirstBlock..(IndexImage.FirstBlock + IndexImage.BlockSize)];
        IndexHeader header = IndexHeader.Parse(image, "an image");

        return LeafBlock.Parse(block, NodeHeader.Parse(block, 1024), header.KeyLength, padByte, 1024);
    }

    private static BranchBlock ReadBranch(byte[] image, int keyLength)
    {
        byte[] block = image[IndexImage.FirstBlock..(IndexImage.FirstBlock + IndexImage.BlockSize)];

        return BranchBlock.Parse(block, NodeHeader.Parse(block, 1024), keyLength, 1024);
    }

    private static string Key(LeafBlock leaf, int index) =>
        System.Text.Encoding.Latin1.GetString(leaf.EntryAt(index).Key);
}
