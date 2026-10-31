/* ==========================================================================
 * case-taggen.cpp — TAGGEN.DBF + TAGGEN.cdx + TAGGEN.fpt
 *
 * The tag-context case under GENERAL: ASCEND and DESCEND of a string, parsed
 * for a tag whose collation adds a byte per character. TAGCTX holds the same
 * pair in machine order.
 *
 *   - G_ASC, ASCEND(K_TEXT): widened to twice the field (e4parse.c:655-672),
 *     and the operand collated into heads, tails and zeros at evaluation time
 *     (e4functi.c:3042-3062).
 *   - G_DESC, DESCEND(K_TEXT): the same, complemented (e4functi.c:3081-3085).
 *   - G_TEXT, the plain field under GENERAL, the control G_ASC must agree with.
 *   - G_MEMO and G_NMEMO, a memo and a nullable memo that are the whole
 *     expression. Under a collation that adds a byte per character the memo is
 *     resolved at half the cap, the expression's length divided by two when it
 *     is at least 239 (i4create.c:1029-1045, i4init.c:466-481): 120 and a key of
 *     240, or 119 and a key of 239 behind the null byte. Rows 5 and 6 agree in
 *     their first 120 bytes and differ after.
 *
 * Building the key copies those bytes: a tag with ASCEND or DESCEND is not
 * collated a second time (E4EXPR.C:671-684). The key length is the widened
 * length, 40, not 80.
 *
 * Every tag is built by i4create under sort4general. i4create parses an ASCEND
 * or DESCEND expression again once the collation is set (i4create.c:1010-1025);
 * i4tagAdd does not, writes key length 20, and the reference then refuses its
 * own file at open (CDX-FORMAT.md section 8.6). That is why this is a table of
 * its own and not three more tags on TAGCTX.
 *
 * The table is marked cp1252, which selects GENERAL's weight array
 * (i4init.c:378-405), and holds an accent and two expansions (ss, ae) so the
 * tails and the two-head path both reach the collated conversion. Text outside
 * ASCII is written as byte arrays, as in case-cdxcoll.cpp.
 *
 * The queries are over ID, which no tag covers, so no optimizer decision is
 * involved: each walks every record in one tag's order.
 *
 * Added 2026-10-31, step 012 sub-step 5a. The data below belongs to this case
 * alone. Other cases keep their own copies on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump-index.h"
#include "dump-query.h"
#include "cases.h"

#define ROWS 8           /* records in this table */
#define MEMO_MAX 200     /* the longest memo written */

/* --------------------------------------------------------------- test data
 *
 * Every byte run is its commented text in cp1252.
 */

static const unsigned char T_CAFE[]    = { 'c','a','f','e', 0 };              /* cafe   */
static const unsigned char T_CAFE_UP[] = { 'C','A','F','E', 0 };              /* CAFE   */
static const unsigned char T_CAFE_AC[] = { 'c','a','f',0xE9, 0 };             /* café   */
static const unsigned char T_ALPHA[]   = { 'a','l','p','h','a', 0 };          /* alpha  */
static const unsigned char T_ALPHA_UP[]= { 'A','l','p','h','a', 0 };          /* Alpha  */
static const unsigned char T_STRASSE[] = { 's','t','r','a',0xDF,'e', 0 };     /* straße */
static const unsigned char T_ZEBRA[]   = { 'z','e','b','r','a', 0 };          /* zebra  */
static const unsigned char T_AEON[]    = { 0xE6,'o','n', 0 };                 /* æon    */

static const unsigned char *TEXTS[ROWS] =
{
   T_CAFE, T_CAFE_UP, T_CAFE_AC, T_ALPHA, T_ALPHA_UP, T_STRASSE, T_ZEBRA, T_AEON
};

/* K_MEMO's lengths: empty, short, either side of the halved cap, two long ones
 * sharing their first 120 bytes. K_NMEMO's; -1 is null, which for a memo means
 * empty (f4memo.c:801-807). */
static const int MEMO_LEN[ROWS]  = { 0, 5, 119, 120, 121, 200, 200, 50 };
static const int NMEMO_LEN[ROWS] = { -1, 20, 118, 119, 120, -1, 200, 5 };

/* A memo's bytes: a lead per row, then digits. Row 2's lead is an accented
 * letter (cp1252 0xC9, É) so the collation has a tail to write; rows 5 and 6
 * share a lead and so their first 120 bytes, and the byte after is the row's
 * own. */
static void memoBytes( int row, int len, char *out )
{
   int k;
   char lead = (char)( row == 2 ? 0xC9 : row == 6 ? 'F' : 'A' + row );

   for ( k = 0; k < len; k++ )
      out[k] = k == 0 ? lead : (char)( '0' + ( k % 10 ) );
   if ( len > 120 )
      out[120] = (char)( 'a' + row );
}

static const QUERYCASE QUERIES[] =
{
   { "ID >= 0", "G_DESC", "every record, walked in GENERAL descending order" },
   { "ID >= 0", "G_ASC",  "every record, walked in the collated ascending conversion's order" },
   { "ID >= 0", "G_TEXT", "every record, walked in the plain GENERAL tag's order" }
};

/* ------------------------------------------------------------------- case */

int caseTagGen( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name           type  len  dec  nulls  */
      { (char *)"ID",      'I',   4,   0,   0 },
      { (char *)"K_TEXT",  'C',  20,   0,   0 },
      { (char *)"K_MEMO",  'M',   4,   0,   0 },
      { (char *)"K_NMEMO", 'M',   4,   0,   r4null },
      { 0, 0, 0, 0, 0 }
   };

   /* name       expression   filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"G_TEXT", (char *)"K_TEXT",          0, 0, 0 },
      { (char *)"G_ASC",  (char *)"ASCEND(K_TEXT)",  0, 0, 0 },
      { (char *)"G_DESC", (char *)"DESCEND(K_TEXT)", 0, 0, 0 },
      { (char *)"G_MEMO", (char *)"K_MEMO",          0, 0, 0 },
      { (char *)"G_NMEMO",(char *)"K_NMEMO",         0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   char memo[MEMO_MAX];
   DATA4 *data;
   INDEX4 *index;
   int i, rc;

   sprintf( path, "%s\\TAGGEN.DBF", outDir );
   printf( "TAGGEN.DBF (0x30 + CDX + FPT, ASCEND, DESCEND and memo under GENERAL) ... " );

   cb->compatibility = 30;

   c4setCodePage( cb, cp1252 );
   data = d4create( cb, path, fields, 0 );
   c4setCodePage( cb, cp0 );

   if ( data == 0 )
      return fail( cb, "d4create TAGGEN.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      f4assign    ( d4fieldJ( data, 2 ), (const char *)TEXTS[i] );

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

   c4setCollatingSequence( cb, sort4general );
   index = i4create( data, 0, tags );
   c4setCollatingSequence( cb, sort4machine );

   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create TAGGEN.cdx" );
   }

   printf( "%d records, 5 tags\n", ROWS );

   rc = finish( cb, data, outDir, "TAGGEN.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "TAGGEN.DBF", "TAGGEN.cdx" );
   if ( rc == 0 )
      rc = dumpQueries( cb, outDir, "TAGGEN.DBF",
                        QUERIES, (int)( sizeof( QUERIES ) / sizeof( QUERIES[0] ) ) );

   return rc;
}
