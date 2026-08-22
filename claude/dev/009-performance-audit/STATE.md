# 009-performance-audit — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** in progress — **sub-steps 1 to 8 are done** and [`SUMMARY.md`](SUMMARY.md) now covers both
halves of the step. What is left is **deciding the ADR candidates** `DESIGN.md` marks; ADR-35 is the only
one written.
**Current sub-step:** none.

## FIRST THING NEXT SESSION — decide these eight, then close the step

Nothing else is owed before 009 closes. The candidates are marked in
[`DESIGN.md`](DESIGN.md) "Decisions this design makes" and its Open questions; `PLAN.md` step 5 promised
them and only **ADR-35** was written. The recommendation column is a proposal to accept, reject or
amend — **not** a decision already taken.

| # | Candidate | `DESIGN.md` | Recommended |
|---|---|---|---|
| 1 | **The cache decorates `IRandomAccessSource`, not `NodeReader`** — a walk's reads are 96% DBF, so an index-only cache misses the whole win, and it is where the C caches (`file4readInternal`, below both layers) | decision 1 | **write** — load-bearing, cited, and the rejected alternative is the one a reader will re-propose |
| 2 | **Optional, on the reference's three-valued policy** — `Off`/`WhenExclusive`/`Always`, default `WhenExclusive`, which caches nothing today and starts by itself when `LOCKING` adds deny modes (`f4opt.c:492-501`) | decision 2, "the important one" | **write** — a *safety* decision (risk R6), it explains a default that looks like a bug, and `LOCKING` needs the reasoning |
| 3 | **The invalidation surface ships with the cache** — `DropCachedBlocks()`, `Table.Refresh()`, `Invalidate` at four levels, `Flush` deliberately unbuilt. **Include what sub-step 6 found:** blocks live in *two* places, the pool and a parked cursor's own copy, which **contradicts `ANALYSIS.md` §9** | decision 3 | **write** — a contradiction of a document needs a durable home |
| 4 | **The pool is engine-wide, not per file** — one budget across a table's data, index and memo, as `CODE4` does | decision 4 | **fold into 1 or 2** — real but one sentence, not an entry |
| 5 | **Eviction is a pure class with the C's priority order** — `other`, `dataLow`, `indexLow`, `dataHigh`, `indexHigh` (`D4OPT.C:324-328`); the order *is* the property, since a table scan cannot evict the index tree | decision 5 | **write, small** — citable, and the second `IBlockResidency` is still planned, so an ADR is where that comparison lands |
| 6 | **The 2× speed-up gate** — the first performance gate in the project and the precedent `QUERY` reuses. **Amend by what happened:** the seek's timed assertion was retargeted to *read counts* because 2.13× sat inside its own noise, and on ext4 the seek ratio was already **1.80×, under the floor** (`results-before-closing.txt` §4) | decision 8 | **write, amended** |
| 7 | **Rent-and-return, and the window as the residency unit** — oversized buffers become admissible so `ArrayPool` re-enters; eviction retires a window and every slice must be returned first | Open questions | **defer** — it describes the zero-copy design that **was not built**. Writing it now documents a plan, not a decision. It belongs with the pinned-window change |
| 8 | **The threading contract, the detector, and the library's first lock** — the contract in the public XML docs; an interlocked claim per `Table` entry point that is *a smoke alarm, not a fire door*; and the lock inside `BlockCache`, because the engine's pool is shared and two tables of one engine in parallel is ordinary. That race was real — removing the lock fails `SharedCacheConcurrencyTests` in 462 ms | Open questions | **write — the second most important**, or "not protection" gets read as protection |

**Not ADRs, deliberately:** `EntryAt` keeping its copy (a local shape decision, `ANALYSIS.md` §6, and
`DESIGN.md` decision 6 already says so), BenchmarkDotNet as the instrument (decision 7), and sub-step 3's
padded-comparison fix.

**One correction to fold into whichever entry covers the pool.** Earlier in this step I wrote that
rent/return needs no refcount because "the owner is exactly one". **Wrong:** two cursors on one tag can
sit on the same block, so **pins are counted** — small, bounded by parked readers, but a count.

## Done so far

The audit itself:

- [x] measured the read path — [`SUMMARY.md`](SUMMARY.md), [`METHOD.md`](METHOD.md),
      [`results.txt`](results.txt)
- [x] decomposed the 14× and priced a cache for this port — [`ANALYSIS.md`](ANALYSIS.md),
      [`results-cpu-analysis.txt`](results-cpu-analysis.txt)
