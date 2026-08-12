# Corpus — golden test files

Checked-in reference files produced by the original Sequiter CodeBase C library, with the
expected values dumped beside them. The C# port is tested against these; **building or
testing CodeBase.NET never compiles or runs C**, and needs neither Windows nor MSVC.

Regenerate with [`test-files-generator/`](../../test-files-generator/README.md):

```bat
build-lib.bat & build-gen.bat & bin\testgen.exe & copy-corpus.bat
```

Never hand-edit these files, and never hand-write expected values in a test. If a code path
turns out to be untested, add a generator case and regenerate.

## Cases

| Files | Version | Covers |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x field set — `C`, `N`, `D`, `L`. No memo, no 263-byte reserved area. Same shape as the 40 dBase III tables in `original/examples/DATA/`. |
| `VFPTYPE.DBF` | `0x30` | Every non-memo Visual FoxPro type: `C N F D L I B Y T`. Exercises the 263-byte reserved area, 4-byte integers, 8-byte doubles, currency scaling and datetime (julian + ms) encodings. |
| `F2XMEMO.DBF` + `.fpt` | `0xF5` | FoxPro 2.x table with a memo: the **10-byte ASCII** in-record memo reference. |
| `VFPMEMO.DBF` + `.fpt` | `0x30` | VFP memo and binary types: `M` text memo, `X` binary memo, `G` general, `Z` binary character — the last three stored as `M`/`M`/`C` with descriptor flag `0x04`. **4-byte binary** memo references. |
| `VFPNULL.DBF` + `.fpt` | `0x30` | Nullable fields and the hidden `_NullFlags` bitmap. See below. |
| `CP1251.DBF` + `.fpt` | `0x30` | A marked code page, single-byte: header byte 29 = `0xC9`, Windows Cyrillic. High-byte text in the record and in the memo. See below. |
| `CP936.DBF` + `.fpt` | `0x30` | A marked code page, multi-byte: header byte 29 = `0x7A`, Simplified Chinese GBK. Trail bytes that look like ASCII, and characters cut in half at a field boundary. See below. |
| `CDXBASE.DBF` + `.cdx` | `0x30` | **Index, ten tags, one block each.** One tag per key shape: character with shared prefixes and blank keys, the same field descending, runs of duplicates, a unique tag, keys holding bytes below the pad character, numeric, double (including `-0.0`), date, integer, and a filtered tag. See below. |
| `CDXDEEP.DBF` + `.cdx` | `0x30` | **Index, multi-level trees.** 600 records and four tags; `D_WIDE` is three levels deep (55 leaves under two levels of branch), so interior nodes, sibling chains and full leaves are all reachable. See below. |
| `CDXCOLL.DBF` + `.cdx` | `0x30` | **Index, collation.** cp1252, one `C(20)` field indexed twice — machine (`keyLen` 20, pad `0x20`) and `GENERAL` (`keyLen` 40, pad `0x00`) — over accented text and the `œ`/`ß`/`þ` expansions. See below. |
| `IDXONE.DBF` + `.cdx` + `.IDX` | `0x30` | **Index, single-tag file.** The same 300-record tree in both shapes: a compound `.cdx` holding one tag, and the `.IDX` derived from it. See below. |

The seven memo and type tables hold **32 records** each; rows 1-3 carry the edge cases (zero,
minimum/maximum, blank/empty). The index tables hold 32, 600, 32 and 300 respectively — depth needs
records, and a wide key needs fewer of them.

`CDXBASE`, `CDXDEEP`, `CDXCOLL` and `IDXONE` are the first tables whose header byte 28 carries the
production-index bit (`0x01`), which `i4create` sets when the index file is the table's own
(`i4create.c:1404-1418`).

### What `VFPNULL` pins down

Ten nullable fields, interleaved with three plain ones, so **the null-bit ordinal is not the field
index** — `N_I` is field 8 and null bit 5. Ten bits make `_NullFlags` two bytes wide, so bits 8 and
9 land in the second byte. Records 1-6 are hand-picked bitmaps (all set `FF 03`, none set `00 00`,
alternating `55 01`, each byte alone); the rest follow a rolling pattern.

Three facts a reader would otherwise have to discover the hard way:

- **The file has one more descriptor than the API has fields.** `_NullFlags` (type `'0'`, flags
  `0x05`) is written after every user field, but `d4numFields` subtracts it (`d4declar.h:594`), so
  `[descriptors]` lists 14 and `[fields]` lists 13.
- **Nulling a field does not blank its bytes.** Every field is assigned a value before the nulls are
  applied, and the assigned bytes are still there — `N_C "ALPHA     " null=1`.
- **A memo that holds a block reference is never null.** Flushing memo content at append time writes
  the new block id with `f4assignLong` (`f4memo.c:801-807`), which clears the null bit. Rows that
  null the memo therefore leave it empty; **record 7 is the deliberate exception** — it asks for a
  null memo *and* writes 40 bytes of content, and the resulting bitmap is `01 00`, with bit 9 clear.

