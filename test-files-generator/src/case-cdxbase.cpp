/* ==========================================================================
 * case-cdxbase.cpp — CDXBASE.DBF + CDXBASE.CDX
 *
 * The corpus's first index file, and the small one: 32 records, so every tag's
 * tree is a single root+leaf block (nodeAttribute 3) and a human can read the
 * whole dump. Ten tags, one per key shape, each a bare field reference so the
 * stored key bytes are the field's bytes (machine collation, CDX-FORMAT.md §12):
 *
 *   T_TEXT   K_C    character keys: long shared prefixes, values short of the
 *                   width (trail > 0), one filling it exactly (trail 0), a
 *                   value that is a prefix of another, two equal keys, and two
 *                   empty values — a blank key has dup 0 and trail = keyLen
 *                   even when its neighbour is blank too (§6.3)
 *   T_TEXTD  K_C    the same keys, descending: physically ascending still, so
 *                   only traversal inverts (§7)
 *   T_DUP    K_DUP  runs of identical keys, ordered by record number
 *   T_UNIQ   K_DUP  unique: typeCode bit 0x01, and fewer keys than records
 *   T_BIN    K_BIN  bytes below the pad character (0x00, 0x01, 0x1F), the pad
 *                   character as data, and 0x80-0xFF — which only an unsigned
 *                   comparison orders correctly
 *   T_NUM    K_N    8-byte numeric keys: pad character NUL, negatives
 *                   complemented whole
 *   T_DBL    K_B    doubles including -0.0, which keys to 00 00 … 00 and
 *                   therefore sorts below every negative (KEY-COLLATION.md §2.1)
 *   T_DATE   K_D    date keys, blank date included
 *   T_INT    K_I    4-byte integer keys, LONG_MIN / LONG_MAX / 0
 *   T_FILT   K_N    a FOR clause: typeCode bit 0x08, filter text after the
 *                   expression text, and a key set smaller than the table
 *
 * The index is built by appending every record first and creating the tags
 * afterwards, which is the bulk path (r4reindexWriteKeys, CDX-FORMAT.md §8.5) —
 * what VFP's INDEX ON and CodeBase's i4create produce, and what packs leaves
 * tight. Trees grown key-by-key through the insert/split path have a different
 * (looser) shape; that belongs to WRITE, which is where splitting is ported.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define ROWS 32          /* records in this table */

/* --------------------------------------------------------------- test data */

/* K_C, C(20). Row 5 and row 17 are empty, so the two blank keys land next to
 * each other at the top of the tag. Rows 1 and 12 are equal, as are 10 and 11,
 * so equal keys are ordered by record number in two different neighbourhoods.
 * Row 9 is exactly 20 characters, so its key has no trailing pad at all. */
static const char *const TEXTS[ROWS] =
{
   "CUSTOMER",                 /*  0 — a prefix of the four below */
   "CUSTOMER-ALPHA",           /*  1 */
   "CUSTOMER-ALPHA-TWO",       /*  2 — shares 14 bytes with row 1 */
   "CUSTOMER-BETA",            /*  3 */
   "CUSTOMER-BETAX",           /*  4 — shares 13 */
   "",                         /*  5 — blank key */
   "ZEBRA",                    /*  6 */
   "AB",                       /*  7 */
   "ABC",                      /*  8 — row 7 is a prefix of it */
   "ABCDEFGHIJKLMNOPQRST",     /*  9 — exactly the field width */
   "MIDDLE",                   /* 10 */
   "MIDDLE",                   /* 11 — equal to row 10 */
   "CUSTOMER-ALPHA",           /* 12 — equal to row 1 */
   "AARDVARK",                 /* 13 */
   "ZEBRA CROSSING",           /* 14 */
   "M",                        /* 15 */
   "MIDDLE-EARTH",             /* 16 */
   "",                         /* 17 — the second blank key */
   "QUEBEC",   "PAPA",    "OSCAR",   "NOVEMBER", "LIMA",    "KILO",
   "JULIETT",  "INDIA",   "HOTEL",   "GOLF",     "FOXTROT", "ECHO",
   "DELTA",    "CHARLIE"
};

/* K_DUP, C(10). Five distinct values over 32 rows, one of them blank, so
 * T_DUP holds 32 keys in runs and T_UNIQ holds 5. */
static const char *const DUPS[6] =
{
   "RED", "GREEN", "BLUE", "", "YELLOW", "BLUE"
};

/* K_BIN, C(8). Eight byte patterns, cycled. The pair at indexes 4 and 5 shares
 * the prefix "AB" and differs only in what follows it — NULs in one, the pad
 * character in the other — so a reader that confuses "trailing pad" with
 * "trailing zero" gets one of them wrong. */
static const unsigned char BIN0[8] = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
static const unsigned char BIN1[8] = { 0x1F, 0x20, 0x21, 0x1F, 0x20, 0x21, 0x1F, 0x20 };
static const unsigned char BIN2[8] = { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA, 0xF9, 0xF8 };
static const unsigned char BIN3[8] = { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20 };
static const unsigned char BIN4[8] = { 'A',  'B',  0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
static const unsigned char BIN5[8] = { 'A',  'B',  0x20, 0x20, 0x20, 0x20, 0x20, 0x20 };
static const unsigned char BIN6[8] = { 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87 };
static const unsigned char BIN7[8] = { 0x7F, 0x80, 0x00, 0xFF, 0x01, 0xFE, 0x02, 0xFD };

static const unsigned char *const BINS[8] =
{
   BIN0, BIN1, BIN2, BIN3, BIN4, BIN5, BIN6, BIN7
};

/* K_D, D(8). Row 2 stays blank. */
static const char *const DATES[ROWS] =
{
   "19000101", "19501231", "        ", "20000101", "20000229", "20200229", "20240229", "20251231",
   "20260101", "20260115", "20260228", "20260301", "20260630", "20260701", "20261231", "20270101",
   "19700101", "19801231", "19851115", "19900620", "19950704", "19980817", "20010911", "20050427",
   "20100302", "20120229", "20150818", "20180505", "20191231", "20220314", "20230801", "19991231"
};

/* K_N, N(12,3) — the numeric key. Row 10 repeats row 9's value, so the numeric
 * tag has equal keys too, not only the character one. */
static double numFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -9999999.999;      /* widest negative that fits N(12,3) */
   if ( i == 2 ) return 99999999.999;
   if ( i == 3 ) return -0.001;
   if ( i == 4 ) return 0.001;
   if ( i == 9 || i == 10 ) return 4242.424;
   return i * 137.5 - 2000.0;
}

