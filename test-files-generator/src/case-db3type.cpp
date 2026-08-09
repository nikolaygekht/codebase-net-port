/* ==========================================================================
 * case-db3type.cpp — DB3TYPE.DBF
 *
 * dBase III / FoxPro 2.x field set (C, N, D, L) with no memo. compatibility 25
 * makes this a version 0x03 table, byte-identical in shape to the dBase III
 * files in original/examples/DATA that the port must read.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "cases.h"

#define ROWS 32          /* records in this table */

/* --------------------------------------------------------------- test data
 *
 * Deterministic and index-driven. Rows 1-3 carry the edge cases (minimum,
 * negative, blank); the rest vary so later index cases have something to sort.
 */

static const char *const DATES[ROWS] =
{
   "19000101", "19501231", "        ", "20000101", "20000229", "20200229", "20240229", "20251231",
   "20260101", "20260115", "20260228", "20260301", "20260630", "20260701", "20261231", "20270101",
   "19700101", "19801231", "19851115", "19900620", "19950704", "19980817", "20010911", "20050427",
   "20100302", "20120229", "20150818", "20180505", "20191231", "20220314", "20230801", "19991231"
};

static const char *const NAMES[8] =
{
   "ALPHA", "bravo", "Charlie Delta", "ECHO", "", "Golf-Hotel", "india juliett", "KILO"
};

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

/* ------------------------------------------------------------------- case */

int caseDb3Type( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name             type  len  dec  nulls */
      { (char *)"CODE",     'C',  10,   0,   0 },
      { (char *)"NAME",     'C',  20,   0,   0 },
      { (char *)"QTY",      'N',   6,   0,   0 },
      { (char *)"PRICE",    'N',  10,   2,   0 },
      { (char *)"HIREDATE", 'D',   8,   0,   0 },
      { (char *)"ACTIVE",   'L',   1,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32];
   DATA4 *data;
   int i;

   sprintf( path, "%s\\DB3TYPE.DBF", outDir );
   printf( "DB3TYPE.DBF (0x03, dBase III types) ... " );

   cb->compatibility = 25;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create DB3TYPE.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assign      ( d4fieldJ( data, 1 ), codeFor( i, buf ) );
      f4assign      ( d4fieldJ( data, 2 ), NAMES[ i % 8 ] );
      f4assignDouble( d4fieldJ( data, 3 ), (double)( ( i * 37 ) % 1000 - 500 ) );
      f4assignDouble( d4fieldJ( data, 4 ), i * 13.75 - 100.5 );
      f4assign      ( d4fieldJ( data, 5 ), DATES[i] );
      f4assign      ( d4fieldJ( data, 6 ), logicalFor( i ) );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "DB3TYPE.DBF" );
}
