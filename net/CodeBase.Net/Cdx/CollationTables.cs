namespace CodeBase.Net.Cdx;

/// <summary>
/// The Visual FoxPro collation weight tables, copied byte for byte from the C library.
///
/// A GENERAL-collated key is not the field's text: each byte is replaced by a [i]head[/i] weight,
/// which decides the primary order, and optionally a [i]tail[/i] weight, which breaks ties between
/// characters that share a head. Case pairs share both, so GENERAL is case-insensitive; accented
/// letters share the head of their base letter and differ only in the tail.
///
/// These tables are data, not an algorithm, and they are reproduced rather than derived. .NET's
/// [c]CompareInfo[/c] cannot stand in for them: it sorts correctly for a human but produces
/// different bytes, and the bytes are what a stored index key is made of. See KEY-COLLATION.md
/// section 3.3.
///
/// One deliberate resolution: the cp1252 table carries four [c]S4ELICON[/c] blocks, a Swedish sort
/// variant that build does not define, so each takes its [c]#else[/c] branch here.
/// </summary>
internal static class CollationTables
{
    /// <summary>The tail value meaning the character has no secondary weight.</summary>
    /// <value>The C library's [c]NO4TAIL_BYTES[/c] (d4defs.h:1903-1918).</value>
    public const byte NoTail = 0xFF;

    /// <summary>The head value marking a character that expands into two others.</summary>
    /// <value>
    /// The C library's [c]EXPAND4CHAR_TO_TWO_BYTES[/c]. The tail is then an index into the
    /// expansion table rather than a weight.
    /// </value>
    public const byte Expands = 0xFF;

    /// <summary>
    /// The Cp1252 GENERAL weights, one head and one tail per byte value.
    /// </summary>
    /// <value>Copied verbatim from [c]cp1252generalCollationArray[/c] (COLL4ARR.C:21-300).</value>
    private static readonly byte[] Cp1252GeneralData =
    [
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 0-7
         16, 255,  17, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 8-15
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 16-23
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 24-31
         17, 255,  18, 255,  19, 255,  20, 255,  21, 255,  22, 255,  23, 255,  24, 255,   // 32-39
         25, 255,  26, 255,  27, 255,  28, 255,  29, 255,  30, 255,  31, 255,  32, 255,   // 40-47
         86, 255,  87, 255,  88, 255,  89, 255,  90, 255,  91, 255,  92, 255,  93, 255,   // 48-55
         94, 255,  95, 255,  33, 255,  34, 255,  35, 255,  36, 255,  37, 255,  38, 255,   // 56-63
         39, 255,  96,   0,  97, 255,  98,   0, 100, 255, 102,   0, 103, 255, 104, 255,   // 64-71
        105, 255, 106,   0, 107, 255, 108, 255, 109, 255, 111, 255, 112,   0, 114,   0,   // 72-79
        115, 255, 116, 255, 117, 255, 118,   0, 119, 255, 120,   0, 122, 255, 123, 255,   // 80-87
        124, 255, 125,   0, 126, 255,  40, 255,  41, 255,  42, 255,  43, 255,  44, 255,   // 88-95
         45, 255,  96,   0,  97, 255,  98,   0, 100, 255, 102,   0, 103, 255, 104, 255,   // 96-103
        105, 255, 106,   0, 107, 255, 108, 255, 109, 255, 111, 255, 112,   0, 114,   0,   // 104-111
        115, 255, 116, 255, 117, 255, 118,   0, 119, 255, 120,   0, 122, 255, 123, 255,   // 112-119
        124, 255, 125,   0, 126, 255,  46, 255,  47, 255,  48, 255,  49, 255,  16, 255,   // 120-127
         16, 255,  16, 255,  24, 255,  50, 255,  19, 255,  51, 255,  52, 255,  53, 255,   // 128-135
         54, 255,  55, 255, 118,   8,  24, 255, 255,   0,  16, 255,  16, 255,  16, 255,   // 136-143
         16, 255,  24, 255,  24, 255,  19, 255,  19, 255,  56, 255,  30, 255,  30, 255,   // 144-151
         57, 255,  58, 255, 118,   8,  24, 255, 255,   0,  16, 255,  16, 255, 125,   4,   // 152-159
         32,   1,  59, 255,  60, 255,  61, 255,  62, 255,  63, 255,  64, 255,  65, 255,   // 160-167
         66, 255,  67, 255,  68, 255,  19, 255,  69, 255,  30, 255,  70, 255,  71, 255,   // 168-175
         72, 255,  73, 255,  88, 255,  89, 255,  74, 255,  75, 255,  76, 255,  77, 255,   // 176-183
         78, 255,  87, 255,  79, 255,  19, 255,  80, 255,  81, 255,  82, 255,  83, 255,   // 184-191
         96,   2,  96,   1,  96,   3,  96,   5,  96,   4,  96,   6, 255,   1,  98,   7,   // 192-199
        102,   2, 102,   1, 102,   3, 102,   4, 106,   2, 106,   1, 106,   3, 106,   4,   // 200-207
        101, 255, 112,   5, 114,   2, 114,   1, 114,   3, 114,   5, 114,   4,  84, 255,   // 208-215
        129, 255, 120,   2, 120,   1, 120,   3, 120,   4, 125,   1, 255,   2, 255,   3,   // 216-223
         96,   2,  96,   1,  96,   3,  96,   5,  96,   4,  96,   6, 255,   1,  98,   7,   // 224-231
        102,   2, 102,   1, 102,   3, 102,   4, 106,   2, 106,   1, 106,   3, 106,   4,   // 232-239
        101, 255, 112,   5, 114,   2, 114,   1, 114,   3, 114,   5, 114,   4,  85, 255,   // 240-247
        129, 255, 120,   2, 120,   1, 120,   3, 120,   4, 125,   1, 255,   2, 125,   4,   // 248-255
    ];

