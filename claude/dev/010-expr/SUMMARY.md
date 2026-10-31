# 010-expr — summary

**A tag stops having to be a bare field name.** `UPPER(NAME)`, `STR(ID)`, `DTOS(HIRED)`,
`NAME+CITY` — the expressions real applications index by — are tags this library types, keys, walks
and positions into, exactly as a bare field is. Capability `EXPR`, **done 2026-09-03**.

## The gate, and what it says

| Assertion | Result |
|---|---|
| Our `FunctionTable` equals the C's `v4functions`, row for row | 193 rows, every field |
| Resolution: per node `(row, length)` and the conditional's second length, against `E4INFO` | 212 expressions, exact |
| Row coverage | three counts: **144 oracle-gated, 49 declared unreachable with a reason, 15 that the reference has no equivalent of** — and every one of the fifteen exercised |
| Refusals: this port refuses exactly what the reference refused, with its code | 17 of 18, the 18th recorded as accepted |
| Values: raw result bytes | **131 constant expressions, 77 field expressions × 16 records** |
| Keys: every stored key of every `CDXEXPR` tag | **22 tags, 338 keys, byte for byte**, and every computed key length equals its header's |
| End to end | every tag selectable, each walks the reference's own order, and a move by record number rebuilds that record's key and descends to it |
| The optimizer contract: the node table's six fields, evaluate-at-a-node with no record, the is-constant predicate, and a `Tag` keeping its compiled expression | all four clauses, over every expression in the dump — **the one part with no oracle**, so the corpus supplies the cases and not the answers |
| The thirteen functions Visual FoxPro has and the reference refuses | **36 values on stored bits and 16 tag key lengths, against Visual FoxPro's own answers** — its own oracle, because the corpus generator is the thing that cannot answer |
| Everything that existed before the step | green, **no golden expectation changed** |

**1909 tests** at close, 639 golden — up from 1450.

## What the step is made of

`net/CodeBase.Net/Expressions/`: the 193-row `FunctionTable` and `FunctionRows`, `ExpressionLexer`,
`ExpressionParser`, `TypeResolver`, `CompiledExpression`, `ExpressionEvaluator`, `ExpressionValue`,
`ExpressionKey`, `ExpressionOptions`, `EvaluationContext`, `IFieldMetadata`/`TableFieldMetadata` —
and a second row band for the fifteen rows Visual FoxPro adds, indexed on past the terminator so the
transcribed 193 stay exactly 193.
Eleven `ErrorCode` members. `SeekConverter`, `KeyTypeResolver`, `RecordKey`, `TableTagCursor`,
`Table` and `CodeBaseEngine` changed to run through it.

## The eight things worth carrying forward

1. **The parse pass is bigger than a shunting yard.** A field's loader row comes from its type, the
   parameter count is checked, `DTOC`/`TTOC` with two parameters are rewritten, and nine functions
   fold a constant parameter away and **remove its node** — `STR(K_NUM,12,3)` is two nodes, not four.
2. **Promotion rewrites a row and adds no node**, and it writes the new length onto the *simulated
   stack*, never onto the node — so a promoted numeric field still reports the field's width. The
   design said otherwise and was corrected; a mutation now proves it.
3. **The inclusive comparisons do not break a tie on width and the strict ones do.** `"AAAA" > "AAA"`
   and `"AAAA" <= "AAA"` are both true, because the tie-break flag is set only on the two named head
   rows and `<=` runs the greater kernel on its own node. **Two records in the whole corpus
   distinguish it**, and no constant expression could have.
4. **`STR` rounds the decimal digits, not the binary value** — 0.135 at two decimals is 0.14. Going
   through a `decimal` is what reproduces it.
5. **A memo operand is NUL-padded**, which matters because key time turns trailing zeros into blanks,
   so a memo operand keys as though trimmed.
6. **The two-axis gate earned its keep.** Skipping the `hasTrim` rewrite reddens axis B only, on
   `TRIM(K_TEXT)`'s keys **and only those** — the prediction `PLAN.md` wrote before any of it existed.
