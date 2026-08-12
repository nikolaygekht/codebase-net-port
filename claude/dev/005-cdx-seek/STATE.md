# 005-cdx-seek — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** designed, not started
**Current sub-step:** PLAN.md step 1 — the `[seeks]` section in the generator

## Done so far

- [x] phases 1-5 — `DESIGN.md` and `PLAN.md` written
- [ ] step 1 — the `[seeks]` dump section
- [ ] step 2 — read the coverage off the dump (Q1, Q3)
- [ ] step 3 — `KeySearch` and the two block searches
- [ ] step 4 — `TagCursor.Seek` and the descent
- [ ] step 5 — descending seek
- [ ] step 6 — `SeekExact(key, record)`
- [ ] step 7 — `KeyIncrement` and `SeekAtOrBefore`, the primitive
- [ ] step 8 — `SeekLast`, `SeekNext` and `SeekPrevious`
- [ ] step 9 — the gate
- [ ] step 10 — documents

## Notes for the next session

Sub-step 1 needs **Windows and MSVC** (ADR-02) and touches the checked-in dumps, so the first
thing to verify after regenerating is that the existing `[keys]` and `[blocks]` sections did **not**
move — 004's gate reads them, and a re-baseline there would be invisible.

Two facts from 004 that this step depends on: `tfile4seek` **mutates the caller's key buffer** on a
descending tag, so each generated case needs its own copy; and `tfile4count` is unusable, so anything
that needs a key count walks with `tfile4dskip`.

Five operations, of which two are ports and three are additions — `SeekAtOrBefore`, `SeekLast` and
`SeekPrevious` have no counterpart in the C library, so they are gated as properties and tied to the
ported `Seek` by an adjacency check (Decision 9). Build `SeekAtOrBefore` first: `SeekLast` is one
comparison on top of it and both need `KeyIncrement`.

The `[seeknext]` section is driven through `d4seekN`/`d4seekNextN` — the **length-taking** forms, because
`T_BIN`'s keys hold `0x00` and the string forms would stop there — and only on the ten tags whose key
transform is the identity. `SeekLast` and `SeekPrevious` have no C counterpart at all, so they are gated
as properties against 004's key sequence; keep that distinction visible in the test names.

## Deviations from the plan

None yet.

## Blockers

None. 004 supplies everything this step reads.
