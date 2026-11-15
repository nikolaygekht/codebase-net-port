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
        CollationWeights.TableFor(CollationName.General, 850).SequenceEqual(CollationTables.Cp850General).Should().BeTrue();
    }

    [Fact]
    public void Cp437SharesCp1252sExpansions_AndCp850HasItsOwn()
    {
        CollationWeights.ExpansionsFor(CollationName.General, 1252).SequenceEqual(CollationTables.Cp1252Expansions).Should().BeTrue();
        CollationWeights.ExpansionsFor(CollationName.General, 437).SequenceEqual(CollationTables.Cp1252Expansions).Should().BeTrue();
        CollationWeights.ExpansionsFor(CollationName.General, 850).SequenceEqual(CollationTables.Cp850Expansions).Should().BeTrue();
    }

    [Theory]
    [InlineData(1251)]
    [InlineData(1250)]
    [InlineData(0)]
    public void GeneralOnACodePageWithNoTableIsRefused(int codePage)
    {
        CollationWeights.Supports(CollationName.General, codePage).Should().BeFalse();

        Action weights = () => CollationWeights.TableFor(CollationName.General, codePage);
        Action expansions = () => CollationWeights.ExpansionsFor(CollationName.General, codePage);

        weights.Should().Throw<CodeBaseException>()
            .Where(e => e.Code == ErrorCode.NotSupported && e.Message.Contains(codePage.ToString()));
        expansions.Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
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
