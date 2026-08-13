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

## ADR-13 — Dump format: DBF/FPT half settled, index half open · superseded by ADR-24

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

## ADR-21 — Text decoding recovers what it can, and an unmarked table is cp437 · accepted

**Context.** Step 002 decodes a character field for the first time, and three questions it cannot avoid
were all standing on defaults nobody chose. ADR-20 left the unmarked default open. The two code-page
corpus cases (ADR-18) made `Encoding`'s inherited behaviour visible: a GBK character cut in half by a
field boundary — `CP936.CUT` is `C(7)` given eight bytes, so it ends on a dangling lead byte in every
one of its 32 records — silently becomes U+FFFD, and a byte the code page leaves undefined (`0x98` in
`CP1251.SWEEP`) passes through to U+0098. And the dump records a character field's raw bytes with no
decoded string, so the C library is silent on what the field should *read* as.

**Decision.** Four parts, all settled here rather than by `Encoding`'s defaults.

- **An unmarked or unrecognized table decodes as cp437.** `CodeBaseEngine.DefaultEncoding` keeps the
  ADR-17 default and ADR-20's open item is closed in its favour. `i4init.c:387`'s "code page 0 uses
  windows ansi by default" is a **collation** default for a GENERAL tag, not a text default; it decides
  which translation table builds an index key, and the engine still transcodes nothing. A recognized
  marker always wins — the file is authoritative about itself (ADR-17).
- **Decoding recovers as much text as the code page allows, and never throws.** A truncated
  multi-byte character yields its complete characters plus **U+FFFD** for the dangling byte, and a
  byte the code page leaves undefined yields whatever that code page maps it to rather than an error.
  A caller who must tell data from damage reads `GetRawBytes`, which is in the public surface for
  exactly this. **The replacement character is asked for explicitly** — `Encoding.GetEncoding(n,
  EncoderFallback.ExceptionFallback, new DecoderReplacementFallback("�"))` — and this is a
  correction to what an earlier note claimed: .NET's default for the legacy code pages is an
  *internal best-fit* fallback that yields **`?`**, and `DecoderFallback.ReplacementFallback` is also
  `?`, not U+FFFD. Verified by running it. A question mark is indistinguishable from one the file
  holds, which defeats the point of marking damage at all; and taking the provider's default would
  make the behaviour depend on which provider the host registered, which ADR-17 puts outside our
  control. The encoder fallback throws because this library only reads.
- **The gate asserts decoded text, not only bytes.** Character fields are compared as strings decoded
  through the table's code page, on top of the raw-byte comparison the dump supports. The expected
  strings come from the generator's own documented input (`DEV_APPROACH.md` §4), because the dump has
  none — `Привет, мир`, `Компьютеры`, `中文测试`, `乗亅丄亊丂俓`.
- **`GetString` returns the field's full declared width, trailing blanks included.** This is what
  `f4str` does — it copies `field->len` bytes and null-terminates, with no trimming anywhere, and there
  is no `f4trim` in this source drop (`F4STR.C:206-243`). So `CP936.TEXT`, a `C(20)` holding four
  characters, reads as `中文测试` followed by twelve spaces. **Whether to offer automatic trimming as an
  opt-in is deferred to an evaluation sub-step** (`002/PLAN.md` step 8a), not decided here.

**Why.** A DBF field width is a **byte** count, not a character count, so `C(8)` in cp936 holds eight
bytes — at most four characters — and the C library truncates at the byte boundary with no regard for
where a character ends. Half characters are therefore not a corruption case but an ordinary one, which
is why the corpus pins them down in every record rather than in one. Refusing such a field would make a
whole table unreadable over one truncated character the file has held for decades; returning `中文测`
plus a marker keeps the row usable and loses nothing that `GetRawBytes` cannot recover.

**Rejected — throwing on a malformed sequence** (`DecoderExceptionFallback`). It converts a data
condition into a control-flow event on the hottest read path, and the caller cannot see the good half
of the field even though the bytes are right there.

**Rejected — dropping the dangling byte silently.** Same recovered text, no marker, and the loss is
then undetectable without re-reading the bytes.

**Rejected — cp1252 for unmarked tables.** The only C-side evidence for it is a collation default
(above), and changing the documented default of `CodeBaseEngine.DefaultEncoding` on that footing would
trade one unwitnessed number for another while breaking anyone who already set it.

