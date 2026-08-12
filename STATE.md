# Project state

**Updated:** 2026-08-12 · step 004 is **committed to `main`**; the only thing left in the working tree is
the pair of design folders for steps 005 and 006, which land next. Nothing is pushed yet: three commits
are waiting, steps 002's, 003's and 004's.
**Active step:** none. [`004-cdx-tags-and-traversal`](claude/dev/004-cdx-tags-and-traversal/) is
**closed**: **900 tests**, and `CDX-READ` is done for decoding and traversal. Steps
[`005-cdx-seek`](claude/dev/005-cdx-seek/) and [`006-tags-on-a-table`](claude/dev/006-tags-on-a-table/)
are **designed and not started**.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read, whole, and a CDX or IDX can be read and walked.** `net/CodeBase.Net.sln` builds
four projects — `CodeBase.Net` (**no NuGet dependencies** by design, ADR-17), `CodeBase.Net.Tests`,
`CodeBase.Net.Golden` and `CodeBase.Net.TestUtils` — and `dotnet test` is green on **900 tests**, 385
of them golden.

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
`GetDecimal`, `GetDate`, `GetDateTime`, `IsNull`, plus `Deleted`, `Eof` and `Bof`. Memo fields answer
too — `GetMemoBytes`, `GetMemoString`, `GetMemoLength`, `GetMemoBlock`, `GetMemoType` — in both
reference encodings.

**The index side is `internal` and not yet wired to a table**, which was step 004's scope call:

```csharp
using IndexFileReader index = IndexFileReader.Open(source, "CUSTOMER.cdx", padByteFor);

TagCursor cursor = index.Tag("NAME").OpenCursor();
for (bool any = cursor.Top(); any; any = cursor.Next())
{
    IndexEntry entry = cursor.Current;   // key bytes, rebuilt and padded, plus the record number
}
```

`CodeBase.Net.Cdx` reads both file shapes — a compound file through its tag directory, and a
single-tag `.IDX` whose tag is named after the file — then tag headers, interior nodes with their
big-endian pointers, and **bit-packed leaves**. Walking follows the leaf chain in either direction and
inverts for a descending tag. Machine and `GENERAL` collation both read; a `GENERAL` tag needs no
weight table, because reading a key never computes one. **What is missing is seek** (step 005) and the
wiring to a `Table` (step 006, which is also what resolves the pad byte for a bare-field tag — ADR-28).

**Everything is gated against the C library's own view, with nothing skipped.** On the table side: all
eleven corpus tables, every record, every field, and every memo value. On the index side: **22 tags,
3364 keys, 155 blocks and 3425 block entries** — including each leaf entry's stored duplicate and trail
counts, so the bit-packing is checked as an encoding and not only through the keys it rebuilds. The one
refusal is a **compressed memo entry**, which no corpus case can gate yet (ADR-23, open).

**`test-files-generator/`** builds and runs end to end (Windows/MSVC):

```bat
build-lib.bat     :: 137 TUs -> obj\codebase.lib   (~2 min; ~1.4 s incremental)
build-gen.bat     :: src\*.cpp -> bin\testgen.exe
bin\testgen.exe   :: -> bin\out\
copy-corpus.bat   :: -> ..\net\corpus\
```

**`net/corpus/`** — eleven cases, four of them indexed. Each table has a `<NAME>.dump.txt` and each
index file a `<NAME>.cdx.dump.txt`, both written by the C library:

| File | Version | Covers |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x set `C N D L`, no memo |
| `VFPTYPE.DBF` | `0x30` | every non-memo VFP type `C N F D L I B Y T` |
| `F2XMEMO.DBF` + `.fpt` | `0xF5` | FoxPro 2.x memo, 10-byte ASCII memo reference |
| `VFPMEMO.DBF` + `.fpt` | `0x30` | memo + binary types `M X G Z`, 4-byte reference, payloads straddling an FPT block boundary |
| `VFPNULL.DBF` + `.fpt` | `0x30` | nullable fields, the hidden `_NullFlags` descriptor, and the memo/null interaction |
| `CP1251.DBF` + `.fpt` | `0x30` | a marked code page, single-byte (byte 29 = `0xC9`) |
| `CP936.DBF` + `.fpt` | `0x30` | a marked code page, multi-byte (byte 29 = `0x7A`), characters cut in half |
| `CDXBASE.DBF` + `.cdx` | `0x30` | **ten tags, one block each** — one per key shape: prefixes, blanks, descending, duplicates, unique, sub-`0x20` bytes, numeric, `-0.0`, date, integer, filtered |
| `CDXDEEP.DBF` + `.cdx` | `0x30` | **three levels deep** — 600 records, 55 leaves under two levels of branch, full leaves, equal keys across block boundaries |
| `CDXCOLL.DBF` + `.cdx` | `0x30` | **machine beside `GENERAL`** over one cp1252 field: `keyLen` 20 and 40, pad `0x20` and `0x00`, accents and the `œ`/`ß`/`þ` expansions |
| `IDXONE.DBF` + `.cdx` + `.IDX` | `0x30` | **one tree in both file shapes**, the `.IDX` derived and verified by walking both (ADR-25) |

