/* ==========================================================================
 * case-vfpmemo.cpp — VFPMEMO.DBF (+ VFPMEMO.fpt)
 *
 * VFP table with memo and the binary variants: 'M' text memo, 'X' binary memo
 * and 'Z' binary character (both stored as 'M'/'C' with nullBinary bit 0x04,
 * DBF-FORMAT.md §5), plus 'G' general. Memo references are the 4-byte binary
 * form. Text payload lengths straddle the 512-byte FPT block boundary.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "cases.h"

#define ROWS 32          /* records in this table */

/* --------------------------------------------------------------- test data */

static const char *const NAMES[8] =
{
   "ALPHA", "bravo", "Charlie Delta", "ECHO", "", "Golf-Hotel", "india juliett", "KILO"
};

/* Text memo payload lengths, cycled. 504 = exactly one 512-byte FPT block once
 * the 8-byte block header is added; 505 is the first length needing two blocks
 * (FPT-MEMO.md §3.3). 0 means "no memo". */
static const int MEMO_LENS[8] = { 0, 1, 7, 63, 200, 503, 504, 505 };

/* Binary memo payloads stay short on purpose: the block-boundary cases are
 * covered by the text memo above, so these only need to prove byte-transparent
 * storage, and keeping them small keeps the .fpt small. */
static const int BIN_LENS[4] = { 0, 1, 5, 16 };

static long intFor( int i )
{
   if ( i == 0 ) return 0L;
   if ( i == 1 ) return -2147483647L - 1L;      /* LONG_MIN, written this way to
                                                 * avoid the unary-minus warning */
   if ( i == 2 ) return 2147483647L;
   return i * 1000003L - 16000000L;
}

/* Deterministic printable memo text of the requested length. */
static void memoText( int i, char *buf, int len )
{
   static const char *const WORDS =
      "the quick brown fox jumps over the lazy dog 0123456789 ";
   int wordsLen = (int)strlen( WORDS );
   int k;

   for ( k = 0; k < len; k++ )
      buf[k] = WORDS[ ( k + i * 3 ) % wordsLen ];
   buf[len] = 0;
}

/* Deterministic binary memo payload: every byte value occurs. */
static void memoBinary( int i, char *buf, int len )
{
   int k;
   for ( k = 0; k < len; k++ )
      buf[k] = (char)( ( k * 7 + i * 31 ) & 0xFF );
}

/* ------------------------------------------------------------------- case */

int caseVfpMemo( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name            type  len  dec  nulls */
      { (char *)"ID",      'I',   4,   0,   0 },
      { (char *)"NAME",    'C',  16,   0,   0 },
      { (char *)"NOTES",   'M',   4,   0,   0 },
      { (char *)"BINMEMO", 'X',   4,   0,   0 },
      { (char *)"GEN",     'G',   4,   0,   0 },
      { (char *)"BINCHAR", 'Z',   8,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], memo[600], binary[600], binChar[8];
   DATA4 *data;
   int i, k, len;

   sprintf( path, "%s\\VFPMEMO.DBF", outDir );
   printf( "VFPMEMO.DBF (0x30 + FPT, memo/binary types) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create VFPMEMO.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      binChar[0] = (char)0x00;  binChar[1] = (char)0x01;  binChar[2] = (char)0x7F;
      binChar[3] = (char)0x80;  binChar[4] = (char)0xFF;  binChar[5] = (char)i;
      binChar[6] = (char)0xAA;  binChar[7] = (char)0x55;

      f4assignLong( d4fieldJ( data, 1 ), intFor( i ) );
      f4assign    ( d4fieldJ( data, 2 ), NAMES[ i % 8 ] );

      len = MEMO_LENS[ i % 8 ];
      memoText( i, memo, len );
      f4memoAssignN( d4fieldJ( data, 3 ), memo, (unsigned)len );

      len = BIN_LENS[ i % 4 ];
      memoBinary( i, binary, len );
      f4memoAssignN( d4fieldJ( data, 4 ), binary, (unsigned)len );

      /* General stays mostly empty — it keeps the .fpt small and still covers
       * the "general field with content" path. */
      if ( i % 8 == 3 )
      {
         for ( k = 0; k < 24; k++ )
            binary[k] = (char)( 0xE0 - k );
         f4memoAssignN( d4fieldJ( data, 5 ), binary, 24 );
      }

      f4assignN( d4fieldJ( data, 6 ), binChar, 8 );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "VFPMEMO.DBF" );
}
