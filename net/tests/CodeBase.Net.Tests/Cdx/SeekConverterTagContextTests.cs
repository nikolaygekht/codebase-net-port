using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using Xunit;

namespace CodeBase.Net.Tests.Cdx;

/// <summary>
/// A tag's expression is resolved for that tag, not as a filter would be.
/// </summary>
/// <value>
/// Golden tests prove the two length rules against the reference, but over the resolver alone; no
/// corpus tag is a bare memo or a GENERAL tag over a key conversion. This pins the one line that
/// hands a tag's context to the resolver, over headers built for the purpose. The lengths are the
/// ones the corpus's tag-context section witnesses.
/// </value>
[Trait("Layer", "Component")]
public sealed class SeekConverterTagContextTests
{
    private static readonly FieldDefinition[] Fields =
    [
        new("K_TEXT", 'C', 'C', 20, 0, 1, false, false, false, false, null),
        new("K_MEMO", 'M', 'M', 4, 0, 21, false, false, false, false, null),
    ];

    [Fact]
    public void ATagOverABareMemoResolvesItAtTheKeySize()
    {
        Converter("K_MEMO", keyLength: 240, collation: "").Compiled.Length.Should().Be(240);
    }

    [Fact]
    public void AGeneralTagOverADescendingStringResolvesItAtTheCollatedWidth()
    {
        Converter("DESCEND(K_TEXT)", keyLength: 40, collation: "GENERAL").Compiled.Length.Should().Be(40);
    }

    private static SeekConverter Converter(string expression, int keyLength, string collation)
    {
        byte[] image = IndexImage.SingleTag(keyLength, root: 1024, expression: expression, collation: collation).Build();

        return SeekConverter.For(
            IndexHeader.Parse(image, "an image"), Fields, codePage: 1252, ExpressionOptions.Invariant, alias: "T");
    }
}
