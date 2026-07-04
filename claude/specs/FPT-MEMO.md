# CodeBase Memo File Formats — FPT (FoxPro, primary) and DBT (dBase, secondary)

Implementation-grade specification for the C# port, derived from the CodeBase C sources in
`original/source`. Primary build configuration: **S4FOX** (Visual FoxPro compatible), which
implies the memo sub-format macro **S4MFOX** (d4defs.h:1061-1065). `S4MDX` implies `S4MMDX`
(d4defs.h:1072-1074) and `S4CLIPPER` implies `S4MNDX` (d4defs.h:1050-1053). Client/server
(S4CLIENT) paths are out of scope.

All byte offsets below are 0-based file/structure offsets. "BE" = big-endian, "LE" = little-endian.

---

## 1. Scope and source files

| Concern | Source |
|---|---|
| Memo file create + raw block dump | m4create.c (`memo4fileCreate` m4create.c:27, `memo4fileDump` m4create.c:342) |
| Memo file open, read, write, allocation | m4file.c (`memo4fileOpen` m4file.c:108, `memo4fileReadPart` m4file.c:161, `memo4fileWritePart` m4file.c:596, `memo4fileWrite` m4file.c:727) |
| Field-level memo API (F4MEMO cache) | f4memo.c (`f4memoRead` f4memo.c:433, `f4memoWrite` f4memo.c:770, `f4memoAssignN` f4memo.c:131) |
| Memo compaction, free-chain helpers, id validation | m4memo.c (`dfile4memoCompress` m4memo.c:193, `memo4fileChainFlush` m4memo.c:432, `d4validateMemoIds` m4memo.c:505) |
| Memo file integrity check (MDX/DBT only) | M4CHECK.C (`f4memoCheck` M4CHECK.C:31) |
| On-disk structures | d4data.h: `MEMO4FILE` (d4data.h:2855-2863), `MEMO4HEADER` (d4data.h:4258-4275), `MEMO4BLOCK` (d4data.h:4293-4304), `MEMO4CHAIN_ENTRY` (d4data.h:4281-4288), `F4MEMO` (d4data.h:3018-3040) |

Note: `m4map.c`, `m4memory.c`, `M4MEM2.C` are **memory management** modules, not memo-file code
(m4memory.c:15, M4MEM2.C header) — do not port them as part of the memo subsystem.

File extension: `"fpt"` under S4MFOX, `"dbt"` otherwise (d4defs.h:2589-2598).

## 2. Common architecture

Runtime handle:

```c
typedef struct {
   FILE4  file ;
   short  blockSize ;            /* # bytes in a memo block */
   DATA4FILE *data ;
   int    fileLock ;             /* true if memo file locked (multi-user builds) */
} MEMO4FILE ;                    /* (d4data.h:2855-2863) */
```

Memo addressing is **block-number based**: the DBF record stores a `memoId`; the physical byte
offset of the memo entry is `memoId * blockSize` (comment d4data.h:2851-2853; code
m4file.c:182-184, m4create.c:371-374). `memoId <= 0` means "no memo" and reads return length 0
(m4file.c:175-179).

---

## 3. FPT format (S4MFOX — Visual FoxPro compatible) — PRIMARY

### 3.1 File header

On-disk layout of `MEMO4HEADER` for S4MFOX (d4data.h:4258-4275, 8 bytes):

| Offset | Len | Type | Endianness | Field | Meaning |
|---|---|---|---|---|---|
| 0 | 4 | u32 | **BIG** | `nextBlock` | Block number of next free (end-of-data) block. Allocation pointer, NOT a free-list. |
| 4 | 2 | — | — | `unused` | Unused / 0 |
| 6 | 2 | u16 | **BIG** | `blockSize` | Bytes per block |

Big-endian proof: on little-endian machines both fields are byte-reversed before writing
(`x4reverseLong`/`x4reverseShort`, m4create.c:74-82) and reversed after reading
(m4file.c:140-142, m4file.c:522-525).

Creation (`memo4fileCreate`, m4create.c:27):
- `blockSize = c4->memSizeMemo` (m4create.c:68). Values < 33 are allowed, including 0 and 1
  (comment m4create.c:40-43). With E4MISC error checking, `memSizeMemo > 512*32` (16384) is
  rejected at create (m4create.c:50-52).
