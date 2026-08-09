/* ==========================================================================
 * cb-config.h — build configuration for the reference CodeBase library.
 *
 * WHY THIS FILE EXISTS
 * --------------------
 * `original/source/` is read-only reference material and must never be
 * modified (see CLAUDE.md). But the shipped `D4all.h` is configured for a
 * Windows DLL build with WinSock and zlib compression, none of which we want
 * or can link.
 *
 * HOW THE OVERRIDE WORKS
 * ----------------------
 * Every .c in the library starts with `#include "d4all.h"`, and the shipped
 * D4all.h is guarded by `#ifndef D4ALL_INC`. We force-include *this* file
 * ahead of everything (cl /FI), and it defines D4ALL_INC itself. By the time
 * a source file reaches `#include "d4all.h"`, the guard is already set, so the
 * shipped header expands to nothing and our switches win — with zero edits to
 * original/source.
 *
 * Keep the switch list below in sync with original/source/D4all.h. Deviations
 * from the shipped defaults are marked [CHANGED].
 * ========================================================================== */

#ifndef D4ALL_INC
#define D4ALL_INC

/* ---- CodeBase configuration ------------------------------------------- */
#define S4STAND_ALONE          /* no client/server; matches port scope */

/* ---- Index file compatibility ------------------------------------------ */
#define S4FOX                  /* CDX compound indexes (VFP). Not MDX/NTX. */

/* ---- Library type ------------------------------------------------------ */
#define S4STATIC               /* [CHANGED] shipped default is S4DLL */

/* ---- Operating system -------------------------------------------------- */
#define S4WIN32
/* S4UNIX is NOT usable: it requires p4port.h ("CodeBase Portability
 * version"), which is not present in this source drop. */

/* ---- Communications ---------------------------------------------------- */
/* [CHANGED] shipped default defines S4WINSOCK; stand-alone needs no sockets */

/* ---- Alterable CodeBase global defines --------------------------------- */
#define DEF4SERVER_ID "localhost"
#define DEF4PROCESS_ID "23165"

/* ---- Error configuration ----------------------------------------------- */
#define E4VBASIC
#define E4PARM_HIGH
#define E4PAUSE

/* ---- Library reducing switches ----------------------------------------- */
#define S4OFF_REPORT           /* report writer is out of scope */
#define S4OFF_COMPRESS         /* [CHANGED] zlib is not shipped with the drop,
                                * and compressed DBF/memo is out of scope */

/* ---- FoxPro collating sequence support --------------------------------- */
#define S4GENERAL              /* GENERAL collation (accents, expansions) */

/* ---- FoxPro code page support ------------------------------------------ */
#define S4CODEPAGE_437         /* U.S. MS-DOS */
#define S4CODEPAGE_1252        /* Windows ANSI */
#define S4CODEPAGE_1250        /* Windows Eastern European */

#define S4VERSION 6503014

#include "d4inc.h"

#endif /* D4ALL_INC */
