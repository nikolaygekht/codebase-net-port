namespace CodeBase.Net.Cdx;

/// <summary>
/// Builds the search for a string in a tag whose whole expression is ASCEND or DESCEND of a string.
///
/// Such a tag stores the conversion of the text, not the text: the bytes as they are in machine order,
/// the collation's weights under a collated one, and either of those complemented for DESCEND. The
/// reference seeks it by converting the caller's value with the collation alone, so it finds an
/// ASCEND tag's keys and never a DESCEND tag's (EXPRESSIONS.md section 9.5). This library puts the
/// value through the same conversion the key went through, so a caller seeks the text itself (ADR-64).
///
/// A whole search pads the value to the operand's width first, as the stored value was. A prefix
/// search keeps only what the caller gave: under a collation, only the heads, which is the part of a
/// stored key a shorter value can predict. Complementing keeps a prefix a prefix, so DESCEND needs no
/// other change.
/// </summary>
internal static class ConversionSearch
{
    /// <summary>
    /// Writes the search for a value through the tag's conversion.
    /// </summary>
    /// <param name="converter">The tag's converter; its [c]Conversion[/c] is not [c]None[/c].</param>
    /// <param name="value">The text to look for, as stored bytes.</param>
    /// <param name="prefix">Whether a short value stands for a prefix rather than a whole key.</param>
    /// <param name="destination">Where to write, at least the tag's key length.</param>
    /// <returns>How many bytes of the destination the search covers.</returns>
    public static int Write(SeekConverter converter, ReadOnlySpan<byte> value, bool prefix, Span<byte> destination)
    {
        // The conversion's operand is the node just before it in postfix order, and its length is the
        // width of the text the stored keys were converted from.
        int width = converter.Compiled.Nodes[^2].Length;
        ReadOnlySpan<byte> text = value[..Math.Min(value.Length, width)];

        int length = converter.Collation == CollationName.Machine
            ? InByteOrder(text, width, prefix, destination)
            : Collated(converter, text, width, prefix, destination);

        if (converter.Conversion == KeyConversion.Descending)
        {
            for (int i = 0; i < length; i++)
                destination[i] = (byte)~destination[i];
        }

        return length;
    }

    private static int InByteOrder(ReadOnlySpan<byte> text, int width, bool prefix, Span<byte> destination)
    {
        text.CopyTo(destination);
        if (prefix)
            return text.Length;

        destination[text.Length..width].Fill((byte)' ');
        return width;
    }

    private static int Collated(
        SeekConverter converter, ReadOnlySpan<byte> text, int width, bool prefix, Span<byte> destination)
    {
        ReadOnlySpan<byte> table = CollationWeights.TableFor(converter.Collation, converter.CodePage);
        ReadOnlySpan<byte> expansions = CollationWeights.ExpansionsFor(converter.Collation, converter.CodePage);

        if (prefix)
            return CollatedKey.WriteSearch(text, table, expansions, converter.KeyLength, destination);

        Span<byte> padded = stackalloc byte[width];
        padded.Fill((byte)' ');
        text.CopyTo(padded);

        return CollatedKey.Write(padded, table, expansions, includeTails: true, destination);
    }
}