**Rejected — trimming trailing blanks in `GetString`.** It reads better for the `C` field that holds a
name, and it is what most callers want. Rejected as the *unconditional* behaviour because padding is
information the file actually holds: trimming is one `TrimEnd()` at the call site, while un-trimming is
impossible, and a reader that silently discards bytes is the wrong default for a library whose whole
premise is reproducing what the file says. Deferred, not dismissed — see step 8a.

**Consequence.** Closes ADR-20's open item and `002-dbf-records-and-fields/DESIGN.md` Q3-Q6. The gate
asserts the padded form regardless of how the trimming evaluation lands, because the padded form is
what the bytes are.

## ADR-22 — Trimming a character field stays at the call site, but not as `TrimEnd()` · accepted

**Context.** ADR-21 settled that `GetString` returns the field's full declared width, blanks
included, because that is what `f4str` returns and because un-trimming is impossible. It deferred the
separate question of whether the library should *offer* trimming, as step 002's sub-step 8a. Almost
every caller reading a `C` field holding a name or a code wants it trimmed, so the question is not
whether trimming happens but where.

**Decision.** **No trimming mode, and no trimmed accessor for now.** `GetString` is the only string
accessor and it returns the padded width. What this evaluation adds is a documented warning rather
than an API: the obvious call-site fix, `GetString(f).TrimEnd()`, is **subtly wrong**, and the XML
docs on `GetString` say so. Callers should write `TrimEnd(' ')`.

**Why not a `TrimTrailingBlanks` mode** on the engine or the table. It is the same objection Decision 4
of step 002 raised against porting `errGo`: a library-wide switch makes every call site ambiguous
about what it returns, and the ambiguity is worst exactly where it matters, in code that reads a field
far from wherever the flag was set. Two tables opened by the same engine would answer differently for
the same bytes, and a helper function taking a `Table` could not know which it had.

**Why not a `GetTrimmedString` accessor**, which has none of the mode's problems and was the leading
candidate. Rejected **for now**, on the ground rule that speculative API is not added ahead of a
caller: it is two lines whenever it is wanted, nothing in the port needs it, and adding a second
string accessor before there is a consumer commits the public surface to a distinction that may want
different edges later — trailing blanks only, or leading too, or the memo path as well. Revisit when
the first real caller appears; the cost of adding it then is the same as the cost of adding it now,
and by then its edges will be known.

**The trap worth documenting.** `string.TrimEnd()` with no argument trims *all* trailing whitespace,
including tabs, carriage returns and newlines. A DBF character field is padded with spaces
specifically, and a tab or a newline at the end of one is **data** — a fixed-width field is a
perfectly ordinary place to store a line of text. So the naive fix silently deletes content for
exactly the callers most likely to reach for it. `TrimEnd(' ')` is right; `TrimEnd()` is a data-loss
bug that no test in this repository would catch, because the corpus pads with spaces only.

**Consequence.** Closes step 002's sub-step 8a. No public surface changes; the `GetString`
documentation gains the warning. If a `GetTrimmedString` is added later it must trim `' '` only, and
this entry is the reason.

## ADR-23 — Compressed memo entries are refused until the corpus can gate them · open

**Context.** An FPT entry declares a type; type 3 means the payload is compressed. It is a CodeBase
extension — `code4memoCompress`'s own comment says "note that those memo files are then incompatible
with FoxPro" (m4memo.c:31-32) and `d4data.h:4296` records the type as a 2002 addition. Step 003 had
to decide what a reader does when it meets one.

**Decision.** **Refuse, with a message that explains rather than merely rejects.** `MemoReader`
throws on type 3, names the field, the record and the block, and says whether the table was created
with the extension enabled — `FeatureFlags.MayHaveCompressedMemos` is parsed from DBF `flags[1]` at
open (D4OPEN.C:2193-2195), so the two cases read differently: "the table was created with compressed
memos enabled, so others are likely" versus "the table does not declare compressed memos, so this
entry is unexpected".

**Why — and what the reason is *not*.** Three things were run together in an earlier draft and are
kept apart here, because the wrong one was doing the work:

- **"zlib is not in the source drop"** — true and irrelevant. Reading needs no dependency at all:
  `System.IO.Compression.ZLibStream` is in the base class library, so ADR-17's no-dependency rule is
  untouched. Adding zlib to the generator is ordinary work.
- **"the stream format is unknown"** — no longer true. `source/zlib.h` is in the drop (v1.1.4) and
  the sibling compressor that survived, `connect4lowCompress`/`connect4lowUncompress`
  (c4conlow.c:69-152), uses zlib's high-level `compress2()`/`uncompress()`, which are RFC 1950
  wrapped rather than raw deflate. The entry layout is 4-byte native-endian uncompressed length then
  the stream (m4file.c:199-212), and the trailing flag on `c4compress` that produces the prefix is
  `1` at the memo call site and `0` everywhere else.
