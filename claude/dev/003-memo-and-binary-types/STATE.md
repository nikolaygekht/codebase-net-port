# 003-memo-and-binary-types — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** **closed** — all sub-steps done, gate passed, 699 tests green.
**Current sub-step:** none. Read [`SUMMARY.md`](SUMMARY.md); it is the file worth keeping.

## Done so far

- [x] `DESIGN.md` — 14 decisions, 4 open questions
- [x] `PLAN.md` — 9 sub-steps, the gate, 6 risks
- [x] 1 `MemoReference` reads both encodings
- [x] 2 `MemoBlockHeader` and `MemoType`
- [x] 3 `MemoReader`, with the three corruption guards
- [x] 4 the `M`/`X`/`G`/`Z` rows in `FieldValueDecoder`, refusals included
- [x] 5 the memo accessors on `Table`
- [x] 6 the payloads, mutation-checked four ways
- [x] 7 `GetMemoString` and the code pages
- [x] 8 the gate stops skipping — `RecordGoldenTests` asserts every field of every record
- [x] 9 `FPT-MEMO.md` updated: open question 1 closed, §3.9 and open question 3 rewritten

## Notes for the next session

Everything durable was promoted: the decisions to `ARCHITECTURE-DECISIONS.md` (**ADR-23**, which is
`open`), the format facts to `FPT-MEMO.md`, the outcome to [`SUMMARY.md`](SUMMARY.md), the position
to the root `STATE.md`. Nothing here is needed to start the next step.

**One thing left open on purpose:** ADR-23. Compressed entries are refused because no corpus case can
gate them, not because the format is unknown or the dependency is a problem. Closing it is its own
step and the recipe is in the ADR.

## Deviations from the plan

Three, all in [`SUMMARY.md`](SUMMARY.md): `BlankRecord`'s blank-reference rule was wrong and this
step's own end-of-file test caught it, `GetMemoType` was kept rather than dropped, and the gate's
skip counter was removed rather than asserted to be zero.

## Blockers

None.
