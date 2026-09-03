using T = CodeBase.Net.Expressions.ExpressionType;

namespace CodeBase.Net.Expressions;

/// <summary>
/// The built-in function table, transcribed from the reference implementation.
///
/// This is data, not code: 193 rows in the reference implementation's own order, because that order
/// is semantic. Overload resolution walks forward from a matched row through its equal-coded
/// siblings, the parser reads an operator's binding power out of a row, name lookup returns the
/// first row whose name matches, and the query optimizer will classify an operator by which band of
/// row indexes it falls in. Every one of those breaks if a row moves.
///
/// The rows were transcribed from the C source (e4functi.c:32-301) and are gated against a dump of
/// the same table taken from the compiled library, which is a genuinely separate reading of it: a
/// slip in the transcription cannot hide, because the two sources are the source text and the built
/// artefact. The gate compares every field of every row, name length included.
///
/// The reference build this port targets is the one the corpus generator links, which is why the two
/// report rows carry no name and the power rows are present.
///
/// Governing specification: EXPRESSIONS.md section 5.
/// </summary>
internal static class FunctionTable
{
    /// <summary>
    /// The code the last row carries, which is how the reference implementation ends a scan.
    /// </summary>
    public const int Terminator = -1;

    /// <summary>
    /// The first row of the logical constants and the prefix not, looked up where a value is expected.
    /// </summary>
    public const int FirstLogical = 30;

    /// <summary>
    /// The last row of the logical constants and the prefix not.
    /// </summary>
    public const int LastLogical = 35;

    /// <summary>
    /// The first row an operator lookup examines, where an operator is expected.
    /// </summary>
    public const int FirstOperator = 36;

    /// <summary>
    /// The last row an operator lookup examines.
    /// </summary>
    /// <value>
    /// The reference implementation puts this two rows past the last real operator, so an operator
    /// lookup can also match the first two named functions (e4expr.h:154). Transcribed as it stands,
    /// because narrowing it would be this port accepting a different language from the reference.
    /// </value>
    public const int LastOperator = 106;

    /// <summary>
    /// The first row a named-function lookup examines.
    /// </summary>
    public const int FirstFunction = 105;

    /// <summary>
    /// The longest name a lookup will compare across both bands.
    /// </summary>
    /// <value>
    /// Nine, which is [c]REPLICATE[/c]. Widening the buffer past the reference's eight cannot let a
    /// longer name match a reference row, because a lookup also requires the recorded name length to
    /// equal the name's, and no reference row records more than eight.
    /// </value>
    private const int LongestName = 9;

    /// <summary>
    /// The longest name a lookup will compare, which is the size of the reference's own name buffer.
    /// </summary>
    /// <value>
    /// Eight (e4parse.c:1224-1233). A longer name is not an error of its own; it simply matches
    /// nothing, because the lookup also requires the lengths to be equal.
    /// </value>
    public const int MaximumNameLength = 8;

