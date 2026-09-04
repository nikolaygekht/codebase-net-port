/* ==========================================================================
 * case-cdxexpr.cpp — CDXEXPR.DBF + CDXEXPR.CDX + CDXEXPR.expr.dump.txt
 *
 * The expression case. Everything before it indexes a bare field; this one
 * indexes what applications actually index by, and records what the reference
 * evaluator answers so the port can be compared value for value.
 *
 * TWO AXES, AND THIS FILE SERVES BOTH
 * -----------------------------------
 * Axis A is what a function computes, and it is tested over CONSTANTS: the
 * expression is the whole case, the edge value sits in the literal, and a
 * failure cannot be a field-decode bug in disguise. Fields appear in the value
 * lists only for what no literal can say -- r4num, r4date, r4int, r4currency,
 * a memo, and null -- because no named function returns those four types
 * either (e4functi.c:39-49); they enter an expression through a field loader
 * or not at all.
 *
 * Axis B is what a value becomes on disk, and that is the tag list: one tag per
 * *result type*, so every key route of the key-conversion rules is exercised,
 * plus the function shapes whose key rules differ (hasTrim, the two concat
 * rows, pre-collated DESCEND, a picture-length DTOC).
 *
 * WHAT THE RECORDS ARE FOR
 * ------------------------
 * With the kernels tested over constants, the records are not an edge-case
 * space -- they supply typed inputs and null states, and stop. Sixteen rows.
 *
 * NOTE ON 'H': d4create forces a binary float to 4 bytes regardless of the
 * requested length (d4create.c:1620-1622). It is a CodeBase extension VFP never
 * writes, and this is the corpus's only case with one.
 *
 * The data below belongs to this case alone -- see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "dump-expr.h"
#include "dump-query.h"
#include "cases.h"

#define ROWS 16
#define DELETED_REC 4L   /* the one record marked deleted, for DEL()/DELETED() */

/* K_TEXT, C(20). Trailing blanks matter to TRIM and to both concat rows, so
 * most values are short of the width; row 4 fills it exactly, rows 5 and 6 are
 * empty and all-blank, and rows 0/1 share a prefix so a prefix `=` has
 * something to be right or wrong about. */
static const char *const TEXTS[ROWS] =
{
   "SMITH",              /*  0 */
   "SMITHSON",           /*  1 — row 0 is a prefix of it */
   "john smith",         /*  2 — lower case, for UPPER */
   "  leading blanks",   /*  3 — for LTRIM/ALLTRIM */
   "ABCDEFGHIJKLMNOPQRST",/* 4 — exactly the width, so TRIM removes nothing */
   "",                   /*  5 — empty */
   "    ",               /*  6 — all blank, which EMPTY and TRIM see differently */
   "123.45",             /*  7 — parses as a number, for VAL */
   "not a number",       /*  8 — does not, for VAL */
   "01/15/2000",         /*  9 — parses as a date, for CTOD */
   "99/99/9999",         /* 10 — does not */
   "ZEBRA",              /* 11 */
   "AARDVARK",           /* 12 */
   "MIDDLE",             /* 13 */
   "MIDDLE",             /* 14 — equal to row 13 */
   "\xC9\xE8"            /* 15 — bytes above 0x7F, which UPPER must not mangle */
};

/* K_CITY, C(15) — the concat partner. Short values, so `+` keeps the interior
 * blanks and `-` moves them to the end. */
static const char *const CITIES[ROWS] =
{
   "EDMONTON", "CALGARY", "TORONTO", "", "MONTREAL", "OTTAWA", "  ", "HALIFAX",
   "REGINA", "VICTORIA", "WINNIPEG", "QUEBEC", "LONDON", "PARIS", "PARIS", "BERLIN"
};

/* K_NUM, N(12,3) — the r4num half of the pair, which is the stored text and
 * has its own key route. Zero, both signs, and the width's limits. */
static double numFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -9999999.999;
   if ( i == 2 ) return 99999999.999;
   if ( i == 3 ) return -0.001;
   if ( i == 4 ) return 2.5;              /* midpoints, so STR over a field */
   if ( i == 5 ) return -2.5;             /* sees them too, not only STR over */
   if ( i == 6 ) return 0.125;            /* a literal */
   if ( i == 7 ) return 0.135;
   return i * 111.111 - 500.0;
}

/* K_INT, I(4) — r4int, reachable only as a bare field. */
static long intFor( int i )
{
   if ( i == 0 ) return 0L;
   if ( i == 1 ) return -2147483647L - 1L;
   if ( i == 2 ) return 2147483647L;
   return ( i % 3 == 0 ) ? -( (long)i * 7L ) : ( (long)i * 7L );
}

/* K_DBL, B(8,6) — a true double, so -0.0 is expressible. */
static double dblFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -0.0;
   if ( i == 2 ) return 3.141592653589793;
   if ( i == 3 ) return -1e300;
   if ( i == 4 ) return 1e-300;
   return i * 0.5 - 4.0;
}

/* K_FLT, H(4) — a 32-bit binary float, and the only one in the corpus.
 *
 * There is no f4assign* that reaches r4floatBin: the type appears in exactly
 * one assignment path in the whole library, f4assignField's same-type memcpy
 * (f4ass_f.c:99), and f4assignDouble formats decimal text into the four bytes
 * instead -- which is what the first run of this case produced ("0.00" read
 * back as a float). The four bytes are therefore written the way
 * f4assignField writes them, through f4assignPtr. That the reference cannot
 * assign one except by copying another field of the same type is itself worth
 * knowing about the H extension. */
static float fltFor( int i )
{
   if ( i == 0 ) return 0.0f;
   if ( i == 1 ) return -0.0f;
   if ( i == 2 ) return 1.0f;
   if ( i == 3 ) return -1.0f;
   if ( i == 4 ) return 3.4028235e38f;    /* FLT_MAX */
   if ( i == 5 ) return 1.1754944e-38f;   /* FLT_MIN normal */
   return (float)( i * 0.25 - 2.0 );
}

