/* ==========================================================================
 * dump-query.cpp — the query-optimizer oracle. See dump-query.h for what is
 * recorded and why every filter is run twice.
 *
 * FOUR MECHANICS, none of them obvious:
 *   - relate4querySet only STORES the string; relate4top is what parses it and
 *     builds the bitmap (r4relate.c:4058-4104, 6036-6043). So nothing is
 *     readable until after relate4top, and a filter that is not r4log fails
 *     there rather than at querySet.
 *   - relation->log.expr and log.infoReport are index-parallel and only exist
 *     once relate4top has run. infoReport[i].tag is a TAG4FILE*, whose name is
 *     tfile4alias (d4declar.h:2556).
 *   - relate->set.flags is null when nothing was optimized, and a null bitmap
 *     means "every record is a candidate" (r4relate.c:82-91). "absent" and
 *     "every bit set" are different states and the dump keeps them apart.
 *   - the master's order comes from the DATA4's selected tag, picked up into
 *     relate->dataTag at relate4top (r4relate.c:2239-2241) -- so a tag must be
 *     selected before relate4top, not after.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-query.h"

/* A returned record set. Sized for the largest corpus table this runs on;
 * overflow is reported rather than silently truncated, because a truncated
 * expectation is worse than none. */
#define MAX4QRECS   4096

typedef struct
{
   long  recs[MAX4QRECS];
   int   count;
   int   overflowed;
} RECSET;

static void recSetInit( RECSET *s )
{
   s->count = 0;
   s->overflowed = 0;
}

static void recSetAdd( RECSET *s, long rec )
{
   if ( s->count >= MAX4QRECS )
   {
      s->overflowed = 1;
      return;
   }
   s->recs[s->count++] = rec;
}

static int recSetSame( const RECSET *a, const RECSET *b )
{
   int i;

   if ( a->overflowed || b->overflowed )
      return 0;
   if ( a->count != b->count )
      return 0;
   for ( i = 0; i < a->count; i++ )
      if ( a->recs[i] != b->recs[i] )
         return 0;
   return 1;
}

/* Record numbers, sixteen to a line, each line tagged so a reader can
 * accumulate until it has `count` of them. */
static void dumpRecSet( FILE *fp, const char *lead, const char *order, const RECSET *s )
{
   int i;

   fprintf( fp, "  %-12s order=%s count=%d%s\n", lead, order, s->count,
            s->overflowed ? " OVERFLOWED" : "" );
   for ( i = 0; i < s->count; i++ )
   {
      if ( ( i % 16 ) == 0 )
         fprintf( fp, "    recs" );
      fprintf( fp, " %ld", s->recs[i] );
      if ( ( i % 16 ) == 15 || i == s->count - 1 )
         fputc( '\n', fp );
   }
}

/* The bitset as the optimizer left it. isFlip is dumped beside the raw bits
 * rather than folded into them: a set stored as its own complement is a
 * different decision from the same set stored directly, and depth 3 compares
 * the decision. */
static void dumpFlagSet( FILE *fp, const char *lead, F4FLAG *set, long records )
{
   long rec;
   int raw = 0;

   if ( set->flags == 0 )
   {
      fprintf( fp, "  %-12s absent\n", lead );
      return;
   }

   for ( rec = 1; rec <= records; rec++ )
      if ( f4flagIsSet( set, (unsigned long)rec ) )
         raw++;

   fprintf( fp, "  %-12s present numFlags=%lu isFlip=%d rawSet=%d\n",
            lead, set->numFlags, (int)set->isFlip, raw );

   {
      int printed = 0;

      for ( rec = 1; rec <= records; rec++ )
      {
         if ( !f4flagIsSet( set, (unsigned long)rec ) )
            continue;
         if ( ( printed % 16 ) == 0 )
            fprintf( fp, "    bits" );
         fprintf( fp, " %ld", rec );
         printed++;
         if ( ( printed % 16 ) == 0 )
            fputc( '\n', fp );
      }
      if ( ( printed % 16 ) != 0 )
         fputc( '\n', fp );
   }
}

/* Is this bitset the same decision as that one -- same shape, same flip, same
 * bits? Used only to answer the dump's own question about whether the set
 * survives being walked. */
static int flagSetSame( F4FLAG *a, F4FLAG *b, long records )
{
   long rec;

   if ( ( a->flags == 0 ) != ( b->flags == 0 ) )
      return 0;
   if ( a->flags == 0 )
      return 1;
   if ( a->numFlags != b->numFlags || a->isFlip != b->isFlip )
      return 0;
   for ( rec = 1; rec <= records; rec++ )
      if ( f4flagIsSet( a, (unsigned long)rec ) != f4flagIsSet( b, (unsigned long)rec ) )
         return 0;
   return 1;
}

