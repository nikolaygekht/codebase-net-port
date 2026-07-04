# CDX (Visual FoxPro Compound Index) File Format — CodeBase S4FOX Implementation Spec

Definitive specification of the `.CDX` compound-index format as implemented by the CodeBase C
library (Sequiter) compiled with `S4FOX`, for the C# port. Every claim cites the C source file
and line where it was verified. S4CLIENT paths are ignored (stand-alone port). S4CLIPPER/S4MDX
divergences are noted only briefly at the end.

All multi-byte integers are **little-endian** unless explicitly stated otherwise. Three fields
are deliberately stored **big-endian** ("non-Intel"): the tag-header `version` counter, and the
two 32-bit values (record number, child node) inside interior-node entries — see §5.2 and §7.

---

## 1. Core constants

| Constant | Value | Source |
|---|---|---|
| `B4BLOCK_SIZE_INTERNAL` (default block/node size) | 512 | (d4defs.h:2213) |
| `I4MULTIPLY` (node-number → byte-offset multiplier) | 1 | (d4defs.h:2126) |
| `I4MAX_KEY_SIZE` / `I4MAX_KEY_SIZE_COMPATIBLE` | 240 / 240 | (d4defs.h:2128-2129) |
| `I4MAX_EXPR_SIZE` (max expression/filter text incl. NUL) | 255 | (d4defs.h:2202) |
| `LEN4TAG_ALIAS` (tag name length) | 10 | (d4defs.h:1696) |
| `LEN4HEADER_WR` (bytes of tag header rewritten on update) | 0x10 (16) | (d4defs.h:1750) |
| `LEN4HEADER_WR_TAG_INDEX` (bytes of file header rewritten) | 0x1C (28) | (d4defs.h:1749) |
| `INVALID4BLOCK_ID` (invalid node value) | `ULONG_MAX` = 0xFFFFFFFF | (d4defs.h:2634), `b4invalidNode()` (d4data.h:3665) |
| "unknown node" sentinel (in-memory checks only) | 0xFFFFFFFE (`INVALID4BLOCK_ID - 1`) | (d4data.h:3706) |
| `L4LOCK_POS` (index lock byte) | 0x7FFFFFFE | (d4defs.h:2179) |

**Node addressing.** A `B4NODE` is a 32-bit unsigned node number; physical byte offset =
`node * multiplier` (`b4nodeGetFilePosition`, B4NODE.C:24-29). With the default multiplier of 1,
node numbers **are** byte offsets, always multiples of the 512-byte block size — this is the
standard VFP convention. Adding one block to a node adds `blockSize / multiplier`
(`b4nodeAddBlocks`, B4NODE.C:56-59). A node value of 0xFFFFFFFF means "none/invalid"; 0 is never
a valid block reference for tree children (block 0 is the file header) and is treated as
corruption if seen as a child pointer (I4TAG.C:765-768; split target of node 0 refused at
I4TAG.C:3095-3100).

**CodeBase big-block extension.** CodeBase can create non-VFP-standard files with a larger
`blockSize` (multiple of 512) and a `multiplier` (≤1024, must divide blockSize), recorded in the
file header when `codeBaseNote == 0xABCD` (d4data.h:3908-3912; i4create.c:856-868; read side
i4init.c:536-548). For VFP compatibility the port should always write blockSize=512,
multiplier=1 and omit the note, but must **honor** it when reading.

---

## 2. Overall file structure

A compound `.CDX` is itself a collection of B-trees, one per tag, **plus one hidden B-tree (the
"tag index" / tag directory) whose keys are the tag names**. The tag directory's header occupies
the first 1024 bytes of the file:

```
offset 0        : tag-directory (tagIndex) header, 1024 bytes (2 × 512 nodes)
offset 1024+    : for each tag i (i = 1..N): tag header at byte 2*512*i … +1024
then            : tree blocks (leaves & branches) of all tags, 512 bytes each, any order
```

- The tag directory is "a tag in the index file" (`INDEX4FILE.tagIndex`, d4data.h:4161).
- On create/reindex, tag headers are written at `iTag * B4BLOCK_SIZE_INTERNAL` with `iTag`
  starting at 2 and stepping by 2, i.e. 1024-byte slots (r4reinde.c:1840-1851,1911).
- The tag-directory keys are built as: tag alias (upper-cased, trimmed — i4create.c:919-920)
  space-padded to exactly 10 bytes (r4reinde.c:382-384), with the **"record number" of the
  directory entry = node number of that tag's header block**:
  `node = 2 * 512 * tagIndex / multiplier` (r4reinde.c:385-389).
- Directory properties: `keyLen = 10`, `typeCode = 0xE0`, `exprLen = 1`, `filterLen = 1`,
  `filterPos = 1`, `exprPos = 0`, `signature = 0x01`, machine collation
  (i4create.c:845-853, 878).
- On open: read the tag-directory header at offset 0 (`tfile4init` with filePos 0,
  i4index.c:1681-1700); if `typeCode >= 64` the file is compound — walk the directory tag
  from top, and for each entry create a tag whose header position is
  `b4recNo(directoryLeaf, keyOn)` and whose name is the 10-byte key value
  (i4index.c:1760-1812). If `typeCode < 64` the file is a single-tag `.idx`-style file
  (i4index.c:1814-1825).
- Open-time sanity check: root ≠ 0, root ≠ 0xFFFFFFFF, `typeCode >= 32` (i4index.c:1706).
- Adding a tag to an existing file appends the key blocks, writes the 1024-byte tag header
  at the (pre-extension) EOF, then inserts `(paddedName, headerNode)` into the tag directory
  with `tfile4add(i4file->tagIndex, tagName, b4node(tagFile->headerOffset), …)`
  (i4add.c:1014-1035).

**EOF tracking:** there is no EOF field in the file; `INDEX4FILE.eof` is derived from the
physical file length at open (`b4nodeSetFromFilePosition(i4, &i4->eof, file4lenLow(...))`,
i4index.c:1703) and grows by one block when extending (i4index.c:906-911).

