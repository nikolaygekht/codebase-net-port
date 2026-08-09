/* ==========================================================================
 * generator.cpp — generates golden test files for the CodeBase.NET port.
 *
 * Uses the original Sequiter CodeBase C library (S4FOX / S4STAND_ALONE) as the
 * reference implementation. Files produced here are the corpus the C# port is
 * differential-tested against, so the output is checked in and this generator
 * is run only when the corpus needs new cases.
 *
 * Usage:  testgen.exe [output-dir]        (default: bin\out)
 *
 * Each case writes <NAME>.DBF (+ <NAME>.FPT when it has memo fields) and a
 * companion <NAME>.dump.txt holding the expected header facts, field
 * descriptors and record values, read back through the C library. The C# port
 * asserts against the dump; expected values are never hand-written.
 *
 * DETERMINISM: the DBF header's "last update" stamp (bytes 1-3) is the system
 * date, which would change three bytes per file on every regeneration. After
 * closing each table we overwrite those three bytes with a frozen date (see
 * freezeDateStamp) so the corpus is byte-stable. That is the only place this
 * generator alters what the C library wrote.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <direct.h>

#define ROWS 32          /* records per table */

/* Frozen DBF "last update" stamp: 2026-01-01. The S4FOX build stores the year
 * as year % 100 (u4util.c:1010-1027, DBF-FORMAT.md §2). */
#define FROZEN_YY 26
#define FROZEN_MM  1
#define FROZEN_DD  1

/* ---------------------------------------------------------------- helpers */

static int fail( CODE4 *cb, const char *what )
{
   fprintf( stderr, "  ERROR: %s (errorCode=%d)\n", what, (int)error4code( cb ) );
   return 1;
}

/* Overwrite the header date stamp with a constant, so regenerating the corpus
 * on a different day produces identical bytes. */
static int freezeDateStamp( const char *path )
{
   unsigned char stamp[3];
   FILE *fp = fopen( path, "r+b" );

   if ( fp == 0 )
   {
      fprintf( stderr, "  ERROR: cannot reopen %s to freeze date stamp\n", path );
      return 1;
   }

   stamp[0] = FROZEN_YY;
   stamp[1] = FROZEN_MM;
   stamp[2] = FROZEN_DD;

   if ( fseek( fp, 1, SEEK_SET ) != 0 || fwrite( stamp, 1, 3, fp ) != 3 )
   {
      fclose( fp );
      fprintf( stderr, "  ERROR: cannot write date stamp in %s\n", path );
      return 1;
   }

   fclose( fp );
   return 0;
}

/* ------------------------------------------------------------- shared data
 *
 * Deterministic, index-driven values. Rows 1-3 of every table carry the edge
 * cases (zero / minimum / blank); the rest vary so later index cases have
 * something to sort.
 */

static const char *const DATES[ROWS] =
{
   "19000101", "19501231", "        ", "20000101", "20000229", "20200229", "20240229", "20251231",
   "20260101", "20260115", "20260228", "20260301", "20260630", "20260701", "20261231", "20270101",
   "19700101", "19801231", "19851115", "19900620", "19950704", "19980817", "20010911", "20050427",
   "20100302", "20120229", "20150818", "20180505", "20191231", "20220314", "20230801", "19991231"
};

static const char *const NAMES[8] =
{
   "ALPHA", "bravo", "Charlie Delta", "ECHO", "", "Golf-Hotel", "india juliett", "KILO"
};

/* "YYYYMMDDHH:MM:SS" — the format f4assignDateTime expects (F4FIELD.C:2008).
 * Returns 0 for the row that must stay blank: f4assignDateTime reads the time
 * at dateTime+8 and would run off the end of a shorter string, so the blank
 * case is produced by leaving d4blank's zeros alone instead. */
static const char *dateTimeFor( int i, char *buf )
{
   if ( i == 2 )
      return 0;

   sprintf( buf, "%s%02d:%02d:%02d", DATES[i], ( i * 7 ) % 24, ( i * 13 ) % 60, ( i * 29 ) % 60 );
   return buf;
}

