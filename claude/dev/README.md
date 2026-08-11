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
