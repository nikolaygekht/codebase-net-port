# Project state

**Updated:** 2026-08-27 · the tree is **clean**; everything is committed to `main` and **not pushed**.
**Step [`009-performance-audit`](claude/dev/009-performance-audit/) is closed** — both halves, the audit
and its remediation, with the decisions recorded as **ADR-35 to ADR-41**. It opened no capability and
adds no row to `PORTING-PLAN.md` §5; what it changed is that **the read path is fast enough for `QUERY`
to drive, and measured rather than guessed at**.
**The next session starts at step [`010-expr`](claude/dev/010-expr/)**, which is designed and not yet
planned — or at `claude/specs/QUERY-OPTIMIZER.md`, which does not depend on it. §3 has both.
**The step's own headline: the largest cost in the read path was on nobody's list.** A
`RandomAccess.GetLength` per block read, four per descent with the cache fully warm, **87% of a cached
descent** — invisible to the audit because a metadata call is not a read (**ADR-35**). A cached seek is
**732 ns** against 3 885, and **the audit's predicted 11.6× for the block cache is met** after standing
at 2.13×. The same call was ~25% of a cached walk and **90% of the memo path, which had never been
measured at all**.
Against the C library the port is **~1.85× on CPU-bound work and at parity or better when the work is
I/O**, and a seek is **5.41×** a plain `Go`+`Skip` where the C's own ratio is 5.84×.
**`COLLATION` is done, `CDX-READ` is done, and risk R2 is retired.**

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read whole, its records can be read in an index tag's order, and the read path is
cached.** `net/CodeBase.Net.sln` builds six projects — `CodeBase.Net` (**no NuGet dependencies** by
design, ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden`, `CodeBase.Net.TestUtils`,
`CodeBase.Net.Benchmarks` and `CodeBase.Net.Profiling` — and `dotnet test` is green on **1333 tests**,
566 of them golden.

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");

for (GoResult go = table.Top(); go == GoResult.Ok; )
{
    FieldDefinition name = table.Fields["NAME"];
    string text = table.GetString(name);     // decoded, trailing blanks kept (ADR-21)
    if (table.Skip(1) != SkipResult.Moved) break;
}
```

Opening a table reads its header, its stored descriptors and its resolved field table, and opens the
memo file beside it when the header declares one. It resolves the code page mark: `CodePage`,
`CodePageNumber` and `CodePageByte` answer for all 26 marks Visual FoxPro documents, without needing
an encoding provider (ADR-19, ADR-20). Moving the cursor reads one record, and the typed accessors
read fields out of it — `GetString`, `GetRawBytes`, `GetBoolean`, `GetInt32`, `GetDouble`,
`GetDecimal`, `GetDate`, `GetDateTime`, `IsNull`, plus `Deleted`, `Eof` and `Bof`. Memo fields answer
too — `GetMemoBytes`, `GetMemoString`, `GetMemoLength`, `GetMemoBlock`, `GetMemoType` — in both
reference encodings.

**The production index opens with the table, and a tag is an order the cursor can follow:**

```csharp
FieldDefinition name = table.Fields["NAME"];
Tag byName = table.Tags["NAME"];          // Table.Tags is empty when the header declares no index

table.SelectTag(byName);                  // Top, Bottom and Skip now move through the tag
for (GoResult go = table.Top(); go == GoResult.Ok; )
{
    string text = table.GetString(name);
    if (table.Skip(1) != SkipResult.Moved) break;
}

// or per call, leaving the table's mode alone:
for (GoResult go = table.GoFirstIndexed(byName); go == GoResult.Ok; go = table.GoNextIndexed(byName))
    Console.WriteLine(table.GetString(name));
```

Both forms share one cursor per tag, so a walk started with either continues with the other. A tag's pad
byte is **derived** from the table's field descriptors when its expression is a bare field name (ADR-28);
the reader below stays `internal`.

`CodeBase.Net.Cdx` reads both file shapes — a compound file through its tag directory, and a
single-tag `.IDX` whose tag is named after the file — then tag headers, interior nodes with their
big-endian pointers, and **bit-packed leaves**. Walking follows the leaf chain in either direction and
inverts for a descending tag. Machine and `GENERAL` collation both read; a `GENERAL` tag needs no
weight table, because reading a key never computes one.

**And a table can be asked where a value is**, which is what the whole read path was for:

```csharp
table.SelectTag(table.Tags["NAME"]);

table.Seek("SMITH");             // exact, whole key: Ok or NoRecord, and a miss lands on nothing
table.SeekPrefix("SMITH");       // SMITH, SMITHSON, SMITHERS alike
table.SeekAtOrAfter("S");        // Found | After | Eof -- positions on a neighbour by design
table.SeekAtOrBefore("S");       // Found | Before | Bof -- a range's other end

while (table.SeekNext() == GoResult.Ok)
    Console.WriteLine(table.GetString(name));
```

**One method per behaviour.** Whether a miss repositions, and whether a short value is a prefix, are
each a choice of method rather than a status code to read or a trailing space to remember. `Seek` also
takes a `double`, an `int` and a `DateOnly` where the tag's keys are of that kind, and refuses by name
where they are not.

Behind it: the `COLL4ARR.C` weight tables for cp1252, cp437 and cp850 **copied verbatim**, every
numeric transform including `t4dblToFox`'s `-0.0` wraparound, the `GENERAL` head-and-tail key with its
expansions, and the empirical `flags4dateTime` bitmap. Collated tags are checked against the table's
code page, because `GENERAL` names a sort order and not a table.

`TagCursor` still offers the same five operations over raw key bytes underneath, and `Synchronize` now
**descends** to a record's position instead of walking to it.

**And the read path caches its blocks**, which is what step 009 was for:

```csharp
using var engine = new CodeBaseEngine
{
    BlockCache = BlockCacheMode.Always,   // Off | WhenExclusive (default) | Always
    BlockCacheBlocks = 2048,
};
…
table.Refresh();                          // forget what was cached and re-read this record
int dropped = engine.DropCachedBlocks();  // or table.DropCachedBlocks(), for one table
```

A hash-addressed pool of fixed-size windows decorates `IRandomAccessSource`, so **one pool serves a
table's data, index and memo alike** — which is where the reference caches, and the only place that
catches a walk, whose reads are 96% DBF (ADR-36). It is optional on the reference's own three-valued
policy, and **the default caches nothing**: no file this port opens excludes other writers yet, so
`WhenExclusive` evaluates to "cache nothing" and **starts caching by itself when `LOCKING` adds deny
modes** (ADR-37). Eviction is a pure class in the C's own priority order — `other`, `dataLow`,
`indexLow`, `dataHigh`, `indexHigh` — so a table scan cannot evict the index tree (ADR-39).

**The library states a threading contract and detects its breach.** Not concurrently, not one thread:
no two operations against one `Table` at the same time, which is ADO.NET's contract for `DbConnection`.
An interlocked claim on each of `Table`'s 22 entry points turns a missing caller lock into an exception
rather than a wrong record set — **a smoke alarm, not a fire door** — and the library's one real lock
sits inside `BlockCache`, because the engine's pool is shared and two tables of one engine queried in
parallel is ordinary (ADR-41).

**Performance can be re-measured by anyone.** `net/benchmarks/CodeBase.Net.Benchmarks` on
BenchmarkDotNet over the committed `PERF10K`, and `CodeBase.Net.Profiling`, a single-process driver an
external profiler can watch that prints reads, `Length` queries and cache hits per operation. The
speed-up itself is gated in the suite at 2× (ADR-40). **Run them natively on Windows, never through
`/mnt/d`** — the same seek costs 9.2 µs natively and 1.58 ms across the 9p bridge.

**Everything is gated against the C library's own view, with nothing skipped.** On the table side: all
twelve corpus tables, every record, every field, and every memo value. On the index side — counted at
step 006, so **a floor**: `CDXTIME` has joined the corpus since, taking the dumps to 25 tag headers and
166 blocks — **22 tags, 3364 keys, 155 blocks and 3425 block entries** — including each leaf entry's stored duplicate and trail
counts, so the bit-packing is checked as an encoding and not only through the keys it rebuilds. Seeking
adds **206 recorded search cases, 104 seek-next runs and 3364 exact-pair assertions**, plus properties for
the three operations the C library does not have. Navigating a table by index adds **3364 records reached
through 18 tags, with every field of every one checked against that record's own dump** — through both
surfaces, in both directions. The one refusal is a **compressed memo entry**, which no corpus case can gate
yet (ADR-23, open).

**`test-files-generator/`** builds and runs end to end (Windows/MSVC):

```bat
build-lib.bat     :: 137 TUs -> obj\codebase.lib   (~2 min; ~1.4 s incremental)
build-gen.bat     :: src\*.cpp -> bin\testgen.exe
bin\testgen.exe   :: -> bin\out\
copy-corpus.bat   :: -> ..\net\corpus\
```

**`net/corpus/`** — twelve cases, five of them indexed. Each table has a `<NAME>.dump.txt` and each
index file a `<NAME>.cdx.dump.txt`, both written by the C library:

| File | Version | Covers |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x set `C N D L`, no memo |
| `VFPTYPE.DBF` | `0x30` | every non-memo VFP type `C N F D L I B Y T` |
| `F2XMEMO.DBF` + `.fpt` | `0xF5` | FoxPro 2.x memo, 10-byte ASCII memo reference |
| `VFPMEMO.DBF` + `.fpt` | `0x30` | memo + binary types `M X G Z`, 4-byte reference, payloads straddling an FPT block boundary |
| `VFPNULL.DBF` + `.fpt` | `0x30` | nullable fields, the hidden `_NullFlags` descriptor, and the memo/null interaction |
| `CP1251.DBF` + `.fpt` | `0x30` | a marked code page, single-byte (byte 29 = `0xC9`) |
| `CP936.DBF` + `.fpt` | `0x30` | a marked code page, multi-byte (byte 29 = `0x7A`), characters cut in half |
| `CDXBASE.DBF` + `.cdx` | `0x30` | **ten tags, one block each** — one per key shape: prefixes, blanks, descending, duplicates, unique, sub-`0x20` bytes, numeric, `-0.0`, date, integer, filtered |
| `CDXDEEP.DBF` + `.cdx` | `0x30` | **three levels deep** — 600 records, 55 leaves under two levels of branch, full leaves, equal keys across block boundaries, and one **descending** tag so a backwards walk crosses blocks |
| `CDXCOLL.DBF` + `.cdx` | `0x30` | **machine beside `GENERAL`** over one cp1252 field: `keyLen` 20 and 40, pad `0x20` and `0x00`, accents and the `œ`/`ß`/`þ` expansions |
| `IDXONE.DBF` + `.cdx` + `.IDX` | `0x30` | **one tree in both file shapes**, the `.IDX` derived and verified by walking both (ADR-25) |
| `CDXTIME.DBF` + `.cdx` | `0x30` | **256 datetimes**, built in step 008 so real keys could check the 10802-byte `flags4dateTime` copy |

Beside them, `net/corpus/benchmark/PERF10K.DBF` + `.cdx` (610 456 + 304 640 bytes): a 10 000-record
table with a `C(20)` and an `N(12,2)` tag, **benchmark data and not a gate** — no `.dump.txt`, no golden
test, and invisible to `Corpus.TableNames`, which is not recursive.

Regeneration is byte-identical, index files included.

**Documentation:** seven format specs, the porting plan, the development approach and the decision
log. `claude/specs/QUERY-OPTIMIZER.md` **does not exist** and is the gap that matters — the optimizer
is the only in-scope subsystem with no source-cited spec (risk R13).

---

## 2. Last session (2026-08-27), and the three before it

**Step 009 is closed, both halves.** This session decided the eight ADR candidates the step was held
open on and wrote the closeout; **no code changed** beyond one orphaned XML-doc `<summary>` in
`Table.cs` that had been left stranded above `Enter()` when sub-step 7 added it, and would have given
docgen the wrong Brief. Tests green at **1333** (767 + 566).

**Six new entries, ADR-36 to ADR-41** — where the cache lives and that one pool serves the engine; the
three-valued policy and why the shipped default caches nothing; the invalidation surface and the block
that lives in two places; eviction as a pure class in the reference's priority order; the project's
first performance gate; and the threading contract with the detector and the library's first lock. Two
candidates deliberately did not become entries: the engine-wide pool is one sentence inside ADR-36, and
**rent-and-return with the window as the residency unit is deferred**, because it describes a zero-copy
design that was not built — writing it now would document a plan, not a decision.

**The design did not survive execution unchanged in three places**, which is why the entries were worth
writing rather than transcribing: dropping blocks turned out to have two halves, `Refresh` has no memo
cache to reset because `MemoReader` keeps no state, and the 2× gate had to be retargeted onto read
counts.

**Before it, 2026-08-21 to 2026-08-22, the remediation ran** — eight sub-steps, the eighth unplanned,
inside the same folder the way `007-audit-glm` held its own. What landed is in §1; what it cost and what
it bought is in [`SUMMARY.md`](claude/dev/009-performance-audit/SUMMARY.md) and the per-sub-step results
files beside it. Three things are worth carrying out of it:

- **Every gate passed and the step was still wrong about where its time went — three times.** Sub-step
  2's comparison work, sub-step 5's "3.9 µs in the `Table` layer", and finally the `Length` syscall.
  **What found it was counting, not timing**: `4.000 Length queries and 0.000 reads per descent` named
  the mechanism in one run, where a gate asserting a ratio cannot notice that the quantity it divides is
  the wrong one.
- **The rule ADR-35 generalises to:** *let the read be the refusal — unless the value being checked
  sizes an allocation.* A fixed-size block read can fail safely; a memo payload whose length the file
  supplies cannot.
- **A process failure and its fix held.** Restoring after a mutation check is done from a checksummed
  copy, **never** from `git` (`DEV_APPROACH.md` §4) — the rule step 008 wrote after `git checkout --`
  destroyed an uncommitted surface. It was followed every time this step mutated something.

**And the audit itself closed 2026-08-19** with no change to `net/`: 10 000 seeks over a 10 000-record
table through the reference C library and through `CodeBase.Net`, same file, same query sets, matching
per-record checksums. `experiments/` is gitignored; the harness *sources* are committed under
[`009-performance-audit/harness/`](claude/dev/009-performance-audit/harness/), so the measurement can be
rebuilt from the repository.

**Two consequences of the 2026-08-19 renumbering, worth knowing before reading a `git log`:** the older
unpushed commits say `007-seek-by-value`, which was the folder's name when they were written — their
content is step 008's; and `007-audit-glm/REMEDIATION-PLAN.md` §2.2 and §2.3 are out of order, because
it planned to measure before `COLLATION` and the reverse happened.

## 3. Next

### Step 010 is `EXPR` — where the next session starts

**It is [designed](claude/dev/010-expr/DESIGN.md), not yet planned.** `CDX-READ` owes it exactly **one**
thing: typing a key expression that is not a bare field name (ADR-28). The second refusal, ADR-30,
turned out not to need the expression engine at all and was closed inside step 008 as **ADR-34** — the
third time that premise had been repeated without being checked. Scope is the **whole** function table,
36 named functions and the operator set, because `QUERY` needs the filter vocabulary anyway.

### Two things that depend on nothing

**Write `claude/specs/QUERY-OPTIMIZER.md`.** Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`, to
the same `FILE.C:line` standard as the existing seven. Must cover the `BITMAP4` tree and flags,
`CONST4` range constraints, filter-to-bitmap decomposition, **which expression forms are optimizable and
which are not**, leaf evaluation via tag seek, AND/OR/negation combination, and the fall-back-to-scan
boundary. Prerequisite for `QUERY` (risk R13), and **the only in-scope subsystem with no source-cited
spec**.

**Close ADR-23 if it is wanted.** Its own small step: add zlib to the generator, reconstruct the
`c4compress` wrapper from the layout the reader pins down, add a case with `code4memoCompress` enabled
and a payload longer than one block. The reader is then a few lines over `ZLibStream`.

### What step 009 left on the table

None of it blocks anything, and all of it is measured rather than suspected:

- **The `byte[512]` per block read** (`IO/SourceReader.cs:33`) — ~10–20% of the 445 ns cached descent,
  and **the largest remaining item after the leaf scan**. Handing out a pinned slice of the cache's own
  window removes the allocation *and* the 14 ns per-window copy. It needs a lifetime rule, worked out in
  [`DESIGN.md`](claude/dev/009-performance-audit/DESIGN.md)'s Open questions and deliberately left
  undecided (ADR-36's consequence).
- **Eviction has never been exercised by a realistic workload.** `PERF10K` is ~1 788 windows against a
  2 048-window pool, so **nothing was ever evicted**. Under a forced small pool a walk is worse than no
  cache at all — the cache-hostile shape by definition — which points at a *missing* mechanism, scan
  resistance and read-ahead (`opt4fileReadSpBuffer`), not a wrong policy. Judging simple against
  faithful needs the second `IBlockResidency` **and** a workload larger than a real pool (ADR-39).
- **`Seek(string)` on a non-character tag reinterprets instead of refusing**, and unparseable text
  becomes the key of zero — a wrong record set with no exception
  ([`DEFECT-seek-string-on-a-non-character-tag.md`](claude/dev/009-performance-audit/DEFECT-seek-string-on-a-non-character-tag.md)).
  Left for its own step because it is a behaviour change and 009's gate was that nothing a file decodes
  to may change.
- **One unexplained movement, recorded rather than smoothed over.** Cached `Go`+`Skip` went 117.5 →
  135.4 ns across sub-step 8, same session, uncached control unmoved, on an arm that never calls
  `NodeReader`.
- **P6 and P7 are still unjudged**; both need a duplicate-heavy corpus tag.

### One open decision, and it is not blocking

**Whether `LOCKING` moves ahead of `WRITE`** in `PORTING-PLAN.md` §5. In stand-alone shared mode a read
lock *is* a write lock (`LOCKING-TRANSACTIONS.md` §1.2), so a locking reader really does exclude
writers — which would make `WhenExclusive` start caching and make `LOCKING` valuable **before** `WRITE`
for the first time. That is a change to §5's priorities to decide rather than assume.

**So the coupling cannot be forgotten**, `PORTING-PLAN.md` §5 carries a **cross-capability obligation**
table: what `WRITE`, `LOCKING` and `TRANS` each owe the block cache, with the C citations, plus a
pointer from each capability's own section and from risk **R6**. A capability is not done until its row
there is done — because nobody starting `WRITE` in six months will read a closed audit's design folder.

### The standing rules this pass established

**Correctness first, and no optimization without a measurement and a gate that still passes** — and the
gate is on the *speed-up itself*, not only on correctness (ADR-40), because every other gate in step 009
passes on a cache that is perfectly correct and never hits.

**Warm up by wall clock, never by pass count.** The audit's own first RAM-backed measurement was 4×
wrong because five passes never gave the JIT time to reach tier-1. The standing check: *a figure that
moves when only its position moves is measuring the runtime, not the code.*

### The corpus spot-check pass

Largely spent: FPT `numChars` = payload-only (witnessed for all 153 entries, step 003), **CDX
interior-node big-endian record number and child pointer** and **the `t4dblToFox` sign rule including
`-0.0`** (witnessed in step 004, with the `GENERAL` head-and-tail layout). What is left of it is the
263-byte reserved area.

**Twelve corpus gaps** remain named in `PORTING-PLAN.md` §6.3, wanting generator cases rather than unit
tests standing in for them.
