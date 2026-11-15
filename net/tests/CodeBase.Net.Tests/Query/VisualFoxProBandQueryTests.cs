using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// Filters over Visual FoxPro's own functions where they can raise or meet a null, run through the public query surface.
/// </summary>
/// <value>
/// VFPBAND's N_ holds 4, -2.5, null, 0, null and 7.75, and no tag is over N_ itself, so every term
/// here is checked per record. The terms of a conjunction are checked left to right, as Visual
/// FoxPro's AND evaluates them, so the guard written first keeps the term after it from raising; the
/// expectation is computed from the table's own values. Governing decisions: ADR-81, ADR-82.
/// </value>
[Trait("Layer", "Component")]
public sealed class VisualFoxProBandQueryTests
{
    [Fact]
    public void AGuardWrittenFirstKeepsTheRootOfANegativeFromBeingTaken() =>
        Count("N_ >= 0 .AND. SQRT(N_) > 1").Should().Be(Expected(v => v >= 0 && Math.Sqrt(v) > 1));

    [Fact]
    public void AGuardWrittenFirstKeepsADivisionByZeroFromBeingMade() =>
        Count("N_ <> 0 .AND. MOD(10, N_) = 0").Should().Be(Expected(v => v != 0 && FloorMod(10, v) == 0));

    [Fact]
    public void AConditionalGuardsAsWell() =>
        Count("IIF(N_ < 0, 0, SQRT(N_)) > 1").Should().Be(Expected(v => v >= 0 && Math.Sqrt(v) > 1));

    [Theory]
    [InlineData("SQRT(N_) > 1 .AND. N_ >= 0", 61)]
    [InlineData("MOD(10, N_) = 0 .AND. N_ <> 0", 1307)]
    [InlineData("SQRT(N_) > 1", 61)]
    public void WrittenTheOtherWayRoundTheFilterRaisesAsVisualFoxProsScanDoes(string filter, int error)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("vfp/VFPBAND.DBF"));

        table.Invoking(t => t.Query(filter).Count)
            .Should().Throw<ExpressionEvaluationException>().Which.VisualFoxProError.Should().Be(error);
        table.Invoking(Walk(filter))
            .Should().Throw<ExpressionEvaluationException>().Which.VisualFoxProError.Should().Be(error);
    }

    /// <summary>Walks a query from its top to its end; record 1 passes, so the raise comes from a step.</summary>
    private static Action<Table> Walk(string filter) => table =>
    {
        CodeBase.Net.Query query = table.Query(filter);
        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
        {
        }
    };

    [Fact]
    public void ANullRecordIsNotAdmittedByAFunctionOfItsNull()
    {
        // The wrong record set ADR-82 closes: ABS of a null read as zero.
        Count("ABS(N_) = 0").Should().Be(Expected(v => v == 0));
    }

    private static int Count(string filter)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("vfp/VFPBAND.DBF"));

        return table.Query(filter).Count;
    }

    /// <summary>
    /// Counts the records whose N_ is present and satisfies the predicate, read from the table itself.
    /// </summary>
    private static int Expected(Func<double, bool> predicate)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("vfp/VFPBAND.DBF"));
        FieldDefinition field = table.Fields["N_"];
        int count = 0;
        int nulls = 0;

        for (int number = 1; number <= table.RecordCount; number++)
        {
            table.Go(number);
            if (table.IsNull(field))
                nulls++;
            else if (predicate(table.GetDouble(field)))
                count++;
        }

        nulls.Should().BePositive("the table must hold a null for the test to mean anything");
        return count;
    }

    /// <summary>Visual FoxPro's remainder, whose sign follows the divisor.</summary>
    private static double FloorMod(double value, double divisor) => value - (divisor * Math.Floor(value / divisor));
}
