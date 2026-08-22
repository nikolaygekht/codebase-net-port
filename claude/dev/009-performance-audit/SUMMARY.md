# 009-performance-audit — summary

**The step has two halves, and this document covers both.**

**The audit closed 2026-08-19** — everything down to "What changed in the repository" is that, left as
written, because it is the record of what was known then. It opened no capability and changed not one
line of `net/`.

**The remediation ran 2026-08-21 to 2026-08-22** and is [its own section
below](#the-remediation--2026-08-21-to-2026-08-22). It changed `net/` substantially, met the audit's
headline prediction, and **overturned three of the audit's own numbers** — flagged inline where they
appear. Its status: eight sub-steps executed, ADR-35 written, the remaining ADR candidates still to be
decided.

Until now every performance property of the port was a guess: `STATE.md` §3 opened the performance
pass with "nothing has ever been measured". This replaces the guesses with numbers on the one path
that matters most, because `QUERY` will drive it far harder than a walk does.

**Method, fairness controls and how to re-run:** [`METHOD.md`](METHOD.md). Raw output:
[`results.txt`](results.txt). The harness itself: [`harness/`](harness/).

> **[`ANALYSIS.md`](ANALYSIS.md) decomposes the 14×** into the cost it removes — read syscalls, at a
> measured 1.18–1.21 µs each — and measures the same win for this port: **11.6× on a seek, 23.4× on a
> walk**. It **confirms finding 2**, closes finding 6, and qualifies finding 4. It also carries a
> retraction of its own first version, which had a harness defect; the corrected numbers are the ones
> quoted here.

## What was measured

A 10 000-record table with a `C(20)` and an `N(12,2)` tag, both written by the reference C library, and
three sets of 10 000 lookups each, run by two programs against the same file — one linked to the
original C library, one to `CodeBase.Net`.

Three configurations, because the C library's block cache is **off unless `code4optStart` is called**
(`cb->optimize`'s default `OPT4EXCLUSIVE` is a permission, not a switch):

- **`c`** — the C library with no cache, reading through the OS page cache exactly as this port does.
  **The fair comparison.**
- **`cs`** — `CodeBase.Net` on .NET 8, Release.
- **`c-opt`** — the C library's own cache on. Not a comparison; a price tag for the cache.

## Result

Best of three interleaved rounds, each the minimum of 9 timed reps after 5 warm-up passes. Per-op in
brackets.

| Scenario | `c` | `cs` | `c-opt` | cs vs c |
|---|---|---|---|---|
| **`name-hit`** 10 000 seeks, `C(20)` whole key | 57.7 ms (5.77 µs) | **82.4 ms (8.24 µs)** | 4.02 ms (0.40 µs) | **1.43× slower** |
| **`name-miss`** 10 000 seeks, absent, land on neighbour | 57.3 ms (5.73 µs) | 81.6 ms (8.16 µs) | 4.00 ms (0.40 µs) | 1.42× slower |
| **`amount-hit`** 10 000 seeks, `N(12,2)` | 51.4 ms (5.14 µs) | 69.8 ms (6.98 µs) | 7.61 ms (0.76 µs) | 1.36× slower |
| **`tag-walk`** 10 000 records in tag order | 26.1 ms (2.61 µs) | 10.7 ms (1.07 µs) | 0.80 ms (0.08 µs) | **2.45× faster** |

`min`, `med` and `mean` agree within 1% in every cell. Absolute figures drifted 4–5% between sessions
with background load; **the ratios reproduced to ±0.01, and they are what this audit claims**.

Allocation, from a separate untimed pass on the C# side:

| Scenario | bytes/op | Gen0 GCs per 10 000 ops |
|---|---|---|
| `name-hit` | 3 956 | 2–3 |
| `name-miss` | 3 957 | 2–3 |
| `amount-hit` | 3 808 | 2 |
| `tag-walk` | 129 | 0 |

**Both sides did the same work, and that is checked rather than asserted.** The query values are
*files* both programs read in the same order, and every scenario reports the sum of the `ID` field of
every record it landed on. All four checksums match to the digit across all three configurations, so
the two libraries reached the same records, in the same order, and read a field out of each.

## Findings

**1. Like-for-like, the port is within half a length: 1.36–1.43× the C's time per seek.** For a
managed port against 32-bit `/O2` C doing bit-unpacking, that is a respectable baseline. It is *not*
the thing to fix first.

> **Superseded 2026-08-22.** Part of that 1.4× was four metadata syscalls per descent, not managed-code
> cost. Uncached, the port is now at **parity** with the C on a seek (1.01×) and faster on a walk and a
> `Go`+`Skip`. Cached — where the comparison is purely CPU — it is a consistent **~1.85×**. See
> [`results-length-syscall.txt`](results-length-syscall.txt) §5 and §8.

**2. The block cache is worth 14×, and it dominates everything else on the list.** `c-opt` does the
identical work — same checksums — at 0.40 µs/seek against `c`'s 5.77 (numeric: 0.76 against 5.14, a
smaller 6.8×). The C's advantage over this port today is 1.4×; the advantage available *from caching
alone* is fourteen.

**This reorders the performance pass.** Suspect **P1 was listed first because it was likely to matter
most; it is now measured to matter more than the rest of the list combined.** It also raises the stakes
on the ADR `STATE.md` §3 already calls for — where a cache shared by several cursors over one tag
lives, and how it stays coherent — because a cache that serves a stale block is a wrong record set, and
this port's first rule is that a wrong record set is far worse than a slow one.

> **Confirmed and quantified by [`ANALYSIS.md`](ANALYSIS.md).** The 14× is entirely **read syscalls
> avoided**: the C spends 5.16 of its 5.56 µs (93%) inside them, at a measured 1.18–1.21 µs each, where a
> cache hit is a hash lookup plus a `memcpy` at 0.002 µs. **The same win is available here** — measured
> with the port's `IRandomAccessSource` swapped for one holding each file in a `byte[]`, a cache is worth
> **11.6× on a character seek, 6.9× on a numeric one, and 23.4× on a tag walk**. The walk is the number
> to keep in view: `QUERY` builds a bitmap by seeking one end of a range and *walking*, so that is the
> pattern it will spend its time in.

**3. P2 is confirmed and quantified: ~3 956 bytes allocated per seek** — 39.6 MB per 10 000-seek pass.
A seek that descends three levels and scans one leaf should allocate nothing. `tag-walk`, which does
not re-scan leaves, allocates 129 bytes/op, which brackets the cost to descent and leaf scanning.

**The audit does not attribute the 3 956 bytes**, and says so rather than guessing: the two named
suspects — `NodeReader.ReadAt`'s fresh `byte[512]` per block (P1's allocation half) and
`LeafBlock.EntryAt`'s key copy per comparison (P2) — plausibly account for all of it, but splitting it
is a profiler's job and worth doing before either fix.

