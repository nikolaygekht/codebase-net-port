# Project state

**Last updated:** 2026-08-08 · commits `9f4f1b4`, `4804c00` on `main`.

A running handoff document: where the project actually is, what was decided and
why, and what to do next. Update it at the end of a working session.

For *what we are building* read [`README.md`](README.md); for *how* read
[`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md); for *what the bytes are* read
[`claude/specs/`](claude/specs/).

---

## 1. Where we are

**No C# exists yet.** There is no solution, no `src/`, no `tests/`, no `corpus/`.
The repository is documentation, the read-only C reference, and a working
test-file generator.

| Milestone | State |
|---|---|
| **M0** — corpus generator + corpus v1 | **In progress.** Build harness done and working; corpus barely started (one table) |
| M1 — DBF reading | Not started |
| M2 — CDX reading | Not started |
| M3 — Collation | Not started |
| M4 — Expressions | Not started |
| **M5 — Query optimizer** (headline) | Not started; **blocked on writing its spec** — see §4 |
| M6-M9 — write, locking, transactions, hardening | Not started |

### What works today

`test-files-generator/` builds and runs end to end:

```bat
build-lib.bat     :: 137 TUs -> obj\codebase.lib   (~2 min; ~1.4 s incremental)
build-gen.bat     :: -> bin\testgen.exe
bin\testgen.exe   :: -> bin\out\SIMPLE.DBF
```

It produces exactly **one** case, `SIMPLE.DBF` (3 fields `N`/`C`/`N(2 dec)`, 3
records, no index, no memo), byte-verified against `DBF-FORMAT.md`: version
`0x30`, `headerLen` 392 = 32 + 3×32 + 1 + 263, the 263 reserved bytes zero,
CodeBase `flags[8]` and `autoIncrementVal` zero (genuine-VFP shape), no trailing
`0x1A`.

### Documentation state

Seven format specs exist and are authoritative. **`QUERY-OPTIMIZER.md` does not
exist** and is the gap that matters — the optimizer is the only in-scope
subsystem with no source-cited spec (risk R13).

---

## 2. Decisions made — do not relitigate without new information

**Corpus generator, not a live oracle.** The C library is ground truth but is
consulted *offline*: it generates `corpus/`, the corpus is checked in, tests read
the corpus. Building or testing CodeBase.NET never compiles or runs C, and needs
neither Windows nor MSVC. Rejected: making the C build a standing test
dependency (imposes Windows+MSVC+x86 on every contributor and CI run to
re-derive bytes that never change).

**Generator is Windows / MSVC / x86 / C++.** Not a preference — a Linux build is
impossible from this drop (`S4UNIX` needs `p4port.h`, never shipped), the library
requires C++ (`#ifdef __cplusplus` default arguments that call sites depend on),
and the x64 `S464BIT` path is broken in the 64-bit file-offset layer.

**Bitmap query optimizer is the headline feature**, promoted from "later maybe"
to milestone M5, landing before any write support. Byte-compatibility is the
foundation, not the goal.

**No Visual FoxPro VM — for now.** VFP would be the better generator (it *is* the
compatibility target, and CodeBase demonstrably differs from it: `flags[8]`,
`autoIncrementVal`, `0x31` overloading, VFP-incompatible autoincrement). But it
isn't installed, wouldn't start, and an XP VM is a large cost for a small number
of open questions. Revisit only when a *specific* question needs it (§4), and
batch them — the natural point is M7, which already flags live-VFP verification.

**DotNetDBF is supplementary only, never a gate.** No CDX support at all, DBT not
FPT, missing `Y`/`T`/`H`, and it defines `B` as *binary* where VFP means *double*
— so it can agree while meaning something different.

**Trunk-based development.** Work directly on `main`. No branches unless
explicitly requested. Commit/push only when asked.

---

## 3. Facts established today (evidence-backed, safe to rely on)

**CDX endianness.** Only three fields are big-endian: the tag-**directory**
`version` counter (file offset 8) and the two trailing 4-byte fields (record
number, child node) of each interior-node entry. Everything else — including the
tag-header `root`/`freeList`/`keyLen`/`blockSize`, `B4STD_HEADER` and the leaf
`B4NODE_HEADER` — is little-endian. Reading rule: a swap under `#ifdef
S4BYTE_SWAP` fires only on big-endian *hosts*, so that field is little-endian on
disk; an unguarded swap or one under `#else`/`#ifndef` means big-endian.

**A regular tag's `version` field is always `00 00 00 00`** (all 33 sample CDX
files). Only the tag directory's counter is maintained. Leave it zero: the
regular-tag write path reverses the 4-byte field with `x4reverseShort`
(`i4add.c:887-889`) while the read path uses `x4reverseLong` (`i4init.c:365`);
they agree only at zero.

