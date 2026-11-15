namespace CodeBase.Net.Cdx;

/// <summary>
/// Picks the weight tables a collated tag is keyed with, from its collation and the table's code page.
///
/// "GENERAL" names a sort order, not a table. The C library keeps a separate one per code page —
/// [c]cp1252generalCollationArray[/c], [c]cp437generalCollationArray[/c],
/// [c]cp850generalCollationArray[/c] (i4conv.c:314-480) — because the same byte means a different
/// character in each, and a weight table is indexed by byte. The index file records only the name,
/// so which table it meant is a property of the [i]table[/i]: pairing GENERAL with the wrong one
/// produces keys that read fine and sort wrongly.
///
/// A CodeBase collation, [c]CBnnnnn[/c], is the opposite: it names its table outright, and the
/// table's code page changes nothing (i4init.c:407-416, ADR-84). Four of them are simple one-for-one
/// mappings; Avaya and the three named GENERALs weigh heads and tails as GENERAL does.
///
/// Governing specification: KEY-COLLATION.md sections 3.2 and 3.3.
/// </summary>
internal static class CollationWeights
{
    /// <summary>
    /// Returns the weight table a collation uses on a table of a given code page.
    /// </summary>
    /// <param name="collation">The collation the tag names.</param>
    /// <param name="codePage">The table's code page number; read for GENERAL only.</param>
    /// <returns>The weight table.</returns>
    /// <exception cref="CodeBaseException">
    /// The collation has no table for that code page. Refused rather than approximated: a weight
    /// table for the wrong code page is a wrong key for every character above 127.
    /// </exception>
    public static ReadOnlySpan<byte> TableFor(CollationName collation, int codePage)
    {
        Refuse(collation, codePage);

        return collation switch
        {
            CollationName.General => codePage switch
            {
                1252 => CollationTables.Cp1252General,
                437 => CollationTables.Cp437General,
                _ => CollationTables.Cp850General,
            },
            CollationName.GeneralCp1252 => CollationTables.Cp1252General,
            CollationName.GeneralCp437 => CollationTables.Cp437General,
            CollationName.GeneralCp850 => CollationTables.Cp850General,
            CollationName.CroatianCp1250 => CollationTables.Cp1250Croatian,
            CollationName.CroatianUpperCp1250 => CollationTables.Cp1250CroatianUpper,
            CollationName.Avaya1252 => CollationTables.Avaya1252,
            CollationName.SpanishCp1252 => CollationTables.SpanishCp1252,
            _ => CollationTables.SpanishCp850,
        };
    }

    /// <summary>
    /// Returns the expansion table a collation uses on a table of a given code page.
    /// </summary>
    /// <param name="collation">The collation the tag names.</param>
    /// <param name="codePage">The table's code page number; read for GENERAL only.</param>
    /// <returns>The expansion table, empty for a simple collation, which expands nothing.</returns>
    /// <exception cref="CodeBaseException">The collation has no table for that code page.</exception>
    public static ReadOnlySpan<byte> ExpansionsFor(CollationName collation, int codePage)
    {
        Refuse(collation, codePage);

        // cp437 and Avaya share cp1252's expansions; cp850 has its own, differing in the last two
        // (COLL4ARR.C:579, 847-855 and 2169).
        return collation switch
        {
            CollationName.General when codePage == 850 => CollationTables.Cp850Expansions,
            CollationName.GeneralCp850 => CollationTables.Cp850Expansions,
            CollationName.General or CollationName.GeneralCp1252 or CollationName.GeneralCp437
                or CollationName.Avaya1252 => CollationTables.Cp1252Expansions,
            _ => [],
        };
    }

    /// <summary>
    /// Reports whether this library can weigh keys for a collation on a code page.
    /// </summary>
    /// <param name="collation">The collation the tag names.</param>
    /// <param name="codePage">The table's code page number.</param>
    /// <returns>True when a weight table exists for the pair.</returns>
    public static bool Supports(CollationName collation, int codePage) =>
        collation != CollationName.General || codePage is 1252 or 437 or 850;

    /// <summary>
    /// Reports whether a collation maps each byte to one weight and nothing else.
    /// </summary>
    /// <param name="collation">The collation.</param>
    /// <returns>True for the Croatian and Spanish collations.</returns>
    /// <value>
    /// The reference's [c]collate4simpleMapping[/c] (D4SEEK.C:25-36), a key size that adds nothing per
    /// character. Such a key is the field's width, keeps its trailing blanks' weights, and is sought
    /// by its prefix as it stands.
    /// </value>
    public static bool IsSimple(CollationName collation) =>
        collation is CollationName.CroatianCp1250 or CollationName.CroatianUpperCp1250
            or CollationName.SpanishCp1252 or CollationName.SpanishCp850;

    /// <summary>
    /// Returns how many key bytes a collation adds for each character of the value.
    /// </summary>
    /// <param name="collation">The collation.</param>
    /// <returns>One for a head-and-tail collation, none for machine order or a simple one.</returns>
    /// <value>The reference's [c]keySizeCharPerCharAdd[/c] (i4conv.c:314-480).</value>
    public static int PerCharacterAdd(CollationName collation) =>
        collation == CollationName.Machine || IsSimple(collation) ? 0 : 1;

    private static void Refuse(CollationName collation, int codePage)
    {
        if (collation != CollationName.Machine && Supports(collation, codePage))
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