- **"no corpus case exists"** — true, and the actual reason. Every one of the 153 non-empty entries
  in the corpus is type 1, because the generator compiles with `S4OFF_COMPRESS`. An inflate path
  with nothing to check it against is a decoder no test can contradict, which `DEV_APPROACH.md` §4
  rules out.

**Rejected — treating "CodeBase-only" as a reason to skip it.** An earlier draft argued that type 3
is not worth supporting because no Visual FoxPro file will hold one. That is backwards for this
project and the argument is recorded so it is not made twice: this is a port *of CodeBase*,
`CLAUDE.md` requires that files the original C library writes are read correctly here, and the
likely user is migrating an existing CodeBase application. If that application ever called
`code4memoCompress`, its memo files hold type-3 entries and refusing them refuses the data the user
came for. Being CodeBase-only argues *for* support.

**Rejected — inflating now and gating later.** It would very likely work, and "very likely" is not a
gate. A file from a `S4COMPRESS_QUICKLZ` build (D4all.h:126) would also inflate to nonsense rather
than to an error, because nothing in the entry records which algorithm wrote it.

**Open — what would close this.** A `CORPUS` step: add zlib to the generator, reconstruct the
`c4compress` wrapper from the layout the reader pins down, and add a case with `code4memoCompress`
enabled and a payload longer than one block (writing is opt-in *and* only fires above one block,
m4file.c:734-743). Once that case exists the reader is a few lines over `ZLibStream`. The
recommendation is to do it as its own step; it was deliberately kept out of 003 so that step stayed
one subsystem wide.

## ADR-24 — The index half of the dump is a sibling file, not new sections · accepted

**Context.** ADR-13 settled the DBF/FPT half of `<NAME>.dump.txt` — raw header, on-disk descriptors,
the C library's field view, per-record bytes and decoded values, memo reference and contents — and
left the index half to be designed when CDX cases arrived. Step 004 brings them.

**Decision.** The index half lives in **`<NAME>.cdx.dump.txt`** (`<NAME>.idx.dump.txt` for a
single-tag file), a sibling of the table's dump rather than more sections inside it. It carries the
file-level header, the tag-directory entries, and per tag: the header fields, the `(key bytes, record
number)` sequence in navigation order, and every block's structure — attribute, key count, siblings,
the leaf bit widths and masks, and **each leaf entry's stored duplicate and trail counts**. The
`d4check` result is recorded in it. The DBF/FPT half of ADR-13 stands unchanged; this entry supersedes
only its open half.

**Rejected — extending `<NAME>.dump.txt` with `[tags]`/`[keys]` sections.** Three reasons. The two
halves are produced by different generator code over different library APIs, and a file per
concern keeps a regression in one from rewriting the other's bytes. A deep table's index dump is
larger than its record dump, and mixing them makes both unreviewable in a diff — legibility in review
is the whole reason the dump is text. And the strict section reader (`CorpusDump` refuses a section
it has never heard of, so a hole cannot pass as success) stays simpler with one reader per file shape.

**Rejected — dumping the index by re-reading the bytes**, as the DBF header half legitimately does.
A DBF header is a few shifts and an or; a bit-packed leaf is the highest-risk decode in the port
(R1), and a generator that interprets it would be grading our own homework — our writer and our
reader agreeing with each other and with nothing else. Every value in the index dump therefore comes
from the C library's own structures: keys from `tfile4key`/`tfile4recNo`, block structure from the
live `B4BLOCK` at `tfile4block(t4)`, per-entry counts from the library's own `x4dupCnt`/`x4trailCnt`
macros (d4declar.h:1807-1854).

**Consequence.** ADR-16 still governs growth: the sections gain optional tokens, never new columns.
Step 005 adds a seek section the same way.

## ADR-25 — The single-tag `.IDX` corpus case is derived from a single-tag `.CDX` · accepted

**Context.** Reading a compact single-tag index file is in scope (`PORTING-PLAN.md` §2.1): CodeBase
reads one — a tag header at file offset 0 with `typeCode < 0x40`, the tag named after the file
(u4namePiece, i4index.c:1694, 1814-1825) — and a CodeBase application that called
`i4open("X.IDX")` has these files. But the library **cannot write one**: `i4create` always builds a
compound file with a tag directory at `typeCode = 0xE0` (i4create.c:847). So there is no way to have
the reference implementation generate the case, and `original/examples/DATA/` contains no `.IDX` at
all (and is never a gate).

