# CodeBase DBF File Format Specification (S4FOX / Visual FoxPro build)

**Source basis:** CodeBase 6.x C source in `original/source` (Sequiter Inc., LGPL release).
**Primary build configuration:** `S4FOX` (Visual FoxPro compatible index/memo/data formats). `S4CLIPPER`/`S4MDX` divergences are noted only where the on-disk DBF differs. All `S4CLIENT` (client/server) paths are ignored — this spec covers the stand-alone engine only.

All multi-byte integers in the file are **little-endian** (the code reads/writes native x86 structs directly and applies `x4reverseLong`/`x4reverseShort`/`x4reverseDouble` only when compiled with `S4BYTE_SWAP` for big-endian platforms, e.g. D4OPEN.C:2124-2135, D4CREATE.C:1462-1473).

---

## 1. Overall file structure

```
+--------------------------------------------+
| 32-byte file header (DATA4HEADER_FULL)     |
+--------------------------------------------+
| N x 32-byte field descriptors              |   (or variable-size, long-field-name variant, §5.4)
| [optional 32-byte "_NullFlags" descriptor] |
+--------------------------------------------+
| 0x0D header terminator                     |
| VFP(0x30/0x31): 263 zero bytes             |   (counted in headerLen)
+--------------------------------------------+   <- offset = headerLen
| record 1  (recordLen bytes)                |
| record 2                                   |
| ...                                        |
| record numRecs                             |
| non-VFP only: trailing 0x1A EOF byte       |
+--------------------------------------------+
```

- `headerLen` includes the `0x0D` terminator (and, for VFP, the following 263 zero bytes) but **not** any `0x1A`. For non-VFP files the `0x0D` is the last header byte and record 1 begins immediately after it (confirmed in EXAMPLE.DBF: byte 256 = `0x0D`, byte 257 = record-1 delete flag `0x20`, with `headerLen`=257; CLASSES.DBF: byte 160 = `0x0D`, byte 161 = record, `headerLen`=161). At create time non-VFP writes a `0x1A` at the `headerLen` position, but it is the first record's slot and is overwritten by record 1 (§2.4, §3.1). For VFP the 263 zero bytes sit inside `headerLen` (confirmed in FOXUSER.DBF: `0x0D` at byte 256, zeros through 519, record 1 delete flag `0x20` at byte 520 = `headerLen`).
- Record `n` starts at file offset `headerLen + recordLen * (n - 1)` (macro `dfile4recordPosition`, d4declar.h:1992-1994).
- Records are never physically deleted except by pack; deletion is a flag byte (§7).

---

## 2. 32-byte file header

In-memory mirror struct `DATA4HEADER_FULL` (d4data.h:3062-3090); it is read/written as a raw 32-byte file image (`file4readAllInternal(&d4->file, 0, &fullHeader, sizeof(fullHeader))`, D4OPEN.C:2120-2122; `file4seqWrite(&seqWrite, &createHeader, sizeof(createHeader))`, D4CREATE.C:1475).

| Offset | Len | Type | Field | Description |
|--------|-----|------|-------|-------------|
| 0 | 1 | u8 | `version` | Version/signature byte. See §2.1. (d4data.h:3065) |
| 1 | 1 | u8 | `yy` | Last-update year. FOX build writes `year % 100`; MDX build writes `year - 1900` (u4util.c:1010-1027) |
| 2 | 1 | u8 | `mm` | Last-update month 1-12 (u4util.c:1026) |
| 3 | 1 | u8 | `dd` | Last-update day 1-31 (u4util.c:1027) |
| 4 | 4 | i32 LE | `numRecs` | Number of records (`S4LONG`, signed 32-bit; d4data.h:3069, d4defs.h:2355-2358) |
| 8 | 2 | u16 LE | `headerLen` | Total header length = start offset of record data (d4data.h:3070) |
| 10 | 2 | u16 LE | `recordLen` | Length of one record including the delete-flag byte (d4data.h:3071) |
| 12 | 8 | u8[8] | `flags[8]` | **CodeBase-specific** feature flags, all zero in genuine VFP files. Each flag is a full byte holding 0 or 1. See §2.2. (d4data.h:3073-3081) |
| 20 | 8 | f64 LE | `autoIncrementVal` | **CodeBase-specific**: current auto-increment counter as an IEEE-754 double (d4data.h:3082-3086). Zero in genuine VFP files. |
| 28 | 1 | u8 | `hasMdxMemo` | `0x01` = production index (.CDX/.MDX) attached; FOX: `0x02` = memo (.FPT) file attached (d4data.h:3087; memo bit set at D4CREATE.C:1358-1361; CDX bit written at file offset `4 + sizeof(S4LONG) + 2*sizeof(short) + sizeof(char[16])` = 28, i4create.c:1408-1417) |
| 29 | 1 | u8 | `codePage` | Language-driver / code-page id byte (d4data.h:3088). See §9. |
| 30 | 2 | u8[2] | `zero2` | Reserved, written as zero (d4data.h:3089; header is `memset` to 0 before filling, D4CREATE.C:1340) |

### 2.1 Version byte values

Values **written** by `dfile4createHeader` (D4CREATE.C:1341-1384):

