/* ==========================================================================
 * dump-expr.h — writes the expression oracle dumps.
 *
 * Two files, because they answer different questions (claude/dev/010-expr):
 *
 *   FUNCTIONS.dump.txt      the reference library's own v4functions[193],
 *                           printed from the array itself. Table-independent.
 *   <NAME>.expr.dump.txt    per expression, the compiled node array and the
 *                           evaluated value, plus the strings the parser
 *                           refuses.
 *
 * Nothing here computes an expression result. Every figure is read from an
 * exported entry point or from the EXPR4 the library built, so the dump is a
 * recording of the reference implementation and never a second one.
 *
 * Include after "d4all.h".
 * ========================================================================== */

#ifndef GEN_DUMP_EXPR_H
#define GEN_DUMP_EXPR_H

/* One expression to record. `text` is parsed as written; `note` is copied into
 * the dump so a reader knows what the case is for. */
typedef struct
{
   const char *text;
   const char *note;
} EXPRCASE;

/* Write FUNCTIONS.dump.txt: every row of v4functions with everything but the
 * function pointer. Needs no table. Returns 0 on success. */
int dumpFunctionTable( const char *outDir );

/* Write <dbfName minus extension>.expr.dump.txt: for each constant expression
 * its node array and its single value; for each field expression its node array
 * and its value at every record; and for each rejection string whether the
 * parser accepted it and the error code if not.
 *
 * `constExprs` must not reference a field -- they are recorded once, with no
 * record dimension. `clockExprs` are recorded for SHAPE ONLY -- type, length,
 * key length and node array, but never a value: DATE() and TIME() read the
 * system clock, and a recorded value for one would change on every run and
 * break the corpus's byte-identical regeneration. Returns 0 on success. */
int dumpExpressions( CODE4 *cb, const char *outDir, const char *dbfName,
                     const EXPRCASE *constExprs, int numConst,
                     const EXPRCASE *fieldExprs, int numField,
                     const EXPRCASE *clockExprs, int numClock,
                     const EXPRCASE *rejects,    int numReject,
                     const EXPRCASE *vfpOnly,    int numVfpOnly );

#endif /* GEN_DUMP_EXPR_H */
