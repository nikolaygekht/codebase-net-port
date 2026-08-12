# 004-cdx-tags-and-traversal — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

Read an index file and walk every tag in it in key order — the tag directory, the tag headers, the
interior nodes and, above all, the **bit-packed leaf nodes**. The index is read as a file in its own
right: nothing in this step connects a tag to a `Table`, and nothing public appears.

**Gate:** for every tag of every index file in `net/corpus/`, the C# walk reproduces the checked-in
dump exactly — the `(key bytes, record number)` sequence in navigation order, **and** every block's
structure, including each leaf entry's stored duplicate and trailing counts. Two things must exist
before that gate can be run, and they are the first half of this step: **the corpus has no index
file at all**, and the dump format has no index half (ADR-13).

**Capability:** `CDX-READ`, plus the index half of `CORPUS` (`PORTING-PLAN.md` §5)
**Governing spec(s):** `specs/CDX-FORMAT.md` §1-§7, §11

This retires the largest single risk in the port (**R1**, bit-packed leaf compression) — which is
why the gate is about *decoding*, not about searching.

## Not in this step

| Deferred | To | Why |
|---|---|---|
| **Seek** — `b4seek` branch binary search, `b4leafSeek` with its duplicate-count skipping and sub-0x20 tie-breaks, the descending seek's key increment, `tfile4go2fox` | **005** | It is a second subsystem with its own failure modes and its own dump section (seek cases with their landing entry). Folding it in would make one step two steps wide, which `DEV_APPROACH.md` §1 forbids. Traversal is what proves the codec; seek is what uses it |
| **Wiring a tag to a `Table`** — `Table.TagNames`, selecting a tag, navigating records in key order, auto-opening the production index | after 005 | The user's scope call for this step. A tag entry is a record pointer, but proving we decode it needs no table |
| **Recomputing a key from its expression** | `EXPR` + `COLLATION` | Keys are read from disk, not derived. The expression *text* is parsed as text and nothing evaluates it. This is what makes `CDX-READ` reachable now |
| **The collation *tables*** — translating a value into key bytes | `COLLATION` | Reading a GENERAL-collated tag needs no table (Decision 9); *seeking* one does, because the search value has to be translated before it is compared. `CBnnnnn` custom collations are refused outright: their tables are disk-loaded (`collate4test` is `MUST4LOAD_ARRAY`, i4conv.c:363-365) and no corpus case can exist for them |
| **Writing** — leaf insert, split, free list, the version counter | `WRITE` | Read path only, as 001-003 were |
| **Locking and version re-validation** (`index4versionCheck`, the 1-second retry) | `LOCKING` | Exclusive single-user reads. The `version` field is parsed and reported, never acted on |
| **Block caching** | when there is a measurement | Each traversal step reads the block it needs. The C caches per tag because it also writes through the cache; a read-only reader that caches is an optimization with no number behind it |
| **`keyLen > 240`** (the 16-bit duplicate/trail masks of `CDX-FORMAT.md` §6.5) | never, probably | A CodeBase extension that also requires non-VFP block sizes. Refused with a message that names it. 240 rather than 255: it is the format's own cap, `I4MAX_KEY_SIZE` |

---

## Part A — the corpus (the `CORPUS` half)

Nothing in `net/corpus/` has an index, and the shipped `original/examples/DATA/` samples are all
single-leaf (`PORTING-PLAN.md` §6.3) — so interior nodes are unreachable without generated cases,
and examples are never a gate. Three new tables, each owning its own data as
`test-files-generator/README.md` requires. **The seven existing tables are not touched**: creating a
production index sets bit 0x01 of DBF byte 28, so adding a tag to `VFPTYPE` would move bytes that
are already gated.

### A.1 Cases

| File | Records | Shape | What it is for |
|---|---|---|---|
| `CDXBASE.DBF` + `.cdx` | 32 | every tree a single root+leaf block (`nodeAttribute` 3) | Fine-grained key shapes, one tag per shape, small enough that a human can read the whole dump |
| `CDXDEEP.DBF` + `.cdx` | 600 | multi-level: at least one tag three levels deep | Interior nodes, sibling chains, leaves that are nearly full, keys equal across a leaf boundary |
| `CDXCOLL.DBF` + `.cdx` | 32 | code page cp1252, one field indexed **twice** | GENERAL collation beside machine collation over the *same* accented text (Decision 9) |
| `IDXONE.DBF` + `.cdx` + `.IDX` | 300 | one tag, in both file shapes | The single-tag `.IDX` layout (Decision 3) |

