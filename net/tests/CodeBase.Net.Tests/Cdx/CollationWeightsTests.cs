using AwesomeAssertions;
using CodeBase.Net.Cdx;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// Which weight and expansion tables a collation means on each code page, and the pairs that have
/// none.
/// </summary>
/// <value>
/// KEY-COLLATION.md sections 3.2 and 3.3: GENERAL is a separate table per code page
/// (i4conv.c:314-480), cp437 shares cp1252's expansions and cp850 has its own (COLL4ARR.C:579 and
/// 847-855). A pair with no table is refused, because weighing with another code page's table
/// orders keys wrongly rather than failing. The tables' contents are CollationTablesTests'.
/// </value>
[Trait("Layer", "Unit")]
public sealed class CollationWeightsTests
{
    [Fact]
    public void EachCodePageHasItsOwnGeneralWeights()
    {
        CollationWeights.TableFor(CollationName.General, 1252).SequenceEqual(CollationTables.Cp1252General).Should().BeTrue();
        CollationWeights.TableFor(CollationName.General, 437).SequenceEqual(CollationTables.Cp437General).Should().BeTrue();

        // GENERAL is VFP's name and follows VFP's keys; on cp850 they differ from CodeBase's table in
        // thorn, which VFP expands to TH. CodeBase's own name for it, CB00007, keeps its table (ADR-86).
        CollationWeights.TableFor(CollationName.General, 850).SequenceEqual(CollationTables.VisualFoxProGeneral(850)).Should().BeTrue();
        CollationWeights.TableFor(CollationName.GeneralCp850, 850).SequenceEqual(CollationTables.Cp850General).Should().BeTrue();
    }

    [Fact]
    public void Cp437SharesCp1252sExpansions_AndCp850HasItsOwn()
    {
        CollationWeights.ExpansionsFor(CollationName.General, 1252).SequenceEqual(CollationTables.Cp1252Expansions).Should().BeTrue();
        CollationWeights.ExpansionsFor(CollationName.General, 437).SequenceEqual(CollationTables.Cp1252Expansions).Should().BeTrue();
        CollationWeights.ExpansionsFor(CollationName.General, 850).SequenceEqual(CollationTables.VisualFoxProGeneralExpansions(850)).Should().BeTrue();
        CollationWeights.ExpansionsFor(CollationName.GeneralCp850, 850).SequenceEqual(CollationTables.Cp850Expansions).Should().BeTrue();
    }

    [Fact]
    public void OnCp850VisualFoxProExpandsThornWhereCodeBaseDoesNot()
    {
        // VFPG850's keys: thorn (0xE7) is the two heads T and H. CodeBase's table gives it the head of T
        // and a tail numbered as H's head (COLL4ARR.C), and its expansion list has no TH.
        CollationTables.Weigh(CollationWeights.TableFor(CollationName.General, 850), 0xE7).Head.Should().Be(CollationTables.Expands);
        CollationTables.Weigh(CollationTables.Cp850General, 0xE7).Head.Should().NotBe(CollationTables.Expands);
    }

    [Theory]
    // The ten code pages beyond CodeBase's three that Visual FoxPro has GENERAL on, their weights
    // recovered from VFP's keys (ADR-86).
    [InlineData(620)]
    [InlineData(852)]
    [InlineData(857)]
    [InlineData(861)]
    [InlineData(865)]
    [InlineData(895)]
    [InlineData(1250)]
    [InlineData(1254)]
    [InlineData(10000)]
    [InlineData(10029)]
    public void GeneralIsSupportedWhereVisualFoxProHasIt(int codePage)
    {
        CollationWeights.Supports(CollationName.General, codePage).Should().BeTrue();
        CollationWeights.TableFor(CollationName.General, codePage).Length.Should().Be(512);
        CollationWeights.ExpansionsFor(CollationName.General, codePage).Length.Should().BePositive();
    }

