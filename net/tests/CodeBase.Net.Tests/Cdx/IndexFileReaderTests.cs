using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// Opening an index file: both shapes, and the files that are refused.
///
/// Layer two and three over an in-memory source. What these catch that the corpus cannot: a directory
/// entry with no name, a tag header the directory points at but the file does not hold, a child
/// pointer past the end, and a short read part-way through a block. Every corpus file is valid by
/// construction, so refusals can only be tested here.
/// </summary>
[Trait("Layer", "Component")]
public sealed class IndexFileReaderTests
{
    /// <summary>Machine-collated character tags pad with spaces, which is what these images use.</summary>
    private static byte Spaces(IndexHeader header) => KeyPadding.Space;

    [Fact]
    public void Open_ASingleTagFileHoldsOneTagNamedAfterTheFile()
    {
        // A single-tag file records no name anywhere, so the file's own name is the tag's — which is
        // what the C library does with it too (i4index.c:1694).
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1), ("BRAVO", 2)])
            .Build();

        using IndexFileReader index = Open(image, "/data/PEOPLE.IDX");

        index.IsCompound.Should().BeFalse();
        index.Directory.Should().BeNull();
        index.Tags.Should().HaveCount(1);
        index.Tags[0].Name.Should().Be("PEOPLE");
    }

    [Fact]
    public void Open_ACompoundFileHoldsOneTagPerDirectoryEntry()
    {
        byte[] image = Compound(
            ("FIRST", [("ALPHA", 1), ("BRAVO", 2)]),
            ("SECOND", [("CHARLIE", 3)]));

        using IndexFileReader index = Open(image, "PEOPLE.cdx");

        index.IsCompound.Should().BeTrue();
        index.Directory.Should().NotBeNull();
        index.Tags.Select(t => t.Name).Should().Equal("FIRST", "SECOND");
        index.Tag("first").Name.Should().Be("FIRST", "a tag is found without regard to case");
    }

    [Fact]
    public void Open_TheDirectoryIsReadWithTheSameMachineryAsAnyTag()
    {
        // Which is the point of it: a leaf-decoding fault cannot wait until the first real tag is
        // walked, because opening the file already went through the same code.
        byte[] image = Compound(("ONLY", [("ALPHA", 1)]));

        using IndexFileReader index = Open(image, "PEOPLE.cdx");
        TagCursor cursor = index.Directory!.OpenCursor();

        cursor.Top().Should().BeTrue();
        System.Text.Encoding.ASCII.GetString(cursor.Current.Key).Should().Be("ONLY      ");
        cursor.Current.Record.Should().Be(IndexImage.NodeOf(1), "the entry points at the tag's header");
    }

    [Fact]
    public void Open_AskingForATagTheFileDoesNotHoldSaysWhatItDoesHold()
    {
        byte[] image = Compound(("FIRST", [("ALPHA", 1)]));

        using IndexFileReader index = Open(image, "PEOPLE.cdx");
        Action act = () => index.Tag("MISSING");

        act.Should().Throw<CodeBaseException>().WithMessage("*FIRST*");
    }

    [Fact]
    public void Open_ACollatedTagIsOpenedWithoutAskingForItsPadByte()
    {
        // The resolver is only for machine collation. A resolver that threw would prove the point, so
        // this one does (ADR-27).
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0), collation: "GENERAL")
            .WithLeaf(8, [("AB", 1)], padByte: 0x00)
            .Build();

        using IndexFileReader index = IndexFileReader.Open(
            new InMemorySource(image),
            "PEOPLE.IDX",
            _ => throw new InvalidOperationException("a collated tag settles its own pad byte"));

        index.Tags[0].PadByte.Should().Be(KeyPadding.Nul);
    }

    [Fact]
    public void Open_TheResolverIsAskedOncePerMachineCollatedTagAndNotUntilTheTagIsUsed()
    {
        // Deferred deliberately: the resolver can refuse a tag whose key expression cannot be typed, and
        // refusing while the file is being opened would make one such tag close the whole file (ADR-28).
        byte[] image = Compound(("FIRST", [("ALPHA", 1)]), ("SECOND", [("BRAVO", 2)]));
        int asked = 0;

        using IndexFileReader index = IndexFileReader.Open(
            new InMemorySource(image),
            "PEOPLE.cdx",
            header =>
            {
                asked++;
                return KeyPadding.Space;
            });

        // Not the directory either: reading it is what opening the file does, and its pad byte is a fact
        // the C library hard-codes rather than one the resolver is asked for.
        asked.Should().Be(0, "no tag has been used yet");

        _ = index.Tags[0].PadByte;
        _ = index.Tags[0].PadByte;
        _ = index.Tags[1].PadByte;

        asked.Should().Be(2, "once for each tag, however often it is asked for");
    }

    [Fact]
    public void Open_AFailureLeavesNoOpenFileBehind()
    {
        InMemorySource source = new(IndexImage.SingleTag(8, root: 0).Build());

        Action act = () => IndexFileReader.Open(source, "PEOPLE.IDX", Spaces);

        act.Should().Throw<CodeBaseException>();
        source.IsDisposed.Should().BeTrue("a caller given an exception should not also be given a handle");
    }

    [Fact]
    public void Open_ADisposedReaderHasClosedItsFile()
    {
        InMemorySource source = new(IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1)])
            .Build());

        using (IndexFileReader.Open(source, "PEOPLE.IDX", Spaces))
        {
        }

        source.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Open_ADirectoryEntryWithNoNameIsRefused()
    {
        byte[] image = Compound(("", [("ALPHA", 1)]));

        Action act = () => Open(image, "PEOPLE.cdx");

        act.Should().Throw<CodeBaseException>().WithMessage("*no name*");
    }

    [Fact]
    public void Open_ADirectoryEntryPointingPastTheEndOfTheFileIsRefused()
    {
        byte[] image = Compound(("FIRST", [("ALPHA", 1)]));

        // The directory's key text is intact; only where it points is wrong. That is exactly the shape
        // of the corruption worth refusing, because the name still reads perfectly.
        RewriteDirectoryEntry(image, node: 0x7F00);

        Action act = () => Open(image, "PEOPLE.cdx");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Read_ABlockPastTheEndOfTheFileIsRefusedAndTheMessageNamesTheNode()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1)])
            .Build();

        using IndexFileReader index = Open(image, "PEOPLE.IDX");
        Action act = () => index.Tags[0].ReadBlock(0x7F00);

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.Index)
            .WithMessage("*32512*");
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFFFFFu)]
    public void Read_ANodeThatIsNotABlockReferenceIsRefused(uint node)
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1)])
            .Build();

        using IndexFileReader index = Open(image, "PEOPLE.IDX");
        Action act = () => index.Tags[0].ReadBlock(node);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Read_ABlockInsideTheHeaderAreaIsRefused()
    {
        byte[] image = IndexImage.SingleTag(8, IndexImage.NodeOf(0))
            .WithLeaf(8, [("ALPHA", 1)])
            .Build();

        using IndexFileReader index = Open(image, "PEOPLE.IDX");
        Action act = () => index.Tags[0].ReadBlock(512);

        act.Should().Throw<CodeBaseException>().WithMessage("*header*");
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void Open_AShortReadIsRefusedRatherThanPadded()
    {
        // A header of zeros is a plausible-looking header, so a read that returns less than it was
        // asked for has to fail rather than leave the rest zero.
        Action act = () => IndexFileReader.Open(
            new FaultySource(4096, FaultySource.Fault.ShortRead),
            "PEOPLE.cdx",
            Spaces);

        act.Should().Throw<CodeBaseException>();
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void Open_AnUnreadableFileFails()
    {
        Action act = () => IndexFileReader.Open(
            new FaultySource(4096, FaultySource.Fault.Throw),
            "PEOPLE.cdx",
            Spaces);

        act.Should().Throw<IOException>();
    }

    [Trait("Layer", "Fault")]
    [Fact]
    public void Open_AFileTooShortToHoldItsHeaderFails()
    {
        Action act = () => IndexFileReader.Open(new InMemorySource(new byte[600]), "PEOPLE.cdx", Spaces);

        act.Should().Throw<CodeBaseException>();
    }

    private static IndexFileReader Open(byte[] image, string fileName) =>
        IndexFileReader.Open(new InMemorySource(image), fileName, Spaces);

    /// <summary>
    /// Builds a compound file: a directory leaf, then a header and a leaf for each tag.
    /// </summary>
    private static byte[] Compound(params (string Name, (string Key, uint Record)[] Keys)[] tags)
    {
        // Block layout: 0 is the directory's leaf, then each tag's header block pair is at a node the
        // directory records, followed by that tag's leaf.
        const int KeyLength = 8;
        const int NameLength = 10;

        List<byte[]> parts = [];
        List<(string Name, uint Node)> entries = [];

        // Header for the directory itself: keyLen 10, typeCode 0xE0, root at the first block.
        byte[] directory = IndexImage.SingleTag(
            NameLength, IndexImage.NodeOf(0), typeCode: 0xE0, expression: "").Build();

        int nodeIndex = 1;
        foreach ((string name, (string, uint)[] keys) in tags)
        {
            uint headerNode = IndexImage.NodeOf(nodeIndex);
            entries.Add((name, headerNode));

            // A tag header occupies two blocks, and its tree starts in the block after them.
            byte[] tag = IndexImage.SingleTag(KeyLength, IndexImage.NodeOf(nodeIndex + 2), typeCode: 0x60)
                .WithLeaf(KeyLength, keys)
                .Build();

            parts.Add(tag[..IndexImage.FirstBlock]);
            parts.Add(tag[IndexImage.FirstBlock..]);
            nodeIndex += 3;
        }

        byte[] directoryLeaf = IndexImage.SingleTag(NameLength, IndexImage.NodeOf(0))
            .WithLeaf(NameLength, [.. entries.Select(e => (e.Name, e.Node))])
            .Build()[IndexImage.FirstBlock..];

        byte[] image = new byte[IndexHeader.Size + IndexImage.BlockSize + parts.Sum(p => p.Length)];
        directory[..IndexHeader.Size].CopyTo(image, 0);
        directoryLeaf.CopyTo(image, IndexHeader.Size);

        int at = IndexHeader.Size + IndexImage.BlockSize;
        foreach (byte[] part in parts)
        {
            part.CopyTo(image, at);
            at += part.Length;
        }

        return image;
    }

    /// <summary>
    /// Repoints the first directory entry at another node, leaving its key text alone.
    /// </summary>
    private static void RewriteDirectoryEntry(byte[] image, uint node)
    {
        int entry = IndexHeader.Size + LeafGeometry.InfoArrayOffset;
        int infoLength = image[IndexHeader.Size + 23];
        int recordBits = image[IndexHeader.Size + 20];

        ulong packed = 0;
        for (int i = 0; i < infoLength; i++)
            packed |= (ulong)image[entry + i] << (i * 8);

        ulong counts = packed >> recordBits;
        ulong rebuilt = node | (counts << recordBits);

        for (int i = 0; i < infoLength; i++)
            image[entry + i] = (byte)(rebuilt >> (i * 8));
    }
}