/* The compiled filter, node by node, with the tag the optimizer matched to
 * each. The node fields are the same six dump-expr.cpp records, so the port
 * lines the two arrays up by index and compares only the tag column here. */
static void dumpNodesWithTags( FILE *fp, L4LOGICAL *log )
{
   int i;

   if ( log->expr == 0 )
   {
      fprintf( fp, "  nodes        none\n" );
      return;
   }

   fprintf( fp, "  nodes        %d\n", log->expr->infoN );
   for ( i = 0; i < log->expr->infoN; i++ )
   {
      E4INFO *info = log->expr->info + i;
      TAG4FILE *tag = ( log->infoReport == 0 ) ? 0 : log->infoReport[i].tag;

      fprintf( fp, "    node %2d row=%d len=%d numParms=%d numEntries=%d fieldNo=%d tag=",
               i, info->functionI, info->len, info->numParms,
               info->numEntries, (int)info->fieldNo );
      if ( tag == 0 )
         fprintf( fp, "-\n" );
      else
         fprintf( fp, "\"%s\"\n", tfile4alias( tag ) );
   }
}

/* Walk a prepared relation from the top, collecting record numbers. */
static int collect( RELATE4 *relate, DATA4 *data, RECSET *out )
{
   int rc;

   recSetInit( out );

   rc = relate4top( relate );
   while ( rc == r4success )
   {
      recSetAdd( out, d4recNo( data ) );
      rc = relate4skip( relate, 1L );
   }

   return ( rc == r4eof || rc == r4success ) ? 0 : rc;
}

