/* ==========================================================================
 * dump-index.h — writes the expected-values dump for an index file.
 *
 * Include after "d4all.h".
 * ========================================================================== */

#ifndef GEN_DUMP_INDEX_H
#define GEN_DUMP_INDEX_H

/* Reopen a finished table with one of its index files and write
 * <NAME>.<ext>.dump.txt beside it — the file header, then per tag its header,
 * every block of its tree and every (key, record number) pair in navigation
 * order, plus the d4check result. Format: net/corpus/README.md.
 *
 * indexName is the index file's name with extension, for example
 * "CDXBASE.CDX" or "IDXONE.IDX". A production index is already open once the
 * table is; anything else is opened with i4open.
 *
 * Returns 0 on success. A failing d4check is a failure: the whole point of the
 * dump is that the reference implementation vouches for the file. */
int dumpIndex( CODE4 *cb, const char *outDir, const char *dbfName, const char *indexName );

#endif /* GEN_DUMP_INDEX_H */
