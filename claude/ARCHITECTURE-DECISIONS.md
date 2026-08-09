# Architecture decisions

Why things are the way they are. Each entry records the **decision, what was rejected, and why** —
the part that is expensive to reconstruct and easy to relitigate.

This file holds *decisions*. It does not describe the architecture as it currently stands — that is
[`PORTING-PLAN.md`](PORTING-PLAN.md) §3–§4 — nor on-disk facts, which are [`specs/`](specs/).
A rule lives in the plan; the argument that produced it lives here.

**Conventions.** Newest last, never renumbered. Status is `accepted`, `open`, or `superseded by
ADR-NN`. An `open` entry is a decision we have consciously deferred, with what would settle it.
When a decision changes, add a new entry and mark the old one superseded — do not edit history.

---

## ADR-01 — The C library is a corpus generator, not a live oracle · accepted

**Context.** The original C library is the ground truth for every byte. Tests need that truth.

**Decision.** Consult it **offline**: it generates golden files into `net/corpus/`, the corpus is
checked in, and tests read the corpus. Building or testing CodeBase.NET never compiles or runs C.

**Rejected — making the C build a standing test dependency.** It would impose Windows + MSVC + x86
on every contributor and every CI run, to re-derive bytes that never change. Generating once and
checking in the result gives the same authority at a fraction of the friction, and makes the expected
bytes reviewable in a diff.

**Consequence.** Coverage is bounded by the cases the generator was taught. When an implementation
path turns out to be untested, the fix is a new generator case — never hand-written expected bytes.

## ADR-02 — The generator is Windows / MSVC / x86 / C++ · accepted

**Context.** A portable generator would be preferable.

**Decision.** Build the reference library as **C++**, **x86**, with **MSVC**, on Windows only.

**Not a preference — three hard constraints.** A Linux build is impossible from this source drop
(`S4UNIX` requires `p4port.h`, never shipped). The library requires C++ (`d4declar.h:563-571`
declares default arguments under `#ifdef __cplusplus` and call sites depend on them). The x64
(`S464BIT`) path has unresolved rot in the 64-bit file-offset layer.

**Consequence.** Only corpus *regeneration* needs Windows. ADR-01 keeps that off the critical path
for everyone else. Details in `test-files-generator/README.md`.

## ADR-03 — The bitmap query optimizer is the headline feature · accepted

**Context.** The optimizer began as a "later maybe"; byte-compatibility was framed as the goal.

**Decision.** Byte-compatibility is the *foundation*; the optimizer is the *point*. It is priority
**P1**, ahead of write support (P2), and needs only the read path — a query optimizer never writes.

**Consequence.** Its correctness rule outranks its performance: a wrong record set is far worse than
a slow one, so any term that cannot be proven safe to optimize falls back to scanning, and the
full-scan equivalence gate runs on every case, always (`PORTING-PLAN.md` §5 `QUERY`, risk R12).

## ADR-04 — No Visual FoxPro VM, for now · accepted

**Context.** VFP would be the better generator: it *is* the compatibility target, and CodeBase
demonstrably differs from it — `flags[8]`, `autoIncrementVal`, the `0x31` overloading, and a
VFP-incompatible auto-increment scheme.

**Decision.** Do not build one yet. Generate from CodeBase and keep every generated case in
genuine-VFP shape, labelling any case that deliberately exercises a CodeBase extension.

**Why.** VFP is not installed, would not start, and an XP VM is a large cost for a small number of
open questions.

**Revisit when** a *specific* question needs it, and batch them — see ADR-11. The natural point is
the `LOCKING` capability, which already flags live-VFP verification of the append path.

## ADR-05 — DotNetDBF is a cross-check, never a gate · accepted

**Decision.** `DotNetDBF` may be used to independently decode plain DBF field data; disagreement is
a useful signal. It never gates anything, and is never used for keys, indexes or memos.

**Why.** No CDX support at all, DBT rather than FPT memo, missing `Y`/`T`/`H`, and it defines `B` as
*binary* where VFP means *double* — so it can agree while meaning something different.

## ADR-06 — The corpus lives in `net/corpus/`, published by a script · accepted

**Decision.** Generated files land in the gitignored `test-files-generator/bin/out/`, and
`copy-corpus.bat` publishes them to `net/corpus/`, which is checked in.

**Why the two directories.** If tests read the generator's output directory, the generated tree and
the reviewed tree drift silently. Publishing is an explicit act that shows up in `git status`.

## ADR-07 — The DBF header date stamp is frozen, not masked · accepted

**Context.** DBF header bytes 1-3 are the date the file was last written, so regenerating the corpus
on another day would change three bytes in every file.

**Decision.** The generator overwrites those three bytes with a constant (2026-01-01) after closing
each table. The corpus is therefore byte-stable and regeneration produces no diff. This is the only
place the generator alters what the C library wrote.

**Rejected — masking the bytes in every comparison.** Weaker (it weakens every byte assertion, not
just this one) and it dirties the diff on every regeneration.

**Consequence.** The divergence still exists for the `WRITE` gates, where the port writes today's
date against a frozen corpus; there it is asserted explicitly rather than masked
(`PORTING-PLAN.md` §8).

## ADR-08 — dBase III memo is out of scope; FoxPro 2.x covers the legacy path · accepted

**Context.** A legacy-memo corpus case was wanted.

**Decision.** Use FoxPro 2.x (`0xF5` + `.FPT`, 10-byte ASCII memo reference) as the legacy-memo case.

