# Audit of steps 002–005 — DBF read, memo, CDX decode, traversal, and seek

**Audited:** 2026-08-13, by an independent pass over code, tests, corpus, and specs.
**Scope:** completeness for Visual FoxPro DBF + CDX reading, edge-case coverage, and performance.
**Method:** read every file in `net/CodeBase.Net/{Dbf,Cdx,Memo,IO}/` and the root, plus all tests in
`net/tests/` and the corpus README; cross-checked findings against `claude/specs/*.md`. No code was
changed.

**Headline:** the read path for VFP DBF + CDX + FPT is **faithful and well-gated** — 1039 tests,
453 golden, against eleven corpus tables and four index files. The bit-packed leaf codec, the two
big-endian islands, the descending inversion, and the seek comparison rules are all correct. The
findings below are edge-case gaps and performance properties that the current corpus cannot reach;
none is a known wrong-record-set bug.

Findings are labelled **C** (correctness for VFP compat), **E** (edge case untested), or **P**
(performance), and ranked **High / Medium / Low**.

---

## 1. Completeness for VFP DBF + CDX reading

### 1.1 What is done and gated

| Capability | Status | Gate |
|---|---|---|
| DBF header + descriptors + resolved fields | done | every header/descriptor byte of all 11 tables vs the C dump |
| Record navigation (`Go/Top/Bottom/Skip/Eof/Bof/Deleted`) | done | every record of all 11 tables, four-way walk equivalence |
| All ordinary field types (`C N F D L I B Y T`) | done | every field of every record, values compared bit-for-bit for `N/F/B` |
| `_NullFlags` + nullable fields | done | `VFPNULL` (7 nullable fields, 13 vs 14 descriptors, the assigned-then-nulled record 7) |
| Memo: both reference encodings, multi-block payloads | done | 153 non-empty entries across 5 `.fpt` tables; 505-byte straddle |
| Binary types `M X G Z` | done | `VFPMEMO` |
| Code-page marks (all 26 VFP-documented) | done | `CP1251` (0xC9), `CP936` (0x7A); mark→number→encoding table-driven for all 26 |
| CDX file open + tag directory + `.IDX` shape | done | `CDXBASE` (compound), `IDXONE` (both shapes) |
| CDX interior nodes (big-endian recno/child) | done | `CDXDEEP` (three levels, 55 leaves, six branches) |
| CDX bit-packed leaves | done | 3425 block entries, each entry's `dup`+`trail` counts compared |
| CDX traversal both directions, descending inversion | done | all 18 tags, `D_TEXTD` and `D_PFX` descending |
| CDX seek (`Seek/SeekAtOrBefore/SeekLast/SeekNext/SeekPrevious/SeekExact`) | done | 206 seek cases, 104 seek-next runs, 3364 exact-pair assertions |
| GENERAL collation **read** (head+tail layout) | done | `CDXCOLL` (keyLen 40, accents, `œ`/`ß`/`þ` expansions) |

### 1.2 What is deliberately refused and documented

These are not gaps — they are named limitations with an ADR or spec citation. Listed here so the
audit is complete.

- **`'7'` legacy datetime field type** is rejected at open (`FieldResolver.ReadableTypes` omits it,
  `FieldResolver.cs:33`) even though `FieldValueDecoder` and `FoxDateTime` have live code paths for
  it. A table with a stored `'7'` field cannot be opened. **This is the one type-letter asymmetry
  worth a decision:** either admit `'7'` (the decoder is ready) or remove the dead `'7'` branches
  from `FieldValueDecoder`/`FoxDateTime`/`BlankRecord` so the dead path is not mistaken for support.
  The spec lists `'7'` as an in-scope read type (`DBF-FORMAT.md` §5). **C, Low** — no corpus case has
  a `'7'` field, so no current file fails; but a real FoxPro 2.x table with a `'7'` datetime column
  would be refused.

- **Compressed memo entries (type 3)** refused at read, ADR-23 open. Spec resolved
  (`FPT-MEMO.md` §3.9: zlib-wrapped, 4-byte uncompressed-length prefix). Needs a corpus case. **E,
  Low** — CodeBase-only extension; no VFP file produces these.

- **Long field names (0x31 `UsesLongFieldNames` flag)** refused as `NotSupported`
  (`FieldDescriptorTable.cs:33`). No corpus case. **E, Low.**

- **A tag whose key expression is not a bare field name** (e.g. `UPPER(NAME)`) — the pad byte cannot
  be derived (`KeyTypeResolver` refuses), so the tag is refused when selected, not at open. ADR-28.
  Waits on `EXPR`. **E, Medium** — common in real VFP applications; the fallback (scan) needs the
  expression engine.

