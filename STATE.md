# Project state

**Updated:** 2026-08-09 · `main` @ `fcd3550` — **everything below is uncommitted**: the
documentation rework, the `VFPNULL` corpus case and generator changes, and the whole of step 001.
**Active step:** none. [`claude/dev/001-dbf-open-and-header`](claude/dev/001-dbf-open-and-header/)
closed — the metadata half of `DBF-READ`, 224 tests green.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF's metadata can be read.** `net/CodeBase.Net.sln` builds four projects — `CodeBase.Net`
(**no NuGet dependencies** by design, ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden` and
`CodeBase.Net.TestUtils` — and `dotnet test` is green on **224 tests**, 89 of them golden.

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");
foreach (FieldDefinition f in table.Fields) { /* name, type, length, offset, nullability */ }
```

Opening a table reads its header, its stored descriptors and its resolved field table, and opens the
memo file beside it when the header declares one. Every one of those is gated against the C
library's own dump for all five corpus tables. **No record or field value is readable yet** — that is
step 002.

**`test-files-generator/`** builds and runs end to end (Windows/MSVC):

```bat
build-lib.bat     :: 137 TUs -> obj\codebase.lib   (~2 min; ~1.4 s incremental)
build-gen.bat     :: src\*.cpp -> bin\testgen.exe
bin\testgen.exe   :: -> bin\out\
copy-corpus.bat   :: -> ..\net\corpus\
```

**`net/corpus/`** — five DBF cases, no indexes, 32 records each, each with a `<NAME>.dump.txt` of
expected header/descriptor/record values read back through the C library:

| File | Version | Covers |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x set `C N D L`, no memo |
| `VFPTYPE.DBF` | `0x30` | every non-memo VFP type `C N F D L I B Y T` |
| `F2XMEMO.DBF` + `.fpt` | `0xF5` | FoxPro 2.x memo, 10-byte ASCII memo reference |
| `VFPMEMO.DBF` + `.fpt` | `0x30` | memo + binary types `M X G Z`, 4-byte binary reference, payloads straddling the 512-byte FPT block boundary |
| `VFPNULL.DBF` + `.fpt` | `0x30` | nullable fields: the hidden `_NullFlags` descriptor, null-bit ordinals that are not field indexes, a two-byte bitmap, and the memo/null interaction |

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

**Later the same day — step 001 opened, and the corpus grew a fifth case.**

- **`claude/dev/001-dbf-open-and-header/DESIGN.md`** written: open a DBF (+ companion FPT) and expose
  its metadata; record navigation and field values are step 002. 15 decisions, 16 classes.
- **`VFPNULL.DBF` + `.fpt` + dump** added so `_NullFlags` is gated rather than guessed. The existing
  four cases regenerated **byte-identical** — the dump grew by optional tokens only (ADR-16).
- Four spec follow-ups came out of reading `D4OPEN.C` and `f4memo.c` against the specs; they are
  listed at the foot of the step's `DESIGN.md`. The one worth knowing: a nullable **memo** cannot be
  null and hold content at once — the deferred flush writes the block id and clears the bit.
- **`PLAN.md`** written (8 sub-steps, layer-3 enumerated), and `DEV_APPROACH.md` §4 gained the rule
  that tests assert the **contract, not the implementation**, plus a widened layer 3 covering corrupt
  content as well as hostile I/O.
- A fifth spec follow-up, and the most consequential: `DBF-FORMAT.md` §5 presents open-time field
  length validation as unconditional. Reading `D4OPEN.C:2448-2649` case by case, a release build
  checks only `I`/`P`/`R`/`Q`/`V`/`5`/`1`/`6`; `D`, `L`, `N`, `F`, `C`, `B`, `H`, `Y`, `T`, `7` are
  never length-checked, and the `M`/`G` 4-or-10 rule is `#ifdef E4MISC`. Decisions 18-19 rebuilt
  around it: containment is structural, validation only a diagnostic.
- **Step 001 executed and closed** — all 8 sub-steps, 224 tests. Read
  [`SUMMARY.md`](claude/dev/001-dbf-open-and-header/SUMMARY.md), not this list. Two spec corrections
  came out of it that would each have caused silent misreads, and both are now in `claude/specs/`:
  the Visual-FoxPro type gate is a **signed** comparison, so `0xF5` correctly refuses `T`/`Y`/`B`
  fields; and open-time length validation barely exists in a release build, so a reader that refuses
  a 7-byte date is *less* compatible than the original.

---

## 3. Next

**Nothing is committed.** The whole of step 001 — design, plan, the `VFPNULL` corpus case, the
generator changes and 47 source files — is still in the working tree, along with the earlier
documentation rework. Committing is the first thing to do.

**Then step 002 — DBF record reading:** navigation (`Top/Bottom/Skip/Go/Eof/Bof/RecNo`) and per-type
field decode, gated on the `[records]` section of the same five dumps. Design and plan first, as
always. Two things to settle as it opens:

1. **Add a code-page-marked corpus case first.** No table carries a non-zero `codePage`, so
   `CodePageMap`'s real branches are ungated — and 002 is the step that decodes text (ADR-10).
2. **The per-type accessor widths** in step 001's `DESIGN.md` Decision 18 are the contract for
   decoding, and they were read from `F4FIELD.C`. Re-check each against the source before writing
   the decoders; the C is not uniform, and its habit of reading a type's natural width regardless of
   the declared length is deliberate and must be copied.

**Also open — independent of the above.**

**Write `claude/specs/QUERY-OPTIMIZER.md`.** Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`,
to the same `FILE.C:line` standard as the existing seven. Must cover the `BITMAP4` tree and flags,
`CONST4` range constraints, filter→bitmap decomposition, **which expression forms are optimizable and
which are not**, leaf evaluation via tag seek, AND/OR/negation combination, and the fall-back-to-scan
boundary. Prerequisite for `QUERY` (risk R13).

**Soon after.** Grow the corpus toward the `CDX-READ` and `COLLATION` gates — the corpus still has no
indexed cases at all, and **multi-level trees are the single biggest gap** (`PORTING-PLAN.md` §6.3
lists what is missing). Then the `CORPUS` spot-check pass against the specs: FPT `numChars` =
payload-only, CDX interior-node big-endian recno, the 263-byte reserved area, the `t4dblToFox` sign
rule.
