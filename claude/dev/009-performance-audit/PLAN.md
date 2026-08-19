# 009-performance-audit — remediation plan

Phase 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). The design is [`DESIGN.md`](DESIGN.md).

**Stoppable after sub-step 3.** Sub-steps 1–3 are CPU work with no design risk and nothing new in the
public surface; the cache is 4–6 and carries the ADR. If the step has to end early, it ends after 3 with
a `SUMMARY.md` saying the cache is still owed.

## Tests

Four layers, each naming what only it can catch (`DEV_APPROACH.md` §4).

| Layer | What goes here | What only this layer catches |
|---|---|---|
| **1. Unit** | `BlockResidency` driven exhaustively: which block retires for a given set of slots, priorities and access order; that an index block is never chosen while a data block is available; that a pool of one still makes progress. The three-valued policy resolved against each access mode — `Off` never caches, `Always` always does, `WhenExclusive` caches only a source that reports itself exclusive — which is `f4opt.c:492-501` as a truth table. `KeySearch.Compare` over prefix and padded values, including a value one byte short of the key and an all-pad value. | Every branch of the eviction rule and of the policy, at memory speed, with no file and no clock. A wrong victim is the one way this step can corrupt a read, and it is provable only here — as is `WhenExclusive`, which no real file can currently exercise. |
| **2. Component** | `CachingRandomAccessSource` over a **counting in-memory source**: a second read of the same block does not reach the source; a read spanning two blocks returns the same bytes as the uncached source; a read larger than the pool still returns the right bytes; an unaligned record-sized read at every offset in a block. And `LeafBlock.CompareAt` / `BranchBlock.ChildAt` agreeing with `EntryAt` on every entry of a real corpus block. | Sequencing and residency without a disk — the straddling read, the eviction under pressure, and the promise that a hit is a hit. Nothing below it sees two calls; nothing above it can tell a hit from a miss. |
| **3. Fault injection** | Moq on the wrapped source: `IOException` mid-fetch leaves no half-filled block resident; a short read is not admitted as a full block; the exception propagates unchanged. Hand-built corrupt blocks are already covered by existing tests and are not re-done here. | That a failed fetch cannot leave the pool holding wrong bytes — the corpus cannot express a failing disk. |
| **4. Golden / corpus** | The **whole existing golden suite, unchanged**, run twice: once uncached and once with the cache enabled through the engine's internal constructor. Both must produce identical results. | Whether the cache changes what a real file decodes to. This is the gate. |

**The parameterised golden run is the heart of it.** Every corpus assertion the port already makes —
3 364 keys, 22 tags, every field of every record of eleven tables — becomes a cache test for free by
running it a second time through a cached factory. A cache that serves a stale or neighbouring block
fails hundreds of assertions, and no new expectation has to be written for any of it.

**No new expected values are invented.** Sub-step 2's `CompareAt` is asserted against `EntryAt` on real
corpus blocks (a round-trip invariant, allowed by §4); the cache is asserted against the uncached source
(the same); and the benchmark asserts nothing.

## Steps

| # | Sub-step | Ends in |
|---|---|---|
| **1** | **A benchmark project with a number in it.** `net/benchmarks/CodeBase.Net.Benchmarks` on BenchmarkDotNet: tag-order walk, seek storm over a character tag and a numeric one, `Go(n)`-then-`Skip(1)`. Add the `PERF10K` generator case (10 000 records, `C(20)` and `N(12,2)` tags) and **commit the table** — a benchmark case, so **no `.dump.txt` and no golden test asserts against it**. Update `FOR-DEVELOPERS.md`, `SonarQube.Analysis.xml`, `net/corpus/README.md`, `test-files-generator/README.md`. | `dotnet run -c Release` in the benchmark project prints a baseline. Nothing in `dotnet test` changed. |
| **2** | **Compare in place.** `LeafBlock.CompareAt`, `BranchBlock.ChildAt`, the span compare in `BranchBlock.Seek`; the four call sites moved over. `EntryAt` untouched. | Suite green; layer-2 tests show `CompareAt` agrees with `EntryAt` everywhere; benchmark shows the descent's CPU down and allocation down by ~1 300 bytes/seek. |
| **3** | **The padded comparison — measure, then decide.** Add a short-value query set to the benchmark so the `ComparesPadded` branch is exercised at all. If it is material, build the comparand to key width once per search and use `SequenceCompareTo`. If it is not, **say so in `SUMMARY.md` and leave the code alone.** | A number for the scalar path, and either a fix with the suite green or a written "measured, left alone". |
| **4** | **`BlockResidency` and `BlockPool`, with no source attached.** The pure eviction rule and the residency store, driven entirely by layer-1 and layer-2 tests. Nothing wired into the engine yet. | Exhaustive unit tests green. No behaviour change anywhere, because nothing calls it. |
| **5** | **Wire it up, optional, with the reference's policy, plus `DropCachedBlocks()`.** `CachingRandomAccessSource`, `CachingSourceFactory`, `BlockCacheMode` (`Off` / `WhenExclusive` / `Always`) and the engine properties; the per-file decision made at open, as `file4optimizeLow` does; `DropCachedBlocks()` on the engine and on `Table`, porting `code4optSuspend` / `d4freeBlocks`. Run the whole golden suite a second time with `Always`. | Suite green **both ways**; `WhenExclusive` caches nothing today and is proved to by a fake source that claims to be exclusive; a write behind the cache's back is seen after `DropCachedBlocks()` and not before it; benchmark shows the cache's numbers; ADR entries written. |

| **6** | **`Table.Refresh()`, and the hooks `LOCKING` will call.** `d4refresh` semantics without the lock branch: reset memo state, drop this table's cached blocks, re-read the current record in place. The cache's `Invalidate(source)` / `Flush(source)` entry points exist as such rather than as private helpers, so §3.7's "flush before unlock, invalidate after lock" has something to call. | Suite green; a layer-2 test writes behind the cache's back and proves the change is invisible before `Refresh()` and visible after; `Refresh()` leaves the cursor on the same record. |

Sub-step 4 before 5 is deliberate: the part that can be *wrong* gets built and proved with no file
anywhere near it, and only then does it start serving real reads.

## Gate

**One mechanical gate, and it is the existing one:**

- `dotnet test` green — **1177 tests** as the floor, plus whatever these sub-steps add — with **no golden
  expectation changed and no corpus file altered**. Same gate shape as the 002–005 remediation
  ([`../007-audit-glm/PLAN.md`](../007-audit-glm/PLAN.md)), for the same reason: none of this may change
  what a well-formed file decodes to.
- **The whole golden suite passes with the cache on as well as off**, from the same expectations.
- The benchmark reports before-and-after for each sub-step, and those numbers go in `SUMMARY.md`. A
  sub-step that cannot show its improvement did not land.

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
