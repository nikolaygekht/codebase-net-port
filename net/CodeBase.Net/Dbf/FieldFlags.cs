namespace CodeBase.Net.Dbf;

/// <summary>
/// The properties a field descriptor records about its field, in one byte.
///
/// Visual FoxPro defines the first three; auto-increment and auto-timestamp are CodeBase
/// extensions. The byte is read only for a Visual FoxPro table: on an older table the same byte
/// exists but means nothing, and the C library does not look at it. Governing specification:
/// DBF-FORMAT.md section 4.
/// </summary>
[Flags]
public enum FieldFlags : byte
{
    /// <summary>
    /// No property is set, which is the usual case.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// The field is a system field, hidden from the table's field list.
    /// </summary>
    /// <value>Set only on the null-flags bitmap, which is also marked binary.</value>
    System = 0x01,

    /// <summary>
    /// The field accepts null, and owns one bit of the null-flags bitmap.
    /// </summary>
    /// <value>
    /// Nullability is per field and opt in. A table whose fields are all ordinary carries this on
    /// none of them and has no bitmap field at all.
    /// </value>
    Nullable = 0x02,

    /// <summary>
    /// The field holds bytes rather than text, so no code-page translation applies to it.
    /// </summary>
    /// <value>
    /// This is what distinguishes the binary variants of the character and memo types, which are
    /// stored under the type letter of their text counterparts.
    /// </value>
    Binary = 0x04,

    /// <summary>
    /// The field is filled from the counter in the file header when a record is appended.
    /// </summary>
    /// <value>A CodeBase extension, stored differently from Visual FoxPro's own auto-increment.</value>
    AutoIncrement = 0x08,

    /// <summary>
    /// The field is filled with the current time when a record is appended.
    /// </summary>
    /// <value>A CodeBase extension.</value>
    AutoTimestamp = 0x10,
}
