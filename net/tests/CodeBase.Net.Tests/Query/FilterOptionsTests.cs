using AwesomeAssertions;
using CodeBase.Net.Expressions;
using CodeBase.Net.IO;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// A query's filter is compiled under the engine's expression settings, not under defaults of its own.
/// </summary>
/// <value>
/// A formatted date is as long as the picture (EXPRESSIONS.md section 7.3). A filter compiled for a
/// shorter picture than the one it is evaluated with cuts the date short, and a comparison over the
/// shorter width then admits every day the cut-off text matches -- a wrong record set, not a slow one.
/// </value>
[Trait("Layer", "Component")]
public sealed class FilterOptionsTests
{
    [Theory]
    [InlineData("DTOC(STOD(\"19870615\")) = \"1987.06.30\"", 0)]
    [InlineData("DTOC(STOD(\"19870615\")) = \"1987.06.15\"", 2)]
    public void AFiltersFormattedDateIsAsLongAsTheEnginesPicture(string filter, int found)
    {
        byte[] image = Dbf.TableImage.Build(" REC001", " REC002");
        using CodeBaseEngine engine = new(new StubFactory(new InMemorySource(image)), new NoCompanions())
        {
            ExpressionOptions = ExpressionOptions.Invariant.WithDatePicture("CCYY.MM.DD"),
        };
        using Table table = engine.OpenTable("memory.dbf");

        table.Query(filter).Count.Should().Be(found);
    }
}
