# Project state

**Updated:** 2026-08-11 · step 003 is committed to `main` and the tree is clean. Nothing is pushed
yet: two commits are waiting, step 002's and this one.
**Active step:** none. [`003-memo-and-binary-types`](claude/dev/003-memo-and-binary-types/) is
**closed**, and with it `DBF-READ` is **complete for reading**: **699 tests**, and the gate asserts
every field of every record with nothing skipped.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read, whole.** Metadata, records, every field value, and the memo behind a memo
reference. `net/CodeBase.Net.sln` builds four projects — `CodeBase.Net` (**no NuGet dependencies**
by design, ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden` and `CodeBase.Net.TestUtils` — and
`dotnet test` is green on **699 tests**, 268 of them golden.

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

Memo fields answer too — `GetMemoBytes`, `GetMemoString`, `GetMemoLength`, `GetMemoBlock`,
`GetMemoType` — in both reference encodings, four-byte binary and ten-byte ASCII.

**Everything is gated against the C library's own dump, with nothing skipped:** all seven corpus
tables, all 224 records, every field, and all 224 memo values of which 153 are non-empty. The single
refusal is a **compressed memo entry**, which no corpus case can gate yet (ADR-23, open).

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

**Step 003 was designed, planned and executed.** With it `DBF-READ` is complete for reading:
**699 tests green**, 268 golden, and `RecordGoldenTests` now asserts **every field of every record
of all seven tables with nothing skipped** — the counter step 002 used to subtract memo fields is
gone, because there is nothing left to subtract. Read
[`SUMMARY.md`](claude/dev/003-memo-and-binary-types/SUMMARY.md). Four things worth carrying forward:

- **Both memo reference encodings are gated end to end.** 224 memo values, 153 of them non-empty,
  across the five tables with an `.fpt`: block number, length, type and every payload byte. The
  ten-byte ASCII form closed **`FPT-MEMO.md`'s first open question** — right-aligned, space-padded,
  blank meaning no memo — settled from `F2XMEMO` because `c4ltoa45`'s body is not in the drop.
- **A test written for this step found a bug step 002 had shipped.** `BlankRecord` decided a blank
  memo reference by the type letter, following `f4blank`'s list, which puts `M` and `G` among the
  space-filled types. The rule is actually the reference *width*: four bytes blank to zeros, ten to
  spaces, so that the blank reads back as "no memo" in whichever encoding is in use. A four-byte
  memo field at end of file was reporting block 538976288 — `0x20202020`. `FPT-MEMO.md` §3.4 already
  said it correctly; the spec was right and the code was wrong.
- **Compressed entries are refused, and the reason is narrower than it first looked** — **ADR-23**,
  deliberately left `open`. Three claims were being run together: zlib's absence from the drop
  (irrelevant — `ZLibStream` is in the base class library, so reading costs no dependency), the
  stream format (now resolved as zlib-wrapped with a 4-byte length prefix, from `source/zlib.h` plus
  the sibling `connect4lowUncompress`), and the absence of a corpus case (the actual reason). Also
  recorded there: "it is CodeBase-only" is an argument *for* supporting it, since this is a port of
  CodeBase and `CLAUDE.md` requires that files the original library writes are read correctly here.
- **Mutation-checked four ways**, each against the five memo tables: the payload read from the block
  start rather than past the header, the header read little-endian, `numChars` treated as including
  the header, and the ten-byte reference parsed as binary. The first three fail 5 tables, the last
  fails exactly 1 — `F2XMEMO`, the only ten-byte table. The blast radius matching the tables at risk
  is itself evidence the tests point at the right thing.

**Known ungated paths, named rather than discovered later:** a block size other than 512, including
zero, which legally means byte granularity; entry types 0, 2 and 3; a payload spanning more than two
blocks (505 bytes is the longest, crossing one boundary); and `G` fields, of which only four are
non-empty. Each is covered by component tests over hand-built images.

---

## 3. Next

**`DBF-READ` is done for reading, so the next milestone needs corpus work before code.** `CDX-READ`
is the priority (`PORTING-PLAN.md` §5), and **the corpus has no indexed case at all** — every table
was generated without one. That is the gap to close first: teach the generator to build CDX files,
and make sure the cases include **multi-level trees**, because the shipped `original/examples/DATA/`
samples are all single-leaf and interior nodes are otherwise unreachable (`PORTING-PLAN.md` §6.3).

Before or alongside that, two things that do not depend on it:

**Write `claude/specs/QUERY-OPTIMIZER.md`.** Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`,
to the same `FILE.C:line` standard as the existing seven. Must cover the `BITMAP4` tree and flags,
`CONST4` range constraints, filter-to-bitmap decomposition, **which expression forms are optimizable
and which are not**, leaf evaluation via tag seek, AND/OR/negation combination, and the
fall-back-to-scan boundary. Prerequisite for `QUERY` (risk R13), and the only in-scope subsystem
with no source-cited spec.

**Close ADR-23 if it is wanted.** Its own small step: add zlib to the generator, reconstruct the
`c4compress` wrapper from the layout the reader pins down, add a case with `code4memoCompress`
enabled and a payload longer than one block. The reader is then a few lines over `ZLibStream`.

Then the `CORPUS` spot-check pass against the specs: FPT `numChars` = payload-only (**now witnessed
for all 153 entries**), CDX interior-node big-endian recno, the 263-byte reserved area, the
`t4dblToFox` sign rule.