`CDXBASE`'s ten tags, each a bare field reference so the key bytes are the field bytes:

| Tag | Over | Properties | Covers |
|---|---|---|---|
| `T_TEXT` | `K_C` C(20) | ascending | distinct keys with long common prefixes, values shorter than the width (trail > 0), a value filling it exactly (trail 0), **two adjacent empty values** — the invariant that a blank key has dup 0 and trail = keyLen even when its neighbour is blank too (`CDX-FORMAT.md` §6.3) |
| `T_TEXTD` | `K_C` | **descending** | descending traversal over keys that are physically ascending |
| `T_DUP` | `K_DUP` C(10) | ascending | runs of identical keys, and their record-number ordering |
| `T_UNIQ` | `K_DUP` | ascending, **unique** | `typeCode` bit 0x01, and a tag holding fewer keys than the table has records |
| `T_BIN` | `K_BIN` C(8) | ascending | keys containing 0x00, 0x01, 0x1F, 0x20 and 0xFF — bytes below the pad character, and the pad character as *data* |
| `T_NUM` | `K_N` N(12,3) | ascending | 8-byte numeric keys: pad character NUL, negatives complemented, zero |
| `T_DBL` | `K_B` B(8) | ascending | `-0.0` through the positive path, and a numeric key whose real bytes end in NUL, so trail > 0 on a *numeric* key |
| `T_DATE` | `K_D` D(8) | ascending | date keys, including a blank date |
| `T_INT` | `K_I` I(4) | ascending | 4-byte integer keys, `LONG_MIN`/`LONG_MAX`/0 |
| `T_FILT` | `K_N` | ascending, `FOR K_I > 0` | `typeCode` bit 0x08, the filter text after the expression text, and a key set smaller than the record count |

Ten tags also give the tag directory ten entries — still one block, so a multi-block tag directory
stays ungated and is named as such.

`CDXDEEP`'s four tags answer the user's "repeating and unique in the overall data set" directly, and
depth is bought with key *width* rather than record count, which keeps the checked-in dump small:

| Tag | Over | Covers |
|---|---|---|
| `D_WIDE` | `K_WIDE` C(40), unique, deliberately **incompressible** (no shared prefixes) | ~11 keys per leaf ⇒ ~55 leaves ⇒ two branch levels above them: **three levels**, so a descent goes down twice |
| `D_PFX` | `K_PFX` C(20), unique, long **shared prefix** | duplicate counts near the maximum, the opposite packing extreme from `D_WIDE` |
| `D_DUP` | `K_DUP` C(8), ~10 distinct values over 600 records | **runs of equal keys crossing leaf boundaries**, and interior entries whose keys are equal to each other |
| `D_NUM` | `K_N` N(12,3), unique | 8-byte keys in a multi-level tree |

`CDXCOLL` is the collation case, and it is deliberately one field read two ways — the sharpest
contrast the format allows, because the same 32 field values produce two different key encodings in
one file:

| Tag | Over | `sortSeq` | Covers |
|---|---|---|---|
| `C_MACH` | `K_TEXT` C(20) | `""` (machine) | keyLen 20, `pChar` `' '`, raw bytes — the baseline |
| `C_GEN` | `K_TEXT` | `"GENERAL"` | **keyLen 40** (head block + tail block, `2 ×` the field width), `pChar` **`'\0'` on a character tag**, accents sharing a head with their base letter, and the expansions `œ→OE`, `ß→SS`, `þ→TH`. Gates `KEY-COLLATION.md` §3.4, which no shipped sample can reach |

Its text is written as **byte arrays**, not source literals, exactly as `CP1251`/`CP936` are — cp1252
is the point of the case, so what a compiler makes of `é` must not enter into it. The table is marked
cp1252 rather than left unmarked because the GENERAL variant is chosen by **the DBF's** code page
(i4init.c:378-405): naming it makes the case say what it is testing, and cp0 would reach the same
array by default and prove less.

Two record counts (32 and ~600) mean two sets of bit widths — `recNumLen` is derived from the
table's record count (`CDX-FORMAT.md` §6.4) — so `infoLen` differs between the files rather than
being a constant the code could accidentally hard-code.