- **Stepping in tag order from a record the tag does not list** — throws `NotSupported`
  (`Table.NotInTag`, ADR-30). Only reachable by mixing `Go(n)` (record order) with a tag-order step
  on a filtered/unique tag. `EXPR` would let the port re-derive the key and seek instead. **E, Low.**

- **`CBnnnnn` collations** refused at index open (`IndexHeader`). Tables live in an external DBF
  the index does not carry. Out of scope (`PORTING-PLAN.md` §2.3). **E, Low.**

### 1.3 What is missing and not yet documented as a refusal

- **GENERAL collation over cp437 and cp850.** `CDXCOLL` gates GENERAL over cp1252 only. The
  collation array is chosen by the table's code page; cp850 would need `S4CODEPAGE_850` in the
  generator. The reader does not validate that the collation name matches the table's code page — a
  `GENERAL` tag on a cp850 table would be read with the cp1252 weight table, producing wrong key
  *comparisons* only when a value is seeked (007's problem, since reading keys needs no table).
  **E, Medium** — reading is correct (keys are bytes); seeking by value is where this bites.

- **A block size other than 512, and a multiplier above one** (`codeBaseNote` extension). Unit-tested
  (`IndexHeaderTests:229`) but no golden case. **E, Low.**

- **`keyLen` above 240** (refused), and the 16-bit compression counters that go with it. **E, Low.**

- **A multi-block tag directory** (≈40+ tags in one file). **E, Low.**

- **A tree built by insert-and-split** rather than the bulk path. Every corpus case appends then
  `INDEX ON`. The only exception is `CDXCOLL`'s second tag, added by `i4tagAdd`. The reader handles
  both; the gap is that a split-produced tree with different free-list state is never gated. **E,
  Low** — relevant mostly to `WRITE`.

- **The free list** (blocks freed by delete). Only a delete path fills it; the reader tolerates and
  steps over a 0-key non-root block. **E, Low.**

- **VFP 9 (version byte 0x32)** is read-tolerated (`LegacyVariant`) but its descriptor flags are not
  read and its memo file is not opened — a documented reproduction of a C library defect
  (`LegacyVariant.cs:10-15`). A VFP 9 table with a memo silently has no memo reader. **E, Low.**

---

## 2. Edge cases untested

### 2.1 Edge cases with no corpus case and no unit test

| # | Edge case | Where | Risk | Label |
|---|---|---|---|---|
| E1 | `'7'` legacy datetime field | no corpus, decoder ready but open refuses | a real FoxPro 2.x table with a `'7'` column is rejected | **C, Medium** |
| E2 | `'H'` (4-byte float) field blanking | `BlankRecord.ZeroBlankTypes` omits `'H'` | a blank `'H'` field is 4 spaces, decoding as a garbage float; needs verification against `f4blank` | **C, Medium** |
| E3 | `FoxDate.ToJulian(0, month, day)` numeric overload | `FoxDate.cs:85` — year 0 is treated as leap in `DayOfYear` (text path blanks it earlier) | only reachable via the numeric overload; a caller computing Julian for year 0 gets a leap-day answer that contradicts the inline comment saying year 0 is not leap | **C, Low** |
| E4 | `MemoFileHeader.BlockSize` read as `UInt16` | `MemoFileHeader.cs:62` — spec §3.1 says `blockSize` is a signed `short` | a stored block size with the high bit set (32768–65535) is negative in the C, positive here; the `blockSize == 0 ⇒ 1` branch and arithmetic differ | **C, Low** |
| E5 | `FoxNumeric` uses `double.TryParse`, not a port of `c4atod` | `FoxNumeric.cs:9-15` — body absent from source drop | scientific notation (`1e5`) parses as 0.0 here (NumberStyles excludes exponents); unknown whether `c4atod` accepts exponents. 224 corpus values agree bit-for-bit, but the corpus may not exercise the divergent cases | **C, Low** |
| E6 | Invalid milliseconds in a `'T'` field | `FoxDateTime.cs` — `seconds = ms / 1000` with no wraparound check | a corrupt ms > 86400000 renders garbage time / `ToDateTime` rolls days; matches C's blind spot but untested | **E, Low** |
| E7 | Deleted records | all 224 corpus records are `deleted=0` | `Deleted` accessor is unit/component tested only; no golden case | **E, Low** |
| E8 | Block size ≠ 0/512 for memo | `MemoReader` | only unit-tested for 0; 1, 64, 1024 untested | **E, Low** |
| E9 | `IsNull` with `byteIndex >= bitmap.Length` | `Table.cs:832` — short-circuits to false | a `_NullFlags` field shorter than the bit count implies reads as non-null silently; probably correct (defensive) but ungated | **E, Low** |

### 2.2 Edge cases with a unit/component test but no golden gate

| # | Edge case | Test layer | Why no golden | Label |
|---|---|---|---|---|
| E10 | `SeekExact` on a descending tag's duplicate run | unit only | corpus has no descending tag with duplicate keys and a seek-exact case | **E, Low** |
| E11 | `SeekNext` across a branch boundary (run spans leaves at two depths) | unit only | `CDXDEEP`'s `D_DUP` runs within one leaf level | **E, Low** |
| E12 | `SeekNext` against reference on numeric/date/currency tags | property check only | driving `d4seekNext` needs value→key transforms (007) | **E, Low** |
| E13 | Single-tag `.IDX` with a deep (multi-block) tree | `IDXONE` is single-block | only `IDXONE` exists; a deep `.IDX` is structurally identical to a deep `.CDX` tag but ungated | **E, Low** |
| E14 | Collation table **construction** (key generation) | none | `COLLATION` not started; `CDXCOLL` gates only reading stored keys, not building them | **E, High** — this is the R2 risk and the prerequisite for `WRITE` and value-seek |

### 2.3 Edge cases explicitly handled and worth noting (not gaps)

- Empty search value → matches every key at tag level; through `d4seekN` → full-width zero key
  (`.NULL.` convention). Both in the dump, side by side; golden test skips the one case with a
  citation. **Handled.**
- All-pad search value keeps its length rather than collapsing to zero
  (`KeySearch.For:81`, `b4block.c:2211-2216`). **Handled.**
- All-`0xFF` value on a descending tag → `Eof` not the first entry (`KeySearch.TryIncrement:179`,
  `I4TAG.C:2341-2350`). **Handled.**
- Empty leaf blocks in the chain are stepped over, not treated as the end (`TagCursor:449-480`).
  **Handled.**
- Re-entering from Eof/Bof lands *on* the boundary entry, not past it (`StepPhysical:406-418`).
  **Handled.**
- `_NullFlags` bit ordering is LSB-first (`Table.cs:832`: `1 << (bit % 8)`), confirmed against
  `DBF-FORMAT.md` §4.1 lines 181-184. **Correct.**
- Signature byte is read but never used in logic (varies within a file). **Handled.**
- Tag directory pad byte hard-coded to space regardless of resolver. **Handled.**

---

## 3. Performance

**Nothing has ever been measured** — this is the project's own flagged next step (`STATE.md` §3).
The findings below are structural, from reading the code; they confirm the four suspects named in
`STATE.md` and add two.

### 3.1 No block cache — every access re-reads and re-parses  [P, High]

`CdxTag.ReadBlock` → `NodeReader.ReadAt` (`NodeReader.cs:61`) allocates a fresh `byte[512]` and
reads from the file for **every** block. `TagCursor.StepPhysical` follows sibling pointers by calling
`ReadBlock` for each step. Consequences:

- A full tag walk re-reads every leaf block from the source on every traversal.
- Every descent from the root re-reads the root and each interior level.
- Every `Top` re-descends.
- The `LeafBlock` sequential-build state (`builtIndex`, `textEnd`) is lost when the block is
  re-read, so backtracking after a forward scan re-builds from entry 0.

The C library keeps a block list per tag plus its own file buffering. **This is also a design
question** (where the cache lives — `NodeReader`, `CdxTag`, or the index file — is an ADR), and the
optimizer will drive this path far harder than a walk: several cursors over one tag, thousands of
seeks per query. `STATE.md` names this as suspect #1 and the analysis agrees.

### 3.2 Key array allocated on every entry read  [P, High]

`LeafBlock.EntryAt` (`LeafBlock.cs:117`): `new IndexEntry(key.AsSpan().ToArray(), ...)`. Called once
per comparison inside `LeafBlock.Seek` (`LeafBlock.cs:140`), so a leaf scan allocates `Count` arrays.
`BranchBlock.EntryAt` (`BranchBlock.cs:114`) does the same, so each binary-search probe allocates.

A `CompareAt(int index)` that compares against the internal `key` span without copying would make a
seek allocation-free. `STATE.md` names this as suspect #2 and the analysis agrees; the fix is local
to `LeafBlock`/`BranchBlock`.

### 3.3 `TableTagCursor.Synchronize` is O(n) in the tag  [P, Medium]

`TableTagCursor.Synchronize` (`TableTagCursor.cs:109`) walks the tag looking for the current record
number. A sequential walk hits the O(1) fast path (the cursor is already on the right entry), so
this is paid only when the record and tag cursors have drifted — `Go(n)` then a tag-order step. The
C library pays O(log n) by re-deriving the key through the expression engine and seeking. **Needs
`EXPR`.** `STATE.md` names this as suspect #3.

A pathological case: alternating `Go(n)` and `Skip(1)` in tag order on a large filtered tag where
the record is near the end is O(n) per step → O(n²) overall. Worth documenting in the XML docs or
narrowing the walk until `EXPR` lands.

### 3.4 A record read per position, no reuse  [P, Low]

`Table.Fetch` reads through `RecordReader` every time. An indexed walk now issues one index read
plus one record read per record. Faithful and correct; worth measuring only because the index read
(path 3.1) dominates. `STATE.md` names this as suspect #4.

### 3.5 `KeySearch.For` always copies the value  [P, Low]

`KeySearch.For` (`KeySearch.cs:73`): `value[..length].ToArray()` — allocates a copy on every seek,
even when the caller passes a span that could be used directly. Defensive (the `KeySearch` outlives
the caller's span) but allocates on the seek hot path.

### 3.6 `IndexFileReader.Tag(name)` is a linear scan  [P, Low]

`IndexFileReader.Tag(name)` (`IndexFileReader.cs:119`) iterates `Tags` with
`String.Equals(..., OrdinalIgnoreCase)`. O(n) in tag count. Low impact (tags are few); a dictionary
would be O(1).

### 3.7 `LeafBlock.Seek` does not use duplicate-count skipping  [P, Low]

The C library's `b4leafSeek` uses duplicate counts to skip byte comparisons it can prove
unnecessary (`b4block.c:2192-2474`). This port rebuilds each key and compares in one place
(`LeafBlock.cs:130-134`, comment acknowledges the trade-off). The walk cost is the same (linear),
but the per-entry cost is higher. Relevant only for large leaf blocks; the allocation fix (3.2)
matters more.

---

## 4. Robustness against corrupt files  [C, Low]

These are not VFP-compatibility bugs (well-formed files read correctly) but hardening gaps for the
`HARDENING` tier. Listed because the audit asked for edge cases.

- **No cycle guard in leaf-chain following.** `TagCursor.StepPhysical` (`TagCursor.cs:449-480`)
  follows `RightNode`/`LeftNode` with no cycle guard. The descent paths have `MaxDepth=32`, but the
  sibling-chain loop is unbounded. A corrupt index with a leaf-chain cycle (A→B→A) loops forever.
  **C, Medium** (DoS on a corrupt file).

- **Mask width vs. bit width not validated in `LeafGeometry`.** `LeafGeometry.Parse` validates
  `recordBits + dupBits + trailBits == infoLength * 8` but not that `recordBits <= 32`,
  `dupBits <= 8`, `trailBits <= 8`. The masks are read from the block (`ReadUInt32LittleEndian` for
  record, single bytes for dup/trail), so a corrupt block declaring `dupBits=16` passes the sum
  check and produces wrong unpacked values silently. In practice the widths are bounded by keyLen
  and recordCount, but a hand-crafted corrupt block is not caught. **C, Low.**

- **`SeekFirstAtOrAbove` on an empty non-root leaf reports Eof without trying the next sibling.**
  `TagCursor.SeekFirstAtOrAbove` (`TagCursor.cs:349`) sets `Eof = true` and returns `false` when the
  descent lands on a `Count == 0` leaf. `StepPhysical` *does* skip empty blocks, but
  `SeekFirstAtOrAbove` never calls it. A well-formed file never has a branch pointing to an empty
  leaf, but a corrupt or post-delete index could. **C, Low.**

- **No block-alignment validation for child pointers.** `BranchBlock.EntryAt` returns `Child` as a
  raw `uint`; `NodeReader.Read`/`BlockAddressing.OffsetOf` guard against node 0 and `NoNode` and
  offsets inside the header, but a child pointer that is valid-offset-yet-not-block-aligned reads a
  block starting mid-way through a real block. **C, Low.**

- **Short read from an index file reports `ErrorCode.Data` not `ErrorCode.Index`.**
  `SourceReader.ReadExactly` (`SourceReader.cs:32`) throws `ErrorCode.Data` on a short read even for
  index files. `NodeReader.ReadAt` does its own past-EOF check with `ErrorCode.Index` first, so the
  common case is classified right; a short read that doesn't exceed `source.Length` but returns fewer
  bytes surfaces as `Data`. Minor classification inconsistency. **C, Low.**

---

## 5. Summary of actionable findings, ranked

| Rank | # | Finding | Label | Where |
|---|---|---|---|---|
| 1 | E14 | Collation key **construction** ungated — R2 risk, prerequisite for `WRITE` and value-seek | **E, High** | `COLLATION` (not started) |
| 2 | P1 | No block cache — every traversal re-reads and re-parses | **P, High** | `NodeReader.ReadAt:61` |
| 3 | P2 | Key array allocated on every `EntryAt` (leaf and branch) | **P, High** | `LeafBlock.cs:117`, `BranchBlock.cs:114` |
| 4 | E1 | `'7'` legacy datetime type refused at open (decoder is ready) — decide admit or remove dead code | **C, Medium** | `FieldResolver.cs:33`, `FieldValueDecoder.cs:33,62,199` |
| 5 | E2 | `'H'` float type not in `BlankRecord.ZeroBlankTypes` — verify against `f4blank` | **C, Medium** | `BlankRecord.cs:23` |
| 6 | P3 | `TableTagCursor.Synchronize` is O(n); alternating `Go`+`Skip` on a large filtered tag is O(n²) | **P, Medium** | `TableTagCursor.cs:109` |
| 7 | C-robust | No cycle guard in leaf-chain following → infinite loop on corrupt file | **C, Medium** | `TagCursor.cs:449-480` |
| 8 | E3 | `FoxDate.ToJulian(0,…)` treats year 0 as leap, contradicts the inline comment | **C, Low** | `FoxDate.cs:85,133-134` |
| 9 | E4 | `MemoFileHeader.BlockSize` read as `UInt16`, spec says signed `short` | **C, Low** | `MemoFileHeader.cs:62` |
| 10 | E5 | `FoxNumeric` uses `double.TryParse` not `c4atod` — exponent behavior diverges | **C, Low** | `FoxNumeric.cs:9-15` |
| 11 | E6 | Invalid milliseconds in `'T'` field — no wraparound check | **E, Low** | `FoxDateTime.cs` |
| 12 | E7 | Deleted records — no corpus case (unit/component only) | **E, Low** | corpus |
| 13 | E8 | Memo block size ≠ 0/512 untested | **E, Low** | `MemoReader` |
| 14 | E9 | `IsNull` with short bitmap reads as non-null silently | **E, Low** | `Table.cs:832` |
| 15 | E10–E13 | Seek edge cases unit-only (descending exact, cross-branch seek-next, deep `.IDX`) | **E, Low** | tests |
| 16 | C-robust | Mask width vs. bit width not validated in `LeafGeometry` | **C, Low** | `LeafGeometry.cs:129` |
| 17 | C-robust | `SeekFirstAtOrAbove` on empty non-root leaf reports Eof without trying sibling | **C, Low** | `TagCursor.cs:349` |
| 18 | C-robust | No block-alignment validation for child pointers | **C, Low** | `BranchBlock.EntryAt` |
| 19 | C-robust | Short read from index reports `ErrorCode.Data` not `ErrorCode.Index` | **C, Low** | `SourceReader.cs:32` |
| 20 | P5 | `KeySearch.For` always copies the value | **P, Low** | `KeySearch.cs:73` |
| 21 | P6 | `IndexFileReader.Tag(name)` linear scan | **P, Low** | `IndexFileReader.cs:119` |
| 22 | P7 | `LeafBlock.Seek` does not use duplicate-count skipping | **P, Low** | `LeafBlock.cs:130` |

---

## 6. Verdict

The read path is **correct for every VFP DBF + CDX + FPT file the corpus can produce**, and the
gating discipline (hand-built bytes as input only, every expectation from the C library's own view,
mutation-checked, gate-count assertions) is sound. No wrong-record-set bug was found.

The highest-value actions, in order:

1. **Start `COLLATION`** — it is the remaining P1 prerequisite for value-seek (007) and write, and
   the only High-severity edge-case gap (E14). The corpus already has the `value → key-bytes` table
   needed to gate it.
2. **Measure then cache** — the block-cache and per-entry allocation findings (P1, P2) are the
   performance pass `STATE.md` already names. The cache placement is a design decision (ADR).
3. **Decide the `'7'` and `'H'` type questions** (E1, E2) — small, either admits the type or removes
   the dead code so the support story is honest.
4. **Add the leaf-chain cycle guard** (C-robust #7) — one bounded loop, prevents an infinite hang
   on a corrupt file.