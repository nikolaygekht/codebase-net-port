using AwesomeAssertions;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// What a table in CodeBase's long-name layout promises once open: its fields by their whole names,
/// and filters that name them.
///
/// The table is built in memory; what the reference reads from such a file is the corpus case
/// LONGNAME's to say. These tests catch what the descriptor tests cannot: the table's long-name flag
/// reaching the path that compiles a filter, which by default would refuse every long name.
/// </summary>
[Trait("Layer", "Component")]
public sealed class LongFieldNameTableTests
{
    private static readonly string[] Records = [" 001SMITH     ", " 002JONES     ", " 003SMITH     "];

    [Fact]
    public void AFieldIsFoundByItsWholeNameInAnyCase()
    {
        (CodeBaseEngine engine, Table table) = TableImage.OpenWithLongNames(Records);
        using (engine)
        using (table)
        {
            table.Fields.Select(f => f.Name).Should().Equal("ID", "CUSTOMER_NAME");
            table.Fields["customer_name"].Name.Should().Be("CUSTOMER_NAME");
        }
    }

    [Theory]
    [InlineData("CUSTOMER_NAME = \"SMITH\"", 2)]
    [InlineData("MEMORY->CUSTOMER_NAME = \"JONES\"", 1)]
    [InlineData("ID = \"003\" .AND. UPPER(Customer_Name) = \"SMITH\"", 1)]
    public void AFilterNamingALongFieldIsAnswered(string filter, int count)
    {
        (CodeBaseEngine engine, Table table) = TableImage.OpenWithLongNames(Records);
        using (engine)
        using (table)
        {
            table.Query(filter).Count.Should().Be(count);
        }
    }
}