---

## 3. Tag header block (also the file header, for the tag directory)

In-memory struct `T4HEADER` (d4data.h:3892-3926). The **on-disk layout** is defined by the
read code (`tfile4init`, i4init.c:327-366) and the write code (r4reinde.c:1793-1911;
i4index.c:2128-2156). All offsets relative to the start of the tag's header node (offset 0 of
the file for the tag directory):

| Offset | Len | Type | Field | Notes |
|---|---|---|---|---|
| 0x000 | 4 | u32 LE | `root` | node # of root block. 0xFFFFFFFF = unknown (d4data.h:3895) |
| 0x004 | 4 | u32 LE | `freeList` | head of free-block list; 0 = none (d4data.h:3897). Only meaningful in the tag-directory header (see §9) |
| 0x008 | 4 | u32 **BE** | `version` | multi-user change counter, stored byte-reversed (i4init.c:365; r4reinde.c:1811-1813; i4index.c:2139-2152) |
| 0x00C | 2 | u16 LE | `keyLen` | key length in bytes (d4data.h:3899) |
| 0x00E | 1 | u8 | `typeCode` | option bits, see §3.1 (d4data.h:3901) |
| 0x00F | 1 | u8 | `signature` | in-memory value set to 0x01 by CodeBase; declared "unused" (d4data.h:3902; i4create.c:853,922). **On disk this byte varies**: EXAMPLE.CDX tag-directory header has 0x01, but DATA1.CDX tag-directory header has 0x00 (confirmed EXAMPLE.CDX byte 15 = 0x01, DATA1.CDX byte 15 = 0x00). Treat as unused — do not rely on the value when reading. |
| 0x010 | 4 | u32 LE | `codeBaseNote` | 0xABCD ⇒ blockSize/multiplier fields valid (d4data.h:3908-3910) |
| 0x014 | 4 | u32 LE | `blockSize` | CodeBase-only, multiple of 512 (d4data.h:3911) |
| 0x018 | 4 | u32 LE | `multiplier` | CodeBase-only (d4data.h:3912) |
| 0x01C | 466 | — | reserved | zero-filled (r4reinde.c:1820 writes 466 zeros for the tag directory; for regular tags 478 zeros follow the first 16 bytes, r4reinde.c:1881 — same resulting offset 494) |
| 0x1EE | 8 | char[8] | `sortSeq` | collating sequence name, e.g. `""` (machine), `"GENERAL"`, `"CBnnnnn"` (i4init.c:342-345, 372-418; r4reinde.c:1823,1884) |
| 0x1F6 | 2 | u16 LE | `descending` | 1 = descending, 0 = ascending (d4data.h:3919) |
| 0x1F8 | 2 | u16 LE | `filterPos` | "not used, == exprLen" (d4data.h:3920); CodeBase writes `filterPos = exprLen` (i4create.c:1076) |
| 0x1FA | 2 | u16 LE | `filterLen` | length of filter text incl. NUL; minimum 1 (i4create.c:1066-1071) |
| 0x1FC | 2 | u16 LE | `exprPos` | "not used, == 0" (d4data.h:3922) |
| 0x1FE | 2 | u16 LE | `exprLen` | length of expression text incl. NUL (i4create.c:971) |
| 0x200 | exprLen | char | expression text | NUL-terminated; "expression starts at a 512 byte offset" (i4init.c:427-431; r4reinde.c:1896-1898) |
| 0x200+exprLen | filterLen | char | filter text | only if `typeCode & 0x08` (i4init.c:494-500; r4reinde.c:1902-1906) |
| … | pad | — | zeros to 0x400 | header block is 1024 bytes total (r4reinde.c:1908, pad = `512 - exprLen - filterLen`) |

The 10 bytes at 0x1F6..0x1FF are read/written as one group of 5 shorts starting at
`topSize(28) + 474 = 502` (i4init.c:348-351; r4reinde.c:1788, 1828-1831).

For the tag directory the expression area is written as 512 zero bytes ("no expression",
r4reinde.c:1835) even though its `exprLen`/`filterLen` are 1.

**Partial header rewrites during normal operation:**
- Root pointer: when `rootWrite` is set, exactly 4 bytes (`header.root`) are written at the
  header offset (I4TAG.C:3405-3431).
- File header (tag directory): when `version` changed, the first `LEN4HEADER_WR` = 16 bytes are
  rewritten at offset 0 with `version` byte-reversed (i4index.c:2132-2156). (The 16-byte write
  covers root, freeList, version, keyLen, typeCode, signature.)
- Version check re-reads the first 16 bytes of the file (i4index.c:2409-2426).
- Quirk: on the first key added to a filtered tag, one NUL byte is written at
  `headerOffset + sizeof(T4HEADER) + 222` (an MDX-heritage "hasKeys" byte landing in the
  reserved area under S4FOX) (i4addtag.c:692-702).

### 3.1 `typeCode` bits

Comment enumeration: "0x01 Uniq; 0x02 Null; 0x04 r4candidate; 0x08 For Clause; 0x32 Compact;
0x80 Compound" (d4data.h:3901). Actual values set by the code:

| Bit | Meaning | Source |
|---|---|---|
| 0x01 | unique tag | (i4create.c:924-931) |
| 0x02 | nullable key expression (VFP null support) | (i4create.c:965-968) |
| 0x04 | candidate key (`r4candidate`/`e4candidate`) — set *instead of* 0x01 | (i4create.c:926-928) |
| 0x08 | FOR clause (filter) present | (i4create.c:1052-1056) |
| 0x20+0x40 (=0x60) | base value for every normal tag ("compact") | (i4create.c:923) |
| 0xE0 | tag-directory header ("compound, compact") | (i4create.c:847) |

Tests used by the code: `typeCode >= 64 (0x40)` ⇒ compound file (i4index.c:1760, 1068);
`typeCode < 32 (0x20)` ⇒ invalid header (i4index.c:1706); `typeCode & 0x05` ⇒ uniqueness
enforced (r4reinde.c:2049). `descending` is **not** a typeCode bit; it is the separate short at
0x1F6 (i4create.c:945-947).