    /// <summary>
    /// The Cp437 GENERAL weights, one head and one tail per byte value.
    /// </summary>
    /// <value>Copied verbatim from [c]cp437generalCollationArray[/c] (COLL4ARR.C:315-577).</value>
    private static readonly byte[] Cp437GeneralData =
    [
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 0-7
         16, 255,  17, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 8-15
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 16-23
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 24-31
         17, 255,  18, 255,  19, 255,  20, 255,  21, 255,  22, 255,  23, 255,  24, 255,   // 32-39
         25, 255,  26, 255,  27, 255,  28, 255,  29, 255,  30, 255,  31, 255,  32, 255,   // 40-47
         86, 255,  87, 255,  88, 255,  89, 255,  90, 255,  91, 255,  92, 255,  93, 255,   // 48-55
         94, 255,  95, 255,  33, 255,  34, 255,  35, 255,  36, 255,  37, 255,  38, 255,   // 56-63
         39, 255,  96,   0,  97, 255,  98,   0, 100, 255, 102,   0, 103, 255, 104, 255,   // 64-71
        105, 255, 106,   0, 107, 255, 108, 255, 109, 255, 111, 255, 112,   0, 114,   0,   // 72-79
        115, 255, 116, 255, 117, 255, 118,   0, 119, 255, 120,   0, 122, 255, 123, 255,   // 80-87
        124, 255, 125,   0, 126, 255,  40, 255,  41, 255,  42, 255,  43, 255,  44, 255,   // 88-95
         45, 255,  96,   0,  97, 255,  98,   0, 100, 255, 102,   0, 103, 255, 104, 255,   // 96-103
        105, 255, 106,   0, 107, 255, 108, 255, 109, 255, 111, 255, 112,   0, 114,   0,   // 104-111
        115, 255, 116, 255, 117, 255, 118,   0, 119, 255, 120,   0, 122, 255, 123, 255,   // 112-119
        124, 255, 125,   0, 126, 255,  46, 255,  47, 255,  48, 255,  49, 255,  16, 255,   // 120-127
         98,   7, 120,   4, 102,   1,  96,   3,  96,   4,  96,   2,  96,   6,  98,   7,   // 128-135
        102,   3, 102,   4, 102,   2, 106,   4, 106,   3, 106,   2,  96,   4,  96,   6,   // 136-143
        102,   1, 255,   1, 255,   1, 114,   3, 114,   4, 114,   2, 120,   3, 120,   2,   // 144-151
        125,   4, 114,   4, 120,   4,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 152-159
         96,   1, 106,   1, 114,   1, 120,   1, 112,   5, 112,   5,  16, 255,  16, 255,   // 160-167
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 168-175
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 176-183
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 184-191
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 192-199
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 200-207
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 208-215
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 216-223
         16, 255, 255,   3,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 224-231
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 232-239
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 240-247
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 248-255
    ];

