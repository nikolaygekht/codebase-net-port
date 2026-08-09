/* ==========================================================================
 * case-vfptype.cpp — VFPTYPE.DBF
 *
 * Every Visual FoxPro field type that is not a memo: C N F D L I B Y T.
 * CodeBase-only types (H, W, Q, V, ...) are deliberately excluded so the file
 * keeps genuine-VFP shape.
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
 * Rows 1-3 carry the type edge cases (zero, minimum, maximum/blank); the rest
 * vary. The numeric extremes are chosen to fit their field widths exactly — a
 * value one digit wider would store as '*' overflow instead.
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

/* "YYYYMMDDHH:MM:SS" — the format f4assignDateTime expects (F4FIELD.C:2008).
 * Returns 0 for the row that must stay blank: f4assignDateTime reads the time
 * at dateTime+8 and would run off the end of a shorter string, so the blank
 * case is produced by leaving d4blank's zeros alone instead. */
static const char *dateTimeFor( int i, char *buf )
{
   if ( i == 2 )
      return 0;

   sprintf( buf, "%s%02d:%02d:%02d", DATES[i], ( i * 7 ) % 24, ( i * 13 ) % 60, ( i * 29 ) % 60 );
   return buf;
}

static double numFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -9999999.999;
   if ( i == 2 ) return 99999999.999;
   return i * 1234.567 - 5000.0;
}

static double floatFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -999999.9999;    /* widest that fits N(12,4) with a sign */
   if ( i == 2 ) return 9999999.9999;
   return i * 3.1416 - 20.0;
}

static long intFor( int i )
{
   if ( i == 0 ) return 0L;
   if ( i == 1 ) return -2147483647L - 1L;      /* LONG_MIN, written this way to
                                                 * avoid the unary-minus warning */
   if ( i == 2 ) return 2147483647L;
   return i * 1000003L - 16000000L;
}

static double doubleFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -0.0;                   /* sorts through the positive
                                                 * path; see KEY-COLLATION.md */
   if ( i == 2 ) return 3.141592653589793;
   if ( i == 3 ) return 1e15;
   if ( i == 4 ) return -1e-15;
   return i * 0.000125 - 2.5;
}

static double currencyFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -12345.6789;
   if ( i == 2 ) return 99999999.9999;
   return i * 111.1111 - 900.0;
}

static const char *logicalFor( int i )
{
   return ( i % 3 == 0 ) ? "T" : ( ( i % 3 == 1 ) ? "F" : "T" );
}

/* ------------------------------------------------------------------- case */

int caseVfpType( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"F_C",    'C',  20,   0,   0 },
      { (char *)"F_N",    'N',  12,   3,   0 },
      { (char *)"F_F",    'F',  12,   4,   0 },
      { (char *)"F_D",    'D',   8,   0,   0 },
      { (char *)"F_L",    'L',   1,   0,   0 },
      { (char *)"F_I",    'I',   4,   0,   0 },
      { (char *)"F_B",    'B',   8,   6,   0 },
      { (char *)"F_Y",    'Y',   8,   4,   0 },
      { (char *)"F_T",    'T',   8,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32];
   const char *dt;
   DATA4 *data;
   int i;

   sprintf( path, "%s\\VFPTYPE.DBF", outDir );
   printf( "VFPTYPE.DBF (0x30, all non-memo VFP types) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create VFPTYPE.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assign        ( d4fieldJ( data, 1 ), NAMES[ i % 8 ] );
      f4assignDouble  ( d4fieldJ( data, 2 ), numFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 3 ), floatFor( i ) );
      f4assign        ( d4fieldJ( data, 4 ), DATES[i] );
      f4assign        ( d4fieldJ( data, 5 ), logicalFor( i ) );
      f4assignLong    ( d4fieldJ( data, 6 ), intFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 7 ), doubleFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 8 ), currencyFor( i ) );

      dt = dateTimeFor( i, buf );
      if ( dt != 0 )
         f4assignDateTime( d4fieldJ( data, 9 ), dt );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "VFPTYPE.DBF" );
}