Memo payload lengths cycle through `0, 1, 7, 63, 200, 503, 504, 505` bytes: 504 is exactly one
512-byte FPT block once the 8-byte block header is added, and 505 is the first length that needs
two blocks.

### What `CP1251` and `CP936` pin down

Both are version `0x30` with a memo, and both hold `ID` `TEXT` … `MEMO`, so the only thing that
distinguishes them from `VFPMEMO` is the text they carry and the code page they name. Header byte 29
is the point: every other table leaves it `0x00`, so a reader that ignored it outright would pass the
rest of the suite.

**Neither marker is one CodeBase will set.** `c4setCodePage` accepts only cp0/437/850/1252/1250 and
raises `e4parm` on anything else, but `d4create` writes `CODE4.codePage` into the header verbatim
with no validation (`D4CREATE.C:1391`) and `d4open` reads it straight back (`D4OPEN.C:2217`). The
generator assigns the field directly. `0xC9` and `0x7A` are what Visual FoxPro stamps on Cyrillic and
Simplified Chinese tables, so the files stay realistic — see ADR-18 for why bytes the C library
refuses to set are the right ones to gate against.

`CP1251` is the single-byte half. Its `SWEEP` field walks `0x80-0xFF` whole across records 1-8,
sixteen bytes at a time, **including `0x98`** — the one byte cp1251 leaves undefined, so what a
reader makes of it is a decision and not an accident. `EXACT` is filled to its width and `SHORT`
never is, so blank padding beside high-byte content is gated in both directions, and the memo carries
the same high bytes so the FPT path is not left to the ASCII cases.

`CP936` is the multi-byte half, and it is the one that breaks byte-wise reasoning. GBK trail bytes
are `0x40-0xFE`, overlapping ASCII:

- **`TRAIL` holds characters whose second byte is `\`, `|`, `A`, `~` or `@`.** In the dump they show
  up as those literal characters inside `"\x81\\\x81|\x81A\x81~\x81@\x82\\"`. Anything scanning a
  character field byte-wise — for a path separator, a delimiter, a quote — finds them.
- **`CUT` is seven bytes wide and is assigned eight bytes of text**, so its last byte is always a
  lead byte with nothing behind it. A field width is a byte count, `f4assignN` truncates
  (`F4STR.C:155-168`), and Visual FoxPro produces exactly this. `EXACT` is the opposite case: four
  characters filling eight bytes with no padding at all.
- **Memo payload lengths 63 and 401 are odd**, so those payloads end mid-character too — the same cut
  on the FPT path instead of in the record.

Both marks are defined by **Visual FoxPro's** documentation — `0xC9` is 1251 and `0x7A` is 936, in the
26-mark table recorded in `DBF-FORMAT.md` §8.1, which ADR-19 makes the authority for this byte. The
port resolves both: these two tables report `CodePage.Cp1251`/`Cp936` and `CodePageNumber` 1251/936,
gated in `TableMetadataGoldenTests`. What is still missing is the last link — decoding a field's bytes
to a string — which is step 002, and these two dumps are the gate for it.

### What the index cases pin down

**`CDXBASE` is the readable one.** Ten tags over eight fields, 32 records, every tree a single
root-and-leaf block, so the whole dump can be read by a human. Between them the tags cover: duplicate
counts from 0 to 14 and trail counts from 0 to the whole key; two blank keys adjacent, which is the
invariant that a key of all pad has `dup=0` however its neighbour looks; two pairs of equal keys, whose
order is then the record number; a key filling its field exactly; keys carrying `0x00`, `0x1F` and
`0x80`-`0xFF`, so only an unsigned comparison orders them; `-0.0`, which keys to eight zero bytes and
sorts below every negative; a blank date; `LONG_MIN` and `LONG_MAX`; a unique tag holding 5 keys where
the table has 32 records; and a filtered tag holding 22.

**`CDXDEEP` is where interior nodes live.** Depth is bought with key *width* rather than record count:
`D_WIDE` is a deliberately incompressible `C(40)`, so a leaf holds about eleven keys and 600 records
need 55 leaves, six branches and a root — **three levels**. `D_PFX` is the opposite extreme, a `C(20)`
with a fourteen-byte shared prefix packing 114 keys into a leaf with 3 bytes to spare; `D_DUP` has ten
distinct values over 600 records, so runs of equal keys cross leaf boundaries and interior entries end
up with keys equal to each other; one of its leaves has `freeSpace` exactly 0.

**`IDXONE` is one tree in two file shapes.** `IDXONE.cdx` is an ordinary compound file holding a single
tag; `IDXONE.IDX` is derived from it by copying the tag header from offset 1024 to offset 0 and
clearing the compound bit (`0x60` → `0x20`). Nothing else moves, so every node number stays valid and
the two files differ in 1025 bytes. The C library **cannot write** a single-tag file
(`i4create.c:847`) — and **cannot `d4check` one either**, because its block accounting flags the tag
directory's header and then every tag's header, which in such a file is the same block
(`i4check.c:889-914`). So this case is witnessed differently: the generator walks both files and
requires the same 300 keys and record numbers from each. See ADR-25.

## The index dump

`<NAME>.cdx.dump.txt` accompanies each index file (`<NAME>.idx.dump.txt` for the `.IDX`). It is a
sibling of the table's dump rather than more sections inside it — ADR-24, which also explains the rule
that matters most here: **every value comes from the C library's own structures, never from a re-read
of the bytes.** Keys and record numbers come from its navigation (`tfile4key`, `tfile4recNo`), block
structure from the live block object at `tfile4block`, and the per-entry counts from its own
`x4dupCnt`/`x4trailCnt`/`x4recNo` macros. A bit-packed leaf is the highest-risk decode in the port, and
a generator that unpacked it here would prove only that our writer and our reader misunderstand the
format the same way.

```
file         CDXBASE.cdx          the index file
table        CDXBASE.DBF          the table it belongs to
shape        compound             or single-tag
blockSize    512                  multiplier 1 unless the CodeBase note says otherwise
check        ok                   d4check, or skipped-single-tag with the reason above

