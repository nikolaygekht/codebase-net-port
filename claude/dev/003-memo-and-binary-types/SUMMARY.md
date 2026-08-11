# 003-memo-and-binary-types — summary

**Closed:** 2026-08-11. **Gate passed.** Capability advanced: `DBF-READ` — **complete for reading.**
Nothing in the dump is unasserted any more.

No commit hash here, for the same reason the root `STATE.md` header carries none: a file cannot name
the commit it is part of. `git log` over this folder is the record.

## What shipped

Memo payloads in both reference encodings, and the binary types. **699 tests green**, up from 630:
431 unit and component, 268 golden.

```csharp
FieldDefinition notes = table.Fields["NOTES"];

table.GetMemoLength(notes);   // int    — 0 when the record has no memo
table.GetMemoBytes(notes);    // byte[] — the payload, verbatim
table.GetMemoString(notes);   // string — decoded with the table's code page (ADR-21)
table.GetMemoBlock(notes);    // int    — the stored block number, for diagnostics
table.GetMemoType(notes);     // MemoType
```

**Library:** `MemoReference` (both encodings), `MemoBlockHeader`, `MemoType` (public),
`MemoEntry`, `MemoReader` (offsets and the corruption guards), the `M`/`X`/`G`/`Z` rows in
`FieldValueDecoder`, and the five accessors above on `Table`. `GetString` now refuses a binary field.

**Tests:** `MemoReferenceTests`, `MemoReaderTests`, `MemoGoldenTests`, plus the memo branch of
`RecordGoldenTests` — which no longer skips anything.

## Deviations from the design

- **`BlankRecord`'s rule was wrong, and a test written for this step caught it.** A blank memo
  reference is decided by the reference *width*, not by the type letter: four bytes blank to zeros,
  ten to spaces, because the blank has to read back as "no memo" in whichever encoding is in use.
  Step 002 had keyed it off `f4blank`'s type list, which puts `M` and `G` in the spaces group — so a
  four-byte memo field at end of file read back as block 538976288 (`0x20202020`). The corpus is
  unambiguous: every empty reference in the Visual FoxPro tables is four zero bytes and every one in
  `F2XMEMO` is ten spaces, and `FPT-MEMO.md` §3.4 already said exactly that. The spec was right and
  the code was wrong.
- **`GetMemoType` was kept** (Q1). It costs one accessor, and Decision 9 needs somewhere for an
  unknown type to surface.
- **The gate's skip counter is gone rather than zeroed.** Step 002 asserted *asserted + skipped =
  field count*; with nothing skipped, a variable that is always zero is theatre. It now asserts
  *asserted = field count*, which is the same guarantee stated honestly.

## What this step proved

Backed by a passing corpus assertion over the five tables with an `.fpt`:

- **Every memo of every record** — 224 values, **153 non-empty** — its block number, its length, its
  type, and every payload byte.
- **Both reference encodings end to end.** Four-byte binary in four tables; ten-byte right-aligned
  ASCII in `F2XMEMO`, whose padding closed `FPT-MEMO.md`'s first open question (`c4ltoa45`'s body is
  absent, so the corpus settled it).
- **Memo text decodes under a marked code page**, and a payload whose *length* cuts a GBK character
  in half yields its whole characters plus U+FFFD — the ADR-21 rule witnessed on the FPT path, not
  only at a field boundary.
- **A memo assigned then marked null comes back not null with its contents** (`VFPNULL` record 7).
- **`BINCHAR`** — the one ordinary field step 002 skipped — matches the dump in all 32 records.
- **Mutation checked**, each against the five memo tables: payload read from the block start instead
  of past the header (5 fail), the header read little-endian (5), `numChars` treated as including
  the header (5), and the ten-byte reference parsed as binary (1 — exactly `F2XMEMO`). The blast
  radius matching the tables at risk is itself evidence the tests target the right thing.

## Deferred

- **Writing, allocation and compaction** — `WRITE`. Nothing here allocates.
- **Compressed entries (type 3)** — refused, **ADR-23**, which is `open`. The stream format is
  resolved (zlib-wrapped, 4-byte length prefix); what is missing is a corpus case, and being a
  CodeBase-only feature is a reason to support it rather than to skip it. Its own step.

**Ungated — no corpus case exists:**

- **A block size other than 512**, including zero, which means byte granularity. Component tests
  only.
- **Entry types 0, 2 and 3.** All 153 entries are text.
- **A payload spanning more than two blocks.** 505 bytes is the longest, crossing one boundary;
  three- and five-block payloads are component tests only.
- **A `G` field with much in it** — four non-empty values, longest 24 bytes.

## For the next step

- **`DBF-READ` is done for reading.** The next capability with a corpus behind it is `CDX-READ`, and
  the corpus has **no indexed case at all** — that is the gap to close first (`PORTING-PLAN.md`
  §6.3), with multi-level trees the part that matters.
- **`FieldValueDecoder` now holds every type rule**, memo and binary included; it is the one place a
  new type joins.
- **`MemoReader` is read-only by construction** — it takes an `IRandomAccessSource` and never writes.
  The write path will want the header's `nextBlock`, which `MemoFileHeader` already parses.
