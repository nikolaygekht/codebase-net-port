# 002-dbf-records-and-fields — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** **closed** — all sub-steps done, gate passed, 630 tests green.
**Current sub-step:** none. Read [`SUMMARY.md`](SUMMARY.md); it is the file worth keeping.

## Done so far

- [x] `DESIGN.md` — 16 decisions, 6 questions
- [x] `PLAN.md` — 10 sub-steps, the gate, the risks
- [x] design review 2026-08-11 — citations re-verified against the C, two fixed, three
      state-machine facts added (Decisions 14-16), Q1 and Q3-Q6 closed, three risks retired
- [x] 1 `CorpusDump` reads `[records]` — no section is deferred any more
- [x] 2 `RecordBuffer` and `RecordPosition`
- [x] 3 `RecordReader`, with the offset formula mutation-checked
- [x] 4 navigation on `Table`
- [x] 5 the simple decoders and the type matrix
- [x] 6 `FoxNumeric` — **Q2 closed, all 224 values match bit for bit**
- [x] 7 `FoxDateTime` and `FoxCurrency`
- [x] 8 text, per ADR-21
- [x] 8a the trimming evaluation — ADR-22, no API added
- [x] 9 the gate: `RecordGoldenTests`, plus `TableScanGoldenTests` walking each table four ways
- [x] mutation checks: the record offset, every cursor-flag transition, and four traversal bugs

## Notes for the next session

Everything durable was promoted: the decisions to `ARCHITECTURE-DECISIONS.md` (ADR-21, ADR-22), the
outcome to [`SUMMARY.md`](SUMMARY.md), the position to the root `STATE.md`. Nothing here is needed to
start step 003.

## Deviations from the plan

Five, all recorded in [`SUMMARY.md`](SUMMARY.md): `BlankRecord` is a class the design did not name,
`RecordPosition` needed two flag transitions the design had missed, `FoxDateTime.ToText` exists so
the gate can compare against the dump's rendered form, sub-step 8a concluded that no API should be
added, and two flag-raises the C performs on the empty-table skip path were dropped as provably dead.

## Blockers

None.