    private static readonly FunctionRow[] TableRows =
    [
        // Field loaders, one per field type the reference implementation can open. The types beyond
        // what DBF-READ admits are transcribed but unreachable: no expression can produce one.
        new(null, 0, 0, 0, T.Character, 0),                    /*   1 - E4FIELD_STR */
        new(null, 0, 0, 0, T.WideString, 0),                   /*   2 - E4FIELD_WSTR */
        new(null, 0, 0, 0, T.WideStringWithLength, 0),         /*   3 - E4FIELD_WSTR_LEN */
        new(null, 1, 0, 0, T.Character, 0),                    /*   4 - E4FIELD_STR_CAT */
        new(null, 2, 0, 0, T.WideString, 0),                   /*   5 - E4FIELD_STR_CAT */
        new(null, 3, 0, 0, T.Logical, 0),                      /*   6 - E4FIELD_LOG */
        new(null, 4, 0, 0, T.DateAsDouble, 0),                 /*   7 - E4FIELD_DATE_D */
        new(null, 5, 0, 0, T.DateAsText, 0),                   /*   8 - E4FIELD_DATE_S */
        new(null, 6, 0, 0, T.NumericAsDouble, 0),              /*   9 - E4FIELD_NUM_D */
        new(null, 7, 0, 0, T.NumericAsText, 0),                /*  10 - E4FIELD_NUM_S */
        new(null, 440, 0, 0, T.Currency, 0),                   /*  11 - E4FIELD_CUR */
        new(null, 441, 0, 0, T.NumericAsDouble, 0),            /*  12 - E4FIELD_DOUB */
        new(null, 442, 0, 0, T.Integer, 0),                    /*  13 - E4FIELD_INT */
        new(null, 443, 0, 0, T.UnsignedInteger, 0),            /*  14 - E4FIELD_UNS_INT */
        new(null, 444, 0, 0, T.Short, 0),                      /*  15 - E4FIELD_SHORT */
        new(null, 445, 0, 0, T.UnsignedShort, 0),              /*  16 - E4FIELD_UNS_SHORT */
        new(null, 446, 0, 0, T.Timestamp, 0),                  /*  17 - E4FIELD_DTTIME */
        new(null, 447, 0, 0, T.TimestampWithMilliseconds, 0),  /*  18 - E4FIELD_DTTIME_MILLI */
        new(null, 448, 0, 0, T.NumericAsDouble, 0),            /*  19 - E4FIELD_INT_D */
        new(null, 449, 0, 0, T.NumericAsDouble, 0),            /*  20 - E4FIELD_CUR_D */
        new(null, 450, 0, 0, T.Long, 0),                       /*  21 - E4FIELD_I8 */
        new(null, 451, 0, 0, T.OleDbDate, 0),                  /*  22 - E4FIELD_DBDATE */
        new(null, 452, 0, 0, T.OleDbTime, 0),                  /*  23 - E4FIELD_DBTIME */
        new(null, 453, 0, 0, T.OleDbTimestamp, 0),             /*  24 - E4FIELD_DBTIMESTAMP */
        new(null, 454, 0, 0, T.BinaryFloat, 0),                /*  25 - E4FIELD_BINFLOAT */
        new(null, 456, 0, 0, T.Float, 0),                      /*  26 - E4FIELD_INT_F */
        new(null, 7, 0, 0, T.Character, 0),                    /*  27 - E4FIELD_MEMO */

        // The constant loaders. A literal enters an expression through one of these three.
        new(null, 8, 0, 0, T.NumericAsDouble, 0),              /*  28 - E4DOUBLE */
        new(null, 9, 0, 0, T.Character, 0),                    /*  29 - E4STRING */
        new(null, 10, 0, 0, T.BinaryFloat, 0),                 /*  30 - E4FLOAT */

        // The logical constants and the prefix not, looked up where a value is expected.
        new(".TRUE.", 14, 6, 0, T.Logical, 0),                 /*  31 - */
        new(".T.", 14, 3, 0, T.Logical, 0),                    /*  32 - */
        new(".FALSE.", 16, 7, 0, T.Logical, 0),                /*  33 - */
        new(".F.", 16, 3, 0, T.Logical, 0),                    /*  34 - */
        new(".NOT.", 18, 5, 5, T.Logical, 1, T.Logical),       /*  35 - */
        new("!", 18, 1, 5, T.Logical, 1, T.Logical),           /*  36 - */

        // The operators. A run of equal codes is one source-level operator's overloads, and the
        // named row of each run is the one an operator lookup can reach.
        new(".OR.", 20, 4, 3, T.Logical, -1, T.Logical),       /*  37 - variadic */
        new(".AND.", 22, 5, 4, T.Logical, -1, T.Logical),      /*  38 - variadic */

        new("+", 25, 1, 7, T.Character, 2, T.Character, T.Character),                             /*  39 - */
        new(null, 25, 0, 7, T.WideString, 2, T.WideString, T.WideString),                         /*  40 - */
        new(null, 25, 0, 7, T.WideStringWithLength, 2, T.WideStringWithLength, T.WideStringWithLength), /*  41 - */
        new(null, 25, 0, 7, T.Character, 2, T.Character, T.Character),                            /*  42 - */
        new(null, 25, 0, 7, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),          /*  43 - */
        new(null, 25, 0, 7, T.DateAsDouble, 2, T.NumericAsDouble, T.DateAsDouble),                /*  44 - */
        new(null, 25, 0, 7, T.DateAsDouble, 2, T.DateAsDouble, T.NumericAsDouble),                /*  45 - */
        new(null, 25, 0, 7, T.BinaryFloat, 2, T.BinaryFloat, T.BinaryFloat),                      /*  46 - */

        new("-", 30, 1, 7, T.Character, 2, T.Character, T.Character),                             /*  47 - */
        new(null, 30, 0, 7, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),          /*  48 - */
        new(null, 30, 0, 7, T.NumericAsDouble, 2, T.DateAsDouble, T.DateAsDouble),                /*  49 - */
        new(null, 30, 0, 7, T.DateAsDouble, 2, T.DateAsDouble, T.NumericAsDouble),                /*  50 - */
        new(null, 30, 0, 7, T.BinaryFloat, 2, T.BinaryFloat, T.BinaryFloat),                      /*  51 - */

        new("#", 50, 1, 6, T.Logical, 2, T.Character, T.Character),                               /*  52 - */
        new("<>", 50, 2, 6, T.Logical, 2, T.Character, T.Character),                              /*  53 - */
        new(null, 50, 0, 6, T.Logical, 2, T.NumericAsDouble, T.NumericAsDouble),                  /*  54 - */
        new(null, 50, 0, 6, T.Logical, 2, T.DateAsDouble, T.DateAsDouble),                        /*  55 - */
        new(null, 50, 0, 6, T.Logical, 2, T.Logical, T.Logical),                                  /*  56 - */
        new(null, 50, 0, 6, T.Logical, 2, T.Currency, T.Currency),                                /*  57 - */
        new(null, 50, 0, 6, T.Logical, 2, T.Timestamp, T.Timestamp),                              /*  58 - */
        new(null, 50, 0, 6, T.Logical, 2, T.TimestampWithMilliseconds, T.TimestampWithMilliseconds), /*  59 - */
        new(null, 50, 0, 6, T.Logical, 2, T.BinaryFloat, T.BinaryFloat),                          /*  60 - */

        new(">=", 60, 2, 6, T.Logical, 2, T.Character, T.Character),                              /*  61 - */
        new("=>", 60, 2, 6, T.Logical, 2, T.Character, T.Character),                              /*  62 - */
        new(null, 60, 0, 6, T.Logical, 2, T.NumericAsDouble, T.NumericAsDouble),                  /*  63 - */
        new(null, 60, 0, 6, T.Logical, 2, T.DateAsDouble, T.DateAsDouble),                        /*  64 - */
        new(null, 60, 0, 6, T.Logical, 2, T.Currency, T.Currency),                                /*  65 - */
        new(null, 60, 0, 6, T.Logical, 2, T.Timestamp, T.Timestamp),                              /*  66 - */
        new(null, 60, 0, 6, T.Logical, 2, T.TimestampWithMilliseconds, T.TimestampWithMilliseconds), /*  67 - */
        new(null, 60, 0, 6, T.Logical, 2, T.BinaryFloat, T.BinaryFloat),                          /*  68 - */

        new("<=", 70, 2, 6, T.Logical, 2, T.Character, T.Character),                              /*  69 - */
        new("=<", 70, 2, 6, T.Logical, 2, T.Character, T.Character),                              /*  70 - */
        new(null, 70, 0, 6, T.Logical, 2, T.NumericAsDouble, T.NumericAsDouble),                  /*  71 - */
        new(null, 70, 0, 6, T.Logical, 2, T.DateAsDouble, T.DateAsDouble),                        /*  72 - */
        new(null, 70, 0, 6, T.Logical, 2, T.Currency, T.Currency),                                /*  73 - */
        new(null, 70, 0, 6, T.Logical, 2, T.Timestamp, T.Timestamp),                              /*  74 - */
        new(null, 70, 0, 6, T.Logical, 2, T.TimestampWithMilliseconds, T.TimestampWithMilliseconds), /*  75 - */
        new(null, 70, 0, 6, T.Logical, 2, T.BinaryFloat, T.BinaryFloat),                          /*  76 - */

        new("=", 40, 1, 6, T.Logical, 2, T.Character, T.Character),                               /*  77 - */
        new(null, 40, 0, 6, T.Logical, 2, T.Logical, T.Logical),                                  /*  78 - */
        new(null, 40, 0, 6, T.Logical, 2, T.NumericAsDouble, T.NumericAsDouble),                  /*  79 - */
        new(null, 40, 0, 6, T.Logical, 2, T.DateAsDouble, T.DateAsDouble),                        /*  80 - */
        new(null, 40, 0, 6, T.Logical, 2, T.Currency, T.Currency),                                /*  81 - */
        new(null, 40, 0, 6, T.Logical, 2, T.Timestamp, T.Timestamp),                              /*  82 - */
        new(null, 40, 0, 6, T.Logical, 2, T.TimestampWithMilliseconds, T.TimestampWithMilliseconds), /*  83 - */
        new(null, 40, 0, 6, T.Logical, 2, T.BinaryFloat, T.BinaryFloat),                          /*  84 - */

        new(">", 80, 1, 6, T.Logical, 2, T.Character, T.Character),                               /*  85 - */
        new(null, 80, 0, 6, T.Logical, 2, T.NumericAsDouble, T.NumericAsDouble),                  /*  86 - */
        new(null, 80, 0, 6, T.Logical, 2, T.DateAsDouble, T.DateAsDouble),                        /*  87 - */
        new(null, 80, 0, 6, T.Logical, 2, T.Currency, T.Currency),                                /*  88 - */
        new(null, 80, 0, 6, T.Logical, 2, T.Timestamp, T.Timestamp),                              /*  89 - */
        new(null, 80, 0, 6, T.Logical, 2, T.TimestampWithMilliseconds, T.TimestampWithMilliseconds), /*  90 - */
        new(null, 80, 0, 6, T.Logical, 2, T.BinaryFloat, T.BinaryFloat),                          /*  91 - */

        new("<", 90, 1, 6, T.Logical, 2, T.Character, T.Character),                               /*  92 - */
        new(null, 90, 0, 6, T.Logical, 2, T.NumericAsDouble, T.NumericAsDouble),                  /*  93 - */
        new(null, 90, 0, 6, T.Logical, 2, T.DateAsDouble, T.DateAsDouble),                        /*  94 - */
        new(null, 90, 0, 6, T.Logical, 2, T.Currency, T.Currency),                                /*  95 - */
        new(null, 90, 0, 6, T.Logical, 2, T.Timestamp, T.Timestamp),                              /*  96 - */
        new(null, 90, 0, 6, T.Logical, 2, T.TimestampWithMilliseconds, T.TimestampWithMilliseconds), /*  97 - */
        new(null, 90, 0, 6, T.Logical, 2, T.BinaryFloat, T.BinaryFloat),                          /*  98 - */

        new("^", 100, 1, 9, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),          /*  99 - */
        new("**", 100, 2, 9, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),         /* 100 - */

        new("*", 102, 1, 8, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),          /* 101 - */
        new("*", 102, 1, 8, T.BinaryFloat, 2, T.BinaryFloat, T.BinaryFloat),                      /* 102 - */
        new("/", 105, 1, 8, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),          /* 103 - */
        new("/", 105, 1, 8, T.BinaryFloat, 2, T.BinaryFloat, T.BinaryFloat),                      /* 104 - */
        new("$", 110, 1, 6, T.Logical, 2, T.Character, T.Character),                              /* 105 - */

        // The named functions.
        new("CHR", 120, 3, 0, T.Character, 1, T.NumericAsDouble),        /* 106 - */
        new("DEL", 130, 3, 0, T.Character, 0),                           /* 107 - */
        new("STR", 140, 3, 0, T.Character, 1, T.NumericAsDouble),        /* 108 - */
        new("STR", 140, 1, 0, T.Character, 1, T.WideString),             /* 109 - */
        new("STRZERO", 145, 7, 0, T.Character, 1, T.NumericAsDouble),    /* 110 - */
        new("SUBSTR", 150, 6, 0, T.Character, 1, T.Character),           /* 111 - */
        new("TIME", 160, 4, 0, T.Character, 0),                          /* 112 - */
        new("UPPER", 170, 5, 0, T.Character, 1, T.Character),            /* 113 - */
        new("DTOS", 180, 4, 0, T.Character, 1, T.DateAsText),            /* 114 - */
        new(null, 180, 0, 0, T.Character, 1, T.DateAsDouble),            /* 115 - */
        new("DTOC", 200, 4, 0, T.Character, 1, T.DateAsText),            /* 116 - */
        new(null, 200, 4, 0, T.Character, 1, T.DateAsDouble),            /* 117 - */
        new("TTOC", 205, 4, 0, T.Character, 1, T.DateAsText),            /* 118 - */
        new(null, 205, 4, 0, T.Character, 1, T.Timestamp),               /* 119 - */
        new(null, 205, 4, 0, T.Character, 1, T.DateAsDouble),            /* 120 - */

        new("SPACE", 210, 5, 0, T.Character, 1, T.NumericAsDouble),      /* 121 - */

        new("TRIM", 220, 4, 0, T.Character, 1, T.Character),             /* 122 - */
        new("LTRIM", 230, 5, 0, T.Character, 1, T.Character),            /* 123 - */
        new("ALLTRIM", 235, 7, 0, T.Character, 1, T.Character),          /* 124 - */
        new("LEFT", 240, 4, 0, T.Character, 1, T.Character),             /* 125 - */
        new("RIGHT", 245, 5, 0, T.Character, 1, T.Character),            /* 126 - */

        new("PADL", 246, 4, 0, T.Character, 1, T.Character),             /* 127 - */
        new("PADR", 247, 4, 0, T.Character, 1, T.Character),             /* 128 - */

        new("IIF", 250, 3, 0, T.Character, 3, T.Logical, T.Character, T.Character),                     /* 129 - */
        new(null, 250, 0, 0, T.NumericAsDouble, 3, T.Logical, T.NumericAsDouble, T.NumericAsDouble),    /* 130 - */
        new(null, 250, 0, 0, T.DateAsDouble, 3, T.Logical, T.DateAsDouble, T.DateAsDouble),             /* 131 - */
        new(null, 250, 0, 0, T.Logical, 3, T.Logical, T.Logical, T.Logical),                            /* 132 - */

        new("DATETIME", 259, 8, 0, T.Timestamp, 3, T.NumericAsDouble, T.NumericAsDouble, T.NumericAsDouble),                 /* 133 - */
        new(null, 259, 8, 0, T.TimestampWithMilliseconds, 3, T.NumericAsDouble, T.NumericAsDouble, T.NumericAsDouble),       /* 134 - */
        new("STOD", 260, 4, 0, T.DateAsDouble, 1, T.Character),          /* 135 - */
        new("CTOD", 270, 4, 0, T.DateAsDouble, 1, T.Character),          /* 136 - */
        new("DATE", 280, 4, 0, T.DateAsDouble, 0),                       /* 137 - */
        new("DAY", 290, 3, 0, T.NumericAsDouble, 1, T.DateAsText),       /* 138 - */
        new(null, 290, 0, 0, T.NumericAsDouble, 1, T.DateAsDouble),      /* 139 - */
        new("MONTH", 310, 5, 0, T.NumericAsDouble, 1, T.DateAsText),     /* 140 - */
        new(null, 310, 0, 0, T.NumericAsDouble, 1, T.DateAsDouble),      /* 141 - */
        new("YEAR", 340, 4, 0, T.NumericAsDouble, 1, T.DateAsText),      /* 142 - */
        new(null, 340, 0, 0, T.NumericAsDouble, 1, T.DateAsDouble),      /* 143 - */
        new("DELETED", 350, 7, 0, T.Logical, 0),                         /* 144 - */
        new("RECCOUNT", 360, 8, 0, T.NumericAsDouble, 0),                /* 145 - */
        new("RECNO", 370, 5, 0, T.NumericAsDouble, 0),                   /* 146 - */
        new("VAL", 380, 3, 0, T.NumericAsDouble, 1, T.Character),        /* 147 - */
        new("L2BIN", 385, 5, 0, T.Character, 1, T.NumericAsDouble),      /* 148 - */

        // The report writer's three rows. This build compiles the report writer out, which is why
        // the total and page-number rows have lost their names but kept their shape.
        new(null, 390, 0, 0, T.None, 0),                                 /* 149 - E4CALC_FUNCTION */
        new(null, 400, 0, 0, T.NumericAsDouble, 0),                      /* 150 - E4TOTAL */
        new(null, 410, 6, 0, T.NumericAsDouble, 0),                      /* 151 - PAGENO */

        new("EMPTY", 415, 5, 0, T.Logical, 1, T.Character),              /* 152 - */
        new(null, 415, 5, 0, T.Logical, 1, T.NumericAsDouble),           /* 153 - */
        new(null, 415, 5, 0, T.Logical, 1, T.Timestamp),                 /* 154 - */
        new(null, 415, 5, 0, T.Logical, 1, T.DateAsDouble),              /* 155 - */
        new(null, 415, 5, 0, T.Logical, 1, T.Logical),                   /* 156 - */

        // Descending and ascending key conversion, one row per type, which is a fifth of the table
        // and almost none of the work: each is type dispatch onto a key transform already ported.
        new("DESCEND", 420, 7, 0, T.Character, 1, T.Character),          /* 157 - E4DESCEND_STR */
        new(null, 420, 7, 0, T.Character, 1, T.NumericAsText),           /* 158 - E4DESCEND_NUM */
        new(null, 420, 7, 0, T.Character, 1, T.DateAsDouble),            /* 159 - E4DESCEND_DATE_D */
        new(null, 420, 7, 0, T.Character, 1, T.Logical),                 /* 160 - E4DESCEND_LOG */
        new(null, 420, 7, 0, T.Character, 1, T.Timestamp),               /* 161 - */
        new(null, 420, 7, 0, T.Character, 1, T.Integer),                 /* 162 - */
        new(null, 420, 7, 0, T.Character, 1, T.Currency),                /* 163 - */
        new(null, 420, 7, 0, T.Character, 1, T.WideString),              /* 164 - */
        new(null, 420, 7, 0, T.Character, 1, T.WideStringWithLength),    /* 165 - */
        new(null, 420, 7, 0, T.Character, 1, T.NumericAsDouble),         /* 166 - */
        new(null, 420, 7, 0, T.Character, 1, T.Long),                    /* 167 - */
        new(null, 420, 7, 0, T.Character, 1, T.UnsignedInteger),         /* 168 - */
        new(null, 420, 7, 0, T.Character, 1, T.UnsignedShort),           /* 169 - */
        new(null, 420, 7, 0, T.Character, 1, T.Short),                   /* 170 - */
        new(null, 420, 7, 0, T.Character, 1, T.OleDbDate),               /* 171 - */
        new(null, 420, 7, 0, T.Character, 1, T.OleDbTime),               /* 172 - */
        new(null, 420, 7, 0, T.Character, 1, T.OleDbTimestamp),          /* 173 - */
        new("ASCEND", 430, 6, 0, T.Character, 1, T.Character),           /* 174 - E4ASCEND_STR */
        new(null, 430, 6, 0, T.Character, 1, T.WideString),              /* 175 - */
        new(null, 430, 6, 0, T.Character, 1, T.WideStringWithLength),    /* 176 - */
        new(null, 430, 6, 0, T.Character, 1, T.NumericAsText),           /* 177 - E4ASCEND_NUM */
        new(null, 430, 6, 0, T.Character, 1, T.DateAsText),              /* 178 - E4ASCEND_DATE */
        new(null, 430, 6, 0, T.Character, 1, T.DateAsDouble),            /* 179 - E4ASCEND_DATE_D */
        new(null, 430, 6, 0, T.Character, 1, T.Logical),                 /* 180 - E4ASCEND_LOG */
        new(null, 430, 6, 0, T.Character, 1, T.Integer),                 /* 181 - */
        new(null, 430, 6, 0, T.Character, 1, T.Timestamp),               /* 182 - */
        new(null, 430, 6, 0, T.Character, 1, T.Currency),                /* 183 - */
        new(null, 430, 6, 0, T.Character, 1, T.NumericAsDouble),         /* 184 - */
        new(null, 430, 6, 0, T.Character, 1, T.UnsignedInteger),         /* 185 - */
        new(null, 430, 6, 0, T.Character, 1, T.UnsignedShort),           /* 186 - */
        new(null, 430, 6, 0, T.Character, 1, T.Short),                   /* 187 - */
        new(null, 430, 6, 0, T.Character, 1, T.Long),                    /* 188 - */
        new(null, 430, 6, 0, T.Character, 1, T.OleDbDate),               /* 189 - */
        new(null, 430, 6, 0, T.Character, 1, T.OleDbTime),               /* 190 - */
        new(null, 430, 6, 0, T.Character, 1, T.OleDbTimestamp),          /* 191 - */

        new("KEYEQUAL", 440, 8, 0, T.Logical, 2, T.Character, T.Character), /* 192 - */
        new(null, Terminator, 0, 0, T.None, 0),                             /* 193 - */
    ];