static const char *codeFor( int i, char *buf )
{
   if ( i == 2 )
      buf[0] = 0;                       /* empty -> blank field */
   else
      sprintf( buf, "CODE%04d", i + 1 );
   return buf;
}

static double numFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -9999999.999;
   if ( i == 2 ) return 99999999.999;
   return i * 1234.567 - 5000.0;
}

static double floatFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -999999.9999;    /* widest that fits N(12,4) with a sign */
   if ( i == 2 ) return 9999999.9999;
   return i * 3.1416 - 20.0;
}

static long intFor( int i )
{
   if ( i == 0 ) return 0L;
   if ( i == 1 ) return -2147483647L - 1L;      /* LONG_MIN, written this way to
                                                 * avoid the unary-minus warning */
   if ( i == 2 ) return 2147483647L;
   return i * 1000003L - 16000000L;
}

static double doubleFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -0.0;                   /* sorts through the positive
                                                 * path; see KEY-COLLATION.md */
   if ( i == 2 ) return 3.141592653589793;
   if ( i == 3 ) return 1e15;
   if ( i == 4 ) return -1e-15;
   return i * 0.000125 - 2.5;
}

static double currencyFor( int i )
{
   if ( i == 0 ) return 0.0;
   if ( i == 1 ) return -12345.6789;
   if ( i == 2 ) return 99999999.9999;
   return i * 111.1111 - 900.0;
}

static const char *logicalFor( int i )
{
   return ( i % 3 == 0 ) ? "T" : ( ( i % 3 == 1 ) ? "F" : "T" );
}

/* Memo payload lengths, cycled. 504 = exactly one 512-byte FPT block once the
 * 8-byte block header is added; 505 is the first length needing two blocks
 * (FPT-MEMO.md §3.3). 0 means "no memo". */
static const int MEMO_LENS[8] = { 0, 1, 7, 63, 200, 503, 504, 505 };

/* Binary memo payloads: short on purpose, see caseVfpMemo. */
static const int BIN_LENS[4] = { 0, 1, 5, 16 };

/* Deterministic printable memo text of the requested length. */
static void memoText( int i, char *buf, int len )
{
   static const char *const WORDS =
      "the quick brown fox jumps over the lazy dog 0123456789 ";
   int wordsLen = (int)strlen( WORDS );
   int k;

   for ( k = 0; k < len; k++ )
      buf[k] = WORDS[ ( k + i * 3 ) % wordsLen ];
   buf[len] = 0;
}

/* Deterministic binary memo payload: every byte value occurs. */
static void memoBinary( int i, char *buf, int len )
{
   int k;
   for ( k = 0; k < len; k++ )
      buf[k] = (char)( ( k * 7 + i * 31 ) & 0xFF );
}

/* ------------------------------------------------------------ dump writing */

/* Escape a byte string into C-ish text: printable ASCII verbatim, everything
 * else as \xHH. Keeps the dump diffable and unambiguous. */
static void dumpEscaped( FILE *fp, const char *p, unsigned long len )
{
   unsigned long k;

   fputc( '"', fp );
   for ( k = 0; k < len; k++ )
   {
      unsigned char c = (unsigned char)p[k];
      if ( c == '"' || c == '\\' )
         fprintf( fp, "\\%c", c );
      else if ( c >= 0x20 && c <= 0x7E )
         fputc( c, fp );
      else
         fprintf( fp, "\\x%02X", c );
   }
   fputc( '"', fp );
}

/* Header and field descriptors read straight from the file, not through the
 * API. This matters: d4create rewrites some creation types before storing them
 * ('X' -> 'M', 'Z' -> 'C', both with nullBinary bit 0x04, DBF-FORMAT.md §5),
 * and the API reports the creation type back. The port must match the bytes,
 * so the bytes are what the dump records. */