/* K_CUR, Y(8,4) — r4currency, reachable only as a bare field. Four implied
 * decimals, so the last row sits on the scaling boundary. */
static double curFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -1.0;
   if ( i == 2 ) return 0.0001;
   if ( i == 3 ) return -0.0001;
   return i * 100.25;
}

/* K_DATE, D(8) — r4date. Row 2 is blank, which DTOS, DTOC and the date
 * arithmetic each treat in their own way. */
static const char *const DATES[ROWS] =
{
   "20000101", "20000229", "        ", "19000101", "20261231", "20240229", "19701231", "20200101",
   "19851115", "20010911", "20100302", "20120229", "20150818", "20180505", "20220314", "19991231"
};

/* K_TS, T(8) — r4dateTime, CCYYMMDDHH:MM:SS. Row 2 stays blank. */
static const char *const STAMPS[ROWS] =
{
   "2000010100:00:00", "2000022912:30:45", "", "1900010100:00:01",
   "2026123123:59:59", "2024022906:15:00", "1970123118:45:30", "2020010100:00:00",
   "1985111509:10:11", "2001091108:46:00", "2010030201:02:03", "2012022913:14:15",
   "2015081820:21:22", "2018050503:04:05", "2022031415:09:26", "1999123123:59:58"
};

/* K_LOG, L(1). */
static int logFor( int i )
{
   return ( i % 3 ) != 0;
}

/* K_MEMO, M(4). Row 0 is empty, row 1 is short, row 2 runs past the 240-byte
 * simple-tag cap so a memo operand's limit has something to refuse. */
static int memoLenFor( int i )
{
   if ( i == 0 ) return 0;
   if ( i == 1 ) return 12;
   if ( i == 2 ) return 300;
   return 20 + i * 3;
}

/* K_NNUM, N(12,3) nullable — the null state, which no literal expresses.
 * Rows 0, 5 and 11 are null. The non-null value is halved so it stays inside
 * N(12,3): a field that overflows its width writes asterisks, which is a real
 * behaviour but a different one, and STR(123456,3,0) covers it deliberately. */
static int isNullRow( int i )
{
   return i == 0 || i == 5 || i == 11;
}

/* ------------------------------------------------------- expression lists */

/* Axis A. Constants only: no field reference, so no record dimension and no
 * dependence on this table's data at all. */