    /// <summary>
    /// The functions Visual FoxPro has that the reference implementation does not.
    /// </summary>
    /// <value>
    /// A second band, indexed on from the terminator, and it exists for a reason worth stating: a
    /// CDX written by Visual FoxPro can carry one of these in a tag expression, and refusing it would
    /// be a hole in the compatibility target rather than a safeguard (ADR-43). They are [b]not[/b]
    /// part of the transcribed table — the reference has no row for any of them, so there is nothing
    /// to transcribe and nothing the transcription gate could compare. What gates them instead is
    /// Visual FoxPro itself, run offline in a VM (ADR-44): its own answers for each function are in
    /// [c]net/corpus/vfp/VFPFVAL.DBF[/c] and its own key lengths in [c]VFPFUNC.CDX[/c].
    ///
    /// Kept in a band of their own rather than mixed in, so that the transcribed 193 stay exactly
    /// 193 and every row index the reference names keeps its value. The codes start well past any the
    /// reference uses, so overload resolution's walk through a run of equal codes cannot wander out
    /// of one band into the other.
    ///
    /// [c]MIN[/c] and [c]MAX[/c] have two rows each because Visual FoxPro compares strings with them
    /// as well as numbers, and both forms are variadic. [c]AT[/c] takes its occurrence as a real
    /// third operand, which the parser supplies as a literal one when a caller leaves it out.
    /// [c]REPLICATE[/c] takes its count at parse time, because the result's length is the count times
    /// the operand's and a key cannot be sized without it.
    /// </value>
    private static readonly FunctionRow[] VisualFoxProTableRows =
    [
        new("ABS", 1000, 3, 0, T.NumericAsDouble, 1, T.NumericAsDouble),                    /* 193 */
        new("INT", 1010, 3, 0, T.NumericAsDouble, 1, T.NumericAsDouble),                    /* 194 */
        new("ROUND", 1020, 5, 0, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),   /* 195 */
        new("MOD", 1030, 3, 0, T.NumericAsDouble, 2, T.NumericAsDouble, T.NumericAsDouble),     /* 196 */
        new("MIN", 1040, 3, 0, T.NumericAsDouble, -1, T.NumericAsDouble),                   /* 197 */
        new(null, 1040, 0, 0, T.Character, -1, T.Character),                                /* 198 */
        new("MAX", 1050, 3, 0, T.NumericAsDouble, -1, T.NumericAsDouble),                   /* 199 */
        new(null, 1050, 0, 0, T.Character, -1, T.Character),                                /* 200 */
        new("SQRT", 1060, 4, 0, T.NumericAsDouble, 1, T.NumericAsDouble),                   /* 201 */
        new("SIGN", 1070, 4, 0, T.NumericAsDouble, 1, T.NumericAsDouble),                   /* 202 */
        new("LEN", 1080, 3, 0, T.NumericAsDouble, 1, T.Character),                          /* 203 */
        new("AT", 1090, 2, 0, T.NumericAsDouble, 3, T.Character, T.Character, T.NumericAsDouble), /* 204 */
        new("ASC", 1100, 3, 0, T.NumericAsDouble, 1, T.Character),                          /* 205 */
        new("LOWER", 1110, 5, 0, T.Character, 1, T.Character),                              /* 206 */
        new("REPLICATE", 1120, 9, 0, T.Character, 1, T.Character),                          /* 207 */
    ];

