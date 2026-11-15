using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The weighed sequences of Visual FoxPro's named collations: contractions, and expansions no two characters spell.
///
/// The bytes are gated by the oracles VFP wrote (VisualFoxProGeneralGoldenTests). What is stated here
/// is the rule in relationships between keys this port builds: which characters take one head
/// between them, which match wins, and what a partial seek of a contraction covers.
///
/// Governing decision: ADR-88.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class CollatedKeySequenceTests
{
    private const int FieldWidth = 10;
    private const int KeyLength = 2 * FieldWidth;

    private static byte[] Stored(string text, CollationName collation, int codePage)
    {
        byte[] key = new byte[KeyLength];
        CollatedKey.Write(Encoding.Latin1.GetBytes(text.PadRight(FieldWidth)), collation, codePage, includeTails: true, key)
            .Should().Be(KeyLength);
        return key;
    }

    private static byte Head(string character, CollationName collation, int codePage) => Stored(character, collation, codePage)[0];

    [Fact]
    public void CzechChIsOneCharacterInEveryCase()
    {
        byte[] key = Stored("CHA", CollationName.Czech, 1250);

        key[0].Should().NotBe(Head("C", CollationName.Czech, 1250), "CH is a letter of its own");
        key[1].Should().Be(Head("A", CollationName.Czech, 1250), "the A after it is the second head");

        foreach (string spelling in new[] { "Ch", "cH", "ch" })
            Stored(spelling, CollationName.Czech, 1250)[0].Should().Be(key[0], spelling);
    }

    [Fact]
    public void TheLongestSequenceWins()
    {
        // DZ and DZS are both letters in Hungarian; DZS takes the three bytes.
        byte[] dzs = Stored("DZS", CollationName.Hungary, 1250);

        dzs[0].Should().NotBe(Head("DZ", CollationName.Hungary, 1250));
        dzs[1].Should().BeLessThan(16, "DZS keys as one head");
    }

    [Fact]
    public void SequencesAreMatchedLeftToRight()
    {
        // CCS is C then CS, DDZS is D then DZS -- the doubled spellings VFP's keys show.
        Stored("CCS", CollationName.Hungary, 1250).AsSpan(0, 2).ToArray().Should().Equal(
            Head("C", CollationName.Hungary, 1250), Head("CS", CollationName.Hungary, 1250));
        Stored("DDZS", CollationName.Hungary, 1250).AsSpan(0, 2).ToArray().Should().Equal(
            Head("D", CollationName.Hungary, 1250), Head("DZS", CollationName.Hungary, 1250));
    }

    [Fact]
    public void ASequenceOfOneByteWritesItsOwnHeadsAndTails()
    {
        // SWEFIN keys AE ligature as A and E with one tail between them, where A and E carry one
        // each: the heads agree with AE spelled out, the tails do not.
        byte[] ligature = Stored("ÆW", CollationName.Swefin, 1252);
        byte[] spelled = Stored("AEW", CollationName.Swefin, 1252);

        ligature.AsSpan(0, 3).ToArray().Should().Equal(spelled.AsSpan(0, 3).ToArray());
        ligature.Should().NotEqual(spelled);
    }

    [Fact]
    public void APartialSeekOfAContractionIsAPrefixOfTheKeyItFinds()
    {
        byte[] search = new byte[4];
        int length = CollatedKey.WriteSearch(Encoding.Latin1.GetBytes("CH"), CollationName.Czech, 1250, KeyLength, search);

        length.Should().Be(1, "CH is one head");
        search[0].Should().Be(Stored("CHA", CollationName.Czech, 1250)[0]);
    }
}
