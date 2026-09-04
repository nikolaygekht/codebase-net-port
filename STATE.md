# Project state

**Updated:** 2026-09-04 · **the tree is clean; not yet pushed.** The last commit is
**step 011's sub-step 7 — the residual filter and the public surface**, so **a caller can run a
query**: `table.Query("UPPER(NAME) = \"SM\" .AND. SALARY > 50000")`, walked in the selected tag's
order or in record-number order, with a count that reads no record when the plan answers the whole
filter. **Depth 4 of the gate is green on its first run** — all 81 filters, both orders, counts and
backwards walks — and **ADR-53's second divergence is proved**: the port's residual checks strictly
fewer terms than the reference's and returns the same records. `dotnet test` green on **3225 tests**
(764 golden, 2461 unit). No corpus file changed.
**Before it, sub-step 6 — the merge algebra**: two terms over one tag now become one seek, and
**depth 3 stayed green through it**, which is what the whole reorder was for. It went red the moment
merging landed and caught two real bugs, one of them a shared-buffer alias that nothing before
merging could have exposed. `dotnet test` green on **3209 tests** (748 golden, 2461 unit), up from
2066. Five mutations, all red. `CDXEXPR` gained seven query filters and its dump is the only
corpus file that changed.
**Before it, sub-step 5 — combination**, which made **the optimizer complete and correct**:
**depth 3 of the gate is green on all 74 filters**, meaning the candidate set this port builds holds
exactly the records the reference's own bitmap holds — compared before any residual filtering and
before any merging exists. `dotnet test` green on **2066 tests** (732 golden, 1334 unit). Four
mutations run, all red. No corpus file changed. It corrected a spec claim that would have cost the
whole gate.
**Before it, sub-step 4 — leaf evaluation**: one constraint reached its records by seeking.
`ITagProbe` (the step's one seam) with a contract test run against **both** implementations,
`TagProbe`, `RecordSet` and `ConstraintSeeker`. **41 single-leaf filters reach exactly the records
the reference's own bitmap holds** — the first end-to-end signal — and `dotnet test` is green on
**2054 tests** (720 golden, 1334 unit). Four of five mutations reddened; **the fifth found dead
code**, which was removed. No corpus file changed.
**Before it, sub-step 3 — decomposition**: a filter became a constraint tree.
`net/CodeBase.Net/Query/` gained `ConstraintValue`, `LeafConstraint`, `ConstraintNode`, `QueryPlan`
and a grown `QueryDecomposer`; **depth 2 of the gate is green on all 74 filters** and `dotnet test`
is green on **2006 tests** (714 golden, 1292 unit). Four of five mutations reddened; **the fifth is
inert for a reason worth knowing** — see §2. No corpus file changed.
**Before it, sub-step 2 — eligibility and subexpression identity**: `net/CodeBase.Net/Query/` now
holds `QueryTagScope` (public), `QueryTag`, `TagEligibility`, `SubtreeMatch` and `QueryDecomposer`,
and **depth 1 of the gate is green on its first run** — every node of all 74 filters matching the
reference's own parallel array, with **five mutations run against it and all five red**. `dotnet
test` green on **1968 tests** (676 golden, 1292 unit). It corrected the spec's type gate, which would
have cost three kinds of tag.
**Before it, sub-step 1 — the query oracle**: `test-files-generator/src/dump-query.h/.cpp` and query
filter lists on three cases, three new `net/corpus/*.query.dump.txt` files holding **70 filters at
five depths each**, and the C# that reads them. **No C# of the optimizer.** `dotnet test` green on
**1936 tests** (666 golden, up from 639), and **all 48 pre-existing corpus files regenerated
byte-identical**. The run **witnessed the reference's range-inverting defect** and confirmed three
predictions nothing had ever checked; §2 has what it found.
**Before it, step 011 was designed and planned** — [`claude/dev/011-query-optimizer/`](claude/dev/011-query-optimizer/)
(`DESIGN.md`, `PLAN.md`, `STATE.md`), with **ADR-52** and **ADR-53** closing ADR-46, corrections
to `claude/specs/QUERY-OPTIMIZER.md` (§3.4, §4.4, §7.3, §11.1 and §12 items 1, 2 and 8),
`claude/PORTING-PLAN.md` (§5's `QUERY` row and section, §8's benign divergences),
`claude/dev/README.md` and this file. **No `.cs` file was touched and no test was run** — the step is
at phase 5 of `DEV_APPROACH.md`, and phase 6 is the only one that writes code. `dotnet test` remains
where step 010 left it, green on **1909 tests**.
**Before it, step 010 closed** —
the expression engine in `net/CodeBase.Net/Expressions/` and the tag path wired through it. All seven
gate assertions hold and twenty-three mutations were run against them. `dotnet test` is green on
**1909 tests** (639 golden + 1270 unit), up from 1450, with **no golden expectation changed**.
**Step 010 is complete and `EXPR` is done.** An expression tag is an ordinary tag: **every key of
every expression tag in `CDXEXPR.cdx` is rebuilt byte for byte — 22 tags, 338 stored keys** — each
tag **walks the records the reference stored in its order**, and moving to a record by number
rebuilds that record's key and descends to it. **ADR-28's refusal is gone.** And the **thirteen
functions Visual FoxPro has that CodeBase refuses** are supported, gated by Visual FoxPro's own
answers in a VM (**ADR-43**, **ADR-44**) — 36 values on stored bits and 16 tag key lengths against
its own headers, which answers ADR-43's named risk.
**Every constant expression and every field expression at every record now evaluates to the
reference's own bytes**, and the compiled form is gated node for node against its `E4INFO`
arrays — row, length and the conditional's second length, plus each expression's result type and
trim marking, over all 212 expressions the reference compiled. **A second generator run closed the
gate's row coverage**: 46 expressions were added for rows nothing reached, so **144 of the 193 table
rows are oracle-gated and the other 49 are declared unreachable with a reason**. The corpus
regenerated byte-identical except the expression dump.
`net/corpus/CDXEXPR.expr.dump.txt` is therefore modified, and it is the only corpus file that is.
**Before that:** **`claude/specs/QUERY-OPTIMIZER.md`** was written, the eighth spec and the last gap,
and read back against step 010's design: `claude/PORTING-PLAN.md` (**R13 retired**),
`claude/ARCHITECTURE-DECISIONS.md` (**ADR-46**), `dev/010-expr/`'s `DESIGN.md` (**new §6**, and §2b
corrected), `PLAN.md`, `STATE.md`, and this file.
**Before that:** the VFP oracle — its scripts
in `test-files-generator/vfp/`, its output in `net/corpus/vfp/` — was committed together with the
documentation that names it, across `CLAUDE.md`, `FOR-DEVELOPERS.md`, `.gitignore`,
`ARCHITECTURE-DECISIONS.md` (**ADR-44**, **ADR-45**), `PORTING-PLAN.md`, `specs/EXPRESSIONS.md`,
`specs/CDX-FORMAT.md`, `net/corpus/README.md`, `dev/010-expr/` and this file. **The oracle has been
run** and no `.cs` file was touched.
**Step [`010-expr`](claude/dev/010-expr/) is under way and its first five sub-steps are done.** The
step is designed *and* planned, and **the oracle exists**: `net/corpus/CDXEXPR.*` plus
`FUNCTIONS.dump.txt` record what the C library's own parser and evaluator answer. The engine's first
five pieces are built against it: the function table, **gated row for row**; the scanner, gated on
every expression the C compiled and on the seven refusals it owns; the parser, gated on every compiled
node array's shape and on the seven refusals *it* owns; overload resolution, gated on every node's row
and length with **row coverage closed**; and the evaluator, gated on the reference's own result bytes
for every expression and every record. **Sub-step 5 was the step's designed stop point and it has been
reached.** **Next is sub-step 6**, the four functions that format text, which is where the corpus
leads because the source states no answer.
**The design changed shape twice on 08-28, and both changes are recorded as ADRs.** The gate is
now **two axes** — what a function *computes* (C#-authored semantics, tested over **constants**) and
what a value *becomes on disk* (stored key bytes, driven from the C library's recorded value so the
two can never cancel). And the engine takes an explicit date policy (**ADR-42**) and supports
**Visual FoxPro's functions** although CodeBase has none of them (**ADR-43**). Since 08-29,
**ADR-44** adds a **second offline oracle** — Visual FoxPro 9, in a VM — for what CodeBase cannot
generate at all; **its first script has been run and its output is committed**, and **ADR-45**
records the two divergences it decided.
**The headline finding: the reference has no arithmetic function at all.** 35 named functions, whole
numeric surface `+ - * / ** ^` — a key-building vocabulary, not an expression language. Beside it:
`STR` rounds **half away from zero on the decimal digits**, `CTOD` reads the century **off the system
clock**, `TRIM` returns the source width **NUL-padded**, and **promotion beats the exact-type
overload row**, which makes all six currency comparison rows dead.
`dotnet test` is green on **1909 tests** (639 golden + 1270 unit), up from 1450, with **no golden
expectation changed**.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read whole, its records can be read in an index tag's order, and the read path is
cached.** `net/CodeBase.Net.sln` builds six projects — `CodeBase.Net` (**no NuGet dependencies** by
design, ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden`, `CodeBase.Net.TestUtils`,
`CodeBase.Net.Benchmarks` and `CodeBase.Net.Profiling` — and `dotnet test` is green on **1909 tests**,
639 of them golden.

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

**`net/corpus/`** — thirteen cases, six of them indexed. Each table has a `<NAME>.dump.txt` and each
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
| `CDXEXPR.DBF` + `.cdx` + `.fpt` | `0x30` | **the expression case, step 010** — 16 records, **22 tags** whose keys are not bare fields, the corpus's only `H` field, a deleted record, and a tag over a nullable field. Beside the usual dumps it carries `CDXEXPR.expr.dump.txt`; `FUNCTIONS.dump.txt` sits next to it |

Beside them, `net/corpus/benchmark/PERF10K.DBF` + `.cdx` (610 456 + 304 640 bytes): a 10 000-record
table with a `C(20)` and an `N(12,2)` tag, **benchmark data and not a gate** — no `.dump.txt`, no golden
test, and invisible to `Corpus.TableNames`, which is not recursive.

And `net/corpus/vfp/` — **produced by Visual FoxPro 9, not by CodeBase** (ADR-44), one directory
down for the same reason as `benchmark/`: `VFPFUNC.DBF` + `.CDX`, 16 records and **18 tag groups of
which 17 are live tags**, one per function CodeBase refuses; and `VFPFVAL.DBF`, 77 constant
expressions stored in fields of their own type. The **files** are the oracle — the C library cannot
dump a VFP-built CDX at all — with `*.vfp.dump.txt` beside them as provenance.

Regeneration is byte-identical, index files included; the VFP corner's `.cdx` version counter is the
one exception, and is assert-and-mask.

**Documentation:** seven format specs **plus
[`QUERY-OPTIMIZER.md`](claude/specs/QUERY-OPTIMIZER.md)**, the porting plan, the development approach
and the decision log. `EXPRESSIONS.md` gained §5.1 (the 35-function set and the absence of
arithmetic) and §7.7 (the 47 comparison rows), and had §7.3's date-format claim corrected — it said
the default picture was set outside this source drop; it is at `C4CODE.C:1126`, which
`API-ERRORS.md` had cited all along.
**`claude/specs/QUERY-OPTIMIZER.md` now exists** (2026-09-03), so every in-scope subsystem has a
source-cited spec and **risk R13 is retired**. Step 011's design pass then corrected it in three
places: §4.4 gains the slot table showing **which two operator branches widen a short constant and
why exactly those**, plus the argument that the widening is *exact* rather than a safety margin;
§7.3 gains the finding that `log4true` reads the wrong two positions and is conservative as a result;
§11.1 and §3.4 now point at **ADR-52**; and **§12 item 2 is resolved** — the widening asymmetry is
not a second defect.
`EXPRESSIONS.md` §3 and §4 were then corrected against the scanner as it was ported: whitespace is
**three rules and not one** — only the space character counts where a *value* is expected, and the
spec had cited that very site for the opposite claim — an **empty parameter list is empty only when
nothing at all is between the brackets**, and a field name over ten characters is `e4unrecValue`.

---

## 2. This session (2026-09-04), the ones before it, and the four before those

**Step 011's sub-step 7 landed: a caller can run a query.** `ResidualFilter`, the public `Query`
with both skip strategies, `Table.Query`, `CodeBaseEngine.QueryTagScope` and `ErrorCode.Relate`.
**Depth 4 green on its first run** — 81 filters, record-number order and tag order, counts and
backwards walks — and with it **ADR-53's second divergence is proved**: the port's residual checks
strictly fewer terms than the reference's, because it asks the plan rather than reading two fixed
positions beside each term, and returns the same records on every filter.

**Three of five mutations did not redden, and that is the right shape rather than a gap.** The trust
rule fails in one direction only. Trusting a term no leaf answered loses the records that term would
have excluded (12 tests red), and a count that skips the residual keeps candidates that are not
answers (2 red). Trusting *nothing*, or refusing to split the outer conjunction, only makes the port
check terms the bitmap already answered — which can only agree with it, so no record moves and
nothing can see it.

**Which is why the fully-mapped short-circuit needed its own assertion**, and writing one found that
the per-term rule did not cover it: a filter rooted at an `.OR.` has its leaves recorded against the
comparisons inside it, so asking whether that one term is answered says no even when every part of it
is. Whole-filter and per-term are the same question at two scales, and the coarse one has to be asked
first.

**Two mutations were invalid before they were valid**, both caught by insisting a run distinguish a
build failure from a pass — one read a field while its own initializer was still running. A mutation
that does not execute proves nothing, and an empty failure list looks identical either way.

**One naming change:** the public class is `Query`, so the namespace is `CodeBase.Net.Queries` — a
class cannot share a name with its own namespace, the caller-facing name is the one a reader sees,
and `Expressions` beside it already sets the plural precedent.

**Before that in the same session — sub-step 6: two terms over one tag become one seek.** `ConstraintMerge`, a fold
pass in the decomposer, and a leaf that answers several terms rather than one. **Depth 3 stayed
green**, which is the claim merging has to make, and `MergeGoldenTests` counts leaves, which is the
claim no correctness check can see — a merge that is perfectly correct and never merges passes every
record-set assertion in the suite.

**The reorder paid off twice on the day it was meant to.** Depth 3 went **red the moment merging
landed**, and both causes were real. One helper conflated *which end wins* with *what a missing end
means*, so every conjunction came back too wide. And `TagProbe.KeyFor` returned searches over a
**shared buffer**, so a second call clobbered the first — nothing before merging ever held two
comparands at once, and a range with two ends is the first thing that does. The probe contract gained
the promise that would have caught it, which is the kind a stand-in passes by accident, because it
allocates, while an implementation careful about allocation fails.

**Where the reference shuffles, this groups.** It can only fold *adjacent* children, so it lifts
non-matching leaves into sibling branches until same-tag ones sit together; grouping by tag reaches
the same pairs with no shuffle, because a branch here is an array rather than a list to reorder.

**The corpus was extended to reach the disjunction side, and that was the most productive part.**
Seven filters were added to `CDXEXPR` (43 to 54; only its query dump changed). **The reference does
fold `.OR.` ranges, and its answers are exactly what the cautious fold produces** on all five shapes
— so Decision 9's refusal to build on the untranscribed `bmf4OrOt` now has its *answers* witnessed,
and §12 item 7 narrows a second time. A second filter puts **`doCheck`'s own case** in the corpus and
shows the port needs no such guard: seeking one end and testing the other reaches the same nothing.

**And extending found a gap, whose fix found a bug.** `K_NUM > 0 .OR. K_NUM = 0` did not fold,
because an exact value was only ever treated as sitting *inside* a range and never as extending its
edge. Reading it as a range whose ends have met removed three special cases rather than adding one —
and immediately broke everything, because the unbounded test looked only at the two ends and an exact
value keeps its value in its own field. Two exact values took the path meant for struck-out values
and came back as **everything**. Caught by the population test and depth 3 together.

**And a prediction in the plan was wrong, which the run said out loud.** Omitting the rule that moves
an inclusive bound past a value struck out at it was supposed to cost one record. It costs none: a
range walk that also drops excluded values drops this one whether or not the bound moved first. So it
is a cost and tidiness rule, not a correctness one, gated by the structural assertion and nothing
else. `QUERY-OPTIMIZER.md` §5.4 now says that rather than repeating the reference's implication.

**Before that in the same session — sub-step 5: the optimizer became complete and correct.** `QueryExecutor`
walks the plan, seeks each leaf and folds the children, and **depth 3 is green on all 74 filters
across three tables** — before any residual filtering, so a decomposition error cannot hide behind a
later pass, and before any merging, so what it measures is decomposition, seeking and combination
alone. The port seeks one tag twice where it will later seek once, which is the right answer reached
the slow way. **That is what the reorder after sub-step 2 was for**: the merge algebra lands next
against a gate that is already green, so anything it moves is its own.

**A spec correction that would have cost the whole gate.** `QUERY-OPTIMIZER.md` §6.4 quoted the
reference's own header comment approvingly — an `.AND.` map "marks as true all records which meet
**at least one** condition". The code beside it folds an `.AND.` branch with `f4flagAnd`, an
**intersection**. Built from the comment, the executor would union where it must intersect and return
far too many records. The comment's parenthesis is the true half: an `.AND.` branch is a superset
because an unoptimizable conjunct was dropped, and the residual check removes what it would have
excluded.

**One routine where the reference has four**, and the reason narrows an open risk. Every leaf starts
from a **freshly zeroed** bitmap, so the mode that picks between the and-side and or-side routines
chooses *how* a leaf's set is computed and never *what it means* — a leaf's set does not depend on
the connective above it. Now §6.5, and it **narrows §12 item 7**: `bmf4OrOt` being untranscribed
cannot affect evaluating a leaf, only merging two `.OR.` leaves on one tag.

**And one thing recorded rather than replicated.** A leaf proved empty short-circuits asymmetrically:
under an `.AND.` the set stays empty, which is right; under an `.OR.` **every record succeeds**, which
under the reference's own or-semantics would turn the query into the whole table. Reachability is not
settled and no corpus case reaches it, so §6.6 records it to stop anyone reproducing it deliberately.

**Before that in the same session — sub-step 4: a constraint reaches its records.** `ITagProbe`, `TagProbe`,
`RecordSet`, `ConstraintSeeker`, and the **first end-to-end signal** — 41 single-leaf filters (24 on
`CDXEXPR`, 9 on `CDXBASE`, 8 on `CDXDEEP`) reaching exactly the records the reference's bitmap holds.
The fake and the real probe are held to **one contract test run against both**, because a stand-in
that reported the end of the keys one entry early would make every layer-2 test above it confidently
wrong.

**The seam is stored order, not tag order**, and that is the whole of what `TagProbe` decides: keys
sort ascending whatever a descending tag's flag says, so stepping up in stored order steps that
cursor backwards. `CDXDEEP`'s `D_PFX` is descending and the only tag over its field, so the corpus
catches it — walking in tag order reddens both its golden tests.

**The range walk seeks one end where the reference seeks two.** The reference reaches two of its four
seek positions by inverting the tag and seeking forwards on the reversed view; this port seeks the
lower end and tests each key against the upper bound as it walks. Same records, one descent, no
inversion — and an empty range needs no rule at all, because the first key tested is already past the
upper bound.

**Four mutations reddened; the fifth found dead code.** Scrubbing the spare bits after a complement
changed nothing, because every read of the set goes through its range check — a bit outside one to
the record count is not a record however it came to be set. Removed, with the invariant left in one
place and a note for whoever later counts bits a byte at a time. The test that claimed to cover it
was renamed to what it actually catches.

**Before that in the same session — sub-step 3: a filter decomposes into a constraint tree.** `ConstraintValue`,
`LeafConstraint` as the closed union Decision 2 asked for, `ConstraintNode`, `QueryPlan` and a
`QueryDecomposer` grown from matching tags to building constraints. **Depth 2 green on all 74
filters, asserted unconditionally** — the reference's defect changes which *slot* a constant lands
in, not which *terms* were optimized, so `fullyMapped` agrees even where its records are wrong.

**Four of five mutations reddened; the fifth is inert, and that is a fact rather than a hole.**
Taking the evaluated width instead of the declared length — Decision 4's rule, and the thing that
decides whether a character constant is a prefix — changed **nothing**. Across all 74 filters the
corpus reaches **93 constants and the two numbers never differ**, because only six kinds of node are
constants at all (a number, a string, the four logical words, and the two functions that read a date
out of text) and each is compiled at its own natural width, so none truncates. The rule is kept
because it is right if they ever diverge, and a new assertion **pins the agreement** so that the day
they do, something goes red instead of quietly changing behaviour. The other four reddened as
designed: reflecting `>` the wrong way (eight tests), never widening a short constant, not clearing
`fullyMapped` on an unoptimizable conjunct, and an `.OR.` keeping the operands it could optimize.

**Nothing merges yet, deliberately.** `K_NUM > 0 .AND. K_NUM < 100` is two leaves that seek one tag
twice and intersect — the right answer reached the slow way, and what lets sub-step 5 gate the record
sets before any merge code exists to change them.

**Before that in the same session — sub-step 2: the port matches tags exactly where the reference
does.** Depth 1
green on its first run across all 74 filters and every node, and five mutations reddened it —
widening eligibility under `Compatible`, visiting a connective's operands forwards, an `.OR.` that
does not abandon its branch, dropping the field-loader row exemption, and dropping the field-identity
check. Restores from a checksummed copy, verified, never `git`.

**The decomposition walk had to land here rather than in sub-step 3.** `e4isTag` is called from
exactly two places, both inside `bitmap4can`'s comparison branch (m4map.c:165, 172) — never from a
sweep over the node array. Only one operand of a comparison whose *other* operand is a constant is
ever tested, and only for comparisons the walk reaches. So the tag array is a property of the
**walk**, and depth 1 cannot be satisfied without it.

**The sharpest pair in the corpus exists to gate exactly that.** `K_NUM > 0 .OR. K_LOG` and
`K_LOG .OR. K_NUM > 0` select the same fourteen records and neither is optimized — but one records
`T_NUM` and the other records nothing, because a connective's operands are visited **backwards** and
an `.OR.` dies at the first one it cannot use. A port that filled the array by testing every node
against every tag would fill both and look right on every other measure.

**A spec correction that would have cost three kinds of tag.** §3.4 said the type gate allows
`r4double`, `r4charBin` and `r4memoBin` — which is what the *comment* at r4log.c:71 says. The **code**
refuses four types and falls through on everything else, so **date, logical and binary-float tags are
eligible**. Four filters were added to witness it, and `K_DATE = STOD("20200101")` and
`EMPTY(K_TEXT) = .T.` both match a tag and are fully mapped. `STOD` is the clock-free way to write a
date constant, since `CTOD` takes its century from the system date. Same pattern as ADR-33: the code
is authoritative, the comment is noise.

**Before that in the same session — sub-step 1: the query oracle exists, and it overturned two things
on its first run.** `dump-query.cpp` sets each filter on a real `RELATE4` and reads what the optimizer decided out
of `RELATE4`/`RELATION4`; each filter is then run a **second** time with `RELATION4.bitmapDisable`
set, so the dump carries the reference's own scan truth beside its optimized answer. 43 filters on
`CDXEXPR`, 15 on `CDXBASE`, 12 on `CDXDEEP`. **All 48 pre-existing corpus files regenerate
byte-identical**, and two independent generator runs produce identical bytes.

**The defect is witnessed, and it is worse than a bound off by one.**
`K_INT > 0 .AND. 4 > K_NUM` returns **{3, 9, 11, 12, 14, 15}** optimized and **{5, 6, 8}** scanned —
**disjoint**. The bound was *inverted*: the dump shows the leaf holding the same raw bits as the
correct `K_NUM < 4` leaf with `isFlip` set, so the complement was taken, and the residual filter then
trusted the term because its operand had matched a tag. Written tag-first, or un-nested, the
reference is correct. **One filter of seventy** disagrees, and it is the one written to provoke it.
ADR-53's choice is evidence now, not inference, and `QUERY-OPTIMIZER.md` §12 item 1 is closed.

**Three more predictions confirmed, none previously checked:** an `H`-field comparison **never**
optimizes (§12 item 3 closed — the refusal comes from the constant side, as §3.2 read it); a
**skipped tag does not end the search**, since `K_DUP = "CHERRY"` matches the plain tag sitting
beside a unique one; and the **short-constant widening changes no records**, which is Decision 6's
"exact, not a safety margin" made a test and is what ADR-53's trust rule rests on.

**Two things the design had wrong, both corrected.** **`bitmapsFreed` is not "there is no bitmap"** —
it means bitmapping *failed*, and across all 70 filters it is **never set**, including the ten that
optimize nothing; a port treating it as the fall-back signal would read a condition that never
occurs. And **the go-around complement fires constantly** — 599 of `CDXDEEP`'s 600 records come back
as **one** raw bit — so depth 3 compares the *effective* set, not the stored bits. `DESIGN.md`
Decision 8 already said that; `PLAN.md`'s "bit for bit" was the loose wording and now says effective.

**Open question 1 answered by the dump rather than by reasoning:** the bitset survives the walk. It
is taken twice and compared, and `setStable` is yes for all 70.

**One thing the corpus still cannot isolate, and it is a decision rather than an oversight:** the
**filtered-tag refusal**. Every filtered tag in the corpus shares its field with a plain tag found
first, so no dump separates "refused" from "something else was found first". Isolating it needs a new
tag, hence a rewritten `.cdx` and every index dump with it — not worth that for one branch of a pure
predicate that sub-step 2 tests exhaustively at layer 1.

**Before that in the same session — step 011 was designed and planned.** `claude/dev/011-query-optimizer/`
holds `DESIGN.md` (five stages, seventeen classes, eleven decisions, four open questions) and
`PLAN.md` (four test layers, nine sub-steps, seven named mutations, two gates). No C# exists.

**Visual FoxPro is not an oracle for this step, and the reason is worth carrying.** ADR-44's rule is
that VFP witnesses a value it computes and the bytes it writes; the optimizer writes nothing, and
three of the four things worth recording are CodeBase's own internal state. The fourth, a record
sequence, VFP *would* answer — under **its** prefix `=` semantics, which the VFP oracle itself showed
run the opposite way from CodeBase's (ADR-45, ADR-50) and which this port deliberately does not
follow. A VFP result set would redden a correct implementation. **CodeBase is the only oracle here —
and it can be asked twice.**

**`RELATION4.bitmapDisable` is a plain struct member** (m4map.c:341), and setting it makes
`bitmap4can` return 0 at once, after which `log4true` re-evaluates every conjunct per record. So each
filter is dumped twice: the reference's optimized answer, and **the reference's own scan truth** with
the optimizer out of the circuit. That is **depth 0**, and it is what makes ADR-53 checkable —
on the `.AND.`-nested reflected comparison the reference's defect can invert a bound, so a *correct*
port is expected to disagree with `relate->set` there. Without a second reading the gate cannot tell
"we decompose differently" from "the reference is wrong here", and would have needed a hand-written
exception; with it, the disagreeing filters are **read from the dump, never listed in the C#**. It
also closes `QUERY-OPTIMIZER.md` §12 item 1, which had the `tagPos = 1` intent as inferred from four
source comments and never observed.

**The design pass found a much better oracle than the plan assumed, and it changed the gate's
shape.** `RELATE4` and `RELATION4` are **public structs in `R4RELATE.H`**, so the generator can dump
what the reference decided at **four depths** instead of only comparing final record sets:
`log.infoReport[i].tag` per node, `relation->fullyMapped`, **`relate->set` — the candidate bitset
before any residual filtering** — and the `relate4top`/`skip`/`count` sequence. Depth 3 is the one
that matters: it compares the set a decomposition error cannot hide from, because no residual pass
has run yet to remove the same records by accident. Beside it runs **full-scan equivalence**, which
needs no reference at all, on every case, always.

**Two decisions were taken and are ADRs rather than notes.** **ADR-52**: tag eligibility replicates
CodeBase's four refusals by default (`QueryTagScope.Compatible`) and widens the **type and collation**
gates under `AllSeekable` — the filter and unique gates are **never** widened, because a filtered tag
indexes a subset of the table and a unique tag stores one key per duplicate group, so both are
structural. The default is what keeps three of the four gate depths meaningful, since the reference is
an oracle only where both sides used the same tags. **ADR-53**: the reference's
`if ( tagPos = 1 && constPos == 0 )` is an assignment C# cannot even compile, so the port implements
the **intent**; and the residual filter trusts a term when decomposition produced a leaf for it, not
by reading two fixed positions. Both **close ADR-46**, which was open on exactly them.

**Reading the C settled two things the spec had left open or wrong.**

- **`QUERY-OPTIMIZER.md` §12 item 2 is resolved and is not a defect.** The partial-string widening
  looks asymmetric because only two of six operator branches apply it — but those two are exactly the
  two that write the `gt` slot, and they are the only two that write it (m4map.c:211-283). Only an
  exclusive *lower* bound can wrongly drop a prefix group. And the widening is **exact rather than a
  safety margin**, because `EXPRESSIONS.md` §8's tie-break — which the corpus found in step 010 —
  already makes `"SM" + 18 blanks > "SM"` true. That is what makes a widened leaf safe to trust with
  no residual re-check, so it is load-bearing for ADR-53 and not a footnote.
- **`log4true` reads the wrong two positions.** It tests the nodes at `-1` and `-2` from a conjunct's
  root, which are the operand roots only when the right operand occupies a single node (r4log.c:770).
  Where it does not, the reference **re-evaluates a term its own bitmap already answered** — slower,
  never wrong. Now in the spec's §7.3.

**And a third gate, because the first two are blind to the thing the step is for.** A `QUERY` that
decomposes nothing and scans every time is **perfectly correct** — a null bitmap answers yes for
every record (r4relate.c:82-91) — so Gate A compares a scan against a scan and passes. That is
**ADR-40's failure repeating in a second module**, and it takes ADR-40's answer: gate the speed-up
itself, and **count rather than time**, because a read count is exact and identical on every machine.
Gate C is five assertions, three of them exact numbers rather than thresholds: a fully-mapped `Count`
reads **zero** records (the reference's own headline claim, r4relate.c:6780-6781), a fully-mapped
query runs **zero** residual evaluations and reads **exactly** what it returns, `x > 5 .AND. x < 10`
compiles to **one** leaf — leaf count is seek count, and the merge algebra's stated purpose is that
the tag is seeked once (C4CONST.C:300-307) — and a selective query's block reads are a small fraction
of a scan's. Step 009 already left the instrumentation: `CountingSource` plus `CodeBaseEngine`'s
factory constructor. Two of the plan's mutations exist only for this gate, and **Gate A stays green
under both of them**.

Separately measured and deliberately **not** gated: a selectivity sweep over `PERF10K` locating where
the optimizer stops beating a scan. That is the measurement that would reverse Decision 8's omission
of the go-around complement — with a number rather than a preference. `PERF10K` stays benchmark data.

**What step 010 already discharged, so step 011 does not.** `OptimizerContractGoldenTests` gates all
four things `QUERY-OPTIMIZER.md` §3 asks of the compiled form. Step 011 owes structural equality
between two node arrays, tag eligibility, the `BITMAP4` tree, the merge algebra, the four seek
positions, the `F4FLAG` bitset, the residual filter and the public surface — and nothing else.

**One housekeeping defect was fixed.** Step 010's close wrote its `claude/dev/README.md` row into the
**renumbering table** rather than the steps table, so the index still said 010 was "in progress —
sub-steps 1-2 of 8". Both rows are now what they should be.

### The five sessions before it

**Step 010's sub-step 5 landed: the evaluator, and the step is at its designed stop point.**
`net/CodeBase.Net/Expressions/` gained `ExpressionValue` (the closed union over the ten types),
`ExpressionOptions` (**ADR-42**, made real), `EvaluationContext` and `ExpressionEvaluator`. Beside
them `FoxDate` gained the julian-to-text direction the reader never needed, and `Table` an internal
`CurrentRecord` that sub-step 8 needs anyway. **Both gates are green:** the layer-1 semantic table is
**168 spec-authored cases**, and layer 4 confirms it on **every one of the 131 constant expressions
and all 77 field expressions at all 16 records**.

**Two families are deliberately outstanding**, deferred by a rule computed from the rows rather than
a list of names, so finishing one empties it: the four that format text are sub-step 6's, and
ascending and descending conversion are sub-step 7's because they produce key bytes. `DTOS` moved
from 6 to 5, because it is the julian-to-text conversion `DAY`/`MONTH`/`YEAR` need anyway.

**The corpus caught two things, and the second is the interesting one.** A memo operand is
**NUL-padded, not blank-padded** — which `specs/EXPRESSIONS.md` §11 already said and I had misread;
it matters because key time turns trailing zero bytes into blanks, so a memo operand keys as though
trimmed. And **the inclusive comparisons do not break a tie on width while the strict ones do**: the
tie-break flag is assigned only for the two *named head* rows, and `<=` calls the greater kernel on
its own node where that flag was never written. So `"AAAA" > "AAA"` and `"AAAA" <= "AAA"` are
**both true**. Now in `specs/EXPRESSIONS.md` §8 with a table, and §7.4b gained `EMPTY`'s five rows
and four answers.

**Only two records in the whole corpus distinguish that**, and no constant expression could have:
a constant comparison has two operands of equal width, so the tie-break never fires. That is the
argument for the field half of the value gate, made concrete.

**Three things are owed and named rather than skipped quietly:** the Windows ANSI upper-case mapping
(the corpus witnesses only the ASCII path, and a 1252 table with an `UPPER` tag would be a new
generator case); `<>` over one absent operand; and ADR-42's other three language presets, which are
a picture string each and gated by nothing.

**Five mutations, and every one reddened both layers** — prefix equality over the full width, a trim
padding with blanks, the inclusive forms breaking a tie, division by zero raising, and date
arithmetic not short-circuiting on a blank date. That the layer-1 table reddens too is the point: it
is a specification and not a restatement of the implementation.

**Before that in the same session — step 010's sub-step 4: overload resolution, and the compiled form is now frozen.**
`net/CodeBase.Net/Expressions/` gained `TypeResolver` and `CompiledExpression`; `ExpressionNode`
gained its result type, the conditional's second length and the row rewrite a promotion is; two more
`ErrorCode` members arrived (−440, −460). **The resolution gate passed on its first run** — all 212
expressions, every node's row and length, each expression's result type and trim marking.

**A second generator run was needed, and it was needed exactly where the plan said.** The gate's own
assertion 2 requires every table row to be oracle-gated or declared unreachable, and on the first
reading only **98 of 193** were reached; 46 of the rest were reachable and simply absent, which means
a wrong overload could have been selected into them with nothing to compare against. 46 expressions
were added to `case-cdxexpr.cpp`, **every one landed on the row it was written for**, and coverage
went to **144 gated plus 49 declared unreachable** — 39 naming a field type this library does not
open, 6 the currency comparisons that are dead in the reference itself, 4 the report writer's and the
terminator. **The corpus regenerated byte-identical except the expression dump**, which is what
adding expressions rather than tags buys, and both corpus READMEs now say the expression lists are
sized by row coverage.

**The one thing the design had wrong.** `DESIGN.md` §2b said a promotion "changes a node's row and
its length". The length assignment is to the *simulated stack*, not to the node — the producer's own
length was written when the walk visited it and the walk never returns. So **a promoted numeric field
still reports the field's width**: `K_NUM + 0`'s field node is row 8 with length 12, not 8, which the
oracle shows directly and which the query optimizer will read. Corrected in `DESIGN.md` and recorded
in `specs/EXPRESSIONS.md` §6, and there is now a mutation that proves it.

**Four mutations, and the one that matters most reddened as designed:** wrapping a promoted producer
in a conversion node instead of rewriting its row turns the resolution gate red **on sequence
length**. That is the shape the design's own earlier draft would have shipped, and it is why the gate
is a sequence equality rather than a per-node lookup. One restore went wrong — the `cp` ran with a
working directory an earlier `cd` had changed — and was caught by the checksum and redone.

**Before that in the same session — step 010's sub-step 3: the parser, and it is gated as a shape.** `net/CodeBase.Net/Expressions/`
gained `FunctionRows`, `FieldReference`, `IFieldMetadata`, `TableFieldMetadata`, `ExpressionConstant`,
`ExpressionNode`, `ParsedExpression` and `ExpressionParser`, plus `FunctionTable.FindByName` and five
more `ErrorCode` members (−210, −400, −425, −430, −460, −470).

**The parse pass is bigger than the plan's one line said**, and reading `e4parse.c` before writing any
of it is what showed that: `expr4parseLow` runs the shunting-yard to completion and *then* calls
`e4massage`, and five behaviours sit on the parse side of that line. A field's loader row is chosen
from the field's type — which is the new `IFieldMetadata` seam `DESIGN.md` §Seams had not listed. The
parameter-count check is parse-time, and happens **twice** with two different conditions. `DTOC` and
`TTOC` given two parameters are **rewritten, not refused**. Nine functions read a constant parameter
and **remove its node**, so `STR(K_NUM,12,3)` is **two** nodes and not four. And a top-level `)` or
`,` ends the expression with the rest of the text ignored, so `K_LOG)` and `K_TEXT,K_CITY` compile to
their first term, as they do there.
`DESIGN.md` §2b, `PLAN.md` sub-step 3 and `specs/EXPRESSIONS.md` §4 were all updated in place.

**And that finding is what made the sub-step gateable before any type exists.** Promotion rewrites a
node's row and its length and adds no node, so `numParms`, `numEntries` and `fieldNo` are final the
moment the parser returns — and the dump records all three per node. So the layer-4 gate is a
**shape** equality against the C's own `E4INFO` arrays: node count plus that three-field sequence,
over all 162 expressions the C compiled. It passed on the second run; the one failure was a wrong
error code, not a wrong shape. `numEntries` is computed in the parser here and in `e4massage` there,
a divergence in *where* and not in *what*, and it is what buys the gate.

**Two reference behaviours a sensible parser gets wrong**, both now tested. `STR` and `STRZERO`
report a non-constant width as **`e4typeSub` (−460)**, while the other eight report `e4notConstant`
(−425) — deliberate in the C, and the corpus records both. And a parameter list is **re-tested at
every comma**, not only at its opening bracket, so `UPPER(K_TEXT,)` is `UPPER(K_TEXT)` with one
parameter; that **corrected the scanner**, whose flag had only applied at the bracket.

**A corpus gap the mutations found.** Four mutations were run; three reddened the shape gate by name.
The fourth — stop counting a variadic operator's repeats — reddened **layer 1 only**, because
`CDXEXPR` holds no run of three or more identical `.AND.`/`.OR.` operators. The variadic collapse
that `QUERY-OPTIMIZER.md` §4.1 depends on has no layer-4 oracle today, and the second generator run
the plan budgets for should add one.

**Before that in the same session — step 010's sub-step 2: the function table and the scanner.** `net/CodeBase.Net/Expressions/`
now holds `ExpressionType`, `FunctionRow`, `FunctionTable`, `ExpressionTokenKind`, `ExpressionToken`
and `ExpressionLexer`, plus four new `ErrorCode` members (−450, −480, −490, −500). Two golden
classes gate them and one unit class covers the scanner's decisions as a table of token streams.

**The plan's sub-steps 2 and 3 swapped their first halves**, and `PLAN.md` says so. An operator is
not tokenized and then looked up: it is **prefix-matched against `v4functions` rows 36-106 in row
order**, first match wins (`e4parse.c:1195`). `>=` beats `>` only because row 60 precedes row 84. So
the table has to exist before the scanner, and the transcription gate lands a sub-step earlier — which
the plan's own ordering rationale wanted anyway.

**The transcription gate passed on the first run**, 193 rows, every field, including the reference's
own inconsistencies (unnamed rows carrying a name length, the wide-string `STR` overload declaring a
length of one). The two readings are genuinely independent: the port typed from `e4functi.c:32-301`,
the dump walked out of the compiled library. **Three mutations were run and all three reddened by
name** — a swapped parameter type, a changed priority, and `PLAN.md`'s `-FIELD` acceptance.

**The scanner is a self-driving two-state pull scanner**, because the language needs the state: a
minus begins a literal where a value is expected and is subtraction where an operator is expected. One
extra flag — *what comes next is the start of a parameter* — reproduces two more behaviours exactly:
`DATE()` scans and `DATE( )` does not, and `UPPER(` is `e4rightMissing` and not `e4unrecValue`.
**Seven of the eighteen recorded rejections are the scanner's**, and the gate asserts both halves —
those seven carry the C's own code, the other eleven **scan cleanly**, because refusing them here
would answer at the wrong pass.

**Three spec corrections came out of it**, all in `specs/EXPRESSIONS.md`: whitespace is **three
rules, not one** (only the space character counts before a *value*, so a tab there is
`e4unrecValue`); an **empty parameter list is empty only when nothing at all is in it**; and a field
name over ten characters is `e4unrecValue`, which also means the dump's comment on `NOSUCHFIELD`
names the wrong cause.


**A second oracle exists on paper: Visual FoxPro 9, in a VM the user can run `.prg` files in.**
That makes ADR-04's premise false — it declined a VFP VM because none existed and the questions
were few — so **ADR-44 supersedes it** and ADR-11 is unblocked though still open. VFP can witness
exactly two things, a value it computes and the bytes it writes; CodeBase stays the only authority
on its own internals (the rejection surface, the error codes, the compiled node arrays) and on the
extensions VFP cannot write, so **ADR-23 is unaffected**.

Written **and run** this session: `test-files-generator/vfp/vfpfunc.prg`, which builds
`VFPFUNC.DBF` + `.CDX` with **18 tags** — one per VFP-only function, plus `TRIM`, `STR` and `PROPER`
as divergence probes and `UPPER` as a control — and `VFPFVAL.DBF`, **77 constant expressions** whose
results are stored in fields of their own type so a numeric answer is bits rather than decimal text.
Its output is committed in **`net/corpus/vfp/`**, a subdirectory `Corpus.TableNames` does not sweep,
published by `copy-vfp-corpus.bat`. Two decisions inside it are worth knowing: the DBF header stamp
is frozen to `26-01-01` exactly as the C generator freezes it (ADR-07), and **the trailing `0x1A`
that VFP writes and CodeBase does not is deliberately kept** — it is a reader-tolerance case the
corpus never had.

**What the second oracle answered, in the order it matters.** Full list in
[`test-files-generator/vfp/README.md`](test-files-generator/vfp/README.md); the durable form is
`specs/EXPRESSIONS.md` §9.1 and `specs/CDX-FORMAT.md` §3.

- **ADR-43's named risk does not materialise.** Key lengths read out of the tag headers VFP wrote:
  every numeric-returning function **8**; `LOWER`/`PROPER`/`TRIM`/`UPPER` over a `C(20)` **20**;
  `REPLICATE(k_text,2)` **40**; `STR(k_num,12,3)` **12**. The same rule the port would compute.
- **`STRTRAN` fails at build time, not parse time.** VFP sizes a value-dependent result at the
  **operand's** width, writes the header, then errors on the first row that does not fit — and
  **leaves the failed tag's three blocks in the file**, well-formed and unreferenced. So the tag
  directory is the only authority on which tags exist; scanning for header blocks finds eighteen
  where `TAGCOUNT()` says seventeen.
- **`TRIM` does not diverge in length**, which was the one divergence that would have broken
  *reading* a VFP-written tag. **`STR` rounding agrees**, closing ADR-43's own open question.
- **The oracle overturned a document rather than confirming it, for the first time.** Prefix `=`
  runs the **opposite** way from Sequiter's own comment at `e4parse.c:59-62`: VFP compares to the end
  of the *right* operand, so `"AAAA" = "AAA"` is true and `"AAA" = "AAAA"` is false. That comment had
  been repeated into `EXPRESSIONS.md` §8 and `DESIGN.md` Decision 8 on its authority; all three are
  corrected, and **ADR-45** settles that the port replicates CodeBase (both true) — which makes
  `NAME = "SM"` a **prefix range** for `QUERY`, not an equality.
- **The century pivot is a second divergence of the same kind.** VFP has a rollover
  (`yy < nYear` → `nCentury + 1`, inclusive at `nYear`); CodeBase has none at all. On a 2026 clock
  `CTOD("01/15/99")` is **2099 in CodeBase and 1999 in VFP**. VFP's default rollover is the machine's
  year + 50 mod 100, so the script now pins it — unpinned, those rows churn every January.
- **Two incidentals worth knowing before reading the bytes:** VFP **lower-cases identifiers** in the
  stored expression text (`INDEX ON ABS(K_NUM)` → `ABS(k_num)`) while `KEY()` returns them
  upper-cased; and `STRZERO` is **not a VFP function**, so two `VFPFVAL` rows are refusals.
- **One scope call, not a defect.** The VM's code page is **1251**, so these two tables carry mark
  `0xC9` where the rest of the corpus carries `0x00`. `MACHINE` ordering is unaffected; case
  conversion above `0x7F` is that code page's, and record 16 is the only row that reaches it.

**Why the file is the artifact and the text dump is only provenance:** the C library cannot dump a
VFP-built CDX at all. Opening a tag parses its expression (`i4ntag.c:1390-1395`), so a tag over
`LOWER(…)` fails to open and the existing dumper never sees it.

**Last session, 2026-08-28: step 010's sub-step 1 landed — the expression oracle.** Three new generator files
(`dump-expr.h/.cpp`, `case-cdxexpr.cpp`), seven new corpus files, **no C# of the engine**. The
existing corpus regenerates **byte-identical**; the only C# touched was five inventory assertions
that the corpus's own policing fired on, plus one guard in `CachedReadGoldenTests` that skips tags
whose expression is not a bare field — ADR-28's refusal, and **sub-step 8 deletes that guard**.

**The gate was redesigned before any of it was built, on the user's objection**, and this is the
change most worth carrying forward. The first draft made the C dump the oracle for everything, on a
misreading of `CLAUDE.md` — which bars hand-written expected **bytes**, an on-disk fact, not
`UPPER("abc") == "ABC"`. It is now **two axes that fail differently**, split where the reference
itself splits (`expr4vary` returns a value, `expr4keyConvert` turns it into key bytes):

- **Axis A, what a function computes** — C#-authored expectations at layer 1, **tested over
  constants** so the expression *is* the case and no failure can be a field-decode bug in disguise.
  Fields appear only for the four types no literal reaches (`r4num`, `r4date`, `r4int`,
  `r4currency`), which are also the four **no function returns**.
- **Axis B, what a value becomes on disk** — stored key bytes only, and **driven from the C library's
  recorded value rather than from our evaluator**, so a wrong kernel and a wrong transform can never
  cancel on the tags the corpus happens to hold.

**What the first run answered, having been listed as unknown:** `STR` rounds **half away from zero on
the decimal digits** (`0.135` → `0.14`, though the nearest double is below it) and overflows to `***`;
the default `dateFormat` picture is **8 characters** and a compile-time constant, not a locale
(`C4CODE.C:1126`); `r4floatBin` keys to **4** bytes; **a tag over a logical expression exists and its
key is 1 byte**, so `KeyTypeResolver`'s refusal of `'L'` goes; the null indicator is one byte marked
by `typeCode` bit **0x02**; and `TRIM` returns the **source width, NUL-padded** — a C#-authored guess
would have said three bytes for `TRIM("abc   ")`.

**Three things the oracle found that no amount of reading would have:**

- **`CTOD` has no century pivot — it reads the clock** (`D4DATE.C:580-601`). Every two-digit year
  becomes the current century, so **no twentieth-century date is reachable through `CTOD`**. That
  made nine cases clock-dependent, which is why the dump now records `dateFormat` and `century` in
  its header and why **ADR-42** exists.
- **Promotion beats the exact-type overload row.** `K_CUR > K_CUR2` resolves the `n,n` row, not
  `Y,Y`, because `r4currency → r4numDoub` is tried first — so **all six currency comparison rows are
  dead**, corroborating `EXPRESSIONS.md` §14 risk 8 from a second direction.
- **No `f4assign*` reaches `r4floatBin`.** The first `CDXEXPR` stored the ASCII text `"0.00"` in its
  `H` field; the type appears in exactly one assignment path in the library (`f4ass_f.c:99`). Left
  unfixed, the port's only `r4floatBin` gate would have asserted against text.

**And one contradiction the rejection dump caught on day one:** `DESIGN.md` §3 lists "a TRIM-family
result may not feed `SUBSTR`" among the parser's restrictions, on the authority of a source comment.
`SUBSTR(TRIM(K_TEXT),1,2)` **parses**. Recorded as unconfirmed rather than false, because the dump
parses with no tag context — and *that* is now a named gap: `e4tagExpr` (−510) can never appear until
a second rejection pass runs with a real `TAG4FILE`.

**Before it, 2026-08-27, step 009 closed** — both halves, with ADR-36 to ADR-41. §1 has what it
landed; [`SUMMARY.md`](claude/dev/009-performance-audit/SUMMARY.md) has what it cost. Three things
worth carrying: every gate passed while the step was wrong about where its time went **three times**,
and what found it was **counting, not timing**; ADR-35 generalises to *let the read be the refusal —
unless the value being checked sizes an allocation*; and restoring after a mutation check is done
from a checksummed copy, **never** from `git`.

**Two consequences of the 2026-08-19 renumbering, worth knowing before reading a `git log`:** the
older unpushed commits say `007-seek-by-value`, which was the folder's name when they were written —
their content is step 008's; and `007-audit-glm/REMEDIATION-PLAN.md` §2.2 and §2.3 are out of order,
because it planned to measure before `COLLATION` and the reverse happened.

## 3. Next

### The VFP oracle's second run, whenever it is convenient — it blocks nothing

The first run is committed and its findings are recorded. Three probe rows were changed *after* it,
so the script and the committed corpus disagree on three lines until someone re-runs
`DO vfpfunc.prg` and `copy-vfp-corpus.bat`: the `CTOD` rows now straddle the pinned `ROLLOVER 29`
(`01/15/28` and `01/15/29` replace `29`/`30`) so a single dump proves the rule that currently takes
two runs to see. Nothing depends on it — the rule is already in `specs/EXPRESSIONS.md` §7.3.

The runs ADR-44 authorises next are separate scripts, none written: code-page marks beyond
CodeBase's five constants, a VFP 9 `0x32` table with a memo, `V`/`Q`/`W` fields, native
auto-increment, a DBC backlink, and an insert-and-split tree with a non-empty free list — the last
two being ADR-11's, which is unblocked but still open.

### Step 011 is under way: sub-steps 1 to 7 are done, and a caller can run a query

**Next is sub-step 8 — the equivalence gate, the widened scope and the efficiency gate**, and it
needs **no generator run**. **Gate A**: for every filter, the optimized record sequence must equal a
brute-force scan of the same compiled expression, in both scopes — the check that needs no reference
at all and is the only one `AllSeekable` has, since a widened tag has no reference answer by
construction. **Gate C**: `CountingSource` behind the engine, and the four counted assertions of
`DESIGN.md` Decision 13 — a fully-mapped count reads **zero** records, a fully-mapped query runs zero
residual evaluations and reads exactly what it returns, and a selective query's block reads are a
fraction of a scan's. Gate C is the only thing that can see an optimizer that is correct and never
optimizes.

**The merge algebra moved from sub-step 4 to sub-step 6.** The first plan gated sub-step 3 on
`FullyMapped` alone — one bit per filter, which cannot see a swapped bound — and then left the
operand-order reflection unchecked for four sub-steps. That is R12's most concrete instance and the
one thing the reference itself gets wrong, so it was the plan's weakest point.

What makes the reorder work is that **merging is an optimization, not a correctness requirement**:
unmerged, `K_NUM > 0 .AND. K_NUM < 100` decomposes into two leaves that seek separately and
intersect, which is the right answer reached by seeking one tag twice. So decomposition, seeking and
combination are a complete optimizer on their own, and **depth 3 can be green on all 74 filters
before any merge code exists**. The twelve routines of merge case analysis then get written against a
green end-to-end gate, with a sharper gate of their own: the record sets must not move, and the leaf
count must fall. Those two fail differently — a wrong merge fails the first, a merge that never
happens fails the second.

Order now: 3 decomposition, 4 leaf evaluation, 5 combination (**depth 3 green here**), 6 the merge
algebra, 7 residual filter and public surface, 8 the equivalence and efficiency gates, 9 close.

**Three of `DESIGN.md`'s four open questions are closed** by sub-step 1's run: the bitset survives
the walk, a filter with nothing optimizable records `set absent` (and `bitmapsFreed` is never set,
which that question had assumed the opposite of), and no wider corpus table is needed. Only
`e4tagExpr` (−510) stays open, with the rejection pass step 010 owes.

**Still owed to the corpus, and deliberately not folded into this run:** the second rejection pass
with a real `TAG4FILE`, so `e4tagExpr` (−510) and the alias refusals stop being unobservable. It is a
change to the *rejection* dump rather than the query one, and mixing the two would have made this
run's byte-identical check ambiguous. The three-or-more `.AND.`/`.OR.` run that step 010 also owed
**is** now covered, by two query filters.

### Step 010 is closed; what came before

**`EXPR` is done and step 010 needs nothing further — all nine sub-steps.** An expression tag is an
ordinary tag, every key of every one in the corpus is rebuilt byte for byte, Visual FoxPro's thirteen
extra functions are supported and gated by Visual FoxPro, and `PORTING-PLAN.md` §5 records the
capability closed and **R5 retired**. One thing is owed and named: the Windows ANSI case mapping,
which no corpus table can witness. The step's record is [`SUMMARY.md`](claude/dev/010-expr/SUMMARY.md).

**`QUERY` is step 011, now designed and planned above.** It is the bitmap optimizer, what the whole
port exists for, specified in [`specs/QUERY-OPTIMIZER.md`](claude/specs/QUERY-OPTIMIZER.md). Step 010 built it
the compiled form it walks and **gated the contract**: the node table with all six fields per node,
evaluation **at a node** with no record for a constant subtree, the per-node is-constant predicate
checked against the evaluator's own behaviour rather than against a copy of its rule, and a `Tag`
that **keeps** its `CompiledExpression` — the same instance the key path builds keys from. What is
left for step 011 is
structural equality between two node arrays, the `BITMAP4` tree, the constraint-merge algebra, the
four seek positions and the `F4FLAG` bitset. **Read `DESIGN.md` §6 first**: writing
[`specs/QUERY-OPTIMIZER.md`](claude/specs/QUERY-OPTIMIZER.md) found that §2b's promotion-as-a-wrapper-node
would have failed sub-step 4's own gate — promotion is an **in-place row rewrite** and node count is
invariant under it (`e4parse.c:227-289`) — and added three things to the compiled form that are cheap
before that gate freezes it and expensive after: the **retained node table** (six fields per node),
**evaluate-at-a-node with no record for a constant subtree**, and a `Tag` that **keeps** its
`CompiledExpression`. `dev/010-expr/STATE.md` has the detail; **ADR-46** has why it lands in this step. Scope is
the **whole** function table, because `QUERY` needs the filter vocabulary anyway. Nothing about the
engine has been written; every layer-4 gate it will be measured by already exists in the corpus.

The plan's own ordering, and the reason for it: the **function table transcription** (now sub-step 2,
and green) had to be right before **row selection** (4) can mean anything, and the row must be right
before a **value** (5) is worth comparing — a wrong row produces a *plausible* value. **Axis B lands complete
at sub-step 7**, driven from recorded values, so **sub-step 8 is pure wiring** and the only sub-step
that can turn an existing test red. The stop point after sub-step 5 is real.

**Things owed to the corpus before the C# gets far:**

- **A second rejection pass with a real `TAG4FILE`.** Every rejection is currently parsed with
  `tagPtr = 0`, so `e4tagExpr` (−510) never appears and `OTHER->K_TEXT` comes back as `e4fieldName`
  (−210). The tag-only refusals — an alias other than the indexed table, and whether the
  `SUBSTR`-over-`TRIM` restriction bites — are unobservable until then.
- **A run of three or more `.AND.`/`.OR.` operators**, over constants and over fields. The whole dump
  holds only two-term runs, so the variadic collapse `QUERY-OPTIMIZER.md` §4.1 depends on is gated by
  C#-authored tests alone — found by a mutation at sub-step 3 that layer 4 did not catch.
- **`CachedReadGoldenTests`'s `KeysABareField` guard is deleted at sub-step 8**, along with ADR-28's
  refusal. It is the one place the suite currently averts its eyes.

### Two things that depend on nothing

**~~Write `claude/specs/QUERY-OPTIMIZER.md`~~ — done 2026-09-03**, and **~~its three generator
cases~~ are now step 011's sub-step 1**. R13 retired. The three, listed in the spec's §12: a
**reflected comparison nested in an `.AND.`** (`4 > K_NUM` beside `K_NUM < 4`, which is where the
reference's `tagPos = 1` defect could invert a bound), an **`H`-field comparison** (predicted
un-optimizable because `E4FLOAT` is missing from `e4isConstant`, and `CDXEXPR` has the corpus's only
`H` field), and an **OR range** (`bmf4OrOt` was read for shape, not transcribed). **ADR-46** carries
the two decisions it surfaced — whether the port keeps CodeBase's tag-eligibility refusals, and what
to do about the defect — and the finding that `EXPR` constrains `QUERY` at sub-steps 3-4, not later.

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
