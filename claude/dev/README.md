# Development steps

One folder per step, in order. The method is [`../DEV_APPROACH.md`](../DEV_APPROACH.md); this file is
just the index.

Start a step by copying the template:

```bash
cp -r claude/dev/_template claude/dev/001-step-name
```

**Every folder has its own number, and numbers are never reused.** An abandoned step keeps its folder
and gets a `SUMMARY.md` explaining why — that is a record, not clutter.

> **Renumbered 2026-08-19.** The audit that followed step 006 was filed as a *second* `006-`, on the
> reasoning that it opened no capability and so deserved no number of its own. That gave two folders the
> same number, which is the one thing this rule exists to prevent, so the folders were renumbered to make
> every number unique and the sequence chronological:
>
> | Was | Is |
> |---|---|
> | `006-audit-glm` | [`007-audit-glm`](007-audit-glm/) |
> | `007-seek-by-value` | [`008-seek-by-value`](008-seek-by-value/) |
> | — | [`009-performance-audit`](009-performance-audit/) *(new)* |
> | `008-expr` | [`010-expr`](010-expr/) |
>
> Every reference to the old names and numbers moved with them. **A folder that opens no capability still
> takes the next number** — that is now the rule, and it is why an audit is numbered like a step.

Not every folder here is a step, though. Two are audits, and they are numbered like steps but open no
capability and add no row to `PORTING-PLAN.md` §5:

- [`007-audit-glm/`](007-audit-glm/) — an independent audit of steps 002–005 and the remediation it
  prompted: `audit.md`, the triage and decisions in `REMEDIATION-PLAN.md`, then `DESIGN.md`, `PLAN.md`
  and `SUMMARY.md` for the fixes. **Closed 2026-08-13** at 1054 tests: five real defects fixed, three of
  the audit's own claims overturned, and every remaining finding given a home.
- [`009-performance-audit/`](009-performance-audit/) — the first measurement of anything in this port:
  10 000 seeks through the C library and through `CodeBase.Net`, over the same table and the same query
  sets. **Closed 2026-08-19**, with **no change to `net/`**: `SUMMARY.md` for the findings, `METHOD.md`
  for how the two sides were held to identical work, `results.txt` for the raw output, and `harness/` for
  the sources, so the measurement can be rebuilt from what is committed.