Regeneration is byte-identical, index files included.

**Documentation:** seven format specs, the porting plan, the development approach and the decision
log. `claude/specs/QUERY-OPTIMIZER.md` **does not exist** and is the gap that matters — the optimizer
is the only in-scope subsystem with no source-cited spec (risk R13).

---

## 2. Last session (2026-08-12)

**Step 004 was designed, planned and executed, and is closed.** `CDX-READ` is done for decoding and
traversal; seek is step 005. Read
[`SUMMARY.md`](claude/dev/004-cdx-tags-and-traversal/SUMMARY.md). Six things worth carrying forward:

- **The corpus had to come first, and the mutation evidence proves it.** Reading the branch **child
  pointer** little-endian instead of big-endian fails every traversal test on the three files that have
  interior nodes — and *no* test on the two that do not. Every CDX shipped in
  `original/examples/DATA/` is single-leaf, so that bug was invisible before this step existed. Five
  mutations were run and each one's blast radius matches the files at risk exactly.
- **The bit-packing is gated as an encoding, not only through the keys.** The index dump records each
  leaf entry's stored duplicate and trail counts, so a key that comes out right from two compensating
  mistakes — a wrong duplicate count against a wrong position in the key text — still fails. That was
  `DESIGN.md`'s open question Q2 and the answer is yes, keep it.
- **GENERAL collation is now witnessed against real bytes**, closing `KEY-COLLATION.md` §3.7's caveat
  that the head-and-tail layout was verified from source only. `CDXCOLL` shows `keyLen` at twice the
  field width, pad byte `0x00` on a *character* tag, case as a primary equality with no tail at all,
  accents as a tail weight, and `æ`/`ß`/`þ` expanding to two head bytes. `-0.0` keying to eight zero
  bytes and sorting below every negative is witnessed too, in `CDXBASE`.
- **Two defects in the reference implementation, found by using it.** `tfile4count` returns 1 for any
  descending tag — it tops the tag, landing at the physical last key, then skips forward physically
  (I4TAG.C:1000-1019). And `d4check` reports *every* single-tag file as corrupt, because its block
  accounting flags the tag directory's header and then each tag's header, which in an `.IDX` is the
  same block (i4check.c:889-914). Both are in `CDX-FORMAT.md` now; the second forced a correction to
  ADR-25, whose witness is now the dual-shape walk.
- **A wrong `ErrorCode` was a design finding, not a typo.** A tag-header read past the end of the file
  surfaced as `Data` from the generic short-read path, so `NodeReader.ReadHeader` now applies the same
  bounds guard as a block read: a directory pointing outside its file is a corrupt *index*, and the
  message names the node.
- **Both build paths are represented.** Every index case appends records and creates the index
  afterwards, which is the bulk path VFP's `INDEX ON` uses and which packs leaves tight; `CDXCOLL`'s
  second tag is added to an existing file by `i4tagAdd`, and that is also why its `signature` byte is
  `0x00` where the first tag's is `0x01` — a fact `CDX-FORMAT.md` §3 previously recorded as unexplained
  variation between two sample files.

**Known ungated paths, named rather than discovered later:** a packed entry wider than four bytes
(needs a table above 65 536 records); a block size other than 512 and a multiplier above one; `keyLen`
above 240; `GENERAL` over cp437 and cp850; `CBnnnnn` collations; a multi-block tag directory; a tree
built by insert-and-split; the free list; and a 0-key non-root block, which the reader tolerates and
steps over.

## 3. Next

**Two steps are designed and ready to execute, in either order.** They are independent — navigating in
tag order needs traversal, not seek — but doing 005 first means the public surface arrives complete in
007 instead of growing a `Seek` afterwards.

