using CodeBase.Net.Dbf;

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
    /// <returns>The byte the tag's trailing-pad counts stand for.</returns>
    /// <exception cref="CodeBaseException">
    /// The expression is not the name of a field of this table, so its type cannot be settled without
    /// evaluating it. The message names the expression, because that is what a caller has to act on.
    /// </exception>
    public static byte PadByteFor(IndexHeader header, IReadOnlyList<FieldDefinition> fields)
    {
        // A collation other than machine order settles it by itself: every collated key pads with NUL,
        // character keys included. No field is consulted, and none is needed (ADR-27).
        if (header.PadByte is byte settled)
            return settled;

        string expression = header.Expression.Trim();

        foreach (FieldDefinition field in fields)
        {
            if (!string.Equals(field.Name, expression, StringComparison.OrdinalIgnoreCase))
                continue;

            return PadByteOf(field, header, expression);
        }

        throw new CodeBaseException(
            ErrorCode.NotSupported,
            $"The key expression '{expression}' is not the name of a field of this table, so this " +
            $"library cannot tell what type its keys are — and a key's type decides what its padding " +
            $"is. Reading such a tag needs the expression engine.");
    }

    /// <summary>
    /// Maps a field's type to the byte a key built from it pads with.
    /// </summary>
    private static byte PadByteOf(FieldDefinition field, IndexHeader header, string expression) =>
        field.Type switch
        {
            // Character data, and 'Z', the binary-marked character type stored exactly like it.
            'C' or 'Z' => KeyPadding.Space,

            // Everything whose key is a fixed-width number: numeric and float go through the double
            // transform, as do date and datetime; currency and integer have their own. All pad with NUL.
            'N' or 'F' or 'B' or 'Y' or 'I' or 'D' or 'T' => KeyPadding.Nul,

            // A logical, memo or general field is not something the C library builds an ordinary key
            // from, and no corpus case has one. Refused rather than guessed at.
            _ => throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"The key expression '{expression}' names a field of type '{field.Type}', which this " +
                $"library does not know how to key. The tag's key length is {header.KeyLength}."),
        };
}
