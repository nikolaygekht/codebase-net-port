/* ==========================================================================
 * case-flags30.cpp — FLAGS30.DBF + FLAGS30.fpt
 *
 * The CodeBase feature flags (header bytes 12-19, DBF-FORMAT.md section 2.2)
 * on a table that is not version 0x31. The reader looks at them only when the
 * version byte is 0x31 (D4OPEN.C:2150-2200); on any other version it never
 * reads them. So this case writes an ordinary 0x30 table with a memo, then
 * sets every one of the eight bytes to 1 — the one value a flag reads as set,
 * including the three flags no build knows — and dumps it through d4open. The
 * dump is the witness that the reference reads such a file as an ordinary
 * table: ordinary descriptors (not the long-name layout flags[4] would mean),
 * plain memos (not the compressed ones flags[1] would mean), plain records
 * (not the compressed data flags[2] would mean), and no refusal.
 *
 * The flag bytes are not in the dump itself — the dump's header section has
 * never printed them, and adding them would change every other dump. The file
 * carries them, and the port's gate reads them from it.
 *
 * Added 2026-11-12, step 013 sub-step 7a-1. The data below belongs to this case
 * alone — see cases.h.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>

#include "util.h"
#include "dump.h"
#include "cases.h"

#define ROWS 4           /* records in this table */

/* --------------------------------------------------------------- test data */

static const char *NAMES[ROWS] = { "first", "second", "", "fourth" };
static const char *NOTES[ROWS] = { "a note", "", "another note", "the last note" };

/* Every flag byte set, written over the header after the table is closed. */
static int setEveryFlag( const char *path )
{
   static const unsigned char ones[8] = { 1, 1, 1, 1, 1, 1, 1, 1 };
   FILE *fp = fopen( path, "r+b" );

   if ( fp == 0 )
   {
      fprintf( stderr, "  ERROR: cannot reopen %s to set its flag bytes\n", path );
      return 1;
   }

   if ( fseek( fp, 12, SEEK_SET ) != 0 || fwrite( ones, 1, 8, fp ) != 8 )
   {
      fclose( fp );
      fprintf( stderr, "  ERROR: cannot write the flag bytes in %s\n", path );
      return 1;
   }

   fclose( fp );
   return 0;
}

/* ------------------------------------------------------------------- case */

int caseFlags30( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name           type  len  dec  nulls */
      { (char *)"ID",     'I',   4,   0,   0 },
      { (char *)"NAME",   'C',  10,   0,   0 },
      { (char *)"NOTE",   'M',   4,   0,   0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520];
   DATA4 *data;
   int i, rc;

   sprintf( path, "%s\\FLAGS30.DBF", outDir );
   printf( "FLAGS30.DBF (0x30 + FPT, every feature-flag byte set) ... " );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create FLAGS30.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 )  { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      f4assignLong( d4fieldJ( data, 1 ), (long)( i + 1 ) );
      f4assign    ( d4fieldJ( data, 2 ), NAMES[i] );
      f4memoAssign( d4fieldJ( data, 3 ), NOTES[i] );

      if ( d4append( data ) < 0 )          { d4close( data ); return fail( cb, "d4append" ); }
   }

   printf( "%d records\n", ROWS );

   rc = finishUndumped( cb, data, outDir, "FLAGS30.DBF" );
   if ( rc == 0 )
      rc = setEveryFlag( path );
   if ( rc == 0 )
      rc = dumpTable( cb, outDir, "FLAGS30.DBF" );

   return rc;
}