static int dumpRawHeaderAndDescriptors( FILE *out, const char *path )
{
   unsigned char h[32], d[32];
   unsigned headerLen;
   int nFields, i;
   FILE *fp = fopen( path, "rb" );

   if ( fp == 0 || fread( h, 1, 32, fp ) != 32 )
   {
      if ( fp ) fclose( fp );
      fprintf( stderr, "  ERROR: cannot read header of %s\n", path );
      return 1;
   }

   headerLen = (unsigned)( h[8] | ( h[9] << 8 ) );

   fprintf( out, "version      0x%02X\n", h[0] );
   fprintf( out, "lastUpdate   %02d-%02d-%02d  (yy mm dd, frozen)\n", h[1], h[2], h[3] );
   fprintf( out, "numRecs      %lu\n",
            (unsigned long)( h[4] | ( h[5] << 8 ) | ( h[6] << 16 ) | ( (unsigned long)h[7] << 24 ) ) );
   fprintf( out, "headerLen    %u\n", headerLen );
   fprintf( out, "recordLen    %u\n", (unsigned)( h[10] | ( h[11] << 8 ) ) );
   fprintf( out, "hasMdxMemo   0x%02X\n", h[28] );
   fprintf( out, "codePage     0x%02X\n", h[29] );

   /* Descriptors run from offset 32 up to the 0x0D terminator. */
   nFields = (int)( ( headerLen - 32 - 1 ) / 32 );

   fprintf( out, "\n[descriptors]   (as stored on disk)\n" );
   for ( i = 0; i < nFields; i++ )
   {
      char name[12];

      if ( fread( d, 1, 32, fp ) != 32 )
      {
         fclose( fp );
         fprintf( stderr, "  ERROR: truncated descriptor %d in %s\n", i + 1, path );
         return 1;
      }
      if ( d[0] == 0x0D )     /* terminator reached early (long-field-name form) */
         break;

      memcpy( name, d, 11 );
      name[11] = 0;

      fprintf( out, "%d %-10s type=%c offset=%lu len=%u dec=%u flags=0x%02X hasTag=%u\n",
               i + 1, name, d[11],
               (unsigned long)( d[12] | ( d[13] << 8 ) | ( d[14] << 16 ) | ( (unsigned long)d[15] << 24 ) ),
               d[16], d[17], d[18], d[31] );
   }

   fclose( fp );
   return 0;
}

static int isMemoType( int type )
{
   return type == r4memo || type == r4gen || type == r4memoBin;
}

