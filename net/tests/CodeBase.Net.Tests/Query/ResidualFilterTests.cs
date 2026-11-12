using System.Text;
using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// Which terms a plan leaves to be checked per record, and what that check answers.
/// </summary>
/// <value>
/// A term a tag answered is not checked again; every other top-level conjunct is, and a filter that
/// is not a conjunction is checked whole unless the plan answers all of it (QUERY-OPTIMIZER.md
/// section 7). A record passes only where every remaining term is true — false and null alike fail.
/// </value>
[Trait("Layer", "Unit")]
public sealed class ResidualFilterTests
{
    private static readonly IFieldMetadata Fields = new TableFieldMetadata(
        [
            new FieldDefinition("K_TEXT", 'C', 'C', 4, 0, 1, false, false, false, false, null),
            new FieldDefinition("K_NNUM", 'N', 'N', 12, 3, 5, false, false, false, false, 0),
            new FieldDefinition("_NullFlags", '0', '0', 1, 0, 17, false, false, false, false, null),
        ],
        "T", longFieldNames: false);

    private static readonly FieldDefinition NullFlags =
        new("_NullFlags", '0', '0', 1, 0, 17, false, false, false, false, null);

    private static readonly QueryTag[] Tags = [Tag("T_TEXT", "K_TEXT")];

    [Fact]
    public void AFilterTheTagsAnswerWhollyLeavesNothingToCheck() =>
        Residual("K_TEXT = \"B\"").HasWork.Should().BeFalse();

    [Theory]
    // The answered conjunct is not checked again; the other decides alone.
    [InlineData("K_TEXT = \"B\" .AND. 1 = 1", true)]
    [InlineData("K_TEXT = \"B\" .AND. 1 = 2", false)]
    [InlineData("1 = 2 .AND. K_TEXT = \"B\"", false)]
    public void OnlyTheConjunctsNoTagAnsweredAreChecked(string filter, bool admitted)
    {
        ResidualFilter residual = Residual(filter);

        residual.HasWork.Should().BeTrue();
        residual.Admits(Context(Record(text: "Z", number: null))).Should().Be(admitted);
    }

    [Theory]
    // An or the walk abandoned is not a conjunction, so it is checked whole: its answered-looking
    // term is not trusted on its own.
    [InlineData("K_TEXT = \"B\" .OR. 1 = 2", "B", true)]
    [InlineData("K_TEXT = \"B\" .OR. 1 = 2", "Z", false)]
    public void AFilterThatIsNotAConjunctionIsCheckedWhole(string filter, string text, bool admitted) =>
        Residual(filter).Admits(Context(Record(text, number: null))).Should().Be(admitted);

    [Fact]
    public void ATermThatIsNullDoesNotAdmitTheRecord()
    {
        // A comparison with an absent value answers null, which is not true.
        ResidualFilter residual = Residual("K_TEXT = \"B\" .AND. K_NNUM = 0");

        residual.Admits(Context(Record("B", number: null))).Should().BeFalse();
        residual.Admits(Context(Record("B", number: "       0.000"))).Should().BeTrue();
    }

    private static ResidualFilter Residual(string text)
    {
        CompiledExpression filter = TypeResolver.Resolve(ExpressionParser.Parse(text, Fields));
        return new ResidualFilter(filter, new QueryDecomposer(Tags, QueryTagScope.Compatible).Decompose(filter));
    }

    private static EvaluationContext Context(RecordBuffer record) =>
        new() { Record = record, NullFlags = NullFlags };

    /// <summary>A record of K_TEXT, K_NNUM and the null flags; a null number sets its flag.</summary>
    private static RecordBuffer Record(string text, string? number)
    {
        RecordBuffer record = new(18);
        Span<byte> raw = record.Raw;

        raw[0] = (byte)' ';
        Encoding.ASCII.GetBytes(text.PadRight(4)).CopyTo(raw[1..]);
        Encoding.ASCII.GetBytes((number ?? string.Empty).PadLeft(12)).CopyTo(raw[5..]);
        raw[17] = number is null ? (byte)1 : (byte)0;

        return record;
    }

    private static QueryTag Tag(string name, string expression)
    {
        CompiledExpression key = TypeResolver.Resolve(ExpressionParser.Parse(expression, Fields));
        return new QueryTag(0, name, 4, KeyKind.Character, CollationName.Machine, false, false, key);
    }
}