---

## 4. Tree block common header (`B4STD_HEADER`)

Every 512-byte tree block (branch and leaf) begins with (d4data.h:3711-3717):

| Offset | Len | Type | Field | Notes |
|---|---|---|---|---|
| 0x00 | 2 | u16 LE | `nodeAttribute` | 0 = interior ("index"), 1 = root (interior), 2 = leaf, 3 = root+leaf (d4data.h:3713; i4addtag.c:742; I4TAG.C:1851-1852; r4reinde.c:2230,2433) |
| 0x02 | 2 | u16 LE | `nKeys` | number of keys in this block |
| 0x04 | 4 | u32 LE | `leftNode` | left sibling node #, 0xFFFFFFFF if none (d4data.h:3715) |
| 0x08 | 4 | u32 LE | `rightNode` | right sibling node #, 0xFFFFFFFF if none (d4data.h:3716) |

Leaf test is a **bit test**, not `>= 2`: `nodeAttribute & 0x02` (Fox 8.0 files may set extra
bits; a value of 5 is *not* a leaf) (b4block.c:2003-2014, 1966-1968). The whole 512-byte block
is read/written in one I/O at `node * multiplier` (i4readBlock I4TAG.C:182-185; b4doFlush
b4block.c:1750-1830). A safety check refuses to write a block at file offset < 1024
(b4block.c:1824-1829).

---

## 5. Interior (branch) node layout

### 5.1 Layout

Immediately after the 12-byte `B4STD_HEADER` follows a packed array of `nKeys` fixed-size
entries with **no additional header and no padding**; entry size `gLen = keyLen + 8`
(`2*sizeof(S4LONG)`) (b4block.c:1477, 1597; b4seek uses `(char*)&b4->nodeHdr + keyCur*groupVal`,
i.e. entries start at offset 12, b4block.c:2145-2150):

| Offset in entry | Len | Field | Endianness |
|---|---|---|---|
| 0 | keyLen | full key value (uncompressed, trailing pad chars included) | — |
| keyLen | 4 | record number of the entry's key | **big-endian** |
| keyLen+4 | 4 | child node number | **big-endian** |

Evidence: `b4recNo` for a branch reads `entry + keyLen` and byte-reverses it
(d4declar.h:1727-1729); descending to a child reads `entry + keyLen + 4` byte-reversed
(`tfile4down`, I4TAG.C:732-733); `b4key(...)->num` for a branch is the child pointer at
`(iKey+1)*(8+keyLen) - 4` (b4block.c:1941-1944); `b4brReplace` writes key then a zeroed long
then the byte-reversed record number at `+keyLen` (b4block.c:2054-2062).

Maximum entries per block: `keysMax = (blockSize - 12) / (keyLen + 8)` (r4reinde.c:2030).
Unused space after the last entry is zero-filled (b4block.c:2916-2917, r4reinde.c:2016).

### 5.2 Entry semantics

Each entry represents one child block; the key/recno are **the last (greatest) key of that
child** and its record number. This is maintained by the split code: after adding a child's
last key changes, the parent entry is replaced via `b4brReplace` (i4addtag.c:884-906), and new
parent entries are built from `b4keyKey(child, lastKey)` / `b4recNo(child, lastKey)` /
`b4node(child->fileBlock)` (i4addtag.c:837-863).

Branch insert (`b4insertBranch`, b4block.c:1595-1656) pseudocode:

```
gLen = keyLen + 8
if blockSize - 12 - gLen*nKeys < gLen:            # full
    try b4insertBranchBalance (only when nKeys==2); else return 1 (split needed)
dst = base + keyOn*gLen
memmove(dst+gLen, dst, gLen*(nKeys-keyOn))        # shift right; old entry at keyOn now at keyOn+1
nKeys++
dst[0..keyLen)         = key
dst[keyLen..keyLen+4)  = bigEndian(recNo)         # parameter rin2
if newFlag or keyOn+1 == nKeys:
    dst[keyLen+4..keyLen+8) = bigEndian(node)     # parameter r1
else:
    (dst+gLen)[keyLen+4..keyLen+8) = bigEndian(node)
    # the shifted-right old entry now points at the NEW (right) block, while the
    # freshly inserted entry inherits the stale child pointer (the old/left block)
    # left behind by the memmove.
```
(b4block.c:1623-1651; call sites pass r1 = new right block node, rin2 = left block's last-key
recno, i4addtag.c:771, 813, 818, 840-844.)

### 5.3 Branch binary search

`b4seek` on a branch does binary search with `u4memcmp` over the first `len` bytes of each key;
result positions `keyOn` at the first entry ≥ search value, returning 0 (exact) or `r4after`
(b4block.c:2123-2168). Descent then follows that entry's child pointer (`tfile4down`,
I4TAG.C:725-733).

---

## 6. Leaf node layout — bit-packed compression

### 6.1 `B4NODE_HEADER` (bytes 0x0C–0x17 of a leaf block)

(d4data.h:3674-3684):

| Offset | Len | Type | Field | Meaning |
|---|---|---|---|---|
| 0x0C | 2 | i16 LE | `freeSpace` | free bytes between the info array and the key-text heap |
| 0x0E | 4 | u32 LE | `recNumMask` | mask with low `recNumLen` bits set (`0xFFFFFFFF >> (32-recNumLen)`, b4block.c:699-701) |
| 0x12 | 1 | u8 | `dupByteCnt` | mask for extracting dup count = `(1<<dupCntLen)-1` (low 8 bits only; see §6.5 for keyLen>255) |
| 0x13 | 1 | u8 | `trailByteCnt` | mask for extracting trail count (same construction) |
| 0x14 | 1 | u8 | `recNumLen` | bits used for record number |
| 0x15 | 1 | u8 | `dupCntLen` | bits used for duplicate(-prefix) count |
| 0x16 | 1 | u8 | `trailCntLen` | bits used for trailing(-pad) count |
| 0x17 | 1 | u8 | `infoLen` | bytes per packed entry = `(recNumLen+dupCntLen+trailCntLen)/8` (b4block.c:692) |

