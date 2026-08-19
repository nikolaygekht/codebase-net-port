/* ==========================================================================
 * perf.cpp -- performance-1-experiment: the reference (C) side.
 *
 * Two subcommands:
 *
 *   perf.exe gen   <dir>          build PERF10K.DBF + PERF10K.cdx and the
 *                                 query sets, into <dir>
 *   perf.exe bench <dir> [reps]   time the query sets against them
 *
 * The query sets are *files*, not an algorithm repeated in two languages: the
 * C# side reads the very same lines in the very same order, so "both did the
 * same work" is a property of the data rather than of two implementations
 * agreeing. Each scenario also prints a checksum (the sum of the ID field of
 * every record it landed on); the two sides must print the same numbers, which
 * is what makes the timings comparable rather than merely similar.
 *
 * NOTE: this is an experiment, not part of the corpus. It writes nothing under
 * net/corpus/ and its output is gitignored.
 * ========================================================================== */

#include "d4all.h"

#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <direct.h>

#define ROWS       10000    /* records in the table */
#define QUERIES    10000    /* seeks per scenario */
#define NAME_LEN      20    /* C(20) -- the character key, exactly filled */
#define MAX_REPS      21
#define WARMUPS        5    /* untimed passes before the clock starts, matching the C# harness */

/* Coprime with ROWS, so i -> (i * STRIDE) % ROWS is a permutation: unique
 * values, and key order unrelated to record order. */
#define STRIDE      7919
/* Coprime with QUERIES, for the same reason: the order the queries are *asked*
 * in is unrelated to both record order and key order, so neither side gets a
 * sequential-access head start. */
#define QSTRIDE     3571

/* ------------------------------------------------------------------ data */

