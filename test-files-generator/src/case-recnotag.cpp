/* ==========================================================================
 * case-recnotag.cpp — RECNOTAG.DBF + RECNOTAG.cdx
 *
 * Tags whose expression reads RECNO(). The reference evaluates RECNO() as the
 * current record's number (E4FUNCTI.C:2509-2516), and building a tag sets that
 * number to each record's own before its key is evaluated (R4REINDX.C:387-391,
 * r4reinde.c:762-767). So a RECNO() key is the record's number, the same every
 * time for the same record — unlike DATE(), TIME() and RECCOUNT(), which the
 * port refuses in a key (ADR-49, ADR-73).
 *
 *   - R_RECNO over RECNO() alone: a numeric key, one per record.
 *   - R_NAMENO over NAME + STR(RECNO(), 4): the idiom that makes a name tag
 *     unique, so equal names order by record number.
 *
 * Names repeat so the second tag's record-number tail decides between them.
 * Filters through dump-query.cpp: a range over RECNO(), a closed range walked
 * in R_NAMENO's order, and a name term beside a number term.
 *
 * Added 2026-11-12, step 013 sub-step 7a-2. The data below belongs to this case
 * alone — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump-index.h"
#include "dump-query.h"
#include "cases.h"

#define ROWS 10          /* records in this table */

/* --------------------------------------------------------------- test data */

static const char *NAMES[ROWS] =
{
   "beta", "alpha", "beta", "gamma", "alpha", "beta", "delta", "alpha", "gamma", "beta"
};

static const QUERYCASE QUERIES[] =
{
   { "RECNO() > 6",                         0,          "a range over RECNO(), answered by R_RECNO" },
   { "RECNO() >= 3 .AND. RECNO() <= 7",     "R_NAMENO", "a closed range, walked in name-then-number order" },
   { "NAME = \"beta\" .AND. RECNO() < 6",   "R_RECNO",  "a name term beside a number term, walked in record-number order" }
};

/* ------------------------------------------------------------------- case */

int caseRecnoTag( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name         type  len  dec  nulls */
      { (char *)"ID",   'I',   4,   0,   0 },
      { (char *)"NAME", 'C',  10,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name         expression                      filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"R_RECNO",  (char *)"RECNO()",                0, 0, 0 },
      { (char *)"R_NAMENO", (char *)"NAME + STR(RECNO(), 4)", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   DATA4 *data;
   INDEX4 *index;
   int i, rc;

   sprintf( path, "%s\\RECNOTAG.DBF", outDir );
   printf( "RECNOTAG.DBF (0x30 + CDX, tags over RECNO()) ... " );

   cb->compatibility = 30;

   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create RECNOTAG.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)( i + 1 ) );
      f4assign    ( d4fieldJ( data, 2 ), NAMES[i] );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   index = i4create( data, 0, tags );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create RECNOTAG.cdx" );
   }

   printf( "%d records, 2 tags\n", ROWS );

   rc = finish( cb, data, outDir, "RECNOTAG.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "RECNOTAG.DBF", "RECNOTAG.cdx" );
   if ( rc == 0 )
      rc = dumpQueries( cb, outDir, "RECNOTAG.DBF",
                        QUERIES, (int)( sizeof( QUERIES ) / sizeof( QUERIES[0] ) ) );

   return rc;
}
