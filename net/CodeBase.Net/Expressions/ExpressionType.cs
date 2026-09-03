namespace CodeBase.Net.Expressions;

/// <summary>
/// The type of a value an expression node produces, or that a function parameter accepts.
///
/// Each member's numeric value is the character code the C library uses for that type
/// (d4defs.h:2774-2821), kept rather than renumbered so that a row of the ported function table can
/// be compared with the reference table's own row with nothing standing between them. The names are
/// C#, the values are the reference implementation's.
///
/// Ten of these are reachable in this port, because an expression's type set is a closure over what
/// [c]DBF-READ[/c] opens rather than a choice this engine makes: [c]Character[/c],
/// [c]NumericAsText[/c], [c]NumericAsDouble[/c], [c]DateAsText[/c], [c]DateAsDouble[/c],
/// [c]Logical[/c], [c]Integer[/c], [c]Currency[/c], [c]Timestamp[/c] and [c]BinaryFloat[/c]. The
/// rest name field types the opener refuses, so no expression can produce one; they are listed
/// because the function table has rows for them and the table is ported entire.
///
/// Governing specification: EXPRESSIONS.md section 2.4, with section 1 of the step design for why
/// the set is derived and not chosen.
/// </summary>
internal enum ExpressionType : byte
{
    /// <summary>
    /// No type, which is how the table spells an absent parameter or an unused return.
    /// </summary>
    None = 0,

    /// <summary>
    /// A double, which the C library spells with the same letter it gives a binary field.
    /// </summary>
    /// <value>
    /// The C library's [c]r4double[/c], which shares the letter [c]B[/c] with [c]r4bin[/c]
    /// (d4defs.h:2774-2775). No row of the function table uses it.
    /// </value>
    Double = (byte)'B',

    /// <summary>
    /// A fixed-length byte string.
    /// </summary>
    /// <value>The C library's [c]r4str[/c] (d4defs.h:2776).</value>
    Character = (byte)'C',

    /// <summary>
    /// A date carried as a Julian day number in a double.
    /// </summary>
    /// <value>The C library's [c]r4dateDoub[/c] (d4defs.h:2777).</value>
    DateAsDouble = (byte)'d',

    /// <summary>
    /// A date carried as eight characters of century, year, month and day.
    /// </summary>
    /// <value>The C library's [c]r4date[/c] (d4defs.h:2778).</value>
    DateAsText = (byte)'D',

    /// <summary>
    /// A float field, which the parser treats exactly as a numeric field.
    /// </summary>
    /// <value>The C library's [c]r4float[/c] (d4defs.h:2779, e4parse.c:2283-2285).</value>
    Float = (byte)'F',

    /// <summary>
    /// A general field, which the parser treats as a memo.
    /// </summary>
    /// <value>The C library's [c]r4gen[/c] (d4defs.h:2782, e4parse.c:2296-2298).</value>
    General = (byte)'G',

    /// <summary>
    /// A four-byte IEEE float, the type a binary float field loads as.
    /// </summary>
    /// <value>The C library's [c]r4floatBin[/c] (d4defs.h:2781).</value>
    BinaryFloat = (byte)'H',

    /// <summary>
    /// A four-byte little-endian signed integer.
    /// </summary>
    /// <value>The C library's [c]r4int[/c] (d4defs.h:2783).</value>
    Integer = (byte)'I',

    /// <summary>
    /// A logical, whose runtime value is zero or one.
    /// </summary>
    /// <value>The C library's [c]r4log[/c] (d4defs.h:2784).</value>
    Logical = (byte)'L',

    /// <summary>
    /// A memo.
    /// </summary>
    /// <value>The C library's [c]r4memo[/c] (d4defs.h:2785).</value>
    Memo = (byte)'M',

    /// <summary>
    /// A numeric carried as a double.
    /// </summary>
    /// <value>The C library's [c]r4numDoub[/c] (d4defs.h:2786).</value>
    NumericAsDouble = (byte)'n',

    /// <summary>
    /// A numeric carried as the field's own text, of the field's own width.
    /// </summary>
    /// <value>
    /// The C library's [c]r4num[/c] (d4defs.h:2787). Not the same key route as
    /// [c]NumericAsDouble[/c], which is why the two are kept apart.
    /// </value>
    NumericAsText = (byte)'N',

    /// <summary>
    /// A wide string carrying its own length in characters at the end of the field.
    /// </summary>
    /// <value>The C library's [c]r5wstrLen[/c] (d4defs.h:2795). Unreachable in this port.</value>
    WideStringWithLength = (byte)'O',

    /// <summary>
    /// A four-byte unsigned integer.
    /// </summary>
    /// <value>The C library's [c]r5ui4[/c] (d4defs.h:2797). Unreachable in this port.</value>
    UnsignedInteger = (byte)'P',

    /// <summary>
    /// A two-byte signed integer.
    /// </summary>
    /// <value>The C library's [c]r5i2[/c] (d4defs.h:2798). Unreachable in this port.</value>
    Short = (byte)'Q',

    /// <summary>
    /// A two-byte unsigned integer.
    /// </summary>
    /// <value>The C library's [c]r5ui2[/c] (d4defs.h:2799). Unreachable in this port.</value>
    UnsignedShort = (byte)'R',

    /// <summary>
    /// A timestamp, as a Julian day number and milliseconds since midnight.
    /// </summary>
    /// <value>The C library's [c]r4dateTime[/c] (d4defs.h:2804).</value>
    Timestamp = (byte)'T',

    /// <summary>
    /// A string of sixteen-bit characters.
    /// </summary>
    /// <value>The C library's [c]r5wstr[/c] (d4defs.h:2807). Unreachable in this port.</value>
    WideString = (byte)'W',

    /// <summary>
    /// A currency, an eight-byte integer scaled by ten to the minus fourth.
    /// </summary>
    /// <value>The C library's [c]r4currency[/c] (d4defs.h:2810).</value>
    Currency = (byte)'Y',

    /// <summary>
    /// An eight-byte signed integer.
    /// </summary>
    /// <value>The C library's [c]r5i8[/c] (d4defs.h:2814). Unreachable in this port.</value>
    Long = (byte)'1',

    /// <summary>
    /// A six-byte OLE DB date structure.
    /// </summary>
    /// <value>The C library's [c]r5dbDate[/c] (d4defs.h:2815). Unreachable in this port.</value>
    OleDbDate = (byte)'2',

    /// <summary>
    /// A six-byte OLE DB time structure.
    /// </summary>
    /// <value>The C library's [c]r5dbTime[/c] (d4defs.h:2816). Unreachable in this port.</value>
    OleDbTime = (byte)'3',

    /// <summary>
    /// A sixteen-byte OLE DB timestamp structure.
    /// </summary>
    /// <value>The C library's [c]r5dbTimeStamp[/c] (d4defs.h:2817). Unreachable in this port.</value>
    OleDbTimestamp = (byte)'4',

    /// <summary>
    /// A timestamp accurate to the millisecond.
    /// </summary>
    /// <value>
    /// The C library's [c]r4dateTimeMilli[/c] (d4defs.h:2821). Unreachable in this port: it arrives
    /// only from a field type the opener refuses.
    /// </value>
    TimestampWithMilliseconds = (byte)'7',
}
