/* ==========================================================================
 * util.cpp — error reporting, the frozen header date stamp, and the common
 * close/freeze/dump tail every case ends with.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump.h"

/* Frozen DBF "last update" stamp: 2026-01-01. The S4FOX build stores the year
 * as year % 100 (u4util.c:1010-1027, DBF-FORMAT.md §2). */
#define FROZEN_YY 26
#define FROZEN_MM  1
#define FROZEN_DD  1

int fail( CODE4 *cb, const char *what )
{
   fprintf( stderr, "  ERROR: %s (errorCode=%d)\n", what, (int)error4code( cb ) );
   return 1;
}

void assignText( FIELD4 *field, const TEXTBYTES *text )
{
   f4assignN( field, (const char *)text->bytes, text->len );
}

void dumpEscapedBytes( FILE *fp, const char *p, unsigned long len )
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

/* Overwrite the header date stamp with a constant, so regenerating the corpus
 * on a different day produces identical bytes. This is the only place the
 * generator alters what the C library wrote. */
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

int finish( CODE4 *cb, DATA4 *data, const char *outDir, const char *fileName )
{
   char path[520];

   if ( d4close( data ) < 0 )
      return fail( cb, "d4close" );

   sprintf( path, "%s\\%s", outDir, fileName );
   if ( freezeDateStamp( path ) != 0 )
      return 1;

   return dumpTable( cb, outDir, fileName );
}
