using AwesomeAssertions;
using CodeBase.Net.Cdx;
using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;
using CodeBase.Net.Queries;
using Xunit;

namespace CodeBase.Net.Tests.Query;

/// <summary>
/// The four refusals, one at a time.
///
/// The corpus can only witness the combined outcome, and for one of the four it cannot witness even
/// that: every filtered tag in the corpus sits over a field that also carries a plain tag, which is
/// found first, so no dump distinguishes "the filtered tag was refused" from "a plain tag was found
/// before it". Isolating that in the corpus would mean adding a tag, hence rewriting an index file
/// and every dump taken from it. The refusal is one branch of a pure predicate, so it is tested here
/// instead -- deliberately, and this is where that decision is recorded in code.
/// </summary>
[Trait("Layer", "Unit")]
public sealed class TagEligibilityTests
{
    /// <summary>
    /// A tag with nothing wrong with it, which each case below spoils in exactly one way.
    /// </summary>
    private static QueryTag Plain(
        KeyKind kind = KeyKind.Character,
        CollationName order = CollationName.Machine,
        bool filtered = false,
        bool unique = false) =>
        new(0, "T", 20, kind, order, filtered, unique, Compile("NAME"));

    private static CompiledExpression Compile(string text) =>
        TypeResolver.Resolve(ExpressionParser.Parse(text, new StubFields()));

    [Fact]
    public void APlainTagIsUsable()
    {
        TagEligibility.Allows(Plain(), QueryTagScope.Compatible).Should().BeTrue();
        TagEligibility.Allows(Plain(), QueryTagScope.AllSeekable).Should().BeTrue();
    }

    /// <summary>
    /// A filtered tag indexes only the records its own condition admits, so seeking it answers a
    /// different question from the one the filter asked. Refused whatever the caller allows.
    /// </summary>
    [Fact]
    public void AFilteredTagIsRefusedUnderBothScopes()
    {
        TagEligibility.Allows(Plain(filtered: true), QueryTagScope.Compatible).Should().BeFalse();
        TagEligibility.Allows(Plain(filtered: true), QueryTagScope.AllSeekable).Should().BeFalse();
    }

    /// <summary>
    /// A unique tag stores one key per group of equal keys, so it cannot list the records in a range
    /// even when it can find the range. Refused whatever the caller allows.
    /// </summary>
    [Fact]
    public void AUniqueTagIsRefusedUnderBothScopes()
    {
        TagEligibility.Allows(Plain(unique: true), QueryTagScope.Compatible).Should().BeFalse();
        TagEligibility.Allows(Plain(unique: true), QueryTagScope.AllSeekable).Should().BeFalse();
    }

    /// <summary>
    /// The three key types the reference refuses because it cannot convert a constant into their key
    /// domain -- which this library can, so widening lifts exactly these.
    /// </summary>
    [Theory]
    [InlineData((int)KeyKind.Int32)]
    [InlineData((int)KeyKind.Currency)]
    [InlineData((int)KeyKind.DateTime)]
    public void TheRefusedKeyTypesAreUsableOnlyWhenWidened(int kind)
    {
        TagEligibility.Allows(Plain((KeyKind)kind), QueryTagScope.Compatible).Should().BeFalse();
        TagEligibility.Allows(Plain((KeyKind)kind), QueryTagScope.AllSeekable).Should().BeTrue();
    }

    /// <summary>
    /// Everything else passes the type gate, because it is a blacklist. Date, logical and binary
    /// float are the three the reference's own comment would have excluded and its code does not.
    /// </summary>
    [Theory]
    [InlineData((int)KeyKind.Character)]
    [InlineData((int)KeyKind.Double)]
    [InlineData((int)KeyKind.Date)]
    [InlineData((int)KeyKind.Logical)]
    [InlineData((int)KeyKind.Single)]
    public void EveryOtherKeyTypeIsUsableByDefault(int kind) =>
        TagEligibility.Allows(Plain((KeyKind)kind), QueryTagScope.Compatible).Should().BeTrue();

    /// <summary>
    /// A collated tag is refused under both scopes, and it is a character tag, so this is the
    /// collation gate on its own and not the type gate again.
    /// </summary>
    /// <value>
    /// The refusal the widened scope does [b]not[/b] lift, because it is not a limitation of the
    /// reference: a filter's comparisons are in byte order and a collated tag's keys are not, so a
    /// range of one selects a different set of records -- and a term answered by a tag is never
    /// checked again, so nothing puts the missing ones back.
    /// </value>
    [Fact]
    public void ACollatedTagIsRefusedUnderBothScopes()
    {
        QueryTag collated = Plain(KeyKind.CollatedCharacter, CollationName.General);

        TagEligibility.Allows(collated, QueryTagScope.Compatible).Should().BeFalse();
        TagEligibility.Allows(collated, QueryTagScope.AllSeekable).Should().BeFalse();
    }

    /// <summary>
    /// Widening lifts the reference's one liftable limit and none of the structural ones, so a tag
    /// that is both filtered and of a widened type stays refused.
    /// </summary>
    [Fact]
    public void WideningDoesNotRescueAStructuralRefusal() =>
        TagEligibility.Allows(Plain(KeyKind.Int32, filtered: true), QueryTagScope.AllSeekable)
            .Should().BeFalse();

    /// <summary>
    /// One field, which is all these cases need: what is being tested is the tag's own properties,
    /// and its key expression only has to compile.
    /// </summary>
    private sealed class StubFields : IFieldMetadata
    {
        private static readonly FieldDefinition Name =
            new("NAME", 'C', 'C', 20, 0, 1, false, false, false, false, null);

        public FieldReference? Find(string? alias, string name) =>
            string.Equals(name, "NAME", StringComparison.OrdinalIgnoreCase)
                ? new FieldReference(1, Name)
                : null;
    }
}