/* K_B, B(8) — a true double, so the key transform's edge cases are reachable
 * in a way a decimal N field cannot express. */
static double dblFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -0.0;              /* keys to 00 00 … 00, sorts first */
   if ( i == 2 ) return 3.141592653589793;
   if ( i == 3 ) return 1e15;
   if ( i == 4 ) return -1e-15;
   if ( i == 5 ) return -1e300;
   if ( i == 6 ) return 1e-300;
   return i * 0.000125 - 2.5;
}

/* K_I, I(4) — both a key and T_FILT's FOR clause, so the split has to be
 * uneven: 18 of the 32 rows are positive. */
static long intFor( int i )
{
   if ( i == 0 ) return 0L;
   if ( i == 1 ) return -2147483647L - 1L;   /* LONG_MIN without the unary-minus warning */
   if ( i == 2 ) return 2147483647L;
   return ( i % 4 == 3 ) ? -( (long)i * 1000L ) : ( (long)i * 1000L );
}

/* ------------------------------------------------------------------- case */

int caseCdxBase( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"K_C",    'C',  20,   0,   0 },
      { (char *)"K_DUP",  'C',  10,   0,   0 },
      { (char *)"K_BIN",  'C',   8,   0,   0 },
      { (char *)"K_N",    'N',  12,   3,   0 },
      { (char *)"K_B",    'B',   8,   6,   0 },
      { (char *)"K_D",    'D',   8,   0,   0 },
      { (char *)"K_I",    'I',   4,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name        expression  filter        unique             descending */
   static TAG4INFO tags[] =
   {
      { (char *)"T_TEXT",  (char *)"K_C",   0,                    0,                 0 },
      /* descending is r4descending (10), not 1: E4PARM_HIGH rejects any other
       * non-zero value outright (i4create.c:945-952). */
      { (char *)"T_TEXTD", (char *)"K_C",   0,                    0,                 r4descending },
      { (char *)"T_DUP",   (char *)"K_DUP", 0,                    0,                 0 },
      { (char *)"T_UNIQ",  (char *)"K_DUP", 0,                    r4uniqueContinue,  0 },
      { (char *)"T_BIN",   (char *)"K_BIN", 0,                    0,                 0 },
      { (char *)"T_NUM",   (char *)"K_N",   0,                    0,                 0 },
      { (char *)"T_DBL",   (char *)"K_B",   0,                    0,                 0 },
      { (char *)"T_DATE",  (char *)"K_D",   0,                    0,                 0 },
      { (char *)"T_INT",   (char *)"K_I",   0,                    0,                 0 },
      { (char *)"T_FILT",  (char *)"K_N",   (char *)"K_I > 0",    0,                 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   DATA4 *data;
   int i, rc;

   sprintf( path, "%s\\CDXBASE.DBF", outDir );
   printf( "CDXBASE.DBF (0x30 + CDX, 10 tags, single-block trees) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create CDXBASE.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong  ( d4fieldJ( data, 1 ), (long)i );
      f4assign      ( d4fieldJ( data, 2 ), TEXTS[i] );
      f4assign      ( d4fieldJ( data, 3 ), DUPS[ i % 6 ] );
      f4assignN     ( d4fieldJ( data, 4 ), (const char *)BINS[ i % 8 ], 8 );
      f4assignDouble( d4fieldJ( data, 5 ), numFor( i ) );
      f4assignDouble( d4fieldJ( data, 6 ), dblFor( i ) );
      f4assign      ( d4fieldJ( data, 7 ), DATES[i] );
      f4assignLong  ( d4fieldJ( data, 8 ), intFor( i ) );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   /* Production index: a null file name means <table>.CDX, and it is what sets
    * bit 0x01 of DBF byte 28 (i4create.c:1404-1418) — the first corpus table to
    * carry that flag. T_UNIQ's duplicates leave errorCode at r4uniqueContinue,
    * a positive status rather than a failure (r4reinde.c:2100-2102), so it is
    * cleared here before anything else reads it. */
   if ( i4create( data, 0, tags ) == 0 )
   {
      d4close( data );
      return fail( cb, "i4create CDXBASE.CDX" );
   }
   if ( error4code( cb ) > 0 )
      error4set( cb, 0 );

   printf( "%d records, %d tags\n", ROWS, (int)( sizeof( tags ) / sizeof( tags[0] ) ) - 1 );

   rc = finish( cb, data, outDir, "CDXBASE.DBF" );
   if ( rc == 0 )
   {
      /* Lower-case ".cdx" is what CodeBase writes for a production index, the
       * same asymmetry as ".fpt" beside a ".DBF" (d4defs.h:2589-2598). A port on
       * a case-sensitive filesystem has to resolve the companion accordingly. */
      rc = dumpIndex( cb, outDir, "CDXBASE.DBF", "CDXBASE.cdx" );
   }

   return rc;
}
