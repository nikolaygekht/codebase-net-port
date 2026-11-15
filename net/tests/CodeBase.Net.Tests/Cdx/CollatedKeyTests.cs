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

    // ---------------------------------------------------- the simple collations

    [Fact]
    public void Simple_EachByteBecomesItsHeadAndTheKeyKeepsTheWidth()
    {
        // t4convertSimpleChar (u4util.c:2366-2425): one head per byte, no tails, the input's length.
        byte[] value = Encoding.Latin1.GetBytes("ab  ");
        byte[] key = new byte[value.Length];

        CollatedKey.Write(value, CollationName.SpanishCp1252, 1252, includeTails: true, key).Should().Be(value.Length);

        byte[] table = CollationTables.SpanishCp1252.ToArray();
        key.Should().Equal(value.Select(b => CollationTables.Weigh(table, b).Head));
    }

    [Fact]
    public void Simple_TrailingBlanksAreWeighedLikeAnyOtherByte()
    {
        // The reference stopped stripping them for simple mappings (u4util.c:2411-2413), so a blank's
        // own head stands at the end of the key, not a zero.
        byte[] key = new byte[3];
        CollatedKey.Write("a  "u8, CollationName.CroatianCp1250, 1250, includeTails: true, key);

        key[2].Should().Be(CollationTables.Weigh(CollationTables.Cp1250Croatian, (byte)' ').Head).And.NotBe(0);
    }

    [Fact]
    public void Simple_APartialSearchIsThePrefixTranslatedAndNothingScanned()
    {
        // D4SEEK.C:104-140 skips GENERAL's scan for where the heads end when the mapping is simple:
        // the search is the value's own bytes, translated, whatever their weights.
        byte[] key = new byte[4];
        int length = CollatedKey.WriteSearch("ab"u8, CollationName.CroatianCp1250, 1250, keyLength: 12, key);

        length.Should().Be(2);
        key[..2].Should().Equal(
            CollationTables.Weigh(CollationTables.Cp1250Croatian, (byte)'a').Head,
            CollationTables.Weigh(CollationTables.Cp1250Croatian, (byte)'b').Head);
    }

    [Fact]
    public void Simple_AFullWidthSearchIsTheStoredKey()
    {
        byte[] value = Encoding.Latin1.GetBytes("abc         ");
        byte[] stored = new byte[12];
        byte[] search = new byte[12];

        CollatedKey.Write(value, CollationName.SpanishCp850, 850, includeTails: true, stored);
        CollatedKey.WriteSearch(value, CollationName.SpanishCp850, 850, keyLength: 12, search).Should().Be(12);

        search.Should().Equal(stored);
    }

    // ---------------------------------------------------- the Unicode transform

    /// <summary>Weighs UTF-16 code units under GENERAL on cp1252 into a key of twice their bytes.</summary>
    private static byte[] Wide(params char[] units)
    {
        byte[] value = Encoding.Unicode.GetBytes(units);
        byte[] key = new byte[2 * value.Length];

        CollatedKey.WriteWide(value, CollationName.General, 1252, key).Should().Be(key.Length);
        return key;
    }

    [Fact]
    public void Wide_HeadsAreBigEndianThenTailsAsZeroAndTheTail()
    {
        // t4convertSubSortCompressUnicode (i4conv.c:484-583): A is head 96 tail 0, u-umlaut head 120
        // tail 4 (COLL4ARR.C), each written as a 16-bit big-endian number.
        Wide('A', 'ü').Should().Equal(0x00, 96, 0x00, 120, 0x00, 0, 0x00, 4);
    }

    [Fact]
    public void Wide_AUnitFrom256UpWeighsAsItselfWithNoTail()
    {
        Wide('Ж', 'A').Should().Equal(0x04, 0x16, 0x00, 96, 0x00, 0, 0x00, 0x00);
    }

    [Fact]
    public void Wide_OnlyTrailingSpacesAreDroppedAndTheBlankOfAWideFieldIsWeighed()
    {
        // U+0020 at the end is dropped (i4conv.c:517-524); U+2000, what CodeBase blanks a wide field
        // with, is a unit from 256 up like any other.
        Wide('A', ' ').Should().Equal(0x00, 96, 0x00, 0, 0x00, 0, 0x00, 0x00);
        Wide('A', ' ').Should().Equal(0x00, 96, 0x20, 0x00, 0x00, 0, 0x00, 0x00);
    }

    [Fact]
    public void Wide_AUnitBelow256IsWeighedAsTheByteOfThatNumber()
    {
        // collate4setupUnicodeFromChar (u4util.c:2733-2741) copies the byte table's first 256 entries:
        // U+009C, a C1 control, is cp1252's byte 0x9C, oe, and expands; U+0153, the real oe, is a unit
        // from 256 up and keys as itself.
        // One unit keys to four bytes, which the two heads of its expansion fill: no room for tails.
        Wide('\u009C').Should().Equal(0x00, 114, 0x00, 102);
        Wide('œ').Should().Equal(0x01, 0x53, 0x00, 0x00);
    }

    [Fact]
    public void Wide_ANamedCollationIgnoresTheCodePage()
    {
        byte[] value = Encoding.Unicode.GetBytes("é");
        byte[] onCp1252 = new byte[4];
        byte[] onCp850 = new byte[4];

        CollatedKey.WriteWide(value, CollationName.GeneralCp850, 1252, onCp1252);
        CollatedKey.WriteWide(value, CollationName.GeneralCp850, 850, onCp850);

        onCp1252.Should().Equal(onCp850);
    }

    [Fact]
    public void Wide_APartialSearchIsTheHeadsOfTheGivenUnits()
    {
        // Not the reference's: its partial seek scans for a byte below sixteen and every head here
        // begins with one, so it searches zero bytes (D4SEEK.C:104-140). The heads alone are the
        // prefix of every stored key that begins with these units (ADR-85).
        byte[] key = new byte[8];

        int length = CollatedKey.WriteWideSearch(Encoding.Unicode.GetBytes("Aü"), CollationName.General, 1252, key);

        length.Should().Be(4);
        key[..4].Should().Equal(0x00, 96, 0x00, 120);
    }

    [Fact]
    public void ByName_AGeneralCollationStillWritesHeadsThenTails()
    {
        // The dispatch sends GENERAL and Avaya to the head-and-tail transform as before.
        byte[] byTables = Stored("café");
        byte[] byName = new byte[KeyLength];

        CollatedKey.Write(Padded("café"), CollationName.General, 1252, includeTails: true, byName);

        byName.Should().Equal(byTables);
    }
}
