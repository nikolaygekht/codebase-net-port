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

/* Report a CodeBase failure with its error code. Always returns 1, so a case
 * can `return fail( cb, "d4create X" );`. */
int fail( CODE4 *cb, const char *what );

/* Common tail of every case: close the table, freeze its header date stamp,
 * then write <NAME>.dump.txt beside it. Returns 0 on success. */
int finish( CODE4 *cb, DATA4 *data, const char *outDir, const char *fileName );

#endif /* GEN_UTIL_H */
