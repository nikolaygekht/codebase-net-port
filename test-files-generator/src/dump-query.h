/* ==========================================================================
 * dump-query.h — writes the query-optimizer oracle dump.
 *
 *   <NAME>.query.dump.txt   per filter expression, what the reference library's
 *                           optimizer decided and what it returned.
 *
 * WHY THIS EXISTS AND WHAT IT RECORDS (claude/dev/011-query-optimizer)
 * -------------------------------------------------------------------
 * The optimizer produces no bytes, so it cannot be gated the way a format is.
 * What it produces is decisions, and RELATE4 / RELATION4 are public structs in
 * R4RELATE.H — so the decisions can be read out directly at four depths rather
 * than inferred from the records that come out at the end:
 *
 *   depth 1  log.infoReport[i].tag   which tag the optimizer matched to node i
 *   depth 2  relation->fullyMapped   whether the bitmap answers the whole filter
 *   depth 3  relate->set             the candidate bitset, BEFORE any residual
 *                                    filtering — the set a decomposition error
 *                                    cannot hide from
 *   depth 4  relate4top/skip/count   the records it finally returned
 *
 * AND WHY EVERY QUERY IS RUN TWICE
 * --------------------------------
 * RELATION4.bitmapDisable takes the optimizer out of the circuit entirely
 * (m4map.c:341): bitmap4init returns 0 at once, bitmapsFreed is set, and
 * log4true then re-evaluates every conjunct per record. So the same filter,
 * run twice, yields the reference's optimized answer and **the reference's own
 * scan truth** — depth 0.
 *
 * That second reading is not a luxury. m4map.c:216/243/251/278 write
 * `if ( tagPos = 1 && ... )`, an assignment, which can file a reflected
 * comparison under the wrong bound; the port implements the intent instead
 * (ADR-53) and is therefore *expected* to disagree with the optimized answer on
 * exactly those cases. Without a second reading there is no way to tell "we
 * decompose differently" from "the reference is wrong here". With it, the
 * disagreement is data: the dump names the filters where the reference's two
 * readings differ, so the port's gate needs no hand-written exception list.
 *
 * Nothing here computes a record set. Every figure is read from the RELATE4 the
 * library built or from the records it handed back.
 *
 * Include after "d4all.h".
 * ========================================================================== */

#ifndef GEN_DUMP_QUERY_H
#define GEN_DUMP_QUERY_H

/* One filter to record.
 *
 * `text` is set with relate4querySet exactly as written. `tagName` names a tag
 * to select before relate4top, which switches the skip strategy from bitset
 * scanning to tag stepping and so changes the ORDER records come out in
 * (r4relate.c:3188-3199) — pass 0 to record record-number order only. `note`
 * is copied into the dump so a reader knows what the case is for. */
typedef struct
{
   const char *text;
   const char *tagName;
   const char *note;
} QUERYCASE;

/* Write <dbfName minus extension>.query.dump.txt: for each filter, the compiled
 * node array with the tag the optimizer matched to each node, fullyMapped, the
 * candidate bitset both immediately after relate4top and again after the scan
 * (so the dump itself says whether the set survives being walked), the records
 * returned in record-number order and in tag order, relate4count, and the same
 * filter's records under bitmapDisable.
 *
 * Returns 0 on success. */
int dumpQueries( CODE4 *cb, const char *outDir, const char *dbfName,
                 const QUERYCASE *cases, int numCases );

#endif /* GEN_DUMP_QUERY_H */
