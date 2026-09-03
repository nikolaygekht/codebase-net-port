# 010-expr — state

**Updated:** 2026-09-03 · **sub-step 1 of 8 is done, sub-step 5b has its oracle, and the design now
carries the optimizer's contract.** The step is designed ([`DESIGN.md`](DESIGN.md)), planned
([`PLAN.md`](PLAN.md)) and has both oracles in the corpus. **No C# of the expression engine exists.** The **second oracle** (Visual FoxPro 9 in a
VM, **ADR-44**) has been **run**, and its output is committed in `net/corpus/vfp/`.

## Where the sub-steps stand

| # | sub-step | state |
|---|---|---|
| 1 | Generator: the `H` probe, `CDXEXPR`, the five dumps | **done** |
| 2 | Lexer | not started — **the next session starts here** |
| 3 | Function table + parser | not started |
| 4 | Types and overload resolution | not started |
| 5 | Evaluator: kernels and null semantics | not started — *the stop point* |
| 5b | The VFP-only functions (ADR-43) | not started — **its oracle is run and committed**: `net/corpus/vfp/`, from `test-files-generator/vfp/vfpfunc.prg` |
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
| `[vfp-only]` | 13 names VFP has and CodeBase does not | **no — the port accepts these (ADR-43)**, and VFP is their oracle (ADR-44) |

Each expression also carries its **compiled node array**: per node the row index of `v4functions`
that overload resolution chose. That is the resolution gate, and it is what turns "a missing overload
row silently selects its neighbour" into a mismatched integer.

## The second oracle, for sub-step 5b

ADR-43 accepts thirteen functions CodeBase refuses, and nothing in `net/corpus/` can gate them — the
generator *is* CodeBase. **ADR-44** settles that: Visual FoxPro 9 is a second offline oracle, under
ADR-01's discipline (generate offline, commit the output, never let the build run it), and its
scripts live in [`test-files-generator/vfp/`](../../../test-files-generator/vfp/README.md) with the
output published into **`net/corpus/vfp/`** — a subdirectory, because `Corpus.TableNames` is not
recursive and these tables have no C-written dump beside them.

`vfpfunc.prg` **was run on 2026-08-29** and its output is committed: `VFPFUNC.DBF` + `.CDX` with
18 tag groups (the thirteen `[vfp-only]` names in this dump's order, plus `PROPER` and `STRTRAN` for
the per-function *length* rule, `TRIM` and `STR` as divergence probes, and `UPPER` as a control) and
`VFPFVAL.DBF`, 77 constant expressions whose results are stored in fields of their own type.

**The `.DBF`/`.cdx` are the oracle, not the text dumps beside them.** Forced rather than chosen: the
C library cannot dump a VFP-built CDX, because opening a tag parses its expression
(`i4ntag.c:1390-1395`), so a tag over `LOWER(…)` never opens. It proved to matter — the key lengths
below are read out of the tag headers, and VFP does not expose them through any function.

**What it answered, and what it changed:**

- **`T_STRTRN`.** VFP sizes a value-dependent result at the **operand's** width (20), writes the tag
  header, and then fails at *build* time on the first row whose result does not fit. `DESIGN.md`'s
  key-length rules can therefore keep computing from the operand. The refused tag's three blocks stay
  in the file, so **the tag directory is the only authority on which tags exist**.
- **`T_VTRIM` does not diverge**: `TRIM(k_text)` keys to 20, the source width, as CodeBase does. The
  reading hazard ADR-43 could not settle is not there.
- **`STR` agrees** on all four midpoint probes, closing ADR-43's open question.
- **`STRZERO` is not a VFP function at all**, which is why two `VFPFVAL` rows are refusals.
- **Two divergences are now decided, as ADR-45**: prefix `=` (and it runs the **opposite** way from
  what `e4parse.c:59-62` says, which `DESIGN.md` Decision 8 had repeated — corrected in place), and
  the century pivot, where VFP has a rollover and CodeBase has none.

