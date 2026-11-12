/* ==========================================================================
 * case-wstr.cpp — WSTR.DBF + WSTR.cdx
 *
 * CodeBase's two UTF-16 field types (d4defs.h:2795-2808): r5wstr 'W', the
 * string at the field's width, and r5wstrLen 'O', the string followed by a
 * u16 count of its characters that nothing reads back. VFP never writes
 * either, and VFP 9 uses 'W' for its Blob — the port tells them apart by the
 * table version (ADR-76).
 *
 * What it pins down:
 *
 *   1. The stored bytes: little-endian code units as assigned
 *      (f4assignWideString, F4WSTR.C:80-120), a shorter string blanked first
 *      to 00 20 pairs — U+2000 to a little-endian reader (F4FIELD.C:92-108),
 *      a longer one cut at the width, and for 'O' the count written last.
 *   2. The keys: a machine-collated tag over either swaps each code unit
 *      (t4unicodeToMachine, i4conv.c:588-602), pad NUL; W_DESC the same keys
 *      descending.
 *   3. STR() of a 'W' field: WideCharToMultiByte(CP_ACP, 0, ...)
 *      (e4functi.c:2694-2716) — so the reference narrows by the HOST's ANSI
 *      code page. The table is marked cp1251 (0xC9) and the port narrows by
 *      the table's code page (ADR-77); the two agree only where the host's is
 *      1251 too, so this case refuses to run anywhere else rather than write
 *      keys that depend on the machine. The rows reach characters cp1251
 *      holds, ones Windows best-fits (some of which .NET does not — the
 *      supplement), ones with no near character, a surrogate pair, and the
 *      blank tail. S_ASC keys them; the expression dump records the values;
 *      the queries compare them.
 *
 * Added 2026-11-12, step 013 sub-step 7a-5. The data below belongs to this
 * case alone — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "dump-expr.h"
#include "dump-query.h"
#include "cases.h"

#define CODE_PAGE  0xC9   /* Windows Cyrillic, assigned directly as case-cp1251 does */
#define ACP_NEEDED 1251   /* the host code page STR() narrows by in the reference */

/* --------------------------------------------------------------- test data */

/* Each string is spelled as code units, NUL-ended, so no source encoding or
 * escape can change what is stored. */
static const WSTR5 S00[] = { 'A', 'B', 'C', 0 };
static const WSTR5 S01[] = { 'a', 'b', 'c', 0 };
static const WSTR5 S02[] = { 0x0416, 0x0430, 0x0440, 0 };                              /* Cyrillic, held by cp1251 */
static const WSTR5 S03[] = { 0x041F, 0x0440, 0x0438, 0x0432, 0x0435, 0x0442, 0 };
static const WSTR5 S04[] = { 'H', 'e', 'l', 'l', 'o', ',', ' ', 0x043C, 0x0438, 0x0440, 0 }; /* exactly ten */
static const WSTR5 S05[] = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 0 };
static const WSTR5 S06[] = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 0 }; /* cut at the width */
static const WSTR5 S08[] = { 0 };                                                     /* empty: blanked */
static const WSTR5 S09[] = { 0x2010, 0x2024, 0x2236, 0 };                             /* Windows best-fits, .NET does not */
static const WSTR5 S10[] = { 0x2013, 0x2116, 0 };                                     /* held by cp1251 at 0x96, 0xB9 */
static const WSTR5 S11[] = { 0x0100, 0x0101, 0 };                                     /* best-fitted by both */
static const WSTR5 S12[] = { 0x4E2D, 0x6587, 0 };                                     /* no near character */
static const WSTR5 S13[] = { 'a', 0xD83D, 0xDE00, 'b', 0 };                           /* a surrogate pair */
static const WSTR5 S14[] = { 'A', 0x2000, 'B', 0 };                                   /* the blank, inside */
static const WSTR5 S16[] = { ' ', ' ', 'A', 'B', 'C', 0 };
static const WSTR5 S17[] = { 'A', 'B', 'C', ' ', ' ', 0 };                           /* real spaces at the end */
static const WSTR5 S18[] = { 0x00E9, 't', 0x00E9, 0 };
static const WSTR5 S19[] = { 0x0401, 0x0451, 0 };
static const WSTR5 S20[] = { 'Z', 0 };
static const WSTR5 S21[] = { 0xFFFF, 0 };
static const WSTR5 S22[] = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 0 };
static const WSTR5 S23[] = { 0x0416, 0 };
static const WSTR5 S24[] = { 0x2007, 0x2015, 0x201B, 0x201F, 0x2032, 0x2044, 0 };  /* more of the supplement */
static const WSTR5 S25[] = { 0x044F, 0x0430, 0 };

/* Row i's string, or 0 for the row left blank. Row 15 repeats row 0. */
static const WSTR5 *VALUES[] =
{
   S00, S01, S02, S03, S04, S05, S06, 0, S08, S09, S10, S11, S12, S13,
   S14, S00, S16, S17, S18, S19, S20, S21, S22, S23, S24, S25
};

