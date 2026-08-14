namespace CodeBase.Net.Cdx;

/// <summary>
/// Picks the weight tables a collated tag is keyed with, from the table's code page.
///
/// "GENERAL" names a sort order, not a table. The C library keeps a separate one per code page —
/// [c]cp1252generalCollationArray[/c], [c]cp437generalCollationArray[/c],
/// [c]cp850generalCollationArray[/c] (i4conv.c:314-480) — because the same byte means a different
/// character in each, and a weight table is indexed by byte.
///
/// The index file records only the name. Which table it meant is therefore a property of the
/// [i]table[/i], and that is why the code page has to reach this far: pairing GENERAL with the wrong
/// one produces keys that read fine and sort wrongly, which no test of the index alone would notice.
///
/// Governing specification: KEY-COLLATION.md sections 3.2 and 3.3.
/// </summary>
internal static class CollationWeights
{
    /// <summary>
    /// Returns the weight table a collation uses on a table of a given code page.
    /// </summary>
    /// <param name="collation">The collation the tag names.</param>
    /// <param name="codePage">The table's code page number.</param>
    /// <returns>The weight table.</returns>
    /// <exception cref="CodeBaseException">
    /// The collation has no table for that code page. Refused rather than approximated: a weight
    /// table for the wrong code page is a wrong key for every character above 127.
    /// </exception>
    public static ReadOnlySpan<byte> TableFor(CollationName collation, int codePage)
    {
        Refuse(collation, codePage);

        return codePage switch
        {
            1252 => CollationTables.Cp1252General,
            437 => CollationTables.Cp437General,
            _ => CollationTables.Cp850General,
        };
    }

    /// <summary>
    /// Returns the expansion table a collation uses on a table of a given code page.
    /// </summary>
    /// <param name="collation">The collation the tag names.</param>
    /// <param name="codePage">The table's code page number.</param>
    /// <returns>The expansion table.</returns>
    /// <exception cref="CodeBaseException">The collation has no table for that code page.</exception>
    public static ReadOnlySpan<byte> ExpansionsFor(CollationName collation, int codePage)
    {
        Refuse(collation, codePage);

        // cp437 shares cp1252's expansions; cp850 has its own, differing in the last two
        // (COLL4ARR.C:579 and 847-855).
        return codePage == 850 ? CollationTables.Cp850Expansions : CollationTables.Cp1252Expansions;
    }

    /// <summary>
    /// Reports whether this library can weigh keys for a collation on a code page.
    /// </summary>
    /// <param name="collation">The collation the tag names.</param>
    /// <param name="codePage">The table's code page number.</param>
    /// <returns>True when a weight table exists for the pair.</returns>
    public static bool Supports(CollationName collation, int codePage) =>
        collation == CollationName.Machine || codePage is 1252 or 437 or 850;

    private static void Refuse(CollationName collation, int codePage)
    {
        if (Supports(collation, codePage) && collation != CollationName.Machine)
            return;

        throw new CodeBaseException(
            ErrorCode.NotSupported,
            collation == CollationName.Machine
                ? "Machine collation keys are the field's bytes and have no weight table."
                : $"This library has {collation} weights for code pages 1252, 437 and 850, and the " +
                  $"table declares {codePage}. Weighing its keys with another code page's table " +
                  "would order them wrongly rather than fail.");
    }
}