    /// <summary>
    /// Gets the transcribed rows, in the reference implementation's order.
    /// </summary>
    /// <value>
    /// Exactly the reference's 193, terminator included, and nothing else. The functions Visual
    /// FoxPro adds are a separate band, so that this stays comparable row for row against the
    /// library's own array.
    /// </value>
    public static IReadOnlyList<FunctionRow> Rows => TableRows;

    /// <summary>
    /// Gets the rows Visual FoxPro adds, which the reference implementation has none of.
    /// </summary>
    public static IReadOnlyList<FunctionRow> VisualFoxProRows => VisualFoxProTableRows;

    /// <summary>
    /// Gets how many rows there are across both bands.
    /// </summary>
    public static int Count => TableRows.Length + VisualFoxProTableRows.Length;

    /// <summary>
    /// Gets the index the Visual FoxPro band starts at.
    /// </summary>
    public static int FirstVisualFoxProRow => TableRows.Length;

    /// <summary>
    /// Gets one row from either band.
    /// </summary>
    /// <param name="row">The row's index.</param>
    /// <returns>The row.</returns>
    public static ref readonly FunctionRow Row(int row) => ref row < TableRows.Length
        ? ref TableRows[row]
        : ref VisualFoxProTableRows[row - TableRows.Length];

    /// <summary>
    /// Answers whether a row is one Visual FoxPro adds rather than one the reference has.
    /// </summary>
    /// <param name="row">The row's index.</param>
    /// <returns>True for a row in the Visual FoxPro band.</returns>
    /// <value>
    /// What the row-coverage check needs in order to report three counts rather than one: rows the
    /// reference's own dump gates, rows nothing can reach, and these — which have no reference row at
    /// all and are gated by Visual FoxPro's own answers instead.
    /// </value>
    public static bool IsVisualFoxPro(int row) => row >= TableRows.Length;

