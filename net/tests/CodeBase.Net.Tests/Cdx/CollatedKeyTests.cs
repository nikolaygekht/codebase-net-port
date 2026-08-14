using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The GENERAL key transform, and the way a partial seek changes it.
///
/// The corpus gates what a stored key looks like. What it cannot reach is the seek side: driving the
/// reference's partial-seek path needs a generator case that records one, and none exists. So the
/// rules below are stated as relationships between keys this port builds — a prefix search must be a
/// prefix of the stored key it is meant to find — rather than as byte patterns written down.
///
/// Governing specification: KEY-COLLATION.md section 3.4 and D4SEEK.C:39-142.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class CollatedKeyTests
{
    private const int FieldWidth = 20;
    private const int KeyLength = 2 * FieldWidth;

    private static byte[] Stored(string text)
    {
        byte[] key = new byte[KeyLength];
        CollatedKey.Write(Padded(text), Table, Expansions, includeTails: true, key).Should().Be(KeyLength);
        return key;
    }

    private static (byte[] Key, int Length) Search(string text)
    {
        byte[] key = new byte[Math.Max(2 * text.Length, 2)];
        int length = CollatedKey.WriteSearch(Encoding.Latin1.GetBytes(text), Table, Expansions, KeyLength, key);
        return (key, length);
    }

    private static ReadOnlySpan<byte> Table => CollationTables.Cp1252General;

    private static ReadOnlySpan<byte> Expansions => CollationTables.Cp1252Expansions;

    private static byte[] Padded(string text) => Encoding.Latin1.GetBytes(text.PadRight(FieldWidth));

    [Fact]
    public void Write_IsCaseAndAccentInsensitiveInItsHeadsAndSeparatesOnItsTails()
    {
        // The whole point of a two-part key: the heads make "RESUME", "resume" and "résumé" order
        // together, and the tails are what still tells them apart.
        byte[] plain = Stored("resume");
        byte[] upper = Stored("RESUME");
        byte[] accented = Stored("résumé");

        upper.Should().Equal(plain, "case is not part of the primary order");
        accented.AsSpan(0, 6).ToArray().Should().Equal(plain.AsSpan(0, 6).ToArray(), "nor are accents");
        accented.Should().NotEqual(plain, "but the tails still separate them");
    }

    [Fact]
    public void Write_TrailingBlanksDoNotChangeTheKey()
    {
        // A blank sorts before everything when it is trailing, so it is removed before weighing. The
        // field is fixed width, so this is not an edge case -- every short value has them.
        Stored("AB").Should().Equal(Stored("AB    "));
    }

    [Fact]
    public void Write_AnExpandingCharacterOccupiesTwoHeads()
    {
        // Ligature oe weighs as O then E, so it takes two of the twenty head slots. A key that gave
        // it one would shift every later character left by one.
        // Written as the cp1252 byte rather than the character: U+0153 is not a Latin-1
        // code point, so encoding it that way would silently produce a question mark.
        byte[] ligature = Stored("\u009CX");
        byte[] spelled = Stored("OEX");

        ligature.AsSpan(0, 3).ToArray().Should().Equal(spelled.AsSpan(0, 3).ToArray());
    }

    [Fact]
    public void WriteSearch_AFullWidthValueKeepsItsTailsAndIsNotPartial()
    {
        // Twenty characters over a forty-byte key is the whole key, so nothing is suppressed and the
        // search is the stored key exactly.
        (byte[] key, int length) = Search(new string('A', FieldWidth));

        length.Should().Be(KeyLength);
        key.AsSpan(0, length).ToArray().Should().Equal(Stored(new string('A', FieldWidth)));
    }

    [Fact]
    public void WriteSearch_AShortValueIsCutBackToItsHeadsAndPrefixesTheStoredKey()
    {
        // The property that makes a prefix seek work at all. "MU" must be a byte-for-byte prefix of
        // the stored key of anything beginning with those letters -- including one whose tails
        // differ, which is what the suppression is for.
        (byte[] key, int length) = Search("MU");

        length.Should().Be(2, "two heads, no tails");

        foreach (string match in new[] { "MU", "MUD", "MULTIPLE", "MÜNCHEN" })
        {
            Stored(match).AsSpan(0, length).ToArray()
                .Should().Equal(key.AsSpan(0, length).ToArray(), $"'{match}' begins with MU");
        }
    }

    [Fact]
    public void WriteSearch_ASearchThatShouldNotMatch_DiffersInsideItsOwnLength()
    {
        // The other half: the prefix must not match things that merely sort nearby.
        (byte[] key, int length) = Search("MU");

        Stored("MA").AsSpan(0, length).ToArray().Should().NotEqual(key.AsSpan(0, length).ToArray());
    }

    [Fact]
    public void WriteSearch_TrailingBlanksInTheSearchChangeNothing()
    {
        // Called out in the reference at length (D4SEEK.C:106-116): seeking "A " is seeking "A",
        // because the blank is stripped before weighing and would otherwise pull nulls into the key.
        (byte[] bare, int bareLength) = Search("A");
        (byte[] padded, int paddedLength) = Search("A     ");

        paddedLength.Should().Be(bareLength);
        padded.AsSpan(0, paddedLength).ToArray().Should().Equal(bare.AsSpan(0, bareLength).ToArray());
    }

    [Fact]
    public void WriteSearch_AnExpandingCharacterStillLengthensAPartialSearch()
    {
        // Noted as a special case in the C (D4SEEK.C:97-99): a partial seek still grows when a
        // character expands, because the heads are what is being counted, not the input characters.
        Search("\u009C").Length.Should().Be(2, "one ligature, two heads");
        Search("A").Length.Should().Be(1);
    }

    [Fact]
    public void WriteSearch_AnEmptyValueSearchesForNothingRatherThanForZeros()
    {
        // A zero-length search must not become a key of zero bytes, which would match the lowest
        // key in the tag rather than every key.
        Search("").Length.Should().Be(0);
    }
}
