using CodeBase.Net.Cdx;

namespace CodeBase.Net;

/// <summary>
/// One index tag of a table: an ordering of its records, by a key the index already holds.
///
/// A tag is a description and holds no position, so two callers may hold the same one while the table
/// has a single cursor — the same shape [c]FieldDefinition[/c] has for fields.
///
/// What a tag orders by is its **key expression**, which the index stores as text and this library does
/// not evaluate: the keys were computed when the index was written and are read back as bytes. That is
/// why a tag can be navigated without an expression engine, and why one whose expression is not a plain
/// field name cannot yet be selected.
/// </summary>
public sealed class Tag
{
    internal Tag(CdxTag inner)
    {
        Inner = inner;
    }

    /// <summary>
    /// Gets the tag's name, upper-cased as the file stores it.
    /// </summary>
    /// <value>
    /// In a compound index this is the name the tag directory holds. In a single-tag file the file
    /// records no name at all, so it is the file's own name without path or extension.
    /// </value>
    public string Name => Inner.Name;

    /// <summary>
    /// Gets the key expression as text, exactly as the index stores it.
    /// </summary>
    public string Expression => Inner.Header.Expression;

    /// <summary>
    /// Gets the FOR clause as text, empty when the tag has none.
    /// </summary>
    /// <value>
    /// A tag with one holds keys for only the records that satisfy it, so navigating it reaches fewer
    /// records than the table has. Nothing here evaluates the clause: the keys that exist are already
    /// the filtered set.
    /// </value>
    public string Filter => Inner.Header.Filter;

    /// <summary>
    /// Gets the number of bytes in each of the tag's keys.
    /// </summary>
    /// <value>
    /// Not the width of the field the key came from: a GENERAL-collated character key is twice its
    /// field's width, because it carries a block of secondary weights after the primary ones.
    /// </value>
    public int KeyLength => Inner.KeyLength;

    /// <summary>
    /// Gets a value indicating whether the tag orders records from its greatest key down.
    /// </summary>
    /// <value>
    /// Keys are stored ascending either way; the flag inverts traversal and nothing else.
    /// </value>
    public bool Descending => Inner.Descending;

    /// <summary>
    /// Gets a value indicating whether the tag holds one key per distinct value.
    /// </summary>
    /// <value>
    /// A unique tag reaches fewer records than the table has, like a filtered one, and for a different
    /// reason: the duplicates were never added.
    /// </value>
    public bool Unique =>
        Inner.Header.Options.HasFlag(TagOptions.Unique) || Inner.Header.Options.HasFlag(TagOptions.Candidate);

    /// <summary>
    /// Gets a value indicating whether the tag has a FOR clause.
    /// </summary>
    public bool Filtered => Inner.Header.Options.HasFlag(TagOptions.HasFilter);

    /// <summary>
    /// Gets the collation the tag's keys were built with, as the header names it.
    /// </summary>
    /// <value>Empty for machine order, or [c]GENERAL[/c] for the Visual FoxPro weight tables.</value>
    public string Collation => Inner.Header.CollationText;

    /// <summary>
    /// The tag as the index reader sees it.
    /// </summary>
    internal CdxTag Inner { get; }

    /// <summary>
    /// Returns the tag's name and what it orders by, for diagnostics and failure messages.
    /// </summary>
    /// <returns>A short description of the tag.</returns>
    public override string ToString() =>
        $"{Name} on {Expression}{(Descending ? " descending" : string.Empty)}";
}
