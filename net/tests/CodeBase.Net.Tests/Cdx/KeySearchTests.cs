using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// How a search value is prepared and compared, which is where a seek is right or wrong.
///
/// Layer one, no blocks and no file. What these catch that the corpus cannot: every combination of
/// stripping, padding and clamping, and the increment at the edges — a value of all 0xFF, and one whose
/// last byte is 0xFF but whose earlier bytes are not.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class KeySearchTests
{
    private const byte Space = 0x20;
    private const byte Nul = 0x00;

    [Fact]
    public void For_AValueWithNoTrailingPadIsAPrefixAndComparesOverItsOwnLength()
    {
        KeySearch search = Search("CUST", keyLength: 20);

        search.Length.Should().Be(4);
        search.ComparesPadded.Should().BeFalse();

        // The whole point of a partial seek: any key beginning with the value matches, however it
        // continues. A key that diverges inside the value's own length does not.
        search.Matches(Key("CUSTOMER-ALPHA      ")).Should().BeTrue();
        search.Matches(Key("CUSTARD             ")).Should().BeTrue("CUSTARD begins with CUST too");
        Search("CUSTO", keyLength: 20).Matches(Key("CUSTARD             ")).Should().BeFalse();
    }

    [Fact]
    public void For_AValueWithTrailingPadStandsForTheWholeKey()
    {
        // The case the corpus settled: stripping the pad and comparing two bytes would match
        // "AB\0\0\0\0\0\0", and the reference implementation does not — a NUL sorts below a space, so
        // that key is *before* the value rather than equal to it.
        KeySearch search = Search("AB      ", keyLength: 8);

        search.Length.Should().Be(2);
        search.ComparesPadded.Should().BeTrue();

        search.Matches(Key("AB      ")).Should().BeTrue();
        search.Compare(new byte[] { 0x41, 0x42, 0, 0, 0, 0, 0, 0 }).Should().BeNegative();
        search.Compare(Key("ABC     ")).Should().BePositive();
    }

    [Fact]
    public void For_AnAllPadValueKeepsItsLengthAndComparesAsAWholeKey()
    {
        KeySearch search = Search("        ", keyLength: 8);

        search.Length.Should().Be(8, "stripping it would leave nothing and match every key");
        search.Matches(Key("        ")).Should().BeTrue();
        search.Compare(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }).Should().BeNegative("a NUL sorts below a space");
        search.Compare(Key("A       ")).Should().BePositive();
    }

    [Fact]
    public void For_AnEmptyValueMatchesEveryKey()
    {
        KeySearch search = KeySearch.For([], keyLength: 8, Space);

        search.Length.Should().Be(0);
        search.Matches(Key("ANYTHING")).Should().BeTrue();
        search.Matches(Key("        ")).Should().BeTrue();
    }

    [Fact]
    public void For_AValueLongerThanTheKeyIsClamped()
    {
        // What the C library does (I4TAG.C:2233-2234). A caller passing more has erred, but diverging
        // from the reference over it would be the worse error.
        KeySearch search = Search("ABCDEFGHIJ", keyLength: 4);

        search.Length.Should().Be(4);
        search.Matches(Key("ABCD")).Should().BeTrue();
    }

    [Fact]
    public void For_ThePadByteIsTheTagsOwnAndNotAlwaysASpace()
    {
        // A numeric tag pads with NUL, so a value's trailing NULs are its pad — and its trailing spaces
        // are content.
        KeySearch numeric = KeySearch.For(new byte[] { 0x80, 0x00, 0x00, 0x00 }, keyLength: 4, Nul);

        numeric.Length.Should().Be(1);
        numeric.ComparesPadded.Should().BeTrue();
        numeric.Matches(new byte[] { 0x80, 0x00, 0x00, 0x00 }).Should().BeTrue();
        numeric.Compare(new byte[] { 0x80, 0x00, 0x2E, 0xE0 }).Should().BePositive();
    }

    [Fact]
    public void Compare_IsUnsigned()
    {
        // Signed bytes would put every accented character and every complemented negative on the wrong
        // side of the comparison.
        KeySearch search = KeySearch.For([0x7F], keyLength: 4, Nul);

        search.Compare([0x80, 0, 0, 0]).Should().BePositive("0x80 is above 0x7F unsigned");
        search.Compare([0x7E, 0, 0, 0]).Should().BeNegative();
    }

    [Fact]
    public void TryIncrement_APrefixRaisesItsLastByte()
    {
        Search("AB", keyLength: 8).TryIncrement(out KeySearch next).Should().BeTrue();

        next.Length.Should().Be(2);
        next.Bytes.ToArray().Should().Equal((byte)'A', (byte)'C');
    }

    [Fact]
    public void TryIncrement_APaddedValueRaisesTheLastPadByteAndNotItsContent()
    {
        // The distinction that decides whether a descending seek steps over a longer key: the successor
        // of "MIDDLE" plus pad is "MIDDLE" plus pad with the final byte raised — not "MIDDLF", which
        // would skip past "MIDDLE-EARTH".
        Search("MIDDLE  ", keyLength: 8).TryIncrement(out KeySearch next).Should().BeTrue();

        next.Bytes.ToArray().Should().Equal(
            (byte)'M', (byte)'I', (byte)'D', (byte)'D', (byte)'L', (byte)'E', 0x20, 0x21);
    }

    [Fact]
    public void TryIncrement_CarriesPastTrailing0xFFBytes()
    {
        KeySearch search = KeySearch.For([0x41, 0xFF, 0xFF], keyLength: 3, Nul);

        search.TryIncrement(out KeySearch next).Should().BeTrue();
        next.Bytes.ToArray().Should().Equal((byte)0x42, (byte)0x00, (byte)0x00);
    }

    [Fact]
    public void TryIncrement_AValueOfAllOnesHasNoSuccessor()
    {
        // Nothing sorts above it, and the C library takes a different path entirely when this happens —
        // on a descending tag it reports the end of the tag rather than its first entry.
        KeySearch search = KeySearch.For([0xFF, 0xFF], keyLength: 2, Nul);

        search.TryIncrement(out _).Should().BeFalse();
    }

    [Fact]
    public void TryIncrement_TheSuccessorIsAPrefixAndNotAPaddedValue()
    {
        // It is a boundary rather than a key: padding it out would put pad bytes after the byte just
        // raised, which would place it back below the value it is supposed to be above.
        Search("AB      ", keyLength: 8).TryIncrement(out KeySearch next).Should().BeTrue();

        next.ComparesPadded.Should().BeFalse();
        next.Compare(Key("AB      ")).Should().BeNegative();
    }

    private static KeySearch Search(string value, int keyLength) =>
        KeySearch.For(System.Text.Encoding.Latin1.GetBytes(value), keyLength, Space);

    private static byte[] Key(string key) => System.Text.Encoding.Latin1.GetBytes(key);
}
