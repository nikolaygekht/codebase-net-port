# 009-performance-audit — remediation design

Phase 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written before any `.cs` file is opened.

The audit itself is [`SUMMARY.md`](SUMMARY.md) and [`ANALYSIS.md`](ANALYSIS.md); this is what we do
about it. Filed here rather than under a new number because it remediates *this* audit, the way
[`007-audit-glm`](../007-audit-glm/) held its own remediation.

## Goal

**Make the read path fast enough for `QUERY` to drive, by caching file blocks and by not copying a key
for every comparison** — verified by a benchmark project *in the solution* that reports before and
after on a committed table, with **every existing test still green and no golden expectation or corpus
file changed**.

That last clause is the gate. This step may not alter what a well-formed file decodes to, and the
audit's own measurements are the only thing that says it worked.

## What the audit left us, and what happens to each

| Finding | Measured | Disposition |
|---|---|---|
| **P1** no block cache | **11.6×** on a seek, **23.4×** on a walk | **Item 3** — the cache, with an ADR |
| **P2** key copied per comparison | **1.7–1.9×** of the descent, 1 292–1 744 bytes/seek | **Item 1** |
| Padded comparison is a scalar loop | **unmeasured** — every query in the audit avoided it | **Item 2** — measure, then fix if it matters |
| **P4** a record read per position | port is *faster* than the C, 0.045 against 0.078 µs | **struck**, nothing to do |
| **P7** no duplicate-count skipping | implemented in the harness: changed the comparison count by **zero** | **not done** — needs a duplicate-heavy corpus tag before it can be judged |
| **P6** `IndexFileReader.Tag(name)` linear scan | not exercised (two tags) | **not done** — tens of tags against a block read; measure only |
| Root not retained per tag | ~11% uncached | **not done** — the cache makes the root a hash hit, so it subsumes this |
| `byte[512]` per block read | 2 144 of the 2 568 bytes/seek that survive P2 | **not fixed** — see Open questions |

**Three items, in the order they will be built.** Items 1 and 2 are CPU work with no design risk;
item 3 is the large one and carries the ADR.

## Why the cache goes at the source, not in the index

The obvious place is `NodeReader`, and it is the wrong one. Two facts settle it:

- **The walk's reads are almost all DBF, not index.** `tag-walk` issues **1.044 reads per record, of
  which 1.000 is the record itself** and 0.044 is a leaf. An index-only cache would leave the walk
  essentially untouched — and the walk is the 23.4×, the biggest number in the audit.
- **Both layers already read through one door.** `RecordReader` (`RecordReader.cs:59`) and
  `NodeReader` (via `SourceReader.ReadExactly`) both call `IRandomAccessSource.Read(offset, span)`.
  That is precisely where the C caches: `file4readInternal` (`f4file.c:2017-2093`) branches on
  `f4->doBuffer` **below** both the index and the DBF code, so `opt4fileRead` serves tag blocks and
  record bytes from the same pool.

So the faithful port — and the only one that gets the walk — is a **decorator on the boundary
interface**. Nothing in `Cdx/` or `Dbf/` changes to get it.

```
Table   TagCursor   NodeReader   RecordReader   MemoReader
                    ↓ all read through ↓
              IRandomAccessSource            ← the cache goes here
                    ↓
        FileRandomAccessSource → RandomAccess.Read  (the 0.849 µs syscall)
```

## Item 1 — compare in place, and keep the copy where it earns its place

`BranchBlock.EntryAt` does `entry[..keyLength].ToArray()`; `LeafBlock.EntryAt` does
`key.AsSpan().ToArray()`. Of the five call sites, **one needs an owned key** — `TagCursor.Current`,
which hands an `IndexEntry` outward — and that contract is **load-bearing**: `IndexGoldenTests.cs:88`
accumulates 3 364 keys across cursor moves, so an aliased key would break the index gate or make it
pass vacuously. `EntryAt` therefore keeps copying, and the fix is additive.

| Add | Replaces | Why it is safe |
|---|---|---|
| `LeafBlock.CompareAt(index, search)` → `int` | `search.Compare(EntryAt(i).Key)` in `LeafBlock.Seek` | rebuilds into the block's existing buffer and compares; **no span escapes** |
| `BranchBlock.ChildAt(index)` → `uint` | `EntryAt(…).Child` at `TagCursor.cs:340` and `:542` | reads four bytes; the key is never touched |
| in `BranchBlock.Seek`, compare `block.AsSpan(offset, keyLength)` | `search.Compare(EntryAt(middle).Key)` | a branch block never mutates its buffer, so the span is valid for its life |

