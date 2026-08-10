/* ==========================================================================
 * case-vfpnull.cpp — VFPNULL.DBF (+ VFPNULL.fpt)
 *
 * The nullable-field case: the `_NullFlags` system field (DBF-FORMAT.md §4.1).
 *
 * Three things this case exists to pin down, none of which any other case
 * reaches:
 *
 *   1. The hidden `_NullFlags` descriptor — type '0', flags 0x05 — is written
 *      after every user field, so the file has one more 32-byte descriptor than
 *      the API reports fields (d4numFields subtracts it, d4declar.h:594).
 *   2. Null bits are numbered over the *nullable* fields in physical order, not
 *      over all fields. Nullable and plain fields are interleaved here so an
 *      implementation that used the field index would produce a different
 *      bitmap.
 *   3. Ten nullable fields make `_NullFlags` two bytes wide, so bits 8 and 9
 *      land in the second byte and the byteNum arithmetic is exercised.
 *
 * A nullable memo is included, and it is not independent of its null bit: when
 * a memo with content is flushed at append time the new block id is written
 * with f4assignLong (f4memo.c:801-807), which calls f4assignNotNull. So a memo
 * field that holds a block reference is never null, however the record was
 * assigned. Rows whose mask nulls the memo therefore leave it empty; row 7 is
 * the deliberate exception that records the clearing (see MEMO_CONFLICT_ROW).
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "cases.h"

#define ROWS    32       /* records in this table */
#define NULLERS 10       /* nullable fields, hence null bits 0..9 */
#define MEMO_BIT 9       /* null-bit ordinal of N_M */

/* The one row that asks for a null memo *and* writes memo content, to record
 * that the content wins and the bit ends up clear. Every other row that nulls
 * the memo leaves it empty so the bit survives. */
#define MEMO_CONFLICT_ROW 6

/* --------------------------------------------------------------- test data */

/* 1-based field numbers of the nullable fields, in physical order. Index into
 * this array *is* the null-bit ordinal — which is the whole point of the case:
 * ordinal 5 is field 8, not field 5. */
static const int NULLABLE_FIELD[NULLERS] = { 2, 3, 4, 5, 6, 8, 9, 10, 11, 12 };

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

/* Short payloads: the FPT block-boundary cases belong to VFPMEMO, so this memo
 * only has to prove that a null memo and an empty memo are different things. */
static const int MEMO_LENS[4] = { 0, 5, 40, 120 };

/* Which nullable fields are NULL in row i, as a bitmask over null-bit ordinals.
 * Rows 1-6 are the hand-picked cases; the rest follow a rolling pattern so every
 * bit is both set and clear across the table. */
static unsigned nullMaskFor( int i )
{
   unsigned mask;
   int k;

   switch ( i )
   {
      case 0:  return 0x3FFu;   /* all ten null          -> _NullFlags FF 03 */
      case 1:  return 0x000u;   /* none null             -> 00 00 */
      case 2:  return 0x155u;   /* ordinals 0,2,4,6,8    -> 55 01 */
      case 3:  return 0x200u;   /* ordinal 9 alone, the high byte  -> 00 02 */
      case 4:  return 0x001u;   /* ordinal 0 alone       -> 01 00 */
      case 5:  return 0x100u;   /* ordinal 8, first bit of byte 1  -> 00 01 */
      case MEMO_CONFLICT_ROW:
               return 0x201u;   /* ordinals 0 and 9 asked for, but the memo is
                                 * written too, so bit 9 loses -> 01 00 */
      default: break;
   }

   mask = 0;
   for ( k = 0; k < NULLERS; k++ )
      if ( ( i + k ) % 3 == 0 )
         mask |= ( 1u << k );
   return mask;
}

/* "YYYYMMDDHH:MM:SS" — the format f4assignDateTime expects (F4FIELD.C:2008).
 * Returns 0 for the blank-date row: f4assignDateTime reads the time at
 * dateTime+8 and would run off the end of a shorter string. */
static const char *dateTimeFor( int i, char *buf )
{
   if ( i == 2 )
      return 0;

   sprintf( buf, "%s%02d:%02d:%02d", DATES[i], ( i * 5 ) % 24, ( i * 17 ) % 60, ( i * 23 ) % 60 );
   return buf;
}

static double numFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -999999.99;      /* widest that fits N(10,2) with a sign */
   if ( i == 2 ) return 9999999.99;
   return i * 137.25 - 2000.0;
}

static double floatFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -9999.9999;      /* widest that fits F(10,4) with a sign */
   if ( i == 2 ) return 99999.9999;
   return i * 2.7183 - 40.0;
}

