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

## ADR-10 — Corpus tables carry no code page marker · superseded by ADR-18

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

## ADR-16 — The dump format grows by optional tokens, never by new columns · accepted

**Context.** Adding the `VFPNULL` case (2026-08-09) meant the dump had to carry three new facts:
which fields are nullable, which are null in a given record, and the raw `_NullFlags` bitmap. The
obvious shape — a `nullable=` and `null=` column on every field line — would have rewritten all four
existing `.dump.txt` files, roughly 1 200 lines of churn, none of it a change in meaning.

**Decision.** New dump facts are emitted **only when they are true**. `nullable=1`, `null=1` and the
trailing `_NULLFLAGS` line appear on tables that have nullable fields and nowhere else. The four
pre-existing dumps regenerated **byte-identical**, verified before publishing.

**Why.** The corpus's value is that a diff is meaningful: a changed byte is either a bug or a
deliberate new fact. A format change that touches every file destroys that property for one commit,
which is exactly the commit where a real regression would hide. Optional tokens also keep the
absence informative — `[fields]` says `nullable=1`, so a record line without `null=1` unambiguously
means "nullable and not null".

**Rejected — always-emit columns.** More regular to parse, and a reader never has to know a token
can be absent. Not worth resetting the corpus's diffability, and the C# parser has to handle
optional trailing tokens anyway.

**Consequence.** The reader in `CodeBase.Net.Golden` parses trailing tokens as optional. This
convention binds the index half of the dump too (ADR-13) when CDX cases arrive.

## ADR-17 — Encoding providers are the host's to register, so the library has no dependencies · accepted

**Context.** DBF record text is stored in a code page named by the header's language-driver byte
(cp437/cp850/cp1252/cp1250, `DBF-FORMAT.md` §8). On .NET 8 those encodings are unavailable until
someone calls `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`. The library was
planned to reference `System.Text.Encoding.CodePages` and do that itself.

**Decision.** **The library never registers an encoding provider.** It calls
`Encoding.GetEncoding(int)` and uses whatever the host registered. The requirement is documented —
`README.md`, `FOR-DEVELOPERS.md`, and the XML docs on `Table.TextEncoding` and
`CodeBaseEngine.DefaultEncoding`. Two consequences:

- **`Table.TextEncoding` is lazy.** Resolving it at open would make a metadata-only read of a
  cp1252-marked table fail without a provider it never uses. Deferred, the failure comes from the
  call that actually needs an encoding.
- **`CodeBase.Net` ships with no NuGet dependencies at all.** `System.Text.Encoding.CodePages`
  becomes a test-project reference (the tests must register the provider to exercise decoding).

**Why.** Registering an encoding provider is a **process-wide side effect**. A library that performs
it on load changes `Encoding.GetEncoding` for every other component in the process, including code
that never touches this library — a decision its author never made and cannot see. Leaving it to
composition keeps the side effect where the application already owns such choices, and the cost is
one documented line of setup.

**Rejected — register it in a static constructor.** Convenient, and the failure mode it prevents is
real (a first `GetEncoding(437)` throwing far from the cause). But convenience does not justify
mutating global state on someone else's behalf, and the alternative is a documented one-liner.

**Rejected — keep the package reference unused, "just in case".** An unused dependency on the very
package this decision declines to use is a standing invitation to call `RegisterProvider` from
inside and quietly undo it.

**Companion.** The *fallback* encoding — for tables whose code-page byte is unmarked (`0x00`) or
unrecognized — is `CodeBaseEngine.DefaultEncoding`, defaulting to cp437 to match the C library's
treatment of unmarked files. A recognized marker always wins: the file is authoritative about
itself. Supersedes the dependency line in `PORTING-PLAN.md` §3.1 and `CLAUDE.md` §Technology stack,
both updated.

## ADR-18 — Two code-page-marked corpus cases, marked with bytes CodeBase itself will not set · accepted

**Context.** Closes ADR-10. Every corpus table left `codePage` at `0x00`, so `CodePageMap`'s real
branches had nothing behind them and a reader that ignored header byte 29 outright would have passed
the whole suite. Step 002 decodes record text, which is where the byte first matters.

**Decision.** Two cases, both version `0x30` with a memo: **`CP1251.DBF`** marked `0xC9` (Windows
Cyrillic, single-byte) and **`CP936.DBF`** marked `0x7A` (Simplified Chinese GBK, multi-byte). Both
carry text no ASCII-only reader could invent, in the record *and* in the memo. Neither byte is one
CodeBase's own setter accepts — `c4setCodePage` takes only cp0/437/850/1252/1250 (`c4set.c:727-745`)
— so the generator assigns `CODE4.codePage` directly, which `d4create` writes verbatim into the
header with no validation (`D4CREATE.C:1391`) and `d4open` reads straight back (`D4OPEN.C:2217`).