| Step | Milestone | Status | What it did |
|---|---|---|---|
| [`001-dbf-open-and-header`](001-dbf-open-and-header/) | `DBF-READ` | **done**, amended 2026-08-10 | Open a DBF (+ companion FPT) and expose its metadata: header, stored descriptors, resolved field table, resolved code page. 224 tests at close, 341 after the amendment; gate green on all seven corpus tables. The amendment fixed the code-page map, which was wrong for 22 of the 26 marks and had no marked table to prove it (ADR-18, ADR-19, ADR-20) |
| [`002-dbf-records-and-fields`](002-dbf-records-and-fields/) | `DBF-READ` | **done** 2026-08-11 | Position on a record and read every ordinary field: `Go`/`Top`/`Bottom`/`Skip`, the four cursor states, per-type decode, text under the table's code page, null and deleted flags. 630 tests at close, 252 golden; gate green on every record of all seven corpus tables, and the offset formula mutation-checked. Closed the numeric question — `double.Parse` matches `c4atod` bit for bit on all 224 values (ADR-21, ADR-22). Memo payloads and the binary-marked types `X`/`G`/`Z` are step 003 |
| [`003-memo-and-binary-types`](003-memo-and-binary-types/) | `DBF-READ` | **done** 2026-08-11 | Read the payload behind a memo reference in both encodings — four-byte binary and ten-byte ASCII — plus the types `X`, `G` and `Z`. 699 tests at close, 268 golden; **the gate now asserts every field of every record with nothing skipped**, which completes `DBF-READ` for reading. Closed `FPT-MEMO.md`'s first open question from the corpus. Compressed entries refused pending a case that can gate them (ADR-23, open) |
| [`004-cdx-tags-and-traversal`](004-cdx-tags-and-traversal/) | `CDX-READ` + `CORPUS` | **done** 2026-08-12 | Gave the corpus its first four index files — ten tags for key shapes, a three-level tree, machine beside `GENERAL` over one field, and a single-tag `.IDX` derived from a one-tag `.cdx` — then read them: tag directory, tag headers, interior nodes, **bit-packed leaves**, both traversal directions. 900 tests at close, 385 golden; **22 tags, 3364 keys, 155 blocks and 3425 block entries** asserted against the C library's own view, mutation-checked five ways. Closed `KEY-COLLATION.md` §3.7's source-only caveat and found two defects in the reference implementation. Internal only; seek is step 005 (ADR-24 to ADR-27) |
| [`005-cdx-seek`](005-cdx-seek/) | `CDX-READ` | **done** 2026-08-12 | The seek family, by key bytes so no collation table is needed: `Seek`, `SeekAtOrBefore`, `SeekLast`, `SeekNext`, `SeekPrevious`, plus exact key-and-record positioning. 972 tests at close, 421 golden; **206 recorded search cases, 104 seek-next runs and 3364 exact-pair assertions**, mutation-checked eight ways. `SeekAtOrBefore` became the primitive the backwards trio is built on. **The corpus overturned the spec's partial-seek pseudocode**: a search value with trailing pad stands for a whole key and its pad bytes compare, without it is a prefix — now witnessed in `CDX-FORMAT.md` §7, along with the all-0xFF descending branch and the two API levels' disagreement about an empty value. Also fixed a latent 004 bug: stepping back from end of file skipped the last entry |
| [`006-tags-on-a-table`](006-tags-on-a-table/) | `CDX-READ` | **done** 2026-08-12 | Gave the index a table: the production `.cdx` opens with the table when byte 28 declares one, `Table.Tags` lists its tags, and a selected tag becomes the cursor's order — with an explicit `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed` four over the same implementation. 1039 tests at close, 453 golden; **3364 records reached through 18 tags, every field of every one checked against the table's own record dump**, both surfaces walked over the whole corpus, and mutation-checked four ways. The pad byte is now **derived** from the field descriptors and matches the C library's `pChar` for all 18 tags (ADR-28), so `EXPR` is left owing only a composite expression. Reading `d4skip`'s tag path closely settled the end cases, which are now `CDX-FORMAT.md` §7.1 (ADR-29) and one refusal rather than a wrong answer (ADR-30) |
| [`007-audit-glm`](007-audit-glm/) | — *(audit)* | **done** 2026-08-13 | An independent audit of steps 002–005, its triage, and the remediation it prompted. 1054 tests at close, 453 golden; **no golden expectation changed and no corpus file was touched**, which is the gate for a remediation. Five real defects fixed — `'7'` removed (ADR-32), signed `MemoFileHeader.BlockSize`, a bounded leaf chain so a cycle of empty blocks cannot hang, `LeafGeometry` bounding its mask widths, and `SeekFirstAtOrAbove` stepping over an empty leaf. Three of the audit's own claims overturned, including **`FoxDate`'s year zero, which was right all along** while the C library's own comment was wrong (ADR-33). Opened no capability; every remaining finding given a home in `REMEDIATION-PLAN.md` §5 |
| [`008-seek-by-value`](008-seek-by-value/) | `COLLATION` + `CDX-READ` | **done** 2026-08-14 | Turned a value into key bytes, and gave a table the seek family: `Seek`, `SeekPrefix`, `SeekAtOrAfter`, `SeekAtOrBefore`, `SeekNext`, `SeekPrevious` — one method per behaviour, so no repositioning hides behind a status code and no meaning depends on trailing whitespace. Ported the `COLL4ARR.C` weight tables verbatim, every numeric transform, the `GENERAL` head-and-tail key, and the empirical `flags4dateTime` bitmap with a new corpus case (`CDXTIME`, 256 datetimes) built to check the copy. 1174 tests at close, 526 golden; **3559 keys rebuilt from the values that produced them**, every character tag sought end to end, and `Synchronize` rewritten from an O(n) walk into a descent — which turned out not to need `EXPR` after all, because ADR-28 already limits a selectable tag to a bare field name. **`COLLATION` done, risk R2 retired**, and the audit's collation/code-page finding closed with it |
| [`009-performance-audit`](009-performance-audit/) | — *(audit)* | **done** 2026-08-19 | The first measurement of anything in this port: 10 000 seeks over a 10 000-record table, run through the reference C library and through `CodeBase.Net`, against the same file and the same query sets — with matching per-record checksums proving both did identical work. **No change to `net/`.** Like-for-like the port is **1.36–1.43× the C's time per seek**, and it walks a tag **2.45× faster**, which removes suspect P4. The finding that reorders the rest: the C library's block cache — off unless `code4optStart` is called — is worth **14×**, far more than everything else on the suspect list combined, so **P1 dominates**. P2 confirmed and quantified at **~3 956 bytes allocated per seek**, though not attributed between the two candidates. P6 and P7 not exercised, and said so. **`ANALYSIS.md` then decomposed the 14×**: it is read syscalls at a measured 1.18–1.21 µs each and nothing else, against a hash-and-`memcpy` hit at 0.002 µs — and the same win is available here, **11.6× on a seek and 23.4× on a walk**, measured by giving the port a perfect cache. With I/O out of both sides the port's own CPU work is **1.4–1.7× the C's and 1.7× faster on a walk**, and the residual is P2: a fresh `byte[keyLength]` materialised for every comparison, worth 1.7–1.9× of the descent to remove. Also closed finding 6 (entries per leaf, and both leaf searches are linear), showed P7 changes nothing on unique keys, and carries a **retraction of its own first pass**, whose RAM-backed numbers never left tier-0 JIT. **Remediation designed and planned** in `DESIGN.md` / `PLAN.md` — five sub-steps, stoppable after three — with the cache decorating `IRandomAccessSource` rather than the index (a walk's reads are 96% DBF) and **optional on the reference's own three-valued policy**, `Off`/`WhenExclusive`/`Always`, defaulting to the C's `OPT4EXCLUSIVE`. Not executed |