/* Reopen a finished table and write <NAME>.dump.txt beside it. */
static int dumpTable( CODE4 *cb, const char *outDir, const char *fileName )
{
   char dbfPath[520], dumpPath[520];
   const char *dot;
   DATA4 *data;
   FILE *out;
   int nFields, i;

   sprintf( dbfPath, "%s\\%s", outDir, fileName );

   dot = strrchr( fileName, '.' );
   sprintf( dumpPath, "%s\\%.*s.dump.txt", outDir,
            dot ? (int)( dot - fileName ) : (int)strlen( fileName ), fileName );

   out = fopen( dumpPath, "wb" );      /* "wb": LF endings, so the dump is
                                        * identical on any host */
   if ( out == 0 )
   {
      fprintf( stderr, "  ERROR: cannot create %s\n", dumpPath );
      return 1;
   }

   fprintf( out, "# CodeBase.NET corpus dump\n" );
   fprintf( out, "# Generated by test-files-generator from the original CodeBase C library.\n" );
   fprintf( out, "# Do not edit by hand; regenerate instead.\n" );
   fprintf( out, "file         %s\n", fileName );

   if ( dumpRawHeaderAndDescriptors( out, dbfPath ) != 0 )
   {
      fclose( out );
      return 1;
   }

   data = d4open( cb, dbfPath );
   if ( data == 0 )
   {
      fclose( out );
      return fail( cb, "reopen for dump" );
   }

   nFields = d4numFields( data );

   fprintf( out, "\n[fields]        (as the C library reports them after open)\n" );
   fprintf( out, "recCount %ld numFields %d\n", (long)d4recCount( data ), nFields );
   for ( i = 1; i <= nFields; i++ )
   {
      FIELD4 *f = d4fieldJ( data, (short)i );
      fprintf( out, "%d %s type=%c len=%d dec=%d\n",
               i, f4name( f ), f4type( f ), (int)f4len( f ), (int)f4decimals( f ) );
   }

   fprintf( out, "\n[records]\n" );
   for ( d4top( data ); !d4eof( data ); d4skip( data, 1 ) )
   {
      fprintf( out, "rec %ld deleted=%d\n", (long)d4recNo( data ), d4deleted( data ) ? 1 : 0 );

      for ( i = 1; i <= nFields; i++ )
      {
         FIELD4 *f    = d4fieldJ( data, (short)i );
         int     type = f4type( f );

         fprintf( out, "  %-10s ", f4name( f ) );

         if ( isMemoType( type ) )
         {
            unsigned long len = f4memoLen( f );

            /* The in-record memo reference first (4-byte binary or 10-byte
             * ASCII block id, FPT-MEMO.md §3.4), then the memo contents. */
            fprintf( out, "ref=" );
            dumpEscaped( out, f4ptr( f ), (unsigned long)f4len( f ) );
            fprintf( out, " len=%lu ", len );
            dumpEscaped( out, f4memoPtr( f ), len );
         }
         else
         {
            dumpEscaped( out, f4ptr( f ), (unsigned long)f4len( f ) );

            switch ( type )
            {
               case r4num:
               case r4float:
               case r4double:
               case r4currency:
                  fprintf( out, " dbl=%.17g", f4double( f ) );
                  break;
               case r4int:
                  fprintf( out, " long=%ld", (long)f4long( f ) );
                  break;
               case r4date:
                  fprintf( out, " str=[%s]", f4str( f ) );
                  break;
               case r4dateTime:
                  /* f4str does not decode datetimes; f4dateTime does
                   * (F4FIELD.C:1940). Blank datetimes come back empty. */
                  fprintf( out, " str=[%s]", f4dateTime( f ) );
                  break;
               default:
                  break;
            }
         }
         fprintf( out, "\n" );
      }
   }

   d4close( data );
   fclose( out );
   printf( "  dump -> %.*s.dump.txt\n",
           dot ? (int)( dot - fileName ) : (int)strlen( fileName ), fileName );
   return 0;
}

/* Close, freeze the stamp, then dump. Common tail of every case. */
static int finish( CODE4 *cb, DATA4 *data, const char *outDir, const char *fileName )
{
   char path[520];

   if ( d4close( data ) < 0 )
      return fail( cb, "d4close" );

   sprintf( path, "%s\\%s", outDir, fileName );
   if ( freezeDateStamp( path ) != 0 )
      return 1;

   return dumpTable( cb, outDir, fileName );
}

/* ---------------------------------------------------------- case: DB3TYPE
 *
 * dBase III / FoxPro 2.x field set (C, N, D, L) with no memo. compatibility 25
 * makes this a version 0x03 table, byte-identical in shape to the dBase III
 * files in original/examples/DATA that the port must read.
 */
static int caseDb3Type( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name             type  len  dec  nulls */
      { (char *)"CODE",     'C',  10,   0,   0 },
      { (char *)"NAME",     'C',  20,   0,   0 },
      { (char *)"QTY",      'N',   6,   0,   0 },
      { (char *)"PRICE",    'N',  10,   2,   0 },
      { (char *)"HIREDATE", 'D',   8,   0,   0 },
      { (char *)"ACTIVE",   'L',   1,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32];
   DATA4 *data;
   int i;

   sprintf( path, "%s\\DB3TYPE.DBF", outDir );
   printf( "DB3TYPE.DBF (0x03, dBase III types) ... " );

   cb->compatibility = 25;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create DB3TYPE.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assign      ( d4fieldJ( data, 1 ), codeFor( i, buf ) );
      f4assign      ( d4fieldJ( data, 2 ), NAMES[ i % 8 ] );
      f4assignDouble( d4fieldJ( data, 3 ), (double)( ( i * 37 ) % 1000 - 500 ) );
      f4assignDouble( d4fieldJ( data, 4 ), i * 13.75 - 100.5 );
      f4assign      ( d4fieldJ( data, 5 ), DATES[i] );
      f4assign      ( d4fieldJ( data, 6 ), logicalFor( i ) );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "DB3TYPE.DBF" );
}

