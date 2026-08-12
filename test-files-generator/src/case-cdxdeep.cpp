/* ==========================================================================
 * case-cdxdeep.cpp — CDXDEEP.DBF + CDXDEEP.cdx
 *
 * The multi-level case. Every CDX in original/examples/DATA is single-leaf, so
 * without a generated case the whole interior-node half of the format —
 * big-endian record numbers and child pointers, descent, sibling chains — is
 * unreachable (PORTING-PLAN.md §6.3).
 *
 * Depth is bought with key *width*, not record count: a 40-byte key packs about
 * eleven entries into a 512-byte leaf, so a few hundred records already need
 * two levels of branch above the leaves. Buying the same depth with a narrow key
 * would take tens of thousands of records and an unreviewable dump.
 *
 * Four tags, spanning the packing range and answering "repeating and unique in
 * the overall data set" in both directions:
 *
 *   D_WIDE   K_WIDE  C(40) unique, deliberately incompressible — no shared
 *                    prefixes, so entries are near their full width, leaves hold
 *                    few keys, and the tree is at its deepest
 *   D_PFX    K_PFX   C(20) unique with a long shared prefix — duplicate counts
 *                    near the maximum, the opposite extreme
 *   D_DUP    K_DUP   C(8) with ten distinct values over every record — runs of
 *                    equal keys that cross leaf boundaries, so interior entries
 *                    end up with keys equal to each other
 *   D_NUM    K_N     N(12,3) unique — 8-byte numeric keys in a multi-level tree
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define ROWS 600         /* records in this table */

/* --------------------------------------------------------------- test data */

/* K_WIDE, C(40). Deliberately hard to compress: the row number is scrambled
 * into the first bytes, so consecutive keys in *key* order share almost
 * nothing. A multiplier coprime with the row count spreads them without
 * repeating, and the tail is filled from a rotating alphabet so nothing is
 * blank-padded either — worst case for the leaf packer, deepest tree per row. */
static void wideBytes( int i, char *out )
{
   static const char ALPHA[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
   int scrambled = ( i * 397 ) % 1000;
   int k;

   sprintf( out, "%03d-", scrambled );
   for ( k = 4; k < 40; k++ )
      out[k] = ALPHA[ ( scrambled * 7 + k * 13 ) % 36 ];
}

/* K_PFX, C(20). Every value starts with the same fourteen bytes, so each key
 * shares thirteen or more with its neighbour: duplicate counts at the top of
 * their range, and leaves that hold many keys. */
static void prefixBytes( int i, char *out )
{
   sprintf( out, "CUSTOMER-ACCT-%04d", i );      /* 18 bytes, blank-padded to 20 */
}

/* K_DUP, C(8). Ten distinct values across 600 rows, so each key repeats about
 * sixty times and its run spills over leaf boundaries. */
static const char *const DUPS[10] =
{
   "ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO",
   "FOXTROT", "GOLF", "HOTEL", "INDIA", "JULIETT"
};

/* K_N, N(12,3). Distinct and non-monotonic, so the numeric tag's key order is
 * not the record order. */
static double numFor( int i )
{
   return ( ( i * 397 ) % 1000 ) * 1000.0 + i * 0.125 - 400000.0;
}

/* ------------------------------------------------------------------- case */

int caseCdxDeep( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"K_WIDE", 'C',  40,   0,   0 },
      { (char *)"K_PFX",  'C',  20,   0,   0 },
      { (char *)"K_DUP",  'C',   8,   0,   0 },
      { (char *)"K_N",    'N',  12,   3,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name       expression    filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"D_WIDE", (char *)"K_WIDE", 0, 0, 0 },
      { (char *)"D_PFX",  (char *)"K_PFX",  0, 0, 0 },
      { (char *)"D_DUP",  (char *)"K_DUP",  0, 0, 0 },
      { (char *)"D_NUM",  (char *)"K_N",    0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], wide[41], pfx[21];
   DATA4 *data;
   int i, rc;

   sprintf( path, "%s\\CDXDEEP.DBF", outDir );
   printf( "CDXDEEP.DBF (0x30 + CDX, 4 tags, multi-level trees) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create CDXDEEP.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      wideBytes( i, wide );
      prefixBytes( i, pfx );

      f4assignLong  ( d4fieldJ( data, 1 ), (long)i );
      f4assignN     ( d4fieldJ( data, 2 ), wide, 40 );
      f4assignN     ( d4fieldJ( data, 3 ), pfx, 18 );
      f4assign      ( d4fieldJ( data, 4 ), DUPS[ i % 10 ] );
      f4assignDouble( d4fieldJ( data, 5 ), numFor( i ) );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   if ( i4create( data, 0, tags ) == 0 )
   {
      d4close( data );
      return fail( cb, "i4create CDXDEEP.cdx" );
   }

   printf( "%d records, %d tags\n", ROWS, (int)( sizeof( tags ) / sizeof( tags[0] ) ) - 1 );

   rc = finish( cb, data, outDir, "CDXDEEP.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "CDXDEEP.DBF", "CDXDEEP.cdx" );

   return rc;
}
