# Project state

**Updated:** 2026-08-10 · `main` @ `37b4592` — **uncommitted**: the two code-page corpus cases and
their generator changes, the code-page resolution they exposed as wrong (three library files), the
tests for both, and the documentation.
**Active step:** none. [`claude/dev/001-dbf-open-and-header`](claude/dev/001-dbf-open-and-header/)
closed — the metadata half of `DBF-READ`, 341 tests green.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF's metadata can be read.** `net/CodeBase.Net.sln` builds four projects — `CodeBase.Net`
(**no NuGet dependencies** by design, ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden` and
`CodeBase.Net.TestUtils` — and `dotnet test` is green on **341 tests**, 116 of them golden.

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");
foreach (FieldDefinition f in table.Fields) { /* name, type, length, offset, nullability */ }
```

Opening a table reads its header, its stored descriptors and its resolved field table, and opens the
memo file beside it when the header declares one. Every one of those is gated against the C
library's own dump for all seven corpus tables. It also resolves the code page mark: `CodePage`,
`CodePageNumber` and `CodePageByte` answer for all 26 marks Visual FoxPro documents, without needing an
encoding provider (ADR-19, ADR-20). **No record or field value is readable yet** — that is step 002,
and it is what turns a resolved code page into decoded text.

**`test-files-generator/`** builds and runs end to end (Windows/MSVC):

```bat
build-lib.bat     :: 137 TUs -> obj\codebase.lib   (~2 min; ~1.4 s incremental)
build-gen.bat     :: src\*.cpp -> bin\testgen.exe
bin\testgen.exe   :: -> bin\out\
copy-corpus.bat   :: -> ..\net\corpus\
```

**`net/corpus/`** — seven DBF cases, no indexes, 32 records each, each with a `<NAME>.dump.txt` of
expected header/descriptor/record values read back through the C library:

| File | Version | Covers |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x set `C N D L`, no memo |
| `VFPTYPE.DBF` | `0x30` | every non-memo VFP type `C N F D L I B Y T` |
| `F2XMEMO.DBF` + `.fpt` | `0xF5` | FoxPro 2.x memo, 10-byte ASCII memo reference |
| `VFPMEMO.DBF` + `.fpt` | `0x30` | memo + binary types `M X G Z`, 4-byte binary reference, payloads straddling the 512-byte FPT block boundary |
| `VFPNULL.DBF` + `.fpt` | `0x30` | nullable fields: the hidden `_NullFlags` descriptor, null-bit ordinals that are not field indexes, a two-byte bitmap, and the memo/null interaction |
| `CP1251.DBF` + `.fpt` | `0x30` | a marked code page, single-byte (byte 29 = `0xC9`): `0x80`-`0xFF` swept whole across records 1-8, high-byte text in record and memo |
| `CP936.DBF` + `.fpt` | `0x30` | a marked code page, multi-byte (byte 29 = `0x7A`): GBK trail bytes that are ASCII (`\`, `|`, `A`), and characters cut in half at a field boundary and at a memo length |

Verified against the specs: `headerLen` = 32 + n×32 + 1 (+263 for `0x30`); 263 reserved bytes zero;
`flags[8]` and `autoIncrementVal` zero (genuine-VFP shape); no trailing `0x1A`; FPT `blockSize` = 512
big-endian; `X`/`Z` stored as `M`/`C` with descriptor flag `0x04`. Regeneration is byte-identical.

**Documentation:** seven format specs, the porting plan, the development approach and the decision
log. `claude/specs/QUERY-OPTIMIZER.md` **does not exist** and is the gap that matters — the optimizer
is the only in-scope subsystem with no source-cited spec (risk R13).

---

## 2. Last session (2026-08-10)

**Two code-page-marked corpus cases**, `CP1251.DBF` (byte 29 = `0xC9`) and `CP936.DBF` (`0x7A`), both
`0x30` with a memo. This closes the last thing blocking step 002's text decoding: byte 29 was `0x00`
in every table, so a reader that ignored it entirely would have passed the whole suite. **ADR-18**
carries the reasoning; `net/corpus/README.md` says what each case pins down. Three facts worth
knowing before touching them:

- **Neither marker is one CodeBase will set.** `c4setCodePage` accepts only cp0/437/850/1252/1250
  (`c4set.c:727-745`), but `d4create` writes `CODE4.codePage` into the header verbatim with no
  validation (`D4CREATE.C:1391`) and `d4open` reads it back unchecked (`D4OPEN.C:2217`). The cases
  assign the field directly and restore `cp0` straight after the create. `0xC9`/`0x7A` are what VFP
  stamps on such tables, so the files stay realistic. `DBF-FORMAT.md` §8 now records all of this.
- **The multi-byte case is the one that matters.** GBK trail bytes overlap ASCII, so `CP936`'s
  `TRAIL` field holds characters whose second byte is `\`, `|` or `A`; and because a field width is a
  byte count, its `CUT` field (seven bytes, given eight) always ends on a **dangling lead byte**.
  Memo lengths 63 and 401 do the same on the FPT path. A decoder that counts bytes as characters
  produces wrong text here rather than an error.
- **The port now resolves both marks.** See the code change below.

**The authority for byte 29 is settled: Visual FoxPro's documentation, not CodeBase's constants**
(**ADR-19**, table in `DBF-FORMAT.md` §8.1). `original/source/` has no enumeration of the mark at all
— five constants, no mention of `0xC9`/`0x7A`/"language driver" anywhere in the C, nothing in the
shipped manual, and only `0x00`/`0x03` across the 46 sample tables — so this is the **one spec fact
with no `FILE.C:line` citation**, sourced instead to two named Microsoft pages. Four consequences:

- **26 marks, one-to-one.** The wider "xBase" table in circulation (per-language OEM blocks, ~50
  marks, many-to-one) is not VFP's and is out of scope; anything outside the 26 is *unrecognized*,
  which is a defined outcome rather than a failure.
- **`0x04` goes to VFP:** 10000 Standard Macintosh, not CodeBase's `cp0004` placeholder.
- **Three outcomes, not two.** Verified on .NET 8 with the provider registered: 24 of the 26 resolve,
  **620 (Mazovia) and 895 (Kamenický) throw** — they are not Windows code pages, so no provider helps.
  Both still resolve to a mark and a number; only `TextEncoding` fails, and its message says why.
- **Collation is out of reach for these pages.** For a GENERAL tag CodeBase handles only
  cp1252/cp0/437/850, errors on cp1250/cp0004 and silently sets nothing for anything else
  (`i4init.c:377-404`); translation tables exist for 1252 and 437 alone. A 1251 or 936 table's index
  keys have **no reference behaviour to port and no gate available** — a `COLLATION` scope limit.

**All 26 marks are implemented** (**ADR-20**), which was a *bug fix*, not new scope: `TextEncoding`
hard-coded four code page numbers, so 22 of the 26 marks silently decoded as cp437 — the spec and the
code disagreed and the code was wrong. `CodePage` now names all 26 (its members are marks, so
`Cp1251 = 0xC9`, and `Reserved = 0x04` became `Cp10000`), `Table.CodePageNumber` is new (`int?`, null
for unmarked and unrecognized alike, needing no encoding provider), and `Table.CodePageByte` remains
the value that round-trips. `CodePageMapTests` gates every mark, the one-to-one property, the `0x04`
resolution and provider independence. **Left open by ADR-20:** ADR-17 justifies the unmarked default as
cp437 "matching the C library", but the only place the C library interprets `cp0` treats it as Windows
ANSI (`i4init.c:387`) — and since the engine transcodes nothing, neither number is actually witnessed
for text.

The other five cases regenerated **byte-identical**, so the shared generator changes disturbed
nothing. `util.h` gained `TEXTBYTES`/`assignText`: text outside ASCII is written as byte arrays,
never as a source literal, because what a literal becomes depends on the compiler's charsets.

Test wiring: the two tables joined the golden suite by discovery, so only three places needed an edit
— the documented-name list, the corpus-size gate, and the code-page assertion, which is now explicit
per table instead of "every table is unmarked". Plus the new `CodePageMapTests` and a golden theory
over `CodePageNumber`. **341 tests green, 116 of them golden.**

**Checked end to end rather than trusted because the suite is green.** Driving the library over the
checked-in files: `CP1251` reports `0xC9` / `Cp1251` / `1251` / `windows-1251` and `CP936` reports
`0x7A` / `Cp936` / `936` / `gb2312`, while unmarked `VFPTYPE` reports `0x00` / `Unmarked` / null and
falls back to `ibm437`. Slicing record 1 out of the raw bytes by the offsets the table reports and
decoding with `TextEncoding` gives back the generator's documented input — `Привет, мир`,
`Компьютеры`, `中文测试` — so mark → code page → encoding → string is right at every rung. **The GBK
check mattered:** .NET reports cp936's `WebName` as `gb2312`, a narrower standard, but `CP936.TRAIL`
decodes to `乗亅丄亊丂俓` (U+4E57 U+4E85 U+4E04 U+4E8A U+4E02 U+4FD3), so lead byte `0x81` — a GBK
extension outside GB2312 — works and the name is a legacy misnomer, not a narrower encoding.

**Two decoding defaults are now inherited by accident, and step 002 must choose them deliberately.**
Both are `Encoding`'s behaviour, not ours, and both are the "wrong text with no error" failure mode
this library cares most about:

- **A character cut in half becomes U+FFFD, silently.** `CP936` record 1 `CUT` decodes to
  `中文测�` — indistinguishable from a genuine replacement character in the data.
- **A byte the code page leaves undefined passes through.** `CP1251` record 2 `SWEEP` puts `0x98` at
  U+0098, a C1 control, rather than replacing it. The rest of that row is correct (`0x90` is `ђ`,
  `0x99` is `™`).

**Still the last code change:** step 001, closed on 2026-08-09 — read its
[`SUMMARY.md`](claude/dev/001-dbf-open-and-header/SUMMARY.md) rather than its `DESIGN.md` or `PLAN.md`.
Two constraints from it that will recur: **every byte-reading boundary must be faked by hand** (no
mocking library can proxy a `Span<byte>` parameter — verified, not assumed), each kept honest by a
contract test; and **open-time field length validation barely exists in a release build**, so a
reader that refuses a 7-byte date is *less* compatible than the original.

---

## 3. Next

**Step 002 — DBF record reading.** Navigation (`Top/Bottom/Skip/Go/Eof/Bof/RecNo`) and per-type
field decode, gated on the `[records]` section of all seven dumps, which nothing reads yet. Run
it through the step process: `cp -r claude/dev/_template claude/dev/002-<name>`, then `DESIGN.md` and
`PLAN.md` before any `.cs`.

Three things to settle as it opens, in this order:

1. **Decode text, which is the only rung of the code-page chain still missing.** Mark → code page →
   encoding all work (ADR-19, ADR-20); what 002 adds is bytes → string. Two questions it must answer
   rather than stumble into: what a `C` field yields when a multi-byte character is **cut in half at
   the field boundary** (`CP936.CUT` is that case in every record) and what `0x98` yields in a cp1251
   table (`CP1251.SWEEP` carries it; the code page leaves it undefined). Settle the unmarked default
   too — ADR-20's open item. The end-to-end gate is available and honest: `CP1251` record 1's `TEXT`
   decodes to `Привет, мир` and `CP936` record 1's to `中文测试`, expectations that come from the
   generator's documented input rather than from bytes we invented.
2. **Re-check the per-type accessor widths against `F4FIELD.C`** before writing any decoder. Step
   001's `DESIGN.md` Decision 18 holds the table, but it is the contract for *decoding* and was
   written while implementing something else. The C is not uniform: text-shaped types honour the
   declared length, while `B`/`H`/`Y`/`T`/`7` read their natural width and ignore it, taking bytes
   from the following field. That is deliberate and must be copied, not corrected.
3. **The containment guarantee is already in place** — every field lies inside its record, asserted
   as a property over generated descriptors. Decoders must not undermine it: read through one
   bounds-checked accessor, and let a decode reach into the *next* field only where the C library
   does, never past the record.

Still ungated after 001, none of it blocking: version `0x31` with feature flags, the `H` field type,
and the long-field-name layout that is currently refused outright.

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
