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
│  ├─ util.h/.cpp      shared: error reporting, frozen date stamp, close+dump tail
│  └─ dump.h/.cpp      shared: the <NAME>.dump.txt writer
├─ obj/                intermediates + per-file compile logs   [gitignored]
└─ bin/                testgen.exe, and bin\out\ default output [gitignored]
```

**Utilities are shared; test data is not.** Each `case-*.cpp` keeps its own row
count, date and name lists, numeric edge cases and memo payload lengths, even
where two cases currently use the same values. That duplication is deliberate:
retuning one case's data can never move another case's bytes.

To add a case: write `src/case-<name>.cpp`, declare it in `src/cases.h`, and call
it from `main.cpp`. `build-gen.bat` compiles every `.cpp` in `src\`, so the build
needs no edit.

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

Each case writes `<NAME>.DBF` (+ `<NAME>.fpt` when it has memo fields) and a
`<NAME>.dump.txt` of expected header, descriptor and record values, read back
through the C library. 32 records each; rows 1-3 carry the edge cases.

| File | Version | Exercises |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x field set `C N D L`; no memo, no 263-byte reserved area |
| `VFPTYPE.DBF` | `0x30` | every non-memo VFP type — `C N F D L I B Y T`; 263-byte reserved area, integer/double/currency/datetime encodings |
| `F2XMEMO.DBF` | `0xF5` | FoxPro 2.x memo: the 10-byte ASCII in-record memo reference |
| `VFPMEMO.DBF` | `0x30` | VFP memo + binary types `M X G Z` (stored `M M G C`, flag `0x04`); 4-byte binary memo references; payloads straddling the 512-byte FPT block boundary |

Verified on the generated bytes: `headerLen` = 32 + n×32 + 1 (+263 for `0x30`);
the 263 reserved bytes all zero; `flags[8]` and `autoIncrementVal` zero, so the
VFP files have genuine-VFP shape with no CodeBase extensions; no trailing
`0x1A`; FPT header `blockSize` = 512 big-endian, matching the specs.

Genuine dBase III memo (version `0x83` + `.DBT`) is **not** producible here — it
is `S4MNDX`-only (`DBF-FORMAT.md` §2.1) and `.DBT` is outside the port's scope.
`F2XMEMO` is the closest reachable legacy-memo case.