### A.2 The index half of the dump

A sibling file, `<NAME>.cdx.dump.txt` (`<NAME>.idx.dump.txt` for the `.IDX` case), not new sections
in `<NAME>.dump.txt` — **ADR-24**. Sections, in order:

```
file        IDXONE.IDX
shape       single-tag            (or: compound)
blockSize   512   multiplier 1    (codeBaseNote absent)
check       ok                    (d4check)

[header]                          the file-level header (the tag directory's, when compound)
  root=… freeList=… version=… keyLen=… typeCode=0x… signature=0x… sortSeq="" descending=0
  exprPos=… exprLen=… filterPos=… filterLen=…

[tags]                            directory entries in key order (compound only)
  1 "T_TEXT    " node=1024
  …

[tag T_TEXT]
  header  keyLen=20 typeCode=0x60 descending=0 sortSeq="" pChar=0x20 expr="K_C" filter=""
  count   32
  [blocks]                        every block of this tag, in the order traversal reaches it
    node=3072 attr=3 nKeys=32 left=- right=- freeSpace=… \
       recNumLen=14 dupCntLen=5 trailCntLen=5 infoLen=3 recNumMask=0x00003FFF
      0 rec=5 dup=0 trail=15
      …
    node=4096 attr=1 nKeys=3                          (a branch: key, recno and child per entry)
      0 key="…" rec=17 child=3072
  [keys]                          navigation order — reversed for a descending tag
    "ALPHA               " 5
```

Two rules make this dump an authority rather than a second opinion:

1. **Every value comes from the C library's own structures, never from a re-read of the bytes.**
   Keys and record numbers come from `tfile4top`/`tfile4skip`/`tfile4key`/`tfile4recNo`; block
   structure comes from `tfile4block(t4)`'s live `B4BLOCK` — `header`, `nodeHdr`, `fileBlock` — and
   the per-entry counts from the library's own `x4dupCnt`/`x4trailCnt`/`x4recNo` macros
   (`d4declar.h:1807-1854`). The DBF half of the dump reads the header raw because a DBF header is a
   few shifts and an or; a bit-packed leaf is exactly the thing we must not grade our own homework
   on. Interior blocks are reachable because the root-to-leaf path lives in `t4->blocks` while the
   cursor is positioned; the writer dumps any block on the path it has not dumped yet, so a full
   walk enumerates the whole tree.
2. **`d4check` runs on every index case and its result is in the dump.** It re-evaluates each key
   expression per record and compares it with the stored key, checks that keys are non-decreasing
   with the record number as tie-break, and that trail counts match the actual pad bytes
   (`CDX-FORMAT.md` §11, i4check.c:127-323). It is the C library certifying its own file, and for
   the derived `.IDX` case (Decision 3) it is what makes the derivation safe.

---

## Part B — the reader

### Classes

All `internal`, all in `CodeBase.Net.Cdx` (`PORTING-PLAN.md` §3.2).

| Class | Role | Responsibility |
|---|---|---|
| `IndexHeader` | Entity | The 1024-byte tag header (`T4HEADER`), which is also the file header. Parse and validate: `root`, `freeList`, `version` (big-endian), `keyLen`, `typeCode`, block geometry, `sortSeq`, `descending`, the expression and filter text |
| `TagOptions` | Entity | The `typeCode` bits as a flags enum, so "unique" is a value and not `& 0x01` at four call sites |
| `BlockAddressing` | Entity | Block size and multiplier, and the one piece of arithmetic that turns a node number into a file offset. Honours `codeBaseNote == 0xABCD`; defaults to 512/1 |
| `NodeHeader` | Entity | The 12-byte `B4STD_HEADER`: attribute, key count, both siblings. `IsLeaf` is a **bit test**, not `>= 2` |
| `LeafGeometry` | Entity | The 12-byte `B4NODE_HEADER`: the three bit widths, the three masks, `infoLen`, `freeSpace`. Decodes one packed entry into (record, duplicate count, trail count) |
| `LeafBlock` | Entity | A whole leaf: reconstructs keys forward from the heap at the block end, using each entry's duplicate count and the pad character. The highest-risk code in the port |
| `BranchBlock` | Entity | A whole interior node: the packed array of `keyLen + 8` entries, whose record number and child node are **big-endian** |
| `IndexEntry` | Entity | One (key, record number) pair — what a cursor is positioned on |
| `NodeReader` | Controller | Reads a block, or a tag header, by node number out of an `IRandomAccessSource`, with the guards of `CDX-FORMAT.md` §11. Both go through the same bounds check, so a directory pointing outside its file is refused as a corrupt index rather than reported as a short read |
| `IndexFileReader` | Controller | Opens an index file: header at offset 0, then either the tag directory (compound) or the single tag named after the file. Owns the source and the tags |
| `TagDirectory` | *folded into `IndexFileReader`* | Walking the directory is fifteen lines inside `Open` and a class of its own bought nothing. The directory is exposed as `IndexFileReader.Directory`, a `CdxTag` like any other, so the gate checks its blocks and keys the same way |
| `CdxTag` | Controller | One tag: its header, its geometry, and access to its blocks. Immutable; holds no position |
| `TagCursor` | Controller | A position in a tag: `Top`, `Bottom`, `Skip(±n)`, `Current`, `Eof`, `Bof`. Mutable, and separate from `CdxTag` so that state is not fused into description |

