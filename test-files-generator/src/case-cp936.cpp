/* ==========================================================================
 * case-cp936.cpp — CP936.DBF (+ CP936.fpt)
 *
 * The marked-code-page pair, multi-byte half: header byte 29 holds 0x7A, the
 * Simplified Chinese (GBK) language driver. CP1251.DBF is the single-byte half
 * and carries the note on how the byte reaches the file; this case is about what
 * a multi-byte code page does to text a reader thinks it can handle a byte at a
 * time.
 *
 * GBK is a double-byte encoding whose lead bytes are 0x81-0xFE and whose trail
 * bytes are 0x40-0xFE apart from 0x7F. The trail range overlaps ASCII, so:
 *
 *   1. **A character's second byte can be an ASCII byte with a meaning of its
 *      own.** TRAIL is built from characters whose trail bytes are 0x5C, 0x7C,
 *      0x41, 0x7E and 0x40 — a field whose bytes contain a backslash, a pipe and
 *      a capital A that are not a backslash, a pipe or a capital A. Anything that
 *      scans a character field byte-wise, for a path separator or a delimiter,
 *      finds them.
 *   2. **A field width is a byte count, so a character can be cut in half at the
 *      field boundary.** CUT is seven bytes wide and is assigned eight bytes of
 *      text; f4assignN truncates (F4STR.C:155-168), so its last byte is a lead
 *      byte with nothing behind it. Visual FoxPro produces exactly this, and a
 *      reader has to decide what such a field decodes to.
 *   3. **Memo payloads truncate the same way.** The odd payload lengths below end
 *      mid-character for the same reason, on the FPT path instead of the record.
 *
 * TRAIL's third variant is shorter than its field, so a 0x5C trail byte sits
 * directly against the blank padding; EXACT is filled to the byte and has none.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "cases.h"

#define ROWS      32     /* records in this table */
#define CODE_PAGE 0x7A   /* Simplified Chinese (GBK), as VFP stamps it */
#define CUT_LEN   7      /* width of CUT: one byte short of the text it is given */

/* --------------------------------------------------------------- test data
 *
 * Every byte run below was produced by encoding its commented text with the code
 * page this table declares. Row 3 is the empty-text row.
 */

static const unsigned char T_CHINESE[] =       /* "中文测试" */
   { 0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4 };
static const unsigned char T_ENCODING[] =      /* "简体中文编码" */
   { 0xBC, 0xF2, 0xCC, 0xE5, 0xD6, 0xD0, 0xCE, 0xC4, 0xB1, 0xE0, 0xC2, 0xEB };
static const unsigned char T_EMPTY[1] = { 0 }; /* used with length 0 */
static const unsigned char T_ASCIIMIX[] =      /* "GBK:中文/ASCII" */
   { 0x47, 0x42, 0x4B, 0x3A, 0xD6, 0xD0, 0xCE, 0xC4, 0x2F, 0x41, 0x53, 0x43,
     0x49, 0x49 };
static const unsigned char T_BEIJING[] =       /* "北京市朝阳区" */
   { 0xB1, 0xB1, 0xBE, 0xA9, 0xCA, 0xD0, 0xB3, 0xAF, 0xD1, 0xF4, 0xC7, 0xF8 };
static const unsigned char T_TABLE1[] =        /* "数据库表 1" */
   { 0xCA, 0xFD, 0xBE, 0xDD, 0xBF, 0xE2, 0xB1, 0xED, 0x20, 0x31 };
static const unsigned char T_HANZITRAIL[] =    /* "汉字乗亅丄亊" — ordinary characters
                                                * followed by ASCII-trail ones */
   { 0xBA, 0xBA, 0xD7, 0xD6, 0x81, 0x5C, 0x81, 0x7C, 0x81, 0x41, 0x81, 0x7E };
static const unsigned char T_FULL20[] =        /* "中文测试简体中文编码" — fills TEXT
                                                * to the byte, ten characters */
   { 0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4, 0xBC, 0xF2, 0xCC, 0xE5,
     0xD6, 0xD0, 0xCE, 0xC4, 0xB1, 0xE0, 0xC2, 0xEB };

static const TEXTBYTES TEXTS[8] =
{
   TEXT_BYTES( T_CHINESE ),
   TEXT_BYTES( T_ENCODING ),
   { T_EMPTY, 0 },
   TEXT_BYTES( T_ASCIIMIX ),
   TEXT_BYTES( T_BEIJING ),
   TEXT_BYTES( T_TABLE1 ),
   TEXT_BYTES( T_HANZITRAIL ),
   TEXT_BYTES( T_FULL20 )
};

/* Characters whose trail byte is an ASCII byte: 乗 0x81 0x5C, 亅 0x81 0x7C,
 * 丄 0x81 0x41, 亊 0x81 0x7E, 丂 0x81 0x40, 俓 0x82 0x5C. */
static const unsigned char R_FORWARD[] =       /* "乗亅丄亊丂俓" — fills TRAIL exactly */
   { 0x81, 0x5C, 0x81, 0x7C, 0x81, 0x41, 0x81, 0x7E, 0x81, 0x40, 0x82, 0x5C };
static const unsigned char R_REVERSE[] =       /* "丂亊丄亅乗俓" — ends on a 0x5C */
   { 0x81, 0x40, 0x81, 0x7E, 0x81, 0x41, 0x81, 0x7C, 0x81, 0x5C, 0x82, 0x5C };
