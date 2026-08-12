# 004-cdx-tags-and-traversal — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** done
**Current sub-step:** none — closed, see [`SUMMARY.md`](SUMMARY.md)

## Done so far

- [x] phases 1-5 — `DESIGN.md` and `PLAN.md`; ADR-24 to ADR-27; `.IDX` brought into scope
- [x] step 1 — `CDXBASE` generator case
- [x] step 2 — the index dump writer
- [x] step 3 — `CDXDEEP` and the derived `IDXONE.IDX`
- [x] step 3b — `CDXCOLL`, the machine-versus-GENERAL collation case
- [x] step 4 — the entities
- [x] step 5 — `NodeReader`, `IndexFileReader`, `TagDirectory`
- [x] step 6 — `TagCursor` traversal
- [x] step 7 — the gate, with five mutation checks
- [x] step 8 — documents

## Notes for the next session

Nothing outstanding in this step. Two things a future session will want to know and would otherwise
rediscover the hard way, both now in `CDX-FORMAT.md`: `tfile4count` is wrong for descending tags, and
`d4check` cannot check a single-tag file at all.

## Deviations from the plan

Carried into `SUMMARY.md`. The one that mattered: ADR-25 assumed `d4check` would certify the derived
`.IDX`, and it cannot, so the witness is the dual-shape walk instead.

## Blockers

None.