static const EXPRCASE CONSTANTS[] =
{
   { "UPPER(\"john smith\")",        "the commonest kernel" },
   { "UPPER(\"\")",                  "empty" },
   { "TRIM(\"abc   \")",             "trailing blanks removed" },
   { "TRIM(\"   \")",                "all blank" },
   { "LTRIM(\"   abc\")",            "leading" },
   { "ALLTRIM(\"  abc  \")",         "both ends" },
   { "SUBSTR(\"ABCDEF\",2,3)",       "interior" },
   { "SUBSTR(\"ABCDEF\",5,10)",      "past the end" },
   { "LEFT(\"ABCDEF\",3)",           0 },
   { "LEFT(\"ABC\",10)",             "longer than the source" },
   { "RIGHT(\"ABCDEF\",3)",          0 },
   { "PADL(\"X\",5)",                0 },
   { "PADR(\"X\",5)",                0 },
   { "SPACE(5)",                     0 },
   { "CHR(65)",                      0 },
   { "CHR(0)",                       "a NUL in a string result" },
   { "STR(2.5,10,0)",                "midpoint, half away or half even" },
   { "STR(-2.5,10,0)",               "midpoint, negative" },
   { "STR(0.125,10,2)",              "midpoint at two decimals" },
   { "STR(0.135,10,2)",              "midpoint the binary form misses" },
   { "STR(12345.6789,12,3)",         "ordinary" },
   { "STR(1,3,0)",                   "narrow" },
   { "STR(123456,3,0)",              "too narrow to fit" },
   { "STRZERO(42,8)",                0 },
   { "STRZERO(-42,8)",               "sign against the zero fill" },
   { "VAL(\"123.45\")",              0 },
   { "VAL(\"not a number\")",        "unparseable" },
   { "VAL(\"\")",                    "empty" },
   { "CTOD(\"01/15/2000\")",         "returns r4dateDoub, not r4date" },
   { "CTOD(\"99/99/9999\")",         "invalid date" },
   /* The picture is "MM/DD/YY" -- a compile-time constant (c4code.c:1126), not
    * a locale -- so CTOD reads a two-digit year. These find the pivot. */
   { "DTOS(CTOD(\"01/15/00\"))",     "two-digit year 00" },
   { "DTOS(CTOD(\"01/15/29\"))",     "29" },
   { "DTOS(CTOD(\"01/15/30\"))",     "30" },
   { "DTOS(CTOD(\"01/15/49\"))",     "49" },
   { "DTOS(CTOD(\"01/15/50\"))",     "50" },
   { "DTOS(CTOD(\"01/15/69\"))",     "69" },
   { "DTOS(CTOD(\"01/15/70\"))",     "70" },
   { "DTOS(CTOD(\"01/15/99\"))",     "99" },
   { "DTOS(STOD(\"19700115\"))",     "STOD takes CCYYMMDD, so no pivot at all" },
   { "STOD(\"20000115\")",           0 },
   { "DTOS(CTOD(\"01/15/2000\"))",   "sortable text" },
   { "DTOC(CTOD(\"01/15/2000\"))",   "picture length" },
   { "DAY(CTOD(\"01/15/2000\"))",    0 },
   { "MONTH(CTOD(\"01/15/2000\"))",  0 },
   { "YEAR(CTOD(\"01/15/2000\"))",   0 },
   { "DATETIME(2026,1,1)",           "the only constant route to r4dateTime" },
   { "L2BIN(1)",                     "long to its 4 binary bytes" },
   { "L2BIN(0)",                     0 },
   { "L2BIN(-1)",                    0 },
   { "L2BIN(65536)",                 "byte order of the result" },
   { "KEYEQUAL(\"ABC\",\"ABC\")",       "added AS Jan 6/05; C,C -> L" },
   { "KEYEQUAL(\"ABC\",\"ABD\")",       0 },
   { "KEYEQUAL(\"ABC\",\"AB\")",        "against the prefix = rule" },
   { "EMPTY(\"\")",                  0 },
   { "EMPTY(\"   \")",               "blank is not empty, or is it" },
   { "EMPTY(\"x\")",                 0 },
   { "\"AAAA\" = \"AAA\"",           "prefix = : CodeBase says true both ways" },
   { "\"AAA\" = \"AAAA\"",           "the direction VFP also makes true" },
   { "\"AAA\" = \"AAA\"",            0 },
   { "\"AAA\" <> \"AAAA\"",          0 },
   { "\"AB\" $ \"XABY\"",            "contained" },
   { "\"Z\" $ \"XABY\"",             "not contained" },
   { "\"\" $ \"XABY\"",              "empty needle" },
   /* The comparison block: rows 51-97 are six operators over eight operand
    * types, and only the first row of each group carries a name -- the rest
    * are the continuations functionI++ walks to. Every spelling is here
    * because a wrong lookup length picks a different group. */
   { "\"ABC\" > \"ABD\"",             "r4str, row group 84" },
   { "\"ABD\" > \"ABC\"",             0 },
   { "\"ABC\" < \"ABD\"",             "row group 91" },
   { "\"ABC\" >= \"ABC\"",            "row group 60" },
   { "\"ABC\" => \"ABC\"",            "the second spelling of >=" },
   { "\"ABC\" <= \"ABC\"",            "row group 68" },
   { "\"ABC\" =< \"ABC\"",            "the second spelling of <=" },
   { "\"ABC\" # \"ABD\"",             "the second spelling of <>" },
   { "\"AAAA\" > \"AAA\"",            "ordering against the prefix = rule" },
   { "\"AAA\" < \"AAAA\"",            0 },
   { "2 > 1",                        "r4numDoub comparison" },
   { "1 > 2",                        0 },
   { "1 < 2",                        0 },
   { "1 >= 1",                       0 },
   { "1 => 1",                       0 },
   { "1 <= 1",                       0 },
   { "1 =< 1",                       0 },
   { "1 <> 2",                       0 },
   { "1 # 2",                        0 },
   { "1 = 1",                        0 },
   { ".T. = .T.",                    "r4log has only = and <>/#" },
   { ".T. <> .F.",                   0 },
   { ".T. # .F.",                    0 },
   { "CTOD(\"01/15/00\") > CTOD(\"01/14/00\")", "r4dateDoub comparison" },
   { "CTOD(\"01/14/00\") < CTOD(\"01/15/00\")", 0 },
   { "CTOD(\"01/15/00\") = CTOD(\"01/15/00\")", 0 },
   { "CTOD(\"01/15/00\") >= CTOD(\"01/15/00\")", 0 },
   { "DATETIME(2026,1,1) > DATETIME(2020,1,1)", "r4dateTime comparison" },
   { "DATETIME(2020,1,1) = DATETIME(2020,1,1)", 0 },

   { "1/0",                          "division by zero yields 0.0, not an error" },
   { "1*3",                          0 },
   { "7/2",                          0 },
   { "2**10",                        0 },
   { "2^10",                         0 },
   { "1+2",                          0 },
   { "5-3",                          0 },
   { "-1",                           "a signed numeric literal, not unary minus" },
   { ".T. .AND. .F.",                0 },
   { ".T. .OR. .F.",                 0 },
   { ".NOT. .T.",                    0 },
   { ".NOT. 1 = 2",                  "NOT is priority 5, comparison 6" },
   { "IIF(.T.,\"YES\",\"NO\")",      "result length is the longer branch" },
   { "IIF(.F.,\"YES\",\"LONGER\")",  0 },
   { "\"AB\"+\"CD\"",                "concat" },
   { "\"AB  \"+\"CD\"",              "interior blanks kept" },
   { "\"AB  \"-\"CD\"",              "interior blanks moved to the end" },
   { "TRIM(\"AB  \")+\"CD\"",         "row 41: a trim anywhere makes the next + move blanks" },

   /* Second run, 2026-09-03. The resolution gate found 46 table rows that are
    * reachable and that nothing above reached, so the row a wrong overload
    * would have been compared against did not exist. These close that, and
    * every one of them is here to pin one row index. */

   /* The long spellings of the logical constants and of negation: three rows
    * the short forms never reach, because lookup matches on name length. */
   { ".TRUE. .AND. .T.",             "row 30, the long spelling" },
   { ".FALSE. .OR. .T.",             "row 32" },
   { "! .T.",                        "row 35, negation spelled with a bang" },

   /* A run of one variadic operator collapses into a single node with an
    * operand per term. Nothing above has a run longer than two, so numParms
    * was never anything but 2 in this dump. */
   { ".T. .AND. .F. .AND. .T.",      "three terms in one .AND. node" },
   { ".T. .OR. .F. .OR. .T. .OR. .F.", "four terms in one .OR. node" },
   { ".T. .AND. .F. .OR. .T. .OR. .F.", ".AND. binds tighter, so the .OR. run is three" },

   /* Date arithmetic in the orders the d+n case above does not reach. */
   { "1 + CTOD(\"01/15/00\")",       "row 43, numeric plus date" },
   { "CTOD(\"01/15/00\") - CTOD(\"01/14/00\")", "row 48, date minus date is a count of days" },
   { "CTOD(\"01/15/00\") - 1",       "row 49, date minus numeric is a date" },

   /* The comparison rows for date-as-double and for timestamp that the four
    * spellings above skip. */
   { "CTOD(\"01/15/00\") <> CTOD(\"01/14/00\")", "row 54" },
   { "CTOD(\"01/15/00\") <= CTOD(\"01/15/00\")", "row 71" },
   { "DATETIME(2026,1,1) <> DATETIME(2020,1,1)", "row 57" },
   { "DATETIME(2026,1,1) >= DATETIME(2020,1,1)", "row 65" },
   { "DATETIME(2020,1,1) <= DATETIME(2026,1,1)", "row 73" },
   { "DATETIME(2020,1,1) < DATETIME(2026,1,1)",  "row 95" },

   /* The conditional's three non-string overloads, and the timestamp and date
    * forms of the functions whose first row takes a stored date. */
   { "IIF(.T.,1,2)",                 "row 129, numeric branches" },
   { "IIF(.T.,CTOD(\"01/15/00\"),CTOD(\"01/14/00\"))", "row 130, date branches" },
   { "IIF(.T.,.T.,.F.)",             "row 131, logical branches" },
   { "TTOC(CTOD(\"01/15/00\"))",     "row 119, TTOC over a date-as-double" },
   { "EMPTY(CTOD(\"01/15/00\"))",    "row 154" },
   { "ASCEND(CTOD(\"01/15/00\"))",   "row 178" }
};

