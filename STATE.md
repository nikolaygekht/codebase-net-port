# Project state

**Updated:** 2026-08-12 · step 005 is **committed to `main`** and the tree is clean. Nothing is pushed
yet: five commits are waiting, steps 002's, 003's, 004's, the design one and 005's.
**Active step:** none. [`005-cdx-seek`](claude/dev/005-cdx-seek/) is **closed**: **972 tests**, and
`CDX-READ` is done for decode, traversal and seek. [`006-tags-on-a-table`](claude/dev/006-tags-on-a-table/)
is **designed and not started**.

State only: what is ready, what changed last session, what is next. Decisions and their reasoning
live in [`claude/ARCHITECTURE-DECISIONS.md`](claude/ARCHITECTURE-DECISIONS.md); per-capability status
and gates in [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) §5.

---

## 1. What is ready

**A DBF can be read, whole, and a CDX or IDX can be read and walked.** `net/CodeBase.Net.sln` builds
four projects — `CodeBase.Net` (**no NuGet dependencies** by design, ADR-17), `CodeBase.Net.Tests`,
`CodeBase.Net.Golden` and `CodeBase.Net.TestUtils` — and `dotnet test` is green on **972 tests**, 421
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

**The index side is `internal` and not yet wired to a table**, which was steps 004 and 005' scope call:

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
the optimizer's per-tag constraints will ask for. **What is missing is the wiring to a `Table`**
(step 006, which is also what resolves the pad byte for a bare-field tag — ADR-28) and turning a *value*
into key bytes, which is `COLLATION`'s half and step 007's.

**Everything is gated against the C library's own view, with nothing skipped.** On the table side: all
eleven corpus tables, every record, every field, and every memo value. On the index side: **22 tags,
3364 keys, 155 blocks and 3425 block entries** — including each leaf entry's stored duplicate and trail
counts, so the bit-packing is checked as an encoding and not only through the keys it rebuilds. Seeking
adds **206 recorded search cases, 104 seek-next runs and 3364 exact-pair assertions**, plus properties for
the three operations the C library does not have. The one refusal is a **compressed memo entry**, which no
corpus case can gate yet (ADR-23, open).

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

**Step 005 was designed, planned and executed, and is closed.** `CDX-READ` now does decode, traversal
*and* seek. Read [`SUMMARY.md`](claude/dev/005-cdx-seek/SUMMARY.md). Five things worth carrying forward:

- **The corpus overturned the specification's partial-seek pseudocode, and this is the finding that
  matters.** A search value means one of two things: with **no trailing pad** it is a prefix, compared
  over its own length; with **trailing pad** it stands for the whole key and its pad bytes take part. So
  `"AB      "` does *not* match the stored key `"AB\x00\x00\x00\x00\x00\x00"` — a NUL is below a
  space, so that key sorts before the value — while `"CUSTOMER-A"` does match
  `"CUSTOMER-ACCT-0599  "`. A pure-prefix reading passes every exact-length case and fails these, which
  is why it took 206 recorded cases to catch. `CDX-FORMAT.md` §7 now carries the rule, witnessed.
- **Three consequences, all in the spec now.** The increment of a padded value lands on its last *pad*
  byte, so the successor of `"MIDDLE      "` is `"MIDDLE     !"` and not `"MIDDLF"` (which would step over
  `"MIDDLE-EARTH"`). A value that cannot be incremented takes a different branch entirely — an all-`0xFF`
  search on a descending tag reports end of file, not the greatest key. And `tfile4seek` can return
  `r4after` with the cursor already past the end, while `d4seek` normalises that to `r4eof`, so a status
  has to be decided from the pair rather than the code.
- **`SeekAtOrBefore` became the primitive rather than an extra.** All three backwards operations are
  "increment, seek, step back one", so the increment is written once; `SeekLast` is one comparison on top
  of it. Two of the five operations are ports and three are additions, and the tests say which is which:
  the additions are gated as properties over the recorded key sequence, tied to the reference-gated `Seek`
  by an adjacency check.
- **A mutation exposed one of my own tests as vacuous.** The adjacency check compared two expectations
  computed from the dump rather than the two cursor landings, so making `SeekAtOrBefore` identical to
  `Seek` left it green. It now compares where the cursors land, and that mutation fails it. Decision 9's
  prediction about which assertions the step-back mutation would break was also wrong, and is corrected in
  the summary: the step-back is shared with the descending seek, which *is* reference-gated.
- **A latent bug from step 004 surfaced**: stepping back from an end-of-file landing moved *past* the last
  entry instead of onto it. A walk that merely stops at the end never notices; a seek that lands at the end
  and then steps back does. Both ends now re-enter the tag, as the record cursor already does.

**Known ungated paths, named rather than discovered later:** `SeekNext` against the reference on a
numeric, date or currency tag (needs 007's transforms); the three added operations against reference
bytes, which cannot exist; a range walk as an operation, left as a composition until `QUERY` calls it; a
partial seek on a `GENERAL`-collated tag by *value*, where the C library's own `considerPartialSeek` flag
exists because tail weights interfere; and a seek racing a writer, which is `LOCKING`.

## 3. Next

**[`006-tags-on-a-table`](claude/dev/006-tags-on-a-table/)** is designed and ready, and needs no generator
run: the gate joins the index dumps' key sequences to the table dumps' field values by record number, so
navigating by index has to deliver the same *records* that reading by number does. It opens the production
`.cdx` when DBF byte 28 declares one, exposes `Table.Tags`, and navigates records in a tag's order two ways
over one implementation — the C library's `Top`/`Bottom`/`Skip` with a selected tag, and an explicit
`GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed` four that name the tag at the call site
and step unconditionally. It also settles ADR-26 for real tags: a bare field-name expression types the key
from the field descriptors exactly (**ADR-28**, verified against all 18 corpus tags before any code), so
`EXPR` is needed only for composite expressions.

**Then 007: seek by value.** `Table.Seek("SMITH")` needs the value-to-key transforms — `t4dblToFox` and its
siblings — which is `COLLATION`'s machine half and is **gateable from the corpus already committed**, since
every tag's stored keys sit beside the field values they were computed from. It inherits three named
questions from 005: the `.NULL.` convention for an empty public seek, whether `SeekNext`'s
degrade-to-seek should survive into a public API, and `considerPartialSeek` for collated partial seeks.

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