/* ---------------------------------------------------------- case: VFPTYPE
 *
 * Every Visual FoxPro field type that is not a memo: C N F D L I B Y T.
 * CodeBase-only types (H, W, Q, V, ...) are deliberately excluded so the file
 * keeps genuine-VFP shape.
 */
static int caseVfpType( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"F_C",    'C',  20,   0,   0 },
      { (char *)"F_N",    'N',  12,   3,   0 },
      { (char *)"F_F",    'F',  12,   4,   0 },
      { (char *)"F_D",    'D',   8,   0,   0 },
      { (char *)"F_L",    'L',   1,   0,   0 },
      { (char *)"F_I",    'I',   4,   0,   0 },
      { (char *)"F_B",    'B',   8,   6,   0 },
      { (char *)"F_Y",    'Y',   8,   4,   0 },
      { (char *)"F_T",    'T',   8,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32];
   const char *dt;
   DATA4 *data;
   int i;

   sprintf( path, "%s\\VFPTYPE.DBF", outDir );
   printf( "VFPTYPE.DBF (0x30, all non-memo VFP types) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create VFPTYPE.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assign        ( d4fieldJ( data, 1 ), NAMES[ i % 8 ] );
      f4assignDouble  ( d4fieldJ( data, 2 ), numFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 3 ), floatFor( i ) );
      f4assign        ( d4fieldJ( data, 4 ), DATES[i] );
      f4assign        ( d4fieldJ( data, 5 ), logicalFor( i ) );
      f4assignLong    ( d4fieldJ( data, 6 ), intFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 7 ), doubleFor( i ) );
      f4assignDouble  ( d4fieldJ( data, 8 ), currencyFor( i ) );

      dt = dateTimeFor( i, buf );
      if ( dt != 0 )
         f4assignDateTime( d4fieldJ( data, 9 ), dt );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "VFPTYPE.DBF" );
}

/* ---------------------------------------------------------- case: F2XMEMO
 *
 * FoxPro 2.x table with a memo: version 0xF5 plus an .FPT whose record memo
 * references are the 10-byte ASCII form (FPT-MEMO.md §3.4), a different code
 * path from VFP's 4-byte binary reference.
 *
 * NOTE: genuine dBase III memo (version 0x83 + .DBT) cannot be produced from
 * this build — it is S4MNDX-only (DBF-FORMAT.md §2.1) — and .DBT is outside
 * the port's scope. This is the closest reachable legacy-memo case.
 */
static int caseF2xMemo( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name              type  len  dec  nulls */
      { (char *)"CODE",      'C',  10,   0,   0 },
      { (char *)"QTY",       'N',   6,   0,   0 },
      { (char *)"ENTRYDATE", 'D',   8,   0,   0 },
      { (char *)"ACTIVE",    'L',   1,   0,   0 },
      { (char *)"NOTES",     'M',  10,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], buf[32], memo[600];
   DATA4 *data;
   int i, len;

   sprintf( path, "%s\\F2XMEMO.DBF", outDir );
   printf( "F2XMEMO.DBF (0xF5 + FPT, 10-byte memo refs) ... " );

   cb->compatibility = 25;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create F2XMEMO.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      len = MEMO_LENS[ i % 8 ];
      memoText( i, memo, len );

      f4assign      ( d4fieldJ( data, 1 ), codeFor( i, buf ) );
      f4assignDouble( d4fieldJ( data, 2 ), (double)( ( i * 41 ) % 1000 - 500 ) );
      f4assign      ( d4fieldJ( data, 3 ), DATES[i] );
      f4assign      ( d4fieldJ( data, 4 ), logicalFor( i ) );
      f4memoAssignN ( d4fieldJ( data, 5 ), memo, (unsigned)len );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "F2XMEMO.DBF" );
}

