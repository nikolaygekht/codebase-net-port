using CodeBase.Net.Cdx;

namespace CodeBase.Net.Expressions;

/// <summary>
/// The tag an expression is resolved for, as far as it changes how the expression is computed.
///
/// The reference parses a tag's expression with the tag at hand, and two lengths depend on it: a memo
/// field that is the whole expression is capped at the key size (e4parse.c:433-457), and an ascending
/// or descending conversion of a string is widened by a collation that adds bytes per character
/// (e4parse.c:655-672). The same conversion then collates its operand with the tag's weights when it
/// is evaluated (e4functi.c:3042-3062), which is why the collation travels with the expression and not
/// only its width. A filter is resolved with no tag, and none of this applies.
/// </summary>
/// <param name="Collation">The collation the tag's header names.</param>
/// <param name="CodePage">The code page of the table, which decides which weight table the collation means.</param>
internal readonly record struct TagContext(CollationName Collation, int CodePage)
{
    /// <summary>
    /// Gets how many bytes the tag's collation adds to each character of a key.
    /// </summary>
    /// <value>
    /// None for machine order, one for GENERAL, whose keys hold a primary and a secondary weight per
    /// character.
    /// </value>
    public int PerCharacterAdd => Collation == CollationName.General ? 1 : 0;

    /// <summary>
    /// Gets a value indicating whether the tag's keys are weighed through a collation rather than kept in byte order.
    /// </summary>
    public bool IsCollated => Collation != CollationName.Machine;

    /// <summary>
    /// Gets the context of a tag built with a collation over a table of a code page.
    /// </summary>
    /// <param name="collation">The collation the tag's header names.</param>
    /// <param name="codePage">The table's code page number.</param>
    /// <returns>The context.</returns>
    public static TagContext For(CollationName collation, int codePage) => new(collation, codePage);
}