Two things fall out of this shape and are worth naming. **Opening a compound file exercises the leaf
codec before any tag is read**, because the tag directory is itself a tag with `keyLen` 10 — so a
codec bug cannot hide until later. And **the entities are pure**: every one of them takes a
`ReadOnlySpan<byte>` and returns values, so the whole risky half is testable at memory speed with no
file anywhere.

### Public surface

**None.** Nothing in this step is `public`; the golden tests reach the internals through the
`InternalsVisibleTo` that already exists. What a caller will eventually use — a tag on a `Table` —
is deliberately not designed yet, because 005 and `EXPR` will both have an opinion and neither has
been written.

### Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| index-file reads | `IRandomAccessSource` (already exists) | `InMemorySource` over a hand-built index image | every block shape, layer 2 |
| index-file failures | `IRandomAccessSource` | `FaultySource`, truncated images | a child pointer past the end, a short read mid-block, layer 3 |
| the pad character | `PadCharacterResolver` delegate | a lambda; from the dump's `pChar` in golden tests | Decision 6 — the one thing the file does not store |
| block bytes | none needed | spans | layers 1, no I/O at all |

Nothing new is opened: an index file is opened by path in these tests, because this step does not
connect one to a table.

---

## Decisions

1. **Traversal follows the leaf sibling chain; descent is used to find the ends.** `Top` descends
   from the root taking the first child at each level; `Skip` moves within the leaf and follows
   `rightNode` at its edge; `Bottom` descends taking the last child. The C library walks its
   root-to-leaf block path instead (`tfile4skip`), and the two are equivalent because the leaf level
   is a doubly-linked list that the split code maintains (`CDX-FORMAT.md` §8.3). This is simpler, and
   it comes with a free consistency check the C's version does not have: **a walk by sibling links
   and a walk that re-descends from the root for every key must produce the same sequence.** Both are
   run over the whole corpus.
   *Rejected — porting the block-path machinery.* It exists to support insert and split, which is
   where the path must be kept anyway. `WRITE` can add it then, against a gate that already exists.

2. **A packed leaf entry is decoded as a little-endian integer over exactly `infoLen` bytes.** The C
   reads a 32-bit word and masks (`x4recNo`, `d4declar.h:1826`), which over-reads past a 3-byte entry
   into its neighbour and discards the excess; with `infoLen > 4` it shifts its base pointer by two
   bytes and subtracts 16 from the shift (`x4dupCntLargeInfo`). Assembling `infoLen` bytes into a
   64-bit value and shifting by `recNumLen` and `recNumLen + dupCntLen` is arithmetically the same
   result — `infoLen` is at most 6, so 48 bits, and the masks are the stored ones — while never
   reading a byte that does not belong to the entry.
   *Rejected — reproducing the over-read.* It is a C memory trick, not a format fact, and it makes
   the last entry of a full block read into the key heap.

