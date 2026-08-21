# 009-performance-audit — remediation plan

Phase 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). The design is [`DESIGN.md`](DESIGN.md).

**Stoppable after sub-step 3.** Sub-steps 1–3 are CPU work with no design risk and nothing new in the
public surface; the cache is 4–6 and carries the ADR. If the step has to end early, it ends after 3 with
a `SUMMARY.md` saying the cache is still owed.

## Tests

Four layers, each naming what only it can catch (`DEV_APPROACH.md` §4), and **a fifth this step
introduces** — a performance layer, because this is the first step whose deliverable *is* a number and
the four correctness layers all pass on a cache that never hits.

| Layer | What goes here | What only this layer catches |
|---|---|---|
| **1. Unit** | `BlockResidency` driven exhaustively: which block retires for a given set of slots, priorities and access order; that an index block is never chosen while a data block is available; that a pool of one still makes progress. The three-valued policy resolved against each access mode — `Off` never caches, `Always` always does, `WhenExclusive` caches only a source that reports itself exclusive — which is `f4opt.c:492-501` as a truth table. `KeySearch.Compare` over prefix and padded values, including a value one byte short of the key and an all-pad value. | Every branch of the eviction rule and of the policy, at memory speed, with no file and no clock. A wrong victim is the one way this step can corrupt a read, and it is provable only here — as is `WhenExclusive`, which no real file can currently exercise. |
| **2. Component** | `CachingRandomAccessSource` over a **counting in-memory source**: a second read of the same block does not reach the source; a read spanning two blocks returns the same bytes as the uncached source; a read larger than the pool still returns the right bytes; an unaligned record-sized read at every offset in a block. And `LeafBlock.CompareAt` / `BranchBlock.ChildAt` agreeing with `EntryAt` on every entry of a real corpus block. | Sequencing and residency without a disk — the straddling read, the eviction under pressure, and the promise that a hit is a hit. Nothing below it sees two calls; nothing above it can tell a hit from a miss. |
| **3. Fault injection** | Moq on the wrapped source: `IOException` mid-fetch leaves no half-filled block resident; a short read is not admitted as a full block; the exception propagates unchanged. Hand-built corrupt blocks are already covered by existing tests and are not re-done here. | That a failed fetch cannot leave the pool holding wrong bytes — the corpus cannot express a failing disk. |
| **4. Golden / corpus** | The **whole existing golden suite, unchanged**, run twice: once uncached and once with the cache enabled through the engine's internal constructor. Both must produce identical results. | Whether the cache changes what a real file decodes to. This is the gate. |
| **5. Performance ratio** | Two deterministic and one timed, all on `PERF10K` in-process, cache `Off` against cache `Always`: (a) the cached seek storm and the cached tag walk issue **at least 2× fewer reads** than the uncached ones over the same query set, counted at the source; (b) allocation per seek is under a documented ceiling (`GC.GetAllocatedBytesForCurrentThread`); (c) **the cached wall clock is at least 2× faster**, best-of-N, both orderings. | That the optimization *still pays*. Nothing in layers 1–4 can tell a working cache from a correct one that never hits, or from a machine on which caching has stopped being worth doing — and those are the two ways this step silently becomes dead weight. |

**The parameterised golden run is the heart of it.** Every corpus assertion the port already makes —
3 364 keys, 22 tags, every field of every record of eleven tables — becomes a cache test for free by
running it a second time through a cached factory. A cache that serves a stale or neighbouring block
fails hundreds of assertions, and no new expectation has to be written for any of it.

**The 2× floor is a tripwire, not a target.** Measured is **11.6×** on a seek and **23.4×** on a walk
(`SUMMARY.md`), so 2× leaves 5.8× and 11.7× of headroom — enough to survive a `Debug` build, a loaded
machine and a shared CI runner, while still reddening long before the cache stops earning its
complexity. Three things can make it fail, and the assertion message must name all three so a red is
diagnosable from the log alone: **the cache regressed**; **the uncached path got fast** — a runtime,
kernel or hardware change that made syscalls cheap enough that this optimization is no longer needed,
which is exactly the signal worth having; or **the machine is too noisy to measure**, which the method
below is built to rule out first.

