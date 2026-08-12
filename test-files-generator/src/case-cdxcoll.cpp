/* ==========================================================================
 * case-cdxcoll.cpp — CDXCOLL.DBF + CDXCOLL.cdx
 *
 * The collation case: **one character field indexed twice**, machine order and
 * GENERAL, in the same index file. The same 32 values therefore produce two
 * different key encodings a few hundred bytes apart, which is the sharpest
 * contrast the format allows.
 *
 * Why it exists. KEY-COLLATION.md §3.7 records that the GENERAL head+tail key
 * layout is verified *from source only* — not one of the 33 sample CDX files
 * shipped in original/examples/DATA carries a "GENERAL" sortSeq, so nothing in
 * this repository has ever confirmed it against real bytes (risk R11). This case
 * is those bytes. It also pins three things machine order cannot reach:
 *
 *   - keyLen is **2x the field width** (keySizeCharPerCharAdd, i4create.c:1040),
 *     so a reader cannot assume a key is as wide as its field;
 *   - pChar is **'\0' on a character tag** (i4init.c:596-604), which is exactly
 *     what a wrong pad-character assumption corrupts (ADR-26, ADR-27);
 *   - accents share a head byte with their base letter and differ in the tail
 *     block, and oe/ss/th expansions produce two head bytes from one character.
 *
 * How the two tags end up with different collations. The collation is chosen per
 * tag from CODE4 state at create time (i4create.c:979-1007, i4tag.c:2915-2924),
 * so one create cannot mix them: C_MACH is created with collatingSequence
 * sort4machine and C_GEN is added afterwards with sort4general. That also gives
 * the corpus its one tag built by the add-a-tag path (i4add.c:1014-1035) rather
 * than by a whole-file build.
 *
 * The table is marked cp1252 because GENERAL's array is selected by **the data
 * file's** code page (i4init.c:378-405): cp1252 and cp437 and cp850 each get
 * their own, cp1250 is refused outright. An unmarked table would reach the same
 * cp1252 array by default and prove less.
 *
 * Text outside ASCII is written as byte arrays, never as source literals — what
 * a literal becomes depends on the compiler's charsets, and this case is about
 * the exact bytes.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define ROWS 32          /* records in this table */

/* --------------------------------------------------------------- test data
 *
 * Every byte run below is its commented text encoded in cp1252. The rows are
 * grouped so that each accent, case variant and expansion sits next to the
 * plain spelling it collates with — under GENERAL they land together, under
 * machine order they are scattered.
 */

static const unsigned char C_ALPHA_U[]  = { 'A','L','P','H','A' };                    /* "ALPHA" */
static const unsigned char C_ALPHA_L[]  = { 'a','l','p','h','a' };                    /* "alpha" */
static const unsigned char C_ALPHA_M[]  = { 'A','l','p','h','a' };                    /* "Alpha" */
static const unsigned char C_EMPTY[1]   = { 0 };                                      /* used with length 0 */
static const unsigned char C_CAFE_L[]   = { 'c','a','f','e' };                        /* "cafe" */
static const unsigned char C_CAFE_LA[]  = { 'c','a','f',0xE9 };                       /* "café" */
static const unsigned char C_CAFE_U[]   = { 'C','A','F','E' };                        /* "CAFE" */
static const unsigned char C_CAFE_UA[]  = { 'C','A','F',0xC9 };                       /* "CAFÉ" */
static const unsigned char C_AETH_1[]   = { 0xC6,'t','h','e','r' };                   /* "Æther" */
static const unsigned char C_AETH_2[]   = { 'A','E','t','h','e','r' };                /* "AEther" */
static const unsigned char C_AEON_1[]   = { 0xE6,'o','n' };                           /* "æon" */
static const unsigned char C_AEON_2[]   = { 'a','e','o','n' };                        /* "aeon" */
static const unsigned char C_OEUV_1[]   = { 0x9C,'u','v','r','e' };                   /* "œuvre" */
static const unsigned char C_OEUV_2[]   = { 'O','E','u','v','r','e' };                /* "OEuvre" */
static const unsigned char C_OEUV_3[]   = { 0x8C,'u','v','r','e' };                   /* "Œuvre" */
static const unsigned char C_STRA_1[]   = { 's','t','r','a',0xDF,'e' };               /* "straße" */
static const unsigned char C_STRA_2[]   = { 'S','T','R','A','S','S','E' };            /* "STRASSE" */
static const unsigned char C_STRA_3[]   = { 's','t','r','a','s','s','e' };            /* "strasse" */
static const unsigned char C_THORN_1[]  = { 0xFE,'o','r','n' };                       /* "þorn" */
static const unsigned char C_THORN_2[]  = { 0xDE,'o','r','n' };                       /* "Þorn" */
static const unsigned char C_THORN_3[]  = { 't','h','o','r','n' };                    /* "thorn" */
static const unsigned char C_UBER_1[]   = { 0xFC,'b','e','r' };                       /* "über" */
static const unsigned char C_UBER_2[]   = { 'U','B','E','R' };                        /* "UBER" */
static const unsigned char C_UBER_3[]   = { 'u','b','e','r' };                        /* "uber" */
static const unsigned char C_NAND_1[]   = { 0xF1,'a','n','d',0xFA };                  /* "ñandú" */
static const unsigned char C_NAND_2[]   = { 'n','a','n','d','u' };                    /* "nandu" */
static const unsigned char C_ELAN_1[]   = { 0xE9,'l','a','n' };                       /* "élan" */
static const unsigned char C_ELAN_2[]   = { 'e','l','a','n' };                        /* "elan" */
static const unsigned char C_OL_1[]     = { 0xD6,'l' };                               /* "Öl" */
static const unsigned char C_OL_2[]     = { 'O','L' };                                /* "OL" */
static const unsigned char C_ZEBRA_1[]  = { 'z','e','b','r','a' };                    /* "zebra" */
static const unsigned char C_ZEBRA_2[]  = { 'Z','e','b','r','a' };                    /* "Zebra" */