**Why two, and why these.** The single-byte and multi-byte halves fail differently, and only the
second is dangerous:

- **A single-byte page** is a pure interpretation question. `CP1251` sweeps `0x80-0xFF` whole across
  eight rows, including `0x98`, the one byte cp1251 leaves undefined — so what a reader makes of an
  undefined byte becomes a decision rather than an accident.
- **A multi-byte page breaks byte-wise reasoning.** GBK trail bytes are `0x40-0xFE`, overlapping
  ASCII: `CP936`'s `TRAIL` field holds characters whose second byte is `\`, `|`, `A`, `~`, `@`, so
  anything scanning a character field for a delimiter finds bytes that are not characters. And
  because a field width is a byte count, a character can be **cut in half at the field boundary**:
  the `CUT` field is seven bytes wide and is given eight, leaving a dangling lead byte as its last
  byte. Odd memo payload lengths do the same on the FPT path. This is what VFP itself produces.

**Why a byte the C library refuses to set.** VFP stamps `0xC9` and `0x7A` on real tables, and this
port has to read real tables. The alternative — using `0xC8`/cp1250, the only non-Western page
CodeBase blesses — would gate an enum value the port already names while leaving untested the case
that actually appears in the wild: a language driver the port has never heard of. Writing the byte
directly is not a divergence; it is the same store CodeBase performs, reached without its setter's
opinion.

**Rejected — cp1250 (`0xC8`) alone.** Cheapest, and it keeps the corpus inside the set the C library
understands. But it exercises no multi-byte behaviour at all, and multi-byte is where a decoder that
counts bytes as characters silently produces wrong text rather than an error.

**Rejected — Shift-JIS (`0x7B`) as the multi-byte case.** Equally sharp (its trail range also
overlaps ASCII) and a fine alternative. GBK was chosen for breadth of lead-byte range.

**Open — the port names neither byte yet.** `CodePage` holds exactly CodeBase's five values, so both
tables resolve to `CodePage.Unknown` and fall back to the default encoding. That is asserted, not
assumed (`TableMetadataGoldenTests`). Whether the enum grows to the standard VFP language-driver set
— and whether an unrecognized marker should keep falling back silently — belongs to **step 002**,
where text decoding first depends on the answer. The corpus now makes either choice testable.

## ADR-19 — The Visual FoxPro documentation is the authority for the code-page mark · accepted

**Context.** Closes the open item in ADR-18. Step 002 decodes record text, so it needs header byte 29
to resolve to an encoding. `original/source/` cannot answer: `d4defs.h:1923-1933` names five constants
(cp0, cp437, cp850, cp1252, cp0004, cp1250), nothing in the C mentions `0xC9`, `0x7A` or "language
driver", the shipped manual never discusses code pages, and `original/examples/DATA/` carries only
`0x00` and `0x03`. Meanwhile `d4create` writes the byte verbatim and `d4open` reads it back unchecked,
so a file may carry any of 256 values — and real VFP files do.