**Method, and it is the audit's own scar written down** (`ANALYSIS.md` §8): warm up **by wall clock**
and never by pass count; measure **best-of-N** and compare the *minima*, because noise only ever adds
time, so the minimum is the most stable statistic a test can take; run the pair in **both orders** and
require both to pass, which is the order-effect check the harness already learned; and let the warm-up
guarantee the file is in the OS page cache for *both* variants, so the ratio measures syscalls and CPU
avoided rather than disk — a **lower** bound for a cold or larger table, on the same reasoning as the
audit's own numbers.

**And the ratio is what makes the gate portable.** Measured 2026-08-21 while sub-step 1 was built: the
same benchmark on the same files reports **194 µs** per walked record and **1.58 ms** per seek when run
from WSL against `/mnt/d`, against **~9 µs** per seek run natively on Windows — a 25–200× difference
that is the filesystem bridge and none of it the code. An absolute threshold would therefore have to be
either wrong on one side or so loose it caught nothing. A *ratio* between two runs on the same machine
in the same process cancels the bridge out entirely, and a costlier read only makes the cache's win
larger, so the 2× floor holds on both sides for the same reason.

**Two of the three do not depend on a clock at all**, and that is deliberate. A read count and an
allocation count are exact, machine-independent and cheap, and between them they catch every *code*
regression the timed test would catch. The timed one is kept for the single thing they cannot see: the
day caching stops being worth it. If the timed assertion ever becomes a maintenance tax, it is the one
that gets a trait and a documented opt-out — the deterministic two stay in the default run.

**No new expected values are invented.** Sub-step 2's `CompareAt` is asserted against `EntryAt` on real
corpus blocks (a round-trip invariant, allowed by §4); the cache is asserted against the uncached source
(the same); and the benchmark asserts nothing.

## Steps

| # | Sub-step | Ends in |
|---|---|---|
| **1** | **A benchmark project with a number in it.** `net/benchmarks/CodeBase.Net.Benchmarks` on BenchmarkDotNet: tag-order walk, seek storm over a character tag and a numeric one, `Go(n)`-then-`Skip(1)`. Add the `PERF10K` generator case (10 000 records, `C(20)` and `N(12,2)` tags) and **commit the table** — a benchmark case, so **no `.dump.txt` and no *correctness* expectation against it**; layer 5 asserts a *ratio* over it, which invents no expected bytes. Update `FOR-DEVELOPERS.md`, `SonarQube.Analysis.xml`, `net/corpus/README.md`, `test-files-generator/README.md`. | `dotnet run -c Release` in the benchmark project prints a baseline. Nothing in `dotnet test` changed. |
| **2** | **Compare in place.** `LeafBlock.CompareAt`, `BranchBlock.ChildAt`, the span compare in `BranchBlock.Seek`; the four call sites moved over. `EntryAt` untouched. | Suite green; layer-2 tests show `CompareAt` agrees with `EntryAt` everywhere; benchmark shows the descent's CPU down; and the layer-5 allocation ceiling (b) lands here rather than in the benchmark, so the ~1 300 bytes/seek stay gone — it is exact and needs no clock. |
| **3** | **The padded comparison — measure, then decide.** Add a short-value query set to the benchmark so the `ComparesPadded` branch is exercised at all. If it is material, build the comparand to key width once per search and use `SequenceCompareTo`. If it is not, **say so in `SUMMARY.md` and leave the code alone.** | A number for the scalar path, and either a fix with the suite green or a written "measured, left alone". |
| **4** | **`BlockResidency` and `BlockPool`, with no source attached.** The pure eviction rule and the residency store, driven entirely by layer-1 and layer-2 tests. Nothing wired into the engine yet. **Now rent-and-return** (`DESIGN.md`, Open questions, settled 2026-08-21): a rented block cannot be evicted until returned, which is the lifetime rule the per-read `byte[512]` needed — measured at ~40% of a RAM-backed descent — and it removes the copy on a hit as well. Buffers are exactly `BlockSize`, because `block.Length` is load-bearing in the leaf's key rebuild. | Exhaustive unit tests green, including: a rented block is never the eviction victim; a forgotten return costs capacity and not correctness; a double return and a use-after-return are caught by the `Debug` guard; every buffer handed out is exactly `BlockSize`. No behaviour change anywhere, because nothing calls it. |
| **5** | **Wire it up, optional, with the reference's policy, plus `DropCachedBlocks()`.** `CachingRandomAccessSource`, `CachingSourceFactory`, `BlockCacheMode` (`Off` / `WhenExclusive` / `Always`) and the engine properties; the per-file decision made at open, as `file4optimizeLow` does; `DropCachedBlocks()` on the engine and on `Table`, porting `code4optSuspend` / `d4freeBlocks`. Run the whole golden suite a second time with `Always`. | Suite green **both ways**; `WhenExclusive` caches nothing today and is proved to by a fake source that claims to be exclusive; a write behind the cache's back is seen after `DropCachedBlocks()` and not before it; **layer 5 green — reads down ≥ 2×, wall clock down ≥ 2× on both the seek storm and the walk, in both orderings**; benchmark shows the cache's numbers; ADR entries written. |

