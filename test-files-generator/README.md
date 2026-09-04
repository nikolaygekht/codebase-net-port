# test-files-generator

Generates golden test files for the CodeBase.NET port by driving the **original
Sequiter CodeBase C library** as a reference implementation.

The C# port is differential-tested against the files produced here. This
generator is a **developer tool, not a test dependency** — its output is
checked in, so building or running the C library is never required to build or
test CodeBase.NET. Run it only when the corpus needs new cases.

## Requirements

- Windows with **MSVC** (Visual Studio 2019 or later — verified on 19.51).
- The `original/source/` tree in this repository (read-only; never modified).

Nothing else. No zlib, no Windows SDK OLE-DB headers, no 32-bit-only SDK.

## Usage

```bat
build-lib.bat            :: compile the CodeBase library  -> obj\codebase.lib
build-gen.bat            :: compile + link the generator  -> bin\testgen.exe
bin\testgen.exe          :: write test files              -> bin\out\
copy-corpus.bat          :: publish them                  -> ..\net\corpus\

build-lib.bat clean      :: force a full rebuild
build-gen.bat clean
bin\testgen.exe <dir>    :: write somewhere else
copy-corpus.bat <dir>    :: publish from somewhere else
```

`bin\out\` is gitignored; `..\net\corpus\` is the checked-in copy the C# tests read. Review
`git status` after publishing — `copy-corpus.bat` overwrites but never deletes, so a renamed
case leaves its old files behind.

`build-lib.bat` is incremental: it skips any source whose `.obj` already
exists. A full build is ~2 minutes; subsequent runs are instant.

### Pointing at your compiler

`config.bat` locates `vcvars32.bat`. It checks a list of common install paths,
then falls back to `vswhere`. To override without editing anything:

```bat
set CB_VCVARS=C:\Path\To\VC\Auxiliary\Build\vcvars32.bat
build-lib.bat
```

If your Visual Studio lives somewhere unusual, add it to the candidate list at
the top of `config.bat`.

## Layout

```
test-files-generator/
├─ config.bat          compiler location (the one thing you may need to edit)
├─ build-lib.bat       original/source  -> obj\codebase.lib
├─ build-gen.bat       src\*.cpp        -> bin\testgen.exe
├─ copy-corpus.bat     bin\out          -> ..\net\corpus
├─ src/
│  ├─ cb-config.h      build switches for the C library (see below)
│  ├─ main.cpp         entry point; runs each case in turn
│  ├─ cases.h          the case entry points
│  ├─ case-db3type.cpp one case per file, each owning its own test data
│  ├─ case-vfptype.cpp
│  ├─ case-f2xmemo.cpp
│  ├─ case-vfpmemo.cpp
│  ├─ case-vfpnull.cpp
│  ├─ case-cp1251.cpp
│  ├─ case-cp936.cpp
│  ├─ case-cdxbase.cpp
│  ├─ case-cdxdeep.cpp
│  ├─ case-cdxcoll.cpp
│  ├─ case-idxone.cpp
│  ├─ util.h/.cpp      shared: error reporting, byte-run assignment, frozen date
│  │                   stamp, close+dump tail, byte escaping
│  ├─ dump.h/.cpp      shared: the <NAME>.dump.txt writer
│  └─ dump-index.h/.cpp shared: the <NAME>.cdx.dump.txt writer
├─ obj/                intermediates + per-file compile logs   [gitignored]
├─ bin/                testgen.exe, and bin\out\ default output [gitignored]
└─ vfp/                the VFP 9 oracle — a different engine, see below
```

**Utilities are shared; test data is not.** Each `case-*.cpp` keeps its own row
count, date and name lists, numeric edge cases and memo payload lengths, even
where two cases currently use the same values. That duplication is deliberate:
retuning one case's data can never move another case's bytes.

To add a case: write `src/case-<name>.cpp`, declare it in `src/cases.h`, and call
it from `main.cpp`. `build-gen.bat` compiles every `.cpp` in `src\`, so the build
needs no edit.

**`vfp/` is a second generator, and not this one.** Everything above is CodeBase. The scripts in
[`vfp/`](vfp/README.md) run in a **Visual FoxPro 9 VM** and produce the files CodeBase cannot write —
starting with an index whose tags use the thirteen functions CodeBase refuses — into `net/corpus/vfp/`,
which the data-driven golden tests do not sweep. It shares nothing with this build but the escaping
rules and the frozen date stamp, both reproduced there so the two dumps can be diffed. See ADR-44.

**Text outside ASCII is written as byte arrays, never as a source string
literal** — what a literal becomes depends on the compiler's source and
execution charsets, which is exactly the thing a code-page case must not leave
to chance. `util.h` has `TEXTBYTES` and `assignText` for that; each array carries
the text it encodes in a comment. Settings a case changes on the shared `CODE4`
(`compatibility`, `codePage`) are its own to restore: the code-page cases assign
`cb->codePage` directly, because `c4setCodePage` refuses the values they need,
and put it back to `cp0` immediately after `d4create`.

## Three things that are not obvious

**1. The C library must be compiled as C++, not C.** `d4declar.h:563-571`
declares default arguments under `#ifdef __cplusplus`, and call sites depend on
them (`d4lockFileInternal( data, 1 )` passes two of three parameters). Compiled
as C it fails immediately, with errors that look like unrelated configuration
rot. Hence `/TP`.