    /// <summary>The 51 pairs VFP builds a named collation on (vfpcoll.prg's survey).</summary>
    public static TheoryData<string, int> NamedPairs =>
    [
        .. new[] { 852, 895, 1250, 10029 }.Select(cp => ("Czech", cp)),
        .. new[] { 437, 850, 1252, 10000 }.Select(cp => ("Dutch", cp)),
        .. new[] { 437, 1252, 10000 }.Select(cp => ("German", cp)),
        .. new[] { 737, 1253, 10006 }.Select(cp => ("Greek", cp)),
        .. new[] { 852, 1250 }.Select(cp => ("Hungary", cp)),
        .. new[] { 437, 850, 861, 1252 }.Select(cp => ("Iceland", cp)),
        .. new[] { 437, 850, 865, 1252, 10000 }.Select(cp => ("Nordan", cp)),
        .. new[] { 620, 852, 1250, 10029 }.Select(cp => ("Polish", cp)),
        .. new[] { 866, 1251, 10007 }.Select(cp => ("Russian", cp)),
        .. new[] { 852, 895, 1250, 10029 }.Select(cp => ("Slovak", cp)),
        .. new[] { 437, 850, 1252, 10000 }.Select(cp => ("Spanish", cp)),
        .. new[] { 437, 850, 865, 1252, 10000 }.Select(cp => ("Swefin", cp)),
        .. new[] { 857, 1254 }.Select(cp => ("Turkish", cp)),
        .. new[] { 437, 850, 1252, 10000 }.Select(cp => ("Uniqwt", cp)),
    ];

    [Fact]
    public void TheSurveyHasFiftyOnePairs() => NamedPairs.Count.Should().Be(51);

    [Theory]
    [MemberData(nameof(NamedPairs))]
    public void ANamedCollationIsSupportedWhereVisualFoxProBuildsIt(string name, int codePage)
    {
        CollationName collation = Enum.Parse<CollationName>(name);
        // Recovered from VFP's keys (ADR-87, ADR-88); every one is a head-and-tail collation.
        CollationWeights.Supports(collation, codePage).Should().BeTrue();
        CollationWeights.TableFor(collation, codePage).Length.Should().Be(512);
        CollationWeights.IsSimple(collation).Should().BeFalse();
        CollationWeights.PerCharacterAdd(collation).Should().Be(1);
    }

    [Theory]
    // VFP refuses each of these (error 1915), so there is nothing to recover.
    [InlineData("Russian", 1252)]
    [InlineData("Czech", 1252)]
    [InlineData("Hungary", 895)]
    [InlineData("Greek", 437)]
    [InlineData("German", 850)]
    [InlineData("Turkish", 1250)]
    public void ANamedCollationIsRefusedWhereVisualFoxProRefusesIt(string name, int codePage)
    {
        CollationName collation = Enum.Parse<CollationName>(name);
        CollationWeights.Supports(collation, codePage).Should().BeFalse();

        Action act = () => CollationWeights.TableFor(collation, codePage);
        act.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
    }

    [Fact]
    public void OnlyANamedCollationHasSequences()
    {
        CollationWeights.SequencesFor(CollationName.General, 1252).Length.Should().Be(0);
        CollationWeights.SequencesFor(CollationName.Russian, 1251).Length.Should().Be(0);
        CollationWeights.SequencesFor(CollationName.Czech, 1250).Length.Should().BePositive();
    }

