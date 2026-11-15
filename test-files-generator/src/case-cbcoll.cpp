/* ==========================================================================
 * case-cbcoll.cpp — CBCP1250.DBF, CBCP1252.DBF, CBCP850.DBF, each + .cdx
 *
 * CodeBase's own collations: the tags whose header names "CBnnnnn" rather
 * than "GENERAL" or nothing (KEY-COLLATION.md §4). The digits are the
 * Collate4name ordinal (d4data.h:91-117), and the weights follow the ordinal,
 * not the data file's code page (i4init.c:407-416) -- unlike GENERAL, which is
 * chosen by the code page. Step 013, sub-step 7a-11.
 *
 * Three tables, because each collation means a code page:
 *
 *   CBCP1250  cp1250  C_HR    sort4croatian       CB00005  simple
 *                     C_HRU   sort4croatianUpper  CB00006  simple
 *   CBCP1252  cp1252  C_ES    sort4spanish        CB00009  simple
 *                     C_AVAYA collate4avaya1252   CB00008  subSortCompress
 *                     C_G437  collate4generalCp437 CB00003 subSortCompress
 *                     C_G850  collate4generalCp850 CB00007 subSortCompress
 *   CBCP850   cp850   C_ES    sort4spanish        CB00010  simple
 *                     C_G1252 collate4generalCp1252 CB00002 subSortCompress
 *
 * plus a machine tag C_MACH on each, created first by i4create, as the
 * control. Every other tag is added by i4tagAdd with the CODE4 setting around
 * the call: the old sort4* setting (i4tag.c:2949-2993), or an explicit
 * collateName (i4add.c:746-755, i4tag.c:3024-3061) for the ones no sort4*
 * value reaches. Over a plain character field i4tagAdd's omissions (ADR-63)
 * do not arise: they concern ASCEND/DESCEND and memo under a collation.
 * Ordinal 1 (machine) is never written -- machine writes eight zeros -- and 4
 * is the collation read from disk tables, which the port refuses.
 *
 * Every table holds one C(12) field: each byte 1-255 alone in a row, so every
 * tag's keys reach every entry of its weight table, then words in the code
 * page's own letters. Text outside ASCII is written as byte arrays, never as
 * source literals.
 *
 * The data below belongs to this case alone -- see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define BYTE_ROWS 255    /* one row per byte value 1-255 */

/* ------------------------------------------------------------- the words */

/* cp1250: Croatian. c-caron E8/C8, c-acute E6/C6, d-stroke F0/D0, s-caron 9A/8A,
 * z-caron 9E/8E, and the digraphs lj, nj, dz-caron spelt with ordinary letters. */
static const unsigned char HR_1[] = { 0xE8,'o','k','o','l','a','d','a' };        /* "čokolada" */
static const unsigned char HR_2[] = { 0xC8,'O','K','O','L','A','D','A' };        /* "ČOKOLADA" */
static const unsigned char HR_3[] = { 'c','o','k','o','l','a','d','a' };         /* "cokolada" */
static const unsigned char HR_4[] = { 0xE6,'e','v','a','p','i' };                 /* "ćevapi" */
static const unsigned char HR_5[] = { 'd',0x9E,'e','p' };                         /* "džep" */
static const unsigned char HR_6[] = { 0xF0,'a','k' };                             /* "đak" */
static const unsigned char HR_7[] = { 0xD0,'A','K' };                             /* "ĐAK" */
static const unsigned char HR_8[] = { 'l','j','u','b','a','v' };                  /* "ljubav" */
static const unsigned char HR_9[] = { 'n','j','i','v','a' };                      /* "njiva" */
static const unsigned char HR_10[] = { 0x9A,'u','m','a' };                        /* "šuma" */
static const unsigned char HR_11[] = { 0x8A,'U','M','A' };                        /* "ŠUMA" */
static const unsigned char HR_12[] = { 0x9E,'a','b','a' };                        /* "žaba" */
static const unsigned char HR_13[] = { 'z','a','b','a' };                         /* "zaba" */
static const unsigned char HR_14[] = { 'a','b','c',' ','d' };                     /* "abc d", a blank inside */

/* cp1252: Spanish and Avaya. n-tilde F1/D1, a-acute E1/C1, u-acute FA, oe 9C,
 * sharp s DF. */
