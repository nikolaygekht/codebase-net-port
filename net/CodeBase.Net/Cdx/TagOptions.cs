namespace CodeBase.Net.Cdx;

/// <summary>
/// What a tag header's option byte says about the tag.
///
/// The values are the bits the C library sets in [c]typeCode[/c], kept as a flags enum so that
/// "unique" is a value rather than a mask repeated at four call sites. Two of them are tested rather
/// than merely stored: a file is compound when the byte is 0x40 or above (i4index.c:1760), and a
/// header carries expression text when it is below 0x80 (i4init.c:420), which is how the hidden
/// tag directory is told apart from a tag.
///
/// Governing specification: CDX-FORMAT.md section 3.1.
/// </summary>
[Flags]
internal enum TagOptions : byte
{
    /// <summary>
    /// No option bits at all, which no real header carries.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Duplicate keys are not stored: the tag holds one key per distinct value.
    /// </summary>
    /// <value>
    /// Set for every kind of uniqueness except a candidate key, which uses its own bit instead
    /// (i4create.c:924-931).
    /// </value>
    Unique = 0x01,

    /// <summary>
    /// The key expression can produce a null.
    /// </summary>
    Nullable = 0x02,

    /// <summary>
    /// The tag is a candidate key, which is uniqueness that refuses nulls as well.
    /// </summary>
    /// <value>Set instead of [c]Unique[/c], not alongside it (i4create.c:926-928).</value>
    Candidate = 0x04,

    /// <summary>
    /// The tag has a FOR clause, so its keys cover only the records that satisfy it.
    /// </summary>
    /// <value>The filter text follows the expression text in the header block.</value>
    HasFilter = 0x08,

    /// <summary>
    /// Leaf nodes are bit-packed rather than fixed-size entry arrays.
    /// </summary>
    /// <value>
    /// Set on every tag the C library writes, and required: a header below 0x20 is refused at open
    /// (i4index.c:1706), which is why an uncompressed FoxPro 2.x index cannot be read here.
    /// </value>
    Compact = 0x20,

    /// <summary>
    /// The file holds several tags, indexed by a hidden tag-name tree.
    /// </summary>
    /// <value>
    /// Tested as "0x40 or above", so it is the bit that separates a compound file from a single-tag
    /// one (i4index.c:1760).
    /// </value>
    Compound = 0x40,

    /// <summary>
    /// The header is the tag directory itself rather than a tag.
    /// </summary>
    /// <value>
    /// The C library tests for "below 0x80" to decide whether to read expression text (i4init.c:420),
    /// so this bit means the header has none.
    /// </value>
    TagDirectory = 0x80,
}