    [Theory]
    // VFP itself refuses GENERAL here (error 1915), so there is nothing to recover.
    [InlineData(1251)]
    [InlineData(866)]
    [InlineData(1253)]
    [InlineData(0)]
    public void GeneralOnACodePageWithNoTableIsRefused(int codePage)
    {
        CollationWeights.Supports(CollationName.General, codePage).Should().BeFalse();

        Action weights = () => CollationWeights.TableFor(CollationName.General, codePage);
        Action expansions = () => CollationWeights.ExpansionsFor(CollationName.General, codePage);

        weights.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported && e.Message.Contains(codePage.ToString()));
        expansions.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
        CollationTables.VisualFoxProGeneralExpansions(codePage).Length.Should().Be(0, "VFP has no GENERAL here to recover");
    }

    [Theory]
    // A CodeBase collation names its own table, so the table's code page changes nothing (ADR-84).
    [InlineData(1252)]
    [InlineData(850)]
    [InlineData(1251)]
    public void ANamedCollationIgnoresTheTablesCodePage(int codePage)
    {
        CollationWeights.Supports(CollationName.SpanishCp1252, codePage).Should().BeTrue();
        CollationWeights.TableFor(CollationName.SpanishCp1252, codePage).SequenceEqual(CollationTables.SpanishCp1252).Should().BeTrue();
        CollationWeights.TableFor(CollationName.SpanishCp850, codePage).SequenceEqual(CollationTables.SpanishCp850).Should().BeTrue();
        CollationWeights.TableFor(CollationName.CroatianCp1250, codePage).SequenceEqual(CollationTables.Cp1250Croatian).Should().BeTrue();
        CollationWeights.TableFor(CollationName.CroatianUpperCp1250, codePage).SequenceEqual(CollationTables.Cp1250CroatianUpper).Should().BeTrue();
        CollationWeights.TableFor(CollationName.Avaya1252, codePage).SequenceEqual(CollationTables.Avaya1252).Should().BeTrue();
        CollationWeights.TableFor(CollationName.GeneralCp1252, codePage).SequenceEqual(CollationTables.Cp1252General).Should().BeTrue();
        CollationWeights.TableFor(CollationName.GeneralCp437, codePage).SequenceEqual(CollationTables.Cp437General).Should().BeTrue();
        CollationWeights.TableFor(CollationName.GeneralCp850, codePage).SequenceEqual(CollationTables.Cp850General).Should().BeTrue();
    }

    [Fact]
    public void ASimpleCollationHasNoExpansions()
    {
        // Its transform reads heads alone (u4util.c:2415-2419); there is nothing to expand into.
        CollationWeights.ExpansionsFor(CollationName.CroatianCp1250, 1250).Length.Should().Be(0);
        CollationWeights.ExpansionsFor(CollationName.SpanishCp850, 850).Length.Should().Be(0);
    }

    [Fact]
    public void AvayaAndTheNamedGeneralsTakeTheirExpansionsFromTheirOwnTable()
    {
        // avaya1252CompressArray is an alias of cp1252's (COLL4ARR.C:2169).
        CollationWeights.ExpansionsFor(CollationName.Avaya1252, 850).SequenceEqual(CollationTables.Cp1252Expansions).Should().BeTrue();
        CollationWeights.ExpansionsFor(CollationName.GeneralCp437, 850).SequenceEqual(CollationTables.Cp1252Expansions).Should().BeTrue();
        CollationWeights.ExpansionsFor(CollationName.GeneralCp850, 1252).SequenceEqual(CollationTables.Cp850Expansions).Should().BeTrue();
    }

    [Theory]
    // keySizeCharPerCharAdd (i4conv.c:314-480): nothing for the four simple ones, one for the rest.
    [InlineData("CroatianCp1250", true, 0)]
    [InlineData("CroatianUpperCp1250", true, 0)]
    [InlineData("SpanishCp1252", true, 0)]
    [InlineData("SpanishCp850", true, 0)]
    [InlineData("Avaya1252", false, 1)]
    [InlineData("General", false, 1)]
    [InlineData("GeneralCp437", false, 1)]
    [InlineData("Machine", false, 0)]
    public void TheSimpleCollationsMapOneForOne(string name, bool simple, int perCharacterAdd)
    {
        CollationName collation = Enum.Parse<CollationName>(name);

        CollationWeights.IsSimple(collation).Should().Be(simple);
        CollationWeights.PerCharacterAdd(collation).Should().Be(perCharacterAdd);
    }

    [Fact]
    public void MachineOrderIsSupportedEverywhere_ButHasNoWeightsToHandOut()
    {
        // Machine keys are the bytes themselves; asking for their weights is a caller's mistake.
        CollationWeights.Supports(CollationName.Machine, 1251).Should().BeTrue();

        Action act = () => CollationWeights.TableFor(CollationName.Machine, 1252);

        act.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported && e.Message.Contains("Machine"));
    }
}
