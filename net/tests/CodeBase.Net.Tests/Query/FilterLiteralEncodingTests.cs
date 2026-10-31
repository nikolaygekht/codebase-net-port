using AwesomeAssertions;
using CodeBase.Net.Tests.Dbf;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// A filter's string literals are encoded as the table's text is read, and only when they need to be.
/// </summary>
/// <value>
/// The golden suite gates the encoding on a cp1251 table. What is pinned here is that a filter of
/// plain ASCII never asks for the encoding at all: a table marked 620, a code page no .NET provider
/// supplies, still answers one, as it did when every literal was read as Latin1.
/// </value>
[Trait("Layer", "Component")]
public sealed class FilterLiteralEncodingTests
{
    /// <summary>Code page 620, Mazovia, which Windows never defined.</summary>
    private const byte Mazovia = 0x69;

    [Fact]
    public void AnAsciiFilterNeedsNoEncodingFromTheTable()
    {
        (CodeBaseEngine engine, Table table) = TableImage.OpenMarked(Mazovia, " REC001", " REC002");
        using (engine)
        {
            table.Query("TEXT = \"REC002\"").Count.Should().Be(1);
        }
    }

    [Fact]
    public void ANonAsciiFilterOnATableWhoseEncodingIsUnavailableIsRefusedForThatReason()
    {
        (CodeBaseEngine engine, Table table) = TableImage.OpenMarked(Mazovia, " REC001");
        using (engine)
        {
            table.Invoking(t => t.Query("TEXT = \"é\"").Top())
                .Should().Throw<CodeBaseException>().Which.Code.Should().Be(ErrorCode.NotSupported);
        }
    }
}