    /// <summary>
    /// The Cp850 GENERAL weights, one head and one tail per byte value.
    /// </summary>
    /// <value>Copied verbatim from [c]cp850generalCollationArray[/c] (COLL4ARR.C:583-843).</value>
    private static readonly byte[] Cp850GeneralData =
    [
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 0-7
         16, 255,  17, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 8-15
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 16-23
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 24-31
         17, 255,  18, 255,  19, 255,  20, 255,  21, 255,  22, 255,  23, 255,  24, 255,   // 32-39
         25, 255,  26, 255,  27, 255,  28, 255,  29, 255,  30, 255,  31, 255,  32, 255,   // 40-47
         86, 255,  87, 255,  88, 255,  89, 255,  90, 255,  91, 255,  92, 255,  93, 255,   // 48-55
         94, 255,  95, 255,  33, 255,  34, 255,  35, 255,  36, 255,  37, 255,  38, 255,   // 56-63
         39, 255,  96,   0,  97, 255,  98,   0, 100, 255, 102,   0, 103, 255, 104, 255,   // 64-71
        105, 255, 106,   0, 107, 255, 108, 255, 109, 255, 111, 255, 112,   0, 114,   0,   // 72-79
        115, 255, 116, 255, 117, 255, 118,   0, 119, 255, 120,   0, 122, 255, 123, 255,   // 80-87
        124, 255, 125,   0, 126, 255,  40, 255,  41, 255,  42, 255,  43, 255,  44, 255,   // 88-95
         45, 255,  96,   0,  97, 255,  98,   0, 100, 255, 102,   0, 103, 255, 104, 255,   // 96-103
        105, 255, 106,   0, 107, 255, 108, 255, 109, 255, 111, 255, 112,   0, 114,   0,   // 104-111
        115, 255, 116, 255, 117, 255, 118,   0, 119, 255, 120,   0, 122, 255, 123, 255,   // 112-119
        124, 255, 125,   0, 126, 255,  46, 255,  47, 255,  48, 255,  49, 255,  16, 255,   // 120-127
         98,   7, 255,   3, 102,   1,  96,   3, 255,   1,  96,   2,  96,   6,  98,   7,   // 128-135
        102,   3, 102,   4, 102,   2, 106,   4, 106,   3, 106,   2, 255,   1,  96,   6,   // 136-143
        102,   1, 255,   1, 255,   1, 114,   3, 255,   0, 114,   2, 120,   3, 120,   2,   // 144-151
        125,   4, 255,   0, 255,   3, 129, 255,  16, 255, 129, 255,  16, 255,  16, 255,   // 152-159
         96,   1, 106,   1, 114,   1, 120,   1, 112,   5, 112,   5,  16, 255,  16, 255,   // 160-167
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 168-175
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  96,   1,  96,   3,  96,   2,   // 176-183
         67, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 184-191
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  96,   5,  96,   5,   // 192-199
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 200-207
        101, 255, 101, 255, 102,   3, 102,   4, 102,   2,  16, 255, 106,   1, 106,   3,   // 208-215
        106,   4,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255, 106,   2,  16, 255,   // 216-223
        114,   1, 255,   2, 114,   3, 114,   2, 114,   5, 114,   5,  16, 255, 119, 105,   // 224-231
        119, 105, 120,   1, 120,   3, 120,   2, 125,   1, 125,   1,  16, 255,  16, 255,   // 232-239
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 240-247
         16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,  16, 255,   // 248-255
    ];

    /// <summary>The cp1252 and cp437 expansions, in the order their indexes name them.</summary>
    /// <value>
    /// Copied from [c]cp1252generalCompressArray[/c] (COLL4ARR.C:304-311), which cp437 shares
    /// (COLL4ARR.C:579). Each pair is the two characters the expanding one sorts as.
    /// </value>
    private static readonly byte[] Cp1252ExpansionData = [79, 69, 65, 69, 84, 72, 83, 83];

    /// <summary>The cp850 expansions, which differ from cp1252 in their third and fourth.</summary>
    /// <value>Copied from [c]cp850generalCompressArray[/c] (COLL4ARR.C:847-855).</value>
    private static readonly byte[] Cp850ExpansionData = [79, 69, 65, 69, 83, 83, 85, 69];

    /// <summary>
    /// Returns the weights a collation gives one byte value.
    /// </summary>
    /// <param name="table">The collation's weight table.</param>
    /// <param name="value">The byte to weigh.</param>
    /// <returns>Its head and tail weights.</returns>
    public static (byte Head, byte Tail) Weigh(ReadOnlySpan<byte> table, byte value) =>
        (table[value * 2], table[(value * 2) + 1]);

    /// <summary>
    /// Returns the two characters an expanding character sorts as.
    /// </summary>
    /// <param name="expansions">The collation's expansion table.</param>
    /// <param name="index">The index the character's tail carried.</param>
    /// <returns>The pair of characters to weigh in its place.</returns>
    /// <exception cref="CodeBaseException">The index names no expansion in this collation.</exception>
    public static (byte First, byte Second) Expand(ReadOnlySpan<byte> expansions, byte index)
    {
        if (index * 2 >= expansions.Length)
        {
            throw new CodeBaseException(
                ErrorCode.Index,
                $"A key names expansion {index}, and this collation defines " +
                $"{expansions.Length / 2}.");
        }

        return (expansions[index * 2], expansions[(index * 2) + 1]);
    }

    /// <summary>Gets the cp1252 GENERAL weight table.</summary>
    public static ReadOnlySpan<byte> Cp1252General => Cp1252GeneralData;

    /// <summary>Gets the cp437 GENERAL weight table.</summary>
    public static ReadOnlySpan<byte> Cp437General => Cp437GeneralData;

    /// <summary>Gets the cp850 GENERAL weight table.</summary>
    public static ReadOnlySpan<byte> Cp850General => Cp850GeneralData;

    /// <summary>Gets the expansion table cp1252 and cp437 share.</summary>
    public static ReadOnlySpan<byte> Cp1252Expansions => Cp1252ExpansionData;

    /// <summary>Gets the cp850 expansion table.</summary>
    public static ReadOnlySpan<byte> Cp850Expansions => Cp850ExpansionData;
}