**Decision.** The generator builds `IDXONE.CDX` with exactly **one** tag and derives `IDXONE.IDX`
from it with the smallest edit that exists: **copy the 1024-byte tag header from offset 1024 to
offset 0, and clear the compound bit** (`typeCode` 0x60 → 0x20). Nothing else moves. Node numbers are
byte offsets, so leaving every tree block where it is keeps `root`, both sibling pointers and every
child pointer valid; the old header copy at offset 1024 becomes unreferenced space, which the format
tolerates because freed blocks are ordinary. The C library then **opens the derived file and writes the
dump from it**, and both files stay in the corpus, so the same tree is read through both shapes and the
sequences must agree.

**Correction, found while executing this step.** The paragraph above originally had `d4check` certify
the derived file. It cannot — and **no** single-tag file can be checked by it, whoever wrote it.
`i4checkBlocks` flags the two blocks of the tag directory's header (i4check.c:889-894) and then walks
the tag list flagging each tag's header (i4check.c:905-914); in a single-tag file the tag list *is*
`{tagIndex}` (i4index.c:1824) and its header is at node 0, so the flag is already set and it returns
`e4index`. The witness is therefore the other half of the plan, which is the stronger one for what the
derivation actually claims: the generator walks `IDXONE.cdx` and `IDXONE.IDX` side by side and requires
the same key and record number at every one of the 300 steps. `d4check` has already certified the
`.cdx`, whose tree blocks these are byte for byte, so what is left to prove is only that the header at
offset 0 with the compound bit cleared reads as one tag over that same tree — which an identical walk
proves exactly. Recorded as a format fact in `CDX-FORMAT.md` §2.1.

**Why this is honest.** Generation authority and verification authority are different things. The
bytes of every tree block are the C library's, unmodified; the derivation asserts exactly one claim —
that the compound bit is what distinguishes the two shapes — and the reference implementation then
reads the result and is held to agreeing with itself about it. Compare the alternative, which is a
corpus case with no verification at all.

**Rejected — hand-assembling an `.IDX` from `CDX-FORMAT.md`.** That is the self-consistent
misunderstanding `DEV_APPROACH.md` §4 exists to prevent, and it would also have to rewrite every node
number in the file.

**Rejected — leaving `.IDX` out of scope.** It is the same S4FOX format through a different entry
point; support costs one strategy resolved at open. Non-compact FoxPro 2.x `.IDX` files remain out —
their flags byte carries neither 0x20 nor 0x40, so `typeCode < 32` and the C library refuses them
too (i4index.c:1706).

**Consequence.** The derivation is generator code with a comment, not a checked-in patched file, so
regeneration reproduces it. It is listed for external live-VFP confirmation under ADR-11.

## ADR-26 — The key pad character is supplied to the CDX reader until `EXPR` exists · accepted

**Narrowed by ADR-27**, which limits everything below to *machine-collated* tags: a non-empty
`sortSeq` fixes the pad character at `'\0'` on its own, so only machine collation is ambiguous.

**Context.** Reconstructing a stored key needs the byte its trail count stands for: `' '` for a
machine-collated character key, `'\0'` for numeric, date, currency and collated keys
(i4init.c:557-602). **The file does not store it, and — for a machine-collated tag — it is not
derivable from the header.** The C
library knows because it parses the key expression and asks its type (`expr4type`); the header holds
the expression *text* and no type at all. `CDX-READ` is otherwise reachable without the expression
engine, because keys are read rather than recomputed.

**Decision.** The CDX reader takes the pad character as an **input**: a `PadCharacterResolver`
delegate supplied at open. The tag directory uses the known fact `' '` (i4init.c:520-525). Golden
tests pass the `pChar` the corpus dump records — a value produced by the reference implementation,
used as test *input*, which `DEV_APPROACH.md` §4 permits. When `EXPR` lands, the resolver is
implemented over the expression's type and a test asserts the derived value equals the dump's for
every corpus tag; only then does the reader become self-sufficient.

**Rejected — inferring the key type from `keyLen`.** 8 bytes means numeric except when a character
field is 8 wide. A wrong pad character corrupts the padded tail of every key in the tag, silently,
and breaks comparison rather than parsing — the failure class this project cares most about.

**Rejected — exposing keys as significant bytes plus a trail count and letting callers pad.** It
pushes a format detail onto every caller and makes the corpus gate compare something that is not a
key.