/* Axis A's remainder: the types no literal reaches, plus the field-side shapes
 * the tags will key. Every record is evaluated. */
static const EXPRCASE FIELDEXPRS[] =
{
   { "K_NUM",                        "r4num -- stored text, no function returns it" },
   { "K_NUM + 0",                    "promoted to r4numDoub, the other key route" },
   { "K_DATE",                       "r4date -- no function returns it" },
   { "K_DATE + 1",                   "date arithmetic" },
   { "K_INT",                        "r4int -- no function returns it" },
   { "K_CUR",                        "r4currency -- no function returns it" },
   { "K_TS",                         "r4dateTime from a field" },
   { "K_DBL",                        "r4numDoub from a binary double" },
   { "K_FLT",                        "r4floatBin, the corpus's only H field" },
   { "K_FLT * 1",                    "the only function route to r4floatBin" },
   { "K_LOG",                        0 },
   { ".NOT. K_LOG",                  0 },
   { "K_MEMO",                       "a memo operand" },
   { "EMPTY(K_MEMO)",                0 },
   { "K_NNUM",                       "null in a nullable field" },
   { "EMPTY(K_NNUM)",                "null through a kernel" },
   { "K_NNUM + 1",                   "null propagation through arithmetic" },
   { "K_NNUM = 0",                   "a comparison with one null operand" },
   { "UPPER(K_TEXT)",                0 },
   { "TRIM(K_TEXT)",                 "hasTrim" },
   { "K_TEXT + K_CITY",              0 },
   { "K_TEXT - K_CITY",              0 },
   { "SUBSTR(K_TEXT,1,5)",           0 },
   { "STR(K_NUM,12,3)",              0 },
   { "STR(K_INT)",                   0 },
   { "DTOS(K_DATE)",                 0 },
   { "DTOC(K_DATE)",                 0 },
   { "TTOC(K_TS)",                   0 },
   { "VAL(K_TEXT)",                  0 },
   { "CTOD(K_TEXT)",                 0 },
   { "DAY(K_DATE)",                  0 },
   { "YEAR(K_DATE)",                 0 },
   { "DESCEND(K_TEXT)",              "pre-collated" },
   { "ASCEND(K_TEXT)",               0 },
   { "DELETED()",                    0 },
   { "DEL()",                        "the deletion flag as text" },
   { "RECNO()",                      "deterministic per record" },
   { "RECCOUNT()",                   0 },
   { "K_INT > 0 .AND. \"A\" $ K_TEXT", "the filter shape" },
   { "K_CUR > K_CUR2",               "two currency operands -- Y,Y row, or promoted to n,n?" },
   { "K_CUR = K_CUR2",               0 },
   { "K_CUR > 0",                    "one currency operand against a numeric literal" },
   { "K_TS > DATETIME(2020,1,1)",    "r4dateTime against a function of the same type" },
   { "K_NUM > 0",                    "r4num promoted to r4numDoub" },
   { "K_INT > 0",                    "r4int promoted" },
   { "K_DATE > CTOD(\"01/01/00\")",   "r4date promoted to r4dateDoub" },
   { "K_FLT > 0",                    "r4floatBin against a numeric literal" },
   { "K_TEXT > \"M\"",               "r4str, over a field" },
   { "K_TEXT <= \"M\"",              0 },

   /* Second run, 2026-09-03 -- the rest of the rows the resolution gate found
    * unreachable from this dump. See the note in CONSTANTS above. */

   /* The binary float's own rows. Only H*n was here, so the seven comparisons,
    * both other arithmetic rows and the division row were never chosen. The
    * right operand is a literal throughout, which promotes to a float and
    * keeps the arithmetic away from the field's extremes. */
   { "K_FLT + 1",                    "row 45" },
   { "K_FLT - 1",                    "row 50" },
   { "K_FLT / 2",                    "row 103" },
   { "K_FLT <> 1",                   "row 59" },
   { "K_FLT >= 1",                   "row 67" },
   { "K_FLT <= 1",                   "row 75" },
   { "K_FLT = 1",                    "row 83" },
   { "K_FLT < 1",                    "row 97" },
   { "K_FLT * K_INT",                "row 25: the only promotion of an integer to a float" },

   /* The stored-date overload of two functions, which a date-as-double
    * argument walks straight past. */
   { "TTOC(K_DATE)",                 "row 117" },
   { "MONTH(K_DATE)",                "row 139" },

   { "EMPTY(K_TS)",                  "row 153" },
   { "EMPTY(K_LOG)",                 "row 155" },
   { "K_LOG .AND. K_LOG .AND. K_LOG", "a three-term run over fields" },

   /* Key conversion, one row per type. This is a fifth of the table and only
    * its two character rows were reached. */
   { "DESCEND(K_NUM)",               "row 157" },
   { "DESCEND(K_DATE)",              "row 158: promoted, since no descend row takes a stored date" },
   { "DESCEND(K_LOG)",               "row 159" },
   { "DESCEND(K_TS)",                "row 160" },
   { "DESCEND(K_INT)",               "row 161" },
   { "DESCEND(K_CUR)",               "row 162" },
   { "DESCEND(K_DBL)",               "row 165" },
   { "ASCEND(K_NUM)",                "row 176" },
   { "ASCEND(K_DATE)",               "row 177: ascend does have a stored-date row" },
   { "ASCEND(K_LOG)",                "row 179" },
   { "ASCEND(K_INT)",                "row 180" },
   { "ASCEND(K_TS)",                 "row 181" },
   { "ASCEND(K_CUR)",                "row 182" },
   { "ASCEND(K_DBL)",                "row 183" }
};

