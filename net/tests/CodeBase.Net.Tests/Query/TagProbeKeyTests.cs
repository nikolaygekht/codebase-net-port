using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.IO;
using CodeBase.Net.Queries;
using CodeBase.Net.Tests.Cdx;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// The key a tag probe seeks for a constraint's value, for each kind of key.
/// </summary>
/// <value>
/// The promise is that a value is sought by the very key a record holding it stores, so the
/// expectation is that stored key, built by [c]RecordKey[/c] — which the corpus gates key for key in
/// KeyTransformGoldenTests — from the value in the tag's own domain. A text shorter than a character
/// key stays a prefix; a tag whose keys carry a null byte gets it in front.
/// </value>
[Trait("Layer", "Component")]
public sealed class TagProbeKeyTests
{
    private static readonly FieldDefinition[] Fields =
    [
        new("K_INT", 'I', 'I', 4, 0, 1, false, false, false, false, null),
        new("K_CUR", 'Y', 'Y', 8, 4, 5, false, false, false, false, null),
        new("K_LOG", 'L', 'L', 1, 0, 13, false, false, false, false, null),
        new("K_NUM", 'N', 'N', 12, 3, 14, false, false, false, false, null),
        new("K_FLT", 'H', 'H', 4, 0, 26, false, false, false, false, null),
        new("K_TEXT", 'C', 'C', 10, 0, 30, false, false, false, false, null),
        new("K_NTEXT", 'C', 'C', 10, 0, 40, false, false, false, false, 0),
    ];

    [Theory]
    // Each value against the key a record holding it stores, and against one holding a neighbour.
    [InlineData("K_INT", 4, "Int32")]
    [InlineData("K_CUR", 8, "Currency")]
    [InlineData("K_FLT", 4, "Single")]
    [InlineData("K_NUM", 8, "Double")]
    public void ANumberIsSoughtByTheKeyARecordHoldingItStores(string expression, int keyLength, string kind)
    {
        (ExpressionValue stored, ExpressionValue neighbour) = kind switch
        {
            "Int32" => (ExpressionValue.Whole(3), ExpressionValue.Whole(4)),
            "Currency" => (ExpressionValue.Currency(12345), ExpressionValue.Currency(12346)),
            "Single" => (ExpressionValue.Float(0.5), ExpressionValue.Float(0.75)),
            _ => (ExpressionValue.Numeric(2.5), ExpressionValue.Numeric(2.75)),
        };
        double sought = kind switch { "Int32" => 3, "Currency" => 1.2345, "Single" => 0.5, _ => 2.5 };

        KeySearch search = Search(expression, keyLength, ExpressionValue.Numeric(sought));

        search.Matches(Stored(expression, keyLength, stored)).Should().BeTrue();
        search.Matches(Stored(expression, keyLength, neighbour)).Should().BeFalse();
    }

    [Fact]
    public void ATruthValueIsSoughtAsItsLetter()
    {
        KeySearch search = Search("K_LOG", 1, ExpressionValue.Logic(true));

        search.Matches(Stored("K_LOG", 1, ExpressionValue.Logic(true))).Should().BeTrue();
        search.Matches(Stored("K_LOG", 1, ExpressionValue.Logic(false))).Should().BeFalse();
    }

    [Fact]
    public void AShortTextIsSoughtAsAPrefix() =>
        Sought("K_TEXT", 10, Text("AB")).Should().Equal("AB"u8.ToArray());

    [Fact]
    public void ATextInANullableTagIsSoughtBehindTheNullByte() =>
        Sought("K_NTEXT", 11, Text("AB")).Should().Equal(0x80, (byte)'A', (byte)'B');

    [Fact]
    public void AShortTextInACollatedTagIsSoughtByItsHeadsAlone()
    {
        // A prefix under a collation keeps the heads a shorter value can predict (KEY-COLLATION.md).
        byte[] heads = new byte[20];
        int length = CollatedKey.WriteSearch(
            "AB"u8,
            CollationWeights.TableFor(CollationName.General, 1252),
            CollationWeights.ExpansionsFor(CollationName.General, 1252),
            20,
            heads);

        Sought("K_TEXT", 20, Text("AB"), "GENERAL").Should().Equal(heads[..length]);
    }

    [Fact]
    public void TheProbeReportsItsTagsKeyLength() =>
        Probe("K_TEXT", 10).KeyLength.Should().Be(10);

    private static ExpressionValue Text(string value) =>
        ExpressionValue.Text(System.Text.Encoding.ASCII.GetBytes(value));

    private static byte[] Sought(string expression, int keyLength, ExpressionValue value, string collation = "") =>
        Search(expression, keyLength, value, collation).Bytes.ToArray();

    private static KeySearch Search(string expression, int keyLength, ExpressionValue value, string collation = "") =>
        Probe(expression, keyLength, collation).KeyFor(ConstraintValue.From(value, value.Bytes.Length));

    private static byte[] Stored(string expression, int keyLength, ExpressionValue value)
    {
        byte[] key = new byte[keyLength];
        RecordKey.WriteValue(Converter(Header(expression, keyLength, "")), value, key);
        return key;
    }

    private static TagProbe Probe(string expression, int keyLength, string collation = "")
    {
        IndexHeader header = Header(expression, keyLength, collation);
        IndexFileReader index = IndexFileReader.Open(
            new InMemorySource(Image(expression, keyLength, collation)), "T.IDX", _ => header.PadByte ?? 0x20);
        return new TagProbe(index.Tags[0], Converter(header));
    }

    private static SeekConverter Converter(IndexHeader header) =>
        SeekConverter.For(header, Fields, 1252, ExpressionOptions.Invariant, "T");

    private static IndexHeader Header(string expression, int keyLength, string collation) =>
        IndexHeader.Parse(Image(expression, keyLength, collation), "an image");

    private static byte[] Image(string expression, int keyLength, string collation) =>
        IndexImage.SingleTag(
                keyLength, IndexImage.NodeOf(0), typeCode: (byte)(expression == "K_NTEXT" ? 0x22 : 0x20),
                expression: expression, collation: collation)
            .WithLeaf(keyLength, [])
            .Build();
}