**Consequence.** `CDX-READ` cannot be called *complete* until `EXPR` supplies the resolver; step 004
closes the decode, not the dependency. `PORTING-PLAN.md` §5 records it under `CDX-READ`'s "needs".

## ADR-27 — Collated tags are read without collation tables; only machine-collated tags need a supplied pad character · accepted

**Context.** ADR-26 said the pad character a key's trail count stands for is "not derivable from the
header", and step 004's first draft refused any tag whose `sortSeq` was not machine collation, on the
grounds that `COLLATION` owns the tables. Both statements conflated **generating** a key with
**reading** one.

**Decision.** Step 004 reads machine-collated **and GENERAL-collated** tags. What a reader actually
needs, all of it table-free:

- **Selecting the collation is a string compare and a code page.** `""` ⇒ machine; `"GENERAL"` ⇒
  cp1252 / cp437 / cp850 by **the data file's** code page (cp0 defaults to cp1252; cp1250 and cp0004
  are refused by the C library itself); `"CBnnnnn"` ⇒ a custom ordinal; anything else ⇒ `e4index`
  (i4init.c:372-418).
- **A non-machine collation fixes the pad character at `'\0'`** (i4init.c:596-604). ADR-26's problem
  therefore exists *only* for machine collation, where it is `' '` for a character key and `'\0'` for
  numeric, date and currency keys. This entry narrows ADR-26 to that case; the resolver seam and the
  `EXPR` dependency are unchanged for it.
- **Traversal compares nothing.** The tables exist to turn a *search value* into key bytes, which is
  step 005's seek, and to re-derive a key from a field value, which is the `COLLATION` gate.

**Why do it now rather than later.** `KEY-COLLATION.md` §3.7 records that the GENERAL head+tail key
layout is **verified from source only** — not one of the 33 shipped sample CDX files carries a
`GENERAL` `sortSeq`, so nothing in the repository has ever confirmed it against real bytes (R11). A
generated case closes that gap and gates three things machine collation cannot reach: the head/tail
layout itself, `keyLen = 2 × the field width` (`keySizeCharPerCharAdd`, i4create.c:1040) — so `keyLen`
stops tracking a field's width, which a reader could otherwise assume forever — and `pChar = '\0'` on
a **character** tag, which is precisely what a wrong pad-character assumption corrupts. The cost is
one 32-record table with the same field indexed twice: the GENERAL arrays are already compiled into
the generator (`i4conv.c:309` includes `coll4arr.c`; cp1252, cp437 and cp850 have static arrays).