After the header, at offset 0x18, lies the **info array**: `nKeys` packed entries of `infoLen`
bytes each (`b4->data`, d4data.h:3748; entry i at `data + i*infoLen`, d4declar.h:1826). The
**key text heap** grows from the end of the block (offset `blockSize`) backwards toward the
info array (b4block.c:1148, r4reindex `curPos = block + blockSize`, r4reinde.c:2153). The gap
between them is `freeSpace`:
`freeSpace = blockSize - 12 - 12 - nKeys*infoLen - totalKeyTextBytes` (initial value
`blockSize - sizeof(B4STD_HEADER) - sizeof(B4NODE_HEADER)` = 488 for 512-byte blocks,
b4block.c:703; maintained at b4block.c:1422, 2851; recomputed after split I4TAG.C:1853-1856).

### 6.2 Packed entry bit layout

Each entry packs `<record#><dupCnt><trailCnt>` LSB-first into `infoLen` bytes interpreted as a
little-endian integer:

- bits `[0, recNumLen)` — record number
- bits `[recNumLen, recNumLen+dupCntLen)` — duplicate count (number of leading key bytes shared
  with the *previous* key in the block)
- bits `[recNumLen+dupCntLen, recNumLen+dupCntLen+trailCntLen)` — trail count (number of
  trailing pad characters)

The source documents it with an example: "say the recNumLen = 14, trail = 5, dup = 5 (total
3 bytes). Actual bits: `RRRR RRRR DDRR RRRR TTTT TDDD`" — i.e. byte0 = recno bits 0-7, byte1 =
recno bits 8-13 then dup bits 0-1, byte2 = dup bits 2-4 then trail bits 0-4 (b4block.c:889-897).

Write algorithm (`x4putInfo`, b4block.c:899-1000):
```
buf[0..5] = 0
*(u32*)buf = rec & recNumMask
if infoLen > 4:                    # only possible when recNumLen > 16
    p = (u32*)(buf + 2); pos = recNumLen - 16
else:
    p = (u32*)buf;       pos = recNumLen
*p |= dupCnt  << pos
pos += dupCntLen
*p |= trail   << pos
copy first infoLen bytes of buf into the entry slot
```
Read algorithms are the mirror image (macros `x4recNo` d4declar.h:1826, `x4dupCnt`
d4declar.h:1807-1809, `x4trailCnt` d4declar.h:1847-1854; out-of-line versions
b4block.c:729-883). Note the `infoLen > 4` case shifts the base pointer by **2 bytes** and
reduces the shift by 16; `recNumLen` is guaranteed > 16 in that case (b4block.c:779-781).

### 6.3 Key text heap & key reconstruction

For each entry only `keyLen - dupCnt - trailCnt` "significant" bytes are stored. Key texts are
laid out from the block end going down: entry 0's bytes occupy the *highest* addresses
(`blockSize - len0` … `blockSize`), entry 1's bytes immediately below, etc.
(b4block.c:1148-1176; r4reinde.c:2201-2203).

Reconstruction while scanning forward (`b4key`, b4block.c:1883-1947):
```
pos = blockAddr + blockSize          # b4->builtPos start (b4block.c:1907)
key = buffer[keyLen]
for i = 0 .. target:
    dup   = x4dupCnt(i) ;  trail = x4trailCnt(i)
    len   = keyLen - dup - trail     # if < 0 → index corrupt (b4block.c:1916-1924)
    pos  -= len
    key[dup .. dup+len)      = bytes at pos          # keep first `dup` bytes from prev key
    key[keyLen-trail .. keyLen) = padChar             # tag->pChar
recno = x4recNo(target)
```
`pChar` is `' '` (0x20) for machine-collated character keys and `'\0'` for numeric/date/
currency/collated keys (i4init.c:557-602; tag directory: `' '`, i4init.c:520-525).

Rules of the format (invariants documented in the source, b4block.c:1007-1016, 2486-2492):
- trailing pad bytes are **always** counted in `trailCnt`, never carried as duplicates: a blank
  key has dup=0, trail=keyLen even when the previous key is also blank;
- `dupCnt` never includes bytes that are trailing pad of the current key
  (`dupCnt + trailCnt ≤ keyLen`, and dup counts only "real" data actually present).

### 6.4 Choosing the bit widths (`b4leafInit`, b4block.c:644-707)

```
cLen = bitcount(keyLen)              # number of iterations of keyLen >>= 1 until 0
trailCntLen = dupCntLen = cLen
c = cLen ; while c > 8: c -= 8       # low 3 bits worth for >255 key support
stored trailByteCnt = dupByteCnt = 0xFF >> (8 - c)     # (b4block.c:663-667)
# runtime 16-bit masks when keyLen > 255:
#   trailMask = (storedTrailByteCnt << 8) + 255   (b4block.c:668-677; re-derived on read,
#                                                  I4TAG.C:234-248)
rLen = bitcount(recordCountOfDBF)    # dfile4recCount (b4block.c:679-681)
recNumLen = rLen + ((8 - (2*trailCntLen % 8)) % 8)     # (b4block.c:683)
recNumLen = max(recNumLen, 12)                          # (b4block.c:684-685)
while (recNumLen + dupCntLen + trailCntLen) % 8 != 0: recNumLen++   # (b4block.c:687-690)
infoLen   = (recNumLen + dupCntLen + trailCntLen) / 8   # (b4block.c:692)
recNumLen = min(recNumLen, 32)                          # (b4block.c:696-697)
recNumMask = 0xFFFFFFFF >> (32 - recNumLen)             # (b4block.c:699-701)
```
The bulk-build path computes the same widths, except `recNumLen = bitcount(recCount)` without
the pre-alignment term before the max(…,12)/padding steps (r4reinde.c:1969-2011), and for the
tag directory it uses `recCount = nTags * 1024` (r4reinde.c:1983-1984).