| Value | Build | Condition |
|-------|-------|-----------|
| `0x30` | S4FOX, `compatibility == 30` | Visual FoxPro 3.0+ table, no CodeBase extensions (D4CREATE.C:1357) |
| `0x31` | S4FOX, `compatibility == 30` | VFP table **with** any CodeBase extension: autoIncrement, compressed memos, compressed data, autoTimestamp, or long field names (D4CREATE.C:1351-1355). Note: real VFP uses 0x31 for "autoincrement present"; CodeBase overloads it for all its extensions and records which ones in `flags[]` (§2.2). |
| `0xF5` | S4FOX, `compatibility == 25/26` | FoxPro 2.x table with memo (D4CREATE.C:1367) |
| `0x03` | S4FOX, `compatibility == 25/26` | FoxPro 2.x table without memo (D4CREATE.C:1370); also generic dBase III in non-FOX builds (D4CREATE.C:1383) |
| `0x83` | S4MNDX build only | dBase III+ with .DBT memo (D4CREATE.C:1376) |
| `0x8B` | S4MMDX (dBase IV/MDX) build only | dBase IV with memo (D4CREATE.C:1379) — **not** a VFP code |

Values **accepted** on open (`dfile4setup`, D4OPEN.C:2144-2224):

- Any version byte is accepted structurally; behavior branches on value:
  - `0x31` — CodeBase-extended VFP: the `flags[]` bytes are validated; if any flag other than `flags[0..4]` is set, open fails with `e4data` (D4OPEN.C:2152-2187). In memory the version is normalized to `0x30` (D4OPEN.C:2188, comment d4data.h:3220).
  - `0x30` — VFP 3.0+; `compatibility` forced to 30 (D4OPEN.C:2206-2207); memo presence = `hasMdxMemo & 0x02` (D4OPEN.C:2352-2353).
  - `0x32` — VFP 9 tables are recognized read-compatibly: `version >= 0x30` gates VFP-only field types (D4OPEN.C:2565-2612), and the 'V'/'Q' field-length checks are relaxed for `0x32` (D4OPEN.C:2482-2487, 2538-2543).
  - Any other value (`0x03`, `0xF5`, `0x83`, ...) — treated as a FoxPro 2.5-level file; VFP-only field types cause an `e4data` error (`version < 0x30` checks, D4OPEN.C:2588-2611); memo presence = `version & 0x80` (D4OPEN.C:2355).
  - `0x04` (dBase 7) — explicitly rejected in the MDX build (`e4notSupported`, D4OPEN.C:2221-2223); FOX build has no special handling for it.

### 2.2 CodeBase `flags[8]` (bytes 12-19, only meaningful when version = 0x31)

Each is a whole byte, value `0` or `1` (d4data.h:3073-3081, D4CREATE.C:1406-1456, D4OPEN.C:2158-2200):

| Index (file offset) | Meaning when 1 |
|---------------------|----------------|
| `flags[0]` (12) | Table contains an auto-increment field; `autoIncrementVal` (offset 20) is live (D4CREATE.C:1406) |
| `flags[1]` (13) | Attached memo file may contain compressed entries (D4CREATE.C:1432) |
| `flags[2]` (14) | Data file itself is compressed (compressed-table info stored after the header at `headerLen`, D4OPEN.C:2766-2788) (D4CREATE.C:1443) |
| `flags[3]` (15) | Table contains an auto-timestamp field (D4CREATE.C:1419) |
| `flags[4]` (16) | Header uses the long-field-name descriptor layout, §5.4 (D4CREATE.C:1455) |
| `flags[5..7]` | Must be 0; unknown set flags make the file unopenable by this engine (D4OPEN.C:2186-2187) |

All five features require `compatibility == 30`; requesting them otherwise returns `e4compatibility` (D4CREATE.C:1395-1456).

### 2.3 headerLen calculation on create

`dfile4createCalcHeaderLen` (D4CREATE.C:1243-1298):

- Normal (short-name) layout: `headerLen = 32 * (numFields + 1) + 1` (32-byte header + 32 per field + 1 terminator byte) (D4CREATE.C:1255).
- Plus `32` if a `_NullFlags` field is added (`numNulls > 0`, compat 30) (D4CREATE.C:1265).
- Plus `263` reserved bytes for compat-30 files without long field names ("visual fox reserves an extra 263 bytes", D4CREATE.C:1269).
- Long-field-names layout: `headerLen = numFields*21 + 34 + Σ(2 + strlen(name) + 1)` per field (D4CREATE.C:1250-1252), plus `2 + strlen("_NullFlags") + 22` if nulls present (D4CREATE.C:1263).

### 2.4 Header terminator and reserved area

Written after the field descriptors (D4CREATE.C:1692-1703):

- compat 30, short names: one byte `0x0D` followed by **263 bytes of 0x00** (D4CREATE.C:1695-1697).
- otherwise (compat 25/26, or long-name layout, or non-FOX builds): the two bytes `0x0D 0x1A` (D4CREATE.C:1700-1702).

The 263-byte area is where Visual FoxPro stores the **DBC backlink** (relative path of the owning database container). **CodeBase always writes zeros there and never reads it — DBC containers are not supported** (no reference to any backlink anywhere in the source; the area is emitted only as `file4seqWriteRepeat(&seqWrite, 263, '\0')`, D4CREATE.C:1697). A zero first byte means "free table" to VFP, so CodeBase-created files open in VFP as free tables.