static const unsigned char R_PADDED[] =        /* "汉字乗" — a 0x5C against the padding */
   { 0xBA, 0xBA, 0xD7, 0xD6, 0x81, 0x5C };

static const TEXTBYTES TRAILS[3] =
{
   TEXT_BYTES( R_FORWARD ),
   TEXT_BYTES( R_REVERSE ),
   TEXT_BYTES( R_PADDED )
};

/* Eight bytes each — four characters, filling EXACT with no padding. */
static const unsigned char X_CHINESE[] =       /* "中文测试" */
   { 0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4 };
static const unsigned char X_SIMPLIFIED[] =    /* "简体中文" */
   { 0xBC, 0xF2, 0xCC, 0xE5, 0xD6, 0xD0, 0xCE, 0xC4 };
static const unsigned char X_HANZITRAIL[] =    /* "汉字乗亅" */
   { 0xBA, 0xBA, 0xD7, 0xD6, 0x81, 0x5C, 0x81, 0x7C };

static const TEXTBYTES EXACTS[3] =
{
   TEXT_BYTES( X_CHINESE ),
   TEXT_BYTES( X_SIMPLIFIED ),
   TEXT_BYTES( X_HANZITRAIL )
};

/* Eight bytes into a seven-byte field: three characters survive and the fourth
 * loses its trail byte, so CUT always ends on a dangling lead byte. */
static const unsigned char C_CHINESE[] =       /* "中文测试" -> ends 0xCA */
   { 0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4 };
static const unsigned char C_ENCODED[] =       /* "编码测试" -> ends 0xCA */
   { 0xB1, 0xE0, 0xC2, 0xEB, 0xB2, 0xE2, 0xCA, 0xD4 };
static const unsigned char C_HANZITRAIL[] =    /* "汉字乗亅" -> ends 0x81, keeping the
                                                * 0x5C trail byte before it */
   { 0xBA, 0xBA, 0xD7, 0xD6, 0x81, 0x5C, 0x81, 0x7C };

static const TEXTBYTES CUTS[3] =
{
   TEXT_BYTES( C_CHINESE ),
   TEXT_BYTES( C_ENCODED ),
   TEXT_BYTES( C_HANZITRAIL )
};

/* "汉字编码测试数据库中文备注" — the memo filler, cycled to whatever length a row
 * asks for. Thirteen characters, so its length is even and a shift of an even
 * number of bytes keeps every slice starting on a lead byte. */
static const unsigned char MEMO_FILLER[] =
{
   0xBA, 0xBA, 0xD7, 0xD6, 0xB1, 0xE0, 0xC2, 0xEB, 0xB2, 0xE2, 0xCA, 0xD4,
   0xCA, 0xFD, 0xBE, 0xDD, 0xBF, 0xE2, 0xD6, 0xD0, 0xCE, 0xC4, 0xB1, 0xB8,
   0xD7, 0xA2
};

/* Payload lengths, cycled. 63 and 401 are odd, so those rows end on a lead byte
 * with nothing behind it — the memo-path form of the CUT field. The FPT
 * block-boundary lengths belong to VFPMEMO. */
static const int MEMO_LENS[6] = { 0, 2, 8, 63, 200, 401 };

static void memoBytes( int i, unsigned char *out, int len )
{
   int fillerLen = (int)sizeof( MEMO_FILLER );
   int k;

   /* The shift is even on purpose: an odd one would start every payload on a
    * trail byte and the truncation above would stop being the only cut. */
   for ( k = 0; k < len; k++ )
      out[k] = MEMO_FILLER[ ( k + i * 2 ) % fillerLen ];
}

/* ------------------------------------------------------------------- case */

int caseCp936( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,       0,   0 },
      { (char *)"TEXT",   'C',  20,       0,   0 },   /* Chinese words and phrases */
      { (char *)"TRAIL",  'C',  12,       0,   0 },   /* ASCII bytes inside characters */
      { (char *)"EXACT",  'C',   8,       0,   0 },   /* filled to the byte */
      { (char *)"CUT",    'C', CUT_LEN,   0,   0 },   /* a character cut in half */
      { (char *)"MEMO",   'M',   4,       0,   0 },   /* the same text on the FPT path */
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   unsigned char memo[512];
   DATA4 *data;
   int i, len;

   sprintf( path, "%s\\CP936.DBF", outDir );
   printf( "CP936.DBF (0x30 + FPT, codePage 0x%02X) ... ", CODE_PAGE );

   cb->compatibility = 30;

   /* Assigned directly, and restored right after: see case-cp1251.cpp for why
    * c4setCodePage is not the way in. */
   cb->codePage = CODE_PAGE;
   data = d4create( cb, path, fields, 0 );
   cb->codePage = cp0;

   if ( data == 0 )
      return fail( cb, "d4create CP936.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      len = MEMO_LENS[ i % 6 ];
      memoBytes( i, memo, len );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      assignText  ( d4fieldJ( data, 2 ), &TEXTS[ i % 8 ] );
      assignText  ( d4fieldJ( data, 3 ), &TRAILS[ i % 3 ] );
      assignText  ( d4fieldJ( data, 4 ), &EXACTS[ i % 3 ] );
      assignText  ( d4fieldJ( data, 5 ), &CUTS[ i % 3 ] );
      f4memoAssignN( d4fieldJ( data, 6 ), (const char *)memo, (unsigned)len );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "CP936.DBF" );
}