#define ROWS ( (int)( sizeof( VALUES ) / sizeof( VALUES[0] ) ) )

/* --------------------------------------------------------- expression dump */

static const EXPRCASE FIELDEXPRS[] =
{
   { "K_W",             "E4FIELD_WSTR: the field's bytes at its width" },
   { "K_O",             "E4FIELD_WSTR_LEN: the field less its two-byte count" },
   { "STR(K_W)",        "row 109, narrowed by the host's ANSI code page over half the width" },
   { "STR(K_W, 3)",     "the width folds as a number's would; the operand then picks row 109" },
   { "STR(K_W, 10, 2)", "the decimals fold too and change nothing" }
};

static const EXPRCASE REJECTIONS[] =
{
   { "STR(K_O)",        "row 109 wants r5wstr, and an O field is r5wstrLen" },
   { "K_W = K_W",       "no row compares a wide string" },
   { "K_W > \"A\"",     "nor a wide string with text" }
};

/* ----------------------------------------------------------------- queries */

/* The cp1251 bytes of the Cyrillic literals, assembled at run time so the
 * source holds no high bytes. */
static char Q_CYRILLIC[64];
static char Q_RANGE[64];

static QUERYCASE QUERIES[] =
{
   { "STR(K_W) = \"ABC\"",                           0,       "equality over the narrowed text, answered by S_ASC" },
   { "STR(K_W) = \"ABC\"",                           "S_ASC", "the same, walked in tag order" },
   { Q_CYRILLIC,                                     0,       "a Cyrillic literal: the table's code page and the host's agree" },
   { "STR(K_W) = \"-.:\"",                           0,       "the supplement's code points, best-fitted by Windows" },
   { "STR(K_W) = \"          \"",                    0,       "the blank rows: U+2000 narrows to a space" },
   { Q_RANGE,                                        "S_ASC", "a range up to a Cyrillic bound" }
};

/* ------------------------------------------------------------------- case */

int caseWstr( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name         type  len  dec  nulls */
      { (char *)"ID",   'I',   4,   0,   0 },
      { (char *)"K_W",  'W',  20,   0,   0 },
      { (char *)"K_O",  'O',  12,   0,   0 },   /* stored 14: two more for the count */
      { 0, 0, 0, 0, 0 }
   };

   /* name           expression           filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"W_ASC",  (char *)"K_W",      0, 0, 0 },
      { (char *)"W_DESC", (char *)"K_W",      0, 0, r4descending },
      { (char *)"O_ASC",  (char *)"K_O",      0, 0, 0 },
      { (char *)"S_ASC",  (char *)"STR(K_W)", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   DATA4 *data;
   INDEX4 *index;
   int i, rc;
   unsigned acp = GetACP();

   sprintf( path, "%s\\WSTR.DBF", outDir );
   printf( "WSTR.DBF (0x30 + CDX, codePage 0x%02X, the W and O wide strings) ... ", CODE_PAGE );

   if ( acp != ACP_NEEDED )
   {
      fprintf( stderr, "\n  ERROR: WSTR needs the host's ANSI code page to be %d and it is %u: the reference\n"
                       "  narrows STR() of a wide field by it, and the corpus records cp%d's answers.\n",
               ACP_NEEDED, acp, ACP_NEEDED );
      return 1;
   }

   /* Zhe, a, er; and the bound of the range: Zhe. */
   sprintf( Q_CYRILLIC, "STR(K_W) = \"%c%c%c\"", (char)0xC6, (char)0xE0, (char)0xF0 );
   sprintf( Q_RANGE, "STR(K_W) >= \"A\" .AND. STR(K_W) <= \"%c\"", (char)0xC6 );

   cb->compatibility = 30;

   cb->codePage = CODE_PAGE;
   data = d4create( cb, path, fields, 0 );
   cb->codePage = cp0;

   if ( data == 0 )
      return fail( cb, "d4create WSTR.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)( i + 1 ) );

      if ( VALUES[i] != 0 )
      {
         f4assignWideString( d4fieldJ( data, 2 ), VALUES[i] );
         f4assignWideString( d4fieldJ( data, 3 ), VALUES[i] );
      }

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   index = i4create( data, 0, tags );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create WSTR.cdx" );
   }

   printf( "%d records, 4 tags\n", ROWS );

   rc = finish( cb, data, outDir, "WSTR.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "WSTR.DBF", "WSTR.cdx" );
   if ( rc == 0 )
      rc = dumpExpressions( cb, outDir, "WSTR.DBF",
                            0, 0,
                            FIELDEXPRS, (int)( sizeof( FIELDEXPRS ) / sizeof( FIELDEXPRS[0] ) ),
                            0, 0,
                            REJECTIONS, (int)( sizeof( REJECTIONS ) / sizeof( REJECTIONS[0] ) ),
                            0, 0,
                            0, 0 );
   if ( rc == 0 )
      rc = dumpQueries( cb, outDir, "WSTR.DBF",
                        QUERIES, (int)( sizeof( QUERIES ) / sizeof( QUERIES[0] ) ) );

   return rc;
}
