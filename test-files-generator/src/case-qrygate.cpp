/* ==========================================================================
 * case-qrygate.cpp — QRYGATE.DBF + QRYGATE.cdx
 *
 * The query-refusal case: three tags the reference's optimizer refuses, each
 * the ONLY tag over its own field, and beside each a control field holding the
 * same values under a plain tag.
 *
 *   Q_UNIQ  over U_TEXT, unique     -- refused (r4log.c:144)
 *   Q_FILT  over F_NUM,  filtered   -- refused (r4log.c:144)
 *   Q_GEN   over G_TEXT, GENERAL    -- refused in the source (r4log.c:114-120),
 *                                      but compiled out (d4defs.h:669-673): USED
 *   Q_UCTL, Q_FCTL, Q_GCTL           -- the plain controls
 *
 * Why it exists. The optimizer walks tags in directory order, which is
 * alphabetical, and takes the first that matches (r4log.c). In CDXBASE the
 * unique tag T_UNIQ shares its field with T_DUP, which sorts first, so
 * T_UNIQ is never examined and nothing shows the unique refusal; the filtered
 * refusal is visible there only because T_FILT happens to sort before T_NUM.
 * Here a refusal shows as "no tag" whatever the walk order, and
 * the control shows what the same filter gets from a tag that is allowed.
 *
 * Each of the three would give a WRONG answer if used: the unique tag holds
 * one entry per value of a field with duplicates, the filtered tag omits the
 * records its filter rejects, and GENERAL orders keys case-insensitively where
 * the filter compares bytes. The reference refuses the first two and USES the
 * third -- its GENERAL filters disagree with its own scan in this dump. The
 * port refuses all three (ADR-59).
 *
 * The table is marked cp1252 so GENERAL is built over its cp1252 array, as in
 * CDXCOLL. Its values are ASCII; only their case matters to GENERAL here.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump-index.h"
#include "dump-query.h"
#include "cases.h"

#define ROWS 24          /* records in this table */

/* --------------------------------------------------------------- test data */

/* Duplicates on purpose: a unique tag keeps one entry per value. */
static const char *const UNIQS[8] =
{
   "APPLE", "CHERRY", "CHERRY", "MANGO", "PEAR", "PEAR", "ZEBRA", "APPLE"
};

/* Case variants on purpose: GENERAL folds them, byte order does not. */
static const char *const GENS[8] =
{
   "alpha", "ALPHA", "Beta", "beta", "mango", "Mango", "zebra", "Zebra"
};

/* 0..6 repeating; the filtered tag leaves out every 3. */
static long numFor( int i ) { return (long)( i % 7 ); }

static const QUERYCASE QUERIES[] =
{
   /* -- unique: the lone tag is refused, the control is used ---------------- */
   { "U_TEXT = \"CHERRY\"",  0,        "a lone UNIQUE tag -- refused, so no tag" },
   { "U_CTRL = \"CHERRY\"",  0,        "the same filter on the control -- its plain tag" },
   { "U_TEXT > \"M\"",       0,        "a range over the lone UNIQUE tag" },
   { "U_CTRL > \"M\"",       "Q_UCTL", "the control's range, walked in its order" },

   /* -- filtered: refused even where its filter would not matter ------------ */
   { "F_NUM > 2",            0,        "a lone filtered tag -- refused" },
   { "F_CTRL > 2",           0,        "the control" },
   { "F_NUM = 3",            0,        "exactly the value the tag's filter leaves out" },
   { "F_CTRL = 3",           0,        "the control" },

   /* -- GENERAL: used by the reference, though it orders differently ---------- */
   { "G_TEXT > \"M\"",       0,        "a lone GENERAL tag -- the reference uses it and is wrong; the port refuses it" },
   { "G_CTRL > \"M\"",       0,        "the control, machine order" },
   { "G_TEXT = \"beta\"",    0,        "eq over the lone GENERAL tag -- wrong in the reference too" },
   { "G_CTRL = \"beta\"",    0,        "the control" },

   /* -- one of each beside a usable term ------------------------------------ */
   { "U_TEXT = \"PEAR\" .AND. U_CTRL = \"PEAR\"", 0,
                                       "a refused term and an allowed one over equal data" }
};

/* ------------------------------------------------------------------- case */

int caseQryGate( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"U_TEXT", 'C',  10,   0,   0 },
      { (char *)"U_CTRL", 'C',  10,   0,   0 },
      { (char *)"F_NUM",  'N',   4,   0,   0 },
      { (char *)"F_CTRL", 'N',   4,   0,   0 },
      { (char *)"G_TEXT", 'C',  10,   0,   0 },
      { (char *)"G_CTRL", 'C',  10,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name        expression    filter              unique             descending */
   static TAG4INFO tags[] =
   {
      { (char *)"Q_UNIQ", (char *)"U_TEXT", 0,                 r4uniqueContinue,  0 },
      { (char *)"Q_UCTL", (char *)"U_CTRL", 0,                 0,                 0 },
      { (char *)"Q_FILT", (char *)"F_NUM",  (char *)"F_NUM <> 3", 0,              0 },
      { (char *)"Q_FCTL", (char *)"F_CTRL", 0,                 0,                 0 },
      { (char *)"Q_GCTL", (char *)"G_CTRL", 0,                 0,                 0 },
      { 0, 0, 0, 0, 0 }
   };

   static TAG4INFO generalTag[] =
   {
      { (char *)"Q_GEN", (char *)"G_TEXT", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   DATA4 *data;
   INDEX4 *index;
   int i, rc;

   sprintf( path, "%s\\QRYGATE.DBF", outDir );
   printf( "QRYGATE.DBF (0x30 + CDX, the query refusals isolated) ... " );

   cb->compatibility = 30;

   c4setCodePage( cb, cp1252 );
   data = d4create( cb, path, fields, 0 );
   c4setCodePage( cb, cp0 );

   if ( data == 0 )
      return fail( cb, "d4create QRYGATE.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      f4assign    ( d4fieldJ( data, 2 ), UNIQS[ i % 8 ] );
      f4assign    ( d4fieldJ( data, 3 ), UNIQS[ i % 8 ] );
      f4assignLong( d4fieldJ( data, 4 ), numFor( i ) );
      f4assignLong( d4fieldJ( data, 5 ), numFor( i ) );
      f4assign    ( d4fieldJ( data, 6 ), GENS[ i % 8 ] );
      f4assign    ( d4fieldJ( data, 7 ), GENS[ i % 8 ] );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   /* The unique tag's duplicates leave r4uniqueContinue in errorCode, a status
    * rather than a failure (r4reinde.c:2100-2102); cleared as CDXBASE does. */
   index = i4create( data, 0, tags );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create QRYGATE.cdx" );
   }
   if ( error4code( cb ) > 0 )
      error4set( cb, 0 );

   /* GENERAL is a CODE4 setting read when a tag is created, so the collated tag
    * is added on its own, as in CDXCOLL. */
   c4setCollatingSequence( cb, sort4general );
   rc = i4tagAdd( index, generalTag );
   c4setCollatingSequence( cb, sort4machine );

   if ( rc < 0 )
   {
      d4close( data );
      return fail( cb, "i4tagAdd Q_GEN" );
   }

   printf( "%d records, 6 tags\n", ROWS );

   rc = finish( cb, data, outDir, "QRYGATE.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "QRYGATE.DBF", "QRYGATE.cdx" );
   if ( rc == 0 )
      rc = dumpQueries( cb, outDir, "QRYGATE.DBF",
                        QUERIES, (int)( sizeof( QUERIES ) / sizeof( QUERIES[0] ) ) );

   return rc;
}