Sub-steps 2-5 never waited on it, and still do not.

## Two things owed to the corpus

- **A second rejection pass with a real `TAG4FILE`.** Everything is parsed with `tagPtr = 0`, so
  `e4tagExpr` (−510) never appears and `OTHER->K_TEXT` returns `e4fieldName` (−210). The tag-only
  refusals are unobservable until this exists — including whether `DESIGN.md` §3's
  "a TRIM result may not feed `SUBSTR`" restriction bites at all. It **does not** without a tag.
- **`CachedReadGoldenTests.KeysABareField` is deleted at sub-step 8**, with ADR-28's refusal. It is
  the one place the suite currently averts its eyes, and it exists only because `CDXEXPR` had to land
  before the engine that reads its tags.

## What `QUERY-OPTIMIZER.md` changed in this step (2026-09-03)

[`specs/QUERY-OPTIMIZER.md`](../../specs/QUERY-OPTIMIZER.md) was written before sub-step 2 and read
back against this step's design. It **corrected one thing and added three**, all in `DESIGN.md` §6
(new) and §2b, mirrored in `PLAN.md`'s sub-steps 3, 4, 5 and 8.

**The correction, which stands on its own.** `DESIGN.md` §2b said a promotion would be "wrapping a
child in a conversion node". It is not: all six are an in-place rewrite of the producer's row and
length (`e4parse.c:227-289`), so **node count is invariant under promotion**. A wrapper node would
have failed `PLAN.md` sub-step 4's own gate, which is a *sequence* equality against the C's `E4INFO`
array — so this was a contradiction inside the step, independent of `QUERY`. A mutation now proves
it: wrap one promotion, and the resolution gate must redden on sequence length.

**Why the optimizer cares about the same fact.** `e4isTag` (`r4log.c:61-255`) matches a subexpression
to a tag by comparing node counts first, then rows, tolerating a differing row **only when both are
field loaders** (`r4log.c:234-236`) — and the six promotions target exactly the field-loader rows
(6, 8, 18, 19, 25) plus `E4FLOAT` for the constant case. Row-rewrite promotion and that exemption are
a matched pair. With wrapper nodes, `K_NUM > 0` would never match a tag on `K_NUM`, and every
promoted comparison would silently fall back to scanning.

**The three additions**, all cheap now and expensive after sub-step 4 freezes the node shape:

| # | Addition | Sub-step |
|---|---|---|
| 1 | `CompiledExpression` **retains** the node table — row, result type, declared length, parameter count, subtree size, field. The first three the resolution gate already needs; the last three are free to keep and cannot be rebuilt later | 3-4 |
| 2 | The evaluator answers **at a node index**, and needs **no record** when that subtree is constant; plus a per-node **is-constant** predicate mirroring `e4isConstant` | 5 |
| 3 | A `Tag` **keeps** its `CompiledExpression` rather than discarding it once `SeekConverter` has taken kind, length and pad | 8 |

One detail worth not rediscovering: the length the optimizer reads is the node's **declared** length,
not the value's runtime width. `"SM"` is declared 2 against a `keyLen` of 20, and that is what makes
`NAME = "SM"` a prefix range rather than a wrong equality.

**Structural equality itself is step 011's**, as are the `BITMAP4` tree, the merge algebra, the four
seek positions and the `F4FLAG` bitset. Nothing in them constrains this step.

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
**ADR-44** — Visual FoxPro 9 is the second offline oracle, in its own frozen corner (supersedes
ADR-04, unblocks ADR-11).
**ADR-45** — prefix `=` and division by zero replicate CodeBase; the oracle also corrects what the
source says VFP does.
**ADR-46** *(open)* — the optimizer's spec is written, and it leaves two decisions the port must
make; it also records that `EXPR` constrains `QUERY` at sub-steps 3-4, which is why `DESIGN.md` §6
exists.

Decisions 1, 3, 5 and 8 of `DESIGN.md` remain ADR candidates for step close, following step 009's
pattern of writing them at closeout.
