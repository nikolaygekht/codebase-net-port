/* ==========================================================================
 * case-longname.cpp — LONGNAME.DBF + LONGNAME.cdx + LONGNAME.fpt
 *
 * CodeBase's long-field-name layout (DBF-FORMAT.md section 4.2). d4create
 * switches to it by itself when any name is longer than ten characters
 * (D4CREATE.C:869-870): version 0x31, flags[4] = 1, and each descriptor is the
 * 32-byte one's last 21 bytes, a 2-byte length and the name (D4CREATE.C:1635-
 * 1648), with no 263-byte reserve and 0x0D 0x1A at the end. FoxPro cannot read
 * this file; the C library can, and the port must read it as the C library does.
 *
 *   - SHIP_STATE is ten characters and ORDER_TOTAL eleven: the boundary.
 *   - Names are given in mixed case. The long layout stores them as given
 *     (D4CREATE.C:1645 writes the caller's string, not an upper-cased copy),
 *     and open upper-cases them (D4OPEN.C:316), so [descriptors] and [fields]
 *     disagree in case on purpose.
 *   - Delivery_Date_Requested is nullable, so the hidden _NullFlags descriptor
 *     is written in the long layout too (D4CREATE.C:1672-1680).
 *   - A memo with a long name, one payload straddling the 512-byte block.
 *   - Four tags by i4create, each over a name past ten characters or at it,
 *     one expression in mixed case; and queries naming them, one qualified by
 *     the table's alias, one with a term only a record can answer.
 *
 * Added 2026-11-12, step 013 sub-step 7a-1. The data below belongs to this case
 * alone — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "dump-query.h"
#include "cases.h"

#define ROWS 8           /* records in this table */
#define MEMO_MAX 600     /* the longest memo written */

/* --------------------------------------------------------------- test data */

static const char  *NAMES[ROWS]  = { "Smith", "Jones", "SMITH", "Alvarez", "smith", "Okafor", "Nguyen", "Brown" };
static const double TOTALS[ROWS] = { 125.50, 42.00, 310.75, 99.99, 100.00, 0.00, 1500.25, -12.50 };
static const char  *STATES[ROWS] = { "CA", "NY", "TX", "CA", "WA", "NY", "TX", "CA" };

/* CCYYMMDD; 0 is null. */
static const char  *DATES[ROWS]  = { "20260315", 0, "20260101", "20251231", 0, "20260228", "20260704", 0 };

/* Memo text; 0 is a generated payload of LONG_MEMO_LEN bytes, past one block. */
static const char  *MEMOS[ROWS]  = { "Leave at the door", "", "Call first", "", "Fragile, handle with care", "", 0, "Refund" };
#define LONG_MEMO_LEN 600

static const QUERYCASE QUERIES[] =
{
   { "UPPER(CUSTOMER_NAME) = \"SMITH\"",   0,          "a long name, answered by CUSTNAME" },
   { "ORDER_TOTAL > 100",                  "TOTAL",    "an eleven-character name, answered by TOTAL and walked in its order" },
   { "LONGNAME->SHIP_STATE = \"CA\"",      "CUSTNAME", "a ten-character name qualified by the table's alias, walked in name order" },
   { "ORDER_TOTAL >= 99.99 .AND. TRIM(Customer_Name) = \"Smith\"", 0, "a tagged term beside one only a record can answer" },
   { "ID > 0",                             "DELIVERY", "every record, walked in the nullable long-named date's order" }
};

/* ------------------------------------------------------------------- case */

int caseLongName( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name                                             type  len  dec  nulls  */
      { (char *)"ID",                                      'I',   4,   0,   0      },
      { (char *)"Customer_Name",                           'C',  20,   0,   0      },
      { (char *)"ORDER_TOTAL",                             'N',  10,   2,   0      },
      { (char *)"SHIP_STATE",                              'C',   2,   0,   0      },
      { (char *)"Delivery_Date_Requested",                 'D',   8,   0,   r4null },
      { (char *)"Delivery_Instructions_For_The_Courier",   'M',   4,   0,   0      },
      { 0, 0, 0, 0, 0 }
   };

   /* name        expression                         filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"CUSTNAME", (char *)"UPPER(Customer_Name)",    0, 0, 0 },
      { (char *)"TOTAL",    (char *)"ORDER_TOTAL",             0, 0, 0 },
      { (char *)"STATE",    (char *)"SHIP_STATE",              0, 0, 0 },
      { (char *)"DELIVERY", (char *)"DELIVERY_DATE_REQUESTED", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   char memo[MEMO_MAX];
   DATA4 *data;
   INDEX4 *index;
   int i, k, rc;

   sprintf( path, "%s\\LONGNAME.DBF", outDir );
   printf( "LONGNAME.DBF (0x31 + CDX + FPT, long field names) ... " );

   cb->compatibility = 30;

   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create LONGNAME.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong  ( d4fieldJ( data, 1 ), (long)( i + 1 ) );
      f4assign      ( d4fieldJ( data, 2 ), NAMES[i] );
      f4assignDouble( d4fieldJ( data, 3 ), TOTALS[i] );
      f4assign      ( d4fieldJ( data, 4 ), STATES[i] );

      if ( DATES[i] == 0 )
         f4assignNull( d4fieldJ( data, 5 ) );
      else
         f4assign( d4fieldJ( data, 5 ), DATES[i] );

      if ( MEMOS[i] != 0 )
         f4memoAssign( d4fieldJ( data, 6 ), MEMOS[i] );
      else
      {
         for ( k = 0; k < LONG_MEMO_LEN; k++ )
            memo[k] = (char)( 'a' + ( k % 26 ) );
         f4memoAssignN( d4fieldJ( data, 6 ), memo, (unsigned)LONG_MEMO_LEN );
      }

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   index = i4create( data, 0, tags );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create LONGNAME.cdx" );
   }

   printf( "%d records, 4 tags\n", ROWS );

   rc = finish( cb, data, outDir, "LONGNAME.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "LONGNAME.DBF", "LONGNAME.cdx" );
   if ( rc == 0 )
      rc = dumpQueries( cb, outDir, "LONGNAME.DBF",
                        QUERIES, (int)( sizeof( QUERIES ) / sizeof( QUERIES[0] ) ) );

   return rc;
}
