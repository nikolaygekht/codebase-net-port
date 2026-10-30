/* ==========================================================================
 * case-cmpmemo.cpp — CMPMEMO.DBF (+ CMPMEMO.fpt)
 *
 * A table created with CodeBase's compressed-memo extension switched on
 * (code4memoCompress, m4memo.c:25-48). That makes the table version 0x31 with
 * flags[1] = 1 (D4CREATE.C:1351-1355, 1424-1433), and from then on every memo
 * entry longer than one block is written as type 3: a 4-byte native length,
 * then a zlib stream (m4file.c:726-772, C4CODE.C:402-444). Visual FoxPro
 * cannot read such a file; the case exists so the port's reader of type 3 has
 * a reference to answer to.
 *
 * Payload lengths straddle the rule's boundary: 512 (the block size) stays
 * type 1, 513 is the first compressed. Text compresses well; the binary memo
 * carries pseudo-random bytes that do not, and the reference writes type 3
 * anyway — there is no "only if smaller". One entry is over 64 KiB, so its
 * length prefix needs more than 16 bits.
 *
 * Besides the ordinary dump, CMPMEMO.memo.dump.txt lists every entry as the
 * reference reads it *without* decompressing (memo4fileReadPart with
 * uncompress = 0): its block, its type, and the length stored on disk. That
 * is the evidence that the compressed path was taken, and which entries took
 * it.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "util.h"
#include "cases.h"

#define ROWS 8           /* records in this table */
#define MAX_LEN 66000    /* the longest payload below */

/* Text memo payload lengths. 512 is exactly the block size, which the
 * compression rule (ptrLen > blockSize, m4file.c:744) leaves uncompressed;
 * 513 is the first length it compresses. 66000 needs a 17-bit length prefix. */
static const int TEXT_LENS[ROWS] = { 0, 1, 511, 512, 513, 4096, 66000, 600 };

/* Binary memo payload lengths, pseudo-random bytes except the last row, which
 * holds every byte value. */
static const int BIN_LENS[ROWS]  = { 0, 1, 16, 512, 513, 2000, 0, 600 };

/* Deterministic printable memo text of the requested length: compresses well. */
static void memoText( int i, char *buf, int len )
{
   static const char *const WORDS =
      "the quick brown fox jumps over the lazy dog 0123456789 ";
   int wordsLen = (int)strlen( WORDS );
   int k;

   for ( k = 0; k < len; k++ )
      buf[k] = WORDS[ ( k + i * 3 ) % wordsLen ];
}

/* Deterministic bytes that do not compress: a fixed linear congruential
 * sequence (the Numerical Recipes constants), seeded by the row. */
static void memoNoise( int i, char *buf, int len )
{
   unsigned long state = 2463534242UL + (unsigned long)i;
   int k;

   for ( k = 0; k < len; k++ )
   {
      state = state * 1664525UL + 1013904223UL;
      buf[k] = (char)( ( state >> 24 ) & 0xFF );
   }
}

/* Every byte value, in order, repeated. */
static void memoAllBytes( char *buf, int len )
{
   int k;
   for ( k = 0; k < len; k++ )
      buf[k] = (char)( k & 0xFF );
}

/* Write <NAME>.memo.dump.txt: each memo entry as stored, not decompressed. */
static int dumpMemoEntries( CODE4 *cb, const char *outDir )
{
   char path[520];
   DATA4 *data;
   FILE *out;
   char *raw = 0;
   unsigned rawLen = 0;
   int i;

   sprintf( path, "%s\\CMPMEMO.DBF", outDir );
   data = d4open( cb, path );
   if ( data == 0 )
      return fail( cb, "d4open CMPMEMO.DBF" );

   sprintf( path, "%s\\CMPMEMO.memo.dump.txt", outDir );
   out = fopen( path, "wb" );
   if ( out == 0 )
   {
      d4close( data );
      printf( "FAILED: cannot write %s\n", path );
      return 1;
   }

   fprintf( out, "# CMPMEMO.fpt entries as stored: memo4fileReadPart with uncompress = 0\n" );
   fprintf( out, "# type 1 = text, 3 = compressed (4-byte length + zlib stream)\n" );
   fprintf( out, "blockSize %u\n\n[entries]\n", (unsigned)data->dataFile->memoFile.blockSize );

   for ( d4top( data ); !d4eof( data ); d4skip( data, 1 ) )
   {
      for ( i = 1; i <= d4numFields( data ); i++ )
      {
         FIELD4 *f = d4fieldJ( data, (short)i );
         long block, type = 0;
         unsigned len = 0;

         if ( f4type( f ) != r4memo && f4type( f ) != r4memoBin )
            continue;

         /* The 4-byte binary memo reference, little-endian (FPT-MEMO.md §3.4). */
         memcpy( &block, f4ptr( f ), sizeof( block ) );

         if ( block > 0 &&
              memo4fileReadPart( &data->dataFile->memoFile, block, &raw, &rawLen,
                                 0L, UINT_MAX - 100, &type, 0 ) < 0 )
         {
            fclose( out );
            d4close( data );
            return fail( cb, "memo4fileReadPart" );
         }
         if ( block > 0 )
            len = rawLen;

         fprintf( out, "rec %ld %-10s block=%ld type=%ld stored=%u\n",
                  (long)d4recNo( data ), f4name( f ), block, block > 0 ? type : 0L, len );
      }
   }

   if ( raw != 0 )
      u4free( raw );
   fclose( out );
   d4close( data );
   printf( "  dump -> CMPMEMO.memo.dump.txt\n" );
   return 0;
}

/* ------------------------------------------------------------------- case */

int caseCmpMemo( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name            type  len  dec  nulls */
      { (char *)"ID",      'I',   4,   0,   0 },
      { (char *)"NOTES",   'M',   4,   0,   0 },
      { (char *)"BINMEMO", 'X',   4,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   char *memo;
   DATA4 *data;
   int i, rc;

   sprintf( path, "%s\\CMPMEMO.DBF", outDir );
   printf( "CMPMEMO.DBF (0x31 + FPT, compressed memo entries) ... " );

   memo = (char *)malloc( MAX_LEN );
   if ( memo == 0 )
   {
      printf( "FAILED: out of memory\n" );
      return 1;
   }

   /* The switch is read at create (D4CREATE.C:2034) and must not leak into a
    * later case, so it is turned off again on every path out. */
   cb->compatibility = 30;
   if ( code4memoCompress( cb, 1 ) != 0 )
   {
      free( memo );
      return fail( cb, "code4memoCompress" );
   }
   data = d4create( cb, path, fields, 0 );
   code4memoCompress( cb, 0 );
   if ( data == 0 )
   {
      free( memo );
      return fail( cb, "d4create CMPMEMO.DBF" );
   }

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 ) { free( memo ); d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)( i + 1 ) );

      memoText( i, memo, TEXT_LENS[i] );
      f4memoAssignN( d4fieldJ( data, 2 ), memo, (unsigned)TEXT_LENS[i] );

      if ( i == ROWS - 1 )
         memoAllBytes( memo, BIN_LENS[i] );
      else
         memoNoise( i, memo, BIN_LENS[i] );
      f4memoAssignN( d4fieldJ( data, 3 ), memo, (unsigned)BIN_LENS[i] );

      if ( d4append( data ) < 0 )         { free( memo ); d4close( data ); return fail( cb, "d4append" ); }
   }

   free( memo );
   printf( "%d records\n", ROWS );

   rc = finish( cb, data, outDir, "CMPMEMO.DBF" );
   if ( rc == 0 )
      rc = dumpMemoEntries( cb, outDir );
   return rc;
}