3. **The `.IDX` corpus case is derived from a single-tag `.CDX`, and the C library certifies it.**
   CodeBase can *read* a single-tag file — a header at offset 0 with `typeCode < 0x40`, one tag whose
   name comes from the file name (`u4namePiece`, i4index.c:1694, 1814-1825) — but it cannot
   *write* one: `i4create` always builds a tag directory with `typeCode = 0xE0` (i4create.c:847). So
   the generator builds `IDXONE.cdx` with exactly one tag and then makes `IDXONE.IDX` from it with
   the smallest possible edit: **copy the 1024-byte tag header from offset 1024 to offset 0 and clear
   the compound bit** (0x60 → 0x20). Nothing else moves. Node numbers are byte offsets, so leaving
   every tree block exactly where it is keeps `root`, both sibling pointers and every child pointer
   valid; the old header copy at 1024 becomes 1 KB of unreferenced space, which the format tolerates
   because freed blocks are ordinary. The library then **opens the result, runs `d4check` on it, and
   dumps it** — and `d4check` re-derives every key from the table (§A.2) — so the expected values are
   the C library's reading of the file, not our writing of it. **ADR-25.**
   *Rejected — hand-assembling an `.IDX` from the spec.* That is the "self-consistent
   misunderstanding" `DEV_APPROACH.md` §4 rules out: our writer and our reader would agree with each
   other and nothing else.
   *Rejected — using a real-world `.IDX`.* There is not one in the drop (`find -iname '*.idx'` is
   empty), and `original/examples/DATA/` is never a gate.
   *Rejected — dropping `.IDX` from scope.* It is the same S4FOX format read through a different
   entry point; the cost is one strategy at open, and a CodeBase application that used
   `i4open("X.IDX")` has these files.

4. **Only *compact* single-tag files are read, because that is all CodeBase reads.** Open refuses
   `typeCode < 32` (i4index.c:1706), and the compact bit is 0x20 — so a FoxPro 2.x non-compact
   `.IDX`, whose flags byte carries neither 0x20 nor 0x40, is refused by the C library too. The port
   refuses it with a message that says *what* the file is rather than that a byte was odd. This is a
   format fact that `CDX-FORMAT.md` §2 states only in passing; the spec gains a subsection for it in
   this step.

5. **The shape of the file is decided once, at open, and becomes a strategy.** `typeCode >= 0x40`
   means compound: read the tag directory, one tag per entry. Below that, one tag named after the
   file. Per `DEV_APPROACH.md` §3.2 this is resolved into an object at open, not re-tested at each
   call site — the same treatment `IDbfFormatVariant` already gets.

6. **The pad character is an input to the reader for machine-collated tags, because the file does not
   contain it** — and only for those, per Decision 9: a non-empty `sortSeq` settles it as `'\0'` by
   itself. Key reconstruction needs to know what byte the trail count stands for, and it is `' '` for a
   machine-collated character key and `'\0'` for everything else (i4init.c:557-602). The C library
   knows which because it *parses the key expression* and asks its type (`expr4type`); the header
   stores the expression text and no type. So until `EXPR` exists, a `PadCharacterResolver` delegate
   supplies it, the tag directory uses the known-fact `' '` (i4init.c:520-525), and golden tests take
   it from the `pChar` the dump records — a value from the reference implementation, used as *input*,
   which `DEV_APPROACH.md` §4 allows. When `EXPR` lands, the resolver is implemented over the
   expression's type and a test asserts the derived value equals the dump's for every corpus tag.
   *Rejected — guessing from `keyLen`.* 8 means numeric except when a character field is 8 wide, and
   a silent wrong pad character corrupts every padded key in the tag.
   *Rejected — exposing keys as significant bytes plus a trail count and letting the caller pad.*
   It pushes a format detail onto every caller and makes the gate compare something other than a key.

7. **Stored masks are used, not derived ones.** `recNumMask`, `dupByteCnt` and `trailByteCnt` are on
   disk and the C reads them from there rather than recomputing from the bit widths. We do the same,
   so a file whose mask disagrees with its width decodes the way CodeBase decodes it. Reproducing
   the reader means reproducing what it *reads*, not what it could have inferred.

8. **A block read is guarded, and the message says which node failed.** Node 0 and 0xFFFFFFFF are
   never valid block references (`CDX-FORMAT.md` §1, I4TAG.C:765-768); an offset below 1024 is inside
   the header area; a block that would run past the end of the file is corruption; and
   `keyLen - dup - trail < 0` inside a leaf is `e4index` (b4block.c:1916-1924). A 0-key non-root
   block is *tolerated* on read rather than refused — `i4readBlock` treats it as inconsistent only
   when `doIndexVerify` is on, and `CDX-FORMAT.md` §14 item 10 records that the library's own delete
   path can leave one behind.