- Initial `nextBlock`:
  - if `blockSize > 512`: `nextBlock = 1` (stored as constant `0x01000000`, which on an LE
    machine is raw bytes `00 00 00 01` = big-endian 1) (m4create.c:69-70);
  - else `nextBlock = 512 / blockSize` (`B4BLOCK_SIZE_INTERNAL` = 512 for S4FOX,
    d4defs.h:2212-2214), byte-reversed (m4create.c:72-77). I.e. the header always owns the
    first 512 bytes of the file.
- After writing the 8-byte header, the file length is set to exactly **512 bytes**
  (m4create.c:216-220).

### 3.2 Memo block (entry) header

Each memo entry begins on a block boundary with an 8-byte `MEMO4BLOCK` (d4data.h:4293-4304):

| Offset | Len | Type | Endianness | Field | Meaning |
|---|---|---|---|---|---|
| 0 | 4 | u32 | **BIG** | `type` | 0 = picture, 1 = text, 2 = OLE object (comment d4data.h:4296-4297); 3 = compressed (CodeBase extension, d4data.h:4296, m4file.c:199) |
| 4 | 4 | u32 | **BIG** | `numChars` | Number of **data** bytes following this header |

Big-endian proof: written via `x4reverseLong` on LE machines (m4create.c:352-359); reversed on
read (m4file.c:189-192, m4memo.c:118-123).