static const unsigned char W1252_1[] = { 0xF1,'a','n','d',0xFA };                 /* "ñandú" */
static const unsigned char W1252_2[] = { 'n','a','n','d','u' };                   /* "nandu" */
static const unsigned char W1252_3[] = { 'o','t','o',0xF1,'o' };                  /* "otoño" */
static const unsigned char W1252_4[] = { 'o','t','o','n','o' };                   /* "otono" */
static const unsigned char W1252_5[] = { 0xC1,'n','g','e','l' };                  /* "Ángel" */
static const unsigned char W1252_6[] = { 'a','n','g','e','l' };                   /* "angel" */
static const unsigned char W1252_7[] = { 'l','l','a','m','a' };                   /* "llama" */
static const unsigned char W1252_8[] = { 'c','h','i','c','o' };                   /* "chico" */
static const unsigned char W1252_9[] = { 0x9C,'u','v','r','e' };                  /* "œuvre" */
static const unsigned char W1252_10[] = { 's','t','r','a',0xDF,'e' };             /* "straße" */
static const unsigned char W1252_11[] = { 'A','L','P','H','A' };                  /* "ALPHA" */
static const unsigned char W1252_12[] = { 'a','l','p','h','a' };                  /* "alpha" */

/* cp850: Spanish. n-tilde A4/A5, a-acute A0, u-acute A3, A-acute B5. */
static const unsigned char W850_1[] = { 0xA4,'a','n','d',0xA3 };                  /* "ñandú" */
static const unsigned char W850_2[] = { 'n','a','n','d','u' };                    /* "nandu" */
static const unsigned char W850_3[] = { 'o','t','o',0xA4,'o' };                   /* "otoño" */
static const unsigned char W850_4[] = { 0xB5,'n','g','e','l' };                   /* "Ángel" */
static const unsigned char W850_5[] = { 'a','n','g','e','l' };                    /* "angel" */
static const unsigned char W850_6[] = { 0xA5,'A','N','D','U' };                   /* "ÑANDU" */
static const unsigned char W850_7[] = { 's','t','r','a',0xE1,'e' };               /* "straße" */

static const TEXTBYTES WORDS_1250[] =
{
   TEXT_BYTES( HR_1 ),  TEXT_BYTES( HR_2 ),  TEXT_BYTES( HR_3 ),  TEXT_BYTES( HR_4 ),
   TEXT_BYTES( HR_5 ),  TEXT_BYTES( HR_6 ),  TEXT_BYTES( HR_7 ),  TEXT_BYTES( HR_8 ),
   TEXT_BYTES( HR_9 ),  TEXT_BYTES( HR_10 ), TEXT_BYTES( HR_11 ), TEXT_BYTES( HR_12 ),
   TEXT_BYTES( HR_13 ), TEXT_BYTES( HR_14 )
};

static const TEXTBYTES WORDS_1252[] =
{
   TEXT_BYTES( W1252_1 ),  TEXT_BYTES( W1252_2 ),  TEXT_BYTES( W1252_3 ),  TEXT_BYTES( W1252_4 ),
   TEXT_BYTES( W1252_5 ),  TEXT_BYTES( W1252_6 ),  TEXT_BYTES( W1252_7 ),  TEXT_BYTES( W1252_8 ),
   TEXT_BYTES( W1252_9 ),  TEXT_BYTES( W1252_10 ), TEXT_BYTES( W1252_11 ), TEXT_BYTES( W1252_12 )
};

static const TEXTBYTES WORDS_850[] =
{
   TEXT_BYTES( W850_1 ), TEXT_BYTES( W850_2 ), TEXT_BYTES( W850_3 ), TEXT_BYTES( W850_4 ),
   TEXT_BYTES( W850_5 ), TEXT_BYTES( W850_6 ), TEXT_BYTES( W850_7 )
};

/* ------------------------------------------------------------ the tags */

/* How a collated tag is asked for: an old sort4* setting, or an explicit name. */
typedef struct
{
   const char *name;
   int sortSeq;              /* sort4*, or -1 when collateName is used */
   Collate4name collateName; /* collate4none when sortSeq is used */
} CBTAG;

/* ------------------------------------------------------------------ table */