    /// <summary>
    /// Finds the row whose name the text begins with, within a band of the table.
    /// </summary>
    /// <param name="text">The text to match, starting at the position to match from.</param>
    /// <param name="first">The first row to examine.</param>
    /// <param name="last">The last row to examine.</param>
    /// <returns>The matching row's index, or minus one where nothing matched.</returns>
    /// <value>
    /// This is how an operator is recognized: the input is not tokenized into a symbol and then
    /// looked up, it is prefix-matched against the table in row order, and the first row that
    /// matches wins. Row order is therefore what decides that the two-character comparisons beat
    /// their one-character prefixes, and not a longest-match rule.
    /// </value>
    public static int FindByPrefix(ReadOnlySpan<char> text, int first, int last)
    {
        // The reference copies at most eight characters plus a terminator into a buffer and upper
        // cases them, so a candidate longer than eight is compared only on its first eight
        // (e4parse.c:1224-1233). Anything shorter simply fails to match a longer name.
        Span<char> candidate = stackalloc char[MaximumNameLength];
        int available = Math.Min(text.Length, MaximumNameLength);
        for (int i = 0; i < available; i++)
            candidate[i] = Upper(text[i]);

        for (int row = first; row <= last; row++)
        {
            FunctionRow entry = TableRows[row];
            if (entry.IsTerminator)
                return -1;

            if (entry.Name is null || entry.NameLength > available)
                continue;

            if (NameMatches(entry, candidate))
                return row;
        }

        return -1;
    }

