# Project state

**Updated:** 2026-08-12 · step 006 is **committed to `main`** and the tree is clean. Nothing is pushed
yet: six commits are waiting, steps 002's, 003's, 004's, the design one, 005's and 006's.
**Active step:** none. [`006-tags-on-a-table`](claude/dev/006-tags-on-a-table/) is **closed**:
**1039 tests**, and `CDX-READ` is **done for reading** — what is left of it waits on `EXPR`.
[`007`](claude/dev/) — seek by value — is next and not yet designed.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read whole, and its records can be read in an index tag's order.**
`net/CodeBase.Net.sln` builds four projects — `CodeBase.Net` (**no NuGet dependencies** by design,
ADR-17), `CodeBase.Net.Tests`, `CodeBase.Net.Golden` and `CodeBase.Net.TestUtils` — and `dotnet test` is
green on **1039 tests**, 453 of them golden.

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

**The production index opens with the table, and a tag is an order the cursor can follow:**

```csharp
FieldDefinition name = table.Fields["NAME"];
Tag byName = table.Tags["NAME"];          // Table.Tags is empty when the header declares no index

table.SelectTag(byName);                  // Top, Bottom and Skip now move through the tag
for (GoResult go = table.Top(); go == GoResult.Ok; )
{
    string text = table.GetString(name);
    if (table.Skip(1) != SkipResult.Moved) break;
}

// or per call, leaving the table's mode alone:
for (GoResult go = table.GoFirstIndexed(byName); go == GoResult.Ok; go = table.GoNextIndexed(byName))
    Read(table.GetString(name));
```

Both forms share one cursor per tag, so a walk started with either continues with the other. A tag's pad
byte is **derived** from the table's field descriptors when its expression is a bare field name (ADR-28);
the reader below stays `internal`.

`CodeBase.Net.Cdx` reads both file shapes — a compound file through its tag directory, and a
single-tag `.IDX` whose tag is named after the file — then tag headers, interior nodes with their
big-endian pointers, and **bit-packed leaves**. Walking follows the leaf chain in either direction and
inverts for a descending tag. Machine and `GENERAL` collation both read; a `GENERAL` tag needs no
weight table, because reading a key never computes one.

**And it can find a key rather than walk to it**, in five operations over key bytes:

```csharp
KeySearch search = KeySearch.For("SMITH"u8, tag.KeyLength, tag.PadByte);

cursor.Seek(search);            // first entry not before the value, in the tag's order
cursor.SeekAtOrBefore(search);  // last entry not after it — a range's other end
cursor.SeekLast(search);        // last entry that still matches
cursor.SeekNext(search);        // next match; SeekPrevious walks a run backwards
cursor.SeekExact(search, 42);   // that key *and* that record number
```

`Seek(low)` to `SeekAtOrBefore(high)`, walked with the cursor's own steps, is a closed range — the shape
the optimizer's per-tag constraints will ask for. **What is missing is turning a *value* into key bytes**,
which is `COLLATION`'s half and step 007's; seeking is still `internal` for that reason.

**Everything is gated against the C library's own view, with nothing skipped.** On the table side: all
eleven corpus tables, every record, every field, and every memo value. On the index side: **22 tags,
3364 keys, 155 blocks and 3425 block entries** — including each leaf entry's stored duplicate and trail
counts, so the bit-packing is checked as an encoding and not only through the keys it rebuilds. Seeking
adds **206 recorded search cases, 104 seek-next runs and 3364 exact-pair assertions**, plus properties for
the three operations the C library does not have. Navigating a table by index adds **3364 records reached
through 18 tags, with every field of every one checked against that record's own dump** — through both
surfaces, in both directions. The one refusal is a **compressed memo entry**, which no corpus case can gate
yet (ADR-23, open).

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
| `CDXDEEP.DBF` + `.cdx` | `0x30` | **three levels deep** — 600 records, 55 leaves under two levels of branch, full leaves, equal keys across block boundaries, and one **descending** tag so a backwards walk crosses blocks |
| `CDXCOLL.DBF` + `.cdx` | `0x30` | **machine beside `GENERAL`** over one cp1252 field: `keyLen` 20 and 40, pad `0x20` and `0x00`, accents and the `œ`/`ß`/`þ` expansions |
| `IDXONE.DBF` + `.cdx` + `.IDX` | `0x30` | **one tree in both file shapes**, the `.IDX` derived and verified by walking both (ADR-25) |