static int cbTable( CODE4 *cb, const char *outDir, const char *table, short codePage,
                    const TEXTBYTES *words, int wordCount, const CBTAG *tags, int tagCount )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"K_TEXT", 'C',  12,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   static TAG4INFO machineTag[] =
   {
      { (char *)"C_MACH", (char *)"K_TEXT", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   TAG4INFO tagInfo[2];
   char path[520], dbf[32], cdx[32];
   unsigned char one[1];
   TEXTBYTES text;
   DATA4 *data;
   INDEX4 *index;
   int i, rc, row = 0;

   sprintf( dbf, "%s.DBF", table );
   sprintf( cdx, "%s.cdx", table );
   sprintf( path, "%s\\%s", outDir, dbf );
   printf( "%s (0x30 + CDX, codePage 0x%02X, CodeBase collations) ... ", dbf, (unsigned char)codePage );

   cb->compatibility = 30;

   /* The mark decides nothing about a CBnnnnn tag's weights, but it decides
    * which ordinal sort4spanish and sort4general pick, and it is the code page
    * the words are written in. Restored right after the create. */
   c4setCodePage( cb, codePage );
   data = d4create( cb, path, fields, 0 );
   c4setCodePage( cb, cp0 );

   if ( data == 0 )
      return fail( cb, "d4create" );

   for ( i = 0; i < BYTE_ROWS + wordCount; i++ )
   {
      if ( i < BYTE_ROWS )
      {
         one[0] = (unsigned char)( i + 1 );
         text.bytes = one;
         text.len = 1;
      }
      else
         text = words[i - BYTE_ROWS];

      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      assignText  ( d4fieldJ( data, 2 ), &text );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
      row++;
   }

   index = i4create( data, 0, machineTag );
   if ( index == 0 )
   {
      d4close( data );
      return fail( cb, "i4create" );
   }

   for ( i = 0; i < tagCount; i++ )
   {
      memset( tagInfo, 0, sizeof( tagInfo ) );
      tagInfo[0].name = (char *)tags[i].name;
      tagInfo[0].expression = (char *)"K_TEXT";

      if ( tags[i].sortSeq >= 0 )
         c4setCollatingSequence( cb, tags[i].sortSeq );
      else
         cb->collateName = tags[i].collateName;

      rc = i4tagAdd( index, tagInfo );

      c4setCollatingSequence( cb, sort4machine );
      cb->collateName = collate4none;

      if ( rc < 0 )
      {
         d4close( data );
         return fail( cb, "i4tagAdd" );
      }
   }

   printf( "%d records, %d tags\n", row, tagCount + 1 );

   rc = finish( cb, data, outDir, dbf );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, dbf, cdx );

   return rc;
}

/* ------------------------------------------------------------------- case */

int caseCbColl( CODE4 *cb, const char *outDir )
{
   static const CBTAG tags1250[] =
   {
      { "C_HR",    sort4croatian,      collate4none },
      { "C_HRU",   sort4croatianUpper, collate4none },
   };

   static const CBTAG tags1252[] =
   {
      { "C_ES",    sort4spanish, collate4none },
      { "C_AVAYA", -1,           collate4avaya1252 },
      { "C_G437",  -1,           collate4generalCp437 },
      { "C_G850",  -1,           collate4generalCp850 },
   };

   static const CBTAG tags850[] =
   {
      { "C_ES",    sort4spanish, collate4none },
      { "C_G1252", -1,           collate4generalCp1252 },
   };

   int rc = cbTable( cb, outDir, "CBCP1250", cp1250,
                     WORDS_1250, (int)( sizeof( WORDS_1250 ) / sizeof( WORDS_1250[0] ) ),
                     tags1250, (int)( sizeof( tags1250 ) / sizeof( tags1250[0] ) ) );
   if ( rc == 0 )
      rc = cbTable( cb, outDir, "CBCP1252", cp1252,
                    WORDS_1252, (int)( sizeof( WORDS_1252 ) / sizeof( WORDS_1252[0] ) ),
                    tags1252, (int)( sizeof( tags1252 ) / sizeof( tags1252[0] ) ) );
   if ( rc == 0 )
      rc = cbTable( cb, outDir, "CBCP850", cp850,
                    WORDS_850, (int)( sizeof( WORDS_850 ) / sizeof( WORDS_850[0] ) ),
                    tags850, (int)( sizeof( tags850 ) / sizeof( tags850[0] ) ) );

   return rc;
}