Example (keyLen=10, 1000 records): cLen=4 ⇒ dup/trail masks 0x0F; rLen=10 → recNumLen=10+
((8-8%8)%8)=10→ min 12 → pad 12+4+4=20 → 24 bits ⇒ recNumLen=16, infoLen=3.

**Widening on demand (`b4reindex`, b4block.c:1675-1723).** When inserting a record number that
doesn't fit `recNumMask` (checked at b4block.c:1057-1088), the block is rewritten in place with
`recNumLen += 8`, `infoLen += 1`, `recNumMask |= 0xFF << oldRecNumLen`, every packed entry
re-encoded (needs `nKeys` extra bytes; if `freeSpace < nKeys` it returns 1 and the block is
split instead). Loop repeats until the record fits (b4block.c:1088-1118).

### 6.5 Large keys (keyLen > 255)

`dupByteCnt`/`trailByteCnt` on disk are single bytes; when `keyLen > 255` the effective 16-bit
extraction masks are `(storedByte << 8) + 255` kept in the in-memory block (B4BLOCK fields
`trailByteCnt/dupByteCnt`, d4data.h:3737-3739), set both at init (b4block.c:668-677) and after
reading a leaf from disk (I4TAG.C:234-248). This is a CodeBase extension (large keys require
non-VFP block sizes); VFP-compatible files always have keyLen ≤ 240 (d4defs.h:2128; enforced
b4block.c:1046-1055).

---

## 7. Leaf seek within a block

`b4leafSeek` (b4block.c:2192-2474) scans entries left-to-right maintaining `curDupCnt` = number
of search-key bytes known to match so far, using the incremental dup counts to skip compares:

```
len = searchLen minus trailing pad chars of the search value (b4calcBlanks, b4block.c:140-153;
      skipped when FoxPro-2.6 compatibility && tag has filter, b4block.c:2211-2214)
allBlank = (len == 0)  → compare full original length instead
duplicates = 0 ; b4top(block)
loop:
  if curDupCnt == duplicates:                # this entry could match
     sig = keyLen - trailCnt(entry)
     compareLen = min(len, sig) - curDupCnt
     bytesSame  = cmp(entryBytes, search + curDupCnt, compareLen)
        # cmp = t4cdxCmp: returns # of leading equal bytes, or -1 if entry byte > search byte
        # (i4init.c:280-302; v4map translation only in S4VMAP builds)
     if bytesSame == -1: return r4after
     if bytesSame == compareLen and curDupCnt + bytesSame == len:  (found-candidate logic,
         incl. binary <0x20 tie-breaking and partial-seek handling, b4block.c:2245-2416)
         → return 0 with curDupCnt updated
     curDupCnt += bytesSame
  keyOn++
  if keyOn >= nKeys: return r4after
  duplicates = x4dupCnt(keyOn)
  curPos -= keyLen - duplicates - trailCnt(keyOn)
  if curDupCnt > duplicates:  (blank/binary special cases b4block.c:2436-2461)
      return r4after  (or 0 for matching all-blank keys, b4block.c:2464-2471)
```
The by-product `curDupCnt` is exactly the dup count the key would get if inserted at the found
position — the insert path relies on it (b4block.c:1182, 1257).

Full-tag seek (`tfile4seek`, I4TAG.C:2203-2359): `tfile4upToRoot`, then loop `b4seek` on each
block and `tfile4down` until a leaf; `r4after` in an interior block simply descends the located
entry. Descending tags: keys are **physically stored ascending**; the descending flag only
inverts traversal (I4TAG.C:67-82), and a descending seek increments the search key by one
(machine collation: increment last non-0xFF byte, zeroing 0xFF tails; general collation:
increment last byte ≥ 10) then skips back (I4TAG.C:2092-2151, 2239-2247, 2292-2356).

Positioning on an exact (key, recno) pair (`tfile4go2fox`, I4TAG.C:1339-1458): seek the key,
then walk forward while `recno < target` (keys equal), returning 0 on exact hit, `r4found` when
positioned at first entry with `rec >= recNum` (insert position for adds).

Skips within a leaf move `curPos` by `keyLen - dup - trail` per entry in either direction
(`b4skip`, b4block.c:2927-3008). `b4top` = `keyOn=0; curDupCnt=0; curPos = block + blockSize -
keyLen + trailCnt(0)` (d4declar.h:1775-1776). `b4goEof` puts `curPos` just above `freeSpace`
(b4block.c:1862-1878).

---

## 8. Update algorithms

### 8.1 Leaf insert (`b4insertLeaf`, b4block.c:1005-1450)

Assumes the block is positioned by `tfile4go(key, rec, goAdd=1)` (i4addtag.c:612). Cases:

1. **Empty block** — if `freeSpace == 0` (virgin block) run `b4leafInit` first
   (b4block.c:1139-1145). Store `keyLen - trail` bytes at `block+blockSize-len`; entry0 =
   `putInfo(rec, trail, 0)` (b4block.c:1146-1176).
2. **Append at end** (`keyOn == nKeys`) — `dup = curDupCnt` (from the preceding seek),
   `reqd = keyLen - trail - dup` (clamped ≥ 0 by shrinking dup, b4block.c:1184-1189). If
   `freeSpace < reqd + infoLen` return 1 (split). Else `curPos -= reqd`, copy
   `key[dup..]`, write info at `data + keyOn*infoLen` (b4block.c:1180-1218).
3. **Insert before an existing entry** — compute the right neighbor's new dup count
   (`rightDups = oldRightDups + extraDups` where `extraDups` = additional bytes the right key
   shares with the new key beyond its old dup count, with blank/binary edge cases,
   b4block.c:1219-1295). If `freeSpace < reqd + infoLen` return 1. Then shift the key heap
   below the insertion point down by `reqd` (`movLen` computation b4block.c:1312-1336), rewrite
   the right key's stored bytes for its new dup count, store the new key's bytes, shift the
   info array right one slot, and write two info entries: the new one
   `putInfo(rec, newTrail, newDups)` and the updated right neighbor
   `putInfo(rightRec, rightBlanks, rightDups)` (b4block.c:1337-1416).

