# 011-query-optimizer — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** designed and planned — **not started**
**Current sub-step:** PLAN.md step 1, the query oracle. No C# of the optimizer exists.

## Done so far

- [x] phases 1-5 — [`DESIGN.md`](DESIGN.md) and [`PLAN.md`](PLAN.md) written, 2026-09-04
- [ ] step 1 — the query oracle (generator; needs a Windows/MSVC run)
- [ ] step 2 — eligibility and subexpression identity
- [ ] step 3 — decomposition
- [ ] step 4 — the merge algebra
- [ ] step 5 — leaf evaluation
- [ ] step 6 — combination
- [ ] step 7 — the residual filter and the public surface
- [ ] step 8 — the equivalence gate, and the widened scope
- [ ] step 9 — close

## Notes for the next session

**Two decisions were taken during design and are already ADRs**, so they do not need re-deriving:
tag eligibility replicates CodeBase by default and widens under an explicit `QueryTagScope`
(**ADR-52**), and the reference's `tagPos = 1` defect is not replicated — the intent is
(**ADR-53**). Both close [ADR-46](../../ARCHITECTURE-DECISIONS.md), which was open on exactly them.

**Two things were settled by reading the C during design, and are worth not re-discovering:**

- **`QUERY-OPTIMIZER.md` §12 item 2 is resolved.** The partial-string widening looks asymmetric
  because only two of six branches apply it, but those two are exactly the two that write the `gt`
  slot, and they are the only two that write it (m4map.c:211-283). Not a defect. `DESIGN.md`
  Decision 6.
- **The reference's residual trust test reads the wrong positions.** `log4true` tests
  `(infoReportPtr-1)->tag` and `-2`, which are the operand roots only when the right operand is a
  single node (r4log.c:770, read directly). Where they are not, the reference re-evaluates a term its
  own bitmap answered — conservative, never wrong. The port trusts per *term* instead and so trusts
  more. `DESIGN.md` Decision 5; depth 4 of the gate is what proves it safe.

**The oracle is the gate's spine and it needs one Windows trip.** Sub-step 1 writes no C# of the
optimizer, exactly as step 010's sub-step 1 did. Fold into the same run the second rejection pass
with a real `TAG4FILE` that step 010 left owed, and answer `DESIGN.md`'s open questions 1-3 from the
dump rather than from reading.

## Deviations from the plan

None yet.

## Blockers

None. Sub-step 1 can start immediately; it needs `test-files-generator/` on Windows, which is the
same dependency step 010 ran under.