> **Attributed and reduced.** Both suspects were real. The per-comparison key copy came out in sub-step
> 2 (−29% to −43%) and the discarded message string in sub-step 8 (−56 bytes per block read), taking a
> character seek from **3 956 to 2 568 bytes**, gated by `SeekAllocationTests`. The `byte[512]` per
> block read is still there and is now the largest single item left.

**4. P4 is not a problem.** "A record read per position, with no reuse" was suspect 4. Measured,
`CodeBase.Net` walks a tag **2.45× faster than the C library does**. That removes P4 from the list and
localises the seek gap to descent and leaf scanning, which is exactly where P1 and P2 live.

> **Confirmed by [`ANALYSIS.md`](ANALYSIS.md), and it survives removing I/O.** Part of the 2.45× is the
> C issuing *more* syscalls per record (2.044 against the port's 1.044), but with I/O taken out of both
> sides the port still walks **1.7× faster** (0.045 against 0.078 µs). P4 is struck from the list, and
> "the port's strong side" is fair.

**5. Gen0 collections are not what the seek timings are sensitive to.** Recorded because it was the
obvious suspect and it is wrong: re-running with `DOTNET_GCgen0size=0x20000000` drove Gen0 collections
to **zero** and moved the timings by under 1%. At this scale the 3 956 bytes/op costs allocation *work*
— zeroing, bump-pointer, cache pressure — not pause time. Under `QUERY`, holding bitmaps live, that
could change.

**6. Numeric seeks are cheaper than character seeks unbuffered, and dearer buffered.** Unbuffered,
numeric costs 0.89× the character time in C and 0.85× in C# — an 8-byte key packs more entries per
leaf. `c-opt` inverts it: 7.61 ms numeric against 4.02 ms character. **Unexplained**, and flagged
rather than guessed at; it is a fact about the reference library, not about this port.

> **Closed by [`ANALYSIS.md`](ANALYSIS.md) §7: it is entries per leaf.** An 8-byte key packs ~50 entries
> into a 512-byte leaf against ~23 for a 20-byte key, and the leaf search is a **linear scan in both
> implementations** — neither can binary-search delta-compressed keys. Measured: **49.51 comparisons per
> numeric seek against 20.92 per character seek**. More comparisons, more CPU, so numeric is dearer
> cached on both sides; uncached the ordering flips because the numeric tree has one level fewer and at
> 1.2 µs a read the syscalls swamp the comparisons.

## What this audit does *not* settle

Named rather than left to look examined:

- **P6 (`IndexFileReader.Tag(name)` linear scan) and P7 (no duplicate-count skipping) were not
  measured.** The table has two tags and no duplicate-heavy tag, so neither suspect was exercised.
  They remain open, and remain expected to be noise. *(`ANALYSIS.md` §6 later implemented P7 and
  confirmed it changes the comparison count by **nothing** on unique keys — it needs a duplicate-heavy
  tag before it can be judged at all.)*
- **This measures CPU and code path, not I/O.** 610 KB of DBF and 305 KB of CDX sit in the OS page
  cache after warm-up. `c-opt`'s 14× is therefore CPU work avoided against an *already cached* file,
  which makes it a **lower** bound for a cold or larger table, not an upper one.
- **`GENERAL` collation is untested.** The table carries no code-page mark and the tags use machine
  collation. A collated seek builds keys through the weight tables and would be slower on both sides.
- **One machine, one sitting, no isolation.** Enough to separate 1.4× from 14×; not enough to argue
  about 5%.
- **`c` is 32-bit** MSVC `/O2` (the project builds x86 deliberately — `test-files-generator/config.bat`)
  against 64-bit .NET 8. Not a controlled variable.
- **There is still no benchmark project in the solution**, and BenchmarkDotNet is still an unused entry
  in the stack list. [`harness/`](harness/) is a documented instrument, not a gate: nothing in
  `dotnet test` runs it, and nothing in `net/` depends on it.

  > **Built in sub-step 1**, as `net/benchmarks/CodeBase.Net.Benchmarks`, joined in sub-step 8 by
  > `CodeBase.Net.Profiling`. And the harness gained the control the audit never took: a `go-skip`
  > scenario, so a seek has something to be divided by.

## What changed in the repository

Nothing in `net/`. Only this folder, plus `STATE.md` §3, where the performance pass is re-ordered
around finding 2, and `.gitignore`, which excludes the scratch working area the harness was developed
in (`experiments/`).

The harness was built and run in `experiments/performance-1-experiment/`, which is **gitignored** — its
610 KB table, query sets and build outputs do not belong in the repository. [`harness/`](harness/) holds
the sources alone, so the measurement can be rebuilt from what is committed. See
[`METHOD.md`](METHOD.md).

---

# The remediation — 2026-08-21 to 2026-08-22

Eight sub-steps against [`DESIGN.md`](DESIGN.md) and [`PLAN.md`](PLAN.md). The per-sub-step account,
with the mutations and the deviations, is [`STATE.md`](STATE.md); the results files it links are the
raw numbers. What follows is what someone reading this in six months needs.

## What the port can do now that it could not

**The block cache exists**, decorating `IRandomAccessSource` so one pool serves a table's data, index
and memo alike — which is where the reference caches and the only place that catches a walk, whose reads
are 96% DBF. It is optional on the reference's own three-valued policy (`Off` / `WhenExclusive` /
`Always`, defaulting to `WhenExclusive`), and **today's default caches nothing**, because no file this
port opens excludes other writers yet. It starts caching by itself when `LOCKING` adds deny modes.

