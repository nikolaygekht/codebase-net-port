using CodeBase.Net.Cdx;
using CodeBase.Net.Expressions;

namespace CodeBase.Net.Queries;

/// <summary>
/// Whether the optimizer is allowed to answer a term with a given tag.
///
/// Four refusals, and they are not the same kind of thing. Three are structural and hold whatever the
/// caller asks for: a filtered tag indexes a subset of the table, so seeking it answers a different
/// question from the one asked; a unique tag stores one key per group of equal keys, so it cannot list
/// a range's records at all; and a collated tag orders its keys differently from the byte order a
/// filter compares in. One, the key type, is the reference implementation's own limit, which this
/// library does not share, and a caller can lift it.
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
        !tag.IsFiltered && !tag.IsUnique && IsUsableOrder(tag.Collation)
        && (scope == QueryTagScope.AllSeekable || IsCompatibleKind(tag.Kind));

    /// <summary>
    /// Decides whether a tag's keys can express a constant exactly.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="value">The constant a term compares against.</param>
    /// <returns>True when a key can be built that means exactly this value.</returns>
    /// <value>
    /// A term is only worth seeking for if the value it names survives being turned into a key. Where
    /// it does not, the tag is left alone and the term is checked per record instead, which is slower
    /// and right.
    ///
    /// This is where the reference's blanket refusal of integer, currency and datetime tags turns
    /// into a narrow one. It gives them up entirely because it cannot convert a constant into their
    /// key domain at all; this library can, so it refuses only the constants that would not survive
    /// the conversion. Two would not: a fractional number against an integer tag, where rounding it
    /// either way admits or drops the record at the boundary, and a value too large for the key's
    /// width. Under the compatible scope none of this is reachable, because those tags are refused
    /// before a constant is ever looked at.
    /// </value>
    public static bool CanExpress(in QueryTag tag, ConstraintValue value) => tag.Kind switch
    {
        KeyKind.Int32 => IsWhole(value.Number) && value.Number is >= int.MinValue and <= int.MaxValue,
        KeyKind.Currency => IsWhole(value.Number * 10000d)
            && value.Number * 10000d is >= long.MinValue and <= long.MaxValue,
        KeyKind.Single => (double)(float)value.Number == value.Number,

        // Only a timestamp can name a moment, and no literal is one -- so a term against a datetime
        // tag has no constant this could convert even in principle.
        KeyKind.DateTime => value.Type is ExpressionType.Timestamp
            or ExpressionType.TimestampWithMilliseconds,

        _ => true,
    };

    /// <summary>Decides whether a number has nothing after its point.</summary>
    private static bool IsWhole(double number) => Math.Floor(number) == number && !double.IsInfinity(number);

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
    /// The key orders a tag may be used in, which is one.
    /// </summary>
    /// <param name="collation">The order the tag's character keys follow.</param>
    /// <returns>True when a term may be answered by seeking a tag in this order.</returns>
    /// <value>
    /// Refused in [b]both[/b] scopes, and that is not conservatism. A comparison in a filter is
    /// defined in byte order -- that is what evaluating it does -- while a collated tag stores its
    /// keys in a different order, so seeking a range of one answers a different question. Over a
    /// table holding [c]alpha[/c], [c]cafe[/c] and [c]aeon[/c] the filter "greater than M" includes
    /// all three, because their first bytes are above that letter's; a collated tag sorts them below
    /// it and a range seek drops them. Nothing downstream puts them back, because a term answered by
    /// a tag is not checked again.
    ///
    /// The reference's source refuses collated tags for exactly this reason and says so: results
    /// "could differ depending on if a tag was available" (AS 04/26/00, r4log.c:108-120). But the
    /// refusal is compiled out of every build that is not a test build (d4defs.h:669-673), so the
    /// reference as shipped uses the tag and returns the wrong records, which the corpus records. This
    /// library refuses it anyway; that is a divergence by decision (ADR-59).
    /// </value>
    private static bool IsUsableOrder(CollationName collation) => collation == CollationName.Machine;
}