| **6** | **`Table.Refresh()`, and the hooks `LOCKING` will call.** `d4refresh` semantics without the lock branch: reset memo state, drop this table's cached blocks, re-read the current record in place. The cache's `Invalidate(source)` / `Flush(source)` entry points exist as such rather than as private helpers, so §3.7's "flush before unlock, invalidate after lock" has something to call. | Suite green; a layer-2 test writes behind the cache's back and proves the change is invisible before `Refresh()` and visible after; `Refresh()` leaves the cursor on the same record. |

Sub-step 4 before 5 is deliberate: the part that can be *wrong* gets built and proved with no file
anywhere near it, and only then does it start serving real reads.

## Gate

**Two mechanical gates: the correctness one is the existing one, and the speed-up one is new.**
`DEV_APPROACH.md` §1 wants a step to have exactly one gate — and this step has two on purpose, because
its deliverable is a number and correctness alone cannot express it. Both are `dotnet test`, both are
mechanical, and neither is an opinion. **This is specific to performance work and is not a template**:
a step whose deliverable is correctness keeps its single gate, and a second gate bolted onto every step
would be noise rather than cover.

- `dotnet test` green — **1177 tests** as the floor, plus whatever these sub-steps add — with **no golden
  expectation changed and no corpus file altered**. Same gate shape as the 002–005 remediation
  ([`../007-audit-glm/PLAN.md`](../007-audit-glm/PLAN.md)), for the same reason: none of this may change
  what a well-formed file decodes to.
- **The whole golden suite passes with the cache on as well as off**, from the same expectations.
- The benchmark reports before-and-after for each sub-step, and those numbers go in `SUMMARY.md`. A
  sub-step that cannot show its improvement did not land.
- **The layer-5 ratio tests are part of `dotnet test`, not of the benchmark run**, and a failure is a
  failure: **the cache must issue ≥ 2× fewer reads and be ≥ 2× faster in wall clock than no cache**, on
  the seek storm and on the walk, in both orderings. This is the only gate that notices the cache has
  stopped earning its keep — every other gate here passes on a cache that is perfectly correct and never
  hits. A red on the timed one is triaged against the deterministic two before anything is changed: if
  reads are still down 2× and only the clock is not, the finding is about the machine or the runtime, and
  it belongs in `SUMMARY.md` as a measurement, not in the code as a fix.

### Proving the gate

Break the code, watch exactly the intended tests fail, restore **from a checksummed copy, never from
`git`** (`DEV_APPROACH.md` §4 — step 006 and step 008 both lost `Table.cs` this way):

| Mutation | Must redden |
|---|---|
| `BlockPool` returns the block one index along | the cached golden run, in hundreds of places; the uncached run stays green — which is what proves the second run is really testing the cache |
| `BlockResidency` retires an index block while a data block is available | the layer-1 eviction tests only |
| A failed fetch admits the partially-filled block | the layer-3 tests only |
| `DropCachedBlocks()` retires nothing | the staleness test only — the one place a caller's escape hatch is proved to work |
| `LeafBlock.CompareAt` compares at `DupCount` instead of 0 | the seek goldens and the `CompareAt`-vs-`EntryAt` component test |
| The cache is consulted but **every lookup reports a miss** (results still correct) | **layer 5 only** — the read-count test and the timed test. Every other gate in this table stays green, which is the whole reason layer 5 exists |
| `CompareAt` copies the key before comparing, as `EntryAt` does | the layer-5 allocation ceiling only |
| `BranchBlock.ChildAt` reads the record number instead of the child | every tag-order golden walk |

