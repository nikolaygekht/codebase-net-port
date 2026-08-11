# Project state

**Updated:** 2026-08-11 · step 002 is committed to `main` and the tree is clean; nothing is pushed
yet.
**Active step:** none. [`002-dbf-records-and-fields`](claude/dev/002-dbf-records-and-fields/) is
**closed** — records and ordinary field values, gate green, **630 tests**. Step 003, memo payloads
and the binary-marked types, is next and is not yet designed.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read: its metadata, its records, and every ordinary field value.**
`net/CodeBase.Net.sln` builds four projects — `CodeBase.Net` (**no NuGet dependencies** by design,
ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden` and `CodeBase.Net.TestUtils` — and `dotnet test`
is green on **630 tests**, 252 of them golden.

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");

for (GoResult go = table.Top(); go == GoResult.Ok; )
{
    FieldDefinition name = table.Fields["NAME"];
    string text = table.GetString(name);     // decoded, trailing blanks kept (ADR-21)
    if (table.Skip(1) != SkipResult.Moved) break;
}
```

Opening a table reads its header, its stored descriptors and its resolved field table, and opens the
memo file beside it when the header declares one. It resolves the code page mark: `CodePage`,
`CodePageNumber` and `CodePageByte` answer for all 26 marks Visual FoxPro documents, without needing
an encoding provider (ADR-19, ADR-20). Moving the cursor reads one record, and the typed accessors
read fields out of it — `GetString`, `GetRawBytes`, `GetBoolean`, `GetInt32`, `GetDouble`,
`GetDecimal`, `GetDate`, `GetDateTime`, `IsNull`, plus `Deleted`, `Eof` and `Bof`.

Every one of those is gated against the C library's own dump for all seven corpus tables and all 224
of their records — **`[records]` included**, which was the one dump section nothing read. **What is
still not readable is a memo payload** and the binary-marked types `X`, `G` and `Z`: step 003.

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

## 2. Last session (2026-08-11)

**Step 002 was reviewed, then executed end to end**, and both halves landed in one commit.

### The design review

Every `FILE.C:line` citation in `DESIGN.md` was checked against the source. The substantive readings
all held — `d4goEof`, the backwards-skip branch, the `r4entry` path, `E4PARM_HIGH` being on in the
shipped build, all three `f4double` outcomes, `d4deleted`. Six findings: two citations were wrong
(`d4bof`/`d4eof` transposed, one range off by one), the flag-reset rule was missing (`d4go` clears
both flags, and `d4skip` clears the beginning flag before deciding where to go), the invalid position
was an unnamed fourth state, `GetString` had no cited counterpart, and Q1 was answerable by citation.
Decisions 14-16 came out of it, along with **ADR-21** and **ADR-22**.

**Five of the six open questions closed without writing code.** Currency is fixed at four decimals —
`d4create` hard-codes `(8, 4, 0x04)` (D4CREATE.C:1569-1571) and `f4currency` never reads `field->dec`,
so the `Y(8,2)` generator case the plan held in reserve was retired. Text was settled by ADR-21: cp437
for an unmarked table, best-effort decoding that never throws, the gate asserting decoded strings, and
`GetString` returning the space-padded declared width as `f4str` does.

### The execution

**630 tests green, 252 golden.** The gate asserts every ordinary field of every record of all seven
tables, and a separate suite walks each table four ways to prove the traversal itself. Read
[`SUMMARY.md`](claude/dev/002-dbf-records-and-fields/SUMMARY.md) rather than the design or the plan.
Five things worth carrying forward:

- **`double.Parse` reproduces `c4atod` bit for bit on all 224 numeric values.** The step's chief risk,
  closed on the first run, compared on the bit pattern. It matters more than it looks: **`c4atod`'s
  body is not in this source drop** — nor are `c4atoi`, `c4atol`, `c4ltoa45`, `c4currencyToA` or
  `c4atoCurrency`, all declarations only. The design's fallback of "port it verbatim" was never
  available, so agreement was the only outcome that did not require reverse-engineering from 224
  values.
- **A correction to what the previous session recorded.** The note that a half character "silently
  becomes U+FFFD" was wrong: .NET's default for the legacy code pages is an internal *best-fit*
  fallback that yields **`?`**, and `DecoderFallback.ReplacementFallback` is `?` as well. Verified by
  running it. `CodePageMap` now asks for `new DecoderReplacementFallback("\uFFFD")` explicitly, so a
  question mark in a decoded field is a question mark the file holds, and the behaviour does not
  depend on which provider the host registered.
- **A table is walked four ways and must give the same records in the same order** — by number,
  forwards, backwards, and backwards from past the end — each record identified by a field the dump
  shows to be unique. This is what no per-record assertion can catch: a skip that jumps a record or a
  walk that stops one short passes every field check in the suite.
- **The gate was mutation-checked, and so was every cursor flag.** Shifting the record offset by one
  record turns 28 golden tests red across every table. Mutating each flag transition in turn fails
  between 1 and 5 tests apiece — except two flag-raises the C performs on the empty-table skip path,
  which failed nothing and turned out to be **provably dead** (the end-of-file position of an empty
  table is record one, and moving there raises the flag already; a negative record count, the one
  input that would differ, is refused at open). They were removed. Four injected traversal bugs — a
  skip that jumps a record, a `Bottom` landing early, a wrong end-of-file position, a walk stopping
  short — each fail 7 to 14 of the scan tests. The suites also assert their own counts, because a
  data-driven gate that discovers nothing reports success having proved nothing.
- **Sub-step 8a concluded no API** (ADR-22). The finding worth keeping is that the obvious call-site
  fix, `GetString(f).TrimEnd()`, is a data-loss bug: the padding is spaces, but the no-argument form
  strips tabs and newlines too, and those are data in a fixed-width field. `TrimEnd(' ')` is right,
  and the XML docs now say so.

**Known ungated paths, named rather than discovered later:** the `H` and `7` field types (no corpus
case); the deletion flag in its **true** state (all 224 corpus records are `deleted=0`); the blank
record (no dump shows one); and millisecond rounding in a datetime (all 64 corpus datetimes fall on a
whole second). Each is covered by unit or component tests against hand-built input, and each is a
cheap generator case if it turns out to matter.

---

## 3. Next

**Design step 003 — memo payloads and the binary-marked types.** Nothing blocks it. Its scope was
drawn by 002 deliberately: the FPT reader, block chains, payloads spanning blocks, and the types `X`,
`G` and `Z`, so that everything memo-backed lands in one place. Follow
[`DEV_APPROACH.md`](claude/DEV_APPROACH.md) — `DESIGN.md` and `PLAN.md` before any `.cs` file.

Three things step 002 left set up for it:

1. **`CorpusDump` already parses the memo lines.** `ref=`, `len=` and the payload are read and kept,
   so 003 adds assertions rather than parsing.
2. **The gate counts what it skips.** `RecordGoldenTests` asserts that fields asserted plus memo
   fields skipped equals the field count, so wiring memo decoding in makes the skip count drop and
   the assertion holds it honest.
3. **`FieldValueDecoder` is where the type matrix lives**, and its `NaturalWidths` table is what
   decides how many bytes a memo reference occupies — four or ten, by version.

**Also open — independent of the above.**

**Write `claude/specs/QUERY-OPTIMIZER.md`.** Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`,
to the same `FILE.C:line` standard as the existing seven. Must cover the `BITMAP4` tree and flags,
`CONST4` range constraints, filter-to-bitmap decomposition, **which expression forms are optimizable
and which are not**, leaf evaluation via tag seek, AND/OR/negation combination, and the
fall-back-to-scan boundary. Prerequisite for `QUERY` (risk R13).

**Soon after.** Grow the corpus toward the `CDX-READ` and `COLLATION` gates — the corpus still has no
indexed cases at all, and **multi-level trees are the single biggest gap** (`PORTING-PLAN.md` §6.3
lists what is missing). Then the `CORPUS` spot-check pass against the specs: FPT `numChars` =
payload-only, CDX interior-node big-endian recno, the 263-byte reserved area, the `t4dblToFox` sign
rule.