**2. `original/source` is never modified.** The shipped `D4all.h` is configured
for a DLL build with WinSock and zlib. Rather than patch it, `src/cb-config.h`
is force-included ahead of everything (`cl /FI`) and defines the `D4ALL_INC`
guard itself, so the shipped header expands to nothing and our switches win.

**3. Build x86, not x64.** The library writes native structs to disk. The x86
build is clean; the x64 (`S464BIT`) path has unresolved rot in the 64-bit
file-offset layer (`file4longMod` undefined, `.dLong` accessed on a non-struct)
in six files. `config.bat` therefore selects `vcvars32.bat`.

Four source files are excluded from the build — `c4long.c`, `COLL4ARR.C` and
`e4str2.c` are `#include`d by other translation units rather than compiled
standalone, and `M4MEM2.C` is OLE-DB-only (its whole body is `#ifdef
OLEDB5BUILD`, and it needs `defs5.hpp`, which is absent from this source drop).

## Determinism

Output is byte-stable: regenerating produces files identical to the checked-in
corpus. The one thing that would not be is the DBF header's "last update" stamp
(bytes 1-3), which is the system date — `freezeDateStamp` overwrites it with a
constant (2026-01-01) after each table is closed. That is the only place this
generator alters what the C library wrote.

## Current cases

Each corpus case writes `<NAME>.DBF` (+ `<NAME>.fpt` when it has memo fields)
and a `<NAME>.dump.txt` of expected header, descriptor and record values, read
back through the C library. 32 records each; rows 1-3 carry the edge cases. The
one **benchmark** case at the end of the table below is the exception: no dump,
and its own subdirectory.

