# 006-tags-on-a-table — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** designed, not started
**Current sub-step:** PLAN.md step 1 — `KeyTypeResolver`

## Done so far

- [x] phases 1-5 — `DESIGN.md` and `PLAN.md` written; ADR-28 recorded
- [ ] step 1 — `KeyTypeResolver`
- [ ] step 2 — opening the production index with the table
- [ ] step 3 — `Tag` and `TagCollection`
- [ ] step 4 — `SelectTag` and tag-order `Top`/`Bottom`
- [ ] step 5 — the explicit `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed` four
- [ ] step 6 — tag-order `Skip` and the two ends
- [ ] step 7 — the gate
- [ ] step 8 — documents

## Notes for the next session

**No generator work and no Windows needed**: the corpus already holds everything this step is
gated against, and the gate joins two existing dumps by record number.

This step is independent of 005 — navigating in tag order needs traversal, not seek — so the two can be
done in either order. Doing 005 first means the public surface arrives complete in 007 rather than
growing a `Seek` afterwards.

Two surfaces onto one implementation (Decision 9): the C library's `Top`/`Bottom`/`Skip` with a selected
tag, and the explicit `…Indexed` four that name the tag per call. They must never drift, so the corpus
walk in the gate runs through both.

The risk that matters is a **regression in record-order navigation**, which 900 existing tests cover:
keep the tag path a branch rather than a rewrite, and the seven un-indexed tables honest.

## Deviations from the plan

None yet.

## Blockers

None.
