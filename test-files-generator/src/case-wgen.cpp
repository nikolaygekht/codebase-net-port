/* ==========================================================================
 * case-wgen.cpp — WGEN.DBF + WGEN.cdx
 *
 * Collated Unicode keys: tags over CodeBase's 'W' and 'O' fields under a
 * head-and-tail collation (step 013, sub-step 7a-12). VFP has neither field.
 *
 * What it pins down:
 *
 *   1. The table (collate4setupUnicodeFromChar, u4util.c:2700-2793): code
 *      units 0-255 take the byte table's entry of the same number, whatever
 *      character that byte is in the code page; every unit from 256 up weighs
 *      as itself with no tail. So U+009C, a C1 control, expands to OE through
 *      cp1252's byte 0x9C, while U+0153, the real oe, keys as itself.
 *   2. The transform (t4convertSubSortCompressUnicode, i4conv.c:484-583):
 *      big-endian heads, then tails as 00 tt, twice the value's bytes, and
 *      only trailing U+0020 dropped -- the U+2000 CodeBase blanks a wide field
 *      with (F4FIELD.C:92-108) is weighed like any character.
 *   3. The header names, by the explicit collation name (i4tag.c:3024-3061):
 *      GENERAL for cp1252's own, CB00008 for Avaya, CB00007 for GENERAL cp850
 *      by name on this cp1252 table. Added by i4tagAdd, which reads the
 *      character setting for a wide tag -- see the loop below.
 *
 * A simple collation over Unicode is "not implemented yet" in the reference
 * (i4init.c:659), so none is here.
 *
 * The data below belongs to this case alone -- see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define UNIT_ROWS 255    /* one row per code unit U+0001 to U+00FF */

/* ---------------------------------------------------------------- the words
 *
 * Each spelled as code units, NUL-ended, so no source encoding can change them. */
static const WSTR5 W00[] = { 'A', 'L', 'P', 'H', 'A', 0 };
static const WSTR5 W01[] = { 'a', 'l', 'p', 'h', 'a', 0 };
static const WSTR5 W02[] = { 'A', 'l', 'p', 'h', 'a', 0 };
static const WSTR5 W03[] = { 'c', 'a', 'f', 0x00E9, 0 };                     /* café */
static const WSTR5 W04[] = { 'c', 'a', 'f', 'e', 0 };
static const WSTR5 W05[] = { 'C', 'A', 'F', 0x00C9, 0 };                     /* CAFÉ */
static const WSTR5 W06[] = { 0x00E6, 'o', 'n', 0 };                          /* æon: U+00E6 expands */
static const WSTR5 W07[] = { 'a', 'e', 'o', 'n', 0 };
static const WSTR5 W08[] = { 0x0153, 'u', 'v', 'r', 'e', 0 };                /* œuvre with U+0153: itself */
static const WSTR5 W09[] = { 0x009C, 'u', 'v', 'r', 'e', 0 };                /* the same with U+009C: OE */
static const WSTR5 W10[] = { 'O', 'E', 'u', 'v', 'r', 'e', 0 };
static const WSTR5 W11[] = { 's', 't', 'r', 'a', 0x00DF, 'e', 0 };           /* straße */
static const WSTR5 W12[] = { 'S', 'T', 'R', 'A', 'S', 'S', 'E', 0 };
static const WSTR5 W13[] = { 0x00FE, 'o', 'r', 'n', 0 };                     /* þorn */
static const WSTR5 W14[] = { 0x0416, 0x0430, 0x0440, 0 };                    /* Cyrillic: itself */
static const WSTR5 W15[] = { 0x4E2D, 0x6587, 0 };                            /* CJK: itself */
static const WSTR5 W16[] = { 0 };                                            /* empty: blanked to U+2000 */
static const WSTR5 W17[] = { 'A', 'B', 'C', ' ', ' ', 0 };                   /* real spaces, dropped */
static const WSTR5 W18[] = { 'A', 'B', 'C', 0 };                             /* U+2000 tail, weighed */
static const WSTR5 W19[] = { 'A', 0x2000, 'B', 0 };                          /* the blank inside */
static const WSTR5 W20[] = { 'a', 0xD83D, 0xDE00, 'b', 0 };                  /* a surrogate pair */
static const WSTR5 W21[] = { 0xFFFF, 0 };
static const WSTR5 W22[] = { 0x00C6, 0x00C6, 0x00C6, 0x00C6, 0x00C6, 0x00C6, 0x00C6, 0x00C6, 0x00C6, 0x00C6, 0 };
                                                                             /* ten Æ: every head and tail at once */

