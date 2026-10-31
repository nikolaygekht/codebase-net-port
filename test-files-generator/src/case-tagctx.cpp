/* ==========================================================================
 * case-tagctx.cpp — TAGCTX.DBF + TAGCTX.cdx + TAGCTX.fpt
 *
 * The tag-context case in machine order: the tags whose expression is resolved
 * differently because it is parsed for a tag (e4parse.c, the tagPtr branches).
 * CDXEXPR's [tag context] section records the lengths through the parser; this
 * case holds the tags themselves, so the rules are witnessed in stored keys,
 * seeks and query answers. Its GENERAL counterpart is TAGGEN.
 *
 *   - T_MEMO, a memo field that is the whole expression: resolved at the key
 *     size, 240, not at the memo operand's 1024 (e4parse.c:433-457). Rows 5 and
 *     6 agree in their first 240 bytes and differ after, so their keys are
 *     equal and the cut is visible in the stored keys.
 *   - T_NMEMO, the same over a nullable memo: 240 less the null byte, 239. A
 *     memo with content is never null (f4memo.c:801-807), so its null rows are
 *     the empty ones.
 *   - T_ASC and T_DESC, ASCEND and DESCEND of the text field: the identity and
 *     the complement in machine order, beside TAGGEN's collated pair.
 *   - T_TEXT, the plain control, so the query list has a tag that does match.
 *
 * Because the optimizer matches a tag on node lengths (r4log.c:154-158), the
 * reference never answers a memo term from T_MEMO: 1024 against 240. The query
 * list asks it to, and the dump records that it did not.
 *
 * Every tag is built by i4create, none added by i4tagAdd, which skips steps
 * i4create takes (CDX-FORMAT.md section 8.6). Added 2026-10-30, step 012
 * sub-step 5a.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "dump-query.h"
#include "cases.h"

#define ROWS 8           /* records in this table */
#define MEMO_MAX 300     /* the longest memo written */

/* --------------------------------------------------------------- test data */

static const char *TEXTS[ROWS] =
{
   "cafe", "CAFE", "alpha", "Alpha", "zebra", "thorn", "elan", "uber"
};

/* K_MEMO's lengths: empty, short, either side of the cap, and two long ones. */
static const int MEMO_LEN[ROWS]  = { 0, 12, 239, 240, 241, 300, 300, 50 };

/* K_NMEMO's lengths; -1 is null, which for a memo means empty (see above). */
static const int NMEMO_LEN[ROWS] = { -1, 20, 238, 239, 240, -1, 300, 5 };

/* A memo's bytes: one letter per row, then a run of digits, so rows sort by
 * their letter and differ in their length. Rows 5 and 6 share a letter and so
 * their first 240 bytes; the byte after is the row's own. */
static void memoBytes( int row, int len, char *out )
{
   int k;
   char lead = (char)( row == 6 ? 'F' : 'A' + row );

   for ( k = 0; k < len; k++ )
      out[k] = k == 0 ? lead : (char)( '0' + ( k % 10 ) );
   if ( len > 240 )
      out[240] = (char)( 'a' + row );
}

static const QUERYCASE QUERIES[] =
{
   { "K_MEMO = \"F\"",              0,         "a memo term: T_MEMO is not matched, 1024 against 240" },
   { "K_MEMO > \"C\"",              "T_MEMO",  "the same, as a range, walked in the cut keys' order" },
   { "K_NMEMO = \"G\"",             0,         "the nullable memo's tag, likewise" },
   { "K_NMEMO > \"\"",              "T_NMEMO", "the records that are not null, walked in the nullable tag's order" },
   { "K_TEXT = \"cafe\"",           0,         "the control: T_TEXT is matched" },
   { "K_TEXT > \"b\" .AND. K_MEMO > \"C\"", "T_DESC", "a matched term beside an unmatched one, walked in descending text order" }
};

/* ------------------------------------------------------------------- case */

int caseTagCtx( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name           type  len  dec  nulls  */
      { (char *)"ID",      'I',   4,   0,   0      },
      { (char *)"K_TEXT",  'C',  20,   0,   0      },
      { (char *)"K_MEMO",  'M',   4,   0,   0      },
      { (char *)"K_NMEMO", 'M',   4,   0,   r4null },
      { 0, 0, 0, 0, 0 }
   };

   /* name       expression   filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"T_MEMO",  (char *)"K_MEMO",  0, 0, 0 },
      { (char *)"T_NMEMO", (char *)"K_NMEMO", 0, 0, 0 },
      { (char *)"T_TEXT",  (char *)"K_TEXT",  0, 0, 0 },
      { (char *)"T_ASC",   (char *)"ASCEND(K_TEXT)",  0, 0, 0 },
      { (char *)"T_DESC",  (char *)"DESCEND(K_TEXT)", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   char memo[MEMO_MAX];
   DATA4 *data;
   INDEX4 *index;
   int i, rc;

   sprintf( path, "%s\\TAGCTX.DBF", outDir );
   printf( "TAGCTX.DBF (0x30 + CDX + FPT, tags parsed in a tag's context) ... " );

   cb->compatibility = 30;

   c4setCodePage( cb, cp1252 );
   data = d4create( cb, path, fields, 0 );
   c4setCodePage( cb, cp0 );

   if ( data == 0 )
      return fail( cb, "d4create TAGCTX.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      f4assign    ( d4fieldJ( data, 2 ), TEXTS[i] );

      memoBytes( i, MEMO_LEN[i], memo );
      f4memoAssignN( d4fieldJ( data, 3 ), memo, (unsigned)MEMO_LEN[i] );

      if ( NMEMO_LEN[i] < 0 )
         f4assignNull( d4fieldJ( data, 4 ) );
      else
      {
         memoBytes( ROWS - 1 - i, NMEMO_LEN[i], memo );
         f4memoAssignN( d4fieldJ( data, 4 ), memo, (unsigned)NMEMO_LEN[i] );
      }

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   index = i4create( data, 0, tags );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create TAGCTX.cdx" );
   }

   printf( "%d records, 5 tags\n", ROWS );

   rc = finish( cb, data, outDir, "TAGCTX.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "TAGCTX.DBF", "TAGCTX.cdx" );
   if ( rc == 0 )
      rc = dumpQueries( cb, outDir, "TAGCTX.DBF",
                        QUERIES, (int)( sizeof( QUERIES ) / sizeof( QUERIES[0] ) ) );

   return rc;
}
