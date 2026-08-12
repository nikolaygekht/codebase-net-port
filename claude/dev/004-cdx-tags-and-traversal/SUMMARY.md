# 004-cdx-tags-and-traversal — summary

**Closed:** 2026-08-12. **Gate passed.** Capabilities advanced: `CDX-READ` — **decode and traversal
done**, seek is step 005; `CORPUS` — the index half.

No commit hash here, for the same reason the root `STATE.md` header carries none: a file cannot name
the commit it is part of. `git log` over this folder is the record.

## What shipped

**The corpus's first index files, and a reader for them.** **900 tests green**, up from 699: 515 unit,
component and fault-injection, 385 golden.

```csharp
using IndexFileReader index = IndexFileReader.Open(source, "CUSTOMER.cdx", padByteFor);

foreach (CdxTag tag in index.Tags)
{
    TagCursor cursor = tag.OpenCursor();
    for (bool any = cursor.Top(); any; any = cursor.Next())
    {
        IndexEntry entry = cursor.Current;   // key bytes, rebuilt and padded, plus the record
    }
}
```

Everything is `internal`: nothing connects a tag to a `Table` and nothing public appeared, which was
the step's scope call.

**Corpus** — four indexed cases, all byte-stable on regeneration:

| Case | Records | What it is for |
|---|---|---|
| `CDXBASE.DBF` + `.cdx` | 32 | Ten tags, one block per tree, one tag per key shape |
| `CDXDEEP.DBF` + `.cdx` | 600 | Four tags; `D_WIDE` is **three levels** — 55 leaves, six branches, a root |
| `CDXCOLL.DBF` + `.cdx` | 32 | One cp1252 `C(20)` field indexed twice, machine and `GENERAL` |
| `IDXONE.DBF` + `.cdx` + `.IDX` | 300 | One tree in both file shapes (ADR-25) |

**Generator** — `dump-index.cpp` writes `<NAME>.cdx.dump.txt` entirely from the C library's own
structures (ADR-24), `case-cdxbase/cdxdeep/cdxcoll/idxone.cpp`, and the byte escaper moved into
`util.cpp` so both dump writers share it. `copy-corpus.bat` now publishes `.cdx` and `.IDX` too.

**Library** — `CodeBase.Net.Cdx`: `IndexHeader`, `TagOptions`, `BlockAddressing`, `CollationName`,
`KeyPadding` with `PadByteResolver`, `NodeHeader`, `LeafGeometry` with `PackedEntry`, `LeafBlock`,
`BranchBlock`, `IndexEntry`, `NodeReader`, `IndexFileReader`, `CdxTag`, `TreeBlock`, `TagCursor`. Plus
`ErrorCode.Index`.

**Tests** — `IndexHeaderTests`, `BlockDecodingTests`, `IndexFileReaderTests`, `TagCursorTests`,
`IndexImage` (the in-memory builder), and on the golden side `CorpusIndexDump`, `DumpIndexTag`,
`DumpIndexBlock`, `IndexGoldenTests`.

## What this step proved

Backed by a passing assertion against the C library's own view, over five index files:

- **22 tags, 3364 keys, 155 blocks and 3425 block entries.** Every key byte-exact in navigation order,
  every block's attribute, key count and both siblings, every leaf's bit widths, masks and free space.
- **The bit-packing as an *encoding*, not only through the keys it rebuilds** — each entry's stored
  duplicate and trail counts are compared. A key can come out right from two compensating mistakes; the
  counts cannot.
- **Interior nodes, at two levels of branch**, with the big-endian record number and child pointer.
- **Both file shapes**: a compound file with a tag directory, and a single-tag `.IDX` whose tag is named
  after the file.
- **GENERAL collation read without a collation table** — `keyLen` 40 against the field's 20, pad byte
  `0x00` on a character tag, case-insensitive primaries, accents in the tail block, and `œ`/`ß`/`þ`
  expanded to two heads. This closed `KEY-COLLATION.md` §3.7's standing caveat that the layout was
  verified from source only.
- **Descending traversal** starts at the greatest key and reverses the stored order.
- **The stored order is `(unsigned key bytes, record number)`** — asserted for every tag, which is the
  invariant `d4check` enforces on the C side.
- **Mutation-checked five ways**, and the blast radius of each is the evidence:

  | Mutation | Failed | Where |
  |---|---|---|
  | Branch **record number** little-endian | 3 | Block decode only, only the three files with interior nodes |
  | Branch **child pointer** little-endian | 15 | All five test kinds, same three files |
  | Leaf `dup` and `trail` swapped | 31 of 36 | Everything compressed |
  | Key heap grown upward | 32 of 36 | Everything |
  | Pad byte forced to a space | 5 | Only the two files holding non-character machine tags |

  The child-pointer row is why the corpus work had to come first: on the shipped samples, which are all
  single-leaf, that bug is invisible.