- [x] designed and planned the remediation — [`DESIGN.md`](DESIGN.md), [`PLAN.md`](PLAN.md)

The remediation:

- [x] 1 — benchmark project + `PERF10K` generator case, committed as **benchmark data, not a gate** —
      baseline in [`results-benchmark-baseline.txt`](results-benchmark-baseline.txt)
- [x] 2 — compare in place (`LeafBlock.CompareAt`, `BranchBlock.ChildAt`, span compare in `Seek`),
      with the layer-5 allocation ceiling as its gate —
      [`results-compare-in-place.txt`](results-compare-in-place.txt)
- [x] 3 — the padded comparison: measured at 2.34×, and fixed —
      [`results-padded-comparison.txt`](results-padded-comparison.txt)
- [x] 4 — `Block`, `BlockPriority`, `IBlockResidency`, `BlockResidency`, `BlockPool` — proved with no
      source attached — [`results-residency-and-pool.txt`](results-residency-and-pool.txt)
- [x] 5 — the cache wired up: `BlockCacheMode`, `BlockCache`, `CachingRandomAccessSource`,
      `CachingSourceFactory`, `DropCachedBlocks()`, and the **layer-5 2× gate** —
      [`results-block-cache.txt`](results-block-cache.txt)
- [x] 6 — `Table.Refresh()`, `Table.DropCachedBlocks()` and the `Invalidate` hooks §3.7 will call —
      [`results-refresh.txt`](results-refresh.txt). `Flush` waits for `WRITE`, because nothing is dirty
      yet

- [x] 7a — the lock inside shared residency, which closed a real race — and 7b, the threading contract
      with its detector — [`results-concurrency.txt`](results-concurrency.txt)
- [x] 8 — **the `Length` syscall: the 730 ns explained, and it was never the cache** — ADR-35,
      [`results-length-syscall.txt`](results-length-syscall.txt)

**Stoppable after 3** with all the CPU work done and the cache still owed — **and that is where we
are.**

### Sub-step 1, done 2026-08-21

`net/benchmarks/CodeBase.Net.Benchmarks` is in the solution on BenchmarkDotNet, and
`net/corpus/benchmark/PERF10K.DBF` + `.cdx` (610 456 + 304 640 bytes) are generated and published.
`dotnet test` is green at **1179 tests, 528 golden** — up exactly the two new layout tests, with no
golden expectation touched and the rest of the corpus regenerating **byte-identical**.

The baseline, run natively on Windows because that is where the audit's figures were taken:

| operation | benchmark | audit | allocated |
|---|---|---|---|
| tag walk, per record | **1.148 µs** | 1.05 µs | 129 B |
| seek, `C(20)`, 4 levels | **9.238 µs** | 7.84 µs | **3 956 B** |
| seek, `N(12,2)`, 3 levels | **7.662 µs** | 7.22 µs | **3 808 B** |
| `Go(n)` then `Skip(1)`, per pair | **1.913 µs** | *not taken* | 0 B |

Within run-to-run variation of the audit's own numbers, which is what makes it a baseline rather than
a new measurement. **The allocation column is new and is what sub-step 2 aims at**: `ANALYSIS.md` §6
attributes 1 292–1 744 B of the ~3 900 B per seek to the key copied per comparison, so the layer-5
ceiling has a concrete number to be set below.

### Sub-step 2, done 2026-08-21

**The copies are gone and the allocation is gated. The wall clock did not move, and that is the
finding.** Full numbers in [`results-compare-in-place.txt`](results-compare-in-place.txt).

| | before | after | |
|---|---|---|---|
| seek `C(20)`, end to end | 9.238 µs / 3 956 B | 9.189 µs / **2 808 B** | −29% allocated |
| seek `N(12,2)`, end to end | 7.662 µs / 3 808 B | 7.414 µs / **2 160 B** | −43% allocated |
| leaf scan, the descent alone | 211.5 ns / 576 B | 148.0 ns / **0 B** | **1.43×** |
| branch search + child | 15.2 ns / 96 B | 6.2 ns / **0 B** | **2.45×** |

`dotnet test` green at **1197 tests** (669 + 528), up 18: fifteen `CompareInPlaceTests` — `CompareAt`
and `ChildAt` against `EntryAt` on every entry of every block of every tag of the five indexed corpus
files — and three `SeekAllocationTests`, the ceilings plus a guard that every measured seek really
finds its record. No golden expectation changed.

