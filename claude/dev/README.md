# Development steps

One folder per step, in order. The method is [`../DEV_APPROACH.md`](../DEV_APPROACH.md); this file is
just the index.

Start a step by copying the template:

```bash
cp -r claude/dev/_template claude/dev/001-step-name
```

Numbers are never reused or renumbered. An abandoned step keeps its folder and gets a `SUMMARY.md`
explaining why — that is a record, not clutter.

| Step | Milestone | Status | What it did |
|---|---|---|---|
| [`001-dbf-open-and-header`](001-dbf-open-and-header/) | `DBF-READ` | **done**, amended 2026-08-10 | Open a DBF (+ companion FPT) and expose its metadata: header, stored descriptors, resolved field table, resolved code page. 224 tests at close, 341 after the amendment; gate green on all seven corpus tables. The amendment fixed the code-page map, which was wrong for 22 of the 26 marks and had no marked table to prove it (ADR-18, ADR-19, ADR-20) |
| [`002-dbf-records-and-fields`](002-dbf-records-and-fields/) | `DBF-READ` | **done** 2026-08-11 | Position on a record and read every ordinary field: `Go`/`Top`/`Bottom`/`Skip`, the four cursor states, per-type decode, text under the table's code page, null and deleted flags. 630 tests at close, 252 golden; gate green on every record of all seven corpus tables, and the offset formula mutation-checked. Closed the numeric question — `double.Parse` matches `c4atod` bit for bit on all 224 values (ADR-21, ADR-22). Memo payloads and the binary-marked types `X`/`G`/`Z` are step 003 |
| [`003-memo-and-binary-types`](003-memo-and-binary-types/) | `DBF-READ` | **done** 2026-08-11 | Read the payload behind a memo reference in both encodings — four-byte binary and ten-byte ASCII — plus the types `X`, `G` and `Z`. 699 tests at close, 268 golden; **the gate now asserts every field of every record with nothing skipped**, which completes `DBF-READ` for reading. Closed `FPT-MEMO.md`'s first open question from the corpus. Compressed entries refused pending a case that can gate them (ADR-23, open) |
| [`004-cdx-tags-and-traversal`](004-cdx-tags-and-traversal/) | `CDX-READ` + `CORPUS` | **done** 2026-08-12 | Gave the corpus its first four index files — ten tags for key shapes, a three-level tree, machine beside `GENERAL` over one field, and a single-tag `.IDX` derived from a one-tag `.cdx` — then read them: tag directory, tag headers, interior nodes, **bit-packed leaves**, both traversal directions. 900 tests at close, 385 golden; **22 tags, 3364 keys, 155 blocks and 3425 block entries** asserted against the C library's own view, mutation-checked five ways. Closed `KEY-COLLATION.md` §3.7's source-only caveat and found two defects in the reference implementation. Internal only; seek is step 005 (ADR-24 to ADR-27) |
| [`005-cdx-seek`](005-cdx-seek/) | `CDX-READ` | **done** 2026-08-12 | The seek family, by key bytes so no collation table is needed: `Seek`, `SeekAtOrBefore`, `SeekLast`, `SeekNext`, `SeekPrevious`, plus exact key-and-record positioning. 972 tests at close, 421 golden; **206 recorded search cases, 104 seek-next runs and 3364 exact-pair assertions**, mutation-checked eight ways. `SeekAtOrBefore` became the primitive the backwards trio is built on. **The corpus overturned the spec's partial-seek pseudocode**: a search value with trailing pad stands for a whole key and its pad bytes compare, without it is a prefix — now witnessed in `CDX-FORMAT.md` §7, along with the all-0xFF descending branch and the two API levels' disagreement about an empty value. Also fixed a latent 004 bug: stepping back from end of file skipped the last entry |
| [`006-tags-on-a-table`](006-tags-on-a-table/) | `CDX-READ` | **designed** 2026-08-12 | Give the index a table: open the production `.cdx` when the header declares one, expose `Table.Tags`, select a tag, and navigate **records** in its order. The first public index surface. Two surfaces onto one implementation: the C library's `Top`/`Bottom`/`Skip` with a selected tag, and an explicit `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed` four that name the tag per call and step unconditionally. Resolves the pad byte from the table's field descriptors when the expression is a bare field name, which is what actually settles ADR-26 for real tags (ADR-28); `EXPR` is then needed only for composite expressions |