On open, the field-descriptor region is sized as `fieldDataLen = headerLen - 32` (D4OPEN.C:2323) and the number of fields is counted by scanning 32-byte steps until a `0x0D` byte is found; missing terminator ⇒ `e4data` (D4OPEN.C:2736-2744). For the long-name layout the scan is by variable-length entries until `0x0D` or `offset == infoLen - 2` (D4OPEN.C:2713-2729).

### 2.5 Header maintenance at runtime

`dfile4updateHeader(data, doTimeStamp, doCount, doAutoIncrement)` (d4file.c:254-380):

- Never rewrites the version byte in normal operation: with timestamp it writes file offsets 1..9 (`yy mm dd numRecs headerLen`; `pos = 1`, `len = 3 + 4 + 2`, d4file.c:321-327), without timestamp offsets 4..9 (`pos = 4`, `len = 4 + 2`, d4file.c:329-333).
- When `doAutoIncrement` and the table has an autoincrement field, `len += 18`, extending the write through `recordLen`(2) + `flags`(8) + `autoIncrementVal`(8), i.e. up to offset 27 (d4file.c:340-347).
- The date stamp (`u4yymmdd(&data->yy)`) and header rewrite happen on close and on file-unlock (d4close.c:71-78, df4unlok.c:77-80).
- Skipped entirely while a transaction is active or rolling back (d4file.c:270-277) and for read-only files (d4file.c:283-285).

### 2.6 Record count semantics and open-time validation

- `numRecs` is a signed 32-bit count of *all* records, including delete-flagged ones.
- On open (non-large-file mode), the engine computes `numRecordsBasedOnFileLength = (fileLen - headerLen) / recordLen` (D4OPEN.C:2240-2251). If the file is opened `OPEN4DENY_WRITE`/`OPEN4DENY_RW`, `numRecs` must equal that value exactly or open fails with `e4data` (D4OPEN.C:2256-2263). In shared mode (debug `E4MISC` builds), a discrepancy of more than +1 record triggers a re-check under the append lock and then failure (D4OPEN.C:2276-2301).
- `recordLen == 0` in the header is rejected (divide-by-zero guard, D4OPEN.C:2230-2231); exception: in large-file mode a genuinely huge record length that overflowed the u16 is tolerated and recomputed from the field list (D4OPEN.C:2137, 2665-2685).
- The recomputed record width from the field descriptors (1 + Σ field lengths) must equal the header's `recordLen`, else `e4data` (D4OPEN.C:2671-2675).
- On append, a new record is written at `headerLen + recordLen*count`; `numRecs` is only incremented in memory (D4APPEND.C:225-245) and flushed to the header via §2.5.

---

## 3. Record layout

- Byte 0 of each record is the **delete flag**: `' '` (0x20) = live, `'*'` (0x2A) = deleted (`d4delete` sets `record[0] = '*'`, d4data.c:306-320; `d4recall` restores `' '`, d4data.c:621-633; `d4deletedInternal(d4)` is `*(d4)->record != ' '`, d4declar.h:750; append validates `record[0]` is `' '` or `'*'`, D4APPEND.C:297).
- Fields follow contiguously with **no alignment padding**. Field data offsets are recomputed at open time by accumulation starting at offset 1, ignoring the descriptor's stored offset field (`recOffset = 1; ... field.offset = recOffset; recOffset += field.len`, D4OPEN.C:270, 432-433).
- A blank (freshly appended) record is byte-0 `' '` plus each field's blank value (§6.14) (`d4openConcludeBlankRecord`, D4OPEN.C:451-455).

### 3.1 End-of-file marker

- **compat 30 (VFP) files: CodeBase maintains no 0x1A EOF byte after the last record.** "3.0 files do not write the extra eof byte" (D4APPEND.C:1716-1723); on append, `lenWrite` is exactly `recWidth` when `compatibility == 30`, and `recWidth + 1` (record + 0x1A staged at `record[recWidth]`, D4APPEND.C:814) otherwise (D4APPEND.C:229-241). **Reader caveat:** native Visual FoxPro *does* write a trailing `0x1A` even for 0x30 files, so third-party VFP tables commonly carry one (confirmed in FOXUSER.DBF, a real VFP 3.0 file: `headerLen`=520, `recordLen`=48, `numRecs`=7 ⇒ records end at 856, and byte 856 = `0x1A`, file length 857). The open-time count check tolerates it because `numRecs` is compared against the *floored* `(fileLen - headerLen) / recordLen` — `(857-520)/48 = 7` — so a lone trailing marker does not break the exact-match validation (§2.6).
- Non-30 files: every append rewrites the trailing `0x1A` after the new last record, and `d4unappend` truncates the file and re-writes `0x1A` (D4APPEND.C:1714-1723).

---

## 4. Field descriptor (32 bytes)

Struct `FIELD4IMAGE` (d4data.h:2933-2947), written verbatim per field (D4CREATE.C:1651):

