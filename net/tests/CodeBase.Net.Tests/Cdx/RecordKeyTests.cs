using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.IO;
using CodeBase.Net.Queries;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The key a tag stores for a value: behind a null byte where the tag reserves one, and weighed
/// where the tag is collated.
/// </summary>
/// <value>
/// The promises are KEY-COLLATION.md section 5.3's for a nullable tag -- a null is the whole key
/// as zero bytes, anything else is the plain key shifted right behind 0x80 -- and, for a collated
/// tag, that a value sought in the tag finds the key a record holding it stores. The plain and
/// collated keys themselves are gated against the reference key for key in KeyTransformGoldenTests;
/// here each is the expectation for the form built around it.
/// </value>
[Trait("Layer", "Component")]
public sealed class RecordKeyTests
{
    private static readonly FieldDefinition[] Fields =
    [
        new("K_TEXT", 'C', 'C', 10, 0, 1, false, false, false, false, null),
        new("K_NTEXT", 'C', 'C', 10, 0, 11, false, false, false, false, 0),
    ];

    [Fact]
    public void ANullInANullableTagIsTheWholeKeyAsZeroBytes()
    {
        byte[] key = Enumerable.Repeat((byte)0xEE, 11).ToArray();

        int written = RecordKey.WriteValue(Converter("K_NTEXT", 11), Text("          ").AsNull(), key);

        written.Should().Be(11);
        key.Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void AValueInANullableTagIsItsPlainKeyBehindTheNullByte()
    {
        byte[] plain = Key("K_TEXT", 10, Text("APPLE     "));
        byte[] key = new byte[11];

        int written = RecordKey.WriteValue(Converter("K_NTEXT", 11), Text("APPLE     "), key);

        written.Should().Be(11);
        key.Should().Equal([0x80, .. plain]);
    }

    [Fact]
    public void ANullSortsBeforeEveryValueInTheTag()
    {
        byte[] none = new byte[11];
        byte[] blank = new byte[11];
        RecordKey.WriteValue(Converter("K_NTEXT", 11), Text("          ").AsNull(), none);
        RecordKey.WriteValue(Converter("K_NTEXT", 11), Text("          "), blank);

        none.AsSpan().SequenceCompareTo(blank).Should().BeNegative();
    }

    [Theory]
    [InlineData("APPLE     ", "APP", true)]
    [InlineData("APPLE     ", "APPLE", true)]
    [InlineData("APPLE     ", "APPLF", false)]
    // GENERAL weighs both cases of a letter alike, head and tail (KEY-COLLATION.md section 3.3:
    // entries 65 and 97 are both {96,0}), so a seek finds either spelling.
    [InlineData("apple     ", "APPLE", true)]
    [InlineData("APPLE     ", "apple", true)]
    public void ACollatedRecordKeyIsFoundBySeekingItsValue(string stored, string sought, bool found)
    {
        // A collated key carries the heads and then the tails; a seek builds the heads alone, so a
        // stored key has to begin with what seeking its value builds (KEY-COLLATION.md section 3).
        byte[] key = new byte[20];
        int written = RecordKey.WriteValue(Converter("K_TEXT", 20, "GENERAL"), Text(stored), key);

        KeySearch search = Probe("K_TEXT", 20, "GENERAL")
            .KeyFor(ConstraintValue.From(Text(sought), sought.Length));

        written.Should().Be(20);
        search.Matches(key).Should().Be(found);
    }

    [Fact]
    public void AnAccentASeekForTheHeadsCannotSeeIsStillInTheStoredKey()
    {
        // An accented letter shares its head with the plain one and carries a tail of its own
        // (KEY-COLLATION.md section 3.3), and the build form keeps the tails: two records differing
        // only in the accent store different keys, though a seek for the heads finds both.
        byte[] plain = new byte[20];
        byte[] accented = new byte[20];
        RecordKey.WriteValue(Converter("K_TEXT", 20, "GENERAL"), Text("CAFE      "), plain);
        RecordKey.WriteValue(Converter("K_TEXT", 20, "GENERAL"), Latin1("CAF\u00C9      "), accented);

        accented.Should().NotEqual(plain);
        accented[..4].Should().Equal(plain[..4], "the heads are the same, so a seek finds both");
    }

    [Fact]
    public void ACollatedKeyIsNotTheTextItsBuiltFrom()
    {
        // Guards the test above against a converter that never collated: machine keys of these
        // values would match the same seeks, so the collated key must differ from the bytes.
        byte[] key = new byte[20];
        RecordKey.WriteValue(Converter("K_TEXT", 20, "GENERAL"), Text("APPLE     "), key);

        key[..10].Should().NotEqual("APPLE     "u8.ToArray());
    }

    private static ExpressionValue Text(string value) =>
        ExpressionValue.Text(System.Text.Encoding.ASCII.GetBytes(value));

    private static ExpressionValue Latin1(string value) =>
        ExpressionValue.Text(System.Text.Encoding.Latin1.GetBytes(value));

    private static byte[] Key(string expression, int keyLength, ExpressionValue value)
    {
        byte[] key = new byte[keyLength];
        RecordKey.WriteValue(Converter(expression, keyLength), value, key);
        return key;
    }

    private static TagProbe Probe(string expression, int keyLength, string collation)
    {
        IndexHeader header = Header(expression, keyLength, collation);
        IndexFileReader index = IndexFileReader.Open(
            new InMemorySource(Image(expression, keyLength, collation)), "T.IDX", _ => header.PadByte ?? 0x20);
        return new TagProbe(index.Tags[0], Converter(expression, keyLength, collation));
    }

    private static SeekConverter Converter(string expression, int keyLength, string collation = "") =>
        SeekConverter.For(Header(expression, keyLength, collation), Fields, 1252, ExpressionOptions.Invariant, "T");

    private static IndexHeader Header(string expression, int keyLength, string collation) =>
        IndexHeader.Parse(Image(expression, keyLength, collation), "an image");

    private static byte[] Image(string expression, int keyLength, string collation) =>
        IndexImage.SingleTag(
                keyLength, IndexImage.NodeOf(0), typeCode: (byte)(expression == "K_NTEXT" ? 0x22 : 0x20),
                expression: expression, collation: collation)
            .WithLeaf(keyLength, [])
            .Build();
}
