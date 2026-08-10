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

Every table holds **32 records**. Rows 1-3 carry the edge cases (zero, minimum/maximum,
blank/empty); the rest vary so the same tables can be reused when index cases arrive.

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

## Three things to know before comparing bytes

**The header date stamp is frozen.** DBF bytes 1-3 are the "last update" date, which would
otherwise change on every regeneration. The generator overwrites them with `26-01-01`
(2026-01-01) after closing each table. It is the only place the generator alters what the C
library wrote, and it makes these files byte-stable.

**The memo extension is lower-case `.fpt`** while the table is `.DBF` — that is what CodeBase
writes (`d4defs.h:2589-2598`). Harmless on Windows; a port running on Linux must resolve the
companion file case-insensitively.

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