| Offset | Len | Type | Field | Description |
|--------|-----|------|-------|-------------|
| 0 | 11 | char[11] | `name` | Field name, upper-cased, space/NUL-trimmed, zero-padded (image is memset 0 then `u4ncpy`/`c4trimN`/`c4upper`, D4CREATE.C:1488-1491). On open compared case-insensitively; `_NullFlags` is matched by `memcmp` of 10 bytes (D4OPEN.C:2611). |
| 11 | 1 | char | `type` | Field type character, upper-cased (D4CREATE.C:1493-1494). See §6. |
| 12 | 4 | i32 LE | `offset` | Offset of the field's data within the record (delete flag = offset 0, first field = 1). Written only in the FOX build (D4CREATE.C:1495-1500); **ignored on open** (offsets recomputed, D4OPEN.C:432-433). |
| 16 | 1 | u8 | `len` | Field length low byte (d4data.h:2938) |
| 17 | 1 | u8 | `dec` | Decimal count — except for `C`/`W`/`O`/`Z` fields where it is the **high byte of a 16-bit length** (§6.1) (d4data.h:2939, D4CREATE.C:1516, D4OPEN.C:2561, D4OPEN.C:425) |
| 18 | 1 | u8 | `nullBinary` | VFP field flags: `0x01` system field, `0x02` nullable, `0x04` binary/no-CP-translation, `0x08` auto-increment (CodeBase), `0x10` auto-timestamp (CodeBase) (d4data.h:2941, D4CREATE.C:1503-1510, `_NullFlags` gets `0x05` = system+binary, D4CREATE.C:1669) |
| 19 | 12 | u8[12] | `filler2` | Reserved, zeros (d4data.h:2942). **Note:** genuine VFP stores the autoincrement next-value (i32 at 19) and step (u8 at 23) here; CodeBase leaves these zero and uses the file-header double instead (§8). |
| 31 | 1 | u8 | `hasTag` | MDX build: field has a production-index tag. FOX: always 0 (d4data.h:2946) |

Descriptor-flag semantics on open (D4OPEN.C:324-380, applied only when `d4version == 0x30`):
- `nullBinary & 0x02` → field is nullable, gets the next `nullBit` (assigned in physical field order, D4OPEN.C:361-370).
- `nullBinary & 0x08` → auto-increment field (D4OPEN.C:329-350).
- `nullBinary & 0x10` → auto-timestamp field (D4OPEN.C:353-358).
- `nullBinary & 0x04` → `binary = 1`; memo/general fields without it get `binary = 2` (memo data is always binary) (D4OPEN.C:372-380).

### 4.1 `_NullFlags` system field

If any field is created with `nulls == r4null` (r4null = 190, d4defs.h:1838) and `compatibility == 30`, one extra hidden field is appended after all user fields (D4CREATE.C:1659-1687):

- `name` = `"_NullFlags"` (`NULL4FLAGS_FIELD_NAME`, d4defs.h:1069)
- `type` = `'0'` (`r4system`, d4defs.h:2813)
- `nullBinary` = `0x05` (D4CREATE.C:1669)
- `len` = `(numNulls + 7) / 8` — one bit per **nullable** field (D4CREATE.C:1670)
- `dec` = 0

Bitmap encoding (f4assignNull/f4assignNotNull, F4FIELD.C:1702-1806; bit lookup precomputed at open, D4OPEN.C:363-368):

```
nullBit  = ordinal of the field among nullable fields, in physical order, 0-based
byteNum  = nullBit / 8
mask     = 1 << (nullBit % 8)
_NullFlags[byteNum] & mask != 0  =>  field is NULL
```

Assigning any value to a nullable field clears its null bit (`f4assignPtr` → `f4assignNotNull`, F4FIELD.C:32-52). On open, a `'0'` field is only accepted when `version >= 0x30` **and** its name is `_NullFlags` (D4OPEN.C:2608-2612).

### 4.2 Long-field-name layout (CodeBase extension, `flags[4]`)

When any field name exceeds 10 characters, version is 0x31 with `flags[4]=1`, and each descriptor is written as (D4CREATE.C:1635-1648):

| Part | Size | Content |
|------|------|---------|
| descriptor tail | 21 | `FIELD4IMAGE` starting at `type` (`sizeof(FIELD4IMAGE) - 11` = 21 bytes: type, offset, len, dec, nullBinary, filler2, hasTag) |
| nameLen | 2 (i16 LE) | `strlen(name) + 1` (includes NUL) |
| name | nameLen | NUL-terminated field name |

Reader: D4OPEN.C:2421-2435 (the `FIELD4IMAGE*` is aimed at `info + offset - 11` so `image->type` etc. line up). This layout is **not readable by FoxPro**.

---

## 5. Field type characters

Constants from d4defs.h:2774-2821. `r4charBin`('Z') and `r4memoBin`('X') are creation-time aliases converted to `'C'`/`'M'` plus `nullBinary |= 0x04` in the stored descriptor (d4defs.h:2771-2772, D4CREATE.C:1565-1568, 1623-1626).

