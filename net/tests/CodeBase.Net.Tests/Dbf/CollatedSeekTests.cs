using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.TestUtils;
using Xunit;

namespace CodeBase.Net.Tests.Dbf;

/// <summary>
/// Seeking text in the collations of steps 7a-11 and 7a-12, through the public surface: a simple one and a Unicode one.
/// </summary>
/// <value>
/// The golden suite seeks every record of every such tag; this states the four paths once each, at
/// the layer that runs on every build. A whole value is found by its own text, and a prefix lands on
/// a record that begins with it -- compared without case, since both collations fold it: Croatian
/// case-insensitively, GENERAL by its weights. ADR-84, ADR-85.
/// </value>
[Trait("Layer", "Component")]
public sealed class CollatedSeekTests
{
    [Theory]
    [InlineData("CBCP1250", "C_HRU", "ljubav")]
    [InlineData("CBCP1252", "C_ES", "llama")]
    public void ASimpleCollationIsSoughtByTheWholeValueAndByAPrefix(string tableName, string tagName, string word)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf(tableName + ".DBF"));
        FieldDefinition field = table.Fields["K_TEXT"];
        table.SelectTag(table.Tags[tagName]);

        table.Seek(word).Should().Be(GoResult.Ok);
        table.GetString(field).TrimEnd().Should().BeEquivalentTo(word);

        table.SeekPrefix(word[..3]).Should().Be(GoResult.Ok);
        table.GetString(field).Should().StartWithEquivalentOf(word[..3]);
    }

    [Theory]
    [InlineData("W_GEN", "K_W")]
    [InlineData("O_GEN", "K_O")]
    public void ACollatedWideTagIsSoughtByTheWholeValueAndByAPrefix(string tagName, string fieldName)
    {
        using CodeBaseEngine engine = new();
        using Table table = engine.OpenTable(Corpus.PathOf("WGEN.DBF"));
        FieldDefinition field = table.Fields[fieldName];
        table.SelectTag(table.Tags[tagName]);

        // GENERAL keys "straße" and "STRASSE" alike: ß expands to SS and case is folded.
        table.Seek("straße").Should().Be(GoResult.Ok);
        table.GetString(field).TrimEnd(' ').Should().BeOneOf("straße", "STRASSE");

        // GENERAL folds case and expands ß, so the record found may be STRASSE; both begin "st".
        table.SeekPrefix("st").Should().Be(GoResult.Ok);
        table.GetString(field).Should().StartWithEquivalentOf("st");
    }
}
