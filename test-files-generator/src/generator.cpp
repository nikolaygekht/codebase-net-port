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
 * NOTE ON DETERMINISM: the DBF header carries a "last update" date stamp
 * (bytes 1-3), so re-running on a different day changes those three bytes.
 * That is expected for now; see README.md.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <string.h>
#include <direct.h>

/* ---------------------------------------------------------------- helpers */

static int fail( CODE4 *cb, const char *what )
{
   fprintf( stderr, "  ERROR: %s (errorCode=%d)\n", what, (int)error4code( cb ) );
   return 1;
}

/* Append one record. `values` is parallel to the table's field order. */
static int appendRow( CODE4 *cb, DATA4 *data, const char *const *values, int nValues )
{
   int i;

   if ( d4appendStart( data, 0 ) < 0 )
      return fail( cb, "d4appendStart" );
   d4blank( data );

   for ( i = 0; i < nValues; i++ )
      f4assign( d4fieldJ( data, (short)( i + 1 ) ), values[i] );

   if ( d4append( data ) < 0 )
      return fail( cb, "d4append" );

   return 0;
}

/* ------------------------------------------------------------ case: SIMPLE
 *
 * The smallest useful table: three plain fields, three records, no index and
 * no memo. Its job is to prove the whole toolchain works end to end.
 */
static int caseSimple( CODE4 *cb, const char *outDir )
{
   static FIELD4INFO fields[] =
   {
      /* name          type  len  dec  nulls */
      { (char *)"ID",     'N',   5,   0,   0 },
      { (char *)"NAME",   'C',  20,   0,   0 },
      { (char *)"AMOUNT", 'N',  10,   2,   0 },
      { 0, 0, 0, 0, 0 }
   };

   static const char *rows[][3] =
   {
      { "1", "ALPHA",   "10.50" },
      { "2", "BRAVO",   "-3.25" },
      { "3", "CHARLIE",  "0.00" },
   };
   const int nRows = (int)( sizeof( rows ) / sizeof( rows[0] ) );

   char path[520];
   DATA4 *data;
   int i;

   sprintf( path, "%s\\SIMPLE.DBF", outDir );
   printf( "SIMPLE.DBF ... " );

   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create SIMPLE.DBF" );

   for ( i = 0; i < nRows; i++ )
   {
      if ( appendRow( cb, data, rows[i], 3 ) != 0 )
      {
         d4close( data );
         return 1;
      }
   }

   if ( d4close( data ) < 0 )
      return fail( cb, "d4close SIMPLE.DBF" );

   printf( "%d records\n", nRows );
   return 0;
}

/* ------------------------------------------------------------ verification
 *
 * Reopen what we just wrote and read it back, so a broken file is caught here
 * rather than three milestones downstream.
 */
static int verify( CODE4 *cb, const char *outDir, const char *fileName )
{
   char path[520];
   DATA4 *data;
   int nFields, i;
   long nRecs;

   sprintf( path, "%s\\%s", outDir, fileName );

   data = d4open( cb, path );
   if ( data == 0 )
      return fail( cb, "reopen for verification" );

   nRecs   = (long)d4recCount( data );
   nFields = d4numFields( data );
   printf( "  verify %s: %ld records, %d fields\n", fileName, nRecs, nFields );

   for ( i = 1; i <= nFields; i++ )
   {
      FIELD4 *f = d4fieldJ( data, (short)i );
      printf( "    %-10s %c len=%-3d dec=%d\n",
              f4name( f ), f4type( f ), (int)f4len( f ), (int)f4decimals( f ) );
   }

   for ( d4top( data ); !d4eof( data ); d4skip( data, 1 ) )
   {
      printf( "    rec %ld:", (long)d4recNo( data ) );
      for ( i = 1; i <= nFields; i++ )
         printf( " [%s]", f4str( d4fieldJ( data, (short)i ) ) );
      printf( "\n" );
   }

   d4close( data );
   return 0;
}

/* ------------------------------------------------------------------- main */

int main( int argc, char **argv )
{
   const char *outDir = ( argc > 1 ) ? argv[1] : "bin\\out";
   CODE4 cb;
   int rc;

   _mkdir( outDir );   /* fine if it already exists */

   code4init( &cb );
   cb.safety        = 0;    /* overwrite existing output */
   cb.compatibility = 30;   /* Visual FoxPro (0x30) table format */
   cb.errOff        = 1;    /* report failures ourselves, no message boxes */

   printf( "CodeBase test-file generator\n" );
   printf( "output: %s\n\n", outDir );

   rc = caseSimple( &cb, outDir );
   if ( rc == 0 )
      rc = verify( &cb, outDir, "SIMPLE.DBF" );

   code4close( &cb );
   code4initUndo( &cb );

   printf( "\n%s\n", rc == 0 ? "OK" : "FAILED" );
   return rc;
}