**Decision.** **Visual FoxPro's documentation is the source of truth for mark → code page**, and it
is the only fact in `claude/specs/` that is not cited to `FILE.C:line`. The 26 marks it defines are
reproduced in `DBF-FORMAT.md` §8.1 with both source URLs: [Table File
Structure](https://learn.microsoft.com/en-us/previous-versions/visualstudio/foxpro/aa975386(v=vs.71))
for the field and [Code Pages Supported by Visual
FoxPro](https://learn.microsoft.com/en-us/previous-versions/visualstudio/foxpro/aa975345(v=vs.71))
for the values. CodeBase's five constants remain documented as *what the engine does with the byte* —
which collations it can build, which values its setters accept — never as what the format permits.

**Why VFP and not CodeBase.** `CLAUDE.md` states the target: byte-for-byte Visual FoxPro
compatibility, with CodeBase as the reference implementation we can execute. Where the two disagree
about **the format**, VFP is the format. CodeBase's list is the subset one C library chose to
interpret, and treating it as the format's definition would silently misread ordinary Cyrillic and
CJK tables — the files this port most needs to read correctly.

**`0x04` resolved in VFP's favour.** CodeBase calls it `cp0004`, "unknown codepage 4, potentially for
backwards support" (`d4defs.h:1930-1931`), and refuses it for GENERAL collation (`i4init.c:404`). VFP
documents it as 10000, Standard Macintosh. It resolves to 10000. CodeBase's own comment declines to
claim knowledge, so there is no real conflict of authorities — only a gap and a fact.

**Rejected — CodeBase's five values only.** Self-consistent, fully source-cited, and wrong about the
world: `0xC9` and `0x7A` would fall back to cp437 and produce mojibake from correctly marked files,
and `0x04` would stay unnamed.

**Rejected — the wider "xBase" mark table in circulation.** Per-language OEM blocks at `0x08`-`0x37`,
plus `0x4D`-`0x50`, `0x57`-`0x59`, `0x86`-`0x88`, `0xCC`; roughly fifty marks, many-to-one onto the
same code pages. It is not VFP's table, no authority we can name defines it, and its many-to-one shape
would push a lossy normalization into the write path. Anything outside VFP's 26 is an **unrecognized
mark**, which is a defined outcome, not a failure.

**Consequences for step 002.**

- **Three outcomes, not two.** Unrecognized mark / recognized mark whose code page .NET cannot supply
  / recognized and available. Verified on .NET 8 with `System.Text.Encoding.CodePages` registered: 24
  of the 26 resolve, while **620 (Mazovia) and 895 (Kamenicky) throw `NotSupportedException`** — they
  are not Windows code pages. `CodePageMap` currently collapses the first two together.
- **The raw byte stays authoritative.** `Table.CodePageByte` is what round-trips; a resolved code page
  is derived and must never be written back in its place.
- **`CodePage`'s shape is now a design question, not an open fact.** Its members are marks, its names
  describe code pages, and it currently mixes CodeBase's `Reserved = 0x04` in with them. Settle it in
  step 002's `DESIGN.md`.
- **Decoding is not collation, and collation is not reachable.** For a GENERAL tag CodeBase handles
  only cp1252/cp0/cp437/cp850, errors on cp1250/cp0004, and silently sets nothing for anything else
  (`i4init.c:377-404`), with translation tables declared for 1252 and 437 alone
  (`d4declar.h:3007-3009`). A 1251 or 936 table's index keys therefore have **no reference behaviour
  to port and no gate available** — a `COLLATION` scope limit to record when that capability opens,
  and a reason to keep text decoding and key building in different types.

## ADR-20 — `CodePage` names the marks; `Table.CodePageNumber` reports the number · accepted

**Context.** ADR-19 settled *what* the marks mean and left the API shape open; ADR-18's "the port names
neither byte yet" is closed by this entry. The trigger was finding that `Table.TextEncoding` already
shipped wrong answers: `CodePageMap.EncodingFor` hard-coded four numbers, so 22 of the 26 documented
marks silently fell back to cp437. The spec and the code disagreed, so by `CLAUDE.md` the code had the
bug — this is a correction to step 001's output, not step 002 scope, and needs no `DESIGN.md` gate.

**Decision.** Three rungs, byte → code page → encoding, each usable on its own:

- **`CodePage`** keeps naming the format's closed set, extended from 6 members to all 26 marks plus
  `Unmarked` and `Unknown`. A member's *value* is the mark, so `Cp1251 = 0xC9`; `Reserved = 0x04`
  became `Cp10000` (ADR-19). An enum is the right shape because the set is closed and documented, and
  it survives in generated help as the list of legal marks.
- **`Table.CodePageNumber`** is new: `int?`, the number a caller passes on to an encoding. Null covers
  both an unmarked table and an unrecognized mark; `CodePage` tells those two apart. It needs no
  encoding provider and answers even for the two marks .NET cannot supply an encoding for, because a
  code page number is *shape*, not text (ADR-17).
- **`Table.CodePageByte`** stays the value that round-trips. A resolved code page is derived and is
  never written back in its place.

**Rejected — drop the enum, expose the byte and the number only.** Genuinely simpler, and `Cp1251` does
duplicate what `1251` already says. Rejected because the mark set is a closed documented vocabulary
worth naming in the public surface and in generated docs, and because `Unmarked` versus unrecognized
reads better as two named values than as `byte == 0` versus `number is null`.

**Consequence.** `Lookup`'s failure message now distinguishes the two marks whose code page no provider
supplies — 620 Mazovia and 895 Kamenicky are FoxPro-era DOS pages Windows never defined, so "register
a provider" is not the fix and the message says so. `CodePageMapTests` gates all 26 marks, their
one-to-one-ness, the `0x04` resolution, and that resolving a number never touches an encoding.

**Open — the unmarked default rests on a weaker footing than ADR-17 claims.** ADR-17 justifies
`UnmarkedCodePage = 437` as matching "the C library's treatment of unmarked files", but the only place
the C library actually interprets `cp0` treats it as **Windows ANSI**: `i4init.c:387`, "code page 0
uses windows ansi by default", for GENERAL collation. There is no transcoding path to compare against
because the engine transcodes nothing, so neither 437 nor 1252 is *witnessed* for text. Settle it in
step 002, where the default first shows through in a decoded string.