7. **The corpus was sized by row coverage, not by taste.** The resolution gate found 46 reachable
   table rows nothing reached; a second generator run added them and every case landed on the row it
   was written for, with the corpus regenerating byte-identical except the expression dump.
8. **The is-constant predicate is narrower than the truth and had to stay that way.** `1+1` is not
   constant because no row says so, and neither is a narrowed float literal, so a comparison against a binary
   — so a comparison against a binary float field is predicted never optimizable. Transcribed rather
   than tidied, because disagreeing with the reference about which queries optimize is worse than the
   missed optimization, and the contract test says so in those words.

## The five Visual FoxPro rules a reader would guess wrongly

`MOD` takes its sign from the **divisor** (floored, not truncated). `INT` truncates towards zero on
both signs, and `INT(-0.5)` is stored as a **positive** zero — bits that no formatting distinguishes
and that an index key carries. `ROUND` rounds half away from zero on the **decimal** digits, the same
rule `STR` follows. `AT` counts from **one** and answers zero for absent. `MIN` and `MAX` are
variadic and have a string form. All five witnessed in the oracle's stored bits, all five in
`specs/EXPRESSIONS.md` §5.2.

## Deviations from the plan

- **Sub-steps 2 and 3 swapped their first halves** (ADR-47): the scanner is table-driven, so the
  table had to be transcribed first.
- **`DTOS` moved from sub-step 6 to 5**: it is the julian-to-text conversion `DAY`/`MONTH`/`YEAR`
  need anyway, so splitting it off would have been artificial.
- **`ExpressionOptions` is public**, against `DESIGN.md`'s "almost none". ADR-42 requires a caller to
  be able to name a century, and there is no other way to say it.
- **`numEntries` is computed in the parser**, where the reference computes it during resolution. It
  follows from the operand count and the postfix order alone, and moving it is what let the parse
  pass be gated as a shape before any type existed.
- **A second generator run happened**, which `PLAN.md` budgeted for and which was needed exactly
  where it said.

## Owed, and named rather than skipped

- **The Windows ANSI case mapping, for `UPPER` and `LOWER` alike** — one gap, not two. Both engines
  map by the table's code page and only the ASCII path is implemented. The CodeBase corpus witnesses
  only ASCII; Visual FoxPro's oracle has exactly one case above 0x7F, and it is excluded from the gate
  by name. Closing it needs a transcribed case table per code page, because `CLAUDE.md` forbids a
  culture-aware routine for keys.
  *Corrected 2026-10-31 (step 012):* neither engine maps by the table's code page — both follow the
  host's ANSI code page (CodeBase only for 1252/1250, ASCII otherwise). Closed by ADR-65 without
  transcribed tables: case is changed by the table's own code page through `Encoding` and Unicode's
  invariant mapping, which is not a culture; the excluded case is gated now.
- **`MOD` by zero and `SQRT` of a negative**, both answering zero where Visual FoxPro raises. The
  oracle wrapped `SQRT` in `ABS` precisely to avoid the second, so there is no witness for either.
- **A second rejection pass with a real `TAG4FILE`**, for the tag-only refusals (`e4tagExpr`), which
  are unobservable while everything is parsed with `tagPtr = 0`.
- **`<>` over one absent operand**, implemented per the spec and gated by nothing.
- **ADR-42's other three language presets**, a picture string each.

## Decisions

Taken during the step: **ADR-42** date policy · **ADR-43** VFP's functions · **ADR-44** the VFP
oracle · **ADR-45** prefix `=` and division by zero · **ADR-46** the optimizer's contract on this
step · **ADR-47** the table before the scanner.

Written at close, from `DESIGN.md`'s decisions 1, 3, 5 and 8: **ADR-48** the type set is a closure
derived from what the reader admits · **ADR-49** key-legal and filter-legal are different sets, and a
non-deterministic key is refused · **ADR-50** prefix equality follows the reference, and the
tie-break asymmetry that comes with it · **ADR-51** the engine is internal except `ExpressionOptions`.