| File | Version | Exercises |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x field set `C N D L`; no memo, no 263-byte reserved area |
| `VFPTYPE.DBF` | `0x30` | every non-memo VFP type — `C N F D L I B Y T`; 263-byte reserved area, integer/double/currency/datetime encodings |
| `F2XMEMO.DBF` | `0xF5` | FoxPro 2.x memo: the 10-byte ASCII in-record memo reference |
| `VFPMEMO.DBF` | `0x30` | VFP memo + binary types `M X G Z` (stored `M M G C`, flag `0x04`); 4-byte binary memo references; payloads straddling the 512-byte FPT block boundary |
| `VFPNULL.DBF` | `0x30` | nullable fields and the hidden `_NullFlags` descriptor: ten nullable fields interleaved with plain ones (so the null-bit ordinal is not the field index), a two-byte bitmap, and the memo/null interaction — see `net/corpus/README.md` |
| `CP1251.DBF` | `0x30` | a marked code page, single-byte: header byte 29 = `0xC9`; `0x80`-`0xFF` swept whole across records 1-8, high-byte text in the record and the memo, padded and exactly-filled character fields |
| `CP936.DBF` | `0x30` | a marked code page, multi-byte: header byte 29 = `0x7A`; GBK characters whose trail byte is an ASCII byte (`\`, `|`, `A`, `~`, `@`), and characters cut in half at a field boundary and at a memo length — see `net/corpus/README.md` |
| `CDXBASE.DBF` | `0x30` | the small index case: 32 records, ten tags, one block per tree — one tag per key shape (character, descending, duplicates, unique, sub-`0x20` bytes, numeric, double with `-0.0`, date, integer, filtered) |
| `CDXDEEP.DBF` | `0x30` | the multi-level index case: 600 records, four tags, one of them three levels deep. Depth comes from a wide incompressible key rather than from record count |
| `CDXTIME.DBF` | `0x30` | the datetime-key case: 256 datetimes over one `T` field, ascending and descending tags. Chosen so that about a third of the stored keys exercise the set half of `flags4dateTimeFlags` and the rest the clear half, plus the day's edges and the calendar's. Whole seconds only, because `f4assignDateTime` ignores a millisecond part for `T` |
| `CDXEXPR.DBF` | `0x30` | the expression case: 16 records, 22 tags, and the only `H` field in the corpus. Its records supply what a literal cannot say — the field-loader-only types and a null — because the function semantics are recorded over constants instead. Beside the usual dumps it writes `CDXEXPR.expr.dump.txt` (node arrays, constant and field values, rejections) through `dump-expr.cpp`, and `FUNCTIONS.dump.txt` is written once from `main.cpp`. **Its expression lists are sized by function-table row coverage**: 144 of the 193 rows are reached by some expression and the remaining 49 are unreachable by construction, so adding a case here is how an uncovered row gets covered. Adding one changes only the expression dump — every other file regenerates byte-identical. Since step 011 it also carries **54 query filters** through `dump-query.cpp` |
| `CDXCOLL.DBF` | `0x30` | the collation case: cp1252, one `C(20)` field indexed both machine and `GENERAL`, so `keyLen` 20 and 40 and pad bytes `0x20` and `0x00` sit in one file |
| `IDXONE.DBF` | `0x30` | the single-tag case: a one-tag `.cdx` plus the `.IDX` derived from it, verified by walking both — see below |

One case is not a corpus case at all:

| File | Version | Exercises |
|---|---|---|
| `benchmark\PERF10K.DBF` | `0x30` | the **benchmark** case: 10 000 records, a `C(20)` tag four levels deep and an `N(12,2)` tag three levels deep. A workload, not an expectation — it writes into a `benchmark` subdirectory and has **no dump**, because nothing asserts against its bytes, only against how fast they are read. It is the table step 009's audit measured, so its shapes are fixed by that measurement. See `net/corpus/README.md` |

`casePerf10k` is the only case that calls `finishUndumped` rather than `finish`,
and the only one that writes outside the output directory's root. Both are
deliberate and the case file says why.

Verified on the generated bytes: `headerLen` = 32 + n×32 + 1 (+263 for `0x30`);
the 263 reserved bytes all zero; `flags[8]` and `autoIncrementVal` zero, so the
VFP files have genuine-VFP shape with no CodeBase extensions; no trailing
`0x1A`; FPT header `blockSize` = 512 big-endian, matching the specs.

Genuine dBase III memo (version `0x83` + `.DBT`) is **not** producible here — it
is `S4MNDX`-only (`DBF-FORMAT.md` §2.1) and `.DBT` is outside the port's scope.
`F2XMEMO` is the closest reachable legacy-memo case.

## Three things about the index cases

**Indexes are built by the bulk path, not key by key.** Each case appends every record first and calls
`i4create` afterwards, which is what VFP's `INDEX ON` and CodeBase's own create do
(`r4reindexWriteKeys`, `CDX-FORMAT.md` §8.5) and what packs leaves tight. A tree grown one key at a
time through insert-and-split has a looser shape; that belongs to `WRITE`, where splitting gets ported.
`CDXCOLL`'s second tag is the one exception — `i4tagAdd` puts it in an existing file, which is also why
its `signature` byte is `0x00` where the first tag's is `0x01`.

**And query cases, since step 011.** `dump-query.cpp` sets each filter on a real `RELATE4` and reads
what the optimizer decided out of `RELATE4`/`RELATION4`, which are public structs: the tag matched to
each compiled node, `fullyMapped`, the candidate bitset **before** any residual filtering, and the
records returned in both orders. Every filter is then run a **second** time with
`RELATION4.bitmapDisable` set, which disables the optimizer (`m4map.c:341`) and leaves the reference
evaluating the filter per record — so the dump carries the reference's own scan truth beside its
optimized answer, and says whether the two agree. Exactly one of the corpus's 81 filters disagrees,
which is the reference demonstrating a known defect on itself. `CDXBASE` supplies the eligibility
refusals (a unique tag, a filtered tag, an integer tag), `CDXDEEP` the ranges that cross leaf
boundaries, `CDXEXPR` everything about expression tags. Filters are chosen to cover every row of
`QUERY-OPTIMIZER.md` §8, plus every shape the constraint algebra folds — which is what took `CDXEXPR`
from 43 to 54, because until then no filter joined two terms over one tag by an `.OR.` whose union is
a single constraint, and that half of the algebra was gated by hand-written tests alone. They **must
not read the clock**: a `CTOD` filter would record a record set
that changes with the year.

**The dumps carry seek cases too.** `dump-index.cpp` derives ten to twelve search values per tag from
that tag's own keys — the first, last and a middle key, the first key of a run of duplicates, a prefix, a
value between two keys, one below and one above everything, an empty one and an all-pad one — then records
what `tfile4seek` returned and where it left the cursor. For the eleven tags whose key transform is the
identity (a machine-collated character tag, where value bytes *are* key bytes) it also records
`d4seekN`-then-`d4seekNextN` runs as record sequences. Two traps: `tfile4seek` **writes through the key
pointer it is given** on a descending tag, so every case is seeked from a fresh copy; and the
**length-taking** forms of the seek calls are required, because `T_BIN`'s keys hold `0x00` and the string
forms would stop there.

**Two library calls cannot be used, and both are worth knowing.** `tfile4count` returns 1 for any
descending tag (it tops the tag, landing at the physical last key, then skips forward physically), so
`dump-index.cpp` counts by walking with the direction-aware `tfile4dskip` instead. And `d4check`
reports every single-tag file as corrupt, because its block accounting flags the tag directory's header
and then each tag's header, which in an `.IDX` is the same block. Both are cited in
`CDX-FORMAT.md` §2.1 and §7.

**`IDXONE.IDX` is derived, and then verified.** CodeBase cannot write a single-tag file, so
`case-idxone.cpp` copies the tag header from offset 1024 to offset 0 and clears the compound bit —
two edits, no block moved, every node number still valid. Since `d4check` will not vouch for the
result, the case walks `IDXONE.cdx` and `IDXONE.IDX` side by side and requires the same key and record
number at every step. See ADR-25.
