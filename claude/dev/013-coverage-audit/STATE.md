# 013-coverage-audit — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** not started — `DESIGN.md` and `PLAN.md` written 2026-10-31; Decision 4 (F5) decided by
the user, ADR-67
**Current sub-step:** PLAN.md step 1

## Done so far

- [ ] step 1 — fresh baseline
- [ ] step 2 — F1a, decomposition
- [ ] step 3 — F1b, execution
- [ ] step 4 — F1c, the result cursor
- [ ] step 5 — F2, keys from values
- [ ] step 6 — F3, the expression engine
- [ ] step 7 — F4 + F5, the tag cursor and the public API
- [ ] step 8 — close

## Notes for the next session

`AUDIT.md` was measured before step 012, which changed code under F1, F3 and F4 (`Query` gained
component tests; `TableTagCursor.SearchFor` rose to CRAP 39.2 with the conversion and null-byte
branches). Sub-step 1 re-measures before anything else.

## Deviations from the plan

None yet.

## Blockers

None.