| Char | Constant | In-record bytes | Create descriptor (len, dec, nullBinary mask) |
|------|----------|-----------------|-----------------------------------------------|
| `'B'` | `r4double` (FOX) / `r4bin` (MDX memo) | 8 | (8, user dec, 0x04) (D4CREATE.C:1616-1618) |
| `'C'` | `r4str` | len | (len & 0xFF, len >> 8, 0) (D4CREATE.C:1516) |
| `'D'` | `r4date` | 8 | (8, 0, 0) (D4CREATE.C:1519) |
| `'F'` | `r4float` | len | (len, dec, 0) — identical storage to `'N'`, treated as `r4num` internally (F4FIELD.C:304-305, 320-321) |
| `'G'` | `r4gen` | 4 or 10 | len = 4 if compat 30 else 10 (D4CREATE.C:1604-1611) |
| `'H'` | `r4floatBin` | 4 | (4, dec, 0x04) (D4CREATE.C:1620-1622) — CodeBase extension |
| `'I'` | `r4int` | 4 | (4, 0, 0x04) (D4CREATE.C:1583-1586) |
| `'L'` | `r4log` | 1 | (1, 0, 0) (D4CREATE.C:1522) |
| `'M'` | `r4memo` | 4 or 10 | len = 4 if compat 30 else 10 (D4CREATE.C:1604-1611) |
| `'N'` | `r4num` | len | (len, dec, 0) (D4CREATE.C:1526) |
| `'O'` | `r5wstrLen` | even(len+2) | (even4up(len+2) & 0xFF, (len+2)>>8, 0x04) (D4CREATE.C:1561-1563) — CodeBase extension |
| `'P'` | `r5ui4` | 4 | (4, 0, 0x04) (D4CREATE.C:1583-1585) — CodeBase ext.; **collides with VFP 'P' (Picture)** |
| `'Q'` | `r5i2` | 2 | (2, 0, 0x04) (D4CREATE.C:1576-1578) — CodeBase ext.; VFP9 uses 'Q' as varbinary, accepted loosely when version = 0x32 (D4OPEN.C:2536-2549) |
| `'R'` | `r5ui2` | 2 | (2, 0, 0x04) (D4CREATE.C:1576-1578) — CodeBase extension |
| `'T'` | `r4dateTime` | 8 | (8, 0, 0x04) (D4CREATE.C:1572-1574) |
| `'V'` | `r5guid` | 16 | (16, 0, 0x04), `LEN5GUID` = 16 (D4CREATE.C:1555-1557, d4defs.h:1721) — CodeBase GUID; VFP9 'V' (Varchar) accepted loosely when version = 0x32 (D4OPEN.C:2479-2492) |
| `'W'` | `r5wstr` (`r4unicode`) | even(len) | (even4up(len & 0xFF), len >> 8, 0x04) (D4CREATE.C:1558-1559); `even4up(a) = a + (a & 1)` (d4defs.h:2309) — CodeBase ext.; **collides with VFP 'W' (Blob)** |
| `'X'` | `r4memoBin` | 4 | stored as `'M'`, (4, 0, 0x04) (D4CREATE.C:1623-1626) |
| `'Y'` | `r4currency` | 8 | (8, 4, 0x04) (D4CREATE.C:1569-1571) |
| `'Z'` | `r4charBin` | len | stored as `'C'`, (len & 0xFF, len >> 8, 0x04) (D4CREATE.C:1565-1567) |
| `'0'` | `r4system` | (numNulls+7)/8 | §4.1 only (D4CREATE.C:1668-1671) |
| `'1'` | `r5i8` | 8 | (8, 0, 0x04) (D4CREATE.C:1587-1589) — CodeBase/OLE-DB extension |
| `'2'` | `r5dbDate` | 6 | (6, 0, 0x04) (D4CREATE.C:1591-1593) — CodeBase/OLE-DB extension |
| `'3'` | `r5dbTime` | 6 | (6, 0, 0x04) (D4CREATE.C:1591-1593) — CodeBase/OLE-DB extension |
| `'4'` | `r5dbTimeStamp` | 16 | (16, 0, 0x04) (D4CREATE.C:1595-1596) — CodeBase/OLE-DB extension |
| `'5'` | `r5date` | 8 | (sizeof(double), 0, 0x04) (D4CREATE.C:1580-1582) — OLE automation date — CodeBase extension |
| `'6'` | `r5ui8` | 8 | (8, 0, 0x04) (D4CREATE.C:1587-1589) — CodeBase extension |
| `'7'` | `r4dateTimeMilli` | 8 | (8, 0, 0x04) (D4CREATE.C:1572-1574) — CodeBase extension (datetime keeping milliseconds) |

Open-time length validation (D4OPEN.C:2448-2649): `M`/`G` must be 4 or 10 in the FOX build (D4OPEN.C:2454-2457); `V` = 16 unless version 0x32; `5` = 8; `1`/`6` = 8; `I`/`P` = 4; `R` = 2; `Q` = 2 unless version 0x32; VFP-only types (`B` as double, `H`, `Y`, `T`, `7`, `0`) require `version >= 0x30` (D4OPEN.C:2564-2617). Unknown type characters ⇒ `e4fieldType` error (D4OPEN.C:2643-2647).

---

## 6. Per-type in-record encodings

### 6.1 `C` character (and `Z` binary character)

Fixed-length, space-padded on the right; contents copied verbatim (`f4assignN`: truncate to `field->len` or copy + `f4blank` remainder, F4STR.C:154-168). Field length up to 65535: the 16-bit length is split as `len` = low byte, `dec` = high byte both in the descriptor (D4CREATE.C:1516) and reassembled on open as `field.len = image->len + (image->dec << 8)` (D4OPEN.C:425, record-width check uses `imageLen + (imageDec << 8)`, D4OPEN.C:2561). The binary variant (`Z` → C + flag 0x04) suppresses code-page tagging only; the byte layout is identical.

### 6.2 `N` numeric / `F` float