[tag *directory*]                 the hidden tag-name tree, read as the tag it is
header       keyLen=… typeCode=0x… signature=0x… descending=… pChar=0x… root=… freeList=…
             version=… headerNode=…
text         exprPos=… exprLen=… filterPos=… filterLen=… sortSeq="GENERAL"
expr         "K_TEXT"
filter       ""
count        10                   keys in the tag
[blocks]                          every block, in the order a full walk reached it
node=… attr=… nKeys=… left=… right=… leaf=1 freeSpace=… recNumLen=… dupCntLen=…
       trailCntLen=… infoLen=… recNumMask=0x… dupByteCnt=0x… trailByteCnt=0x…
  0 rec=… dup=… trail=…           one line per packed leaf entry
  …                               an interior block instead lists: i child=… rec=… key="…"
blocks 1                          how many blocks were listed
[keys]                            navigation order — reversed for a descending tag
  "ALPHA               " 5
```

Two things to know when comparing against it. `left` and `right` are printed as unsigned decimal with
no interpretation, so "no sibling" reads as `4294967295`. And the `[blocks]` order is documentation
rather than a promise — a port may read blocks in any order, so the golden tests compare them as a set
keyed by node number.

## Three things to know before comparing bytes

**The header date stamp is frozen.** DBF bytes 1-3 are the "last update" date, which would
otherwise change on every regeneration. The generator overwrites them with `26-01-01`
(2026-01-01) after closing each table. It is the only place the generator alters what the C
library wrote, and it makes these files byte-stable.

**The memo extension is lower-case `.fpt`** while the table is `.DBF` — that is what CodeBase
writes (`d4defs.h:2589-2598`). **The production index is lower-case `.cdx` for the same reason.**
Harmless on Windows; a port running on Linux must resolve the companion file case-insensitively. The
derived `IDXONE.IDX` is upper-case because the generator, not CodeBase, names it.

**These files carry CodeBase provenance, not Visual FoxPro's.** They are written in genuine-VFP
shape (`flags[8]` and `autoIncrementVal` zero, no trailing `0x1A`, no CodeBase extensions), but
they were not produced by VFP. `codePage` is `0x00` (unmarked) in every case but `CP1251` and
`CP936`, because unmarked is what CodeBase defaults to.

## Dump format

`<NAME>.dump.txt` accompanies each table. LF line endings, plain text, meant to be diffed in
review. Sections:

- header fields read **raw from the file** (version, frozen stamp, `numRecs`, `headerLen`,
  `recordLen`, `hasMdxMemo`, `codePage`);
- `[descriptors]` — the 32-byte field descriptors **as stored on disk**. This is the
  authoritative view: `d4create` rewrites `X`→`M` and `Z`→`C` and records "binary" in the flags
  byte, so the stored type differs from the creation type;
- `[fields]` — what the C library reports after reopening, which is where the `X`/`Z` creation
  types reappear;
- `[records]` — per record, the raw in-record bytes of every field (printable ASCII verbatim,
  everything else `\xHH`), plus a decoded value for numeric, integer, date and datetime fields.
  Memo fields show the in-record reference (`ref=`) and then the memo contents.

Three tokens appear **only when they are true**, so a table without nullable fields dumps exactly as
it did before they existed:

| Token | Where | Meaning |
|---|---|---|
| `nullable=1` | `[fields]` line | the field accepts nulls (`f4nullable`) |
| `null=1` | `[records]` field line | the field is null in this record (`f4null`) |
| `_NULLFLAGS "…"` | last line of each record | the raw `_NullFlags` bitmap bytes. The field is not in `[fields]`, so without this line the bitmap layout would never be gated against stored bytes — only through the `null=1` flags. |

The name is upper-cased here (`_NULLFLAGS`) but stored mixed-case in the descriptor (`_NullFlags`):
CodeBase upper-cases every field name on open (`c4upper`, `D4OPEN.C:316`) while matching the system
field against the stored bytes case-sensitively (`D4OPEN.C:2611`).