Finally `nKeys++; freeSpace -= reqd + infoLen` (b4block.c:1420-1422).

### 8.2 Add with splits (`tfile4addDo`, i4addtag.c:573-907)

```
tfile4go(key, rec, 1)                        # position; also unique checks (i4addtag.c:653-690)
version = versionOld + 1                     # bump change counter (i4addtag.c:728)
loop:
  if no current block: create new root:
      node = index4extend(); alloc block; leftNode = rightNode = -1; nodeAttribute = 1
      (multi-user shared mode: swap physical position with the just-split right block via
       tfile4swap, which also patches both blocks' neighbors' sibling pointers on disk,
       i4addtag.c:746-754, 467-547)
      b4insert(root, newBlockLastKey, newBlockNode, itsRecno, 1)
      b4insert(root, oldBlockLastKey, oldBlockNode, itsRecno, 1)
      header.root = rootNode ; rootWrite = 1 ; done (i4addtag.c:729-768)
  rc = b4insert(block, key, rec, recno, 0)
  if rc == 0:
      if inserted at last position of block, propagate b4brReplace(lastKey,lastRec)
      up through parents while each was on its last key (updateReqd, i4addtag.c:780-781,
      884-906) ; done
  if rc == 1:  # block full → split
      new = tfile4split(t4, block)           # §8.3
      re-insert into old or new depending on keyOn (i4addtag.c:810-822)
      key/rec for the parent iteration: keyInfo = old block's (new) last key,
      rec1 = its recno, rec = new block's node (i4addtag.c:836-844)
      continue with parent block
```
`tfile4add` wraps this in a retry loop because a split may consume the insertion without adding
the key (`didAdd == 0` → return 1 → retry) (i4addtag.c:551-567).

### 8.3 Block split (`tfile4split`, I4TAG.C:3071-3187)

- New block node from `index4extend` (free list or EOF) (I4TAG.C:3092).
- Leaf split (`tfile4leafSplit`, I4TAG.C:1738-1872): old block keeps the first
  `nKeys - nKeys/2` keys, the new (right) block receives the last `nKeys/2`. The new block's
  first entry is re-encoded with dup=0 and its full significant bytes (trail recomputed),
  remaining key text and info entries are memcpy'd, both `freeSpace` values recomputed
  (I4TAG.C:1784-1858). Special case `nKeys == 1`: one block gets the single key, the other is
  emptied (I4TAG.C:1746-1781).
- Branch split (`tfile4branchSplit`, I4TAG.C:612-642): `nNew = (nKeys+1)/2` (minus 1 when
  `keyOn > nNew`); the last `nNew` entries are memcpy'd to the new block; both attributes set
  to 0 (root bit cleared).
- Sibling links: `new.right = old.right ; new.left = old ; old.right = new`, and if the new
  block has a right neighbor, that neighbor's `leftNode` field (file offset `node + 4`) is
  patched on disk directly (I4TAG.C:3124-3154).

### 8.4 Leaf remove (`b4removeLeaf`, b4block.c:2480-2877)

- Last remaining key: reset block to empty (`freeSpace = blockSize - 24`, zero data,
  b4block.c:2516-2541).
- Removing the block's last entry: zero its key bytes and info slot (b4block.c:2548-2572).
- General case: the right neighbor's dup count is recomputed —
  `newRightDup = min(removedDup, oldRightDup)` normally (b4block.c:2691), with two special
  cases: keep `oldRightDup` when removing an all-blank key between all-blank keys
  (b4block.c:2680-2687), and full re-derivation by comparing physical neighbor keys when the
  removed key was `dup+trail == keyLen` with pad-char data at the boundary
  (b4block.c:2588-2673). Then the surviving bytes are merged (`copyBuffer` juggling,
  b4block.c:2695-2753), the heap compacted upward by `removeLen`
  (b4block.c:2755-2814), the info array shifted left one slot and the right neighbor's info
  re-encoded (b4block.c:2817-2843). `nKeys--; freeSpace += infoLen + removeLen`
  (b4block.c:2850-2851).

Branch remove is a plain array delete of one `keyLen+8` entry (b4block.c:2882-2921).

Higher-level removal keeps the B-tree valid via `tfile4removeCurrent` /
`tfile4balance` (i4remove.c:2269-2410, 2486+): empty blocks are unlinked, parent references
fixed (`tfile4removeRef*`, i4remove.c:542-955), underfull leaves/branches merged with siblings
(`tfile4balanceMerge*`, i4remove.c:957-1300), freed blocks returned via `index4shrink`
(§9). A one-key root replacing itself with its child, sibling borrow, etc. are all in
i4remove.c:575-955.

### 8.5 Bulk build (create/reindex) (`r4reindexWriteKeys`, r4reinde.c:1929-2119)

Keys are produced sorted (`sort4get`), then appended into an in-memory chain of
`nBlocks` block images (leaf at level 0, branches above):

