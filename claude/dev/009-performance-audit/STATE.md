# 009-performance-audit — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** in progress — the audit is closed, its remediation is designed and planned and **not
started**
**Current sub-step:** [`PLAN.md`](PLAN.md) step 1, not begun. **No `.cs` file has been opened.**

## Done so far

The audit itself:

- [x] measured the read path — [`SUMMARY.md`](SUMMARY.md), [`METHOD.md`](METHOD.md),
      [`results.txt`](results.txt)
- [x] decomposed the 14× and priced a cache for this port — [`ANALYSIS.md`](ANALYSIS.md),
      [`results-cpu-analysis.txt`](results-cpu-analysis.txt)
- [x] designed and planned the remediation — [`DESIGN.md`](DESIGN.md), [`PLAN.md`](PLAN.md)

The remediation:

- [ ] 1 — benchmark project + `PERF10K` generator case, committed as **benchmark data, not a gate**
- [ ] 2 — compare in place (`LeafBlock.CompareAt`, `BranchBlock.ChildAt`, span compare in `Seek`)
- [ ] 3 — the padded comparison: measure, then fix or write down that it did not matter
- [ ] 4 — `BlockResidency` + `BlockPool`, proved with no source attached
- [ ] 5 — wire the cache up: `BlockCacheMode`, `CachingSourceFactory`, `DropCachedBlocks()`
- [ ] 6 — `Table.Refresh()` and the `Invalidate` / `Flush` hooks §3.7 will call

**Stoppable after 3** with all the CPU work done and the cache still owed.

## Notes for the next session

- **Start at `PLAN.md` step 1.** Read [`DESIGN.md`](DESIGN.md) first; the two decisions that shape
  everything are that the cache decorates `IRandomAccessSource` (not `NodeReader`, because a walk's
  reads are 96% DBF) and that it is **optional on the reference's own three-valued policy**, defaulting
  to `WhenExclusive`.
- **The measurement harness already exists** and is committed under [`harness/`](harness/);
  [`METHOD.md`](METHOD.md) has the command to lay it back out into the gitignored
  `experiments/performance-1-experiment/`. Sub-step 1 turns it into a real benchmark project — it is a
  starting point, not something to reinvent.
- **Warm up by wall clock, never by pass count.** This audit's own first RAM-backed measurement was 4×
  wrong because five passes never gave the JIT time to reach tier-1 ([`ANALYSIS.md`](ANALYSIS.md) §8).
  BenchmarkDotNet is chosen for exactly this reason. The standing check: a figure that moves when only
  its *position* moves is measuring the runtime, not the code.
- **`EntryAt`'s copy is load-bearing** — `IndexGoldenTests.cs:88` accumulates 3 364 keys across cursor
  moves. Sub-step 2 is additive and must leave `EntryAt` alone.
- **Nothing here has an ADR yet.** `DESIGN.md` marks the candidates; `PLAN.md` step 5 writes them. If
  the design survives execution unchanged they are transcription; if it does not, they record what
  actually happened, which is the point.

## Deviations from the plan

None — execution has not started.

## Blockers

None. One open decision that is **not** blocking, recorded in the root `STATE.md` §3: whether
`LOCKING` moves ahead of `WRITE` in `PORTING-PLAN.md` §5. It affects the step *after* this one, not
this one.

The coupling it comes from is captured so it cannot be lost: `PORTING-PLAN.md` §5's
**cross-capability obligation** table says what `WRITE`, `LOCKING` and `TRANS` each owe the cache, and
sub-step 5 must build `Invalidate(source)` / `Flush(source)` as real entry points so `LOCKING` has
something to call.
