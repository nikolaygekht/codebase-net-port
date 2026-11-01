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
