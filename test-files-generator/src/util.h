/* ==========================================================================
 * util.h — helpers shared by every generator case.
 *
 * Utilities only. **Test data is never shared**: each case file owns the
 * values it writes (date lists, name lists, numeric edge cases, memo payload
 * lengths), so changing one case's data set cannot disturb another's bytes.
 *
 * Include after "d4all.h" — CODE4/DATA4 come from there. (cb-config.h is
 * force-included by the build, so d4all.h expands to the configured headers.)
 * ========================================================================== */

#ifndef GEN_UTIL_H
#define GEN_UTIL_H

#include <stdio.h>   /* FILE, for dumpEscapedBytes */

/* Report a CodeBase failure with its error code. Always returns 1, so a case
 * can `return fail( cb, "d4create X" );`. */
int fail( CODE4 *cb, const char *what );

/* A run of bytes with an explicit length.
 *
 * Test text outside ASCII is written as bytes, never as a source string
 * literal: what a literal becomes depends on the compiler's source and
 * execution charsets, and the code-page cases exist to pin down the exact bytes
 * that reach the file. */
typedef struct
{
   const unsigned char *bytes;
   unsigned             len;
} TEXTBYTES;

/* Wrap a byte array as a TEXTBYTES, taking the length from the array itself. */
#define TEXT_BYTES( a )   { ( a ), (unsigned)sizeof( a ) }

/* Assign a byte run to a character field. Longer than the field truncates,
 * shorter blank-pads — the same f4assignN the string form goes through
 * (F4STR.C:122-168), which is how the mid-character truncation case is made. */
void assignText( FIELD4 *field, const TEXTBYTES *text );

/* Common tail of every case: close the table, freeze its header date stamp,
 * then write <NAME>.dump.txt beside it. Returns 0 on success. */
int finish( CODE4 *cb, DATA4 *data, const char *outDir, const char *fileName );

/* Escape a byte run into C-ish text: printable ASCII verbatim, everything else
 * as \xHH, wrapped in double quotes. Shared by both dump writers so a key and a
 * field value are escaped by the same rules — the port has one unescaper. */
void dumpEscapedBytes( FILE *fp, const char *p, unsigned long len );

#endif /* GEN_UTIL_H */