Right-justified ASCII decimal in a field of `len` bytes with `dec` decimals, produced by `c4dtoa45` (c4.c:763-921): buffer is filled `'0'`, integer part space-padded on the left, explicit `'.'` at position `len - dec - 1` when `dec > 0`, `'-'` immediately before the first digit for negatives, and **all-asterisks (`'*' * len`) on overflow** (c4.c:816-820). Limits enforced at create (FOX): `len ≤ 20` (`F4MAX_NUMERIC`, d4defs.h:2130), `dec ≤ 19` (`F4MAX_DECIMAL`, d4defs.h:2131), `dec ≤ len - 1` (`F4DECIMAL_OFFSET` = 1, d4defs.h:2132; check at D4CREATE.C:1545). Read back with `c4atod`/`c4atoi` on the text (F4DOUBLE.C:340, F4INT.C:262). Blank = all spaces.

### 6.3 `D` date

8 ASCII bytes `"YYYYMMDD"` (`date4assignLow` writes year 4 digits, month 2, day 2, zero-filled, D4DATE.C:404-406). Blank/empty = 8 spaces; `"00000000"` is also treated as blank on read (D4DATE.C:690-693). Internal numeric form is the Julian day number — days since Jan 1, 4713 BC, computed as `c4ytoj(year) + dayOfYear + 1721425` where `c4ytoj(y) = (y-1)*365 + (y-1)/4 - (y-1)/100 + (y-1)/400` (proleptic Gregorian; D4DATE.C:346-361, 659-665; `JULIAN4ADJUSTMENT 1721425` = 0000/12/31, d4defs.h:2715).

### 6.4 `L` logical

1 byte. True = `'Y'`,`'y'`,`'T'`,`'t'`; anything else reads as false (F4TRUE.C:41-49). Blank = `' '` (space) via default `f4blank` path (F4FIELD.C:167-169).

### 6.5 `M` memo / `G` general / `X` binary memo

- compat 30 (VFP): 4-byte **little-endian signed 32-bit block number** into the .FPT file; `0` = no memo entry (`f4assignLong` writes `*(S4LONG*)ptr = lValue` when `f4len == 4`, F4LONG.C:93-114; append writes 0 for empty non-null memo, D4APPEND.C:790-794; block number read via `f4long`, f4memo.c:793).
- compat 25/26 and dBase builds: 10-byte right-justified ASCII block number, space-padded (`c4ltoa45(lValue, ptr, field->len)`, F4LONG.C:116; blank memo assigned `" "`, D4APPEND.C:796).
- Memo file extension is `.fpt` in the FOX build (`MEMO4EXT "fpt"`, d4defs.h:2592), `.dbt` otherwise (d4defs.h:2595). Memo file is opened iff header says so (§2.1; D4OPEN.C:2350-2361).

### 6.6 `B` double (FOX)

8-byte IEEE-754 double, little-endian, raw (`*((double*)ptr) = dValue`, F4DOUBLE.C:186-193). The descriptor's `dec` is display-only metadata (D4CREATE.C:1617). In the MDX build `'B'` is instead a 10-byte binary .DBT memo reference (d4defs.h:2774-2775, D4OPEN.C:2576-2583).

### 6.7 `H` binary float

4-byte IEEE-754 float, little-endian, raw (F4DOUBLE.C:234-253).

### 6.8 `I` integer, `P` unsigned int, `Q`/`R` short

`I`/`P`: 4-byte little-endian signed/unsigned 32-bit two's-complement, raw (`*((long*)ptr) = iValue`, F4INT.C:111-130). `Q`/`R` (`r5i2`/`r5ui2`): 2-byte little-endian, raw (`*((short*)ptr) = iValue`, F4INT.C:96-110). Blank = all zero bytes (F4FIELD.C:139-157).

### 6.9 `Y` currency

8 bytes = **64-bit little-endian two's-complement integer of (value × 10,000)** ("input source is longlong 10000 times greater than actual value (i.e. 4 decimal digits ...)", currency4add comment F4FIELD.C:464-466). In-memory type `CURRENCY4 { unsigned short lo[4]; }` — sign is the top bit of `lo[3]` (d4data.h:2972-2976, currency4compare F4FIELD.C:402-403). Descriptor always (len 8, dec 4) (D4CREATE.C:1570). String/double conversion goes through `c4atoCurrency`/`c4currencyToA` (used at F4FIELD.C:1648, 1696; `t4dblToCur` formats the double with 4 decimals then parses, i4conv.c:226-235). *(The bodies of `c4atoCurrency`/`c4currencyToA` are not present in this source drop — only declarations at d4declar.h:455-456 — so digit-level rounding behavior is [UNVERIFIED]; the ×10⁴ 64-bit LE layout above is verified.)*

### 6.10 `T` datetime / `7` datetime-with-millis

8 bytes = two consecutive little-endian signed 32-bit longs (f4dateTime reads `val = *(long*)fPtr` then `*(((long*)fPtr)+1)`, F4FIELD.C:1966-1998):

| Bytes | Content |
|-------|---------|
| 0-3 | Julian day number (same epoch/algorithm as `D`, §6.3; `date4long` of "YYYYMMDD", F4FIELD.C:2040) |
| 4-7 | Milliseconds since midnight = `hour*3600000 + min*60000 + sec*1000 (+ ms)` (`time4long`, i4conv.c:201-221) |

