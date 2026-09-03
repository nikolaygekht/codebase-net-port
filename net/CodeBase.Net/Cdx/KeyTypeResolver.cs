using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;

namespace CodeBase.Net.Cdx;

/// <summary>
/// Works out what byte a tag's keys are padded with, from the table the tag belongs to.
///
/// The file records the key expression as *text* and never its type, so the pad byte cannot be read
/// out of an index (ADR-26). What it can be read out of is the **table**: when the expression is the
/// name of one of its fields, that field's type is the key's type, and the pad byte follows from the
/// mapping the C library uses (i4init.c:557-604). That covers every tag in the corpus and the great
/// majority of real ones, because an index on a bare field is what applications write (ADR-28).
///
/// This is not a guess from the expression's shape. It is the same answer the expression engine would
/// compute for the same input, by a much shorter route. An expression that is *not* a field reference —
/// [c]UPPER(NAME)[/c], [c]STR(ID)+CITY[/c] — is refused, and waits for the expression engine.
///
/// Governing specification: KEY-COLLATION.md section 3.7, and ADR-26 with ADR-28.
/// </summary>
internal static class KeyTypeResolver
{
    /// <summary>
    /// Works out the pad byte for a tag of a table.
    /// </summary>
    /// <param name="header">The tag's header, which carries the expression text and the collation.</param>
    /// <param name="fields">The fields of the table the tag indexes.</param>
    /// <param name="alias">
    /// The name a field reference in the expression may be qualified by. Empty by default, which
    /// leaves a qualified name resolving to nothing — the same answer the reference gives when no
    /// second table is open, and the only answer a tag expression may have anyway.
    /// </param>
    /// <returns>The byte the tag's trailing-pad counts stand for.</returns>
    /// <exception cref="CodeBaseException">
    /// The expression cannot be compiled, or produces a type this library does not key. The message
    /// names the expression, because that is what a caller has to act on.
    /// </exception>
    /// <value>
    /// The padding follows from the key's [i]type[/i], and the type of a key is the type of the
    /// expression that builds it — so this compiles the expression rather than looking for a field of
    /// that name. It is resolved on a tag's first use, so a table with one unreadable tag keeps its
    /// other tags working (ADR-28).
    /// </value>
    public static byte PadByteFor(
        IndexHeader header, IReadOnlyList<FieldDefinition> fields, string alias = "")
    {
        // A collation other than machine order settles it by itself: every collated key pads with NUL,
        // character keys included. No expression is consulted, and none is needed (ADR-27).
        if (header.PadByte is byte settled)
            return settled;

        string expression = header.Expression.Trim();
        CompiledExpression compiled = TypeResolver.Resolve(
            ExpressionParser.Parse(expression, new TableFieldMetadata(fields, alias)));

        return PadByteOf(compiled.ResultType, header, expression);
    }

    /// <summary>
    /// Maps an expression's result type to the byte a key built from it pads with.
    /// </summary>
    /// <param name="type">The expression's result type.</param>
    /// <param name="header">The tag's header, for the message when the type has no key.</param>
    /// <param name="expression">The expression text, for the same reason.</param>
    /// <returns>The pad byte.</returns>
    /// <exception cref="CodeBaseException">The type is not one this library keys.</exception>
    public static byte PadByteOf(ExpressionType type, IndexHeader header, string expression) =>
        type switch
        {
            // Text of any provenance pads with blanks, a memo operand included.
            ExpressionType.Character or ExpressionType.Memo or ExpressionType.General =>
                KeyPadding.Space,

            // Everything whose key is a fixed-width number pads with NUL: the numeric and date forms
            // go through the double transform, currency, integer, float and datetime have their own,
            // and a logical is a single letter that is never padded at all.
            ExpressionType.NumericAsText or ExpressionType.NumericAsDouble
                or ExpressionType.DateAsText or ExpressionType.DateAsDouble
                or ExpressionType.Currency or ExpressionType.Integer
                or ExpressionType.Timestamp or ExpressionType.TimestampWithMilliseconds
                or ExpressionType.BinaryFloat or ExpressionType.Logical => KeyPadding.Nul,

            _ => throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"The key expression '{expression}' produces a value of type '{(char)type}', which " +
                $"this library does not know how to key. The tag's key length is {header.KeyLength}."),
        };
}