IMPORTANT: the struct comment says numChars is "Including the 'MemoBlock'" (d4data.h:4298) but
the FOX code actually stores the **payload length only**: the writer sets
`numChars = memoLen` where `memoLen` is the user data length (m4create.c:355-358, called from
m4file.c:702 with the memo's byte count), and the block-count calculation *adds*
`sizeof(MEMO4BLOCK)` on top of `numChars` (m4file.c:469, m4file.c:495). Do not include the
8-byte header in `numChars`. (The "including" comment is only true for the DBT/MDX variant, see §4.2.)

CodeBase only ever writes `type = 1` (text) for normal memos — `memo4fileWrite` hardcodes
`mType = 1L` (m4file.c:730) and `f4memoWritePart` passes `1L` (f4memo.c:978) — or `type = 3`
for compressed entries (m4file.c:767). Types 0/2 are read transparently (type is just echoed
back to the caller, m4file.c:257) but never produced.

Data follows the header immediately at `memoId*blockSize + 8` (m4file.c:193,
m4create.c:427-428). Slack space in the final block is not explicitly zeroed; after a write
that grows the file, the file length is extended (if needed) so it always ends on a
`blockSize` boundary (m4create.c:444-472).

### 3.3 Block size semantics

- `blockSize` is a signed 16-bit quantity in memory (`short`, d4data.h:2858) read from header
  offset 6.
- **`blockSize == 0` is legal and means byte granularity**: writes treat it as an effective
  block size of 1 (m4file.c:620-631: "FoxPro now supports block size of '0' - which means '1'").
- Blocks required for a memo of `memoLen` bytes:
  `numBlocks = (memoLen + 8 + blockSize - 1) / blockSize` (m4file.c:459-470).
- Blocks owned by an existing entry are recomputed from its stored `numChars` the same way:
  `(numChars + 8 + blockSize - 1) / blockSize` (m4file.c:474-496).

### 3.4 Memo reference in the DBF record

Two encodings, selected by the field's descriptor length (`f4len(field)`):

1. **4-byte binary (VFP 3.0 / compatibility = 30, the primary target)**
   - The field occupies 4 bytes in the record and holds the `memoId` as a **little-endian
     signed 32-bit integer** read/written in native LE form
     (`*(S4LONG*)ptr`, F4LONG.C:280-301 for read; F4LONG.C:93-114 for write; byte reversal only
     under `S4BYTE_SWAP` big-endian builds).
   - Created with length 4 when `c4->compatibility == 30` (D4CREATE.C:915-931 for record
     length; D4CREATE.C:1599-1611 sets `createFieldImage.len = 4`).
   - Blank (no memo) = **four 0x00 bytes**: `f4blank` zero-fills fields flagged binary
     (F4FIELD.C:128-131); the memo write path calls `f4blank` when the new id is 0 with the
     comment "for FOX 3.0, must set to 0, else spaces" (f4memo.c:801-807).
   - The FIELD4 `binary` flag: `0x01` = r4memoBin/r4charBin binary content, `0x02` = 4-byte
     binary memo reference for r4memo/r4gen (d4data.h:2994).
2. **10-byte ASCII (FoxPro 2.x compatibility = 25/26, and all dBase variants)**
   - The field occupies 10 bytes holding the block number as a right-aligned ASCII decimal
     number; written with `c4ltoa45(value, ptr, 10)` (F4LONG.C:115-116) and parsed with
     `c4atol(ptr, 10)` (F4LONG.C:343). Blank = 10 spaces (`f4blank` space-fills non-binary
     fields, F4FIELD.C:128-131); `c4atol` of spaces yields 0 = no memo.
   - (`c4ltoa45` body is not present in this source drop — declared d4declar.h:1898; usage
     comments show negative width = zero-fill, positive = normal, e4functi.c:870-874.
     [UNVERIFIED detail: space- vs zero-padding of positive widths — verify against a real VFP
     file: VFP uses leading spaces.])

Field types that use memo references: `r4memo` = 'M', `r4gen` = 'G' (d4defs.h:2782-2785), and
`r4memoBin` = 'X' (d4defs.h:2809) which is stored in the DBF descriptor as type 'M' with
length 4 and the binary flag 0x04 (D4CREATE.C:1623-1626; nullBinary flag semantics
d4data.h:2941: 0x02 allows null, 0x04 binary). `r4memoBin`/`r4charBin` are only allowed when
compatibility == 30 (D4CREATE.C:940-945).

When assigning to a memo field of a 0x30-format table, the field's null bit is cleared
(`f4assignNotNull`, f4memo.c:670-673).

### 3.5 DBF header linkage

- Create, compatibility 30: DBF `version` byte = **0x30**, or **0x31** if the table uses
  autoIncrement / compressed memos / compressed data / autoTimestamp / long field names
  (D4CREATE.C:1341-1357). If any memo-class field exists, header byte 28 gets
  `hasMdxMemo |= 0x02` (D4CREATE.C:1358-1361; struct offset: `hasMdxMemo` is the byte after
  version(0), yy/mm/dd(1-3), numRecs(4-7), headerLen(8-9), recordLen(10-11), flags[8](12-19),
  autoIncrementVal(20-27) → offset 28; d4data.h:3062-3090).
- Create, compatibility 25/26: version = **0xF5** with memo, else 0x03 (D4CREATE.C:1363-1371).
- Open: `hasMemo = (version == 0x30) ? (hasMdxMemo & 0x02) : (version & 0x80)`; if set, the
  FPT with the same base name is opened (D4OPEN.C:2350-2362). Missing/unopenable memo file is
  a hard open failure (D4OPEN.C:2359-2360).
- CodeBase-proprietary header `flags[8]` at DBF offsets 12-19: `flags[1] == 1` marks "attached
  memo file contains potentially compressed entries" (d4data.h:3073-3081); set at create from
  `c4->compressedMemos` (D4CREATE.C:266-272), read at open into
  `d4->compressedMemosSupported` (D4OPEN.C:2193-2195). These bytes are reserved-zero in
  genuine VFP files.

The FPT is created by `d4create` right after the DBF when any memo field exists
(D4CREATE.C:2061-2078).

### 3.6 Read algorithm (memo4fileReadPart, m4file.c:161-293)

All reads go through `memo4fileReadPart`; the whole-memo read is the macro
`memo4fileRead(m4,id,pp,pl,tp) = memo4fileReadPart(m4,id,pp,pl,0,UINT_MAX-100,tp,1)`
(d4declar.h:2503).

```
read(memoId, offset, readMax) -> (data, len, type):
  if memoId <= 0: return len 0                              (m4file.c:175-179)
  pos = memoId * blockSize                                  (m4file.c:182-184)
  hdr = read 8 bytes at pos; type/numChars byte-reversed    (m4file.c:186-192)
  pos += 8                                                  (m4file.c:193)
  if type == 3 and uncompress:                              (m4file.c:199-212)
      if offset != 0: error (partial read unsupported on compressed)
      actualLength = read u32 LE at pos; pos += 4           (first 4 data bytes = uncompressed length)
      avail = actualLength; toRead = numChars - 4
  else:
      avail = numChars - offset; toRead = avail             (m4file.c:215-219)
  clamp avail/toRead to readMax                             (m4file.c:221-225)
  corruption guard: if avail > 0x7FFFFFF0
      or (raw read      and avail  > fileLength)
      or (uncompressing and toRead > fileLength): e4memoCorrupt   (m4file.c:229-238)
  (re)allocate caller buffer to avail+2 if needed (2 NUL bytes for UTF-16 termination)
                                                            (m4file.c:241-252)
  read toRead bytes at pos+offset
  if type == 3 and uncompress: inflate into output, len = actualLength (m4file.c:263-285)
```

At the field level, `f4memoReadLow` appends two 0x00 terminator bytes to the in-memory copy
(f4memo.c:578-582) and remembers `memoOffset` for change detection (f4memo.c:586-587).

### 3.7 Write / allocation algorithm — update-in-place vs append, no free chain

Architecture comment (m4file.c:24-32):

> keeps the 'eof' position available; when writing a memo, if a new entry writes at 'eof';
> if an existing entry and can still fit in current spot, uses current spot. If it can't, it
> uses eof. **S4FOX does not maintain a free chain of available memo blocks.**

`memo4fileWrite` (m4file.c:727-773) optionally compresses (§3.9) and delegates to
`memo4fileWritePart(f4memo, &memoId, ptr, memoLen, offset=0, lenWrite, type)`
(m4file.c:596-721):

```
writePart(memoId, ptr, memoLen, offset, lenWrite, type):
  effBlock = (blockSize == 0) ? 1 : blockSize               (m4file.c:620-631)
  if offset == 0:                                           (first/only chunk)
      if memoLen == 0: memoId = 0; return                   (m4file.c:653-657)  # empty memo -> blank ref
      need = (memoLen + 8 + effBlock - 1) / effBlock        (m4file.c:668, 459-470)
      have = (memoId > 0) ? blocksOfExisting(memoId) : 0    (m4file.c:673-682, 474-496)
      if have >= need and memoId != 0:
          keep memoId                                       # UPDATE IN PLACE (m4file.c:684-688)
      else:
          memoId = allocateAtEof(need)                      # APPEND (m4file.c:690-699)
      dump block header {type BE, numChars=memoLen BE} + lenWrite data bytes at memoId*blockSize
                                                            (m4file.c:702, m4create.c:342-475)
      if file grew: extend file length up to next blockSize multiple (m4create.c:444-472)
  else:                                                     (subsequent chunk of a partial write)
      raw write lenWrite bytes at memoId*effBlock + 8 + offset  (m4file.c:706-717)
```

`allocateAtEof(need)` = `memo4fileNextWritePosition` (m4file.c:557-592):

```
lock memo file if not already locked                        (m4file.c:510-517)
hdr = read 8-byte MEMO4HEADER at 0; nextBlock byte-reversed (m4file.c:519-525)
newId = hdr.nextBlock
hdr.nextBlock = newId + need
write header back (byte-reversed)                           (m4file.c:541-553, 578-580)
unlock if we locked it                                      (m4file.c:583-586)
return newId
```

Consequences the port must reproduce:
- **Freed blocks are never reused.** Shrinking or orphaning an entry leaves dead blocks; only
  `d4memoCompress` (§3.10) reclaims space by rewriting the file.
- The EOF pointer is the *header* `nextBlock`, not the physical file length — this permits
  concurrent writers to reserve disjoint ranges under the header lock.
- An overwrite that fits keeps the same `memoId`, so the DBF record needs no update; the
  caller updates the record field only when `newId != oldId` (f4memo.c:801-807).
- Writing an empty memo returns id 0 and the field is blanked; the old blocks are orphaned.

Field-level flow: `d4write` → for each memo field `f4memoUpdate` (writes only when
`isChanged`, f4memo.c:747-766) → `f4memoWrite` (f4memo.c:770-814) → then the record itself is
written (d4write.c:536-548). So memo data lands on disk before the record referencing it.

Partial/streaming API: `f4memoWritePart(field, buf, dataLen, memoLen, offset)`
(f4memo.c:918-1013) — first call must have `offset == 0` and the final total `memoLen` to
reserve space; later calls raw-write into the reserved range (usage rules m4file.c:598-612).
It locks the record and (at offset 0) the memo file (f4memo.c:954-964); the caller finishes
with `f4memoWriteFinish(data)` = `dfile4memoUnlock` (f4memo.c:906-914). `f4memoAssignFile` /
`f4memoFile` stream files in/out in 16 KB chunks over these primitives (f4memo.c:1017-1212).

### 3.8 F4MEMO in-memory cache

Per memo field, `F4MEMO` caches `contents/len/lenMax`, `status` (0 = current, 1 = must read
from disk), `isChanged`, plus `contentsOld` used to remove stale index keys built over memo
expressions (d4data.h:3018-3040). Empty memos point `contents` at a shared 2-byte
`f4memoNullChar` sentinel rather than allocating (f4memo.c:216-226). `f4memoLen`/`f4memoPtr`
lazily fault in the memo when `status == 1` (f4memo.c:253-258, 349-354).

In multi-user mode, before any memo read/write `d4validateMemoIds` re-reads the record from
disk and refreshes the memo-id bytes of unchanged fields in the record buffer, so ids observed
under lock are current (m4memo.c:505-547; called from f4memoRead f4memo.c:507-514 and
f4memoWrite f4memo.c:783-787).

### 3.9 Compressed memo entries (type = 3, CodeBase extension — NOT VFP compatible)

- Enabled per-CODE4 via `code4memoCompress(c4, 1)`; explicitly "incompatible with FoxPro"
  (m4memo.c:25-48). Requires the S4COMPRESS build; files are flagged via DBF `flags[1]`
  (§3.5).
- Write: only when the payload exceeds one block (`ptrLen > effectiveBlockSize`,
  m4file.c:743). Scratch buffer sized `ptrLen*1.001 + 1 + 12 + sizeof(long)` — the classic
  **zlib** worst-case bound plus a 4-byte length prefix (comment "extra for zlib...",
  m4file.c:746). `c4compress(c4, out, &outLen, in, inLen, level, 1)` produces the stored
  image; on success the entry is written with `type = 3` (m4file.c:760-772).
- Stored layout of a type-3 entry (derived from the reader, m4file.c:199-212, 263-285):

  | Offset in entry | Len | Content |
  |---|---|---|
  | 0 | 4 | type = 3 (u32 BE) |
  | 4 | 4 | numChars (u32 BE) = 4 + compressed stream length |
  | 8 | 4 | uncompressed length (u32, native LE) |
  | 12 | numChars-4 | compressed stream |

- Read: decompress via `c4uncompress` into a buffer of the uncompressed length
  (m4file.c:263-285). Partial reads of compressed entries are rejected
  (m4file.c:203-204, f4memo.c:853-858), as are `f4memoAssignFile`/`f4memoFile`
  (f4memo.c:1029-1035, 1147-1151).
- `c4compress`/`c4uncompress` bodies are not in this source drop (declared
  d4declar.h:2451-2452). Comments identify zlib (m4file.c:746); a QuickLZ build variant also
  exists (`S4COMPRESS_QUICKLZ`, d4data.h:2820-2823). [UNVERIFIED: exact stream format
  (raw deflate vs zlib-wrapped) — for the port, treat type-3 as optional/CodeBase-only and
  verify against a produced file if needed.]

### 3.10 Memo compaction — d4memoCompress

`d4memoCompress` (m4memo.c:130-189) → `dfile4memoCompress` (m4memo.c:193-426) reclaims dead
blocks: creates a temporary memo file with the same block size (m4memo.c:224-250), streams
every record, copies each live memo (chunked via `memo4fileReadPart` with `uncompress=0` so
compressed entries are copied verbatim with their type, m4memo.c:327-341), assigns the new ids
into the records, rewrites the records, then atomically replaces the old file via
`file4replace` (m4memo.c:389) and re-establishes the memo lock if it was held
(m4memo.c:396-404). Minimum resulting file length 512 is enforced (m4memo.c:413-421).

### 3.11 Locking (multi-user)

- The memo file has a single advisory write lock used to serialize header/allocation updates:
  S4FOX locks **1 byte at offset 0x40000000** (`L4LOCK_POS_OLD`, d4defs.h:2177-2179) —
  optionally offset by `c4->largeFileOffset` as the high dword — with infinite retry
  (`WAIT4EVER`, m4file.c:60-69). No byte-range lock is taken if the file is opened exclusive
  (`OPEN4DENY_RW`, m4file.c:57-58).
- After acquiring the lock, buffered file state is refreshed (`file4refresh`, m4file.c:72-74).
- The lock is acquired lazily inside `memo4fileGetFileHeader` when allocating
  (m4file.c:509-517) and released immediately afterwards *only if it was not already held*
  (m4file.c:583-586). "A memo file lock only lasts if the .dbf file is locked"
  (comment m4file.c:44); `d4unlock` releases it via `dfile4memoUnlock` (d4unlock.c:275-278,
  df4unlok.c:27).
- Record-level enforcement: assigning to a memo field requires a write lock on the record when
  `lockEnforce` is on (f4memo.c:156-162); reads acquire the record lock/refresh via
  `d4validateMemoIds` when read-locking is enabled (f4memo.c:507-514).

### 3.12 Corruption checks to replicate

| Check | Condition | Source |
|---|---|---|
| Read length sanity | requested `avail > 0x7FFFFFF0` or larger than physical file → `e4memoCorrupt` | m4file.c:229-238 |
| Length-part read failure | block-header read fails during compaction → `ULONG_MAX` → `e4memoCorrupt` | m4memo.c:103-124, m4memo.c:304-309 |
| Compaction progress | chunk position exceeds memo length → `e4memoCorrupt` | m4memo.c:317-322 |
| Streaming source shrank | file shorter than initially measured → `e4memoCorrupt` | f4memo.c:1107-1111 |
| `f4memoCheck` | **not supported** for FPT ("not supported for FoxPro FPT memo files or dBase III and Clipper", M4CHECK.C:20-23) — MDX/DBT only |

---

## 4. DBT format — dBase IV / S4MMDX (secondary)

### 4.1 File header (24+ bytes used; `MEMO4HEADER` non-FOX branch, d4data.h:4258-4275)

| Offset | Len | Type | Endianness | Field | Meaning |
|---|---|---|---|---|---|
| 0 | 4 | u32 | LE | `nextBlock` | Head of the free chain **and** EOF allocation pointer (see §4.3) |
| 4 | 4 | u32 | LE | `zero` | 0 (doubles as the free-chain "count" slot when the header is flushed as a chain node) |
| 8 | 8 | char[8] | — | `fileName` | Base name of the DBF, blank-padded, no extension (m4create.c:160-166) |
| 16 | 2 | u16 | LE | `zero2` | 0 |
| 18 | 2 | u16 | LE | `x102` | Constant **0x0102** (m4create.c:89; byte-swapped builds write 0x201, m4create.c:183) |
| 20 | 2 | u16 | LE | `blockSize` | Bytes per block |
| 22 | 2 | u16 | LE | `zero3` | 0 |

All values native LE (byte reversal only under `S4BYTE_SWAP`, m4create.c:179-187).
`nextBlock` starts at 1 (m4create.c:88). Block size must be a non-zero multiple of 512 —
`memSizeMemo` is rounded up (m4create.c:45-46); maximum 512*63 (m4create.c:56-58).

### 4.2 Block (entry) header — 8 bytes (`MEMO4BLOCK` non-FOX branch, d4data.h:4299-4303)

| Offset | Len | Type | Endianness | Field | Meaning |
|---|---|---|---|---|---|
| 0 | 2 | i16 | LE | `minusOne` | **-1** (0xFF 0xFF) validity marker |
| 2 | 2 | u16 | LE | `startPos` | 8 = offset of data from block start (m4create.c:362) |
| 4 | 4 | u32 | LE | `numChars` | data length **+ 8** (includes this header; m4create.c:363) |

Read: if `minusOne != -1` the entry is treated as empty/invalid (m4file.c:427-431); data
length returned = `numChars - 8` (m4file.c:437); data read from `memoId*blockSize + startPos`
(m4file.c:448-449).

### 4.3 Free chain — DBT DOES reuse freed blocks

Free areas are kept in an ascending, coalescing singly linked list. Each free area starts with
an 8-byte `MEMO4CHAIN_ENTRY` on disk: `next` (u32 LE, block number of next free area) and
`num` (u32 LE, number of blocks in this free area) (d4data.h:4281-4288; flushed as
`2*sizeof(S4LONG)` at `blockNo*blockSize`, m4memo.c:432-452). The list head is the file header
itself at block 0 (`nextBlock` = first free block; walking starts by reading block 0,
m4memo.c:456-499, M4CHECK.C:73-75). The chain terminates implicitly at EOF: reading a chain
node past the physical file returns 0 bytes → `next = num = -1` (m4memo.c:473-483); a chain
`next` of 0 is corruption (`e4memoCorrupt`, m4memo.c:493-496).

Write algorithm (`memo4fileWrite`, m4file.c:892-1111):

```
needed = (ptrLen + 8 + blockSize - 1) / blockSize                (m4file.c:943)
oldNum = blocks of old entry from its numChars (0 if none)       (m4file.c:911-940)
if oldNum >= needed and ptrLen > 0:
    memoId = oldBlockNo + oldNum - needed        # reuse TAIL of old area (m4file.c:945-947)
    dump entry at memoId                                          (m4file.c:948)
    if oldNum == needed: done                                     (m4file.c:952-953)
    remaining oldNum-needed leading blocks become a free area to insert below
lock memo file                                                    (m4file.c:961-966)
walk the free chain keeping (prev, cur):
    - insert the freed old area at its sorted position            (m4file.c:1006-1014)
    - coalesce adjacent free areas (prev.blockNo+prev.num == cur.blockNo)  (m4file.c:1018-1032)
    - if new data not yet placed: first free area with num >= needed is used,
      allocating from its TAIL (memoId = cur.blockNo + cur.num - needed)   (m4file.c:1061-1065)
      chain reaching EOF (next == -1) always fits (num = needed)           (m4file.c:1053-1058)
      on EOF write, pad file length to a blockSize multiple
      ("for dBASE IV compatibility")                                       (m4file.c:1066-1078)
    - flush modified chain nodes to disk as they are passed       (m4file.c:993-994, 1042-1043)
unlock (if this call locked)                                      (m4file.c:1044-1047)
```

Writing an empty memo (`ptrLen == 0`) sets the id to 0 and pushes the old area onto the free
chain (m4file.c:903-908 plus the chain walk).

Locking: 2 bytes at `L4LOCK_POS - 1` = 0xEFFFFFFE (m4file.c:63-64; `L4LOCK_POS` = 0xEFFFFFFF
for S4MDX with negative locks, d4defs.h:2181-2188).

Sanity check on dump: writing more than 4 blocks past EOF ⇒ `e4memoCorrupt`
(m4create.c:384-395). `f4memoCheck` (MDX builds only) bitmap-verifies that record references
and the free chain exactly tile the file with no overlap (M4CHECK.C:31-97).

DBF version byte with memo: **0x8B** (D4CREATE.C:1378-1379). Record reference: 10-byte ASCII
only (§3.4 option 2; the 4-byte form is FOX-specific, D4CREATE.C:922-930).

## 5. DBT format — dBase III / Clipper / S4MNDX (brief)

- Fixed block size **512** (`MEMO4SIZE` = 0x200, d4defs.h:1076-1077; m4create.c:84-85,
  m4file.c:133-134/143-144). Header: only `nextBlock` (u32 LE) at offset 0, initial value 1
  (m4create.c:86, d4data.h:4260-4266 — the S4MNDX struct has no blockSize field).
- No per-entry header. Entry = raw bytes terminated by **a single 0x1A byte**
  (m4create.c:347-348, 397-402). Note: dBase III historically wrote two 0x1A bytes; CodeBase
  writes one and stops reading at the first (m4file.c:354-384). Reads scan 512-byte chunks
  until 0x1A (m4file.c:341-407) — an entry may not contain 0x1A (binary-unsafe).
- Update in place if the old entry's chunk-rounded extent can hold the new data
  (m4file.c:805-831: fits only if `readSize > ptrLen`); otherwise append at header
  `nextBlock`, advancing it by `(ptrLen + 511) / 512` blocks (m4file.c:801, 833-883).
  **No free chain** (m4file.c:42-43) and **no memo-file locking under Clipper**
  ("clipper version has no free chain--do not lock memo file", m4file.c:42-44; chain/lock code
  compiled out via `!S4CLIPPER`).
- DBF version byte with memo: **0x83** (D4CREATE.C:1374-1376).

## 6. Behavioral checklist for the C# port (FPT primary)

1. Header `nextBlock`/`blockSize` and entry `type`/`numChars` are **big-endian**; everything
   in the DBF record and DBT variant is little-endian.
2. `numChars` in FPT = payload only; in DBT(IV) = payload + 8.
3. Allocation pointer lives in the FPT header, not the file length; always update it under the
   0x40000000 byte lock; extend the file to a blockSize multiple after appends.
4. Overwrite in place when the existing entry's block span suffices; never shrink `nextBlock`;
   never reuse orphaned FPT blocks (compaction is a separate whole-file rewrite).
5. Empty memo ⇒ reference 0 / blanked field (zeros for 4-byte refs, spaces for 10-byte).
6. blockSize 0 ⇒ granularity 1; accept any blockSize ≥ 0 on open (`assert blockSize != 0`
   only, m4file.c:150); cap at 16384 on create with error checking enabled.
7. Reads must validate lengths against physical file size (`e4memoCorrupt`).
8. Memo write happens before the DBF record write in `d4write` (d4write.c:536-548).
9. In shared mode, refresh memo ids from disk (`d4validateMemoIds`) before reading/writing.

## 7. Open questions / risks for the C# port

1. **`c4ltoa45` exact padding** for 10-byte ASCII memo references is not in this source drop
   (only the declaration, d4declar.h:1898). VFP writes right-aligned space-padded numbers;
   verify against a FoxPro 2.x-format file before implementing the 10-byte path. (The primary
   4-byte VFP path is fully verified.)
2. **Default `memSizeMemo`** (FPT block size when the user does not set one) is initialized in
   `code4init`, whose implementation is not in this drop. [UNVERIFIED — VFP's default is 64;
   CodeBase docs historically say 512. Decide and document a default; any multiple works since
   blockSize is read from the header at open.]
3. **Compressed memos (type 3)**: `c4compress` body missing; stream format (zlib wrapper vs raw
   deflate, QuickLZ variant) unverified. Since the feature is explicitly FoxPro-incompatible
   (m4memo.c:31-33), recommend the port reads type 3 as an error or optional extension, and
   never writes it by default.
4. **Orphaned-block growth**: faithful porting means FPT files grow monotonically under heavy
   memo churn until `d4memoCompress` is run. Decide whether the port exposes an equivalent
   compaction API (it should, for parity).
5. **MNDX terminator edge case**: the 0x1A terminator is written at `pos + len`
   (m4create.c:397-402) but the block reservation `(ptrLen + 511)/512` (m4file.c:801) does not
   account for it, so a memo whose length is an exact multiple of 512 places its terminator in
   the next, unreserved block. Replicating vs fixing this is a compatibility decision (only
   affects the Clipper/dBase III variant).
6. **Concurrent update-in-place**: FPT in-place overwrites of an entry happen without holding
   the memo header lock (only the allocation path locks, m4file.c:684-702); CodeBase relies on
   the record write-lock for exclusion. The C# port must hold the record lock across
   `f4memoWrite` to be safe, as CodeBase does (f4memo.c:156-162, d4write path).
7. **`type` values 0/2** (picture/OLE) are pass-through in CodeBase; the port should preserve
   unknown type codes on copy/compaction rather than validating them.
8. **Large files**: block numbers are signed 32-bit longs; max addressable FPT size is
   `2^31 * blockSize` bytes but ids > 2^31-1 are unrepresentable — same limit as VFP (2 GB with
   blockSize small). `largeFileOffset` shifts the lock byte for >4 GB configurations
   (m4file.c:68); the port can restrict to 2 GB memo files for VFP parity.
