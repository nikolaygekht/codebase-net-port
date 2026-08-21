# 009-performance-audit — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** in progress — the audit is closed and its remediation is **under way**: sub-steps 1 to 4
of seven are done
**Current sub-step:** [`PLAN.md`](PLAN.md) step 5, wiring the cache up. Not begun.

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
- [ ] 5 — wire the cache up: `BlockCacheMode`, `CachingSourceFactory`, `DropCachedBlocks()`, and the
      **layer-5 2× speed-up gate** (reads, wall clock)
- [ ] 6 — `Table.Refresh()` and the `Invalidate` / `Flush` hooks §3.7 will call

- [ ] 7 — the threading contract in the docs, and the `Interlocked` detector that makes a violation
      throw instead of returning someone else's record (independent of 4–6)

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