`KeySearch.Compare` already takes a `ReadOnlySpan<byte>`, so nothing else moves and no public surface
changes. **The reason the copy was there is real and stays documented** — see `ANALYSIS.md` §6; this is
a correct decision applied at the wrong granularity, not an oversight being swept away.

## Item 2 — the padded comparison stops being a scalar loop

`KeySearch.Compare` takes `key[..Length].SequenceCompareTo(Bytes)` — vectorized — when the search value
is a prefix, and a **byte-at-a-time loop** when `ComparesPadded` (`KeySearch.cs:139-144`). Every value
in the audit filled `C(20)` exactly, so **the scalar branch was never measured**, and it is the branch a
real `Seek("SMITH")` takes: `Into` sets `comparesPadded: content < length`, which is true for any value
shorter than the key.

**Measure first.** A short-value query set over `T_NAME` says whether this matters at all; if it does,
the fix is to build the comparand to full key width **once per search** instead of branching per byte
per comparison, and then always use `SequenceCompareTo`.

Where the padding lives is a sub-step decision, not settled here: `Into` *borrows* the caller's buffer
and documents that the caller must not write to it, so having the search write pad into it needs either
a widened contract or a buffer the search owns. One allocation per search replacing twenty scalar
comparisons is an acceptable trade if it comes to that; twenty-one allocations is not.

## Item 3 — the block cache

### What it is

A hash-addressed pool of fixed-size blocks, shared by every file an engine has open, serving reads that
would otherwise be syscalls. The shape is the C's (`ANALYSIS.md` §1) with its two non-obvious parts kept:

- **The priority split.** The C keeps `dbfLo`, `dbfHi`, `indexLo`, `indexHi` and `other` as separate
  lists (`d4data.h:661-671`) so **a table scan cannot evict the index tree**. A single LRU sized to a
  working set fails exactly when `QUERY` needs it most: a full scan running beside thousands of tag
  seeks. This is copied deliberately.
- **A hit must be cheap.** The C's is a hash lookup plus a `memcpy` — 0.002 µs for 512 bytes. A hit path
  that allocated would hand most of the win straight back, which is the same defect as P2.

### Optional, with the reference's own three-valued policy

**The cache is optional, exactly as it is in the original.** Two independent gates have to open before a
single block is cached, and porting both is what keeps this safe:

1. **The pool has to exist.** `file->doBuffer` is only set when `opt->numBuffers > 0`
   (`f4opt.c:512-524`), and the pool is allocated by `code4optStart`. No call, no cache.
2. **The per-file policy has to permit it**, and it is *three*-valued, not a boolean
   (`f4opt.c:492-501`):

```c
if ( optFlag == -1 )              /* OPT4EXCLUSIVE -- the shipped default */
   optFlag = ( file->lowAccessMode != OPEN4DENY_NONE ) ? 1 : 0 ;
```

| `cb->optimize` | Means |
|---|---|
| `OPT4OFF` (0) | never cache this file |
| **`OPT4EXCLUSIVE` (-1)** | cache **only if the file was opened excluding other writers** |
| `OPT4ALL` (1) | always cache |

**`OPT4EXCLUSIVE` is a safety rule, not a convenience default.** A cached block is stale the moment
another process writes the file, and DBF files are classically shared —
`FileRandomAccessSource` already opens `FileShare.ReadWrite` because "these files are normally open
elsewhere". Caching only what we have excluded other writers from makes staleness *impossible* rather
than unlikely. That is why the reference ships that value, and it is the one to copy.

**What it gives us today, and free later.** No file this port opens is exclusive yet — an access mode is
`LOCKING`'s to introduce — so the faithful default evaluates to "cache nothing", which is exactly today's
behaviour and today's freshness guarantee. A caller who knows they are the only writer asks for
`Always` and gets the 11.6× / 23.4×. And when `LOCKING` adds deny modes, **the default starts caching by
itself**, for the files where it is provably safe, with no API change.

**The C# shape is idiomatic, not transliterated** (`PORTING-PLAN.md` §4). The two gates collapse into a
mode plus a size, and the pool is allocated on the first open that qualifies rather than by an explicit
`code4optStart`:

```csharp
public enum BlockCacheMode { Off, WhenExclusive, Always }

// on CodeBaseEngine
public BlockCacheMode BlockCache { get; set; } = BlockCacheMode.WhenExclusive;
public int BlockCacheBlocks { get; set; }        // 0 means no pool at all
```

