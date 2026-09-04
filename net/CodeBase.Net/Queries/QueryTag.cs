using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;

namespace CodeBase.Net.Queries;

/// <summary>
/// What the optimizer needs to know about one tag, taken once when a filter is planned.
///
/// The optimizer never opens an index to decide whether a tag can answer a term: it needs the tag's
/// compiled key expression, to compare against a subexpression of the filter, and five facts about
/// the tag itself, to decide whether it is allowed to. Projecting those into a value keeps the whole
/// planning half free of files, which is what lets it be tested without one.
///
/// [c]Ordinal[/c] is the tag's position in the table's tag list, and it is what a plan carries so the
/// executor can find the real tag again. The tags are considered in that order and the first match
/// wins, so it is load bearing rather than incidental.
/// </summary>
/// <param name="Ordinal">The tag's position in the table's tag list, counted from zero.</param>
/// <param name="Name">The tag's name.</param>
/// <param name="KeyLength">How many bytes one of its keys occupies.</param>
/// <param name="Kind">What its keys are made of, which is what the type gate reads.</param>
/// <param name="Collation">The order its character keys follow.</param>
/// <param name="IsFiltered">Whether it indexes only the records its filter admits.</param>
/// <param name="IsUnique">Whether it stores one key per group of equal keys.</param>
/// <param name="Key">Its key expression, compiled.</param>
internal readonly record struct QueryTag(
    int Ordinal,
    string Name,
    int KeyLength,
    KeyKind Kind,
    CollationName Collation,
    bool IsFiltered,
    bool IsUnique,
    CompiledExpression Key);