**`original/examples/DATA/` is not sufficient as a corpus.** Measured: 46 DBFs
containing only `C, D, L, M, N` (**7 of 12 in-scope types absent**: `B F G H I T
Y`); 40 are dBASE III `0x03`, 5 are FoxPro 2.x `0xF5`, exactly **one** is VFP
`0x30` (`FOXUSER.DBF`, 7 records), none are `0x31`; no nullable fields. Across 32
CDX files and 56 tags there are **zero interior nodes** — every tag is a single
leaf. Interior-node traversal and leaf splits are untestable without generated
cases.

**The plan's "bitmap optimizer" pointer was wrong.** `r4bit.c` is Windows DIB/BMP
*image* handling for the report writer; `o4opt.c` is the I/O buffer cache. The
real optimizer is `BITMAP4` (`R4RELATE.H:268-307`), implemented in `C4CONST.C`
and `m4map.c` — a boolean tree (`BITMAP4LEAF`/`BITMAP4AND`, `andOr`, `children`)
whose leaves are per-tag range constraints (`CONST4 lt/le/gt/ge/eq` + an `ne`
list). Reachable for a single table via `relate4init` + `relate4querySet`, so
joins are not required.

---

## 4. Open questions (need a decision, none blocking today)

1. **Header date-stamp non-determinism.** DBF bytes 1-3 are the generation date,
   so regenerating on another day changes three bytes per file. Decide *before*
   checking in a corpus: teach the generator to freeze the stamp, or mask those
   bytes in comparisons. Freezing is stronger — it preserves true byte-identity.
2. **Toolchain pinning.** `config.bat` currently takes whatever `vcvars32.bat`
   resolves to (this machine has MSVC 14.29 and 14.51; it built clean on 14.51).
   Pin for reproducibility, or keep it loose?
3. **Deferred VFP-only questions** — batch these if a VM ever gets built:
   - Does VFP distinguish tag `typeCode` `0x60` vs `0xE0` as CodeBase does?
     (`CDX-FORMAT.md:672`)
   - Should the port also implement VFP's *native* autoincrement (per-field
     descriptor bytes 19-23), which is incompatible with CodeBase's header-based
     scheme? (`DBF-FORMAT.md:359`)
4. **Corpus dump format.** Not yet designed. Needs to carry: field descriptors,
   each record's raw + decoded fields, each tag's `(rawKeyBytes, recno)` walk,
   memo entries, `d4check` result, and a standalone `value → key-bytes` table for
   the M3 gate. Text and diffable, since it gets reviewed in pull requests.

---

## 5. Next steps

Recommended order. (1) and (2) are independent and could go in either order or
in parallel.

**1. Write `claude/specs/QUERY-OPTIMIZER.md`.**
Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`. Same `FILE.C:line`
standard as the existing seven. Must cover: `BITMAP4` tree structure and flags,
`CONST4` range constraints, filter→bitmap decomposition rules, **which
expression forms are optimizable and which are not**, leaf evaluation via tag
seek, AND/OR/negation set combination, and the fall-back-to-scan boundary.
This is a stated prerequisite for M5 (risk R13) and the highest-value document
we do not have.

**2. Finish M0 — grow the corpus.**
The generator's one case proves the toolchain, nothing more. Add, roughly in
order of what later milestones need:
- the 7 missing field types (`B F G H I T Y`), plus nullable fields and `0x31`
  CodeBase-extension cases (labelled as non-VFP);
- **tagged tables large enough to force multi-level trees** — the single biggest
  corpus gap, since nothing shipped has an interior node;
- leaf blocks driven to the widening/split boundary; record counts crossing
  `recNumLen` thresholds;
- GENERAL-collated strings (accents, expansions, trailing blanks); datetimes
  across many seconds-of-day for the ULP bitset; currency signs and rounding;
- memos forcing FPT growth and compaction;
- before/after mutation pairs for the M6 write gate;
- resolve question (1) above, then design the dump format (question 4).

**3. M0 spot-check pass.**
The specs' adversarial re-verification never completed (plan §9). Confirm a few
claims per format against freshly generated bytes before building on them: FPT
`numChars` = payload-only, **CDX interior-node big-endian recno** (needs a
multi-level tree from step 2 — currently unverifiable), the 263-byte reserved
area, the `t4dblToFox` sign rule.

**4. Then M1.**
Create the solution (`src/CodeBase.Net`, `tests/CodeBase.Net.Tests`,
`tests/CodeBase.Net.Golden`) and start DBF reading against the corpus.

### Do not

- Hand-write expected bytes. If a path is untested, add a generator case and
  regenerate.
- Use `CultureInfo`/`CompareInfo`/`GetSortKey()` anywhere near index keys.
- Modify anything under `original/source/`.
- Assume one endianness per file format (§3).
- Let the query optimizer return a record set that differs from a full scan —
  when in doubt it must fall back to scanning (risk R12).