`T` ignores/rounds milliseconds on conversion to text (round up at ≥500ms, F4FIELD.C:1868-1873 — "FoxPro ignores this part"); `'7'` keeps them, rendering as `HH:MM:SS.mmm` (F4FIELD.C:1886-1890). Invalid date or time components are stored as 0 ("leave the field as blank, for Fox ODBC compatibility", F4FIELD.C:2044-2048). Blank = 8 zero bytes (F4FIELD.C:145-156). AutoTimestamp fills this format with UTC now (`date4timeNowFull`: `dtTime[0] = julian`, `dtTime[1] = ms`, D4DATE.C:788-806).

### 6.11 `W` wide string / `O` wide string with length

`W`: fixed-size UTF-16LE (2 bytes/char), length forced even at create (D4CREATE.C:1558-1559). Assign copies the raw UTF-16 bytes (F4WSTR.C:80-93). Blank = repeated UTF-16LE space `00 20`-pattern written byte-wise as `0x00,0x20` pairs (F4FIELD.C:98-107 — note the byte order there produces 0x2000; see Open Questions).
`O`: same, but the field is 2 bytes longer and the **last 2 bytes are a little-endian u16 character count** ("length of data in # characters at the end of the field ... extra bytes stored as NULLS", d4defs.h:2789-2794; `*lenPos = len / 2` at `ptr + len - 2`, F4WSTR.C:96-116).

### 6.12 `V` GUID (CodeBase meaning)

16 opaque binary bytes (LEN5GUID = 16, d4defs.h:1721); blank = zeros (F4FIELD.C:148-156).

### 6.13 `1`/`6` (i8/ui8), `2` DBDATE, `3` DBTIME, `4` DBTIMESTAMP, `5` automation date

OLE-DB support types, stored as the raw Windows structs, little-endian: `1`/`6` = 8-byte integer; `2` = `DBDATE {i16 year, u16 month, u16 day}` (6 bytes — blank = 1899-12-30, bytes `6b 07 0c 00 1e 00`, F4FIELD.C:109-125); `3` = DBTIME (6 bytes); `4` = DBTIMESTAMP (16 bytes); `5` = IEEE double OLE automation date (D4OPEN.C:2498-2506 length checks; create sizes D4CREATE.C:1580-1596).

### 6.14 Blank values summary (f4blank, F4FIELD.C:90-171)

- Zero-filled: `W`, `O` (fox: 0x00/0x20 pattern), `I`, `Z`(binary C), `X`, `Y`, `T`, `7`, `B`, `V`, `Q`, `R`, `P`, `1`, `6`, `3`, `4`, and any field with the binary flag.
- `2` (DBDATE): 1899-12-30.
- Everything else (`C`, `N`, `F`, `D`, `L`, 10-byte `M`/`G`): spaces.
- 4-byte `M`/`G` in VFP files: 0 (int) via the append path (D4APPEND.C:790-794).

---

## 7. Auto-increment and auto-timestamp (CodeBase extensions)

- Created by passing `nulls == r4autoIncrement` (195) or `r4autoTimestamp` (200) in `FIELD4INFO` (d4defs.h:1839-1841). Only one of each per table; autoIncrement must be type `'B'` double, autoTimestamp must be `'T'`/`'7'` (D4CREATE.C:876-894).
- The counter lives in the **file header** at offset 20 as a double, starting from `c4->autoIncrementStart` (D4CREATE.C:1402-1406). On each append the header value is incremented by 1 and assigned into the field as a double (`d4incrementAutoIncrementValue` / `d4assignAutoIncrementField`, D4APPEND.C:29-59); the header is rewritten with the +18-byte extended write (§2.5).
- Auto-timestamp fields get UTC now in `T` format on append (D4APPEND.C:64-85).
- Files with these features are version 0x31 + flags — **not** the VFP 8/9 native autoincrement (which uses descriptor bytes 19-23); a genuine VFP autoinc table opened read-write fails with `e4notSupported` unless read-only (D4OPEN.C:333-343).

---

## 8. Code page / language driver byte

Header byte 29 is written directly from `CODE4.codePage` (D4CREATE.C:1391) and read back on open (D4OPEN.C:2217). Values used by CodeBase (d4defs.h:1923-1933):

| Value | Constant | Meaning |
|-------|----------|---------|
| 0 | `cp0` | unmarked — treated as cp437/US OEM (indexes default this to Windows-ANSI general collation, i4init.c:387) |
| 1 (0x01) | `cp437` | DOS US |
| 2 (0x02) | `cp850` | DOS international |
| 3 (0x03) | `cp1252` | Windows ANSI |
| 4 (0x04) | `cp0004` | "unknown codepage 4" back-compat placeholder (d4defs.h:1930-1931) |
| -56 (0xC8) | `cp1250` | Windows Eastern European (d4defs.h:1932-1933) |

These byte values coincide with the standard xBase language-driver IDs for those code pages. `code4codePage`/`c4set.c` accepts exactly cp437/cp850/cp1252 for setting (c4set.c:734-739). The code page influences collation of index keys and upper-casing (e4functi.c:2888-2889) but the engine performs **no transcoding of record data**.

---

## 9. Large file (>4 GB) handling