/* Recorded for SHAPE only -- these read the system clock, so a recorded value
 * would change on every run and break byte-identical regeneration. Their type,
 * length, key length and node array are stable, which is what the resolution
 * gate needs; the values are checked C#-side against the clock. */
static const EXPRCASE CLOCKEXPRS[] =
{
   { "DATE()",                       "r4dateDoub from the clock" },
   { "TIME()",                       "r4str from the clock" },
   { "DATE() + 1",                   "arithmetic over a clock value" },
   { "DTOS(DATE())",                 "and through a kernel" }
};

/* The refusal half. "We refuse what CodeBase refuses" is otherwise an
 * assertion nobody checks -- and a parser that accepts a superset writes tags
 * neither CodeBase nor VFP can read. Anything recorded as ACCEPTED here the
 * port must also accept. */
static const EXPRCASE REJECTIONS[] =
{
   { "-K_TEXT",                      "no unary minus outside a numeric literal" },
   { "{01/01/2000}",                 "no brace date literals" },
   { "'unterminated",                "e4unterminated" },
   { "TOOLONGFN(\"X\")",             "function names are 8 characters" },
   { "SUBSTR(TRIM(K_TEXT),1,2)",     "a trim result may not feed SUBSTR" },
   { "STR(K_NUM,K_INT)",             "width must be a constant" },
   { "LEFT(\"ABC\",K_INT)",          "2nd parameter must be a constant" },
   { "SPACE(K_INT)",                 "1st parameter must be a constant" },
   { "NOSUCHFIELD",                  "unknown name" },
   { "UPPER(",                       "unbalanced" },
   { "K_TEXT +",                     "dangling operator" },
   { "UPPER(K_TEXT, 2)",             "wrong parameter count" },
   { "UPPER(K_INT)",                 "wrong parameter type" },
   { "1.AND.X",                      "a dot terminates a number" },
   { "K_TEXT.K_CITY",                "S4FOX alias dot, against the dotted operator" },
   { "OTHER->K_TEXT",                "an alias that is not the indexed table" },
   { ".T. > .F.",                    "no > row accepts r4log -- rows 84-90 skip it" },
   { ".T. >= .F.",                   0 }
};

/* Names Visual FoxPro has and CodeBase does not. These are NOT rejections:
 * this port supports them (DESIGN.md Decision 11), because a CDX written by
 * VFP can carry one in a tag expression and refusing it would be a hole in
 * the compatibility target rather than a safeguard. The section records what
 * the reference does, so the divergence is documented and not merely claimed.
 *
 * Every one is deterministic, and the arithmetic ones return r4numDoub -- so
 * they add no new key route at all: key length and key bytes follow the result
 * TYPE (specs/EXPRESSIONS.md 9.1, 9.3), and the r4numDoub route is already
 * gated by T_VAL. The string-returning ones are the ones to watch, because
 * their parse-time result LENGTH is per-function. */
static const EXPRCASE VFPONLY[] =
{
   { "ABS(-1)",                      "r4numDoub -- no new key route" },
   { "INT(1.5)",                     "r4numDoub" },
   { "ROUND(1.5,0)",                 "r4numDoub" },
   { "MOD(5,3)",                     "r4numDoub" },
   { "MIN(1,2)",                     "r4numDoub" },
   { "MAX(1,2)",                     "r4numDoub" },
   { "SQRT(4)",                      "r4numDoub" },
   { "SIGN(-3)",                     "r4numDoub" },
   { "LEN(\"AB\")",                  "r4numDoub" },
   { "AT(\"B\",\"AB\")",             "r4numDoub" },
   { "ASC(\"A\")",                   "r4numDoub" },
   { "LOWER(\"AB\")",                "r4str -- result length is the operand's" },
   { "REPLICATE(\"A\",3)",           "r4str -- result length is n x operand, and 9 chars besides" }
};

/* ------------------------------------------------------ query filters */

/* Axis C, the query optimizer (claude/dev/011-query-optimizer). These are not
 * expressions to evaluate but FILTERS to optimize, and what is recorded is what
 * the reference's optimizer decided about each -- see dump-query.h.
 *
 * The list is sized by section 8 of specs/QUERY-OPTIMIZER.md: every row of its
 * optimizable/not table wants a case, because "not optimizable" is a decision
 * the port has to reproduce and not merely a gap. `tagName` selects a tag before
 * relate4top, which changes the ORDER records come out in.
 *
 * Nothing here reads the clock. CTOD and the date pictures take their century
 * from the system clock (D4DATE.C:580-601), so a filter over one would record a
 * record set that changes with the year -- which is exactly what a corpus file
 * must not do. Dates are compared through DTOS as text. */
