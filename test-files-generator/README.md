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

build-lib.bat clean      :: force a full rebuild
build-gen.bat clean
bin\testgen.exe <dir>    :: write somewhere else
```

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
├─ build-gen.bat       src\generator.cpp -> bin\testgen.exe
├─ src/
│  ├─ cb-config.h      build switches for the C library (see below)
│  └─ generator.cpp    the test-file cases
├─ obj/                intermediates + per-file compile logs   [gitignored]
└─ bin/                testgen.exe, and bin\out\ default output [gitignored]
```

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

## Known non-determinism

The DBF header's "last update" stamp (bytes 1-3) is today's date, so
regenerating on a different day changes three bytes per file. Anything doing a
byte-for-byte comparison against a checked-in corpus must either mask those
bytes or the generator must be taught to freeze them. Not yet addressed — see
`claude/PORTING-PLAN.md` §8.

## Current cases

| File | Contents | Exercises |
|---|---|---|
| `SIMPLE.DBF` | 3 fields (`N`, `C`, `N` with decimals), 3 records, no index, no memo | End-to-end sanity: VFP `0x30` header, 263-byte reserved area, field descriptors, record encoding |

Verified byte-level on `SIMPLE.DBF`: version `0x30`; `headerLen` = 392 =
32 + 3×32 + 1 + 263; the 263 reserved bytes all zero; CodeBase `flags[8]` and
`autoIncrementVal` all zero (so the file has genuine-VFP shape, no CodeBase
extensions); no trailing `0x1A`, matching `DBF-FORMAT.md`.
