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

#endif /* GEN_CASES_H */
