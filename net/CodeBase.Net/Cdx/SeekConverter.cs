using CodeBase.Net.Dbf;
using CodeBase.Net.Expressions;

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

    /// <summary>A four-byte binary float, which only an expression over an H field produces.</summary>
    Single,

    /// <summary>A truth value, keyed as one letter, which only an expression produces.</summary>
    Logical,
}

/// <summary>
/// Whether a tag's whole expression converts a string into key form, and which way.
/// </summary>
/// <value>
/// Such a tag stores the conversion's bytes, not the string's, so a string sought in it has to go
/// through the same conversion to be found (ADR-64).
/// </value>
internal enum KeyConversion
{
    /// <summary>The expression is not, as a whole, an ascending or descending conversion of a string.</summary>
    None,

    /// <summary>The expression is [c]ASCEND[/c] of a string.</summary>
    Ascending,

    /// <summary>The expression is [c]DESCEND[/c] of a string: the ascending form, every byte complemented.</summary>
    Descending,
}

/// <summary>
/// Everything a tag needs in order to turn a value into a key, resolved once when the tag is opened.
///
/// The C library does this at open too, picking a pair of conversion functions from the key's type
/// and hanging them off the tag (tfile4initSeekConv, i4init.c:557-753). The same choice is made here,
/// as data rather than function pointers, and it is the only place the mapping lives.
///
/// The kind now comes from the [b]compiled expression's result type[/b] rather than from a field's
/// declared type, which is what lets a tag be keyed on anything the engine can compute. A tag over a
/// bare field still records the field, because the seek path takes a caller's value in its stored
/// form and needs to know what that form is.
///
/// The compiled expression is [b]kept[/b] and not discarded once the kind, the length and the pad
/// byte have been read off it. The query optimizer decides whether a tag can stand in for a
/// subexpression by comparing compiled node arrays, so throwing this away would make that
/// impossible, and recompiling from the header text later is not the same thing.
/// </summary>
internal sealed class SeekConverter
{
    private SeekConverter(
        KeyKind kind,
        FieldDefinition? field,
        CompiledExpression compiled,
        int keyLength,
        byte padByte,
        CollationName collation,
        int codePage,
        KeyConversion conversion)
    {
        Kind = kind;
        Field = field;
        Compiled = compiled;
        KeyLength = keyLength;
        PadByte = padByte;
        Collation = collation;
        CodePage = codePage;
        Conversion = conversion;
    }

    /// <summary>Gets the kind of key the tag stores.</summary>
    public KeyKind Kind { get; }

    /// <summary>Gets the field the tag is built on, or null when its expression is not a bare name.</summary>
    public FieldDefinition? Field { get; }

    /// <summary>Gets the tag's compiled key expression.</summary>
    public CompiledExpression Compiled { get; }

    /// <summary>Gets the tag's key length.</summary>
    public int KeyLength { get; }

    /// <summary>Gets the byte the tag pads its keys with.</summary>
    public byte PadByte { get; }

    /// <summary>Gets which collation weighs this tag's character keys.</summary>
    public CollationName Collation { get; }

    /// <summary>Gets the code page of the table, which decides which weight table the collation means.</summary>
    public int CodePage { get; }

    /// <summary>Gets whether the tag's whole expression is an ascending or descending conversion of a string.</summary>
    public KeyConversion Conversion { get; }

    /// <summary>
    /// Gets a value indicating whether the key carries a leading byte saying the value is there.
    /// </summary>
    public bool HasNullIndicator => ExpressionKey.CanBeAbsent(Compiled);

    /// <summary>
    /// Resolves the converter for a tag.
    /// </summary>
    /// <param name="header">The tag's header.</param>
    /// <param name="fields">The fields of the table the tag indexes.</param>
    /// <param name="codePage">The table's code page number.</param>
    /// <param name="options">The date picture and century policy the expression is read under.</param>
    /// <param name="alias">The name a field reference may be qualified by.</param>
    /// <returns>The converter.</returns>
    /// <exception cref="CodeBaseException">
    /// The expression cannot be compiled, produces a type this library does not key, cannot be used
    /// for a key because its value would not be the same twice, or names a collation this library has
    /// no weights for on the table's code page.
    /// </exception>
    public static SeekConverter For(
        IndexHeader header,
        IReadOnlyList<FieldDefinition> fields,
        int codePage,
        ExpressionOptions options,
        string alias)
    {
        string expression = header.Expression.Trim();
        CollationName collation = header.Collation;

        // The table's name goes to the scanner as well as to the resolver: a tag expression may
        // qualify a field with it, and the scanner is where the reference reads the aliases a name
        // may carry (e4parse.c:2357-2417). Without it the qualified form is refused as an unknown
        // field, which is what this port did until the query path made the omission visible. It is
        // resolved for this tag, as the reference parses it, because the tag's collation and a bare
        // memo change lengths -- and lengths are what the optimizer matches a filter's terms on.
        CompiledExpression compiled = TypeResolver.Resolve(
            ExpressionParser.Parse(expression, new TableFieldMetadata(fields, alias), [alias]),
            TagContext.For(collation, codePage));

        RefuseIfNotTheSameTwice(compiled, expression);

        // One compile, one resolution, one refusal: the pad byte follows from the same result type
        // the kind does, so it is read off the expression already compiled here rather than by
        // compiling it a second time.
        byte padByte = header.PadByte ?? KeyTypeResolver.PadByteOf(compiled.ResultType, header, expression);

        KeyKind kind = StoredKindOf(compiled, collation, codePage, expression);

        FieldDefinition? field = fields.FirstOrDefault(
            f => string.Equals(f.Name, expression, StringComparison.OrdinalIgnoreCase));

        return new SeekConverter(
            kind, field, compiled, header.KeyLength, padByte, collation, codePage, ConversionOf(compiled));
    }