- `r4reindexAdd` (r4reinde.c:2123-2212) appends to the current leaf image, computing
  `dup = commonPrefix(key, lastKey)` clamped by `kLen - lastTrail` ("don't allow duplicating
  trail bytes", r4reinde.c:2161-2162), `trail = 0` for full duplicates else `b4calcBlanks`
  (r4reinde.c:2166-2175). When the leaf is full it is flushed (`r4reindexToDisk`) and the last
  key is promoted into the level-1 branch image (r4reindexBranchAdd writes
  `key + LE-recno… actually recno little-then-reversed`: key bytes, then `lRecno` (masked, LE in
  memory then byte-reversed before writing — net **big-endian** on disk), then byte-reversed
  block node (r4reinde.c:1744-1754, 2317-2330, 2426-2430)).
- `r4reindexFinish` (r4reinde.c:2216-2434): single-block tag ⇒ `nodeAttribute |= 3`
  (leaf+root); otherwise the last key propagates up each branch level, the topmost branch gets
  `nodeAttribute = 1` (r4reinde.c:2230, 2432-2433).
- Blocks are written sequentially after the header area; `header.root = lastblock`
  (r4reinde.c:2116-2117); the free list is reset to 0 and file truncated to EOF
  (r4reinde.c:1787, 1914-1918).
- Unique tags: during the sorted scan, a key equal to `lastKey` errors (`e4unique`/
  `e4candidate`), returns `r4unique` (`r4unique`/`r4candidate`) or is skipped
  (`r4uniqueContinue`) (r4reinde.c:2083-2107).

---

## 9. Free list and file extension

There is **one free list for the whole file**, headed by the `freeList` field of the
tag-directory header (file offset 4):

- **Free** (`index4shrink`, i4index.c:1990-2089): write the current `freeList` head into the
  **first 4 bytes of the freed block**, then set `freeList = freedNode`
  (i4index.c:2061-2073). (In S4COMIX 2.5/2.6 compatibility the link lives at bytes 4-7,
  i4index.c:2063-2068 — not applicable to the VFP-30 port.)
- **Allocate** (`index4extend`, i4index.c:857-1018): if `freeList != 0`, pop: result =
  `freeList`; new `freeList` = u32 read from the head block's first 4 bytes
  (i4index.c:901-961). Else result = `i4->eof`, and `eof += blockSize/multiplier`
  (i4index.c:906-911). `index4extend` may only be called with the version already bumped
  (E4ANALYZE check, i4index.c:879-884).

The per-tag `freeList` header field of regular tags is written as 0 and never maintained
(r4reinde.c wrote whole header with freeList untouched from create-time zero; only the
tag-directory `freeList` is read/written at runtime, i4index.c:935, 2070-2073).

---

## 10. Version counter, concurrency, locking

- `version` (tag-directory header offset 8, big-endian) is the whole-file change counter.
  Every writer bumps it before modifying the tree: `tagIndex->header.version = versionOld + 1`
  (i4addtag.c:728; i4add.c:1310-1312; r4reinde.c:350-353), and `index4updateHeader` flushes the
  first 16 bytes only when `versionOld != header.version`, then sets
  `versionOld = versionReadUnlocked = version` (i4index.c:2128-2156).
- Readers call `index4versionCheck`: re-read the first 16 bytes; if `version == versionOld`
  nothing changed; otherwise all cached blocks of every tag are discarded and the previous
  position re-found by key+recno (`tfile4go`) or EOF (i4index.c:2348-2473; i4versionCheck
  i4index.c:2219-2336). `versionReadUnlocked` tracks the newest version seen while reading
  without a lock (i4index.c:2452-2467; d4data.h:4163-4164).
- Write lock: one byte at offset `L4LOCK_POS - 1` = 0x7FFFFFFD (or `L4LOCK_POS` +
  `largeFileOffset` in large-file mode) of the CDX file (I4LOCK.C:381-384; d4defs.h:2179).
  If the file is locked (or opened exclusive), `index4update` flushes all dirty blocks,
  the root pointers (`tfile4update`, I4TAG.C:3363-3434 — flushes `saved` and `blocks` lists,
  then the 4-byte root if `rootWrite`), and the header (i4index.c:1042-1086).
- When a reader detects an inconsistency mid-descent while the file is *not* locked, it waits
  one second (`tfile4outOfDate`, I4TAG.C:1878-1922), refreshes, and retries from the root
  (return code 2 from `tfile4down` / i4readBlock, I4TAG.C:678, 808-823, 2254-2265).
- Multi-user root creation compatibility: on new-root creation in shared mode the root's and
  right block's physical positions are swapped and all affected sibling links patched on disk
  ("(temporary) fix for FoxPro multi-user compatibility", tfile4swap, i4addtag.c:467-547).

---

## 11. Corruption detection / validation

- **On open**: root ≠ 0/-1 and `typeCode >= 32` (i4index.c:1706-1716); expression key length
  must equal header `keyLen` (i4init.c:485-490); filter must be logical type
  (i4init.c:516-517); collation name must be known (i4init.c:372-418); CodeBase
  blockSize/multiplier consistency (i4init.c:542-545).
- **Per block read** (`i4readBlock`, I4TAG.C:112-433), when `CODE4.doIndexVerify` is on:
  - a non-root block with `nKeys == 0` ⇒ inconsistent (I4TAG.C:278-287);
  - E4ANALYZE: `0 ≤ nKeys ≤ blockSize`, leaf `0 ≤ freeSpace ≤ blockSize`
    (I4TAG.C:291-303);
  - parent/child recno consistency: `b4recNo(parent, keyOn) == b4recNo(child, nKeys-1)`
    (I4TAG.C:362-381);
  - inconsistency with the file locked ⇒ hard `e4index` error; unlocked ⇒ retry after refresh
    (I4TAG.C:410-418).
- **During traversal**: `keyLen - dup - trail < 0` in a leaf ⇒ `e4index` (b4block.c:1916-1924,
  2429-2430); write of a block below offset 1024 refused (b4block.c:1824-1829); split target
  node of 0 refused (I4TAG.C:3095-3100).
- **`d4check`** (i4check.c:30+, c4checkRecord i4check.c:127-323): walks each tag in key order
  verifying: recno in `1..recCount` and not duplicated per tag (bit flags,
  i4check.c:144-160); re-evaluated expression matches the stored key (i4check.c:170-211);
  trail count matches actual pad bytes (i4check.c:228-246); keys non-decreasing with recno
  tie-break (`rc==0 && num <= oldRec` ⇒ error) (i4check.c:247-298); and record count of tag vs
  filter (E85708, i4check.c:308).

---

## 12. Key value formats (what the stored key bytes are)

Key bytes are the output of the tag expression, transformed so plain `memcmp` sorts correctly:

- **Character** (machine collation): raw bytes, space-padded to keyLen; `stok = t4noChangeStr`
  (i4init.c:583-592, i4conv.c t4noChangeStr).
- **Numeric / double / date-as-double** (8 bytes): IEEE double, bytes reversed to big-endian;
  positive ⇒ set top bit (`result[0] += 0x80`), negative ⇒ complement **all 8 bytes**
  (`t4dblToFox`, i4conv.c:2432-2465). Dates convert via `date4long` then `t4dblToFox`
  (t4dtstrToFox, i4conv.c:1124-1128). `pChar = '\0'` (i4init.c:563-581).
- **Currency** (8 bytes): int64 scaled by 10⁴, byte-reversed, sign-bit flip (`t4i8ToFox`,
  i4conv.c:1096-1116; `t4strToCur` i4conv.c:273).
- **Integer** (4 bytes): byte-reversed, `+0x80`/`-0x80` on the top byte (`t4intToFox`,
  i4conv.c:1012-1035); short analogue t4shortToFox (i4conv.c:1066-1076).
- **General/custom collations**: bytes are translated through collation tables selected by
  `sortSeq` ("GENERAL" per codepage cp1252/cp437/cp850; "CBnnnnn" CodeBase custom)
  (i4init.c:372-418); descending order is NOT encoded in the key bytes (§7).

Comparison inside blocks is always unsigned bytewise (`u4memcmp` for branches b4block.c:2150,
`t4cdxCmp` for leaves returning match length, i4init.c:280-302).

---

## 13. S4CLIPPER / S4MDX divergences (for orientation only)

- S4CLIPPER (`.NTX`-style, one file per tag): 1024-byte blocks (d4defs.h:2209), multiplier 512
  (d4defs.h:2134), uncompressed entries `{pointer,num,value}` with a pointer array
  (d4data.h:3647-3652, 3862-3865), text header with sign/version/root/eof (d4data.h:3779-3823).
- S4MDX (dBase IV `.MDX`): 48 tag slots of 32 bytes at offset 544 (I4HEADER,
  d4data.h:3828-3847; i4index.c:1888-1896), uncompressed leaf entries, typeCodes
  0x10/0x50/0x58 (d4data.h:3935).
- Neither uses leaf compression, big-endian branch fields, nor a tag-name B-tree.

---

## 14. Open questions / risks for the C# port

1. **`typeCode` 0x40 bit semantics.** CodeBase writes 0x60 for every tag and 0xE0 for the tag
   directory, and tests `>= 64` for "compound" (i4create.c:847,923; i4index.c:1760). The header
   comment ("0x32 Compact; 0x80 Compound", d4data.h:3901) conflicts with the code. Verify
   against genuine VFP files whether VFP itself distinguishes 0x60 vs 0xE0 the same way.
2. **`signature` byte.** Written as 0x01 (i4create.c:853,922) and declared "unused"
   (d4data.h:3902). Unknown whether VFP tools care about other values.
3. **`x4putInfo` writes 6 bytes** into a temp buffer and copies `infoLen` of them
   (b4block.c:924,935, 1176). `infoLen` can theoretically reach 8 with 16-bit dup/trail
   counters (keyLen > 255) plus 32-bit recno — but `recNumLen` is capped at 32 and the >4-byte
   path assumes the dup/trail fit in the 4 bytes at offset 2 (b4block.c:943-956). Key lengths
   requiring `dupCntLen > 16` are impossible (keyLen ≤ 0xFFFF), but keyLen > 240 is a CodeBase
   extension; the port can restrict to infoLen ≤ 6 and keyLen ≤ 240 for VFP compatibility.
4. **Descending + general collation seeks** rely on properties of the collation key layout
   (values < 10 are tail markers, blank = 0x11) (I4TAG.C:2101-2137). If the port initially
   supports only machine collation, gate descending general-collation seeks off.
5. **Record-number cap.** `recNumLen ≤ 32` and mask arithmetic caps usable record numbers at
   2³²-2 in principle, but the code comments note failures historically at ~70M records before
   the cap fix (b4block.c:694-697) — port should include the cap and the widening loop
   (§6.4).
6. **`tfile4swap` shared-mode root swap** (i4addtag.c:467-547) exists purely for FoxPro
   multi-user compatibility; decide whether the C# port supports shared-write CDX at all. If
   files are opened exclusively, the swap is skipped (`lowAccessMode != OPEN4DENY_RW` test,
   i4addtag.c:746-754).
7. **Version write ordering is not atomic**: readers may see the version change before/after
   the tree blocks land; CodeBase mitigates with `versionReadUnlocked` and the 1-second retry
   (`tfile4outOfDate`) (i4index.c:2452-2467; I4TAG.C:1878-1922). The port must reproduce
   "retry from root on inconsistency" or restrict to exclusive access.
8. **The `hasKeys` NUL write at `header + sizeof(T4HEADER) + 222`** (i4addtag.c:692-698)
   depends on the C `sizeof(T4HEADER)` (compiler-dependent padding of the S4FOX struct). It
   lands in the reserved region so it is harmless, but the port should simply not write it.
9. **`filterPos`/`exprPos` fields**: CodeBase sets `filterPos = exprLen`, `exprPos = 0`
   (i4create.c:1076, 850-851) and never reads them back (filter located at
   `512 + exprLen`, i4init.c:496-498). Confirm VFP also ignores them.
10. **Empty non-root leaves**: `b4removeLeaf` can leave a 0-key block that the balance pass
    should remove; `i4readBlock` treats a 0-key non-root block as inconsistent
    (I4TAG.C:278-287). Port must ensure balance always runs after deletions (i4remove.c:2269+),
    or tolerate 0-key blocks when reading its own files.
11. **Collation tables** ("GENERAL", "CBnnnnn") are external data (collate4setupReadFromDisk,
    i4init.c:441-460); the C# port needs the actual VFP GENERAL weight tables for cp1252/437/850
    to be byte-compatible — these were not extracted in this pass.
