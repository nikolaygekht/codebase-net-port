# 004-cdx-tags-and-traversal — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | `IndexHeader` parse and validation; the `sortSeq` → collation mapping and its pad character; `TagOptions` bits; `BlockAddressing` including `codeBaseNote`; `NodeHeader` with the leaf **bit test**; `LeafGeometry`'s entry decode at `infoLen` 3, 4, 5 and 6; `LeafBlock` key reconstruction; `BranchBlock`'s big-endian pair | Values no corpus file holds: `nodeAttribute` 5, which is `>= 2` and is **not** a leaf, a non-512 block size, `infoLen > 4`, `keyLen - dup - trail` negative, a `typeCode` below 32, a `"CBnnnnn"` or unknown `sortSeq`, and `"GENERAL"` on a cp1250 table — which the C library refuses (i4init.c:404-405) |
| 2 Component | `NodeReader` + `IndexFileReader` + `TagDirectory` + `TagCursor` over `InMemorySource` on hand-built index images: a one-block tag, a two-level tag, a directory with several tags, a single-tag file | The traversal state machine — top, bottom, both `Eof` and `Bof`, stepping across a leaf boundary in both directions, a descending tag — and the descent/sibling agreement, all without a disk |
| 3 Fault injection | `FaultySource` and truncated or contradictory images: a child pointer of 0, of 0xFFFFFFFF, past the end of the file; a block whose `nKeys` exceeds its capacity; a leaf whose heap and info array overlap; a short read mid-block; `keyLen` above 240; a non-compact `.IDX`; a leaf chain that reaches a branch; a branch whose child is itself | That a broken index **refuses** instead of walking into whatever the bytes happen to say. An index that returns plausible record numbers for a corrupt tree is the worst failure this subsystem has |
| 4 Golden / corpus | Every tag of `CDXBASE.cdx`, `CDXDEEP.cdx`, `CDXCOLL.cdx`, `IDXONE.cdx` and `IDXONE.IDX`, plus each compound file's tag directory: the `[keys]` sequence, every `[blocks]` value, and the ordering invariant of Decision 12 | Whether we read the real bytes at all. Layers 1-3 can all pass on a self-consistent misreading of the bit packing |

**Corpus coverage.** Three compound files and one single-tag file; 17 tags plus 4 tag directories;
32-record and ~600-record bit-width sets; single-block trees and a three-level tree; machine and
GENERAL collation over the same field, with `keyLen` 20 and 40 and pad characters `' '` and `'\0'`.

Touched but **uncovered by any corpus case**, to be listed as ungated in `SUMMARY.md`:

- **`infoLen > 4`**, which needs `recNumLen > 16` and therefore a table above 65 536 records — a
  corpus case too large to check in. Covered at layer 1 only, by the arithmetic identity of
  Decision 2, and named as our interpretation rather than a witnessed fact.
- **`keyLen > 240`** (refused) and **a block size other than 512** — both CodeBase extensions
  (`codeBaseNote == 0xABCD`), which `i4create` never writes for a VFP-compatible file.