    /// <summary>
    /// Works out the kind of key a tag stores, and refuses a collation this library has no weights for.
    /// </summary>
    /// <value>
    /// A collated string tag over an ascending or descending conversion stores bytes the conversion
    /// has already weighed, so its keys are copied rather than collated a second time
    /// (E4EXPR.C:671-684). The conversion still needs the weights, so they are required all the same.
    /// Which weight table a collation means depends on the table's code page, and the index does not
    /// record it; this is the one point that knows both, so a mismatch is refused before any key is
    /// built from the wrong table.
    /// </value>
    private static KeyKind StoredKindOf(
        CompiledExpression compiled, CollationName collation, int codePage, string expression)
    {
        KeyKind kind = KindOf(compiled.ResultType, collation, expression);
        bool converts = compiled.Nodes.Any(n => n.Row is >= FunctionRows.Descend and <= FunctionRows.LastAscend);

        if ((kind == KeyKind.CollatedCharacter || (converts && collation != CollationName.Machine))
            && !CollationWeights.Supports(collation, codePage))
        {
            throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"Tag '{expression}' is collated {collation}, and this library has no {collation} " +
                $"weights for the table's code page {codePage}.");
        }

        return kind == KeyKind.CollatedCharacter && converts ? KeyKind.Character : kind;
    }

    /// <summary>
    /// Works out whether an expression is, as a whole, an ascending or descending conversion of a string.
    /// </summary>
    /// <value>
    /// Only the root counts. Where a conversion is part of a larger expression, a value sought in the
    /// tag does not map onto a key, and it is sought as it stands.
    /// </value>
    private static KeyConversion ConversionOf(CompiledExpression compiled)
    {
        int row = compiled.Nodes[^1].Row;

        if (row is < FunctionRows.Descend or > FunctionRows.LastAscend
            || FunctionTable.Row(row).Parameter1 != ExpressionType.Character)
        {
            return KeyConversion.None;
        }

        return row < FunctionRows.Ascend ? KeyConversion.Descending : KeyConversion.Ascending;
    }

    /// <summary>
    /// Refuses an expression whose value is not the same twice (Decision 5).
    /// </summary>
    /// <value>
    /// A key built from the clock or from where the cursor happens to be is a key nothing can find
    /// again, and the reference does not forbid it — it simply produces a tag nobody can maintain.
    /// This port refuses it when the tag is [i]selected[/i], and says which function made it so,
    /// because a caller who sees the name can fix the expression. A filter over the same functions
    /// evaluates normally; it is only a stored key that cannot survive them.
    /// </value>
    private static void RefuseIfNotTheSameTwice(CompiledExpression compiled, string expression)
    {
        foreach (ExpressionNode node in compiled.Nodes)
        {
            string? name = node.Row switch
            {
                FunctionRows.Today => "DATE",
                FunctionRows.Time => "TIME",
                FunctionRows.RecordNumber => "RECNO",
                FunctionRows.RecordCount => "RECCOUNT",
                _ => null,
            };

            if (name is not null)
            {
                throw new CodeBaseException(
                    ErrorCode.NotSupported,
                    $"The key expression '{expression}' uses {name}(), whose value is not the same " +
                    "twice, so a key built from it could never be found again.");
            }
        }
    }

    /// <summary>
    /// Maps an expression's result type to the kind of key a tag over it stores.
    /// </summary>
    /// <remarks>
    /// The rows of i4init.c:557-753 that this library keys, read by result type rather than by field
    /// type. A string is the only one whose answer depends on anything else, because a collation
    /// replaces its bytes with weights. The four types that carry a number, a date or their stored
    /// text all key through the same transform, which is why they share one kind.
    /// </remarks>
    private static KeyKind KindOf(ExpressionType type, CollationName collation, string expression) =>
        type switch
        {
            ExpressionType.Character or ExpressionType.Memo or ExpressionType.General =>
                collation == CollationName.Machine ? KeyKind.Character : KeyKind.CollatedCharacter,
            ExpressionType.NumericAsText or ExpressionType.NumericAsDouble
                or ExpressionType.DateAsDouble => KeyKind.Double,
            ExpressionType.DateAsText => KeyKind.Date,
            ExpressionType.Integer => KeyKind.Int32,
            ExpressionType.Currency => KeyKind.Currency,
            ExpressionType.Timestamp or ExpressionType.TimestampWithMilliseconds => KeyKind.DateTime,
            ExpressionType.BinaryFloat => KeyKind.Single,
            ExpressionType.Logical => KeyKind.Logical,
            _ => throw new CodeBaseException(
                ErrorCode.NotSupported,
                $"The key expression '{expression}' produces a value of type '{(char)type}', which " +
                "this library does not build keys from."),
        };
}
