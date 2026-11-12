/* ==========================================================================
 * case-dtmilli.cpp — DTMILLI.DBF + DTMILLI.cdx
 *
 * The CodeBase '7' field, r4dateTimeMilli (d4defs.h:2821): a datetime that
 * keeps its milliseconds. VFP never writes one and cannot open it. The same
 * eight bytes as 'T' — Julian day, milliseconds since midnight — but the
 * milliseconds are stored as given (f4assignDateTime with ".MMM", F4FIELD.C:
 * 2006-2060), rendered as given (time4assign, F4FIELD.C:1859-1891), and keyed
 * without rounding by t4dateTimeToFoxMilli (i4conv.c:2292-2364), which still
 * consults flags4dateTime, by the truncated second.
 *
 * The moments: seconds spread across the day by a stride coprime to 86400, so
 * that both flagged and unflagged seconds of flags4dateTime are reached; each
 * with milliseconds cycling through the values either side of the rounding
 * point and of a whole second; the day's two edges; a repeated moment; and one
 * blank record, eight zero bytes. Two tags over the field, ascending and
 * descending, by i4create. No query case: no constant has this type, so no
 * filter can compare one.
 *
 * Added 2026-11-12, step 013 sub-step 7a-4. The data below belongs to this case
 * alone — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define ROWS 300         /* records in this table */
#define STRIDE 7919      /* seconds between moments: prime, so coprime to 86400 */

/* --------------------------------------------------------------- test data */

/* Milliseconds either side of the rounding point and of a whole second. */
static const int MILLIS[] = { 0, 1, 123, 499, 500, 501, 876, 998, 999, 250 };

static const char *DAYS[] = { "20260115", "19991231", "20000229", "20381119" };

/* The moment of row i as "CCYYMMDDHH:MM:SS.MMM", or 0 for the blank row. */
static const char *momentFor( int i, char *buf )
{
   int second, milli;

   if ( i == 7 )
      return 0;                                         /* blank */
   if ( i == 0 )
   {
      sprintf( buf, "%s00:00:00.000", DAYS[0] );      /* the day's first moment */
      return buf;
   }
   if ( i == 1 )
   {
      sprintf( buf, "%s23:59:59.999", DAYS[0] );      /* and its last */
      return buf;
   }
   if ( i == 2 )
   {
      sprintf( buf, "%s00:00:00.001", DAYS[0] );
      return buf;
   }
   if ( i == 3 )
      i = 4;                                            /* row 3 repeats row 4's moment */

   second = (int)( ( (long)i * STRIDE ) % 86400L );
   milli  = MILLIS[ i % (int)( sizeof( MILLIS ) / sizeof( MILLIS[0] ) ) ];

   sprintf( buf, "%s%02d:%02d:%02d.%03d",
            DAYS[ i % (int)( sizeof( DAYS ) / sizeof( DAYS[0] ) ) ],
            second / 3600, ( second / 60 ) % 60, second % 60, milli );
   return buf;
}

/* ------------------------------------------------------------------- case */

int caseDtMilli( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name           type  len  dec  nulls */
      { (char *)"ID",      'I',   4,   0,   0 },
      { (char *)"K_DTMS",  '7',   8,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name         expression  filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"M_ASC",  (char *)"K_DTMS",  0, 0, 0 },
      { (char *)"M_DESC", (char *)"K_DTMS",  0, 0, r4descending },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32];
   const char *moment;
   DATA4 *data;
   INDEX4 *index;
   int i, rc;

   sprintf( path, "%s\\DTMILLI.DBF", outDir );
   printf( "DTMILLI.DBF (0x30 + CDX, the '7' datetime with milliseconds) ... " );

   cb->compatibility = 30;

   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create DTMILLI.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)( i + 1 ) );

      moment = momentFor( i, buf );
      if ( moment != 0 )
         f4assignDateTime( d4fieldJ( data, 2 ), moment );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   index = i4create( data, 0, tags );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create DTMILLI.cdx" );
   }

   printf( "%d records, 2 tags\n", ROWS );

   rc = finish( cb, data, outDir, "DTMILLI.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "DTMILLI.DBF", "DTMILLI.cdx" );

   return rc;
}