    /// <summary>
    /// Finds the row a function name resolves to.
    /// </summary>
    /// <param name="name">The name, as the expression spelled it.</param>
    /// <returns>The matching row's index, or minus one where nothing matched.</returns>
    /// <value>
    /// A function name is matched whole, unlike an operator: the lookup requires the name's length to
    /// equal the row's, so a longer name matches nothing rather than matching a shorter row's prefix
    /// (e4parse.c:1258-1263). That is what makes a name of more than eight characters an unknown
    /// function, since no row has a name that long. The scan starts at the first named row and runs to
    /// the terminator (e4parse.c:1951).
    /// </value>
    public static int FindByName(ReadOnlySpan<char> name)
    {
        Span<char> candidate = stackalloc char[LongestName];
        int available = Math.Min(name.Length, LongestName);
        for (int i = 0; i < available; i++)
            candidate[i] = Upper(name[i]);

        for (int row = FirstFunction; row < TableRows.Length; row++)
        {
            FunctionRow entry = TableRows[row];
            if (entry.IsTerminator)
                break;

            if (entry.Name is null || entry.NameLength != name.Length)
                continue;

            if (NameMatches(entry, candidate))
                return row;
        }

        // The reference's own scan stops at the terminator; this carries on into the band Visual
        // FoxPro adds, which is the whole of what ADR-43 decided. Nothing else reaches these rows:
        // an operator lookup never leaves rows 36 to 106, so they cannot be matched by accident.
        for (int row = 0; row < VisualFoxProTableRows.Length; row++)
        {
            FunctionRow entry = VisualFoxProTableRows[row];

            if (entry.Name is null || entry.NameLength != name.Length)
                continue;

            if (NameMatches(entry, candidate))
                return TableRows.Length + row;
        }

        return -1;
    }

    /// <summary>
    /// Answers whether an upper-cased candidate begins with a row's name.
    /// </summary>
    /// <value>
    /// The comparison runs over the length the row records rather than over the name's own characters.
    /// The two are the same everywhere but one row, the wide-string overload of [c]STR[/c], whose
    /// recorded length is one; the reference compares that row on its first character alone, and this
    /// does too, because narrowing it would be this port refusing a name the reference accepts.
    /// </value>
    private static bool NameMatches(FunctionRow entry, ReadOnlySpan<char> candidate) =>
        candidate[..entry.NameLength].SequenceEqual(entry.Name!.AsSpan(0, entry.NameLength));

    /// <summary>
    /// Upper cases one character the way the reference implementation does when matching a name.
    /// </summary>
    private static char Upper(char ch) => ch is >= 'a' and <= 'z' ? (char)(ch - 32) : ch;
}