static long intFor( int i )
{
   if ( i == 0 ) return 0L;
   if ( i == 1 ) return -2147483647L - 1L;   /* LONG_MIN, written this way to
                                              * avoid the unary-minus warning */
   if ( i == 2 ) return 2147483647L;
   return i * 999983L - 15000000L;
}

static double doubleFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -0.0;                /* sorts through the positive path;
                                              * see KEY-COLLATION.md */
   if ( i == 2 ) return 2.718281828459045;
   return i * 0.00025 - 4.0;
}

static double currencyFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -99999.9999;
   if ( i == 2 ) return 12345678.9999;
   return i * 77.7777 - 600.0;
}

/* Deterministic printable memo text of the requested length. */
static void memoText( int i, char *buf, int len )
{
   static const char *const WORDS =
      "nullable memo payload 0123456789 abcdefghijklmnopqrstuvwxyz ";
   int wordsLen = (int)strlen( WORDS );
   int k;

   for ( k = 0; k < len; k++ )
      buf[k] = WORDS[ ( k + i * 5 ) % wordsLen ];
   buf[len] = 0;
}

/* ------------------------------------------------------------------- case */

int caseVfpNull( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls   */
      { (char *)"ID",     'I',   4,   0,   0       },   /* plain, so bit 0 is not field 1 */
      { (char *)"N_C",    'C',  10,   0,   r4null  },   /* null bit 0 */
      { (char *)"N_N",    'N',  10,   2,   r4null  },   /* null bit 1 */
      { (char *)"N_F",    'F',  10,   4,   r4null  },   /* null bit 2 */
      { (char *)"N_D",    'D',   8,   0,   r4null  },   /* null bit 3 */
      { (char *)"N_L",    'L',   1,   0,   r4null  },   /* null bit 4 */
      { (char *)"PLAIN",  'C',   8,   0,   0       },   /* breaks the ordinal/index tie */
      { (char *)"N_I",    'I',   4,   0,   r4null  },   /* null bit 5 */
      { (char *)"N_B",    'B',   8,   6,   r4null  },   /* null bit 6 */
      { (char *)"N_Y",    'Y',   8,   4,   r4null  },   /* null bit 7 */
      { (char *)"N_T",    'T',   8,   0,   r4null  },   /* null bit 8 — second byte */
      { (char *)"N_M",    'M',   4,   0,   r4null  },   /* null bit 9 — second byte */
      { (char *)"TAIL",   'L',   1,   0,   0       },   /* _NullFlags follows this */
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32], memo[160], plain[16];
   const char *dt;
   unsigned mask;
   DATA4 *data;
   int i, k, len;

   sprintf( path, "%s\\VFPNULL.DBF", outDir );
   printf( "VFPNULL.DBF (0x30 + FPT, nullable fields) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create VFPNULL.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      sprintf( plain, "P%05d", i );

      /* Every field gets a real value first. The nulls are applied afterwards,
       * so a null field still has whatever bytes assignment left behind — which
       * is exactly the state the port has to read correctly. */
      f4assignLong    ( d4fieldJ( data,  1 ), (long)i );
      f4assign        ( d4fieldJ( data,  2 ), NAMES[ i % 8 ] );
      f4assignDouble  ( d4fieldJ( data,  3 ), numFor( i ) );
      f4assignDouble  ( d4fieldJ( data,  4 ), floatFor( i ) );
      f4assign        ( d4fieldJ( data,  5 ), DATES[i] );
      f4assign        ( d4fieldJ( data,  6 ), ( i % 3 == 1 ) ? "F" : "T" );
      f4assign        ( d4fieldJ( data,  7 ), plain );
      f4assignLong    ( d4fieldJ( data,  8 ), intFor( i ) );
      f4assignDouble  ( d4fieldJ( data,  9 ), doubleFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 10 ), currencyFor( i ) );

      dt = dateTimeFor( i, buf );
      if ( dt != 0 )
         f4assignDateTime( d4fieldJ( data, 11 ), dt );

      mask = nullMaskFor( i );

      /* Writing memo content clears the null bit at flush time, so a row that
       * wants a null memo must leave it empty — except the conflict row, which
       * exists to record exactly that clearing. */
      if ( ( mask & ( 1u << MEMO_BIT ) ) == 0 || i == MEMO_CONFLICT_ROW )
      {
         len = MEMO_LENS[ i % 4 ];
         memoText( i, memo, len );
         f4memoAssignN( d4fieldJ( data, 12 ), memo, (unsigned)len );
      }

      f4assign        ( d4fieldJ( data, 13 ), ( i % 2 == 0 ) ? "T" : "F" );

      for ( k = 0; k < NULLERS; k++ )
         if ( mask & ( 1u << k ) )
            f4assignNull( d4fieldJ( data, (short)NULLABLE_FIELD[k] ) );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "VFPNULL.DBF" );
}
