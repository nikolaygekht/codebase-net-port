# Project state

**Updated:** 2026-09-03 · **the tree is clean and two commits are unpushed.** The second of them is
documents only — **`claude/specs/QUERY-OPTIMIZER.md`** is written, the eighth spec and the last gap,
and it was read back against step 010's design: `claude/PORTING-PLAN.md` (**R13 retired**),
`claude/ARCHITECTURE-DECISIONS.md` (**ADR-46**), `dev/010-expr/`'s `DESIGN.md` (**new §6**, and §2b
corrected), `PLAN.md`, `STATE.md`, and this file. **No `.cs` file was touched.**
**Before that:** the VFP oracle — its scripts
in `test-files-generator/vfp/`, its output in `net/corpus/vfp/` — was committed together with the
documentation that names it, across `CLAUDE.md`, `FOR-DEVELOPERS.md`, `.gitignore`,
`ARCHITECTURE-DECISIONS.md` (**ADR-44**, **ADR-45**), `PORTING-PLAN.md`, `specs/EXPRESSIONS.md`,
`specs/CDX-FORMAT.md`, `net/corpus/README.md`, `dev/010-expr/` and this file. **The oracle has been
run** and no `.cs` file was touched.
**Step [`010-expr`](claude/dev/010-expr/) is under way and its first sub-step is done.** The step is
designed *and* planned, and **the oracle exists**: `net/corpus/CDXEXPR.*` plus `FUNCTIONS.dump.txt`
record what the C library's own parser and evaluator answer — 987 assertions in
[`ORACLE-SUITE.txt`](claude/dev/010-expr/ORACLE-SUITE.txt). **No C# of the engine is written yet**,
by design: sub-step 1 is the gate, and sub-steps 2-8 build against it.
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
`dotnet test` is green on **1357 tests** (590 golden + 767 unit), up from 1333.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read whole, its records can be read in an index tag's order, and the read path is
cached.** `net/CodeBase.Net.sln` builds six projects — `CodeBase.Net` (**no NuGet dependencies** by
design, ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden`, `CodeBase.Net.TestUtils`,
`CodeBase.Net.Benchmarks` and `CodeBase.Net.Profiling` — and `dotnet test` is green on **1357 tests**,
590 of them golden.

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
**`claude/specs/QUERY-OPTIMIZER.md` now exists** (2026-09-03, 911 lines, 171 cited lines), so every
in-scope subsystem has a source-cited spec and **risk R13 is retired**.

---

## 2. This session (2026-08-29), the one before it, and the three before that

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

### Step 010 continues at sub-step 2 — where the next session starts

**Sub-step 1 is done, the oracle is committed, and the design now carries the optimizer's contract.**
The next session opens [`PLAN.md`](claude/dev/010-expr/PLAN.md) at **sub-step 2, the lexer**, and
works forward. **Read `DESIGN.md` §6 before sub-step 3**: writing
[`specs/QUERY-OPTIMIZER.md`](claude/specs/QUERY-OPTIMIZER.md) found that §2b's promotion-as-a-wrapper-node
would have failed sub-step 4's own gate — promotion is an **in-place row rewrite** and node count is
invariant under it (`e4parse.c:227-289`) — and added three things to the compiled form that are cheap
before that gate freezes it and expensive after: the **retained node table** (six fields per node),
**evaluate-at-a-node with no record for a constant subtree**, and a `Tag` that **keeps** its
`CompiledExpression`. `dev/010-expr/STATE.md` has the detail; **ADR-46** has why it lands in this step. Scope is
the **whole** function table, because `QUERY` needs the filter vocabulary anyway. Nothing about the
engine has been written; every layer-4 gate it will be measured by already exists in the corpus.

The plan's own ordering, and the reason for it: the **function table transcription** (sub-step 3)
must be right before **row selection** (4) means anything, and the row must be right before a
**value** (5) is worth comparing — a wrong row produces a *plausible* value. **Axis B lands complete
at sub-step 7**, driven from recorded values, so **sub-step 8 is pure wiring** and the only sub-step
that can turn an existing test red. The stop point after sub-step 5 is real.

**Two things are owed to the corpus before the C# gets far:**

- **A second rejection pass with a real `TAG4FILE`.** Every rejection is currently parsed with
  `tagPtr = 0`, so `e4tagExpr` (−510) never appears and `OTHER->K_TEXT` comes back as `e4fieldName`
  (−210). The tag-only refusals — an alias other than the indexed table, and whether the
  `SUBSTR`-over-`TRIM` restriction bites — are unobservable until then.
- **`CachedReadGoldenTests`'s `KeysABareField` guard is deleted at sub-step 8**, along with ADR-28's
  refusal. It is the one place the suite currently averts its eyes.

### Two things that depend on nothing

**~~Write `claude/specs/QUERY-OPTIMIZER.md`~~ — done 2026-09-03.** R13 retired. What it leaves behind
is three generator cases, listed in the spec's §12 and worth adding before any `QUERY` C#: a
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