- **A multi-block tag directory**, which needs roughly forty tags in one file.
- **GENERAL over cp437 and cp850** (the array is chosen by the DBF's code page) — cp1252 is gated,
  the others depend on Q5; cp850 also needs `S4CODEPAGE_850` in the generator's `cb-config.h`.
- **`CBnnnnn` custom collations** (refused): their tables are disk-loaded, so no case can exist.
- **A 0-key non-root block** and **the free list**, both of which only a delete path produces.
- **`version` non-zero on a regular tag**, which CodeBase never writes (`CDX-FORMAT.md` §10).

**Expected values.** Every key, record number and block value comes from
`net/corpus/<NAME>.cdx.dump.txt`, which the C library wrote from its own structures. The pad
character comes from the same dump, as *input* to the reader (Decision 6). Hand-built index images
in layers 1-3 are **input**, never expectation.

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **`CDXBASE` case in the generator.** The table, its ten tags, `d4check` | The generator runs and `d4check` returns ok; regenerating gives byte-identical files. A first look at the bytes: the tag headers sit at 1024-byte slots from offset 1024, every tree is one block with `nodeAttribute` 3, and `sortSeq` is eight zeros — the machine-collation assumption |
| 2 | **The index dump writer** (`dump-index.cpp`), from the library's own structures | Its output for `CDXBASE`, read by a human once, end to end. Then: *`T_UNIQ` has fewer keys than the table has records*; *`T_TEXTD`'s sequence is `T_TEXT`'s reversed*; *the two adjacent blank keys both show `dup=0 trail=20`*. Settles Q2 |
| 3 | **`CDXDEEP` and `IDXONE` cases.** The deep table, and the `.IDX` derivation of Decision 3 | The dump's block attributes show **at least three levels** for `D_WIDE` (Q1 — raise the record count if not); `D_DUP` shows equal keys either side of a leaf boundary; `d4check` ok on `CDXDEEP` and on `IDXONE.cdx`; and the C library opens `IDXONE.IDX` with `i4open`, walks it beside `IDXONE.cdx` and agrees on all 300 keys — which is the check that the derivation preserved the tree, and the **only** one available, because `d4check` cannot check a single-tag file at all (i4check.c:889-914) |
| 3b | **`CDXCOLL`, the collation case** (Decision 9): cp1252, one C(20) field indexed both machine and `GENERAL` | The dump shows `C_GEN` with **keyLen 40** and `pChar=0x00` against `C_MACH`'s 20 and `0x20`; `d4check` ok, which means the C library re-derived every collated key and agreed; the accented rows sort after their base letters rather than after `Z`, and `œ`/`ß`/`þ` occupy two head bytes — the first real bytes anyone has for `KEY-COLLATION.md` §3.4. Settles Q5 (the cp437 twin) |
| 4 | **The entities.** `IndexHeader`, `TagOptions`, `BlockAddressing`, `NodeHeader`, `LeafGeometry`, `LeafBlock`, `BranchBlock` | Layer 1, over hand-built spans: *a leaf is decided by bit 0x02, so attribute 5 is not a leaf although it is greater than 2*; *`version` is read big-endian and `root` little-endian in the same header*; *a branch entry's record number and child are big-endian*; *the key heap grows down from the block end*; *a blank key repeats nothing from its neighbour*; *`keyLen - dup - trail < 0` throws `ErrorCode.Index`*; *`infoLen` 5 and 6 decode by the same arithmetic*. **Mutation check:** flipping the branch pair to little-endian, and the heap to grow upward, must each break these tests |
| 5 | **`NodeReader`, `IndexFileReader`, `TagDirectory`.** Opening both shapes, and resolving the collation | Layer 2: *a compound file yields one tag per directory entry, with the header node the directory recorded*; *a single-tag file yields one tag named after the file*; *the tag directory decodes with pad character `' '` and `keyLen` 10*; *a `"GENERAL"` `sortSeq` resolves by the table's code page and needs no resolver, because its pad character is `'\0'`* (Decision 9). Layer 3: *`typeCode < 32` is refused as non-compact*; *`root` of 0 or 0xFFFFFFFF is refused*; *a child pointer past the end of the file is refused and the message names the node*; *a short read mid-block refuses rather than padding*; *`"CBnnnnn"`, an unknown `sortSeq`, and `"GENERAL"` on a cp1250 table are each refused with the collation named* |
| 6 | **`TagCursor` traversal.** `Top`, `Bottom`, `Skip`, `Eof`, `Bof`, descending | Layer 2: *a walk from `Top` to `Eof` visits every key once, in order*; *`Bottom` then backward gives the reverse of that*; *stepping off either end sets `Eof`/`Bof` and stays there*; *a leaf boundary is crossed in both directions*; *a descending tag walks the physical order backwards*. **Cross-check:** *walking by sibling links equals re-descending from the root for every key* (Decision 1). Settles Q4 |
| 7 | **The gate.** `CorpusIndexDump` + the golden suite | The gate below. Built as: parse the dump strictly (an unknown section is refused, as `CorpusDump` already does); assert `[keys]` for every tag; assert every `[blocks]` field including per-entry `dup`/`trail`; assert the block set reached by descent equals the dump's. **Mutation checks, each of which must go red:** the branch pair read little-endian; the leaf info's `dup` and `trail` fields swapped; the pad character forced to `' '` for every tag (which must break `T_NUM`, `T_DBL`, `T_DATE`, `T_INT` and `D_NUM` and nothing else — the blast radius is the evidence) |
| 8 | **Documents.** `CDX-FORMAT.md` gains the single-tag `.IDX` subsection (Decisions 3 and 4); `net/corpus/README.md` and `test-files-generator/README.md` gain the three cases and the dump's index half; ADR-24, ADR-25, ADR-26 land; `PORTING-PLAN.md` §5 statuses move | The spec diff cites `FILE.C:line` for every new claim; `claude/dev/README.md` and the root `STATE.md` name what shipped; `SUMMARY.md` lists the ungated paths above verbatim |

Sub-steps 1-3 are the `CORPUS` half and produce no C#; they are a natural commit boundary
(`corpus: …`, as the code-page and `VFPNULL` cases were). Sub-steps 4-8 are the step proper.
Before writing any `///` comment, load the `docgen-skill` skill — the failure mode is silent.

## Gate

```
dotnet test net/CodeBase.Net.sln
```

green, with the index golden suite asserting, **for every tag of every index file in the corpus and
for each compound file's tag directory**:

- the whole `(key bytes, record number)` sequence in navigation order, and its count;
- every block's `node`, attribute, key count and sibling pointers;
- every leaf's `freeSpace`, the three bit widths, `infoLen` and the three masks;
- **every leaf entry's stored duplicate and trail counts**, which is the bit-packing itself;
- every branch entry's key, record number and child node;
- **the ordering invariant** — the walked sequence non-decreasing under *unsigned* byte comparison
  with the record number breaking ties, reversed for a descending tag (Decision 12), which is the
  same invariant `d4check` enforces on the C side;

with **no tag skipped and no block skipped** — asserted arithmetically, as step 003's gate does: the
suite counts tags and blocks compared and checks the totals against the dump's own counts, so an
empty comparison cannot pass as success.

## Risks

| Risk | Why it would make this step *wrong* | Cheapest early exposure |
|---|---|---|
| **The bit fields are read at the wrong offsets or widths** (R1) | Every key and every record number in the file is wrong, and the failure is silent because any bit pattern decodes to *some* record number | Sub-step 4's unit tests, then sub-step 7's per-entry `dup`/`trail` assertions over the whole corpus. This is why `[blocks]` is in the dump at all |
| **The key heap is walked in the wrong direction** | Keys come out looking like keys — fragments of neighbouring keys — for exactly the entries whose duplicate counts are non-zero | The named mutation check in sub-step 4, plus `D_PFX` (near-maximal duplicate counts) and `D_WIDE` (near-zero) failing differently |
| **The pad character is wrong for a tag** (Decisions 6, 9) | Numeric keys come back space-padded: they compare and sort wrong, silently, and only in the padded tail | The blast-radius mutation in sub-step 7: forcing `' '` everywhere must fail the five non-character tags **and `C_GEN`, the character tag whose pad character is `'\0'`** — and no others |
| **Byte comparison is done signed somewhere** (Decision 12) | Every key byte above 0x7F misorders: accented GENERAL keys and every complemented negative numeric key | The ordering invariant in sub-step 7 runs over `C_GEN` (accents, 0x80+ head bytes) and `T_NUM`/`T_DBL` (complemented negatives), so a signed compare cannot pass it |
| **Interior nodes read little-endian** | Descent lands on a plausible wrong block; the walk still produces keys, in the wrong order or from the wrong subtree | Sub-step 3 must produce a three-level tree before sub-step 7 can catch this at all — which is why the corpus half comes first |
| **The corpus comes out single-leaf anyway** | The step's headline claim — interior nodes are read — would be untested, exactly as `original/examples/DATA/` left it | Q1 is checked in sub-step 3, from the dump's own block attributes, before any C# is written |
| **The `.IDX` derivation produces a file the C library reads but VFP would not** | We would gate `.IDX` support against a file shape that does not occur in the wild | The `IDXONE.cdx`/`IDXONE.IDX` sequence equality in sub-step 3 — `d4check` turned out to be unavailable here, see ADR-25's correction — and the derivation changes exactly one byte's meaning (the compound bit), which is the smallest claim available. Flagged for external live-VFP confirmation under ADR-11 |
| **A test asserts the implementation** — e.g. that descent visited blocks in a particular order | The traversal could not be rewritten (a block cache, a path stack) without a red suite | `[blocks]` is asserted as a *set* plus per-block values, not as an order; the order in the dump is documentation, and the test says so |