**`Table.Refresh()` and `DropCachedBlocks()` exist**, with `Invalidate` at four levels, so
`LOCKING-TRANSACTIONS.md` §3.7's "flush before unlock, invalidate after lock" has something to call.
`Flush` is deliberately unbuilt: nothing is dirty in a read-only library.

**The library has a threading contract and says so**, plus a detector that turns a missing caller lock
into an exception rather than a wrong record set. And it has its first lock — inside `BlockCache`,
because the engine's pool is shared and two tables of one engine queried in parallel is ordinary. That
race was real: removing the lock fails `SharedCacheConcurrencyTests` in 462 ms, every time.

**Performance can be measured and re-measured by anyone.** `net/benchmarks/CodeBase.Net.Benchmarks` on
BenchmarkDotNet over the committed `PERF10K`, and `CodeBase.Net.Profiling` — a single-process driver an
external profiler can watch, which prints reads, `Length` queries and cache hits per operation.

## The numbers

BenchmarkDotNet, Windows 11, Ryzen 9 9950X, .NET 8.0.30 x64, Release, over `PERF10K`. "Start" is
sub-step 1's baseline; "end" is 2026-08-22.

| per operation | start, cache `Off` | end, cache `Off` | end, cache `Always` |
|---|---|---|---|
| seek, `C(20)`, 4 levels | 9 238 ns | **5 637 ns** | **732 ns** |
| tag walk, per record | 1 148 ns | **1 134 ns** | **114 ns** |
| `Go(n)` then `Skip(1)`, per pair | 1 913 ns | **1 981 ns** | **135 ns** |
| seek, allocated | 3 956 B | **2 568 B** | 2 568 B |

