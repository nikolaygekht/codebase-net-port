/* ==========================================================================
 * case-idxone.cpp — IDXONE.DBF + IDXONE.cdx + IDXONE.IDX
 *
 * The single-tag case. CodeBase *reads* a single-tag index file — a tag header
 * at file offset 0 with typeCode < 0x40, the tag named after the file
 * (i4index.c:1694, 1814-1825) — but it cannot *write* one: i4create always
 * builds a compound file with a tag directory at typeCode 0xE0
 * (i4create.c:847). There is no .IDX anywhere in the source drop either.
 *
 * So this case builds a compound index holding exactly one tag and derives the
 * single-tag file from it with the smallest edit that exists (ADR-25):
 *
 *   1. copy the 1024-byte tag header from offset 1024 to offset 0
 *   2. clear the compound bit in the copy: typeCode 0x60 -> 0x20
 *
 * Nothing else moves. Node numbers are byte offsets, so leaving every tree
 * block exactly where it is keeps root, both sibling pointers and every child
 * pointer valid; the original header at 1024 becomes unreferenced space, which
 * the format tolerates because freed blocks are ordinary. The C library then
 * opens the result, d4check re-derives every key from the records, and the dump
 * is written from what the library read — so the expected values are its
 * reading of the file, not our writing of it.
 *
 * Both files stay in the corpus: the same tree read through both shapes must
 * yield the same keys, which is the check that the derivation preserved it.
 * The key is 40 bytes wide so the tree is three levels deep, and the derivation
 * therefore has to preserve interior-node child pointers rather than only a
 * root pointer.
 *
 * The data below belongs to this case alone. Other cases keep their own copies
 * on purpose — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "util.h"
#include "dump-index.h"
#include "cases.h"

#define ROWS 300                 /* records in this table */
#define HEADER4SLOT 1024         /* a tag header occupies two 512-byte nodes */
#define OFF4TYPE_CODE 0x0E       /* typeCode within a tag header (CDX-FORMAT.md §3) */
#define BIT4COMPOUND 0x40        /* i4index.c:1760 tests typeCode >= 0x40 */

/* --------------------------------------------------------------- test data */

/* K_WIDE, C(40) — unique and incompressible, so leaves hold few keys and the
 * tree needs three levels for only 300 rows. Its own scramble, deliberately
 * different from CDXDEEP's. */
static void wideBytes( int i, char *out )
{
   static const char ALPHA[] = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
   int scrambled = ( i * 211 ) % 500;
   int k;

   sprintf( out, "%03d:", scrambled );
   for ( k = 4; k < 40; k++ )
      out[k] = ALPHA[ ( scrambled * 11 + k * 17 ) % 36 ];
}

/* -------------------------------------------------------------- derivation */

static int deriveIdx( const char *cdxPath, const char *idxPath )
{
   unsigned char *image;
   long size;
   FILE *fp;
   size_t got;

   fp = fopen( cdxPath, "rb" );
   if ( fp == 0 )
   {
      fprintf( stderr, "  ERROR: cannot read %s\n", cdxPath );
      return 1;
   }

   fseek( fp, 0, SEEK_END );
   size = ftell( fp );
   fseek( fp, 0, SEEK_SET );

   if ( size < 2 * HEADER4SLOT )
   {
      fclose( fp );
      fprintf( stderr, "  ERROR: %s is too small to hold a tag header\n", cdxPath );
      return 1;
   }

   image = (unsigned char *)malloc( (size_t)size );
   if ( image == 0 )
   {
      fclose( fp );
      fprintf( stderr, "  ERROR: out of memory for %s\n", cdxPath );
      return 1;
   }

   got = fread( image, 1, (size_t)size, fp );
   fclose( fp );
   if ( got != (size_t)size )
   {
      free( image );
      fprintf( stderr, "  ERROR: short read of %s\n", cdxPath );
      return 1;
   }

   /* The two edits, and nothing else. */
   memcpy( image, image + HEADER4SLOT, HEADER4SLOT );
   if ( ( image[OFF4TYPE_CODE] & BIT4COMPOUND ) == 0 )
   {
      free( image );
      fprintf( stderr, "  ERROR: tag header typeCode 0x%02X is not compound\n",
               (unsigned)image[OFF4TYPE_CODE] );
      return 1;
   }
   image[OFF4TYPE_CODE] = (unsigned char)( image[OFF4TYPE_CODE] & ~BIT4COMPOUND );

   fp = fopen( idxPath, "wb" );
   if ( fp == 0 )
   {
      free( image );
      fprintf( stderr, "  ERROR: cannot create %s\n", idxPath );
      return 1;
   }

   if ( fwrite( image, 1, (size_t)size, fp ) != (size_t)size )
   {
      fclose( fp );
      free( image );
      fprintf( stderr, "  ERROR: short write of %s\n", idxPath );
      return 1;
   }

   fclose( fp );
   free( image );
   return 0;
}

