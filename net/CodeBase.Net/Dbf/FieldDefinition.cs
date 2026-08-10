namespace CodeBase.Net.Dbf;

/// <summary>
/// One field of an open table, as the engine understands it rather than as the file stores it.
///
/// Four things differ from the stored descriptor, and each of them matters to a caller. The name is
/// upper-cased. The type is the one the field was created with, so the binary memo and binary
/// character types reappear under their own letters instead of the ones they are stored under. The
/// length is resolved, which for a character field means combining two stored bytes into one number
/// that can reach 65535. And the offset is recomputed by accumulation rather than taken from the
/// descriptor, which is what makes the fields add up to the record.
/// </summary>
public sealed class FieldDefinition
{
    internal FieldDefinition(
        string name,
        char type,
        char storedType,
        int length,
        int decimals,
        int recordOffset,
        bool isBinary,
        bool isSystem,
        bool isAutoIncrement,
        bool isAutoTimestamp,
        int? nullBit)
    {
        Name = name;
        Type = type;
        StoredType = storedType;
        Length = length;
        Decimals = decimals;
        RecordOffset = recordOffset;
        IsBinary = isBinary;
        IsSystem = isSystem;
        IsAutoIncrement = isAutoIncrement;
        IsAutoTimestamp = isAutoTimestamp;
        NullBit = nullBit;
    }

    /// <summary>
    /// Gets the field name, upper-cased.
    /// </summary>
    /// <value>
    /// Upper-cased because the engine does that when it opens a table, so a field written in mixed
    /// case is reported and found in upper case.
    /// </value>
    public string Name { get; }

    /// <summary>
    /// Gets the type the field was created with.
    /// </summary>
    /// <value>
    /// The letter a caller should reason about. It differs from the stored letter for the two
    /// binary variants: a binary memo is stored as a memo and a binary character field as a
    /// character field, each marked binary, and both are reported here under their own letter.
    /// </value>
    public char Type { get; }

    /// <summary>
    /// Gets the type letter the file stores for this field, upper-cased.
    /// </summary>
    /// <value>Equal to the created type except for the two binary variants.</value>
    public char StoredType { get; }

    /// <summary>
    /// Gets the number of bytes the field occupies in a record.
    /// </summary>
    /// <value>
    /// Resolved, not simply the stored length byte: a character field carries a 16-bit length split
    /// across two bytes of its descriptor, so this can exceed 255.
    /// </value>
    public int Length { get; }

    /// <summary>
    /// Gets the number of decimal places.
    /// </summary>
    /// <value>
    /// Zero for every type that has no use for one, including character fields, whose descriptor
    /// keeps a length byte where other types keep this.
    /// </value>
    public int Decimals { get; }

    /// <summary>
    /// Gets the offset of the field within a record, counting the deletion flag as offset zero.
    /// </summary>
    /// <value>
    /// Accumulated from the lengths of the fields before it, never read from the descriptor. A file
    /// may disagree with itself about stored offsets; accumulated ones always add up to the record
    /// length, which is what keeps a field's bytes inside the record it belongs to.
    /// </value>
    public int RecordOffset { get; }

    /// <summary>
    /// Gets a value indicating whether the field holds bytes rather than text.
    /// </summary>
    /// <value>Binary fields are exempt from code-page translation.</value>
    public bool IsBinary { get; }

    /// <summary>
    /// Gets a value indicating whether the field is a hidden system field.
    /// </summary>
    /// <value>True only of the null-flags bitmap, which is not part of a table's field list.</value>
    public bool IsSystem { get; }

    /// <summary>
    /// Gets a value indicating whether the field is filled from the header counter on append.
    /// </summary>
    public bool IsAutoIncrement { get; }

    /// <summary>
    /// Gets a value indicating whether the field is filled with the current time on append.
    /// </summary>
    public bool IsAutoTimestamp { get; }

    /// <summary>
    /// Gets the bit of the null-flags bitmap that says whether this field is null.
    /// </summary>
    /// <value>
    /// Counted over the nullable fields in the order they appear, so an ordinary field between two
    /// nullable ones consumes no bit and the ordinal is not the field's position. Absent when the
    /// field cannot be null.
    /// </value>
    public int? NullBit { get; }

    /// <summary>
    /// Gets a value indicating whether the field accepts null.
    /// </summary>
    public bool IsNullable => NullBit.HasValue;

    /// <summary>
    /// Returns the field rendered as its name, type and length.
    /// </summary>
    /// <returns>A short description, for diagnostics and test failure messages.</returns>
    public override string ToString() => $"{Name} {Type}({Length},{Decimals}) @{RecordOffset}";
}
