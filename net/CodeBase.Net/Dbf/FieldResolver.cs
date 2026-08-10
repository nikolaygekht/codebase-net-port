using System.Globalization;

namespace CodeBase.Net.Dbf;

/// <summary>
/// Turns the descriptors a file stores into the fields an open table has.
///
/// This is where the stored view becomes the engine's view: names are upper-cased, the two binary
/// variants reappear under their own type letters, lengths are resolved per type, null bits are
/// numbered, and offsets are accumulated so that the fields and the deletion flag account for the
/// record exactly. That last point is the whole of this library's containment guarantee, so it is
/// checked rather than assumed.
///
/// Validation is thin on purpose. It rejects what a release build of the C library rejects and no
/// more, because refusing a file the reference implementation reads makes this port less compatible
/// while looking more careful. What keeps that safe is the accumulation check, not the type rules.
/// </summary>
internal static class FieldResolver
{
    /// <summary>
    /// The name the null-flags field is stored under, matched with its case.
    /// </summary>
    private const string NullFlagsName = "_NullFlags";

    /// <summary>
    /// The stored type letters this library reads.
    /// </summary>
    /// <value>
    /// The CodeBase and OLE-DB extensions are absent deliberately. They are real types the C
    /// library knows, but creating or reading them is outside this port's first version, and
    /// accepting them would mean shipping their length rules with no corpus case behind them.
    /// </value>
    private static readonly char[] ReadableTypes =
        ['C', 'N', 'F', 'D', 'L', 'M', 'G', 'I', 'B', 'Y', 'T', 'H', '0'];

    /// <summary>
    /// The stored type letters that Visual FoxPro introduced.
    /// </summary>
    private static readonly char[] VisualFoxProOnlyTypes = ['B', 'H', 'Y', 'T', '0'];