/* ------------------------------------------------------------- verification
 *
 * d4check cannot validate a single-tag file: i4checkBlocks flags the tag
 * directory's header blocks and then flags every tag's header, and in a
 * single-tag file those are the same block, so it always reports the file as
 * corrupt (i4check.c:889-914; see dump-index.cpp). The derived file is therefore
 * witnessed a different way, and a stronger one for what the derivation actually
 * claims: **the same tree, read through both file shapes, must produce the same
 * keys.** d4check has already certified the .cdx, whose tree blocks these are —
 * byte for byte, since the derivation moved none of them — so what is left to
 * prove is only that the header at offset 0 with the compound bit cleared reads
 * as one tag over that same tree. Walking both and comparing every key and
 * record number proves exactly that.
 */
static int compareWalks( CODE4 *cb, const char *outDir, const char *dbfName,
                         const char *nameA, const char *nameB )
{
   char dbfPath[520], pathA[520], pathB[520];
   DATA4 *data;
   INDEX4 *ia, *ib;
   TAG4FILE *ta, *tb;
   int savedAutoOpen, keyLen, rc = 0;
   long n = 0;

   sprintf( dbfPath, "%s\\%s", outDir, dbfName );
   sprintf( pathA, "%s\\%s", outDir, nameA );
   sprintf( pathB, "%s\\%s", outDir, nameB );

   savedAutoOpen = cb->autoOpen;
   cb->autoOpen = 0;
   data = d4open( cb, dbfPath );
   cb->autoOpen = savedAutoOpen;
   if ( data == 0 )
      return fail( cb, "reopen for walk comparison" );

   ia = i4open( data, pathA );
   ib = ( ia == 0 ) ? 0 : i4open( data, pathB );
   if ( ia == 0 || ib == 0 )
   {
      d4close( data );
      return fail( cb, "i4open for walk comparison" );
   }

   ta = (TAG4FILE *)l4first( &ia->indexFile->tags );
   tb = (TAG4FILE *)l4first( &ib->indexFile->tags );
   if ( ta == 0 || tb == 0 )
   {
      d4close( data );
      fprintf( stderr, "  ERROR: an index file has no tag\n" );
      return 1;
   }

   keyLen = (int)ta->header.keyLen;
   if ( keyLen != (int)tb->header.keyLen )
   {
      d4close( data );
      fprintf( stderr, "  ERROR: key lengths differ: %d vs %d\n", keyLen, (int)tb->header.keyLen );
      return 1;
   }

   if ( tfile4top( ta ) < 0 || tfile4top( tb ) < 0 )
   {
      d4close( data );
      return fail( cb, "tfile4top for walk comparison" );
   }

   for ( ;; )
   {
      char *ka = tfile4key( ta );
      char *kb = tfile4key( tb );
      long movedA, movedB;

      if ( ka == 0 || kb == 0 )
      {
         fprintf( stderr, "  ERROR: tfile4key failed at key %ld\n", n );
         rc = 1;
         break;
      }
      if ( memcmp( ka, kb, (size_t)keyLen ) != 0 || tfile4recNo( ta ) != tfile4recNo( tb ) )
      {
         fprintf( stderr, "  ERROR: %s and %s disagree at key %ld\n", nameA, nameB, n );
         rc = 1;
         break;
      }

      n++;
      movedA = tfile4dskip( ta, 1L );
      movedB = tfile4dskip( tb, 1L );
      if ( movedA != movedB )
      {
         fprintf( stderr, "  ERROR: %s and %s end at different keys (%ld)\n", nameA, nameB, n );
         rc = 1;
         break;
      }
      if ( movedA != 1L )
         break;
   }

   if ( rc == 0 )
      printf( "  %s and %s agree on all %ld keys\n", nameA, nameB, n );

   if ( d4close( data ) < 0 )
      return fail( cb, "d4close after walk comparison" );

   return rc;
}

/* ------------------------------------------------------------------- case */

int caseIdxOne( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"K_WIDE", 'C',  40,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   /* One tag only: the derivation needs the file to hold exactly one, and its
    * name is irrelevant in the .IDX, where the file name supplies it. */
   static TAG4INFO tags[] =
   {
      { (char *)"X_WIDE", (char *)"K_WIDE", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], cdxPath[520], idxPath[520], wide[41];
   DATA4 *data;
   int i, rc;

   sprintf( path, "%s\\IDXONE.DBF", outDir );
   sprintf( cdxPath, "%s\\IDXONE.cdx", outDir );
   sprintf( idxPath, "%s\\IDXONE.IDX", outDir );
   printf( "IDXONE.DBF (0x30 + CDX + derived single-tag IDX) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create IDXONE.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      wideBytes( i, wide );

      f4assignLong( d4fieldJ( data, 1 ), (long)i );
      f4assignN   ( d4fieldJ( data, 2 ), wide, 40 );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   if ( i4create( data, 0, tags ) == 0 )
   {
      d4close( data );
      return fail( cb, "i4create IDXONE.cdx" );
   }

   printf( "%d records, 1 tag\n", ROWS );

   rc = finish( cb, data, outDir, "IDXONE.DBF" );
   if ( rc == 0 )
      rc = deriveIdx( cdxPath, idxPath );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "IDXONE.DBF", "IDXONE.cdx" );
   if ( rc == 0 )
      rc = dumpIndex( cb, outDir, "IDXONE.DBF", "IDXONE.IDX" );
   if ( rc == 0 )
      rc = compareWalks( cb, outDir, "IDXONE.DBF", "IDXONE.cdx", "IDXONE.IDX" );

   return rc;
}