**The 1.7–1.9× claim was run down rather than reinterpreted**, because the first write-up of this
sub-step explained the shortfall away and that was not good enough. `results-compare-in-place.txt` §6
has it in full. The short version, all on today's build:

| | copying | the audit's hand-inlined loop | **the library** |
|---|---|---|---|
| T_NAME, RAM-backed descent | 664.4 ns | 460.2 ns (**1.44×**) | 531.7 ns (1.25×) |
| T_AMT, RAM-backed descent | 924.1 ns | 517.2 ns (**1.79×**) | 683.6 ns (1.35×) |

**The audit reproduces** — its copying arm to 3–4%, its in-place arm to 3–9%, and its *allocation* to
1–3% in every arm. It measured what it said it measured. What does not reproduce is reaching its
number through the library's structure: identical allocation, so the same work, but `Build` is a call
per compared entry and carries two corruption checks the harness omitted and the library must keep.
That is ~3.3 ns an entry, which the numeric tag pays fifty times a seek.

**Inlining the scan did not close it** — 1.14×/1.34× before, 1.15×/1.32× after — **so that change was
reverted.** It bought nothing measurable and made `Seek` drive `builtIndex` directly, which is more
coupling for no return. The remaining gap is the call itself. Closing it means duplicating `Build` inside `Seek` or moving its throws out of
line, and **that is left undone on purpose**: 133 ns is 1.4% of a file-backed seek and 20% of a
RAM-backed descent, and the residual is worth the same order again *in the same cached world*. It gets
revisited after the cache exists and can be measured where it matters.

**And the elephant is corroborated without borrowing the audit's numbers**: the same descent costs
**9 189 ns through a file and ~530 ns from RAM — ~17×**, all of it I/O. That is what P1 converts one
into the other, and it is the same fact as `SUMMARY.md`'s 11.6× and 23.4×, measured a different way.
P2 was never that and is not that.

**One more copy, one level up.** `TagCursor` compared `search.Matches(Current.Key)` at five sites, and
`Current` copies the key to do it — the same compare-and-discard, a level higher. Now `MatchesHere`,
through `CompareAt`: allocation per seek 2 808 → **2 760 B** and 2 160 → **2 128 B**, exactly one
`byte[20]` and one `byte[8]`. No measurable time change; kept for the allocation and because it is the
same defect. `ANALYSIS.md` §6's call-site table marks `TagCursor.Current` as "keeps the key: yes" —
true of the caller outside, and it missed the cursor's own five compare-and-discard uses.

**Both mutations were proved and both were localised as predicted.** `CompareAt` comparing at
`DupCount` reddens the equivalence test, 52 goldens and the find-guard; `ChildAt` reading the record
number reddens the equivalence test, 36 seek/cursor tests and 53 goldens. Restored from checksummed
copies, never from `git` (`DEV_APPROACH.md` §4).

### Sub-step 3, done 2026-08-21

**It mattered, and the fix was cheaper than the plan expected.** Full numbers in
[`results-padded-comparison.txt`](results-padded-comparison.txt).

| rule, 20 bytes, per comparison | ns |
|---|---|
| prefix, `SequenceCompareTo` | 2.592 |
| **padded, scalar loop (was)** | **6.052** — 2.34× |
| **padded, the library now** | **2.770** |
| padded, hand-written floor | 2.389 |

3.28 ns saved per comparison, ~21 comparisons per character seek, so **~69 ns a seek** — ~13% of a
RAM-backed descent and 0.8% of a file-backed one, the same shape as sub-step 2.

**The plan proposed the wrong fix and `DESIGN.md` had already spotted why it was awkward**: "build the
comparand to key width once per search" runs into `Into` *borrowing* the caller's buffer, so writing
pad into it needs a widened contract or a buffer the search owns. None of that is needed — **the buffer
already holds the padded comparand.** `SearchFor` fills it (`TableTagCursor.cs:89-92`) and `Into` finds
the pad by *stripping* it, so every byte from `Length` to the original width is exactly the pad the
loop was synthesizing. Remembering the original width is the whole change: one `int` field, no write,
no allocation, no contract change. The narrow cases keep the loop and need it.

**Mutation:** comparing only the content bytes reddens 6 seek goldens and 2 `KeySearchTests`. Worth
separating two things — **the corpus does test this rule** (those goldens are landings checked against
the C library); what was missing was a *measurement*, not a test.

### Sub-step 4, done 2026-08-21