static const QUERYCASE QUERIES[] =
{
   /* -- the six operators, tag on the left, over a plain numeric tag ------- */
   { "K_NUM > 0",                 "T_NUM",   "gt" },
   { "K_NUM >= 0",                "T_NUM",   "ge" },
   { "K_NUM < 0",                 "T_NUM",   "lt" },
   { "K_NUM <= 0",                "T_NUM",   "le" },
   { "K_NUM = 0",                 "T_NUM",   "eq" },
   { "K_NUM <> 0",                "T_NUM",   "ne -- an exclusion list of one" },

   /* -- the constant on the left: each must reflect to its mirror slot ----- */
   { "0 < K_NUM",                 "T_NUM",   "reflected -- same set as K_NUM > 0" },
   { "0 <= K_NUM",                "T_NUM",   "reflected -- same set as K_NUM >= 0" },
   { "0 > K_NUM",                 "T_NUM",   "reflected -- same set as K_NUM < 0" },
   { "0 >= K_NUM",                "T_NUM",   "reflected -- same set as K_NUM <= 0" },

   /* -- THE case: a reflected comparison nested inside an .AND. ------------
    *
    * m4map.c:216/243/251/278 write `if ( tagPos = 1 && constPos == 0 )`, an
    * assignment, so the condition actually evaluated is `constPos == 0` alone.
    * For a comparison whose operands are single nodes and which sits at the
    * start of the postfix array the two readings agree; nested behind another
    * term they need not. These two filters denote the same records, so the
    * reference disagreeing with ITSELF between its optimized and bitmapDisable
    * runs is the defect witnessing itself (ADR-53). */
   { "K_INT > 0 .AND. 4 > K_NUM", "T_NUM",   "reflected INSIDE an .AND. -- where the tagPos=1 defect can bite" },
   { "K_INT > 0 .AND. K_NUM < 4", "T_NUM",   "the same filter written tag-first -- must denote the same records" },
   { "4 > K_NUM",                 "T_NUM",   "the same comparison un-nested -- both readings agree here" },

   /* -- an expression tag is an ordinary tag ------------------------------- */
   { "UPPER(K_TEXT) = \"SMITH\"",       "T_UPPER",  "a whole subexpression matched to a tag" },
   { "UPPER(K_TEXT) > \"S\"",           "T_UPPER",  "a SHORT constant against keyLen 20 -- the partial-string widening, gt becomes ge" },
   { "UPPER(K_TEXT) >= \"S\"",          "T_UPPER",  "the same bound already inclusive -- no widening to do" },
   { "\"S\" < UPPER(K_TEXT)",           "T_UPPER",  "the widening's other path: constant-left < also produces gt" },
   { "SUBSTR(K_TEXT,1,5) = \"SMITH\"",  "T_SUBSTR", "a folded constant parameter, so two nodes and not four" },
   { "K_TEXT+K_CITY > \"S\"",           "T_CONCAT", "a two-field subexpression matched whole" },
   { "DTOS(K_DATE) > \"20100101\"",     "T_DTOS",   "a date compared as text, so the clock is never read" },

   /* -- runs of three, which no other dump in the corpus holds --------------
    * .AND. and .OR. are variadic in the compiled form, so a run of three is
    * ONE node with three parameters. Step 010 found this gated by C#-authored
    * tests alone. */
   { "K_NUM > -100 .AND. K_NUM < 100 .AND. K_TEXT > \"A\"", "T_NUM", "a three-term .AND. -- the variadic collapse" },
   { "K_NUM = 0 .OR. K_NUM = 2.5 .OR. K_NUM = 0.125",       "T_NUM", "a three-term .OR. -- the variadic collapse" },

   /* -- merging: several constraints on one tag become one range ----------- */
   { "K_NUM > 0 .AND. K_NUM < 100",       "T_NUM", "two bounds on one tag -- one leaf, one seek pair" },
   { "K_NUM >= 0 .AND. K_NUM <= 0",       "T_NUM", "bounds that meet -- collapses to eq" },
   { "K_NUM > 100 .AND. K_NUM < 0",       "T_NUM", "bounds that cross -- provably empty before any I/O" },
   { "K_NUM <> 0 .AND. K_NUM <> 2.5",     "T_NUM", "an exclusion list of two" },
   { "K_NUM >= 0 .AND. K_NUM <> 0",       "T_NUM", "an exclusion equal to the bound -- ge must tighten to gt" },

   /* -- OR, whose failure mode is the opposite of AND's --------------------- */
   { "K_NUM < 0 .OR. K_NUM > 100",        "T_NUM", "an OR range -- section 12 item 7, bmf4OrOt was never transcribed" },
   { "K_NUM = 0 .OR. K_NUM = 2.5",        "T_NUM", "two equalities on one tag" },
   { "K_NUM > 0 .AND. K_LOG",             "T_NUM", "partly optimizable .AND. -- the leaf survives, K_LOG re-evaluates" },
   { "K_NUM > 0 .OR. K_LOG",              "T_NUM", "partly optimizable .OR. -- the WHOLE disjunction must fall back" },

   /* -- terms no tag can answer, and the reasons differ -------------------- */
   { "K_FLT > 0",                 0, "an H field: E4FLOAT is absent from e4isConstant, so predicted never optimizable" },
   { "K_INT > 0",                 0, "an integer tag, refused by type (r4log.c:127-139)" },
   { "K_CUR > 0",                 0, "a currency tag, refused by type" },
   { "K_NNUM > 0",                0, "a nullable field -- its tag T_NULL is plain, so this one should optimize" },
   { "EMPTY(K_TEXT)",             0, "a function call at the top of a term -- not a comparison" },
   { "K_LOG",                     0, "a bare logical field" },
   { "DELETED()",                 0, "DELETED() -- neither connective nor comparison" },
   { ".NOT. K_NUM > 0",           0, "E4NOT is neither connective nor comparison, so the whole term falls back" },
   { "1 = 1",                     0, "both sides constant -- neither matches a tag" },
   { "K_NUM > K_DBL",             0, "both sides fields -- no constant" },

   /* -- the type gate's ALLOW side, which the refusals above cannot show ----
    *
    * r4log.c:127-139 is a BLACKLIST of four types, not the whitelist of three
    * its own comment names, so a date tag and a logical tag are eligible. Both
    * need a comparison to be reached at all, and the date one has to avoid the
    * clock: STOD takes a full CCYYMMDD string (row 134) where CTOD infers the
    * century from the system date. */
   { "K_DATE = STOD(\"20200101\")", "T_DATE",  "a DATE tag -- eligible, because the type gate refuses only I, Y, T and T-with-ms" },
   { "K_DATE > STOD(\"20000101\")", "T_DATE",  "a range over a date tag" },
   { "EMPTY(K_TEXT) = .T.",         "T_EMPTY", "a LOGICAL tag -- also outside the blacklist" },

   /* -- the .OR. short circuit is order-dependent, and that is observable ---
    * bitmap4can visits a connective's children BACKWARDS (m4map.c:107), and an
    * .OR. destroys the whole branch the moment one child fails. So in
    * "K_NUM > 0 .OR. K_LOG" the failing K_LOG is reached first and the
    * comparison is never visited at all -- its operand never even gets tested
    * against a tag. Reversed, the comparison IS visited first and its tag is
    * recorded, on a branch that is then thrown away. Same records, different
    * parallel array, and only this pair can tell a port's walk order. */
   { "K_LOG .OR. K_NUM > 0",        "T_NUM",   "the mirror of the .OR. above -- the optimizable disjunct is visited FIRST" },

   /* -- what MERGING two terms over one tag has to get right ----------------
    *
    * Everything above joins terms over different tags or joins them by .OR.
    * without their union being one constraint. These are the cases where two
    * leaves really do become one, and until they existed the whole disjunction
    * side of the merge algebra was gated by C#-authored tests alone. */
   { "K_NUM > 0 .OR. K_NUM > 100",     "T_NUM", "overlapping .OR. ranges -- their union is one range" },
   { "K_NUM <= 0 .OR. K_NUM > 0",      "T_NUM", ".OR. ranges that meet exactly, so the union is everything" },
   { "K_NUM > 0 .OR. K_NUM = 0",       "T_NUM", "a value sitting on the open end of a range" },
   { "K_NUM <> 0 .OR. K_NUM <> 2.5",   "T_NUM", "two exclusions joined by .OR. -- no value fails both" },
   { "UPPER(K_TEXT) > \"S\" .OR. UPPER(K_TEXT) > \"M\"", "T_UPPER",
     "overlapping .OR. ranges over a character tag, both short constants" },

   /* A bounded range with a value struck out of it. Every other exclusion case
    * here is unbounded, which the port answers by naming what it leaves out and
    * inverting -- a different path from walking a range and skipping one key. */
   { "K_NUM > -100 .AND. K_NUM < 1000 .AND. K_NUM <> 0.125", "T_NUM",
     "a bounded range with one value struck out, and a run of three .AND. terms on ONE tag" },

   /* 0.125 and 0.135 are adjacent stored values with nothing between them, so
    * both exclusive ends land on the same pair of neighbouring keys. That is the
    * shape the reference guards with doCheck (C4CONST.C:591-605): seeking both
    * ends of a range that contains nothing must yield nothing, not the one
    * record the two seeks collided on. */
   { "K_NUM > 0.125 .AND. K_NUM < 0.135", "T_NUM",
     "a range with nothing in it whose two seeks collide -- doCheck's case" },

   /* -- the two degenerate answers ----------------------------------------- */
   { "K_NUM > 1000000000",        "T_NUM", "empty result" },
   { "K_NUM > -1000000000",       "T_NUM", "every record" }
};

