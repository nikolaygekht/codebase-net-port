# 006-tags-on-a-table — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** closed — gate passed, see [`SUMMARY.md`](SUMMARY.md)
**Current sub-step:** none

## Done so far

- [x] phases 1-5 — `DESIGN.md` and `PLAN.md` written; ADR-28 recorded
- [x] step 1 — `KeyTypeResolver`
- [x] step 2 — opening the production index with the table
- [x] step 3 — `Tag` and `TagCollection`
- [x] step 4 — `SelectTag` and tag-order `Top`/`Bottom`
- [x] step 5 — the explicit `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed` four
- [x] step 6 — tag-order `Skip` and the two ends
- [x] step 7 — the gate, and four mutation checks
- [x] step 8 — documents: ADR-29, ADR-30, `CDX-FORMAT.md` §7.1, `PORTING-PLAN.md` §5, `README.md`

## Notes for the next session

**007 is next** — seek by value on a `Table`, which needs `COLLATION`'s machine-half transforms.
`KeySearch` is the seam it plugs into.

Two things `EXPR` still owes this step, both refusals rather than gaps: typing a key expression that is
not a bare field name (ADR-28), and positioning a tag on a record it does not list (ADR-30).

## Deviations from the plan

Recorded in [`SUMMARY.md`](SUMMARY.md) — five, of which two produced ADRs (29 and 30) and one a new
specification subsection (`CDX-FORMAT.md` §7.1).