Regeneration is byte-identical, index files included.

**Documentation:** seven format specs, the porting plan, the development approach and the decision
log. `claude/specs/QUERY-OPTIMIZER.md` **does not exist** and is the gap that matters — the optimizer
is the only in-scope subsystem with no source-cited spec (risk R13).

---

## 2. Last session (2026-08-12)

**Step 006 was designed, planned and executed, and is closed.** A table now opens its production index
and can be navigated in a tag's order, which makes this the first index feature a caller can see. Read
[`SUMMARY.md`](claude/dev/006-tags-on-a-table/SUMMARY.md). Five things worth carrying forward:

- **The pad byte is settled for real tags, and derived rather than supplied.** ADR-28's rule — a tag whose
  expression is a bare field name takes that field's type — is implemented in `KeyTypeResolver` and checked
  against the `pChar` the C library recorded for **all 18 corpus tags**. Resolution is lazy, per tag, on
  first use, so a tag whose expression this port cannot type refuses when it is selected and the table's
  other tags keep working. That was a change to 004's reader, which resolved every tag at open.
- **The gate verifies records, not record numbers.** Every one of the 3364 positions a tag-order walk
  reaches is compared field by field against that record's entry in the table's own dump. The mutation that
  justifies it: reading a *neighbouring* record while reporting the right number fails exactly those four
  golden walks and nothing else — every record-number assertion in the suite stays green.
- **Reading `d4skip`'s tag path closely changed four behaviours the design had guessed at.** A skip of zero
  does not consult the tag at all; at end of file a backwards skip re-enters through the tag's bottom and
  counts that as a step; a backwards skip that runs out stops on the tag's *first* record and leaves it
  readable, raising only the beginning flag; and running out is *not* always both flags. The facts are now
  `CDX-FORMAT.md` §7.1, and the difference they create between `Skip` and the four `…Indexed` methods is
  **ADR-29**: a skip is relative and boundary-readable, a positioning call reports no record.
- **One case is refused rather than answered** (**ADR-30**): stepping in a tag's order from a record the tag
  does not list. The C library re-derives the record's key through the expression; this port cannot, and
  answering "end of file" would hand back a record set that silently stops early. A filtered or unique tag
  is still fully walkable — only mixing `Go(n)` to an unlisted record with a tag-order step is refused.
- **`Table.HasProductionIndex` was removed.** It reported the header's claim; `HasIndex` reports the open
  file, and since a declared index that is missing is an error at open the two could never disagree.

**Known ungated paths, named rather than discovered later:** a composite key expression such as
`UPPER(NAME)`, refused until `EXPR`; a tag over a `Y`, `T`, `Z` or `F` field, which the resolver handles and
no corpus tag exercises; an index entry naming a record past the end of the table, which every generated
index precludes and a hand-built pair covers instead; a table whose every tag is refused; and two index
files on one table.

## 3. Next

**Step 007: seek by value.** `Table.Seek("SMITH")` needs the value-to-key transforms — `t4dblToFox` and its
siblings — which is `COLLATION`'s machine half and is **gateable from the corpus already committed**, since
every tag's stored keys sit beside the field values they were computed from. It is the last piece before a
caller can ask a table a question rather than walk it, and it inherits three named questions from 005: the
`.NULL.` convention for an empty public seek, whether `SeekNext`'s degrade-to-seek should survive into a
public API, and `considerPartialSeek` for collated partial seeks. Nothing is designed yet.

**`EXPR` is what `CDX-READ` still owes**, in exactly two places, both refusals rather than gaps: typing a
key expression that is not a bare field name (ADR-28) and positioning a tag on a record it does not list
(ADR-30).

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