**[`005-cdx-seek`](claude/dev/005-cdx-seek/)** — the seek *family*, because one seek cannot use a
duplicate key and a range needs both of its ends: `Seek` (first entry not less than the value),
`SeekAtOrBefore` (last entry not greater), `SeekLast` (last entry still matching), `SeekNext` and
`SeekPrevious`, plus exact key-and-record positioning. `Seek(low)` to `SeekAtOrBefore(high)` is a closed
range, which is what the optimizer's per-tag constraints will ask a tag for. Ports `b4seek`'s binary search in a
branch, `b4leafSeek`'s scan with its duplicate-count skipping and its sub-pad-byte and all-blank cases,
partial (prefix) seeks, and the descending seek's increment-then-step-back. **Searching is by key
bytes**, which is what makes it gateable now: turning a *value* into key bytes is `COLLATION`'s work.

Its gating is deliberately of two strengths, and the design says which is which. `Seek` and `SeekNext`
are ported and gated against the reference — two new dump sections, `[seeks]` (roughly 170 cases derived
from each tag's own keys) and `[seeknext]` (record sequences from `d4seekN`/`d4seekNextN`, drivable on
the ten tags whose key transform is the identity). **`SeekAtOrBefore`, `SeekLast` and `SeekPrevious` do not exist in the
C library at all** — `grep` for them over the whole drop returns nothing — so they are gated as
*properties* over the key sequence 004 recorded, and tied back to the gated `Seek` by an adjacency
check: where a value is absent the two must land on **adjacent** entries, and where it is present they
must bracket its run.

It also settles the two **stopping rules** and gates their composition, which is what makes index-order
traversal and duplicate-walking the same machinery: `SeekNext`/`SeekPrevious` stop where the key stops
matching (`NoEntry`, the C's own behaviour), `Next`/`Previous` stop where the tag ends (`Eof`/`Bof`, 004's
traversal), and a cursor left anywhere by a seek — on a match, on a greater key, past either end — is a
valid place to keep walking from. Sub-step 1 needs Windows, and the first thing to
check after regenerating is that the existing `[keys]` and `[blocks]` sections did not move.

**[`006-tags-on-a-table`](claude/dev/006-tags-on-a-table/)** — the first public index surface: open the
production `.cdx` when DBF byte 28 declares one, expose `Table.Tags`, and navigate *records* in a tag's
order two ways over one implementation — the C library's `Top`/`Bottom`/`Skip` with a selected tag, and
an explicit `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed` four that name the tag at
the call site and step unconditionally. **No generator work**: the gate joins the index dumps' key sequences to the
table dumps' field values by record number, so navigating by index has to deliver the same records
reading by number does. It also largely settles ADR-26 — once a tag has a table, a bare-field expression
types the key from the field descriptors, exactly rather than by guessing (**ADR-28**), leaving `EXPR`
needed only for composite expressions.

**Then 007: seek by value.** `Table.Seek("SMITH")` needs the value-to-key transforms — `t4dblToFox` and
its siblings — which is `COLLATION`'s machine half, and it is **gateable from the corpus we already
have**: every tag's stored keys sit beside the field values they were computed from, so the transforms
can be checked byte-for-byte without generating anything. GENERAL-collated seeks additionally need the
weight tables, and `CDXCOLL` is the case for them.

Two things that do not depend on any of it:

**Write `claude/specs/QUERY-OPTIMIZER.md`.** Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`,
to the same `FILE.C:line` standard as the existing seven. Must cover the `BITMAP4` tree and flags,
`CONST4` range constraints, filter-to-bitmap decomposition, **which expression forms are optimizable
and which are not**, leaf evaluation via tag seek, AND/OR/negation combination, and the
fall-back-to-scan boundary. Prerequisite for `QUERY` (risk R13), and the only in-scope subsystem
with no source-cited spec.

**Close ADR-23 if it is wanted.** Its own small step: add zlib to the generator, reconstruct the
`c4compress` wrapper from the layout the reader pins down, add a case with `code4memoCompress`
enabled and a payload longer than one block. The reader is then a few lines over `ZLibStream`.

The `CORPUS` spot-check pass against the specs is now largely spent: FPT `numChars` = payload-only
(witnessed for all 153 entries, step 003), **CDX interior-node big-endian record number and child
pointer** and **the `t4dblToFox` sign rule including `-0.0`** (witnessed in step 004, and the
GENERAL head-and-tail layout with them). What is left of it is the 263-byte reserved area.
