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
they were not produced by VFP. `codePage` is `0x00` (unmarked) because that is what CodeBase
defaults to.

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
