/* ==========================================================================
 * dump.h — writes the expected-values dump that accompanies each table.
 *
 * Include after "d4all.h".
 * ========================================================================== */

#ifndef GEN_DUMP_H
#define GEN_DUMP_H

/* Reopen a finished table and write <NAME>.dump.txt beside it: raw header,
 * on-disk field descriptors, the C library's field view, and every record's
 * raw bytes plus decoded values. Format is described in net/corpus/README.md.
 * Returns 0 on success. */
int dumpTable( CODE4 *cb, const char *outDir, const char *fileName );

#endif /* GEN_DUMP_H */
