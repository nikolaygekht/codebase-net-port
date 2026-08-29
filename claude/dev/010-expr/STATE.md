# 010-expr — state

**Updated:** 2026-08-28 · **sub-step 1 of 8 is done.** The step is designed
([`DESIGN.md`](DESIGN.md)), planned ([`PLAN.md`](PLAN.md)) and has its oracle in the corpus. **No C#
of the expression engine exists.**

## Where the sub-steps stand

| # | sub-step | state |
|---|---|---|
| 1 | Generator: the `H` probe, `CDXEXPR`, the five dumps | **done** |
| 2 | Lexer | not started — **the next session starts here** |
| 3 | Function table + parser | not started |
| 4 | Types and overload resolution | not started |
| 5 | Evaluator: kernels and null semantics | not started — *the stop point* |
| 5b | The VFP-only functions (ADR-43) | not started |
| 6 | The text-formatting family | not started |
| 7 | Axis B in full — key time | not started |
| 8 | Wire into the tag path | not started |

## What sub-step 1 produced

In `net/corpus/`: `CDXEXPR.DBF` + `.cdx` + `.fpt`, their two ordinary dumps,
`CDXEXPR.expr.dump.txt`, and `FUNCTIONS.dump.txt`. In `test-files-generator/src/`: `dump-expr.h`,
`dump-expr.cpp`, `case-cdxexpr.cpp`. Beside this file, [`ORACLE-SUITE.txt`](ORACLE-SUITE.txt) renders
the expression dump flat as `EXPRESSION -> value` — **derived, not a source**: regenerate it from the
dump, assert against the dump.

`CDXEXPR.expr.dump.txt` has five sections. Their meanings are not interchangeable:

| section | what it is | is it an expectation for the port? |
|---|---|---|
| `[constant expressions]` | 109 cases, no record dimension | yes |
| `[field expressions]` | 49 cases × 16 records | yes |
| `[clock-dependent expressions]` | `DATE()`, `TIME()` — **shape only, no value** | type/length/nodes only |
| `[rejections]` | 18 strings CodeBase refuses | yes |
| `[vfp-only]` | 13 names VFP has and CodeBase does not | **no — the port accepts these (ADR-43)** |

Each expression also carries its **compiled node array**: per node the row index of `v4functions`
that overload resolution chose. That is the resolution gate, and it is what turns "a missing overload
row silently selects its neighbour" into a mismatched integer.

## Two things owed to the corpus

- **A second rejection pass with a real `TAG4FILE`.** Everything is parsed with `tagPtr = 0`, so
  `e4tagExpr` (−510) never appears and `OTHER->K_TEXT` returns `e4fieldName` (−210). The tag-only
  refusals are unobservable until this exists — including whether `DESIGN.md` §3's
  "a TRIM result may not feed `SUBSTR`" restriction bites at all. It **does not** without a tag.
- **`CachedReadGoldenTests.KeysABareField` is deleted at sub-step 8**, with ADR-28's refusal. It is
  the one place the suite currently averts its eyes, and it exists only because `CDXEXPR` had to land
  before the engine that reads its tags.

## The shape of the gate, in one paragraph

Two axes, because they fail differently and the reference splits there too — `expr4vary` returns a
value, `expr4keyConvert` turns it into key bytes. **Axis A** (what a function computes) is
C#-authored at layer 1 and **tested over constants**, with the C dump as oracle only for the
unknowns, for row selection and for filters. **Axis B** (what a value becomes on disk) is stored key
bytes only, and is **driven from the C library's recorded value** rather than from our evaluator, so
a wrong kernel and a wrong transform cannot cancel. The mutations that prove the separation are in
`PLAN.md`: breaking prefix `=` must redden axis A only; skipping the `hasTrim` rewrite, axis B only.

## Decisions taken here

**ADR-42** — the engine takes an explicit date policy, defaulting to invariant.
**ADR-43** — Visual FoxPro's functions are supported, though CodeBase has none of them.

Decisions 1, 3, 5 and 8 of `DESIGN.md` remain ADR candidates for step close, following step 009's
pattern of writing them at closeout.
