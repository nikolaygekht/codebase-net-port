using AwesomeAssertions;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// What makes a tag's key expression the same expression as a piece of a filter.
///
/// The corpus gates this over the expressions it happens to contain; these state the rules one at a
/// time, including the two that decide whether the whole optimizer works at all. A tag on a numeric
/// field must match a comparison against that field even though comparing promotes the field's
/// loader to a different row -- and two different fields that happen to share a width must not match
/// each other, which is the only thing keeping that promotion tolerance honest.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class SubtreeMatchTests
{
    private static readonly Fields Metadata = new();

    private static CompiledExpression Compile(string text) =>
        TypeResolver.Resolve(ExpressionParser.Parse(text, Metadata));

    /// <summary>
    /// Matches a tag's key expression against the left operand of a comparison, which is where the
    /// optimizer looks.
    /// </summary>
    private static bool MatchesLeftOperandOf(string filter, string key)
    {
        CompiledExpression compiled = Compile(filter);
        int root = compiled.Nodes.Count - 1;
        int right = root - 1;
        int left = right - compiled.Nodes[right].SubtreeSize;

        return SubtreeMatch.Matches(compiled, left, Compile(key));
    }

    [Fact]
    public void ABareFieldMatchesATagOnThatField() =>
        MatchesLeftOperandOf("NAME = \"X\"", "NAME").Should().BeTrue();

    [Fact]
    public void AFunctionCallMatchesATagOverTheSameCall() =>
        MatchesLeftOperandOf("UPPER(NAME) = \"X\"", "UPPER(NAME)").Should().BeTrue();

    [Fact]
    public void AFunctionCallDoesNotMatchATagOverItsArgument() =>
        MatchesLeftOperandOf("UPPER(NAME) = \"X\"", "NAME").Should().BeFalse();

    [Fact]
    public void ABareFieldDoesNotMatchATagOverAFunctionOfIt() =>
        MatchesLeftOperandOf("NAME = \"X\"", "UPPER(NAME)").Should().BeFalse();

    /// <summary>
    /// The promotion tolerance, and the reason it has to exist. Comparing a numeric field against a
    /// number rewrites the field's loader to the row that yields a double, so the same field sits on
    /// a different row inside the filter than inside the tag that indexes it. Without the tolerance
    /// no numeric tag would ever answer a numeric comparison.
    /// </summary>
    [Fact]
    public void APromotedFieldStillMatchesTheTagThatIndexesIt()
    {
        CompiledExpression filter = Compile("SALARY > 0");
        CompiledExpression key = Compile("SALARY");

        int fieldNode = 0;
        filter.Nodes[fieldNode].Row.Should().NotBe(key.Nodes[^1].Row, "the comparison promoted it");
        SubtreeMatch.Matches(filter, fieldNode, key).Should().BeTrue();
    }

    /// <summary>
    /// And what keeps that tolerance honest: two fields of the same width and type still are not each
    /// other, so a tag over one cannot answer a term over the other. With the field identity dropped
    /// and the row tolerance kept, this is exactly the pair that would match wrongly.
    /// </summary>
    [Fact]
    public void TwoFieldsOfTheSameShapeDoNotMatchEachOther() =>
        MatchesLeftOperandOf("SALARY > 0", "BONUS").Should().BeFalse();

    /// <summary>
    /// A literal is part of the expression's identity, so two calls differing only in a constant are
    /// different expressions.
    /// </summary>
    [Fact]
    public void ACallWithADifferentLiteralIsADifferentExpression() =>
        MatchesLeftOperandOf("NAME + \"A\" = \"X\"", "NAME + \"B\"").Should().BeFalse();

    /// <summary>
    /// The number the parser folds into a node counts too, which is what tells two substrings of the
    /// same field apart. They have identical node shapes, identical rows and identical fields.
    /// </summary>
    [Fact]
    public void TwoSubstringsOfOneFieldAreDifferentExpressions()
    {
        MatchesLeftOperandOf("SUBSTR(NAME,1,5) = \"X\"", "SUBSTR(NAME,1,5)").Should().BeTrue();
        MatchesLeftOperandOf("SUBSTR(NAME,2,5) = \"X\"", "SUBSTR(NAME,1,5)").Should().BeFalse();
    }

    /// <summary>
    /// A tag whose expression is longer than the whole subexpression cannot match it, and asking must
    /// not read off the front of the array.
    /// </summary>
    [Fact]
    public void ATagLongerThanTheSubexpressionIsRefusedAndDoesNotOverrun() =>
        MatchesLeftOperandOf("NAME = \"X\"", "UPPER(NAME) + CITY").Should().BeFalse();

    private sealed class Fields : IFieldMetadata
    {
        // SALARY and BONUS are deliberately identical in every respect but name and offset: they are
        // the pair that tells the field-identity rule from the row tolerance.
        private static readonly FieldDefinition[] All =
        [
            new("NAME", 'C', 'C', 20, 0, 1, false, false, false, false, null),
            new("CITY", 'C', 'C', 15, 0, 21, false, false, false, false, null),
            new("SALARY", 'N', 'N', 12, 2, 36, false, false, false, false, null),
            new("BONUS", 'N', 'N', 12, 2, 48, false, false, false, false, null),
        ];

        public FieldReference? Find(string? alias, string name)
        {
            for (int index = 0; index < All.Length; index++)
            {
                if (string.Equals(All[index].Name, name, StringComparison.OrdinalIgnoreCase))
                    return new FieldReference(index + 1, All[index]);
            }

            return null;
        }
    }
}
