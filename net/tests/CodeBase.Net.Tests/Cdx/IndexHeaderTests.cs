using System.Buffers.Binary;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// What a tag header says, and which headers are refused.
///
/// Layer one: no file, no block, just a span. What these catch that the corpus cannot is every header
/// the reference implementation would never write — an uncompressed index, a root of zero, a key
/// longer than the format allows, a collation whose tables live in another file.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class IndexHeaderTests
{
    [Fact]
    public void Parse_ReadsTheFieldsThatDecideHowTheTreeIsRead()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 20, root: 2048, expression: "NAME").Build();

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.Root.Should().Be(2048);
        header.KeyLength.Should().Be(20);
        header.TypeCode.Should().Be(0x20);
        header.IsCompound.Should().BeFalse();
        header.IsTagDirectory.Should().BeFalse();
        header.Descending.Should().BeFalse();
        header.Expression.Should().Be("NAME");
        header.Filter.Should().BeEmpty();
        header.Collation.Should().Be(CollationName.Machine);
    }

    [Fact]
    public void Parse_ReadsTheChangeCounterBigEndianAndTheRootLittleEndian()
    {
        // The two endiannesses sit eight bytes apart in one header, which is the format's habit and
        // the mistake most worth catching early.
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 0x00000C00).Build();
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(8), 0x0102);

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.Root.Should().Be(0x0C00, "the root is little-endian");
        header.Version.Should().Be(0x0102, "the change counter is big-endian");
    }

    [Fact]
    public void Parse_ADirectoryHeaderCarriesNoExpressionAndPadsWithSpaces()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 10, root: 2048, typeCode: 0xE0, expression: "").Build();

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.IsCompound.Should().BeTrue();
        header.IsTagDirectory.Should().BeTrue();
        header.PadByte.Should().Be(KeyPadding.Space);
    }

    [Theory]
    [InlineData(0x60, true)]
    [InlineData(0x40, true)]
    [InlineData(0x3F, false)]
    [InlineData(0x20, false)]
    public void Parse_CompoundIsTestedAsAtLeastFortyRatherThanAsOneBit(byte typeCode, bool compound)
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048, typeCode: typeCode).Build();

        IndexHeader.Parse(image, "an image").IsCompound.Should().Be(compound);
    }

    [Fact]
    public void Parse_TheOptionBitsAreNamed()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048, typeCode: 0x60 | 0x01 | 0x08).Build();

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.Options.Should().HaveFlag(TagOptions.Unique);
        header.Options.Should().HaveFlag(TagOptions.HasFilter);
        header.Options.Should().HaveFlag(TagOptions.Compact);
        header.Options.Should().NotHaveFlag(TagOptions.Candidate);
    }

    [Fact]
    public void Parse_AFilteredTagKeepsItsFilterTextAfterItsExpression()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048, typeCode: 0x68, expression: "AMOUNT").Build();

        // Both lengths count a terminating NUL, and the filter follows the expression in the area.
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x1FA), 7);
        System.Text.Encoding.ASCII.GetBytes("ID > 0").CopyTo(image.AsSpan(512 + 7));

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.Expression.Should().Be("AMOUNT");
        header.Filter.Should().Be("ID > 0");
    }

    [Fact]
    public void Parse_AnUnfilteredTagHasNoFilterEvenThoughItsHeaderDeclaresALength()
    {
        // Every header says its filter is at least one byte long, because the length counts a NUL that
        // is there whether or not a filter is. Reading it anyway would hand back a byte of padding.
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048, expression: "AMOUNT").Build();

        IndexHeader.Parse(image, "an image").FilterBytes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFFFFFu)]
    public void Parse_ARootThatIsNoBlockIsRefused(uint root)
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: root).Build();

        Action act = () => IndexHeader.Parse(image, "an image");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Parse_AnUncompressedIndexIsRefusedAndSaysWhatItIs()
    {
        // typeCode below 0x20 means the leaves are not bit-packed, which is a FoxPro 2.x index. The C
        // library refuses it at the same test (i4index.c:1706), so this is not a port limitation.
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048, typeCode: 0x01).Build();

        Action act = () => IndexHeader.Parse(image, "an image");

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported)
            .WithMessage("*uncompressed*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(241)]
    [InlineData(1024)]
    public void Parse_AKeyLengthOutsideOneToTwoHundredAndFortyIsRefused(int keyLength)
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048)
            .WithHeaderShort(12, (short)keyLength)
            .Build();

        Action act = () => IndexHeader.Parse(image, "an image");

        act.Should().Throw<CodeBaseException>();
    }

    [Fact]
    public void Parse_AGeneralCollationIsReadAndFixesThePadByteAtNul()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 40, root: 2048, collation: "GENERAL").Build();

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.Collation.Should().Be(CollationName.General);
        header.CollationText.Should().Be("GENERAL");

        // The header settles this on its own, which is why a collated tag needs no resolver: any
        // collation other than machine order pads with NUL, character keys included (ADR-27).
        header.PadByte.Should().Be(KeyPadding.Nul);
    }

    [Fact]
    public void Parse_AMachineCollatedTagLeavesThePadByteToBeSupplied()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 20, root: 2048).Build();

        // The one thing the format does not record. A character key pads with spaces and a numeric one
        // with NULs, and only the key expression's type separates them (ADR-26).
        IndexHeader.Parse(image, "an image").PadByte.Should().BeNull();
    }

    [Fact]
    public void Parse_ACodeBaseCollationIsRefusedBecauseItsTablesAreElsewhere()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 20, root: 2048, collation: "CB00004").Build();

        Action act = () => IndexHeader.Parse(image, "an image");

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported)
            .WithMessage("*CB00004*");
    }

    [Fact]
    public void Parse_ADescendingTagSaysSo()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048, descending: true).Build();

        IndexHeader.Parse(image, "an image").Descending.Should().BeTrue();
    }

    [Fact]
    public void Parse_AShortHeaderIsRefusedRatherThanPadded()
    {
        Action act = () => IndexHeader.Parse(new byte[512], "half a header");

        act.Should().Throw<CodeBaseException>().WithMessage("*512*");
    }

    [Fact]
    public void Parse_AnExpressionThatRunsPastTheHeaderIsRefused()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048).Build();
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x1FE), 600);

        Action act = () => IndexHeader.Parse(image, "an image");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Parse_TheStandardGeometryIsAssumedWhenTheHeaderDoesNotDeclareOne()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048).Build();

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.Addressing.BlockSize.Should().Be(512);
        header.Addressing.Multiplier.Should().Be(1);
    }

    [Fact]
    public void Parse_ADeclaredGeometryIsHonouredWhenTheMarkerIsThere()
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048).Build();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(16), 0xABCD);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(20), 2048);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(24), 4);

        IndexHeader header = IndexHeader.Parse(image, "an image");

        header.Addressing.BlockSize.Should().Be(2048);
        header.Addressing.Multiplier.Should().Be(4);
    }

    [Theory]
    [InlineData(600u, 1u)]
    [InlineData(2048u, 3u)]
    [InlineData(0u, 1u)]
    public void Parse_ADeclaredGeometryThatContradictsItselfIsRefused(uint blockSize, uint multiplier)
    {
        byte[] image = IndexImage.SingleTag(keyLength: 8, root: 2048).Build();
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(16), 0xABCD);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(20), blockSize);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(24), multiplier);

        Action act = () => IndexHeader.Parse(image, "an image");

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }
}