## Two defects in the reference implementation

Both found by using it, both now in `CDX-FORMAT.md`:

- **`tfile4count` returns 1 for any descending tag** (§7). It calls `tfile4top`, which on a descending
  tag lands at the physical *last* key, then skips forward with the physical `tfile4skip`, which moves
  nowhere (I4TAG.C:1000-1019). Witnessed on `T_TEXTD`, which holds 32 keys. The generator counts by
  walking with the direction-aware `tfile4dskip` instead.
- **`d4check` reports every single-tag file as corrupt** (§2.1). `i4checkBlocks` flags the tag
  directory's header blocks and then each tag's header, and in an `.IDX` those are the same block
  (i4check.c:889-914; i4index.c:1824). This changed how `IDXONE.IDX` is verified — see the correction
  in ADR-25 — and the replacement witness, walking both file shapes and comparing all 300 keys, is
  stronger for what the derivation actually claims.

## Deviations from the design

- **`d4check` cannot certify the derived `.IDX`**, which ADR-25 had assumed. Corrected in place there,
  with the dual-shape walk as the witness. The design's plan already contained that check as a
  "cheap structural cross-check"; it turned out to be the primary one.
- **`Q1` answered: 600 records are enough** for a three-level tree with a 40-byte key. No increase was
  needed.
- **`Q2` answered: the `[blocks]` section stays.** The mutation table above is the argument — the leaf
  `dup`/`trail` mutation is caught by it, and a key-only gate would have to rely on the key comparison
  noticing a compensating error.
- **`Q5` answered: no cp437 twin of `CDXCOLL`.** cp1252 gates the layout, which was the point; the
  code-page-to-array mapping is four lines of string comparison and is listed as ungated below rather
  than covered by a second 32-record table.
- **`Q4` settled: `Skip(long)` with the obvious loop**, no block-level shortcut. Nothing measured one.
- **A tag-header read got its own guard.** A directory entry pointing past the end of the file surfaced
  as `ErrorCode.Data` from the generic short-read path; `NodeReader.ReadHeader` now applies the same
  bounds check as a block read, so it is refused as a corrupt index and the message names the node.
- **The tag directory is exposed** as `IndexFileReader.Directory`, which the design did not mention. It
  is read as a tag either way, and the gate checks its blocks and keys like any other tag's.

## Ungated — no corpus case exists

- **A packed entry wider than four bytes.** Needs `recNumLen` above 16, so a table of more than 65 536
  records. Unit-tested at widths 3 to 6 by the arithmetic identity, and that is our interpretation
  rather than a witnessed fact.
- **A block size other than 512, and a multiplier above one** — the `codeBaseNote` extension, which
  `i4create` never writes for a VFP-compatible file.
- **`keyLen` above 240** (refused), and the 16-bit compression counters that go with it.
- **GENERAL over cp437 and cp850** — the array is chosen by the table's code page, and only cp1252 is
  gated. cp850 would also need `S4CODEPAGE_850` in the generator's configuration.
- **`CBnnnnn` collations** (refused): their tables live in a file the index does not carry.
- **A multi-block tag directory**, which needs roughly forty tags in one file.
- **A tree built by insert-and-split** rather than by the bulk path. Every case here appends records
  first and creates the index afterwards, which is what VFP's `INDEX ON` does; `CDXCOLL`'s second tag is
  the one exception, added to an existing file by `i4tagAdd`.
- **The free list**, which only a delete path fills, and a **0-key non-root block**, which the reader
  tolerates and steps over.

## For the next step

- **Seek is 005**: `b4seek`'s branch binary search, `b4leafSeek` with its duplicate-count skipping and
  its sub-`0x20` and all-blank special cases, `tfile4go2fox`, and the descending seek's key increment.
  The dump gains a seek section the same way this one gained an index half (ADR-16).
- **A collated seek needs `COLLATION`**, because the search value has to be translated through the
  weight tables before it is compared, and the descending increment rule is collation-dependent
  (I4TAG.C:2092-2151). Reading needed none of that; seeking does. Say so in 005's design rather than
  discovering it.
- **`CDX-READ` cannot be called complete until `EXPR` supplies the pad byte** for a machine-collated
  tag (ADR-26). Everything else about it is done.
- **`CDXDEEP` is the case to reach for** when something needs a real tree: 600 records, three levels,
  leaves that are full, and runs of equal keys crossing block boundaries.