**Nothing is wired up, by design.** What landed is the part that can be *wrong* rather than slow:
`IO/Block.cs`, `BlockPriority`, `IBlockResidency`, `BlockResidency` (five intrusive LRU lists, O(1) per
operation) and `BlockPool` (buffers of one size, allocated once, claim/release). Details in
[`results-residency-and-pool.txt`](results-residency-and-pool.txt).

**The reference's order is taken verbatim and it is citable**: `other`, `dataLow`, `indexLow`,
`dataHigh`, `indexHigh` — `prio[0]`…`prio[4]` at **`D4OPT.C:324-328`**, found only after searching
case-insensitively, which is the mixed-case-filename trap `CLAUDE.md` warns about. That order *is* the
property: a table scan cannot evict the index tree.

**The heuristics are deferred behind `IBlockResidency`, not written off** — availability marking with
its priority counting system, list normalisation by average length, hit-count and access-time scoring,
dbfLo→dbfHi promotion. They change which block is given up, never what a read returns; and since speed
is the only reason this step exists, "probably not worth it" is not a finding. The second
implementation is planned and the two get measured against each other. **No factory**: with one
implementation that is the speculative half, and the constructor already takes the interface.

**A correction to something said earlier in this step.** I claimed rent/return needs no refcount
because "the owner is exactly one". Wrong: two cursors on one tag can sit on the same block, so **pins
are counted**. Small, bounded by parked readers, and not a general refcounting scheme — but it is a
count.

`dotnet test` green at **1246 tests** (718 + 528), up 39. Two of them are attacks rather than cases: a
**20 000-operation randomised run** at a fixed seed checking the invariants after every operation, and
`ServingBlocksAllocatesNothing`, which requires 1000 claim/touch/release cycles to allocate **exactly
zero** bytes.

**Mutations, both localised as the plan predicted:** reversing the priority scan reddens exactly 3
tests; ignoring pins reddens 7, including the randomised run, which found it unaided. **Both left the
golden suite green** — which is the plan's own prediction for a sub-step that wires nothing up, and
means these layer-1 tests are the only thing standing behind this code until sub-step 5.

### Sub-step 8, done 2026-08-22

**The 730 ns was four metadata syscalls per descent, and the block cache was innocent.**
`NodeReader.ReadAt` bounds-checked every block read against `source.Length` — a live
`RandomAccess.GetLength` that `CachingRandomAccessSource` forwards straight to the file, because the
cache caches *reads*. Full account in [`results-length-syscall.txt`](results-length-syscall.txt); the
decision and the two rejected alternatives in **ADR-35**.

| BenchmarkDotNet, same session, Ryzen 9 9950X | before | after | |
|---|---|---|---|
| seek `C(20)`, cache `Off` | 9 024.2 ns | **5 636.8 ns** | 1.60× |
| seek `C(20)`, cache `Always` | 3 885.4 ns | **731.9 ns** | **5.31×** |
| index descent alone (`CodeBase.Net.Profiling`) | 3 927 ns | **445 ns** | **8.8×** |
| `Go`+`Skip`, `Off` — the control | 2 004.0 ns | 1 980.6 ns | unchanged ✔ |

**The cache's hit path costs 14 ns per window**, not 730 — a cached descent now sits 57 ns above the
no-syscall floor. **The audit's predicted 11.6× is met**: 12.8× against sub-step 5's uncached 9 386 ns,
7.7× against today's. It had stood at 2.13× since sub-step 5.

**The uncached path gained 1.60× too**, which is why a cached `Length` was rejected — it would have
helped only `Always`, and today's default caches nothing. ADR-35 has the staleness argument as well.

**No test changed**, which is the evidence the guarantee is unchanged; the mutation that removes the
translating `catch` reddens **four** tests, up from one, because the short-read path is now the only
path. **1332 tests** green (766 + 566), up 1 — an index-node short read, which nothing covered.

**What found it was counting, not timing.** `CodeBase.Net.Profiling` is a new single-process driver
(BenchmarkDotNet runs each benchmark in a child process, so a profiler attached to it watches the
harness) and it prints what no benchmark printed: reads, **`Length` queries** and cache hits per
operation. `4.000` `Length` queries and `0.000` reads per descent named the mechanism in one run. VS
instrumentation confirmed the counts and was useless for the time, inflating the run 10× with the bias
falling on call-heavy code — `ReadOnlySpan.get_Item` reached its top-functions list.

**And the C library now has the control the audit skipped.** `harness/perf.cpp` has a `go-skip`
scenario, so "how much slower is a seek than a plain positioning" has a reference answer:

| seek ÷ go | C | port |
|---|---|---|
| cached | 5.84× | **5.41×** |
| uncached | 2.29× | 2.85× |

**~5.4× is the shape of the operation, not a port cost** — before this fix the port's ratio was 51×.
Standing against the C: **~1.85× on CPU-bound work** (seek 1.84×, `Go`+`Skip` 1.99×, walk 1.83× —
consistent across three unrelated operations), and **parity or better when the work is I/O** (uncached
seek 1.01×, `Go`+`Skip` 0.81×, walk 0.47×). This **supersedes the audit's "port cached" column**, which
came from a `byte[]` standing in for a perfect cache rather than from `BlockCache`.

**One thing left unexplained on purpose.** Cached `Go`+`Skip` measured 117.5 ns before and 135.4 ns
after, same session, tiny error bars, with the uncached control unmoved — and that arm never calls
`NodeReader`. §6 of the results file has it. This step has twice explained a shortfall away and been
wrong; this one is recorded open.

## Still open

### 1. The item sub-step 8 promotes

All three `Length` sites are fixed. `NodeReader.BlockCount` and `MemoReader` landed the same day —
[`results-length-syscall.txt`](results-length-syscall.txt) §7:

| | before | after | |
|---|---|---|---|
| tag walk, per record | 156 ns / 0.043 `Length` | **111 ns / 0.000** | 1.41× |
| **memo read** | 3 329 ns / 3.612 `Length` | **343 ns / 0.000** | **9.7×** |

**The memo path was 90% metadata syscall** and nobody knew, because nothing had ever measured it. The
driver's `memo` arm was written for this.

**1333 tests** green, up one: nothing covered a payload claim in the band the new threshold owns, and
the first mutation attempted — an off-by-one on the allocation cap — reddened nothing because the other
clause caught it. The test that replaced it asserts on **bytes allocated**, since both routes refuse
with the same sentence and what the check avoids is the allocation itself. Raising the threshold so the
file is never asked reddens it with 268 535 416 bytes against a 1 MB ceiling.

Two things worth carrying: the walk's cycle bound **stays the file's own block count** — only *when* it
is asked moved, deferred past a 64-block run — and the memo **payload** check stays, because its length
comes out of the file and sizes an allocation, so only claims under 64 KB are left to the read.

**And so:** the `byte[512]` allocated per block read (`IO/SourceReader.cs:33`)
was ~1.5% of a 3 927 ns cached descent and is ~10–20% of a 445 ns one. Handing out the cache's pinned
window instead of copying into a fresh array removes that allocation *and* the 14 ns/window copy, and
it is now the largest remaining item after the leaf scan — which is itself ~148 ns of the 388 ns code
floor, and is the bit-unpacking, so ~2× the C is where it should be.


### 2. Data for stressing the cache

Yes — the point is to make **eviction actually happen**, because nothing measured so far has evicted
anything: PERF10K is ~1 788 windows and the pool defaults to 2 048, so every figure in this step was
taken with the whole working set resident. The policy — five LRU lists in the reference's priority
order — has therefore never been exercised, and there is no evidence for or against it.

Two ways, and they are not equivalent:

- **Shrink the pool.** Free, already wired: `CODEBASE_BENCHMARK_CACHE_BLOCKS` on
  `ReadPathBenchmarks`. It makes eviction happen, but with a small pool over a small file, so the
  hot-to-cold ratio is not a real one.
- **A workload larger than a realistic pool** — a new generator case. Honest, and it costs a Windows
  generator run and committed data.

**Done, 2026-08-21** — no new API was needed, `BlockCacheBlocks` has been public since sub-step 5. Full
table in [`results-before-closing.txt`](results-before-closing.txt) §3. The headline:

| | 2048 (fits) | 512 | 128 | `Off` |
|---|---|---|---|---|
| tag walk, per record | 160 ns | **1 151** | **1 334** | **1 074** |
| `Go`+`Skip`, per pair | 104 ns | 845 | 1 330 | 1 901 |
| seek `C(20)` | 4 100 ns | 5 456 | 5 658 | 8 458 |

**Under pressure a walk is worse than no cache at all.** A walk touches every record once in tag order,
which is the cache-hostile shape by definition: it pays for bookkeeping and eviction and still reads.
Seeks keep gaining, at 1.55× and 1.49× instead of 2.06×.

