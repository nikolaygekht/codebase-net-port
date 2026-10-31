/* ==========================================================================
 * main.cpp — generates golden test files for the CodeBase.NET port.
 *
 * Uses the original Sequiter CodeBase C library (S4FOX / S4STAND_ALONE) as the
 * reference implementation. Files produced here are the corpus the C# port is
 * differential-tested against, so the output is checked in and this generator
 * is run only when the corpus needs new cases.
 *
 * Usage:  testgen.exe [output-dir]        (default: bin\out)
 *
 * Each case writes <NAME>.DBF (+ <NAME>.fpt when it has memo fields) and a
 * companion <NAME>.dump.txt holding the expected header facts, field
 * descriptors and record values, read back through the C library. The C# port
 * asserts against the dump; expected values are never hand-written.
 *
 * Layout: one case per src/case-*.cpp, each owning its own test data; shared
 * utilities in util.cpp and dump.cpp. See cases.h to add a case.
 *
 * DETERMINISM: the DBF header's "last update" stamp (bytes 1-3) is the system
 * date, which would change three bytes per file on every regeneration, so
 * util.cpp freezes it after each table is closed. That is the only place this
 * generator alters what the C library wrote.
 * ========================================================================== */

#include "d4all.h"

#include <stdio.h>
#include <direct.h>

#include "cases.h"
#include "dump-expr.h"

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

   /* The reference library's own function table, printed from the array. It
    * depends on no table and gates the port's transcription of it. */
   printf( "FUNCTIONS.dump.txt (v4functions, %d rows) ... ", EXPR4NUM_FUNCTIONS );
   rc = dumpFunctionTable( outDir );
   printf( "%s\n", rc == 0 ? "ok" : "FAILED" );

   if ( rc == 0 ) rc = caseDb3Type( &cb, outDir );
   if ( rc == 0 ) rc = caseVfpType( &cb, outDir );
   if ( rc == 0 ) rc = caseF2xMemo( &cb, outDir );
   if ( rc == 0 ) rc = caseVfpMemo( &cb, outDir );
   if ( rc == 0 ) rc = caseVfpNull( &cb, outDir );
   if ( rc == 0 ) rc = caseCp1251( &cb, outDir );
   if ( rc == 0 ) rc = caseCp936 ( &cb, outDir );
   if ( rc == 0 ) rc = caseCdxBase( &cb, outDir );
   if ( rc == 0 ) rc = caseCdxDeep( &cb, outDir );
   if ( rc == 0 ) rc = caseIdxOne ( &cb, outDir );
   if ( rc == 0 ) rc = caseCdxColl( &cb, outDir );
   if ( rc == 0 ) rc = caseCdxTime( &cb, outDir );
   if ( rc == 0 ) rc = caseCdxExpr( &cb, outDir );
   if ( rc == 0 ) rc = caseCmpMemo( &cb, outDir );
   if ( rc == 0 ) rc = caseQryGate( &cb, outDir );
   if ( rc == 0 ) rc = caseTagCtx ( &cb, outDir );
   if ( rc == 0 ) rc = caseTagGen ( &cb, outDir );
   if ( rc == 0 ) rc = caseTagBad ( &cb, outDir );

   /* Last, and not part of the corpus proper: the benchmark table, into
    * <outDir>\benchmark, with no dump. */
   if ( rc == 0 ) rc = casePerf10k( &cb, outDir );

   code4close( &cb );
   code4initUndo( &cb );

   printf( "\n%s\n", rc == 0 ? "OK" : "FAILED" );
   return rc;
}
