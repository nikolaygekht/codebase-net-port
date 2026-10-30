using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// A filter that negates a comparison over a nullable field, run through the public query surface.
/// </summary>
/// <value>
/// CDXEXPR's K_NNUM is null on three of its sixteen records. The expectation is read from the table
/// itself, record by record, so the test states the promise and not a list of numbers.
/// </value>
[Trait("Layer", "Component")]
public sealed class NegatedNullFilterTests
{
    [Fact]
    public void ANegatedComparisonLeavesOutTheRecordsWhereTheFieldIsNull()
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("CDXEXPR.DBF"));
        FieldDefinition field = table.Fields["K_NNUM"];

        List<int> expected = [];
        int nulls = 0;
        for (int number = 1; number <= table.RecordCount; number++)
        {
            table.Go(number);
            if (table.IsNull(field))
                nulls++;
            else if (table.GetDouble(field) != 0)
                expected.Add(number);
        }

        CodeBase.Net.Query query = table.Query(".NOT.(K_NNUM = 0)");
        List<int> found = [];
        for (GoResult go = query.Top(); go == GoResult.Ok; go = query.Next())
            found.Add(query.RecordNumber);

        nulls.Should().BePositive("the table must hold a null for the test to mean anything");
        found.Should().Equal(expected);
    }
}
