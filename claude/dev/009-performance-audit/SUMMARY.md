# 009-performance-audit — summary

**Closed 2026-08-19.** The first measurement of anything in this port. Filed under a step number
because it settles four of the performance pass's named suspects and reorders the rest — but it opened
no capability, `PORTING-PLAN.md` §5 gained no row, and **not one line of `net/` changed**.

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

## What changed in the repository

Nothing in `net/`. Only this folder, plus `STATE.md` §3, where the performance pass is re-ordered
around finding 2, and `.gitignore`, which excludes the scratch working area the harness was developed
in (`experiments/`).

The harness was built and run in `experiments/performance-1-experiment/`, which is **gitignored** — its
610 KB table, query sets and build outputs do not belong in the repository. [`harness/`](harness/) holds
the sources alone, so the measurement can be rebuilt from what is committed. See
[`METHOD.md`](METHOD.md).
