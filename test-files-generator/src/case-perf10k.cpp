/* ==========================================================================
 * case-perf10k.cpp — benchmark\PERF10K.DBF + benchmark\PERF10K.cdx
 *
 * The only **benchmark** case: a table large enough to measure against, with
 * no dump and no golden assertion. Every other case here is an expectation;
 * this one is a workload.
 *
 * It is the table step 009's performance audit measured — 10 000 records, a
 * C(20) tag four levels deep and an N(12,2) tag three levels deep — so the
 * numbers the benchmark project prints are comparable with the ones in
 * claude/dev/009-performance-audit/SUMMARY.md rather than merely similar. The
 * field and tag shapes, the row count and the scrambling stride are therefore
 * fixed by that measurement and are not free to tidy: change them and the
 * audit's figures stop being a baseline.
 *
 * Why it is generated here rather than left in the audit's harness: a
 * benchmark nobody can re-run is exactly the state the audit got the project
 * out of, and 55-leaf CDXDEEP is far too small to make a block cache evict
 * anything (DESIGN.md, Open questions, settled 2026-08-19).
 *
 * Three things are deliberately different from every other case:
 *
 *   - it writes into a **benchmark** subdirectory of the output, because
 *     net/corpus/ is discovered non-recursively by CodeBase.Net.TestUtils
 *     Corpus.TableNames and a dumpless table in it would be swept into every
 *     data-driven golden test;
 *   - it calls finishUndumped, so no PERF10K.dump.txt exists to be asserted
 *     against;
 *   - it writes **no query sets**. The audit's harness wrote query files so
 *     that two languages provably asked the same questions; with the C side
 *     gone the benchmark derives its queries from the table itself, which
 *     cannot drift from the data and saves 600 KB of committed text.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>
#include <direct.h>

#include "util.h"
#include "cases.h"

#define ROWS      10000   /* records in this table */
#define NAME_LEN     20   /* NAME, C(20), exactly filled — a whole-key seek */
#define CITIES      500   /* distinct CITY values, so that field has duplicates */

/* Coprime with ROWS, so i -> ( i * STRIDE ) % ROWS is a permutation: every
 * value unique, and key order unrelated to record order. A benchmark whose
 * keys arrive in record order would measure sequential reads and call them
 * seeks. */
#define STRIDE     7919

/* --------------------------------------------------------------- test data */

static const char ALPHA[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

/* NAME, C(20). Exactly twenty characters, so a seek for one is a whole-key
 * seek with no padding rule in the way. The scrambled row number is embedded,
 * which makes the value unique, and the leading letters are derived from it, so
 * consecutive keys in key order share a little but not much — leaves that are
 * realistically rather than artificially compressible. */
static void nameFor( int i, char *out /* >= NAME_LEN + 1 bytes */ )
{
   int s = ( i * STRIDE ) % ROWS;
   int k;

   sprintf( out, "%c%c%c%c-%05d-",
            ALPHA[ s % 26 ], ALPHA[ ( s / 26 ) % 26 ],
            ALPHA[ ( s / 676 ) % 26 ], ALPHA[ ( s / 7 ) % 26 ], s );

   for ( k = 11; k < NAME_LEN; k++ )
      out[k] = ALPHA[ ( s * 3 + k * 5 ) % 26 ];

   out[NAME_LEN] = 0;
}

/* AMOUNT, N(12,2). Quarters, so the value is exact in binary and the two
 * decimals the field stores are the two the key was built from — no rounding
 * sits between the value a benchmark asks for and the key on disk. */
static double amountFor( int i )
{
   int s = ( i * STRIDE ) % ROWS;
   return s * 1000.0 + ( i % 100 ) * 0.25;
}

/* ------------------------------------------------------------------- case */

int casePerf10k( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"NAME",   'C',  20,   0,   0 },
      { (char *)"CITY",   'C',  16,   0,   0 },
      { (char *)"AMOUNT", 'N',  12,   2,   0 },
      { (char *)"HIRED",  'D',   8,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name       expression  filter  unique  descending
    *
    * Two tags, and the pair is the point: T_NAME is a 20-byte character key
    * four levels deep, T_AMT an 8-byte numeric key three levels deep, so a
    * descent measured through one is not the same descent measured through the
    * other. Both are bare field names, which is what ADR-28 lets a table
    * select. */
   static TAG4INFO tags[] =
   {
      { (char *)"T_NAME", (char *)"NAME",   0, 0, 0 },
      { (char *)"T_AMT",  (char *)"AMOUNT", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char dir[520], path[520], name[NAME_LEN + 1], city[17], hired[9];
   DATA4 *data;
   int i;

   /* A subdirectory of its own: see the header comment. */
   sprintf( dir, "%s\\benchmark", outDir );
   _mkdir( dir );   /* fine if it already exists */

   sprintf( path, "%s\\PERF10K.DBF", dir );
   printf( "benchmark\\PERF10K.DBF (0x30 + CDX, 2 tags, no dump) ... " );
   fflush( stdout );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create PERF10K.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      nameFor( i, name );
      sprintf( city, "CITY-%04d", i % CITIES );
      sprintf( hired, "%04d%02d%02d", 2000 + i % 26, 1 + i % 12, 1 + i % 28 );

      f4assignLong  ( d4fieldJ( data, 1 ), (long)i );
      f4assignN     ( d4fieldJ( data, 2 ), name, NAME_LEN );
      f4assign      ( d4fieldJ( data, 3 ), city );
      f4assignDouble( d4fieldJ( data, 4 ), amountFor( i ) );
      f4assign      ( d4fieldJ( data, 5 ), hired );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   if ( i4create( data, 0, tags ) == 0 )
   {
      d4close( data );
      return fail( cb, "i4create PERF10K.cdx" );
   }

   printf( "%d records, %d tags\n", ROWS, (int)( sizeof( tags ) / sizeof( tags[0] ) ) - 1 );

   return finishUndumped( cb, data, dir, "PERF10K.DBF" );
}