/* ---------------------------------------------------------- case: VFPMEMO
 *
 * VFP table with memo and the binary variants: 'M' text memo, 'X' binary memo
 * and 'Z' binary character (both stored as 'M'/'C' with nullBinary bit 0x04,
 * DBF-FORMAT.md §5), plus 'G' general. Memo references are the 4-byte binary
 * form. Payload lengths straddle the 512-byte FPT block boundary.
 */
static int caseVfpMemo( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name            type  len  dec  nulls */
      { (char *)"ID",      'I',   4,   0,   0 },
      { (char *)"NAME",    'C',  16,   0,   0 },
      { (char *)"NOTES",   'M',   4,   0,   0 },
      { (char *)"BINMEMO", 'X',   4,   0,   0 },
      { (char *)"GEN",     'G',   4,   0,   0 },
      { (char *)"BINCHAR", 'Z',   8,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], memo[600], binary[600], binChar[8];
   DATA4 *data;
   int i, k, len;

   sprintf( path, "%s\\VFPMEMO.DBF", outDir );
   printf( "VFPMEMO.DBF (0x30 + FPT, memo/binary types) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create VFPMEMO.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      binChar[0] = (char)0x00;  binChar[1] = (char)0x01;  binChar[2] = (char)0x7F;
      binChar[3] = (char)0x80;  binChar[4] = (char)0xFF;  binChar[5] = (char)i;
      binChar[6] = (char)0xAA;  binChar[7] = (char)0x55;

      f4assignLong( d4fieldJ( data, 1 ), intFor( i ) );
      f4assign    ( d4fieldJ( data, 2 ), NAMES[ i % 8 ] );

      len = MEMO_LENS[ i % 8 ];
      memoText( i, memo, len );
      f4memoAssignN( d4fieldJ( data, 3 ), memo, (unsigned)len );

      /* Binary memos stay short — the FPT block-boundary cases are covered by
       * NOTES above; these only need to prove byte-transparent storage. */
      len = BIN_LENS[ i % 4 ];
      memoBinary( i, binary, len );
      f4memoAssignN( d4fieldJ( data, 4 ), binary, (unsigned)len );

      /* General stays mostly empty — it keeps the .FPT small and still covers
       * the "general field with content" path. */
      if ( i % 8 == 3 )
      {
         for ( k = 0; k < 24; k++ )
            binary[k] = (char)( 0xE0 - k );
         f4memoAssignN( d4fieldJ( data, 5 ), binary, 24 );
      }

      f4assignN( d4fieldJ( data, 6 ), binChar, 8 );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );
   return finish( cb, data, outDir, "VFPMEMO.DBF" );
}

/* ------------------------------------------------------------------- main */

int main( int argc, char **argv )
{
   const char *outDir = ( argc > 1 ) ? argv[1] : "bin\\out";
   CODE4 cb;
   int rc;

   _mkdir( outDir );   /* fine if it already exists */

   code4init( &cb );
   cb.safety = 0;      /* overwrite existing output */
   cb.errOff = 1;      /* report failures ourselves, no message boxes */
                       /* cb.compatibility is set per case */

   printf( "CodeBase test-file generator\n" );
   printf( "output: %s\n\n", outDir );

   rc = caseDb3Type( &cb, outDir );
   if ( rc == 0 ) rc = caseVfpType( &cb, outDir );
   if ( rc == 0 ) rc = caseF2xMemo( &cb, outDir );
   if ( rc == 0 ) rc = caseVfpMemo( &cb, outDir );

   code4close( &cb );
   code4initUndo( &cb );

   printf( "\n%s\n", rc == 0 ? "OK" : "FAILED" );
   return rc;
}