Like the C, **the decision is made per file at open time** (`file4optimizeLow` runs from the open path),
so changing the mode affects files opened after it, not files already open — which is what the property
documents rather than a footgun to discover. A per-table override, the equivalent of
`d4optimize(data, flag)`, is an optional argument on `OpenTable` and is low priority.

### Non-exclusive access — what currency actually depends on

The obvious worry is "what does a cache do when the file is shared?", and the reference's answer is not
"detect other writers". It is that **currency has always been the caller's responsibility, expressed
through locks**, and the cache simply inherits that contract. `d4refresh` (`D4FRESH.C:130-148`) states it
in one condition:

```c
if ( lockTestFile( write ) == 1 || lockTestFile( read ) == 1 ||
     file.lowAccessMode != OPEN4DENY_NONE || opt == 0 )
   return d4go( data, data->recNum ) ;     /* trust what we have -- nothing can have changed */

if ( doBuffer ) opt->forceCurrent = 1 ;    /* otherwise force a re-read from disk on a hit */
```

So the C recognises exactly three situations in which a cached block is known good: **we hold a write
lock, we hold a read lock, or the file was not opened shared.** In any other case a caller who needs
current data asks for it, and `opt->forceCurrent` makes a *hit* re-read from disk instead of returning
what it holds (`o4opt.c:1575-1585`) — the block stays resident, only its contents are refreshed.

Three consequences for this port, and they are the honest answer to "how will the cache look with
non-exclusive access?":

1. **`Always` on a shared file can serve stale blocks — as `OPT4ALL` can.** That is not a defect being
   ported; it is the xBase contract, where a reader with no lock was never promised currency. But this
   port has **no locks at all yet**, so a caller who chooses `Always` currently has no way to get
   currency back short of closing the table. That must be what the property's documentation says.
2. **The invalidation surface is portable now, and this step ports it.** The C has two levels —
   `d4freeBlocks` (`d4index.c:29`), which drops every block a table's tags hold, and `code4optSuspend`,
   which flushes the whole pool. Neither needs locking, both are cheap, and together they make `Always`
   defensible for a long-running reader: read fast, drop, read again. Without them `Always` is a
   one-way door, which is a worse API than a slow one.
3. **`WhenExclusive` is the only setting that is safe with no locks**, which is why it is the default.
   When `LOCKING` lands, the C's rule generalises rather than changes: trust the cache while a lock is
   held or the file is non-shared, refresh otherwise. That is a `LOCKING` sub-step, not a redesign.

**What the port does not need to reproduce.** The C also keeps a per-tag path of `B4BLOCK`s above the
cache (`tfile4upToRoot`, `ANALYSIS.md` §9), which is a *second* layer that `d4freeBlocks` exists to
clear. This port re-descends from the root every time, so it has one cache and one invalidation point —
simpler, and one fewer place for staleness to hide.

### Block size, and reads that straddle

One power-of-two block size, 512 by default, aligned from offset 0 — matching CDX node alignment and
the C's `blockPower`. **DBF records are not block-aligned** (`32 + headerLen + n × recordLength`), so a
record read routinely spans two cached blocks and a tag-header read spans two as well. The decorator
therefore loops over the blocks a request touches, exactly as `opt4fileRead` does, and copies each
piece into the caller's span. A request larger than the whole pool falls through to the underlying
source rather than evicting everything.

### What it does *not* do

It does not remove the `byte[512]` that `NodeReader` allocates per block, because
`IRandomAccessSource.Read` copies into a caller-owned span by contract — as does `opt4fileRead`. Handing
out the pooled array by reference instead would avoid the copy and the allocation, but a `LeafBlock`
holds its block for its whole life and `TagCursor` holds blocks across calls, so a pooled array could be
evicted from under a live block. That needs a lifetime rule and is **out of scope**; see Open questions.

## Item 4 — `Refresh()`, and the hooks `LOCKING` will call

Added 2026-08-19 after the cache's coherency story was worked out. **`d4refresh` belongs here;
`d4lock` does not** — see "Why locking is a separate step" below.

### `Table.Refresh()`

`d4refresh` (`D4FRESH.C:130-175`) is "discard what is cached for this table and re-read the current
record". Its lock-testing branch is an *optimization* — when a lock is held, or the file is
non-shared, nothing can have changed, so a plain `d4go` suffices. **Without locks, the honest port is
to always do the work**, which makes it small and implementable now:

- reset the memo cache for every memo field (`f4memoReset`, and `file4refresh` on the memo file);
- drop this table's cached blocks;
- re-read the current record, keeping the cursor where it is.

That is the caller's answer to "I chose `Always`, and now I want to know whether the file moved". It
is what makes `Always` a door rather than a one-way door, and it is meaningful even with no cache at
all, because the memo reset and the record re-read are real.

### The two hooks, built now and called later

`LOCKING-TRANSACTIONS.md` §3.7 states the ordering the port must preserve:

> After acquiring a file/index lock the engine calls `file4refresh` (drop optimization caches;
> `df4lock.c:658-660`, `I4LOCK.C:401-403`) and `i4versionCheck` … Unlocking flushes …
> **flush before unlock, invalidate after lock**.

So the cache is built with those two operations as explicit, per-file entry points —
`Invalidate(source)` and (when `WRITE` exists) `Flush(source)` — rather than as a `Refresh()`
implementation detail. `LOCKING` then plugs in by calling them in the specified order, and **the cache
does not get redesigned to accept it.** Building the seam now costs nothing; discovering it later
costs a rewrite of the thing whose failure mode is a wrong record set.

### Why locking is a separate step

**It was not missed.** `LOCKING` is **P3, not started** in `PORTING-PLAN.md` §5; its spec
`LOCKING-TRANSACTIONS.md` is already written down to §5.3's concrete lock table; and **risk R6 already
names this exact interaction** — *"read caching on unlocked files can mix old/new bytes … default to no
unsafe read-opt on shared files"* — which is what decision 2 does. The plan anticipated this and the
design implements its mitigation.