static const char ALPHA[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

/* NAME, C(20), exactly 20 characters so a seek is a whole-key seek on both
 * sides with no padding rule involved. The scrambled row number is embedded,
 * which makes the value unique; the leading letters are derived from it, so
 * consecutive keys in key order share little and the leaves are realistically
 * -- not artificially -- compressible. */
static void nameFor( int i, char *out /* >= 21 bytes */ )
{
   int s = ( i * STRIDE ) % ROWS;
   int k;

   sprintf( out, "%c%c%c%c-%05d-",
            ALPHA[ s % 26 ], ALPHA[ ( s / 26 ) % 26 ],
            ALPHA[ ( s / 676 ) % 26 ], ALPHA[ ( s / 7 ) % 26 ], s );

   for ( k = 11; k < NAME_LEN; k++ )
      out[k] = ALPHA[ ( s * 3 + k * 5 ) % 26 ];

   out[NAME_LEN] = 0;
}

/* A NAME-shaped value that is certainly absent: the same shape with a five
 * digit group above ROWS, so it cannot collide with any stored value. */
static void missNameFor( int i, char *out /* >= 21 bytes */ )
{
   int s = ( i * STRIDE ) % ROWS + ROWS;
   int k;

   sprintf( out, "%c%c%c%c-%05d-",
            ALPHA[ s % 26 ], ALPHA[ ( s / 26 ) % 26 ],
            ALPHA[ ( s / 676 ) % 26 ], ALPHA[ ( s / 7 ) % 26 ], s );

   for ( k = 11; k < NAME_LEN; k++ )
      out[k] = ALPHA[ ( s * 3 + k * 5 ) % 26 ];

   out[NAME_LEN] = 0;
}

/* AMOUNT, N(12,2). Quarters, so the value is exact in binary and the two
 * decimals the field stores are the two decimals the key was built from --
 * no rounding sits between the value asked for and the key on disk. */
static double amountFor( int i )
{
   int s = ( i * STRIDE ) % ROWS;
   return s * 1000.0 + ( i % 100 ) * 0.25;
}

/* --------------------------------------------------------------- helpers */

static int fail( CODE4 *cb, const char *what )
{
   fprintf( stderr, "ERROR: %s (errorCode=%d)\n", what, (int)error4code( cb ) );
   return 1;
}

static double nowMs( void )
{
   static LARGE_INTEGER freq = { 0 };
   LARGE_INTEGER t;

   if ( freq.QuadPart == 0 )
      QueryPerformanceFrequency( &freq );

   QueryPerformanceCounter( &t );
   return (double)t.QuadPart * 1000.0 / (double)freq.QuadPart;
}

static int cmpDouble( const void *a, const void *b )
{
   double x = *(const double *)a, y = *(const double *)b;
   return ( x < y ) ? -1 : ( x > y ) ? 1 : 0;
}

/* One result line, in the format the C# side also prints, so the two runs can
 * be diffed field by field. */
/* Read operations the process issued during one untimed pass, and the bytes they
 * moved. This is the OS's own count of read requests -- not physical disk I/O, which
 * a warm file does none of -- so it counts exactly the syscalls the block cache is
 * there to avoid. */
static void ioAround( long long *reads, long long *bytes )
{
   IO_COUNTERS io;

   if ( !GetProcessIoCounters( GetCurrentProcess(), &io ) )
   {
      *reads = -1;
      *bytes = -1;
      return;
   }

   *reads = (long long)io.ReadOperationCount;
   *bytes = (long long)io.ReadTransferCount;
}

static void report( const char *scenario, int ops, long long checksum,
                    double *ms, int reps, long long ioReads, long long ioBytes )
{
   double sorted[MAX_REPS], sum = 0.0;
   int i;

   for ( i = 0; i < reps; i++ )
   {
      sorted[i] = ms[i];
      sum += ms[i];
   }
   qsort( sorted, reps, sizeof( double ), cmpDouble );

   printf( "scenario=%-12s ops=%d checksum=%lld reps=%d "
           "min_ms=%.3f med_ms=%.3f mean_ms=%.3f us_per_op=%.3f "
           "reads_per_op=%.3f readbytes_per_op=%.1f\n",
           scenario, ops, checksum, reps,
           sorted[0], sorted[reps / 2], sum / reps,
           sorted[0] * 1000.0 / ops,
           (double)ioReads / ops, (double)ioBytes / ops );
}

/* ------------------------------------------------------------------- gen */

static int writeNameQueries( const char *dir, const char *file, int miss )
{
   char path[520], name[NAME_LEN + 1];
   FILE *fp;
   int j;

   sprintf( path, "%s\\%s", dir, file );
   fp = fopen( path, "wb" );
   if ( fp == 0 )
   {
      fprintf( stderr, "ERROR: cannot write %s\n", path );
      return 1;
   }

   for ( j = 0; j < QUERIES; j++ )
   {
      int i = ( j * QSTRIDE ) % ROWS;

      if ( miss )
         missNameFor( i, name );
      else
         nameFor( i, name );

      fprintf( fp, "%s\n", name );
   }

   fclose( fp );
   return 0;
}

static int writeAmountQueries( const char *dir, const char *file )
{
   char path[520];
   FILE *fp;
   int j;

   sprintf( path, "%s\\%s", dir, file );
   fp = fopen( path, "wb" );
   if ( fp == 0 )
   {
      fprintf( stderr, "ERROR: cannot write %s\n", path );
      return 1;
   }

   for ( j = 0; j < QUERIES; j++ )
      fprintf( fp, "%.2f\n", amountFor( ( j * QSTRIDE ) % ROWS ) );

   fclose( fp );
   return 0;
}

static int generate( CODE4 *cb, const char *dir )
{
   static FIELD4INFO fields[] =
   {
      /* name        type  len  dec  nulls */
      { (char *)"ID",     'I',   4,  0,  0 },
      { (char *)"NAME",   'C',  20,  0,  0 },
      { (char *)"CITY",   'C',  16,  0,  0 },
      { (char *)"AMOUNT", 'N',  12,  2,  0 },
      { (char *)"HIRED",  'D',   8,  0,  0 },
      { 0, 0, 0, 0, 0 }
   };

   /* name        expression  filter  unique  descending */
   static TAG4INFO tags[] =
   {
      { (char *)"T_NAME", (char *)"NAME",   0, 0, 0 },
      { (char *)"T_AMT",  (char *)"AMOUNT", 0, 0, 0 },
      { 0, 0, 0, 0, 0 }
   };

   char path[520], name[NAME_LEN + 1], city[17], hired[9];
   DATA4 *data;
   int i;

   sprintf( path, "%s\\PERF10K.DBF", dir );
   printf( "generating %s ... ", path );
   fflush( stdout );

   cb->compatibility = 30;
   data = d4create( cb, path, fields, 0 );
   if ( data == 0 )
      return fail( cb, "d4create PERF10K.DBF" );

   for ( i = 0; i < ROWS; i++ )
   {
      if ( d4appendStart( data, 0 ) < 0 ) { d4close( data ); return fail( cb, "d4appendStart" ); }
      d4blank( data );

      nameFor( i, name );
      sprintf( city, "CITY-%04d", i % 500 );
      sprintf( hired, "%04d%02d%02d", 2000 + i % 26, 1 + i % 12, 1 + i % 28 );

      f4assignLong  ( d4fieldJ( data, 1 ), (long)i );
      f4assignN     ( d4fieldJ( data, 2 ), name, NAME_LEN );
      f4assign      ( d4fieldJ( data, 3 ), city );
      f4assignDouble( d4fieldJ( data, 4 ), amountFor( i ) );
      f4assign      ( d4fieldJ( data, 5 ), hired );

      if ( d4append( data ) < 0 ) { d4close( data ); return fail( cb, "d4append" ); }
   }

   if ( i4create( data, 0, tags ) == 0 )
   {
      d4close( data );
      return fail( cb, "i4create PERF10K.cdx" );
   }

   if ( d4close( data ) < 0 )
      return fail( cb, "d4close" );

   printf( "%d records, 2 tags\n", ROWS );

   if ( writeNameQueries  ( dir, "queries-name.txt",      0 ) != 0 ) return 1;
   if ( writeNameQueries  ( dir, "queries-name-miss.txt", 1 ) != 0 ) return 1;
   if ( writeAmountQueries( dir, "queries-amount.txt"       ) != 0 ) return 1;

   printf( "query sets: %d hits, %d misses, %d numeric\n", QUERIES, QUERIES, QUERIES );
   return 0;
}

/* ----------------------------------------------------------------- bench */

static char  qName[QUERIES][NAME_LEN + 1];
static char  qMiss[QUERIES][NAME_LEN + 1];
static double qAmount[QUERIES];

static int loadLines( const char *dir, const char *file, char (*out)[NAME_LEN + 1] )
{
   char path[520], line[128];
   FILE *fp;
   int j = 0;

   sprintf( path, "%s\\%s", dir, file );
   fp = fopen( path, "rb" );
   if ( fp == 0 )
   {
      fprintf( stderr, "ERROR: cannot read %s -- run `perf.exe gen` first\n", path );
      return 1;
   }

   while ( j < QUERIES && fgets( line, sizeof( line ), fp ) != 0 )
   {
      size_t n = strlen( line );
      while ( n > 0 && ( line[n - 1] == '\n' || line[n - 1] == '\r' ) )
         line[--n] = 0;

      if ( n != NAME_LEN )
      {
         fclose( fp );
         fprintf( stderr, "ERROR: %s line %d is %d bytes, expected %d\n",
                  file, j + 1, (int)n, NAME_LEN );
         return 1;
      }

      memcpy( out[j++], line, NAME_LEN + 1 );
   }

   fclose( fp );

   if ( j != QUERIES )
   {
      fprintf( stderr, "ERROR: %s holds %d lines, expected %d\n", file, j, QUERIES );
      return 1;
   }
   return 0;
}

static int loadDoubles( const char *dir, const char *file, double *out )
{
   char path[520], line[128];
   FILE *fp;
   int j = 0;

   sprintf( path, "%s\\%s", dir, file );
   fp = fopen( path, "rb" );
   if ( fp == 0 )
   {
      fprintf( stderr, "ERROR: cannot read %s -- run `perf.exe gen` first\n", path );
      return 1;
   }

   while ( j < QUERIES && fgets( line, sizeof( line ), fp ) != 0 )
      out[j++] = atof( line );

   fclose( fp );

   if ( j != QUERIES )
   {
      fprintf( stderr, "ERROR: %s holds %d lines, expected %d\n", file, j, QUERIES );
      return 1;
   }
   return 0;
}

/* Each pass returns the checksum, so an accidentally-empty run cannot look
 * fast: the two sides must agree on it before a timing means anything. */

static long long passNameSeek( DATA4 *data, FIELD4 *id, char (*q)[NAME_LEN + 1] )
{
   long long sum = 0;
   int j;

   for ( j = 0; j < QUERIES; j++ )
      if ( d4seek( data, q[j] ) == r4success )
         sum += f4long( id );

   return sum;
}

/* The miss set: d4seek cannot match, returns r4after or r4eof, and has already
 * positioned on the neighbour when it can. That is C# SeekAtOrAfter, not C#
 * Seek -- see README, "What maps to what". */
static long long passNameMiss( DATA4 *data, FIELD4 *id, char (*q)[NAME_LEN + 1] )
{
   long long sum = 0;
   int j;

   for ( j = 0; j < QUERIES; j++ )
   {
      int rc = d4seek( data, q[j] );
      if ( rc == r4success || rc == r4after )
         sum += f4long( id );
   }

   return sum;
}

static long long passAmountSeek( DATA4 *data, FIELD4 *id, const double *q )
{
   long long sum = 0;
   int j;

   for ( j = 0; j < QUERIES; j++ )
      if ( d4seekDouble( data, q[j] ) == r4success )
         sum += f4long( id );

   return sum;
}

static long long passWalk( DATA4 *data, FIELD4 *id )
{
   long long sum = 0;

   for ( d4top( data ); !d4eof( data ); d4skip( data, 1 ) )
      sum += f4long( id );

   return sum;
}

static int bench( CODE4 *cb, const char *dir, int reps, int optimized )
{
   char path[520];
   double ms[MAX_REPS];
   DATA4 *data;
   FIELD4 *id;
   TAG4 *byName, *byAmount;
   long long checksum = 0;
   long long ioR0 = 0, ioR1 = 0, ioB0 = 0, ioB1 = 0;
   int r;

   if ( loadLines  ( dir, "queries-name.txt",      qName   ) != 0 ) return 1;
   if ( loadLines  ( dir, "queries-name-miss.txt", qMiss   ) != 0 ) return 1;
   if ( loadDoubles( dir, "queries-amount.txt",    qAmount ) != 0 ) return 1;

   /* The library's own block cache is off unless code4optStart is called -- cb->optimize
    * alone does nothing (OPT4EXCLUSIVE, its default, is a *permission*, not a switch).
    * The default run therefore reads through the OS page cache exactly as CodeBase.Net
    * does, which is the comparison worth making. The `opt` mode turns the cache on to
    * show what it is worth, because "should the port have one, and where" is an open
    * design question rather than a settled one. */
   if ( optimized )
      cb->optimize = OPT4ALL;

   sprintf( path, "%s\\PERF10K.DBF", dir );
   data = d4open( cb, path );
   if ( data == 0 )
      return fail( cb, "d4open PERF10K.DBF" );

   if ( optimized )
   {
      d4optimize( data, OPT4ALL );
      if ( code4optStart( cb ) < 0 )
      {
         d4close( data );
         return fail( cb, "code4optStart" );
      }
   }

   id       = d4field( data, "ID" );
   byName   = d4tag( data, "T_NAME" );
   byAmount = d4tag( data, "T_AMT" );

   if ( id == 0 || byName == 0 || byAmount == 0 )
   {
      d4close( data );
      return fail( cb, "the table is missing ID, T_NAME or T_AMT" );
   }

   printf( "side=c%s records=%ld reps=%d optimize=%d blockcache=%s\n",
           optimized ? "-opt" : "    ", (long)d4recCount( data ), reps,
           (int)cb->optimize, optimized ? "on" : "off" );

   /* --- name, whole-key, every query a hit ------------------------------ */
   d4tagSelect( data, byName );
   for ( r = 0; r < WARMUPS; r++ ) checksum = passNameSeek( data, id, qName );
   for ( r = 0; r < reps; r++ )
   {
      double t0 = nowMs();
      long long c = passNameSeek( data, id, qName );
      ms[r] = nowMs() - t0;
      if ( c != checksum ) { d4close( data ); fprintf( stderr, "ERROR: unstable checksum\n" ); return 1; }
   }
   /* one extra untimed pass, measured for read operations rather than time */
   ioAround( &ioR0, &ioB0 );
   passNameSeek( data, id, qName );
   ioAround( &ioR1, &ioB1 );
   report( "name-hit", QUERIES, checksum, ms, reps, ioR1 - ioR0, ioB1 - ioB0 );

   /* --- name, whole-key, every query a miss ----------------------------- */
   for ( r = 0; r < WARMUPS; r++ ) checksum = passNameMiss( data, id, qMiss );
   for ( r = 0; r < reps; r++ )
   {
      double t0 = nowMs();
      long long c = passNameMiss( data, id, qMiss );
      ms[r] = nowMs() - t0;
      if ( c != checksum ) { d4close( data ); fprintf( stderr, "ERROR: unstable checksum\n" ); return 1; }
   }
   /* one extra untimed pass, measured for read operations rather than time */
   ioAround( &ioR0, &ioB0 );
   passNameMiss( data, id, qMiss );
   ioAround( &ioR1, &ioB1 );
   report( "name-miss", QUERIES, checksum, ms, reps, ioR1 - ioR0, ioB1 - ioB0 );

   /* --- numeric key ----------------------------------------------------- */
   d4tagSelect( data, byAmount );
   for ( r = 0; r < WARMUPS; r++ ) checksum = passAmountSeek( data, id, qAmount );
   for ( r = 0; r < reps; r++ )
   {
      double t0 = nowMs();
      long long c = passAmountSeek( data, id, qAmount );
      ms[r] = nowMs() - t0;
      if ( c != checksum ) { d4close( data ); fprintf( stderr, "ERROR: unstable checksum\n" ); return 1; }
   }
   /* one extra untimed pass, measured for read operations rather than time */
   ioAround( &ioR0, &ioB0 );
   passAmountSeek( data, id, qAmount );
   ioAround( &ioR1, &ioB1 );
   report( "amount-hit", QUERIES, checksum, ms, reps, ioR1 - ioR0, ioB1 - ioB0 );

   /* --- bonus: a full walk in tag order, the other thing the perf pass
    *     wants a number for. Not a seek scenario; reported beside them. --- */
   d4tagSelect( data, byName );
   for ( r = 0; r < WARMUPS; r++ ) checksum = passWalk( data, id );
   for ( r = 0; r < reps; r++ )
   {
      double t0 = nowMs();
      long long c = passWalk( data, id );
      ms[r] = nowMs() - t0;
      if ( c != checksum ) { d4close( data ); fprintf( stderr, "ERROR: unstable checksum\n" ); return 1; }
   }
   /* one extra untimed pass, measured for read operations rather than time */
   ioAround( &ioR0, &ioB0 );
   passWalk( data, id );
   ioAround( &ioR1, &ioB1 );
   report( "tag-walk", ROWS, checksum, ms, reps, ioR1 - ioR0, ioB1 - ioB0 );

   if ( d4close( data ) < 0 )
      return fail( cb, "d4close" );

   return 0;
}

/* --------------------------------------------------------------- syscall */

/* What one small read costs on this machine, with no CodeBase involved at all.
 *
 * The block cache's whole job is to not make this call, so the value below is the
 * constant the 14x has to be explained by. Offsets are scattered over the index the
 * benchmark seeks through, in blocks of the size the CDX uses, on a file the OS has
 * cached -- so this measures the syscall and its copy, not the disk. */
static int syscallCost( const char *dir, int reps )
{
   char path[520];
   double ms[MAX_REPS], sorted[MAX_REPS];
   char buf[512];
   HANDLE h;
   DWORD got;
   long long blocks;
   LARGE_INTEGER size, off;
   int r, i, j;
   const int COUNT = 100000;

   sprintf( path, "%s\\PERF10K.cdx", dir );
   h = CreateFileA( path, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_EXISTING, 0, 0 );
   if ( h == INVALID_HANDLE_VALUE )
   {
      fprintf( stderr, "ERROR: cannot open %s\n", path );
      return 1;
   }

   GetFileSizeEx( h, &size );
   blocks = size.QuadPart / 512;

   /* warm the OS cache, and warm it the same way the timed loop will touch it */
   for ( i = 0; i < (int)blocks; i++ )
   {
      off.QuadPart = (long long)i * 512;
      SetFilePointerEx( h, off, 0, FILE_BEGIN );
      ReadFile( h, buf, 512, &got, 0 );
   }

   for ( r = 0; r < reps; r++ )
   {
      double t0 = nowMs();
      for ( i = 0; i < COUNT; i++ )
      {
         off.QuadPart = ( (long long)( i * 7919 ) % blocks ) * 512;
         SetFilePointerEx( h, off, 0, FILE_BEGIN );
         ReadFile( h, buf, 512, &got, 0 );
      }
      ms[r] = nowMs() - t0;
   }
   CloseHandle( h );

   for ( i = 0; i < reps; i++ ) sorted[i] = ms[i];
   qsort( sorted, reps, sizeof( double ), cmpDouble );

   printf( "syscall  seek+read (2 calls)      blocks=%lld reps=%d count=%d "
           "min_ms=%.3f med_ms=%.3f us_per_read=%.3f\n",
           blocks, reps, COUNT, sorted[0], sorted[reps / 2],
           sorted[0] * 1000.0 / COUNT );

   /* Positional read: one syscall instead of two. This is what .NET's RandomAccess.Read
    * does, and therefore what CodeBase.Net pays per block; the loop above is the
    * SetFilePointer-then-ReadFile pair the C library uses (f4file.c:1253,1318). */
   h = CreateFileA( path, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_EXISTING, 0, 0 );
   if ( h != INVALID_HANDLE_VALUE )
   {
      OVERLAPPED ov;

      for ( r = 0; r < reps; r++ )
      {
         double t0 = nowMs();
         for ( i = 0; i < COUNT; i++ )
         {
            long long o = ( (long long)( i * 7919 ) % blocks ) * 512;
            memset( &ov, 0, sizeof( ov ) );
            ov.Offset = (DWORD)o;
            ov.OffsetHigh = (DWORD)( o >> 32 );
            ReadFile( h, buf, 512, &got, &ov );
         }
         ms[r] = nowMs() - t0;
      }
      CloseHandle( h );

      for ( i = 0; i < reps; i++ ) sorted[i] = ms[i];
      qsort( sorted, reps, sizeof( double ), cmpDouble );
      printf( "syscall  positional (1 call)      reps=%d count=%d "
              "min_ms=%.3f med_ms=%.3f us_per_read=%.3f\n",
              reps, COUNT, sorted[0], sorted[reps / 2], sorted[0] * 1000.0 / COUNT );
   }

   /* the same loop with the read replaced by a memcpy from RAM: what a cache hit
    * costs instead, so the difference is the syscall and nothing else */
   {
      char *ram = (char *)malloc( (size_t)( blocks * 512 ) );
      if ( ram != 0 )
      {
         memset( ram, 0, (size_t)( blocks * 512 ) );
         for ( r = 0; r < reps; r++ )
         {
            double t0 = nowMs();
            for ( i = 0; i < COUNT; i++ )
            {
               j = (int)( ( (long long)( i * 7919 ) % blocks ) * 512 );
               memcpy( buf, ram + j, 512 );
            }
            ms[r] = nowMs() - t0;
         }
         for ( i = 0; i < reps; i++ ) sorted[i] = ms[i];
         qsort( sorted, reps, sizeof( double ), cmpDouble );
         printf( "memcpy   512 bytes from RAM      reps=%d count=%d "
                 "min_ms=%.3f us_per_copy=%.3f\n",
                 reps, COUNT, sorted[0], sorted[0] * 1000.0 / COUNT );
         free( ram );
      }
   }

   return 0;
}

/* ------------------------------------------------------------------ main */

int main( int argc, char **argv )
{
   const char *cmd  = ( argc > 1 ) ? argv[1] : "";
   const char *dir  = ( argc > 2 ) ? argv[2] : "out";
   int reps         = ( argc > 3 ) ? atoi( argv[3] ) : 5;
   const char *mode = ( argc > 4 ) ? argv[4] : "plain";
   int optimized    = ( strcmp( mode, "opt" ) == 0 );
   CODE4 cb;
   int rc;

   if ( reps < 1 ) reps = 1;
   if ( reps > MAX_REPS ) reps = MAX_REPS;

   if ( strcmp( cmd, "gen" ) != 0 && strcmp( cmd, "bench" ) != 0 && strcmp( cmd, "syscall" ) != 0 )
   {
      fprintf( stderr, "usage: perf.exe gen <dir>\n"
                       "       perf.exe bench <dir> [reps] [plain|opt]\n"
                       "       perf.exe syscall <dir> [reps]\n"
                       "         plain  no library block cache -- the fair comparison\n"
                       "         opt    code4optStart, the cache on -- what it is worth\n" );
      return 2;
   }

   _mkdir( dir );

   code4init( &cb );
   cb.safety = 0;    /* overwrite existing output */
   cb.errOff = 1;    /* report failures ourselves, no message boxes */

   if ( strcmp( cmd, "gen" ) == 0 )
      rc = generate( &cb, dir );
   else if ( strcmp( cmd, "syscall" ) == 0 )
      rc = syscallCost( dir, reps );
   else
      rc = bench( &cb, dir, reps, optimized );

   code4close( &cb );
   code4initUndo( &cb );

   if ( rc != 0 )
      fprintf( stderr, "FAILED\n" );

   return rc;
}