9. **GENERAL-collated tags are read here, not deferred — reading a collated key needs no collation
   table.** An earlier draft refused any non-machine `sortSeq`, which confused *generating* a key with
   *reading* one. Three facts, checked in the C:

   - **Selecting the collation is a string compare and a code page.** `""` ⇒ machine; `"GENERAL"` ⇒
     cp1252 / cp437 / cp850 chosen by **the data file's** code page, with cp0 defaulting to cp1252 and
     **cp1250 refused outright**; `"CBnnnnn"` ⇒ a custom ordinal; anything else ⇒ `e4index`
     (i4init.c:372-418). No table is touched to decide this.
   - **The pad character stops being ambiguous.** For *any* non-machine collation `pChar = '\0'`
     (i4init.c:596-604). So a collated tag needs no resolver at all, and Decision 6's open question is
     confined to machine-collated tags — where it is `' '` for character keys and `'\0'` for the rest.
   - **Nothing in this step compares a key**, so the tables — which exist to turn a *search value*
     into key bytes — are not on this step's path. They become necessary in 005, for seeking a
     collated tag, and in `COLLATION`, for re-deriving a key from a field value.

   And there is a reason to *want* the case rather than merely tolerate it: `KEY-COLLATION.md` §3.7
   records that the GENERAL head+tail layout is **verified from source only** — not one of the 33
   shipped sample CDX files carries a `GENERAL` `sortSeq`. A generated case closes that standing gap
   (R11) and gates three things machine collation cannot reach: the head/tail key layout,
   `keyLen = 2 × the field width` (`keySizeCharPerCharAdd`, i4create.c:1040) — so `keyLen` stops
   tracking a field's width — and `pChar = '\0'` on a **character** tag, which is exactly what a wrong
   pad-character assumption would corrupt. The tables are already compiled into the generator
   (`i4conv.c:309` includes `coll4arr.c`; cp1252, cp437 and cp850 have static arrays), so the case
   costs a table and two tags. **ADR-27.**
   *Still refused:* `CBnnnnn` (disk-loaded tables, ungatable) and any `sortSeq` the C library itself
   rejects — the refusal reproduces `i4init.c:418`, naming what the file asked for.

10. **The `version` counter is read and reported, never acted on.** Reads are exclusive in this step,
    so there is nothing to re-validate. It is parsed big-endian because that is what it is, and
    `CDX-FORMAT.md` §10's mandate — a regular tag's `version` is always zero and must stay so — is a
    write-side rule that this step only has to not contradict.

11. **A cursor is separate from a tag, and a tag can hand out more than one.** `CdxTag` is
    immutable description; `TagCursor` holds `Eof`/`Bof`/position. The C fuses them into `TAG4FILE`
    because it has one position per tag, and the optimizer will eventually want several open at once
    over one tag. This is not speculative generality — it is the SRP split that makes the traversal
    state machine testable without a file, and it costs one class.

