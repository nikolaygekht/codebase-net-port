/* ==========================================================================
 * case-cp1251.cpp — CP1251.DBF (+ CP1251.fpt)
 *
 * The marked-code-page pair, single-byte half: header byte 29 holds 0xC9, the
 * Windows Cyrillic language driver (DBF-FORMAT.md §8). CP936.DBF is the
 * multi-byte half.
 *
 * Every other corpus table leaves byte 29 at 0x00, so a reader that ignored the
 * byte outright would pass the whole suite. Four things this case pins down:
 *
 *   1. The byte reaches the file verbatim. d4create writes (char)c4->codePage
 *      into the header with no validation (D4CREATE.C:1391) and d4open reads it
 *      straight back (D4OPEN.C:2217), so a language driver CodeBase's own setter
 *      refuses — c4setCodePage takes only cp0/437/850/1252/1250 (c4set.c:727) —
 *      still lands in a file Visual FoxPro would call its own. 0xC9 is what VFP
 *      stamps on a Cyrillic table, so the field is assigned directly here rather
 *      than through the setter.
 *   2. The engine transcodes nothing. Every byte assigned is the byte stored:
 *      the code page says how a reader should interpret a record, and never
 *      names a transformation the writer applied.
 *   3. Bytes no ASCII-only reader could invent. SWEEP walks 0x80-0xFF whole
 *      across rows 1-8, sixteen bytes at a time — including 0x98, the one byte
 *      cp1251 leaves undefined, so whatever a reader makes of it is a decision
 *      and not an accident.
 *   4. High-byte text on the memo path too, so the FPT holds content needing the
 *      same code page the record does.
 *
 * EXACT is filled to its width and SHORT never is, so blank padding beside
 * high-byte content is gated in both directions.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "cases.h"

#define ROWS       32     /* records in this table */
#define CODE_PAGE  0xC9   /* Windows Cyrillic. c4->codePage is a short, the
                           * header field a char, so 201 stores as 0xC9. */
#define SWEEP_LEN  16     /* width of SWEEP, and the bytes per sweeping row */
#define SWEEP_ROWS 8      /* 8 rows x 16 bytes covers 0x80-0xFF exactly */

/* --------------------------------------------------------------- test data
 *
 * Every byte run below was produced by encoding its commented text with the
 * code page this table declares. Row 3 is the empty-text row.
 */

static const unsigned char T_HELLO[]  =        /* "Привет, мир" */
   { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, 0x2C, 0x20, 0xEC, 0xE8, 0xF0 };
static const unsigned char T_MOSCOW[] =        /* "Москва" */
   { 0xCC, 0xEE, 0xF1, 0xEA, 0xE2, 0xE0 };
static const unsigned char T_EMPTY[1] = { 0 }; /* used with length 0 */
static const unsigned char T_HEDGE[]  =        /* "ЁЖИК и ёжик" — 0xA8/0xB8 */
   { 0xA8, 0xC6, 0xC8, 0xCA, 0x20, 0xE8, 0x20, 0xB8, 0xE6, 0xE8, 0xEA };
static const unsigned char T_NUMERO[] =        /* "Тест №42" — 0xB9 is not a letter */
   { 0xD2, 0xE5, 0xF1, 0xF2, 0x20, 0xB9, 0x34, 0x32 };
static const unsigned char T_GREET[]  =        /* "Здравствуйте" */
   { 0xC7, 0xE4, 0xF0, 0xE0, 0xE2, 0xF1, 0xF2, 0xE2, 0xF3, 0xE9, 0xF2, 0xE5 };
static const unsigned char T_RANGE[]  =        /* "ЪЫЬЭЮЯ абвгдеёж" */
   { 0xDA, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0x20, 0xE0, 0xE1, 0xE2, 0xE3, 0xE4,
     0xE5, 0xB8, 0xE6 };
static const unsigned char T_MIXED[]  =        /* "Файл-1252/1251" */
   { 0xD4, 0xE0, 0xE9, 0xEB, 0x2D, 0x31, 0x32, 0x35, 0x32, 0x2F, 0x31, 0x32,
     0x35, 0x31 };

static const TEXTBYTES TEXTS[8] =
{
   TEXT_BYTES( T_HELLO ),
   TEXT_BYTES( T_MOSCOW ),
   { T_EMPTY, 0 },
   TEXT_BYTES( T_HEDGE ),
   TEXT_BYTES( T_NUMERO ),
   TEXT_BYTES( T_GREET ),
   TEXT_BYTES( T_RANGE ),
   TEXT_BYTES( T_MIXED )
};

/* Exactly ten bytes each, so EXACT is full and has no padding at all. */
static const unsigned char E_COMPUTERS[] =     /* "Компьютеры" */
   { 0xCA, 0xEE, 0xEC, 0xEF, 0xFC, 0xFE, 0xF2, 0xE5, 0xF0, 0xFB };
static const unsigned char E_KEYBOARD[]  =     /* "Клавиатура" */
   { 0xCA, 0xEB, 0xE0, 0xE2, 0xE8, 0xE0, 0xF2, 0xF3, 0xF0, 0xE0 };
static const unsigned char E_PROGRAM[]   =     /* "Программа!" */
   { 0xCF, 0xF0, 0xEE, 0xE3, 0xF0, 0xE0, 0xEC, 0xEC, 0xE0, 0x21 };