A break that reddens nothing means the gate is decorative; one that reddens everything means it is not
localised enough to say what it proves.

## Risks

- **A stale block is a wrong record set.** The whole reason item 3 is optional and defaults to
  `WhenExclusive` (`DESIGN.md` decision 2) — the reference's own rule, which makes staleness *impossible*
  rather than unlikely by caching only files opened excluding other writers. The risk that survives is a
  caller who chooses `Always` while another process writes; the mitigation is documentation now and
  invalidation when `LOCKING` lands, and the ADR must say so rather than implying the cache is free.
- **`WhenExclusive` is dead code until `LOCKING`**, because no source reports an access mode yet. That is
  deliberate — it is the faithful default and it starts working by itself later — but it means the mode
  is only provable at layer 1 against a fake, and `SUMMARY.md` must say that plainly rather than let a
  passing test suite imply the path is exercised.
- **`Always` is a documented sharp edge, not a bug.** On a file another process writes it can serve stale
  blocks, exactly as `OPT4ALL` can, because in xBase currency comes from locks and this port has none yet.
  `DropCachedBlocks()` is the escape hatch that ships with it; the XML doc on the property has to say the
  quiet part out loud, and the layer-2 staleness test is what keeps the promise honest.
- **`Refresh()` is honest but slower than the C's**, which skips the work when a lock proves nothing can
  have changed (`D4FRESH.C:131-144`). With no locks, this port always does it. That is correct and it is
  a divergence to record in `SUMMARY.md`, not a defect — and it disappears when `LOCKING` supplies the
  test the branch needs.
- **Committed benchmark data is ~915 KB** and is *not* a gate: no dump, no golden assertion. The risk is
  that a future hand treats a benchmark file as a corpus case; `net/corpus/README.md` must mark it as
  benchmark-only.
- **"Run the golden suite twice" is one line here and 40 call sites in the code.** Counted 2026-08-21:
  `new CodeBaseEngine()` appears **40 times across the 9 golden classes that open a file**
  (`TableMetadata` 13, `SeekByValue` 9, `TagNavigation` 7, `Record` 4, `Synchronize` 2, `TableScan` 2,
  `Memo`/`Text`/`KeyTransform` 1 each). Sub-step 5 therefore starts with a decision it must record:
  parameterise those nine classes over `BlockCacheMode` so the cached run is inside plain `dotnet test`
  (~526 golden becomes ~1 050, and every site takes the mode as a parameter), or route them through one
  helper an environment variable switches and run `dotnet test` a second time (cheap, but the gate leaves
  the default run). The first is preferred — a gate outside the default command is a gate that stops
  being run — but it is real work and it is not free.
- **A timed assertion in the unit-test suite can flake, and a flaky gate gets ignored.** That is the real
  cost of layer 5 (c), and it is bought down three ways rather than waved off: a **2× floor against 11.6×
  measured**, **best-of-N minima** rather than means, and **two deterministic companions** that catch every
  code regression on their own. The rule that keeps it honest: **if the timed test ever fails
  intermittently, it gets a trait and a documented opt-out — it does not get its threshold lowered**,
  because a floor that moves to match the code is not a gate. The read-count and allocation tests never
  leave the default run.
- **`PERF10K` is not in the page cache on a fresh machine**, so the first pass of the timed test pays real
  disk. Warm-up covers it — but it means the warm-up is load-bearing, not decoration, and a warm-up
  shortened later would show up as a mysterious 2× failure on cold runs only.
- **The benchmark measures the runtime instead of the code.** This audit already made that mistake and
  reported a figure 4× wrong (`ANALYSIS.md` §8). BenchmarkDotNet is chosen for exactly this reason;
  the standing check is that a variant's number must not move when only its *position* moves.
- **Sub-step 2 looks free and is not.** `EntryAt`'s copy is load-bearing for `IndexGoldenTests` (it
  accumulates 3 364 keys across cursor moves). If a later hand reaches for "and now make `EntryAt`
  return a span", that gate breaks silently — hence the mutation check above and the note in
  `ANALYSIS.md` §6.
- **The cache's numbers assume the working set fits.** 11.6× and 23.4× were measured on a 915 KB working
  set with nothing reaching the disk. On a table larger than the pool the answer is different and this
  step measures nothing about it — `SUMMARY.md` must say so plainly.