Folding `d4lock` in would break the step (`DEV_APPROACH.md` §1) in three ways at once: it advances a
**second** capability, it needs a **second gate** of a different kind (contention cannot be expressed by
the corpus — R3's mitigation is a *two-process interop test* plus external live-VFP verification), and it
carries the project's highest interop risk (**R3**, VFP byte-range offsets) into a change whose whole
appeal is that it cannot alter what a file decodes to.

**It is the natural next step, though, and it makes the cache safe on shared files.** Worth knowing when
that is scheduled: in stand-alone shared mode a read lock *is* a write lock — "whenever the file is not
opened exclusive every lock request is forced to `lock4write`" (§1.2, `d4lock.c:200-206`) — so a reader
that locks genuinely excludes writers, and `WhenExclusive` could then extend to "or while locked". That
makes `LOCKING` valuable **before** `WRITE` for the first time, which is a change to §5's P2/P3 ordering
and therefore a decision to record, not one to make quietly.

## Classes

| Class | ECB | Responsibility, one sentence |
|---|---|---|
| `CachingRandomAccessSource` | Controller | Serves one file's reads out of the shared pool, fetching from the wrapped source on a miss. |
| `BlockPool` | Controller | Owns the resident blocks for an engine and finds, admits and retires them. |
| `BlockResidency` | Entity | Pure: given the slots and their priorities, names the block to retire — the C's five-list split, with no I/O and no clock. |
| `CachingSourceFactory` | Boundary | Wraps an `IRandomAccessSourceFactory` so a cached engine composes without any other class knowing, and applies the per-file policy at open. |
| `BlockCacheMode` | Entity | The reference's `OPT4OFF` / `OPT4EXCLUSIVE` / `OPT4ALL` as an enum, named for what it means rather than what it was numbered. |
| *(on `CodeBaseEngine` and `Table`)* | Boundary | `DropCachedBlocks()` — the `code4optSuspend` / `d4freeBlocks` pair: discard what the cache believes, so `Always` is not a one-way door. |
| `LeafBlock.CompareAt`, `BranchBlock.ChildAt` | — | Additions to existing entities; no new class. |

`BlockResidency` is split out on purpose: eviction is the part that can be *wrong* rather than slow, and
pure means it can be driven exhaustively at memory speed (`DEV_APPROACH.md` §3.2 — "parsing, I/O, and
caching are three classes, never one").

## Seams

Everything this step needs is already an interface, which is why no production class outside the cache
changes shape.

- **`IRandomAccessSource`** — the cache both *implements* it and *depends* on it. The wrapped instance is
  the seam: a hand-written in-memory source proves cache hits and misses with no file, and Moq proves
  the hostile cases (a short read, an `IOException` mid-fetch, a read that returns fewer bytes than
  asked). Per `DEV_APPROACH.md` §5 the span-taking `Read` cannot be mocked, so the in-memory fake is the
  primary double and it must be run through the existing `RandomAccessSourceContract`.
- **`IRandomAccessSourceFactory`** — where the cache is composed in; the engine's existing internal
  constructor already takes it, so the benchmark and the tests can inject a cached factory with no new
  seam at all.
- **A read counter** is the one new observable: the tests need to prove a *hit did not reach the file*,
  and that is an interaction which genuinely is the requirement (`DEV_APPROACH.md` §4). A counting
  in-memory source, not a mock verification, keeps it at layer 2.

## Decisions this design makes

1. **The cache decorates `IRandomAccessSource`, not `NodeReader`** — because the walk's reads are 96%
   DBF, and because that is where the C caches. **ADR candidate.**
2. **The cache is optional, with the reference's three-valued policy and its default** —
   `Off` / `WhenExclusive` / `Always`, defaulting to `WhenExclusive`, which today caches nothing because
   no file is opened exclusively yet and which starts caching by itself once `LOCKING` adds deny modes.
   A cached block is stale as soon as another process writes, and this is the rule that makes staleness
   impossible rather than merely unlikely. **ADR candidate, and the important one.**
3. **The invalidation surface ships with the cache, not after it.** `DropCachedBlocks()` on the engine and
   on a table, porting `code4optSuspend` and `d4freeBlocks`, plus `Table.Refresh()` for `d4refresh`.
   Lock-driven trust and per-read `forceCurrent` wait for `LOCKING`; discarding does not need to.
   **The cache exposes `Invalidate(source)` / `Flush(source)` as entry points from the start**, so
   `LOCKING-TRANSACTIONS.md` §3.7's "flush before unlock, invalidate after lock" plugs in without the
   cache being redesigned. `d4lock` itself is a separate step — not missed, deferred: `LOCKING` is P3,
   its spec is written, and **R6 already names this hazard with the mitigation decision 2 implements**.
4. **The pool is engine-wide, not per file** — one budget shared across a table, its index and its memo,
   as `CODE4` does; a per-file pool would give a table with three files three budgets.
5. **Eviction is a pure class with the C's priority split** — index blocks cannot be evicted by a data
   scan.
6. **`EntryAt` keeps its copy**; the hot paths get narrow methods beside it. Not an ADR — a local shape
   decision, recorded in `ANALYSIS.md` §6.
7. **The benchmark uses BenchmarkDotNet**, which is already in the stack list unused — chosen
   specifically because it handles warm-up and tiered JIT, the trap that made this audit's own first
   RAM-backed measurement wrong by 4× (`ANALYSIS.md` §8).

## Open questions

- ~~**Does the benchmark table get committed?**~~ **Settled 2026-08-19: yes.** `PERF10K` (610 KB DBF +
  305 KB CDX) becomes a generator case and a committed corpus file, because `CDXDEEP`'s ~55 leaves cannot
  exercise eviction and a benchmark nobody can re-run is the state this audit just got us out of. Sub-step
  1 owes `net/corpus/README.md` and `test-files-generator/README.md` with it. It is a **benchmark** case,
  not a gate: no `.dump.txt`, and no golden test asserts against it.
- **Pooled blocks without a copy.** Removing `NodeReader`'s `byte[512]` needs a rule for a block held by
  a live `LeafBlock` while the pool wants to retire it. Reference counting, or blocks pinned for a
  cursor's lifetime, or leaving the copy. Deferred, and worth its own measurement first — after item 3
  the copy is 2 144 of 2 568 bytes per seek.
- **Default pool size.** The C sizes from `memStartBlock`/`memMaxBlock`. A block count with a documented
  default is the obvious shape; what the default *is* wants a measurement across table sizes that this
  step does not take. Note the interaction with the mode: with `WhenExclusive` and no exclusive opens,
  the pool is never allocated at all, so the default size costs nothing until it is asked for.
- **`WhenExclusive` needs something to ask.** The policy is portable now but unusable until a source can
  report how it was opened. Either `IRandomAccessSource` grows a way to say so, or the factory that
  opened it records it — a small seam decision for sub-step 5, and the reason the mode is testable long
  before `LOCKING` exists (a fake source can claim to be exclusive).
- **`QUERY` may want more than a block cache.** A bitmap build walks a tag range; the C also has
  read-ahead for runs (`opt4fileReadSpBuffer`). Whether sequential read-ahead is worth adding is a
  `QUERY`-era question, not this one.