static const unsigned char E_HEDGEHOGS[] =     /* "ЁЖИКИ-ёжик" */
   { 0xA8, 0xC6, 0xC8, 0xCA, 0xC8, 0x2D, 0xB8, 0xE6, 0xE8, 0xEA };

static const TEXTBYTES EXACTS[4] =
{
   TEXT_BYTES( E_COMPUTERS ),
   TEXT_BYTES( E_KEYBOARD ),
   TEXT_BYTES( E_PROGRAM ),
   TEXT_BYTES( E_HEDGEHOGS )
};

/* Well short of the field width, so the blank padding behind high-byte content
 * is what a reader has to trim. */
static const unsigned char S_HOUSE[]  = { 0xC4, 0xEE, 0xEC };              /* "Дом" */
static const unsigned char S_CAT[]    = { 0xEA, 0xEE, 0xF2 };              /* "кот" */
static const unsigned char S_HEDGE[]  = { 0xA8, 0xE6 };                    /* "Ёж" */
static const unsigned char S_YA[]     = { 0xDF };                          /* "Я" */
static const unsigned char S_STREET[] =                                    /* "ул. Мира" */
   { 0xF3, 0xEB, 0x2E, 0x20, 0xCC, 0xE8, 0xF0, 0xE0 };

static const TEXTBYTES SHORTS[5] =
{
   TEXT_BYTES( S_HOUSE ),
   TEXT_BYTES( S_CAT ),
   TEXT_BYTES( S_HEDGE ),
   TEXT_BYTES( S_YA ),
   TEXT_BYTES( S_STREET )
};

/* "Привет мир 0123456789 абвгдеёжзийклмнопрстуфхцчшщъыьэюя " — the memo filler,
 * cycled to whatever length a row asks for. Single-byte throughout, so any
 * length is a whole number of characters. */
static const unsigned char MEMO_FILLER[] =
{
   0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, 0x20, 0xEC, 0xE8, 0xF0, 0x20, 0x30,
   0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x20, 0xE0, 0xE1,
   0xE2, 0xE3, 0xE4, 0xE5, 0xB8, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xEB, 0xEC,
   0xED, 0xEE, 0xEF, 0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
   0xF9, 0xFA, 0xFB, 0xFC, 0xFD, 0xFE, 0xFF, 0x20
};

/* Payload lengths, cycled. The FPT block-boundary lengths belong to VFPMEMO;
 * here the memo only has to carry high bytes, empty included. */
static const int MEMO_LENS[6] = { 0, 1, 7, 63, 200, 401 };

/* Sixteen bytes for SWEEP. Rows 1-8 take one slice each of 0x80-0xFF; later rows
 * alternate an ASCII letter with a Cyrillic capital, so one field holds both. */
static void sweepBytes( int i, unsigned char *out )
{
   int k;

   if ( i < SWEEP_ROWS )
   {
      for ( k = 0; k < SWEEP_LEN; k++ )
         out[k] = (unsigned char)( 0x80 + i * SWEEP_LEN + k );
      return;
   }

   for ( k = 0; k < SWEEP_LEN; k++ )
      out[k] = ( k % 2 ) ? (unsigned char)( 'A' + ( ( i + k ) % 26 ) )
                         : (unsigned char)( 0xC0 + ( ( i * 3 + k ) % 32 ) );
}

static void memoBytes( int i, unsigned char *out, int len )
{
   int fillerLen = (int)sizeof( MEMO_FILLER );
   int k;

   for ( k = 0; k < len; k++ )
      out[k] = MEMO_FILLER[ ( k + i * 5 ) % fillerLen ];
}

/* ------------------------------------------------------------------- case */

int caseCp1251( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"TEXT",   'C',  20,   0,   0 },   /* Cyrillic words and phrases */
      { (char *)"EXACT",  'C',  10,   0,   0 },   /* filled to the byte, never padded */
      { (char *)"SHORT",  'C',  12,   0,   0 },   /* always padded */
      { (char *)"SWEEP",  'C',  16,   0,   0 },   /* 0x80-0xFF across rows 1-8 */
      { (char *)"MEMO",   'M',   4,   0,   0 },   /* the same text on the FPT path */
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   unsigned char sweep[SWEEP_LEN], memo[512];
   DATA4 *data;
   int i, len;

   sprintf( path, "%s\\CP1251.DBF", outDir );
   printf( "CP1251.DBF (0x30 + FPT, codePage 0x%02X) ... ", CODE_PAGE );

   cb->compatibility = 30;

   /* Assigned directly: c4setCodePage would refuse this value, while d4create
    * writes whatever the field holds (D4CREATE.C:1391). Restored right after
    * the create so a later case cannot inherit it. */
   cb->codePage = CODE_PAGE;
   data = d4create( cb, path, fields, 0 );
   cb->codePage = cp0;

   if ( data == 0 )
      return fail( cb, "d4create CP1251.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      sweepBytes( i, sweep );
      len = MEMO_LENS[ i % 6 ];
      memoBytes( i, memo, len );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      assignText  ( d4fieldJ( data, 2 ), &TEXTS[ i % 8 ] );
      assignText  ( d4fieldJ( data, 3 ), &EXACTS[ i % 4 ] );
      assignText  ( d4fieldJ( data, 4 ), &SHORTS[ i % 5 ] );
      f4assignN   ( d4fieldJ( data, 5 ), (const char *)sweep, SWEEP_LEN );
      f4memoAssignN( d4fieldJ( data, 6 ), (const char *)memo, (unsigned)len );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "CP1251.DBF" );
}
