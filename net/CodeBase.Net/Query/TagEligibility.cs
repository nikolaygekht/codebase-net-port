using CodeBase.Net.Cdx;

namespace CodeBase.Net.Query;

/// <summary>
/// Whether the optimizer is allowed to answer a term with a given tag.
///
/// Four refusals, and they are not the same kind of thing. Two are structural and hold whatever the
/// caller asks for: a filtered tag indexes a subset of the table, so seeking it answers a different
/// question from the one asked, and a unique tag stores one key per group of equal keys, so it cannot
/// list a range's records at all. Two are the reference implementation's own limits, which this
/// library does not share, and a caller can lift them.
///
/// Governing specification: QUERY-OPTIMIZER.md section 3.4.
/// </summary>
internal static class TagEligibility
{
    /// <summary>
    /// Decides whether a tag may answer a term.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="scope">Which tags the caller allows.</param>
    /// <returns>True when the optimizer may use it.</returns>
    public static bool Allows(in QueryTag tag, QueryTagScope scope) =>
        !tag.IsFiltered && !tag.IsUnique
        && (scope == QueryTagScope.AllSeekable || (IsCompatibleKind(tag.Kind) && IsCompatibleOrder(tag.Collation)));

    /// <summary>
    /// The key types the reference implementation will optimize on.
    /// </summary>
    /// <param name="kind">The kind of key.</param>
    /// <returns>True when the reference would not skip a tag of this kind.</returns>
    /// <value>
    /// A [b]blacklist of four[/b] and not a shortlist of three, which is worth stating because the
    /// source's own comment beside it names three allowed types and the code refuses four
    /// (r4log.c:127-139). Read as a shortlist it would wrongly exclude date, logical and binary
    /// float tags, and the corpus witnesses all three being used: a tag over a date field and one
    /// over a logical expression are both matched and both answer their filters completely.
    ///
    /// The reason for the four is that a constant cannot be converted into their key domain there --
    /// stated at r4log.c:71-91, and untrue here since typed seek by value landed.
    /// </value>
    private static bool IsCompatibleKind(KeyKind kind) =>
        kind is not (KeyKind.Int32 or KeyKind.Currency or KeyKind.DateTime);

    /// <summary>
    /// The key orders the reference implementation will optimize on.
    /// </summary>
    /// <param name="collation">The order the tag's character keys follow.</param>
    /// <returns>True when the reference would not skip a tag in this order.</returns>
    /// <value>
    /// The refusal is a correctness argument rather than a missing feature: the reference's residual
    /// comparison runs in byte order, so a collated tag and the check that follows it could disagree
    /// (AS 04/26/00, r4log.c:108-120). Here the argument is inert, because a term backed by a tag is
    /// never re-compared and a term not backed by one never consults a tag's order -- so the two
    /// orders never meet in one answer. The refusal is kept anyway under the compatible scope,
    /// because it is what makes the reference's own record sets comparable.
    /// </value>
    private static bool IsCompatibleOrder(CollationName collation) => collation == CollationName.Machine;
}