That is **not** a verdict on the policy — no recency policy survives a pure scan and the reference's
heuristics would not either. The priority split protects the index *from* a scan; nothing protects a
scan from itself. It points at a **missing mechanism**: scan resistance, and read-ahead for sequential
runs (`opt4fileReadSpBuffer`, which `ANALYSIS.md` §1 noted and this port did not take). Judging simple
against faithful still needs the second implementation and a workload larger than a realistic pool.

## Notes for the next session

- **Start at `PLAN.md` step 1.** Read [`DESIGN.md`](DESIGN.md) first; the two decisions that shape
  everything are that the cache decorates `IRandomAccessSource` (not `NodeReader`, because a walk's
  reads are 96% DBF) and that it is **optional on the reference's own three-valued policy**, defaulting
  to `WhenExclusive`.
- **Run the benchmark on Windows, not from WSL.** Same files, same code, 2026-08-21: **194 µs** per
  walked record and **1.58 ms** per seek through `/mnt/d`, against **9.2 µs** per seek natively. That is
  the filesystem bridge and none of it is the code — which is *why* layer 5 asserts a ratio and never an
  absolute figure. `FOR-DEVELOPERS.md` has the `cmd.exe` one-liner.
- **The measurement harness already exists** and is committed under [`harness/`](harness/);
  [`METHOD.md`](METHOD.md) has the command to lay it back out into the gitignored
  `experiments/performance-1-experiment/`. Sub-step 1 lifted `PERF10K`'s field and tag shapes from its
  `perf.cpp gen` — deliberately, so the numbers stay comparable.
- **Warm up by wall clock, never by pass count.** This audit's own first RAM-backed measurement was 4×
  wrong because five passes never gave the JIT time to reach tier-1 ([`ANALYSIS.md`](ANALYSIS.md) §8).
  BenchmarkDotNet is chosen for exactly this reason. The standing check: a figure that moves when only
  its *position* moves is measuring the runtime, not the code.
- **`EntryAt`'s copy is load-bearing** — `IndexGoldenTests.cs:88` accumulates 3 364 keys across cursor
  moves. Sub-step 2 is additive and must leave `EntryAt` alone.
- **The speed-up is gated, at 2×** — `PLAN.md` §Tests layer 5, added 2026-08-21 at the user's request.
  Three assertions on `PERF10K` with the cache `Off` against `Always`: reads down ≥ 2× and allocation
  under a ceiling (both exact, no clock), and wall clock down ≥ 2× (best-of-N minima, both orderings).
  The reason it exists: **every other gate in this step passes on a cache that is perfectly correct and
  never hits**, and none of them notices if syscalls get cheap enough that the optimization stops paying.
  If the timed one flakes it gets a trait, **never a lower threshold**.
- **Nothing here has an ADR yet.** `DESIGN.md` marks the candidates; `PLAN.md` step 5 writes them. If
  the design survives execution unchanged they are transcription; if it does not, they record what
  actually happened, which is the point.

## Deviations from the plan

**1. The benchmark table is `net/corpus/benchmark/`, not `net/corpus/`.** The plan said to commit
`PERF10K` into the corpus. It cannot go in the corpus *root*: `Corpus.TableNames` discovers tables by
enumerating that directory and **twelve golden classes iterate what it finds**, with
`CorpusLayoutTests.Corpus_HoldsExactlyTheDocumentedTables` asserting the exact set and
`Corpus_PairsEveryTableWithADump` requiring a dump for each. A dumpless benchmark table there breaks
both. `TableNamesIn` is not recursive, so a subdirectory is invisible to discovery; the benchmark
reaches it through the new `Corpus.BenchmarkPathOf`, and two new layout tests pin the arrangement —
the files exist, and the dump does not.

**2. `casePerf10k` needed a dumpless tail.** `finish()` is close + freeze-date-stamp + write-dump;
the benchmark case wants the first two only, so `util.cpp` now has `finishUndumped()` and `finish()`
is written in terms of it. No other case changes.

