using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// The collation weight tables, checked as a faithful copy rather than as behaviour.
///
/// These are data lifted verbatim from COLL4ARR.C, so what can go wrong is a transcription slip:
/// a dropped entry, a shifted row, a wrong branch of a preprocessor block. One wrong weight is one
/// wrong character, invisible on ASCII and visible on the single accented name in production.
///
/// The structural properties below cover every entry of every table. The spot values are the ones
/// KEY-COLLATION.md section 3.3 calls out, each traceable to its line in the C. What the tables
/// [i]mean[/i] is gated by the corpus once the transform exists.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class CollationTablesTests
{
    public static TheoryData<string> AllTables => new("cp1252", "cp437", "cp850");

    private static ReadOnlySpan<byte> Table(string name) => name switch
    {
        "cp1252" => CollationTables.Cp1252General,
        "cp437" => CollationTables.Cp437General,
        _ => CollationTables.Cp850General,
    };

    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryTable_WeighsAllTwoHundredAndFiftySixByteValues(string name)
    {
        // A translation array is one head and one tail per byte value, no more and no less. A copy
        // that lost or gained a row would shift every entry after it and still look plausible.
        Table(name).Length.Should().Be(256 * 2);
    }

    [Theory]
    [MemberData(nameof(AllTables))]
    public void EveryTable_KeepsItsHeadsAtSixteenOrAbove(string name)
    {
        // The property the seek logic relies on: head weights below sixteen never occur, which is
        // what lets values under ten be read as tail markers (D4SEEK.C:117,134). Checking it across
        // all 256 entries catches a shifted row anywhere in the table, which spot values cannot.
        ReadOnlySpan<byte> table = Table(name);

        for (int value = 0; value < 256; value++)
        {
            (byte head, _) = CollationTables.Weigh(table, (byte)value);

            head.Should().BeGreaterThanOrEqualTo(16, $"byte {value} of {name} carries head {head}");
        }
    }

    [Fact]
    public void Cp1252_IsCaseInsensitive_BecauseACasePairSharesBothWeights()
    {
        // 'A' and 'a' are the same character to GENERAL, tails included (COLL4ARR.C entries 65,97).
        CollationTables.Weigh(CollationTables.Cp1252General, (byte)'A')
                       .Should().Be(CollationTables.Weigh(CollationTables.Cp1252General, (byte)'a'));

        CollationTables.Weigh(CollationTables.Cp1252General, (byte)'A').Should().Be(((byte)96, (byte)0));
    }

    [Fact]
    public void Cp1252_AnAccentSharesItsHeadAndDiffersOnlyInTheTail()
    {
        // This is what "sub-sort" means: u and u-umlaut order together until the tail separates
        // them (COLL4ARR.C:296-297, KEY-COLLATION.md section 3.3).
        (byte plainHead, byte plainTail) = CollationTables.Weigh(CollationTables.Cp1252General, (byte)'u');
        (byte accentHead, byte accentTail) = CollationTables.Weigh(CollationTables.Cp1252General, 252);

        accentHead.Should().Be(plainHead);
        plainTail.Should().Be(0);
        accentTail.Should().Be(4);
    }

    [Fact]
    public void Cp1252_AnExpandingCharacterCarriesAnExpansionIndexInsteadOfATail()
    {
        // oe (156) is expansion 0 and thorn (254) is expansion 2 (COLL4ARR.C:180,298). The head is
        // the marker, so the tail stops being a weight and becomes an index.
        CollationTables.Weigh(CollationTables.Cp1252General, 156)
                       .Should().Be((CollationTables.Expands, (byte)0));

        CollationTables.Weigh(CollationTables.Cp1252General, 254)
                       .Should().Be((CollationTables.Expands, (byte)2));
    }

    [Fact]
    public void Cp1252_ExpansionsAreTheFourTheCLibraryLists()
    {
        // "OE", "AE", "TH", "SS" in that order (COLL4ARR.C:304-311), which cp437 shares.
        ReadOnlySpan<byte> expansions = CollationTables.Cp1252Expansions;

        CollationTables.Expand(expansions, 0).Should().Be(((byte)'O', (byte)'E'));
        CollationTables.Expand(expansions, 1).Should().Be(((byte)'A', (byte)'E'));
        CollationTables.Expand(expansions, 2).Should().Be(((byte)'T', (byte)'H'));
        CollationTables.Expand(expansions, 3).Should().Be(((byte)'S', (byte)'S'));
    }

    [Fact]
    public void Cp850_ExpansionsDifferFromCp1252InTheirThirdAndFourth()
    {
        // "OE", "AE", "SS", "UE" (COLL4ARR.C:847-855). The two tables are not interchangeable, and
        // using one where the other belongs would misorder exactly these characters.
        ReadOnlySpan<byte> expansions = CollationTables.Cp850Expansions;

        CollationTables.Expand(expansions, 2).Should().Be(((byte)'S', (byte)'S'));
        CollationTables.Expand(expansions, 3).Should().Be(((byte)'U', (byte)'E'));
    }

    [Fact]
    public void Expand_OfAnIndexTheCollationDoesNotDefine_IsRefused()
    {
        Action act = () => CollationTables.Expand(CollationTables.Cp1252Expansions, 9);

        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.Index);
    }

    [Fact]
    public void Cp1252_TakesTheNonSwedishBranchOfItsConditionalBlocks()
    {
        // COLL4ARR.C guards four entries with S4ELICON, a Swedish sort this build does not define.
        // Entry 198 is the giveaway: the Swedish branch makes it a plain head, ours an expansion.
        CollationTables.Weigh(CollationTables.Cp1252General, 198)
                       .Should().Be((CollationTables.Expands, (byte)1));

        CollationTables.Weigh(CollationTables.Cp1252General, 196).Should().Be(((byte)96, (byte)4));
        CollationTables.Weigh(CollationTables.Cp1252General, 197).Should().Be(((byte)96, (byte)6));
    }

    [Fact]
    public void TheThreeTablesAreNotCopiesOfEachOther()
    {
        // A generator bug that emitted the same array three times would pass every test above.
        CollationTables.Cp437General.SequenceEqual(CollationTables.Cp1252General).Should().BeFalse();
        CollationTables.Cp850General.SequenceEqual(CollationTables.Cp1252General).Should().BeFalse();
        CollationTables.Cp850General.SequenceEqual(CollationTables.Cp437General).Should().BeFalse();
    }
}