- With `S4FILE_EXTENDED` defined, all file positions are `FILE4LONG`, a 64-bit union (`dLong` + `piece.longLo/longHi`) with macro arithmetic (d4data.h:420-456).
- Record positions are computed in 64 bits: `pos = (DWORDLONG)recWidth * (recNo-1) + headerLen` (d4declar.h:1992).
- On open the engine precomputes `numRecsBeforeFileLong = (ULONG_MAX - headerLen)/recordLen - 10` — the record count at which the file crosses 4 GB (D4OPEN.C:2234-2237). `numRecs` itself stays a signed 32-bit count; a computed record count with a nonzero high 32 bits is rejected (D4OPEN.C:2248-2249).
- `CODE4.largeFileOffset` (d4data.h:2377) relocates the lock bytes beyond 4 GB and, when nonzero, relaxes header `recordLen` verification (record widths that overflow the u16 `recordLen` are recomputed from the field descriptors and a stored `recordLen` of 0 tolerated) (D4OPEN.C:2226, 2665-2686; df4lock.c:123-142).

---

## 10. Divergences in other index-format builds (brief)

- **S4MDX (dBase IV):** version `0x8B` with .DBT memo (D4CREATE.C:1379); memo/gen fields always 10-byte text references (D4CREATE.C:1609-1610); `'B'` is a binary memo, counts as a memo field (D4OPEN.C:2576-2582); header year is `year-1900` (u4util.c:1023-1024); descriptor `hasTag` used for production MDX tags (d4data.h:2946); dBase 7 files (version 4, 48-byte descriptors, name[32]) are detected but rejected (`FIELD4IMAGE_MDX4`, d4data.h:2951-2967; D4OPEN.C:2221-2223, 2380-2384).
- **S4CLIPPER:** version `0x83` w/ memo via S4MNDX (D4CREATE.C:1376); numeric fields up to len 19/dec 15 (`F4MAX_NUMERIC/F4MAX_DECIMAL`, d4defs.h:2138-2139); no VFP field types; tags live in separate .NTX files tracked per-data-file (`LIST4 tagfiles`, d4data.h:3320-3322).

---

## 11. Open questions / risks for the C# port

1. **Currency string conversion bodies missing.** `c4atoCurrency`/`c4currencyToA` are declared (d4declar.h:455-456) but their implementation file is absent from this source drop; rounding/overflow semantics of text↔currency conversion are [UNVERIFIED]. The binary layout (int64 LE ×10⁴) is confirmed and sufficient for the port; implement conversions with decimal arithmetic and truncation-vs-rounding tests against VFP.
2. **`W` blank byte order.** `f4blank` writes the pair `0x00,0x20` per character (F4FIELD.C:100-106), i.e. UTF-16LE U+2000, not U+0020 space. This looks like an upstream bug; the port must decide whether to replicate it bit-for-bit (round-trip fidelity) or blank with U+0020.
3. **CodeBase-only types collide with VFP types.** `'W'` (CodeBase wide-string) is VFP9's Blob, `'P'` (CodeBase ui4) is VFP's Picture, `'Q'`/`'V'` differ from VFP9 varbinary/varchar (only loosely accepted when version = 0x32, D4OPEN.C:2479-2549). Files created with these CodeBase types are not VFP-portable; the port should gate them behind an "extensions" switch.
4. **Auto-increment storage is incompatible with VFP.** CodeBase keeps the counter in header bytes 20-27 with flag bytes 12-16 under version 0x31 (§2.2, §7); VFP keeps next/step per-field at descriptor bytes 19-23. A VFP-native autoinc table is only readable read-only (D4OPEN.C:333-343). Decide whether the C# port should additionally implement the native VFP scheme.
5. **DBC backlink unsupported.** The 263-byte area is always zeroed (D4CREATE.C:1697); tables that belong to a VFP database container will open, but the backlink is ignored and would be destroyed by a header-rewriting copy. Port should at minimum preserve those bytes on modify.
6. **Version 0x32 (VFP9) is read-tolerated, not fully supported.** Varchar/varbinary 'V'/'Q' length checks are skipped (D4OPEN.C:2482-2543) but no null-bit "full/partial" varchar semantics exist anywhere in the engine. Decide the port's VFP9 story explicitly.
7. **In-memory `version` vs on-disk 0x31.** The engine normalizes 0x31→0x30 in memory (D4OPEN.C:2152-2188) and deliberately never rewrites byte 0 during header updates (d4file.c:320-321). A port that naively serializes its in-memory header would silently downgrade 0x31 files to 0x30 — replicate the "skip byte 0" write window (offsets 1..9, extended to 1..27 for autoincrement updates).
8. **`numRecs` vs file length policy.** Exact-match on exclusive/deny-write open, +1 tolerance (then locked re-check) on shared open, and looser rules for compressed/large files (D4OPEN.C:2256-2303) — these recovery semantics matter for crash-compatibility and should be ported as-is.
9. **EOF byte asymmetry.** VFP-mode files carry no trailing 0x1A while FoxPro 2.x-mode files must maintain one on every append/unappend (§3.1). Interop tests should cover third-party files both with and without the marker in both modes.
10. **Character fields >255 bytes** use `dec` as the length high byte (§6.1); any C# descriptor model must treat (len,dec) as a union depending on type character, including for `W`/`O`/`Z`.
11. **Codepage byte is pass-through.** No transcoding is done by the engine (§8); the C# port must pick an explicit policy for string encoding (probably decode via the LDID→.NET Encoding map) while keeping the raw bytes stable for index compatibility.
12. **Field count limit.** `nFields` is a signed short with a comment claiming range 1-32768 (d4data.h:3204); the practical limit is `headerLen ≤ 65535` ⇒ ~2045 short-name fields. Enforce the header-length limit, not the comment.
