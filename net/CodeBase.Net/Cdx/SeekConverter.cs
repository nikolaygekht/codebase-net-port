using CodeBase.Net.Dbf;

namespace CodeBase.Net.Cdx;

/// <summary>
/// What kind of key a tag stores, which decides how a value becomes key bytes.
/// </summary>
/// <value>
/// One entry per row of the C library's own selection table (tfile4initSeekConv, i4init.c:557-753),
/// narrowed to the types this library keys.
/// </value>
internal enum KeyKind
{
    /// <summary>Character data in machine order: the bytes as they stand.</summary>
    Character,

    /// <summary>Character data weighed through a collation.</summary>
    CollatedCharacter,

    /// <summary>An eight-byte double, which numeric, float and true double fields all become.</summary>
    Double,

    /// <summary>A four-byte signed integer.</summary>
    Int32,

    /// <summary>A currency value, held as ten-thousandths.</summary>
    Currency,

    /// <summary>A date, keyed as its Julian day number.</summary>
    Date,

    /// <summary>A datetime, keyed through the day, the rounded time and the decrement table.</summary>
    DateTime,
}

/// <summary>
/// Everything a tag needs in order to turn a value into a key, resolved once when the tag is opened.
///
/// The C library does this at open too, picking a pair of conversion functions from the key's type
/// and hanging them off the tag (tfile4initSeekConv, i4init.c:557-753). The same choice is made here,
/// as data rather than function pointers, and it is the only place the mapping lives.
///
/// A tag whose key expression is not a bare field name is refused, exactly as its pad byte is
/// (ADR-28). That refusal is what makes this total: for every tag a caller can select, the key type
/// is known, so building a key can fail because a record has no entry but never because the library
/// could not work out what to build.
/// </summary>
internal sealed class SeekConverter
{
    private SeekConverter(
        KeyKind kind,
        FieldDefinition? field,
        int keyLength,
        byte padByte,
        CollationName collation,
        int codePage)
    {
        Kind = kind;
        Field = field;
        KeyLength = keyLength;
        PadByte = padByte;
        Collation = collation;
        CodePage = codePage;
    }

    /// <summary>Gets the kind of key the tag stores.</summary>
    public KeyKind Kind { get; }

    /// <summary>Gets the field the tag is built on, or null when the tag is not a bare field.</summary>
    public FieldDefinition? Field { get; }

    /// <summary>Gets the tag's key length.</summary>
    public int KeyLength { get; }

    /// <summary>Gets the byte the tag pads its keys with.</summary>
    public byte PadByte { get; }

    /// <summary>Gets which collation weighs this tag's character keys.</summary>
    public CollationName Collation { get; }

    /// <summary>Gets the code page of the table, which decides which weight table the collation means.</summary>
    public int CodePage { get; }

    /// <summary>
    /// Resolves the converter for a tag.
    /// </summary>
    /// <param name="header">The tag's header.</param>
    /// <param name="fields">The fields of the table the tag indexes.</param>
    /// <param name="codePage">The table's code page number.</param>
    /// <returns>The converter.</returns>
    /// <exception cref="CodeBaseException">
    /// The expression is not a bare field name, names a field whose type this library does not key,
    /// or names a collation this library has no weights for on the table's code page.
    /// </exception>
    public static SeekConverter For(
        IndexHeader header, IReadOnlyList<FieldDefinition> fields, int codePage)
    {
        string expression = header.Expression.Trim();
        byte padByte = KeyTypeResolver.PadByteFor(header, fields);
        CollationName collation = header.Collation;

        FieldDefinition? field = fields.FirstOrDefault(
            f => string.Equals(f.Name, expression, StringComparison.OrdinalIgnoreCase));

        if (field is null)
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"The key expression '{expression}' is not the name of a field of this table, so a " +
                $"value cannot be turned into a key for it. That needs the expression engine.");
        }

        KeyKind kind = KindOf(field, collation, expression);

        // A collated tag names a sort order; which weight table that means depends on the table's
        // code page, and the index does not record it. Settled here, at the one point that knows
        // both, so a mismatch is refused before any key is built from the wrong table.
        if (kind == KeyKind.CollatedCharacter && !CollationWeights.Supports(collation, codePage))
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"Tag '{expression}' is collated {collation}, and this library has no {collation} " +
                $"weights for the table's code page {codePage}.");
        }

        return new SeekConverter(kind, field, header.KeyLength, padByte, collation, codePage);
    }

    /// <summary>
    /// Maps a field's type to the kind of key a tag over it stores.
    /// </summary>
    /// <remarks>
    /// The rows of i4init.c:557-753 that this library keys. A character field is the only one whose
    /// answer depends on anything but its type, because a collation replaces its bytes with weights.
    /// </remarks>
    private static KeyKind KindOf(FieldDefinition field, CollationName collation, string expression) =>
        field.Type switch
        {
            'C' or 'Z' => collation == CollationName.Machine
                ? KeyKind.Character
                : KeyKind.CollatedCharacter,
            'N' or 'F' or 'B' => KeyKind.Double,
            'I' => KeyKind.Int32,
            'Y' => KeyKind.Currency,
            'D' => KeyKind.Date,
            'T' => KeyKind.DateTime,
            _ => throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"The key expression '{expression}' names a field of type '{field.Type}', which this " +
                "library does not build keys from."),
        };
}