/* ------------------------------------------------------------------- case */

int caseCdxExpr( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls  */
      { (char *)"ID",     'I',   4,   0,   0      },
      { (char *)"K_TEXT", 'C',  20,   0,   0      },
      { (char *)"K_CITY", 'C',  15,   0,   0      },
      { (char *)"K_NUM",  'N',  12,   3,   0      },
      { (char *)"K_INT",  'I',   4,   0,   0      },
      { (char *)"K_DBL",  'B',   8,   6,   0      },
      { (char *)"K_FLT",  'H',   4,   2,   0      },   /* forced to 4 bytes */
      { (char *)"K_DATE", 'D',   8,   0,   0      },
      { (char *)"K_TS",   'T',   8,   0,   0      },
      { (char *)"K_CUR",  'Y',   8,   4,   0      },
      { (char *)"K_CUR2", 'Y',   8,   4,   0      },
      { (char *)"K_LOG",  'L',   1,   0,   0      },
      { (char *)"K_MEMO", 'M',   4,   0,   0      },
      { (char *)"K_NNUM", 'N',  12,   3,   r4null },
      { 0, 0, 0, 0, 0 }
   };

   /* Tags on two axes. The first ten are function *shapes* whose key rules
    * differ; the next five are one per result *type* a function can return;
    * the five after that are the four types no function returns plus the
    * datetime a field must supply. Then the null indicator, then a filter. */
   static TAG4INFO tags[] =
   {
      /* name        expression                filter  unique  descending */
      { (char *)"T_UPPER", (char *)"UPPER(K_TEXT)",     0, 0, 0 },
      { (char *)"T_TRIM",  (char *)"TRIM(K_TEXT)",      0, 0, 0 },
      { (char *)"T_SUBSTR",(char *)"SUBSTR(K_TEXT,1,5)",0, 0, 0 },
      { (char *)"T_CONCAT",(char *)"K_TEXT+K_CITY",     0, 0, 0 },
      { (char *)"T_CONCTR",(char *)"K_TEXT-K_CITY",     0, 0, 0 },
      { (char *)"T_STRI",  (char *)"STR(K_INT)",        0, 0, 0 },
      { (char *)"T_STRN",  (char *)"STR(K_NUM,12,3)",   0, 0, 0 },
      { (char *)"T_DTOS",  (char *)"DTOS(K_DATE)",      0, 0, 0 },
      { (char *)"T_DTOC",  (char *)"DTOC(K_DATE)",      0, 0, 0 },
      { (char *)"T_DESC",  (char *)"DESCEND(K_TEXT)",   0, 0, 0 },

      { (char *)"T_VAL",   (char *)"VAL(K_TEXT)",       0, 0, 0 },  /* r4numDoub  */
      { (char *)"T_CTOD",  (char *)"CTOD(K_TEXT)",      0, 0, 0 },  /* r4dateDoub */
      { (char *)"T_EMPTY", (char *)"EMPTY(K_TEXT)",     0, 0, 0 },  /* r4log      */
      { (char *)"T_FLT",   (char *)"K_FLT*1",           0, 0, 0 },  /* r4floatBin */
      { (char *)"T_DTIME", (char *)"DATETIME(2026,1,1)",0, 0, 0 },  /* r4dateTime */

      { (char *)"T_NUM",   (char *)"K_NUM",             0, 0, 0 },  /* r4num      */
      { (char *)"T_DATE",  (char *)"K_DATE",            0, 0, 0 },  /* r4date     */
      { (char *)"T_INT",   (char *)"K_INT",             0, 0, 0 },  /* r4int      */
      { (char *)"T_CUR",   (char *)"K_CUR",             0, 0, 0 },  /* r4currency */
      { (char *)"T_TS",    (char *)"K_TS",              0, 0, 0 },  /* r4dateTime */

      { (char *)"T_NULL",  (char *)"K_NNUM",            0, 0, 0 },  /* null byte  */
      { (char *)"T_FILT",  (char *)"UPPER(K_TEXT)",
        (char *)"K_INT > 0 .AND. \"A\" $ K_TEXT",       0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   static char memo[512];

   char path[520];
   DATA4 *data;
   int i, rc;

   sprintf( path, "%s\\CDXEXPR.DBF", outDir );
   printf( "CDXEXPR.DBF (0x30 + CDX + FPT, expression tags) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create CDXEXPR.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      int len = memoLenFor( i );
      int k;

      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong    ( d4fieldJ( data,  1 ), (long)i );
      f4assign        ( d4fieldJ( data,  2 ), TEXTS[i] );
      f4assign        ( d4fieldJ( data,  3 ), CITIES[i] );
      f4assignDouble  ( d4fieldJ( data,  4 ), numFor( i ) );
      f4assignLong    ( d4fieldJ( data,  5 ), intFor( i ) );
      f4assignDouble  ( d4fieldJ( data,  6 ), dblFor( i ) );
      {
         float f = fltFor( i );
         memcpy( f4assignPtr( d4fieldJ( data, 7 ) ), &f, sizeof( f ) );
      }
      f4assign        ( d4fieldJ( data,  8 ), DATES[i] );
      if ( STAMPS[i][0] != '\0' )
         f4assignDateTime( d4fieldJ( data, 9 ), STAMPS[i] );
      f4assignDouble  ( d4fieldJ( data, 10 ), curFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 11 ), curFor( ( i + 3 ) % ROWS ) );
      f4assign        ( d4fieldJ( data, 12 ), logFor( i ) ? "T" : "F" );

      for ( k = 0; k < len; k++ )
         memo[k] = (char)( 'a' + ( k % 26 ) );
      if ( len > 0 )
         f4memoAssignN( d4fieldJ( data, 13 ), memo, (unsigned)len );

      if ( isNullRow( i ) )
         f4assignNull( d4fieldJ( data, 14 ) );
      else
         f4assignDouble( d4fieldJ( data, 14 ), numFor( i ) / 2.0 );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   /* One deleted record, so DEL() and DELETED() have both cases to answer.
    * Deleted records are still indexed -- the deletion flag is a record
    * property, not a key one -- so this does not shrink any tag. */
   if ( d4go( data, DELETED_REC ) < 0 )  { d4close( data ); return fail( cb, "d4go to delete" ); }
   d4delete( data );
   if ( d4write( data, DELETED_REC ) < 0 ) { d4close( data ); return fail( cb, "d4write deleted" ); }

   if ( i4create( data, 0, tags ) == 0 )
   {
      d4close( data );
      return fail( cb, "i4create CDXEXPR.CDX" );
   }
   if ( error4code( cb ) > 0 )
      error4set( cb, 0 );

   printf( "%d records, %d tags\n", ROWS, (int)( sizeof( tags ) / sizeof( tags[0] ) ) - 1 );

   rc = finish( cb, data, outDir, "CDXEXPR.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "CDXEXPR.DBF", "CDXEXPR.cdx" );
   if ( rc == 0 )
      rc = dumpExpressions( cb, outDir, "CDXEXPR.DBF",
                            CONSTANTS,  (int)( sizeof( CONSTANTS  ) / sizeof( CONSTANTS[0]  ) ),
                            FIELDEXPRS, (int)( sizeof( FIELDEXPRS ) / sizeof( FIELDEXPRS[0] ) ),
                            CLOCKEXPRS, (int)( sizeof( CLOCKEXPRS ) / sizeof( CLOCKEXPRS[0] ) ),
                            REJECTIONS, (int)( sizeof( REJECTIONS ) / sizeof( REJECTIONS[0] ) ),
                            VFPONLY,    (int)( sizeof( VFPONLY    ) / sizeof( VFPONLY[0]    ) ) );
   if ( rc == 0 )
      rc = dumpQueries( cb, outDir, "CDXEXPR.DBF",
                        QUERIES, (int)( sizeof( QUERIES ) / sizeof( QUERIES[0] ) ) );

   return rc;
}