**3. The benchmark host registers an encoding provider.** `PERF10K` names no code page and the
library reads an unmarked table as cp437 (the C library's own default), which no runtime supplies
unregistered — so `GetString`, which is how the query sets are built, threw at `[GlobalSetup]`. The
benchmark is a host application, so it does the host's part (ADR-17), through a `[ModuleInitializer]`
rather than `Main`: BenchmarkDotNet runs each benchmark in a **child process** whose entry point is
its own generated code, so this assembly's `Main` never executes there.

**4. `SonarQube.Analysis.xml` needed no change.** The plan listed it. `**/*.Benchmarks/**` is already
in `sonar.coverage.exclusions` and `net/corpus/**` already covers the new subdirectory.

**5. Sub-step 2 needed a second benchmark class, and the benchmark needed the library's internals.**
The plan's gate is "benchmark shows the descent's CPU down", and the end-to-end seek could not show it
— ~60% of a file-backed seek is read syscalls, so the whole descent is a small slice of the rest.
`DescentBenchmarks` measures the same search over the same real block through both paths, which is
where 1.43× and 2.45× are visible. It needs `LeafBlock` and `KeySearch`, so `CodeBase.Net` and
`CodeBase.Net.TestUtils` now name `CodeBase.Net.Benchmarks` in `InternalsVisibleTo` — which
`DESIGN.md` §Seams already assumed when it said the benchmark would inject a cached factory through
the engine's internal constructor.

**6. The allocation test asks for `DefaultEncoding` rather than registering a provider.** `PERF10K`
names no code page, an unmarked table is read as cp437, and `CodeBase.Net.Tests` registers no encoding
provider — nor should it, since layers 1–3 include tests about what happens when one is absent. The
engine's `DefaultEncoding` is the other documented route, and the table's text is ASCII so Latin1
reproduces its bytes exactly.

**7. `DESIGN.md`'s "P2 … 1.7–1.9× of the descent" invites a wrong reading, and the measurement says
so.** It is 1.7–1.9× of the *comparison work*, which is ~1% of a file-backed seek and ~13% of a cached
one — about 90 ns either way. P2 was worth doing: it is free, it is measured, and it removes 29–43% of
the allocation on the hot path. But **it is not where the time is, and P1 is.** `results-compare-in-place.txt`
§3 has the arithmetic; the design table is left as written, because it records what the audit believed
when it was written.

### Sub-step 5, done 2026-08-21

**The cache works, and it delivers a third of what the audit predicted on a seek — for a reason that is
not the cache's.** Numbers in [`results-block-cache.txt`](results-block-cache.txt).

| | Off | **Always** | audit said |
|---|---|---|---|
| `Go(n)` then `Skip(1)`, per pair | 1 969 ns | **86 ns — 23.0×** | 23.4× ✔ |
| tag walk, per record | 1 206 ns | **162 ns — 7.4×** | 23.4× |
| seek `C(20)` | 9 386 ns | **4 400 ns — 2.13×** | 11.6× |

**Where the seek's missing 5× went.** A cached seek costs 4 400 ns and the index descent inside it is
~530 ns from RAM, so **~3.9 µs of a cached seek is CPU in the `Table` layer above the tree** — string
encoding, the search buffer, the at-or-after protocol, the record read and its decode. Nothing has ever
measured that layer. The audit priced a seek as descent plus I/O; the layer between them is the biggest
single share once the I/O is gone. `Go(n)`-then-`Skip(1)` hitting 23.0× is the control that makes this
reading solid: same I/O, almost none of that layer, lands on the audit's number.

**So P1 is real and delivered** — 7.4× to 23× wherever the work is I/O — **and the next thing worth
measuring is the `Table` layer, not the cache.**

**The gate has no clock on a seek, and that is a measurement rather than a convenience.** At 2.13× a 2×
floor would sit inside noise of its own threshold. The floor is not lowered: *what is measured* changed
instead. A seek is gated on its **read count** — exact, and unbounded because a cached seek storm
reaches the file zero times — while the clock is applied to the walk (7.4×) and `Go`+`Skip` (23×), where
the cache's win is large. The timed assertions exist for the one signal a read count cannot give: the
day syscalls get cheap enough that caching stops paying, and that is equally visible in a walk.

**Allocation did not move** — 2 792 B per seek either way. The cache removes the syscall, not the
`byte[512]` per read, which is the deferred caller-side-buffer half and **now the largest single item
left in the read path**.

**The bug the design caught before it shipped**, worth keeping because it is this step's whole risk: the
first cut kept the window map in the per-file source, so a slot retired for one file was a slot another
file still believed was its own — silently serving the wrong file's bytes. The map lives in the shared
cache, where eviction removes its key in the same breath, which is also where the reference keeps it.
`TwoFilesSharingAPoolAreNeverServedEachOthersBytes` is that case, on a pool of one slot.

**1318 tests** green (752 + 566), up 72.

### Sub-step 6, done 2026-08-21

**`Table.Refresh()` and `Table.DropCachedBlocks()` are in, and dropping blocks turned out to have two
halves.** Details in [`results-refresh.txt`](results-refresh.txt).

Blocks live in **two** places: the engine's pool, and the block a **parked tag cursor** is sitting on —
its own copy, because `NodeReader` allocates per read and the cache copies into it. Dropping only the
pool would leave an indexed walk on stale bytes while the record path read fresh ones, which is worse
than dropping neither. The C has the same two levels and `d4freeBlocks` drops both.

**That contradicts `ANALYSIS.md` §9**, which said this port would need only one invalidation point
because it "re-descends from the root every time". True when a cursor held nothing between calls; false
since `TagCursor` began keeping its block. The note is wrong, the code is right, and this is where it is
recorded.

**`Flush` is not built** — `Invalidate` exists at four levels, but nothing is dirty in a read-only
library, so `Flush` today would be a method whose body is a comment. It is the write path's first job.

**A vacuous test, caught by a mutation, and this is the part worth remembering.** The first version of
`AParkedCursorStopsBelievingItsOwnBlockToo` parked a cursor, dropped, then called `Top()` — which
re-descends from the root whatever the cursor held — and compared record numbers with a freshly opened
table. Against the mutation "drop the pool but leave the cursors alone" it reddened **nothing**. The
rewrite observes a **read** rather than a value: stepping inside a held leaf must read the index zero
times, and the same step after forgetting must read it. And only the *index* file's reads count, because
a tag-order walk visits scattered records, so the record window misses regardless.

**1325 tests** green (759 + 566).

### Sub-step 7, done 2026-08-21

**It had two halves, and the first one closed a race that existed.** Before it, the library contained
**no synchronisation of any kind**, and the engine's cache is shared by every file it opens — which the
contract "do not use a *table* concurrently" does not cover, because two tables of one engine queried in
parallel is ordinary. Removing the lock again makes `SharedCacheConcurrencyTests` fail **both** tests in
462 ms, every time: the race was real, reachable and cheap to provoke.

**What the lock costs, and how to read it.** Against the pre-lock run: `Go(n)`+`Skip(1)` went 86 → 104 ns
while *everything else got cheaper*, uncached included (seek 9 386 → 8 458). The machine was ~10% faster
in that process, and that variance swamps almost everything — so the one figure moving *against* the
trend is the signal: **~9 ns per cached window service**, an uncontended acquisition. 17% of a `Go`+`Skip`
pair, ~1% of a seek, invisible in a walk. Splitting the lock would not reduce it (what was measured is
the acquisition, not the serialisation), so it is recorded rather than optimised.

**The detector is not a lock and says so**: interlocked claim on each of `Table`'s 22 entry points,
re-entrant for the holding thread because `Refresh` calls `DropCachedBlocks`, and able to miss a racer
arriving in the same instant. A smoke alarm, not a fire door — and worth having only because of the
ground rule: a missing caller lock otherwise produces a wrong record set silently, with every individual
call behaving correctly.

The test that makes "mid-flight" a place rather than a hope: a source that **blocks inside a read**,
armed only after the table is open, so the intruding thread arrives at a known point.

**1331 tests** green (765 + 566).

## A defect found on the way, and deliberately not fixed here

**`Seek(string)` on a tag whose keys are not character does not refuse — it reinterprets.** Written up
in [`DEFECT-seek-string-on-a-non-character-tag.md`](DEFECT-seek-string-on-a-non-character-tag.md).
The serious row: on a numeric tag the string's bytes are read as a numeric field's text, unparseable
text becomes **0.0**, and the seek **silently looks for the key of zero**. No exception, a plausible
answer, the wrong key — which `CLAUDE.md` calls worse than any slowness.

Found because sub-step 5's equivalence test pointed a string seek at every tag of every corpus table
for the first time. **Present with the cache off**, so it is not the cache's. Left for its own step: it
is a behaviour change, and this step's gate is that nothing a file decodes to may change. The
equivalence test pins the current behaviour against the *cache* altering it without blessing it as
correct.

## Blockers

None. One open decision that is **not** blocking, recorded in the root `STATE.md` §3: whether
`LOCKING` moves ahead of `WRITE` in `PORTING-PLAN.md` §5. It affects the step *after* this one, not
this one.

The coupling it comes from is captured so it cannot be lost: `PORTING-PLAN.md` §5's
**cross-capability obligation** table says what `WRITE`, `LOCKING` and `TRANS` each owe the cache, and
sub-step 5 must build `Invalidate(source)` / `Flush(source)` as real entry points so `LOCKING` has
something to call.
