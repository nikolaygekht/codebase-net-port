/* ==========================================================================
 * cases.h — the generator cases. One case per source file, each writing one
 * table (plus its .fpt when it has memo fields) and its dump.
 *
 * Each case file is self-contained in its **data**: its own row count, date
 * and name lists, numeric edge cases and memo payload lengths live in that
 * file and nowhere else. Utilities are shared (util.h, dump.h); data is not,
 * so editing one case can never move another case's bytes.
 *
 * To add a case: create src/case-<name>.cpp, declare it here, and call it from
 * main.cpp. The build picks up every .cpp in src/ automatically.
 *
 * Include after "d4all.h".
 * ========================================================================== */

#ifndef GEN_CASES_H
#define GEN_CASES_H

int caseDb3Type( CODE4 *cb, const char *outDir );   /* DB3TYPE.DBF — 0x03 */
int caseVfpType( CODE4 *cb, const char *outDir );   /* VFPTYPE.DBF — 0x30 */
int caseF2xMemo( CODE4 *cb, const char *outDir );   /* F2XMEMO.DBF — 0xF5 + FPT */
int caseVfpMemo( CODE4 *cb, const char *outDir );   /* VFPMEMO.DBF — 0x30 + FPT */
int caseVfpNull( CODE4 *cb, const char *outDir );   /* VFPNULL.DBF — 0x30 + FPT, _NullFlags */
int caseCp1251( CODE4 *cb, const char *outDir );    /* CP1251.DBF — 0x30 + FPT, codePage 0xC9 */
int caseCp936 ( CODE4 *cb, const char *outDir );    /* CP936.DBF  — 0x30 + FPT, codePage 0x7A */
int caseCdxBase( CODE4 *cb, const char *outDir );   /* CDXBASE.DBF — 0x30 + CDX, 10 tags, one block each */
int caseCdxDeep( CODE4 *cb, const char *outDir );   /* CDXDEEP.DBF — 0x30 + CDX, multi-level trees */
int caseIdxOne ( CODE4 *cb, const char *outDir );   /* IDXONE.DBF  — 0x30 + CDX + derived single-tag IDX */
int caseCdxColl( CODE4 *cb, const char *outDir );   /* CDXCOLL.DBF — 0x30 + CDX, machine and GENERAL collation */
int caseCdxTime( CODE4 *cb, const char *outDir );   /* CDXTIME.DBF — 0x30 + CDX, datetime keys and the fox flag bitmap */
int caseCdxExpr( CODE4 *cb, const char *outDir );   /* CDXEXPR.DBF — 0x30 + CDX + FPT, expression tags and the expression dump */
int caseCmpMemo( CODE4 *cb, const char *outDir );   /* CMPMEMO.DBF — 0x31 + FPT, compressed memo entries */
int caseQryGate( CODE4 *cb, const char *outDir );   /* QRYGATE.DBF — 0x30 + CDX, the query refusals isolated */
int caseTagCtx ( CODE4 *cb, const char *outDir );   /* TAGCTX.DBF  — 0x30 + CDX + FPT, tags parsed in a tag's context */
int caseTagGen ( CODE4 *cb, const char *outDir );   /* TAGGEN.DBF  — 0x30 + CDX + FPT, ASCEND, DESCEND and memo under GENERAL */
int caseTagBad ( CODE4 *cb, const char *outDir );   /* TAGBAD.DBF  — 0x30 + CDX, the GENERAL DESCEND tag i4tagAdd writes and the reference refuses */
int caseLongName( CODE4 *cb, const char *outDir );  /* LONGNAME.DBF — 0x31 + CDX + FPT, the long-field-name descriptor layout */
int caseFlags30( CODE4 *cb, const char *outDir );   /* FLAGS30.DBF  — 0x30 + FPT, every feature-flag byte set, read as ordinary */
int caseRecnoTag( CODE4 *cb, const char *outDir );  /* RECNOTAG.DBF — 0x30 + CDX, tags over RECNO() */
int caseDtMilli( CODE4 *cb, const char *outDir );   /* DTMILLI.DBF  — 0x30 + CDX, the '7' datetime with milliseconds */
int caseWstr( CODE4 *cb, const char *outDir );      /* WSTR.DBF     — 0x30 + CDX, cp1251, the W and O wide strings */
int caseCbColl( CODE4 *cb, const char *outDir );    /* CBCP1250/CBCP1252/CBCP850.DBF — 0x30 + CDX, CodeBase's CBnnnnn collations */

/* The one benchmark case: a workload, not an expectation. It writes into a
 * `benchmark` subdirectory of the output and has no dump, because nothing
 * asserts against its bytes — see case-perf10k.cpp. */
int casePerf10k( CODE4 *cb, const char *outDir );   /* benchmark\PERF10K.DBF — 0x30 + CDX, 10 000 records */

#endif /* GEN_CASES_H */