static const TEXTBYTES TEXTS[ROWS] =
{
   TEXT_BYTES( C_ALPHA_U ),  TEXT_BYTES( C_ALPHA_L ),  TEXT_BYTES( C_ALPHA_M ),
   { C_EMPTY, 0 },
   TEXT_BYTES( C_CAFE_L ),   TEXT_BYTES( C_CAFE_LA ),  TEXT_BYTES( C_CAFE_U ),
   TEXT_BYTES( C_CAFE_UA ),
   TEXT_BYTES( C_AETH_1 ),   TEXT_BYTES( C_AETH_2 ),
   TEXT_BYTES( C_AEON_1 ),   TEXT_BYTES( C_AEON_2 ),
   TEXT_BYTES( C_OEUV_1 ),   TEXT_BYTES( C_OEUV_2 ),   TEXT_BYTES( C_OEUV_3 ),
   TEXT_BYTES( C_STRA_1 ),   TEXT_BYTES( C_STRA_2 ),   TEXT_BYTES( C_STRA_3 ),
   TEXT_BYTES( C_THORN_1 ),  TEXT_BYTES( C_THORN_2 ),  TEXT_BYTES( C_THORN_3 ),
   TEXT_BYTES( C_UBER_1 ),   TEXT_BYTES( C_UBER_2 ),   TEXT_BYTES( C_UBER_3 ),
   TEXT_BYTES( C_NAND_1 ),   TEXT_BYTES( C_NAND_2 ),
   TEXT_BYTES( C_ELAN_1 ),   TEXT_BYTES( C_ELAN_2 ),
   TEXT_BYTES( C_OL_1 ),     TEXT_BYTES( C_OL_2 ),
   TEXT_BYTES( C_ZEBRA_1 ),  TEXT_BYTES( C_ZEBRA_2 )
};

/* ------------------------------------------------------------------- case */

int caseCdxColl( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"K_TEXT", 'C',  20,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name       expression   filter  unique  descending */
   static TAG4INFO machineTag[] =
   {
      { (char *)"C_MACH", (char *)"K_TEXT", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   static TAG4INFO generalTag[] =
   {
      { (char *)"C_GEN", (char *)"K_TEXT", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   DATA4 *data;
   INDEX4 *index;
   int i, rc;

   sprintf( path, "%s\\CDXCOLL.DBF", outDir );
   printf( "CDXCOLL.DBF (0x30 + CDX, machine and GENERAL over one field) ... " );

   cb->compatibility = 30;

   /* cp1252 is what selects GENERAL's cp1252 array when the tag is created, so
    * the mark has to be on the table before the index is. Restored right after
    * the create so no later case inherits it. */
   c4setCodePage( cb, cp1252 );
   data = d4create( cb, path, fields, 0 );
   c4setCodePage( cb, cp0 );

   if ( data == 0 )
      return fail( cb, "d4create CDXCOLL.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      assignText  ( d4fieldJ( data, 2 ), &TEXTS[i] );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   /* Machine order first — the default, and the baseline. */
   index = i4create( data, 0, machineTag );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create CDXCOLL.cdx" );
   }

   /* Then GENERAL, added to the same file. The setting is CODE4-level and is
    * read when the tag is created, so it is set around this call only. */
   c4setCollatingSequence( cb, sort4general );
   rc = i4tagAdd( index, generalTag );
   c4setCollatingSequence( cb, sort4machine );

   if ( rc < 0 )
   {
      d4close( data );
      return fail( cb, "i4tagAdd C_GEN" );
   }

   printf( "%d records, 2 tags\n", ROWS );

   rc = finish( cb, data, outDir, "CDXCOLL.DBF" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "CDXCOLL.DBF", "CDXCOLL.cdx" );

   return rc;
}
