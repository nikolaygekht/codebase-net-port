namespace CodeBase.Net.Cdx;

/// <summary>
/// One entry of a tag: a key and the record it points at.
///
/// The key is the stored bytes, rebuilt to its full length and padded — not the value the key was
/// computed from. Recovering that value is the expression engine's business and for most key types is
/// not possible at all, because the transforms that make bytes sort correctly are not all reversible.
///
/// Keys sort by unsigned byte comparison, and where two are equal the record number breaks the tie.
/// That pair, not the key alone, is what the tree orders by.
/// </summary>
/// <param name="Key">The stored key bytes, exactly as long as the tag's key length.</param>
/// <param name="Record">The record number, counting from one.</param>
internal readonly record struct IndexEntry(byte[] Key, uint Record);
