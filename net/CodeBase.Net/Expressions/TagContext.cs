using CodeBase.Net.Cdx;

namespace CodeBase.Net.Expressions;

/// <summary>
/// The tag an expression is resolved for, as far as it changes the expression's lengths.
///
/// The reference parses a tag's expression with the tag at hand, and two lengths depend on it: a memo
/// field that is the whole expression is capped at the key size (e4parse.c:433-457), and an ascending
/// or descending conversion of a string is widened by a collation that adds bytes per character
/// (e4parse.c:655-672). A filter is resolved with no tag, and neither applies.
/// </summary>
/// <param name="PerCharacterAdd">
/// How many bytes the tag's collation adds to each character of a key: none for machine order, one
/// for GENERAL, whose keys hold a primary and a secondary weight per character.
/// </param>
internal readonly record struct TagContext(int PerCharacterAdd)
{
    /// <summary>
    /// Gets the context of a tag built with a collation.
    /// </summary>
    /// <param name="collation">The collation the tag's header names.</param>
    /// <returns>The context.</returns>
    public static TagContext For(CollationName collation) =>
        new(collation == CollationName.General ? 1 : 0);
}