/* Row (UNIT_ROWS + i)'s string, or 0 for the row left blank. */
static const WSTR5 *WORDS[] =
{
   W00, W01, W02, W03, W04, W05, W06, W07, W08, W09, W10, W11, W12, W13,
   W14, W15, W16, 0, W17, W18, W19, W20, W21, W22
};

#define WORD_ROWS ( (int)( sizeof( WORDS ) / sizeof( WORDS[0] ) ) )

/* ------------------------------------------------------------------- tags */

typedef struct
{
   const char *name;
   const char *expression;
   short descending;
   Collate4name collateNameUnicode;
} WTAG;

/* ------------------------------------------------------------------- case */

int caseWgen( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name         type  len  dec  nulls */
      { (char *)"ID",   'I',   4,   0,   0 },
      { (char *)"K_W",  'W',  20,   0,   0 },
      { (char *)"K_O",  'O',  12,   0,   0 },   /* stored 14: two more for the count */
      { 0, 0, 0, 0, 0 }
   };

   static TAG4INFO machineTag[] =
   {
      { (char *)"W_MACH", (char *)"K_W", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   static const WTAG collated[] =
   {
      /* No descending tag: with one, the reference itself dies with an access
       * violation while this case is dumped (step 013, 7a-12) -- the descending
       * walk is already gated by WSTR's W_DESC and by CDXDEEP's D_PFX. */
      { "W_GEN",   "K_W", 0,            collate4generalCp1252 },
      { "W_AVAYA", "K_W", 0,            collate4avaya1252 },
      { "W_G850",  "K_W", 0,            collate4generalCp850 },
      { "O_GEN",   "K_O", 0,            collate4generalCp1252 },
   };

   TAG4INFO tagInfo[2];
   char path[520];
   WSTR5 one[2];
   DATA4 *data;
   INDEX4 *index;
   int i, rc;

   sprintf( path, "%s\\WGEN.DBF", outDir );
   printf( "WGEN.DBF (0x30 + CDX, cp1252, collated W and O keys) ... " );

   cb->compatibility = 30;

   /* cp1252 is what makes collate4generalCp1252 write "GENERAL" rather than
    * "CB00002" (i4tag.c:3044-3050). Restored right after the create. */
   c4setCodePage( cb, cp1252 );
   data = d4create( cb, path, fields, 0 );
   c4setCodePage( cb, cp0 );

   if ( data == 0 )
      return fail( cb, "d4create WGEN.DBF" );

   for ( i = 0; i < UNIT_ROWS + WORD_ROWS; i++ )
   {
      const WSTR5 *value;

      if ( i < UNIT_ROWS )
      {
         one[0] = (WSTR5)( i + 1 );
         one[1] = 0;
         value = one;
      }
      else
         value = WORDS[i - UNIT_ROWS];

      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)( i + 1 ) );

      if ( value != 0 )
      {
         f4assignWideString( d4fieldJ( data, 2 ), value );
         f4assignWideString( d4fieldJ( data, 3 ), value );
      }

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   index = i4create( data, 0, machineTag );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create WGEN.cdx" );
   }

   for ( i = 0; i < (int)( sizeof( collated ) / sizeof( collated[0] ) ); i++ )
   {
      memset( tagInfo, 0, sizeof( tagInfo ) );
      tagInfo[0].name = (char *)collated[i].name;
      tagInfo[0].expression = (char *)collated[i].expression;
      tagInfo[0].descending = collated[i].descending;

      /* The CHARACTER setting, though the tag is wide: i4tagAdd chooses between
       * collateName and collateNameUnicode by t4->isUnicode (i4add.c:746-750),
       * which nothing has set yet -- tfile4initSeekConv sets it later
       * (i4init.c:625). i4create sets it first (i4create.c:981-983). The keys
       * are still built by the Unicode transform, which tfile4initSeekConv
       * chooses from the expression's type; the dump's own open checks the
       * key length the file records. */
      cb->collateName = collated[i].collateNameUnicode;
      rc = i4tagAdd( index, tagInfo );
      cb->collateName = collate4none;   /* the default (C4CODE.C:1075) */

      if ( rc < 0 )
      {
         d4close( data );
         return fail( cb, "i4tagAdd" );
      }
   }

   printf( "%d records, %d tags\n", UNIT_ROWS + WORD_ROWS,
           1 + (int)( sizeof( collated ) / sizeof( collated[0] ) ) );

   rc = finish( cb, data, outDir, "WGEN.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "WGEN.DBF", "WGEN.cdx" );

   return rc;
}
