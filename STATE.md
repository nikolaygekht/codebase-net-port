# Project state

**Updated:** 2026-08-19 · the tree is **clean**, and everything is committed and **pushed** to `main` —
including step 008's three commits, which had been waiting (their messages say `007-seek-by-value`, the
name the folder carried when they were written). `net/` is untouched — **no `.cs` file was opened this
session** — so **1177 tests, 526 golden** still stands.
**Active step:** [`009-performance-audit`](claude/dev/009-performance-audit/) — its audit is
**[closed](claude/dev/009-performance-audit/SUMMARY.md)** and its remediation is
[designed](claude/dev/009-performance-audit/DESIGN.md),
[planned](claude/dev/009-performance-audit/PLAN.md) and **not started**; the live sub-step is named in
[its own `STATE.md`](claude/dev/009-performance-audit/STATE.md).
[`008-seek-by-value`](claude/dev/008-seek-by-value/) is
**[closed](claude/dev/008-seek-by-value/SUMMARY.md)**: a table can be asked where a value is, and
**no navigation path refuses any more** — ADR-34 closed the last one.
**`COLLATION` is done, `CDX-READ` is done, and risk R2 is retired.**
**Next session starts at `PLAN.md` sub-step 1** — the benchmark project — of six, none executed. Step **010, `EXPR`**
is [designed](claude/dev/010-expr/DESIGN.md) and follows it. **The performance pass now has numbers
instead of guesses**, and the block cache is measured to be worth an order of magnitude here too.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read whole, and its records can be read in an index tag's order.**
`net/CodeBase.Net.sln` builds four projects — `CodeBase.Net` (**no NuGet dependencies** by design,
ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden` and `CodeBase.Net.TestUtils` — and `dotnet test` is
green on **1177 tests**, 526 of them golden.

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

**Everything is gated against the C library's own view, with nothing skipped.** On the table side: all
eleven corpus tables, every record, every field, and every memo value. On the index side: **22 tags,
3364 keys, 155 blocks and 3425 block entries** — including each leaf entry's stored duplicate and trail
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

**`net/corpus/`** — eleven cases, four of them indexed. Each table has a `<NAME>.dump.txt` and each
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

Regeneration is byte-identical, index files included.

**Documentation:** seven format specs, the porting plan, the development approach and the decision
log. `claude/specs/QUERY-OPTIMIZER.md` **does not exist** and is the gap that matters — the optimizer
is the only in-scope subsystem with no source-cited spec (risk R13).

---

## 2. Last session (2026-08-19)

**The port was measured for the first time, and `claude/dev` was renumbered.** No code changed —
`net/` is byte-for-byte what step 008 left.

**[`009-performance-audit`](claude/dev/009-performance-audit/) is closed.** 10 000 seeks over a
10 000-record table, run through the reference C library and through `CodeBase.Net`, against the same
file and the same query files, with per-record checksums proving both did identical work. Read its
[`SUMMARY.md`](claude/dev/009-performance-audit/SUMMARY.md); the numbers and what they change are in
§3, and the decomposition of the 14× is
[`ANALYSIS.md`](claude/dev/009-performance-audit/ANALYSIS.md). The short version: **the 14× is read
syscalls and nothing else, and the same win is available here — 11.6× on a seek, 23.4× on a walk. The
port's own CPU work is 1.4–1.7× the C's, and faster on a walk.**

**Its remediation is designed and planned, and not started.**
[`DESIGN.md`](claude/dev/009-performance-audit/DESIGN.md) and
[`PLAN.md`](claude/dev/009-performance-audit/PLAN.md) — six sub-steps, stoppable after three. Three
things go in: the block cache, compare-in-place, and the padded comparison; plus `Table.Refresh()` and
the coherency hooks. `d4lock` deliberately stays out. §3 has the shape and the reasoning.

**The `claude/dev` numbering collision is fixed.** The audit that followed step 006 had been filed as a
*second* `006-`, on the reasoning that an audit opening no capability deserved no number. That made two
folders share a number, so the folders were renumbered and every reference moved with them:

| Was | Is |
|---|---|
| `006-audit-glm` | [`007-audit-glm`](claude/dev/007-audit-glm/) |
| `007-seek-by-value` | [`008-seek-by-value`](claude/dev/008-seek-by-value/) |
| — | [`009-performance-audit`](claude/dev/009-performance-audit/) *(new)* |
| `008-expr` | [`010-expr`](claude/dev/010-expr/) |

**The rule changed with it**, in [`claude/dev/README.md`](claude/dev/README.md): *a folder that opens no
capability still takes the next number.* An audit is numbered like a step; what makes it not a step is
that `PORTING-PLAN.md` §5 gains no row, not that it goes unnumbered.

Two consequences worth knowing before reading a `git log`:

- **The three unpushed commits say `007-seek-by-value`.** That was the folder's name when they were
  written. Their content is step 008's.
- **`007-audit-glm/REMEDIATION-PLAN.md` §2.2 and §2.3 are out of order** — it planned to measure before
  `COLLATION`, and the reverse happened. Its step numbers now point where the work actually went; a note
  at §2.2 says so, and the section order is left alone because it records a plan, not an outcome.

**`experiments/` is gitignored**, and holds the scratch area the harness was built in. The harness
*sources* are committed, under
[`009-performance-audit/harness/`](claude/dev/009-performance-audit/harness/), so the measurement can be
rebuilt from the repository; the 610 KB table it generates is not.

## 3. Next

### Step 008 is closed — a table can be asked where a value is

[`008-seek-by-value`](claude/dev/008-seek-by-value/) is done; read its
[`SUMMARY.md`](claude/dev/008-seek-by-value/SUMMARY.md). **`COLLATION` is complete, `CDX-READ` is
complete, and risk R2 is retired** — the second of the port's two highest silent-corruption risks,
after `CDX-READ`'s bit-packed leaves. Both are now closed.

Section 1 has the surface. Underneath it: the `COLL4ARR.C` weight tables copied verbatim, every numeric
transform, the `GENERAL` head-and-tail key, and the empirical `flags4dateTime` bitmap — with a new
corpus case, **`CDXTIME`**, built specifically so real keys could check that 10802-byte copy.

**Gated by 3559 keys** rebuilt from the values that produced them, every character tag sought end to
end, and every position of eight tags stepped through `Go(n)` then `Skip(±1)` against the dump's own
key order.

**Three things the step corrected in the documents rather than in code.** `considerPartialSeek` is
bigger than `KEY-COLLATION.md` §3.4 describes — the key is also cut back to its head bytes.
`Synchronize` never needed `EXPR`, because ADR-28 already limits a selectable tag to a bare field name.
And the audit's §1.3 collation/code-page finding closed as a side effect of writing the code, five
sub-steps before it was scheduled.

**Two C branches are provably untestable**, and are recorded in the summary rather than left looking
like gaps: the datetime one-byte borrow, and the collated tail-count guard. Both are faithful
reproductions whose effect is always masked; neither can be made to fail by mutation, and the summary
says why so nobody removes them later.

**A process failure worth carrying forward.** `git checkout -- Table.cs` destroyed the uncommitted seek
surface mid-session — the same mistake step 006 made, in a rule written to prevent it. It was
reconstructed and the suite went green immediately. `DEV_APPROACH.md` §4 is sharpened: restore by
checksum from a copy, **never** by `git`, regardless of which file the mutation touched.

### The 002–005 audit is closed — five defects fixed, three of its claims overturned

An independent pass over steps 002–005 lives in
[`claude/dev/007-audit-glm/`](claude/dev/007-audit-glm/), with the triage, the decisions, and the
remediation beside it. Read [`SUMMARY.md`](claude/dev/007-audit-glm/SUMMARY.md). It found **no
wrong-record-set bug**, which matches its own verdict.

**Fixed:** `'7'` removed (ADR-32); `MemoFileHeader.BlockSize` reads signed; the leaf chain is bounded,
so a cycle of empty blocks no longer hangs; `LeafGeometry` bounds its mask widths and not only their
sum; `SeekFirstAtOrAbove` steps over an empty leaf the way a walk already did; an index short read says
`ErrorCode.Index`; a `'T'` millisecond count that leaves the calendar is a library error rather than an
`ArgumentOutOfRangeException`; and `IsNull` past a short bitmap is pinned as not-null.

**Overturned:** `'H'` blanking was already correct (`f4blank` space-fills `r4floatBin`); `'7'`'s stated
justification was false; and **`FoxDate`'s year zero was right all along** — the C library computes it
as leap and `JULIAN4ADJUSTMENT` depends on it, while the comment above the line, which this port had
faithfully copied, was never applied upstream (ADR-33). Dates before AD 1 are now out of scope.

**Decided along the way**, all recorded rather than left in this file: ADR-29 and ADR-30 stand as
written; `Table.HasProductionIndex` stays removed (**ADR-31**); `'7'` is out of scope (**ADR-32**); dates
before AD 1 are out of scope (**ADR-33**); and the mutation-check process rule is now `DEV_APPROACH.md`
§4, "Proving a gate".

**Everything else has a home**, in [`REMEDIATION-PLAN.md`](claude/dev/007-audit-glm/REMEDIATION-PLAN.md)
§5: the performance findings below, and **twelve corpus gaps** named in `PORTING-PLAN.md` §6.3 that want
generator cases rather than unit tests standing in for them.

### Step 010 is `EXPR` — where the next session starts

**It is [designed](claude/dev/010-expr/DESIGN.md), not yet planned.** `CDX-READ` owes it exactly **one**
thing: typing a key expression that is not a bare field name (ADR-28). The second refusal, ADR-30, turned
out not to need the expression engine at all and was closed inside step 008 as **ADR-34** — the third
time that premise had been repeated without being checked. Scope is the **whole** function table, 36
named functions and the operator set, because `QUERY` needs the filter vocabulary anyway.

### The performance pass — measured, and P1 dominates

**It has numbers now.** [`009-performance-audit`](claude/dev/009-performance-audit/) closed 2026-08-19:
10 000 seeks over a 10 000-record table, the same file and the same query files driven through the
reference C library and through `CodeBase.Net`, with matching per-record checksums proving both did
identical work. Full findings and method in its
[`SUMMARY.md`](claude/dev/009-performance-audit/SUMMARY.md) and
[`METHOD.md`](claude/dev/009-performance-audit/METHOD.md); what follows is only what it changes here.

Four configurations, because the C library's cache is off unless `code4optStart` is called, and because
the port can be given a perfect one by swapping its internal `IRandomAccessSource` for a `byte[]`:

| µs per operation | C | C **cached** | `CodeBase.Net` | port **cached** |
|---|---|---|---|---|
| seek, `C(20)` key, 4 levels | 5.56 | 0.40 | 7.84 | **0.68** |
| seek, `N(12,2)` key, 3 levels | 4.97 | 0.75 | 7.22 | **1.04** |
| tag walk, per record | 2.49 | 0.08 | 1.05 | **0.045** |

**The 14× is read syscalls, and only read syscalls.** Counted with `GetProcessIoCounters`, not inferred:
the C issues 4.286 reads per character seek at a measured **1.18–1.21 µs** each — 93% of its 5.56 µs —
and zero with the cache on. A hit is a hash lookup plus a `memcpy`: 0.002 µs for 512 bytes.

**What the measurement settled:**

1. **Build the cache — it is worth 11.6× on a seek and 23.4× on a walk**, and those are the port's own
   numbers, measured, not the C's borrowed. P1 is far and away the largest item in the pass. **The walk
   is the one to keep in view**: `QUERY` builds a bitmap by seeking one end of a range and then
   *walking*, so a 10 000-record range goes from 10.5 ms to 0.45 ms.
2. **The port's own CPU work is only 1.4–1.7× the C's, and on a walk it is 1.7× faster.** With I/O out
   of both sides: character seek 1.70×, numeric 1.38×, walk 0.58×. The 1.41× headline was not hiding
   anything, and P4 stays struck from the list.
3. **P2 is the whole of the remaining gap, and it needs no design decision.** The port materialises a
   fresh `byte[keyLength]` for *every* key comparison (`BranchBlock.EntryAt`, `LeafBlock.EntryAt`) where
   the C compares in place. Removing just those copies is measured at **1.7–1.9× of the descent** and
   1 292–1 744 bytes/seek, with no algorithmic change and identical results — `KeySearch.Compare` already
   takes a `ReadOnlySpan<byte>`. **The copy is deliberate and documented, and justified at exactly one of
   its five call sites** — `TagCursor.Current`, which hands an entry outward while the cursor moves on.
   The other four compare and discard, or read only the child pointer. The fix is therefore *additive*: a
   compare-in-place path beside `EntryAt`, whose contract does not change. `ANALYSIS.md` §6 has the call
   site table.
4. **P7 is implemented and measured, and does nothing here.** The C's duplicate-count skip changed the
   comparison count by zero on both tags, because every key is unique. It needs a duplicate-heavy corpus
   tag before it can be judged.
5. **Root retention is subsumed by the cache.** The port issues 5 reads per character seek where the C
   issues 4.286, because `tfile4upToRoot` keeps the root loaded per tag. Worth ~11% uncached; with a
   cache the root is a hash hit and the saving vanishes.
6. **GC is not the lever.** Forcing a 512 MB Gen0 budget took collections to zero and moved the timings
   under 1%.

**The remediation is designed and planned**, in [`DESIGN.md`](claude/dev/009-performance-audit/DESIGN.md)
and [`PLAN.md`](claude/dev/009-performance-audit/PLAN.md), filed inside 009 the way `007-audit-glm` held
its own. **The next session starts at `PLAN.md` step 1**, and
[`009-performance-audit/STATE.md`](claude/dev/009-performance-audit/STATE.md) is the live step state that
names the current sub-step. **Six sub-steps, stoppable after three** with all the CPU work done and the cache still owed:
benchmark project → compare-in-place → padded comparison → eviction rule → wire the cache up →
`Table.Refresh()` and the coherency hooks. Nothing is
executed yet; no `.cs` file has been opened.

**Two design decisions worth knowing before reading it:**

- **The cache decorates `IRandomAccessSource`, not `NodeReader`** — because a tag walk issues 1.044 reads
  per record of which **1.000 is the DBF record**, so an index-only cache would miss the 23.4× entirely.
  `RecordReader` and `NodeReader` already read through that one interface, which is exactly where the C
  caches (`file4readInternal`, below both layers).
- **It is optional, with the reference's own three-valued policy**: `Off` / `WhenExclusive` / `Always`,
  defaulting to `WhenExclusive`. `OPT4EXCLUSIVE` is the C's shipped default and it is a *safety* rule, not
  a convenience one — `optFlag = (file->lowAccessMode != OPEN4DENY_NONE)` (`f4opt.c:492-501`) caches only
  files opened excluding other writers, which makes staleness impossible rather than unlikely. Today no
  file this port opens is exclusive, so the faithful default caches nothing and keeps today's freshness;
  it **starts caching by itself** when `LOCKING` adds deny modes. A caller who knows they are the only
  writer asks for `Always`.

**`d4refresh` is in; `d4lock` is not, and that was not an oversight.** `LOCKING` is **P3, not started**
in `PORTING-PLAN.md` §5, `claude/specs/LOCKING-TRANSACTIONS.md` is already written, and **risk R6 already
names this exact hazard** — "read caching on unlocked files can mix old/new bytes; default to no unsafe
read-opt on shared files" — which is what the `WhenExclusive` default does. What this step adds is
`Table.Refresh()` and the cache's `Invalidate` / `Flush` entry points, so §3.7's **flush before unlock,
invalidate after lock** has something to call when `LOCKING` arrives. Worth noting for when it is
scheduled: in shared mode a read lock *is* a write lock (§1.2), so a locking reader really does exclude
writers — which would make `LOCKING` valuable **before** `WRITE` for the first time, and that is a change
to §5's priorities to decide rather than assume.

**And so the coupling cannot be forgotten**, `PORTING-PLAN.md` §5 now carries a
**cross-capability obligation** table: what `WRITE`, `LOCKING` and `TRANS` each owe the block cache,
with the C citations, plus a pointer from each capability's own section and from risk **R6**. Nobody
starting `WRITE` in six months will read a closed audit's design folder, so the obligation lives with
the capability that owes it, and a capability is not done until its row there is done.

The rule is unchanged and now has teeth: **correctness first, and no optimization without a measurement
and a gate that still passes.** A cache that serves a stale block is a wrong record set — which is why
the default is the reference's, and why the eviction rule is a pure class proved before it ever serves a
read.

**One methodological scar worth keeping.** The analysis' first pass reported the port's cached seek at
3.10 µs and concluded a cache was worth only 2.6× here. That was a harness defect, not a finding: it
warmed up by pass count, and a RAM-backed pass is fast enough that five of them never gave the JIT the
wall-clock it needs to reach tier-1. The harness now warms by wall clock and runs two variants in
reverse order as an order-effect check, and the two tiering settings agree to 2%. **The file-backed
numbers were never affected.**

Full decomposition, with the source citations and the two hypotheses it rules out, is
[`ANALYSIS.md`](claude/dev/009-performance-audit/ANALYSIS.md).

**Two things the audit could not reach**, and the next measurement should:

- **It measured CPU, not I/O.** 610 KB of DBF and 305 KB of CDX sit in the page cache after warm-up, so
  the 14× is CPU work avoided against an already-cached file — a **lower** bound for a cold or larger
  table, not an upper one.
- **There is still no benchmark project in the solution**, and BenchmarkDotNet is still an unused entry in
  the stack list — **that is `PLAN.md` sub-step 1**, which promotes the audit's harness
  ([`harness/`](claude/dev/009-performance-audit/harness/), committed as documentation, not as a gate)
  into `net/benchmarks/` and adds the `PERF10K` case. The baseline the audit *did not* take — an
  index-only walk and `Go(n)`-then-`Skip(1)` — is owed there.

Two things that do not depend on any of it:

**Write `claude/specs/QUERY-OPTIMIZER.md`.** Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`,
to the same `FILE.C:line` standard as the existing seven. Must cover the `BITMAP4` tree and flags,
`CONST4` range constraints, filter-to-bitmap decomposition, **which expression forms are optimizable
and which are not**, leaf evaluation via tag seek, AND/OR/negation combination, and the
fall-back-to-scan boundary. Prerequisite for `QUERY` (risk R13), and the only in-scope subsystem
with no source-cited spec.

**Close ADR-23 if it is wanted.** Its own small step: add zlib to the generator, reconstruct the
`c4compress` wrapper from the layout the reader pins down, add a case with `code4memoCompress`
enabled and a payload longer than one block. The reader is then a few lines over `ZLibStream`.

The `CORPUS` spot-check pass against the specs is now largely spent: FPT `numChars` = payload-only
(witnessed for all 153 entries, step 003), **CDX interior-node big-endian record number and child
pointer** and **the `t4dblToFox` sign rule including `-0.0`** (witnessed in step 004, and the
GENERAL head-and-tail layout with them). What is left of it is the 263-byte reserved area.