    /// <summary>
    /// Resolves every descriptor into a field, and checks that they account for the record.
    /// </summary>
    /// <param name="descriptors">The descriptors as stored, in file order.</param>
    /// <param name="variant">How this version of the format is read.</param>
    /// <param name="recordLength">The record length the header declares.</param>
    /// <returns>The table's fields, and its null-flags field if it has one.</returns>
    /// <exception cref="CodeBaseException">
    /// A descriptor names a type this library does not read, or a type the table's version does not
    /// allow, or a length that type cannot have; or the fields do not account for the record.
    /// </exception>
    public static ResolvedFields Resolve(
        IReadOnlyList<FieldDescriptor> descriptors,
        IDbfFormatVariant variant,
        int recordLength)
    {
        List<FieldDefinition> resolved = new(descriptors.Count);
        int offset = 1;         // offset zero is the deletion flag
        int nullBitCount = 0;

        foreach (FieldDescriptor descriptor in descriptors)
        {
            char storedType = char.ToUpperInvariant(descriptor.Type);
            Validate(descriptor, storedType, variant);

            // Flags mean nothing unless the version says they do, so on an older table every field
            // is plain text however the byte is set.
            FieldFlags flags = variant.InterpretsDescriptorFlags ? descriptor.Flags : FieldFlags.None;
            bool isBinary = flags.HasFlag(FieldFlags.Binary);
            bool isNullable = flags.HasFlag(FieldFlags.Nullable);

            (int length, int decimals) = ResolveSize(storedType, descriptor, variant);

            resolved.Add(new FieldDefinition(
                descriptor.Name.ToUpperInvariant(),
                ReportedType(storedType, isBinary),
                storedType,
                length,
                decimals,
                offset,
                isBinary,
                flags.HasFlag(FieldFlags.System),
                flags.HasFlag(FieldFlags.AutoIncrement),
                flags.HasFlag(FieldFlags.AutoTimestamp),
                isNullable ? nullBitCount++ : null));

            offset += length;
        }

        // The invariant everything else rests on. With offsets accumulated from one, this makes
        // every field's bytes a subrange of the record by construction, so no later code has to
        // trust a length or an offset to stay inside it.
        if (offset != recordLength)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"The fields occupy {offset} bytes including the deletion flag, but the header " +
                $"declares a record length of {recordLength}.");
        }

        return Split(resolved);
    }

    /// <summary>
    /// Refuses a field this library cannot read, or that its table's version cannot carry.
    /// </summary>
    private static void Validate(FieldDescriptor descriptor, char storedType, IDbfFormatVariant variant)
    {
        if (!ReadableTypes.Contains(storedType))
        {
            throw new CodeBaseException(
                ErrorCode.FieldType,
                $"Field '{descriptor.Name}' has type '{descriptor.Type}', which this library does " +
                "not read.");
        }

        if (!variant.AllowsVisualFoxProTypes && VisualFoxProOnlyTypes.Contains(storedType))
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"Field '{descriptor.Name}' has type '{storedType}', which a table of version " +
                $"0x{variant.NormalizedVersion:X2} cannot carry.");
        }

        // Matched against the stored bytes with their case, as the C library does, so a field of
        // this type under any other name is a corrupt descriptor rather than a hidden bitmap.
        if (storedType == '0' && !string.Equals(descriptor.Name, NullFlagsName, StringComparison.Ordinal))
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"Field '{descriptor.Name}' is a system field but is not named {NullFlagsName}.");
        }

        // The only length rule a release build of the C library enforces on a type this port reads.
        // Everything else it checks belongs to a type rejected above, and the familiar rules for
        // dates, logicals, memos and the rest fire only in its debug build.
        if (storedType == 'I' && descriptor.Length != 4)
        {
            throw new CodeBaseException(
                ErrorCode.Data,
                $"Field '{descriptor.Name}' is an integer of {descriptor.Length} bytes; " +
                "an integer is four.");
        }
    }

    /// <summary>
    /// Reports the type a field was created with, which is not always the type it is stored as.
    /// </summary>
    private static char ReportedType(char storedType, bool isBinary) => (storedType, isBinary) switch
    {
        ('C', true) => 'Z',
        ('M', true) => 'X',
        _ => storedType,
    };

    /// <summary>
    /// Resolves a field's length and decimal count, which the C library does per type.
    /// </summary>
    private static (int Length, int Decimals) ResolveSize(
        char storedType,
        FieldDescriptor descriptor,
        IDbfFormatVariant variant) => storedType switch
    {
        // Fixed-width types whose descriptor keeps no decimal count.
        'I' or 'L' or 'D' or 'M' or 'G' => (descriptor.Length, 0),

        // A double reports its decimal count only where doubles exist at all.
        'B' => variant.AllowsVisualFoxProTypes ? (descriptor.Length, (int)descriptor.Decimals) : (descriptor.Length, 0),

        // Types that genuinely have decimal places.
        'N' or 'F' or 'Y' or 'T' or 'H' => (descriptor.Length, descriptor.Decimals),

        // Everything else, which is the character type and the null-flags field: the decimal byte
        // is the high half of a 16-bit length, so the field can reach 65535 bytes.
        _ => (descriptor.Length + (descriptor.Decimals << 8), 0),
    };

    /// <summary>
    /// Separates the hidden null-flags field from the fields a table reports.
    /// </summary>
    private static ResolvedFields Split(List<FieldDefinition> resolved)
    {
        // Only a trailing system field is hidden, matching d4numFields, which subtracts one only
        // when the last field is the bitmap. One anywhere else stays an ordinary field.
        if (resolved.Count > 0 && resolved[^1].StoredType == '0')
            return new ResolvedFields(resolved[..^1], resolved[^1]);

        return new ResolvedFields(resolved, null);
    }
}

/// <summary>
/// The fields of an open table, with its null-flags field held apart from them.
/// </summary>
/// <param name="Fields">
/// The fields a caller sees, in file order, excluding the null-flags field.
/// </param>
/// <param name="NullFlags">
/// The hidden field holding the bitmap of which nullable fields are null, or null when the table
/// has no nullable field.
/// </param>
internal sealed record ResolvedFields(IReadOnlyList<FieldDefinition> Fields, FieldDefinition? NullFlags);