**Why not genuine dBase III.** Version `0x83` + `.DBT` is `S4MNDX`-only (`DBF-FORMAT.md` §2.1) and
cannot be produced from the S4FOX build we compile; `.DBT` is also outside the port's stated scope.

**Consequence.** A second S4MNDX generator with dBase file and index support is wanted eventually,
but only after `QUERY` — it widens compatibility beyond the VFP target that justifies the port.

## ADR-09 — Trunk-based development · accepted

**Decision.** Work directly on `main`. No feature branches unless explicitly requested. Commit and
push only when asked.

## ADR-10 — Corpus tables carry no code page marker · open

**Context.** Every corpus table has `codePage` `0x00` (unmarked), because that is the CodeBase
default. Real VFP tables normally carry an LDID, which also decides the default index collation.

**Open.** Add a codepage-marked case before the port has to exercise the LDID→`Encoding` map
(`DBF-FORMAT.md` §8), or decide that unmarked is the only shape v1 supports.

## ADR-11 — Questions only a live VFP can settle · open

Deferred by ADR-04; batch them if a VM is ever built.

- Does VFP distinguish tag `typeCode` `0x60` from `0xE0` as CodeBase does? (`CDX-FORMAT.md:672`)
- Should the port also implement VFP's *native* auto-increment (per-field descriptor bytes 19-23),
  which is incompatible with CodeBase's header-based scheme? (`DBF-FORMAT.md:359`)

## ADR-12 — MSVC toolchain is not pinned · open

`config.bat` takes whatever `vcvars32.bat` it finds first (this machine has MSVC 14.29 and 14.51; it
built clean on 14.51). Pin it for reproducibility, or keep it loose for convenience? Unresolved —
and low-stakes while ADR-01 keeps regeneration off the critical path.

## ADR-13 — Dump format: DBF/FPT half settled, index half open · open

**Settled and implemented** (`net/corpus/README.md`): raw header, on-disk field descriptors, the C
library's field view, and per-record raw bytes plus decoded values, with memo reference and contents.

**Still to design**, when CDX cases arrive: each tag's `(rawKeyBytes, recno)` walk, the `d4check`
result, and the standalone `value → key-bytes` table the `COLLATION` gate needs.

## ADR-14 — `CodeBase.Net`, with a capital B, everywhere · accepted

**Context.** The documents had drifted between `CodeBase.Net` (assembly, namespaces) and
`Codebase.Net` (solution file). Both spellings were in use before any code existed.

**Decision.** **`CodeBase.Net`** is the name, in one casing, everywhere it appears:

| Thing | Name |
|---|---|
| Solution | `net/CodeBase.Net.sln` |
| Library project, assembly, root namespace | `CodeBase.Net` |
| Test projects | `CodeBase.Net.Tests`, `CodeBase.Net.Golden`, `CodeBase.Net.Benchmarks` |
| SonarQube project key / name | `CodeBase.Net` / `CodeBase.NET` |

**Why.** The original product is *CodeBase* (Sequiter) — the capital B is theirs, and the port's
namespaces already used it. Settling one spelling before the solution exists costs nothing; settling
it afterwards means renaming projects, assemblies and a Sonar project with its history.

**Note.** The repository *directory* is `Codebase.Net`. Pre-existing, cosmetic, and not worth a
rename — it is not the assembly name and nothing references it.

## ADR-15 — API documentation is generated by docgen, so XML comments follow docgen's rules · accepted

**Context.** API help will be produced by Gehtsoft's **docgen** (`.claude/skills/docgen-skill`), whose
`cs2ds` extractor is not MSDN or DocFX. It understands a subset of the XML-doc tags and renders its
own BBCode; standard MSDN conventions applied blindly produce truncated briefs, inert links and
dropped emphasis.

**Decision.** Write every `///` comment to docgen's rules **from the first line of code**, not as a
later cleanup. The rules that bite (full set in the skill's `references/source-extraction.md`):

- the **first line of `<summary>` is the Brief** — one complete, standalone, plain-text sentence, on
  one line, never wrapped and with no inline markup; everything after it becomes the Details;
- **docgen BBCode, not XML formatting**: `[c]code[/c]`, `[i]`, `[b]` — `<c>` breaks the sentence
  across lines and `<i>`/`<b>` are dropped entirely;
- **`[clink=Fully.Qualified.Type]text[/clink]`, not `<see cref>`**, which renders inert or empty, and
  only ever in a detail paragraph;
- **no angle brackets or `->` arrows in prose**, not even escaped — write the words;
- **no `<remarks>`** — it is emitted nowhere and the content vanishes silently;
- never start a `///` line with `- ` or `* ` (it becomes a stray bullet);
- safe structural tags: `<summary>`, `<para>`, `<param>`, `<returns>`, `<exception cref>`, `<value>`.

**Why decide now.** Retrofitting these across a documented library is a mechanical but large edit,
and the failure mode is silent — the comments look fine in the IDE and only the generated help is
wrong. Costing nothing up front, it costs a sweep later.

**Rejected — write MSDN-style comments now, convert when docs are wired up.** The conversion is not
mechanical (briefs must be *rewritten* to fit one line, cross-references re-targeted), and nothing is
gained by deferring it.

**Consequence.** The library `.csproj` sets `GenerateDocumentationFile`. The docgen `doc/` project
itself is not built yet — it can be added whenever the public API stabilises, and the comments will
already be correct.