int dumpQueries( CODE4 *cb, const char *outDir, const char *dbfName,
                 const QUERYCASE *cases, int numCases )
{
   char path[520];
   char stem[260];
   char *dot;
   DATA4 *data;
   FILE *fp;
   long records;
   int i;
   int rc = 0;

   strcpy( stem, dbfName );
   dot = strrchr( stem, '.' );
   if ( dot != 0 )
      *dot = '\0';

   sprintf( path, "%s\\%s.DBF", outDir, stem );
   data = d4open( cb, path );
   if ( data == 0 )
      return fail( cb, "d4open for query dump" );

   sprintf( path, "%s\\%s.query.dump.txt", outDir, stem );
   fp = fopen( path, "wb" );
   if ( fp == 0 )
   {
      d4close( data );
      fprintf( stderr, "  ERROR: cannot write %s\n", path );
      return 1;
   }

   records = d4recCount( data );

   fprintf( fp, "# CodeBase.NET corpus query dump\n" );
   fprintf( fp, "# Generated by test-files-generator from the original CodeBase C library.\n" );
   fprintf( fp, "# Do not edit by hand; regenerate instead.\n" );
   fprintf( fp, "table        %s.DBF\n", stem );
   fprintf( fp, "numRecs      %ld\n", records );
   fprintf( fp, "useGeneral   %d   (CODE4.useGeneralTagsInRelate, r4log.c:115-120)\n",
            (int)cb->useGeneralTagsInRelate );
   fprintf( fp, "errUnique    %d   (CODE4.errDefaultUnique, c4code.c:984)\n",
            (int)cb->errDefaultUnique );

   fprintf( fp, "\n[query cases]\ncount        %d\n", numCases );

   for ( i = 0; i < numCases; i++ )
   {
      const QUERYCASE *c = cases + i;
      RELATE4 *relate;
      RECSET optimized, byTag, scanned;
      F4FLAG setAtTop;
      unsigned long counted = 0;
      int haveSetAtTop = 0;
      int agrees;

      fprintf( fp, "\nquery %d ", i );
      dumpEscapedBytes( fp, c->text, (unsigned long)strlen( c->text ) );
      if ( c->note != 0 )
         fprintf( fp, "   ; %s", c->note );
      fputc( '\n', fp );

      if ( error4code( cb ) != 0 )
         error4set( cb, 0 );

      /* ---- run 1: optimized, no tag selected -> record-number order ---- */

      d4tagSelect( data, 0 );

      relate = relate4init( data );
      if ( relate == 0 )
      {
         fprintf( fp, "  init         FAILED errorCode=%d\n", (int)error4code( cb ) );
         error4set( cb, 0 );
         continue;
      }

      if ( relate4querySet( relate, c->text ) < 0 )
      {
         fprintf( fp, "  querySet     FAILED errorCode=%d\n", (int)error4code( cb ) );
         error4set( cb, 0 );
         relate4free( relate, 0 );
         continue;
      }

      /* relate4top is where the filter is parsed and the bitmap is built, so
       * the set is read immediately after it and again after the walk. */
      recSetInit( &optimized );
      {
         int step = relate4top( relate );

         if ( step < 0 )
         {
            fprintf( fp, "  top          FAILED rc=%d errorCode=%d\n", step, (int)error4code( cb ) );
            error4set( cb, 0 );
            relate4free( relate, 0 );
            continue;
         }

         setAtTop = relate->set;
         haveSetAtTop = 1;

         while ( step == r4success )
         {
            recSetAdd( &optimized, d4recNo( data ) );
            step = relate4skip( relate, 1L );
         }
      }

      fprintf( fp, "  fullyMapped  %d\n", (int)relate->relation->fullyMapped );
      fprintf( fp, "  bitmapsFreed %d\n", (int)relate->relation->bitmapsFreed );
      dumpNodesWithTags( fp, &relate->relation->log );
      if ( haveSetAtTop )
         dumpFlagSet( fp, "set", &setAtTop, records );
      dumpFlagSet( fp, "setAfterWalk", &relate->set, records );
      if ( haveSetAtTop )
         fprintf( fp, "  setStable    %s\n",
                  flagSetSame( &setAtTop, &relate->set, records ) ? "yes" : "NO" );

      dumpRecSet( fp, "optimized", "record", &optimized );

      counted = relate4count( relate );
      fprintf( fp, "  count        %lu\n", counted );

      relate4free( relate, 0 );

      /* ---- run 2: optimized, with a tag selected -> tag order ---- */

      recSetInit( &byTag );
      if ( c->tagName != 0 )
      {
         TAG4 *tag = d4tag( data, c->tagName );

         if ( tag == 0 )
         {
            fprintf( fp, "  byTag        NO SUCH TAG \"%s\"\n", c->tagName );
            error4set( cb, 0 );
         }
         else
         {
            d4tagSelect( data, tag );
            relate = relate4init( data );
            if ( relate != 0 && relate4querySet( relate, c->text ) >= 0 &&
                 collect( relate, data, &byTag ) == 0 )
            {
               fprintf( fp, "  byTagName    \"%s\"\n", c->tagName );
               dumpRecSet( fp, "byTag", "tag", &byTag );
            }
            else
            {
               fprintf( fp, "  byTag        FAILED errorCode=%d\n", (int)error4code( cb ) );
               error4set( cb, 0 );
            }
            if ( relate != 0 )
               relate4free( relate, 0 );
            d4tagSelect( data, 0 );
         }
      }

      /* ---- run 3: the same filter with the optimizer disabled ---- *
       *
       * bitmapDisable makes bitmap4init return 0 at once (m4map.c:341), after
       * which log4true re-evaluates every conjunct per record and a null
       * bitmap admits every record as a candidate. What comes out is what the
       * filter MEANS to the reference, with the optimizer out of the circuit. */

      if ( error4code( cb ) != 0 )
         error4set( cb, 0 );

      relate = relate4init( data );
      recSetInit( &scanned );
      if ( relate == 0 )
      {
         fprintf( fp, "  scan         INIT FAILED errorCode=%d\n", (int)error4code( cb ) );
         error4set( cb, 0 );
         continue;
      }

      relate->relation->bitmapDisable = 1;

      if ( relate4querySet( relate, c->text ) < 0 || collect( relate, data, &scanned ) != 0 )
      {
         fprintf( fp, "  scan         FAILED errorCode=%d\n", (int)error4code( cb ) );
         error4set( cb, 0 );
         relate4free( relate, 0 );
         continue;
      }

      dumpRecSet( fp, "scan", "record", &scanned );
      relate4free( relate, 0 );

      /* The line the port's gate reads. Where these disagree the reference has
       * demonstrated its own defect on this filter, and the port is measured
       * against the scan rather than against the optimized answer. */
      agrees = recSetSame( &optimized, &scanned );
      fprintf( fp, "  agree        %s\n", agrees ? "yes" : "NO" );
   }

   fprintf( fp, "\n[end]\n" );

   printf( "  dump -> %s.query.dump.txt  (%d filters)\n", stem, numCases );

   fclose( fp );
   if ( d4close( data ) < 0 )
      rc = fail( cb, "d4close after query dump" );

   return rc;
}