**A cached seek is 12.8× the uncached seek the step started from, which meets the audit's predicted
11.6×.** It had stood at 2.13× from sub-step 5 until sub-step 8 found why.

Against the C library, on the same machine and table, with the `go-skip` control the audit never took:

| | C | port | |
|---|---|---|---|
| cached seek | 397 ns | 732 ns | 1.84× |
| cached walk, per record | 80 ns | 114 ns | 1.43× |
| cached `Go`+`Skip`, per pair | 68 ns | 135 ns | 1.99× |
| uncached seek | 5.572 µs | 5.637 µs | **1.01× — parity** |
| uncached walk, per record | 2.511 µs | 1.179 µs | **0.47× — port faster** |
| **seek ÷ its own `Go`+`Skip`, cached** | **5.84×** | **5.41×** | |

**When the work is I/O the port matches or beats the C; when it is CPU it costs a consistent ~1.85×**,
across three unrelated operations. And **a seek costs ~5.4× a plain positioning in both
implementations** — that ratio is the shape of the operation, not a port cost.

Where the ~1.85× is spent is where it should be: `LeafBlock.Seek` is ~148 ns of the 388 ns code floor,
the largest single item, and it is the bit-unpacking — a leaf cannot be binary-searched, so every key
passed is rebuilt from its duplicate prefix and trail count.

## The four things worth carrying forward

**1. The largest cost in the read path was not on anybody's list.** Every gate in `PLAN.md` passed at
sub-step 7 and the step was still wrong about where its time went — for the third time, after sub-step
2's comparison work and sub-step 5's "3.9 µs in the `Table` layer". The actual answer was a
`RandomAccess.GetLength` per block read, four per descent with the cache fully warm, 87% of a cached
descent (**ADR-35**, [`results-length-syscall.txt`](results-length-syscall.txt)). The audit's
`GetProcessIoCounters` method could not see it, because a metadata call is not a read.

**What found it was measuring counts, not time.** A gate that asserts a ratio cannot notice that the
quantity it is dividing is the wrong one. `4.000 Length queries and 0.000 reads per descent` named the
mechanism in a single run.

**2. Two of the three memo/index `Length` sites were worse than the one that was hunted.** The memo path
was **90% metadata syscall** — 3 329 → 343 ns per read — and had never been measured at all, because
there is no memo benchmark. A cached walk was ~25% the same call.

**3. The rule the fix generalises to:** *let the read be the refusal — unless the value being checked
sizes an allocation.* A fixed-size block read can fail safely; a memo payload whose length the file
supplies cannot, because the buffer is allocated before the read. That exception is why `MemoReader`
keeps a check `NodeReader` dropped.

**4. Sub-step ordering earned its keep, and so did mutation.** `BlockResidency` and `BlockPool` were
built and proved with no file anywhere near them, and the design caught the step's whole risk before it
shipped — a first cut kept the window map per file, so a slot retired for one file was a slot another
file still believed was its own. Mutation caught three vacuous or wrong tests, including one in
sub-step 8 that passed with the code under test deleted.

## What this step does not settle

- **Eviction has never been exercised by a realistic workload.** `PERF10K` is ~1 788 windows against a
  2 048-window pool, so nothing was ever evicted. Forced small pools show a **walk is worse than no
  cache at all** under pressure — the cache-hostile shape by definition. That points at a *missing*
  mechanism, scan resistance and read-ahead (`opt4fileReadSpBuffer`), not a wrong policy, and judging
  simple against faithful needs the second `IBlockResidency` and a workload larger than a real pool.
- **The `byte[512]` per block read remains**, and sub-step 8 promoted it: ~1.5% of the old cached
  descent, ~10–20% of the new one. Handing out the cache's pinned window removes the allocation *and*
  the 14 ns/window copy. Largest remaining item after the leaf scan.
- **One unexplained movement, recorded rather than smoothed over.** Cached `Go`+`Skip` went 117.5 →
  135.4 ns across sub-step 8, same session, uncached control unmoved, on an arm that never calls
  `NodeReader`. [`results-length-syscall.txt`](results-length-syscall.txt) §6. Judged not worth chasing
  now.
- **A defect found on the way and deliberately not fixed here:** `Seek(string)` on a non-character tag
  reinterprets instead of refusing, and unparseable text becomes the key of zero
  ([`DEFECT-seek-string-on-a-non-character-tag.md`](DEFECT-seek-string-on-a-non-character-tag.md)). It is
  a behaviour change and this step's gate was that nothing a file decodes to may change.
- **`P6` and `P7` are still unjudged.** P7 was implemented and changes the comparison count by nothing
  on unique keys; both need a duplicate-heavy corpus tag.