**Rejected — refusing every non-machine `sortSeq` until `COLLATION` lands.** It would have left the
port unable to open ordinary VFP indexes (GENERAL is VFP's default collation for a marked table) for
no technical reason, and left the spec's least-verified section unverified for longer.

**Still refused:** `"CBnnnnn"` custom collations, whose tables are disk-loaded
(`collate4test` is `MUST4LOAD_ARRAY`, i4conv.c:363-365) so no corpus case can gate them; and any
`sortSeq` the C library rejects, reproducing i4init.c:418 with the collation named.

**Consequence.** Seeking a *collated* tag still needs `COLLATION`, because the search value must be
translated before it is compared, and the descending-seek key increment is collation-dependent
(I4TAG.C:2092-2151). Step 005 states that boundary explicitly rather than discovering it.

## ADR-28 — A tag's key type comes from the table's field descriptors when its expression is a bare field name · accepted

**Context.** ADR-26 established that a machine-collated tag has to be *told* the byte its trailing-pad
counts stand for, because a tag header records the key expression's *text* and not its type, and said
`EXPR` would answer it. Step 006 wires a tag to a table, and at that point the answer is available from
somewhere much smaller than an expression engine: the table's own field descriptors.

**Decision.** When a tag's expression is, after trimming, **the name of a field of the table it belongs
to** — matched case-insensitively, as the C library upper-cases field names on open — that field's type
*is* the key's type, and the pad byte follows from the C library's own mapping (i4init.c:557-604):
`' '` for a character key under machine collation, `'\0'` for a character key under any other collation
and for every numeric, currency, date and datetime key. Anything else — a name that matches no field, a
composite expression such as `UPPER(NAME)` or `STR(ID)+CITY` — is **refused when the tag is first
used**, naming the expression, and waits for `EXPR`. In practice that means the pad byte is resolved
lazily, on the first `SelectTag` or `…Indexed` call naming the tag, and never at open.

**Why this is exact and not a heuristic.** The rule is not "guess the type from the expression"; it is
"if the expression is a field reference, the type is that field's". That is what `expr4parseLow`
computes for the same input, by a much longer route. All 18 corpus tags are of this shape, as are the
overwhelming majority of the 56 tags in the shipped `original/examples/DATA/` samples — indexes on a
bare field are what applications write.

**Refused at selection, not at open.** One tag with an expression this port cannot type must not make
the whole table unopenable, and the C library agrees by construction: it does not care about a tag
until something asks it to. So `OpenTable` succeeds, `Tags` lists every tag the file holds, and only
`SelectTag` on an unresolvable one fails.

**Rejected — inferring the type from `keyLen`.** Eight bytes means a numeric key except when a character
field is eight wide, and four means an integer except when a character field is four wide. A wrong pad
byte corrupts the padded tail of every key in the tag, silently, and breaks comparison rather than
parsing — the failure class this project cares most about.

**Rejected — waiting for `EXPR`.** It would leave the port unable to navigate by index at all, for the
sake of tag shapes no corpus case holds, and `EXPR` is a substantially larger subsystem than the step
that needs this.

**Consequence.** ADR-26's resolver seam stays, and this is its first real implementation; a test may
still supply a pad byte directly, which is what the internal 004 and 005 gates do. When `EXPR` lands it
replaces the refusal branch only, and a test then asserts that the derived type agrees with this rule
for every corpus tag — the two must never disagree where both apply.

---

## ADR-29 — A selected tag skips like `d4skip`; the four `…Indexed` methods position like `d4top` · accepted

**Context.** Step 006 gives tag-order navigation two surfaces: `Top`/`Bottom`/`Skip` with a tag
selected, and `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed`, which name the tag
per call. They share one cursor per tag and visit the same records in the same order. They disagree on
one thing: what a move that could not be made leaves behind.

**Decision.** `Skip` reproduces `d4skip`'s tag path exactly (CDX-FORMAT.md §7.1): a backward skip that
runs out of entries **stops on the tag's first record, which stays readable**, and reports only the
beginning; a forward one ends past the last record; a skip of zero stays put without consulting the tag.
The four `…Indexed` methods are **positioning calls**: a move that cannot be made reports `NoRecord` and
leaves the cursor past the end with both flags, the way `Top` and `Bottom` do on an empty table.

**Why the difference is deliberate.** It is the difference the port already has between `Skip` and `Go`,
and each shape suits its caller. A skip is relative, so ending up against a boundary with the boundary
record still readable is useful and is what the C library does. `GoNextIndexed` is a positioning call
used as a loop condition — `for (go = GoFirstIndexed(t); go == Ok; go = GoNextIndexed(t))` — and a
"failed" move that left a readable record on the boundary would either loop forever or need the caller
to compare record numbers to notice. Reporting no record ends the loop and cannot be misread.

**Rejected — one shape for both.** Making the four skip-shaped costs the loop termination above.
Making `Skip` position-shaped would diverge from `d4skip` on a behaviour a caller can see, for symmetry
that no caller asked for.

**Consequence.** The gate walks the whole corpus through *both* surfaces and requires the record
sequences to be identical, which is the part that must never drift. The flag difference at the ends is
tested per surface, and stated in the XML docs of the methods that have it.

---

## ADR-30 — Stepping in a tag's order from a record the tag does not list is refused, not answered · accepted

**Context.** `d4skip` re-derives the current record's key through the tag's expression and seeks with it
before stepping (d4skip.c:1245-1275), so it can carry on from the nearest key even when the record has
no entry of its own — which happens on a filtered tag, on a unique tag, and whenever another process
removed the key. Without `EXPR` this port cannot derive that key; it can only look for the record
itself, by walking the tag.

**Decision.** When the record the cursor is on has no entry in the selected tag, tag-order stepping
**throws** `ErrorCode.NotSupported`, naming the record, the tag and the expression that would have to be
evaluated. It does not report end of file.

**Rejected — answering end of file.** It is a plausible-looking wrong answer: the caller gets a record
set that silently omits everything after the gap, with nothing saying why. "A wrong record set is far
worse than a slow one" cuts the same way for navigation as for the optimizer.

**Rejected — walking the tag for the *nearest* key instead of the exact record.** Landing "near" a
record needs the record's key, which is the thing that is missing; comparing records by number would
give an order the tag does not have.

**Consequence.** A filtered or unique tag is fully walkable — every record it lists is reachable from
`Top` or `GoFirstIndexed` — and only mixing `Go(n)` to a record *outside* the tag with a tag-order step
is refused. `EXPR` removes the restriction by supplying the key, and the refusal branch is the only
thing it replaces.

---

## ADR-31 — `Table.HasProductionIndex` stays removed; `HasIndex` is the only index question a caller can ask · accepted

**Context.** Step 006 deleted `Table.HasProductionIndex`, a public property that reported the header's
production-index bit (`hasMdxMemo & 0x01`, `DbfHeader.cs:114`). It was designed in step 001
(`001-dbf-open-and-header/DESIGN.md:89`) before the index actually opened. The deletion was made
without an ADR and was carried as an open judgement call. The 002–005 audit did not reach it; this
closes it either way.

**Decision.** The removal stands. `Table.HasIndex` (`Table.cs:116`, `opened.Index is not null`) is the
public surface, and it reports the opened file rather than the header's claim about one.

**Why.** The two could never disagree. A header that declares a production index whose file is missing
is an error at open, so by the time a caller holds a `Table` the bit and the open file agree by
construction. Two properties that are provably equal are one property and a question about which to
trust. The one that reports a fact about the object the caller holds is the one to keep.

**Rejected — deprecate rather than delete.** `[Obsolete]` buys a migration path for callers, and there
are none: the property had no test and no caller, and nothing in this library is released or pushed.
A deprecation cycle for a property nobody ever called is ceremony.

**Rejected — keep it as the header's raw claim.** It would be honest only if a caller could act on the
difference, and there is no state in which the difference exists.

**Consequence.** The header bit is now not publicly reachable at all: `DbfHeader` is `internal`
(`DbfHeader.cs:16`) and nothing re-exposes it. That is accepted — the bit's only meaning to a caller is
"is there an index", which `HasIndex` answers more truthfully. If `WRITE` ever needs to set the bit
without opening the index, it does so through the internal header, not a public property.

---

## ADR-32 — The `'7'` datetime-with-milliseconds type is out of scope, and its code goes with it · accepted

**Context.** `FieldResolver.ReadableTypes` (`FieldResolver.cs:33-34`) omits `'7'`, so a table carrying
one cannot be opened, while `FieldValueDecoder` and `FoxDateTime` carry live `'7'` branches — a natural
width entry (`FieldValueDecoder.cs:33`), a refuse-as-number entry (`:62`), a type guard (`:199`), and
`FoxDateTime.ToText`'s `includeMilliseconds` parameter with its two branches (`FoxDateTime.cs:72,94,102`).
The 002–005 audit raised the asymmetry (E1) and proposed admitting the type, on the grounds that "the
decoder is ready" and that a real FoxPro 2.x table would otherwise be refused.

**Decision.** `'7'` is **out of scope**. Remove the dead branches, the `includeMilliseconds` parameter,
the unit test that exercises the millisecond path
(`FoxDateTimeAndCurrencyTests.ToText_KeepsTheMillisecondsForTheVariantThatHasThem`), and the
`field.Type == '7'` sub-expression in `RecordGoldenTests.cs:161`, which can never evaluate true. The
refusal at open stays and is the honest statement of support.

**Why — the scope already said so.** `PORTING-PLAN.md` §2.1 lists the in-scope field types as
`C, N, F, D, L, M, G, I, B, Y, T, H` plus `'0'`. `'7'` is not among them. `FieldResolver` implements
the declared scope correctly; it is the decoder branches that were never in it.

**Why — the audit's two premises are both wrong.** *"A real FoxPro 2.x table would be refused"*: it
cannot happen. `'7'` is a CodeBase extension (`D4CREATE.C:1572-1574`, `DBF-FORMAT.md` §5) admitted only
when `version >= 0x30` compared **signed** (`DBF-FORMAT.md` §2.1), and every FoxPro 2.x version byte is
`0x80` or above, so it compares below `0x30` and the C library denies the type itself. *"The decoder is
ready"*: it is not. `BlankRecord.ZeroBlankTypes` is `['I','Y','T','B','X','Z']` and omits `'7'`, while
`f4blank` zero-fills `r4dateTimeMilli` (`original/source/f4field.c:145`). Admitting `'7'` today would
blank it to eight spaces where the C library writes eight zeros — a wrong byte, not a missing feature.

**Why — nothing can gate it.** No corpus case has a `'7'` column and the generator has no case that
would produce one. Admitting the type means admitting an ungated read path, against the rule that every
capability is gated against the C library's own view.

**Rejected — admit `'7'` and add a corpus case.** That is a defensible future step: teach the generator
a `'7'` column, fix `BlankRecord`, gate the text rendering. It is not this step, and it widens v1 scope
past `PORTING-PLAN.md` §2.1 for a type Visual FoxPro never writes — and VFP compatibility, not CodeBase
feature parity, is what the port is for.

**Rejected — leave the dead branches in place as groundwork.** Dead code behind a refusal reads as
support to anyone auditing the type list; the 002–005 audit read it exactly that way and filed it as a
gap. The branches are three lines and a parameter; they cost more as a false signal than they save.

**Consequence.** `'7'` moves to `PORTING-PLAN.md` §2.3 (LATER maybe) beside the other CodeBase-only
extensions, and `FoxDateTime` renders `'T'` only — rounding at 500 ms, which is the sole behaviour the
corpus witnesses. If `'7'` is ever wanted, this ADR is superseded and the work is: generator case,
`BlankRecord`, `ReadableTypes`, and the text path back.

---

## ADR-33 — Dates before AD 1 are out of scope; year zero is a malformed field, reproduced not supported · accepted

**Context.** The 002-005 audit raised (E3) that `FoxDate.DayOfYear` computes year 0 as a leap year while
the comment directly above it says year 0 is 1 BC and is not one. Reading `D4DATE.C` settled what the C
library does and exposed a scope question the port had never stated: what, if anything, does this library
promise about dates before AD 1?

**What the C library does.** `c4ymdDoY` (D4DATE.C:324) computes the leap flag as
`((year%4 == 0) && (year%100 != 0)) || (year%400 == 0)`, which for year 0 takes the `%400` branch and
yields 1. This is load-bearing rather than incidental: `c4ytoj(0)` returns -366, so year 0 runs 366 days,
and `-366 + 366 + 1721425` lands exactly on `JULIAN4ADJUSTMENT`, documented as 0000/12/31. A common year
zero would move every date from AD 1 onward by one day and require the constant to be 1721424. The
comment above the line (D4DATE.C:323) claims the opposite and was never applied; the same author's
sibling fix that same day, the negative-year correction in `c4ytoj`, was.

**Decision.** Dates before AD 1 are **out of scope** (`PORTING-PLAN.md` §2.2). The port makes no promise
about BC semantics, adds no corpus case, and gates nothing. It continues to reproduce the C library's
arithmetic exactly, including year 0 as leap, because that arithmetic is what every stored date key was
built from. `FoxDate.ToDate` keeps returning null for year below 1, and the code comments now say why.

**Why this costs nothing.** A stored date cannot be BC. `date4long` accepts ASCII digits and spaces only
(D4DATE.C:670-699), so there is no sign character and no year below zero is representable in the eight
bytes. `DayOfYear`'s `year < 0` guard is defensive and unreachable from any stored field, and
`c4ytoj`'s negative-year correction is exercised by exactly one input in practice: year 0, where
`yr = -1`. Scoping BC out therefore removes no capability a real file could ask for.

**Why year zero still has to work.** It is not a date; it is what a **malformed field decodes to**. A
space counts as a zero digit in the conversion, so a blank year, or a partial write such as `"    0229"`,
comes out as year 0 with whatever month and day follow. Refusing it would diverge from the C library on
input a real file can contain, and the resulting Julian number feeds a date tag's index key — so getting
it wrong is a wrong record set, which the project's ground rule ranks worse than anything else here.

**Rejected — "fix" year zero to a common year.** It would break `JULIAN4ADJUSTMENT`, shift every date
from AD 1 onward by a day, and make every date index key disagree with the C library and with Visual
FoxPro. Compatibility means reproducing exact bytes, not equivalent behaviour, and this is the clearest
case of it in the date code.

**Rejected — refuse year zero at decode.** Cleaner-looking and wrong for the same reason: the C library
answers, so this port answers identically.

**Rejected — arguing the calendar.** Whether year 0 "is" 1 BC is a numbering convention, not a fact about
the world. Historical BC/AD reckoning has no year 0 at all; astronomical numbering and ISO 8601 define
0 = 1 BC so that arithmetic can cross the boundary. The C library's constants commit to the astronomical
convention while its comment argues from the historical one, which is how the discrepancy arose. The port
does not adjudicate it -- it reproduces the constants and records the ambiguity.

**Consequence.** `DBF-FORMAT.md` §6.3 gains the leap-year fact, the un-applied-comment note and the
"no stored date can be BC" statement, all cited. `FoxDate`'s class summary states the scope limit, and
the two inline comments that were wrong now say what the code actually does and why. The audit's E3 is
closed as **the code is correct and the comment was wrong**, not as a defect fixed.
