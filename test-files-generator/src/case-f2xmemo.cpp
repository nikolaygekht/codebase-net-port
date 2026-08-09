/* ==========================================================================
 * case-f2xmemo.cpp — F2XMEMO.DBF (+ F2XMEMO.fpt)
 *
 * FoxPro 2.x table with a memo: version 0xF5 plus an .FPT whose record memo
 * references are the 10-byte ASCII form (FPT-MEMO.md §3.4), a different code
 * path from VFP's 4-byte binary reference.
 *
 * NOTE: genuine dBase III memo (version 0x83 + .DBT) cannot be produced from
 * this build — it is S4MNDX-only (DBF-FORMAT.md §2.1) — and .DBT is outside
 * the port's scope. This is the closest reachable legacy-memo case.
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

static const char *const DATES[ROWS] =
{
   "19000101", "19501231", "        ", "20000101", "20000229", "20200229", "20240229", "20251231",
   "20260101", "20260115", "20260228", "20260301", "20260630", "20260701", "20261231", "20270101",
   "19700101", "19801231", "19851115", "19900620", "19950704", "19980817", "20010911", "20050427",
   "20100302", "20120229", "20150818", "20180505", "20191231", "20220314", "20230801", "19991231"
};

/* Memo payload lengths, cycled. 504 = exactly one 512-byte FPT block once the
 * 8-byte block header is added; 505 is the first length needing two blocks
 * (FPT-MEMO.md §3.3). 0 means "no memo". */
static const int MEMO_LENS[8] = { 0, 1, 7, 63, 200, 503, 504, 505 };

static const char *codeFor( int i, char *buf )
{
   if ( i == 2 )
      buf[0] = 0;                       /* empty -> blank field */
   else
      sprintf( buf, "CODE%04d", i + 1 );
   return buf;
}

static const char *logicalFor( int i )
{
   return ( i % 3 == 0 ) ? "T" : ( ( i % 3 == 1 ) ? "F" : "T" );
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

/* ------------------------------------------------------------------- case */

int caseF2xMemo( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name              type  len  dec  nulls */
      { (char *)"CODE",      'C',  10,   0,   0 },
      { (char *)"QTY",       'N',   6,   0,   0 },
      { (char *)"ENTRYDATE", 'D',   8,   0,   0 },
      { (char *)"ACTIVE",    'L',   1,   0,   0 },
      { (char *)"NOTES",     'M',  10,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32], memo[600];
   DATA4 *data;
   int i, len;

   sprintf( path, "%s\\F2XMEMO.DBF", outDir );
   printf( "F2XMEMO.DBF (0xF5 + FPT, 10-byte memo refs) ... " );

   cb->compatibility = 25;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create F2XMEMO.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      len = MEMO_LENS[ i % 8 ];
      memoText( i, memo, len );

      f4assign      ( d4fieldJ( data, 1 ), codeFor( i, buf ) );
      f4assignDouble( d4fieldJ( data, 2 ), (double)( ( i * 41 ) % 1000 - 500 ) );
      f4assign      ( d4fieldJ( data, 3 ), DATES[i] );
      f4assign      ( d4fieldJ( data, 4 ), logicalFor( i ) );
      f4memoAssignN ( d4fieldJ( data, 5 ), memo, (unsigned)len );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "F2XMEMO.DBF" );
}
