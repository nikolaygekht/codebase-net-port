# Project state

**Updated:** 2026-08-09 · `main` @ `fcd3550` (the documentation rework below is uncommitted).
**Active step:** none — the next one starts under `claude/dev/`.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**No C# exists yet** — no solution, no `net/src/`, no `net/tests/`. What exists is the reference
material, a working corpus generator, and the first corpus.

**`test-files-generator/`** builds and runs end to end (Windows/MSVC):

```bat
build-lib.bat     :: 137 TUs -> obj\codebase.lib   (~2 min; ~1.4 s incremental)
build-gen.bat     :: src\*.cpp -> bin\testgen.exe
bin\testgen.exe   :: -> bin\out\
copy-corpus.bat   :: -> ..\net\corpus\
```

**`net/corpus/`** — four DBF cases, no indexes, 32 records each, each with a `<NAME>.dump.txt` of
expected header/descriptor/record values read back through the C library:

| File | Version | Covers |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x set `C N D L`, no memo |
| `VFPTYPE.DBF` | `0x30` | every non-memo VFP type `C N F D L I B Y T` |
| `F2XMEMO.DBF` + `.fpt` | `0xF5` | FoxPro 2.x memo, 10-byte ASCII memo reference |
| `VFPMEMO.DBF` + `.fpt` | `0x30` | memo + binary types `M X G Z`, 4-byte binary reference, payloads straddling the 512-byte FPT block boundary |

Verified against the specs: `headerLen` = 32 + n×32 + 1 (+263 for `0x30`); 263 reserved bytes zero;
`flags[8]` and `autoIncrementVal` zero (genuine-VFP shape); no trailing `0x1A`; FPT `blockSize` = 512
big-endian; `X`/`Z` stored as `M`/`C` with descriptor flag `0x04`. Regeneration is byte-identical.

**Documentation:** seven format specs, the porting plan, the development approach and the decision
log. `claude/specs/QUERY-OPTIMIZER.md` **does not exist** and is the gap that matters — the optimizer
is the only in-scope subsystem with no source-cited spec (risk R13).

---

## 2. Last session (2026-08-09)

- **Corpus v1** — replaced the single `SIMPLE.DBF` proof-of-toolchain with the four cases above,
  32 records each, and added the `.dump.txt` format so tests never need hand-written expectations.
  Committed as `fcd3550`.
- **Froze the DBF header date stamp** so the corpus is byte-stable (ADR-07).
- **Split the generator** from one 679-line `generator.cpp` into `main.cpp` + one file per case +
  shared `util`/`dump`; test data is per-case on purpose. Output verified byte-identical.
- **Documentation rework** (uncommitted): added `claude/DEV_APPROACH.md` and `claude/dev/`
  templates; recast `PORTING-PLAN.md` §5 from an M0-M9 roadmap into a capability inventory with
  priorities and live status; split this file, `claude/ARCHITECTURE-DECISIONS.md`,
  `FOR-DEVELOPERS.md` and `CLAUDE.md` along the boundaries in `CLAUDE.md` §"Where things live".

Two generator bugs found and fixed while producing the corpus: `f4assignDateTime` read past the end
of a short string (garbage time bytes in the blank-datetime row), and the dump reported the API field
type, hiding that `X`/`Z` are stored on disk as `M`/`C` with flag `0x04`.

---

## 3. Next

**Now — either of these; they are independent.**

1. **Write `claude/specs/QUERY-OPTIMIZER.md`.** Sources: `R4RELATE.H:268-396`, `C4CONST.C`,
   `m4map.c`, to the same `FILE.C:line` standard as the existing seven. Must cover the `BITMAP4`
   tree and flags, `CONST4` range constraints, filter→bitmap decomposition, **which expression forms
   are optimizable and which are not**, leaf evaluation via tag seek, AND/OR/negation combination,
   and the fall-back-to-scan boundary. Prerequisite for `QUERY` (risk R13).

2. **Start `DBF-READ`.** Create the solution under `net/` and read DBF files without indexes,
   asserted against the four corpus tables and their dumps. Watch the memo companion file: CodeBase
   writes lower-case `.fpt` beside an upper-case `.DBF`, so it must be resolved case-insensitively
   on Linux.

Either way, run it through the step process — `cp -r claude/dev/_template claude/dev/001-<name>`,
write `DESIGN.md` and `PLAN.md`, and only then write code.

**Soon after.** Grow the corpus toward the `CDX-READ` and `COLLATION` gates — the corpus still has no
indexed cases at all, and **multi-level trees are the single biggest gap** (`PORTING-PLAN.md` §6.3
lists what is missing). Then the `CORPUS` spot-check pass against the specs: FPT `numChars` =
payload-only, CDX interior-node big-endian recno, the 263-byte reserved area, the `t4dblToFox` sign
rule.