12. **The stored order is `(unsigned key bytes, record number)` — bytes alone are not the whole
    order, and this step asserts the part it can.** Nothing here compares a key, but the corpus can
    still be held to the invariant, so the gate asserts it for every tag: the walked sequence is
    non-decreasing under **unsigned** byte comparison with the record number breaking ties (reversed
    for a descending tag). That is the same invariant `d4check` enforces on the C side
    (`rc == 0 && num <= oldRec` ⇒ error, i4check.c:247-298), and it is worth stating because three
    things ride on top of "just compare the bytes", all of which land in **005**:

    - **Unsigned, and unsigned only.** `t4cdxCmp` casts both operands to `unsigned char *` and
      returns the count of leading equal bytes, or -1 once a stored byte exceeds the search byte
      (i4init.c:279-299); branch search uses `u4memcmp` → `c4memcmp` → `memcmp`
      (d4declar.h:162-164). Byte translation happens only under `S4VMAP`, which is not this build. A
      signed comparison would misorder every byte above 0x7F — every accented character, and every
      complemented negative numeric key.
    - **Bytes sort correctly only because the *encodings* are built for it**, not because bytes
      happen to sort: `t4dblToFox` reverses to big-endian and then adds 0x80 for positives or
      complements all eight bytes for negatives (i4conv.c:2432-2466); `t4intToFox`, `t4i8ToFox` /
      `t4curToFox` and the date-as-double path are the same idea; GENERAL puts primary weights in a
      head block ahead of the secondary tail block. **This is a `COLLATION` obligation, not a
      comparator one** — which is why that capability is gated separately on `value → key bytes`.
    - **A seek does not compare over `keyLen`.** `b4leafSeek` strips trailing pad characters from the
      *search value* (`b4calcBlanks`) and compares `min(searchLen, significant)` bytes, so `"SMITH"`
      finds `"SMITH               "`; and there are explicit special cases for bytes below the pad
      character and for all-blank keys (b4block.c:2245-2416, 2436-2461). A plain `memcmp` over the
      full key length gives wrong partial seeks. For a descending seek the key-increment rule is
      **collation-dependent** (last non-0xFF byte for machine, last byte ≥ 10 for GENERAL,
      I4TAG.C:2092-2151).

    One consequence is visible in this step's corpus and is a **format fact, not a defect**:
    `T_DBL`'s `-0.0` keys to `00 00 … 00`, because it takes the positive path (`-0.0 >= 0`) and the
    byte add wraps `0x80 + 0x80 → 0x00` — so it sorts *below every negative value*
    (`KEY-COLLATION.md` §2.1). Byte order and numeric order genuinely disagree there, the dump will
    show it at the top of that tag, and the port reproduces it rather than fixing it.

## Open questions

| # | Question | Status |
|---|---|---|
| Q1 | **Is ~600 records enough for a three-level tree, and is `D_WIDE`'s data incompressible enough?** The arithmetic says a 40-byte key packs about 11 entries per leaf, giving ~55 leaves, 6 branches and a root. It depends on the actual duplicate counts the bulk build computes. | **Answered by generating it.** Sub-step 3 asserts the depth it got by reading the dump's block attributes, and raises the record count if it came out at two levels. The number goes in `net/corpus/README.md` either way |
| Q2 | **Does the `[blocks]` section belong in the dump at all, given `[keys]` already gates the decode?** It doubles the dump's size, and a key sequence that matches is strong evidence the packing was read right. | **Leaning keep, decide in sub-step 2.** `[keys]` gates the *decode*; per-entry `dup`/`trail` gate the *encoding*, which is what `WRITE` will have to reproduce and what R1 is actually about. A key can also come out right from compensating errors — a wrong duplicate count with a wrong heap position |
| Q3 | **Should the `.IDX` case keep its source `.cdx` in the corpus?** Keeping both means the same tree is read through both shapes, which is a cheap structural cross-check; it also costs a second file that differs from the first in 1025 bytes. | **Answered: keep both**, and the cross-check turned out to be the case's *only* witness rather than a bonus — `d4check` cannot check a single-tag file (ADR-25's correction). Both files are in the corpus, and `IDXONE` is also the first table whose DBF header carries the production-index flag |
| Q4 | **Does `TagCursor.Skip(n)` for `\|n\| > 1` earn its place now, or is `Skip(±1)` enough for traversal?** The C's `tfile4skip` takes a count and short-circuits within a block. | **Leaning `Skip(long)` with the obvious loop**, no block-level shortcut until something measures it. Settle in sub-step 6 |
| Q5 | **Should a cp437 twin of `CDXCOLL` be added?** GENERAL's *array* is chosen by the DBF's code page, so cp1252 gates the layout while a second table would gate the selection — the same characters keying differently because the code page differs. cp850 needs `S4CODEPAGE_850` added to `cb-config.h`, a one-line additive change. | **Leaning yes for cp437, no for cp850**, decided in the same sub-step: if `CDXCOLL` lands cleanly the twin is a copy with different byte arrays and a different mark. Whatever is left out is named as ungated |
| Q6 | **What does the reader do with a tag whose expression text it cannot parse as text at all** — a non-UTF/non-ASCII expression, or `exprLen` disagreeing with the NUL terminator? | **Answered by a test, not a decision.** The expression is stored bytes; it is exposed as bytes *and* as a best-effort string, and nothing in this step depends on its meaning |
