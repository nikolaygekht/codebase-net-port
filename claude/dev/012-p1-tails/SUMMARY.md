# 012-p1-tails — summary

Written when the step closes. **Assume this is the only file from this folder anyone reads again.**

**Outcome:** completed  ·  **Gate:** passed — Release 2622 unit, 1133 golden; every regeneration
changed only the files `DESIGN.md` §Corpus changes declares  ·  **Commits:** `33ca4fe`, `d7cc5bf`,
`31309de`, `af076aa`, `79c2041`, `7852912`, `4530ecc`, and the closing commit
**Capability advanced:** `DBF-READ`, `CDX-READ`, `EXPR`, `QUERY`, `CORPUS` — tails closed, no status
change for the first four; `CORPUS` to 18 cases, 10 of them indexed (`PORTING-PLAN.md` §5).

## What shipped

The P1 capabilities' owed tails, each with a witness it did not have, plus five sub-steps the user
added as findings came in.

- **Compressed memo entries read** (`MemoReader`, type 3, zlib via `ZLibStream`; ADR-55) and a
  compressed-data table refused at open (ADR-56). Case `CMPMEMO`; the generator builds with zlib
  1.3.1 vendored.
- **`Seek(string)` only on character tags** (ADR-58): a numeric, date, integer or logical tag refuses
  text instead of seeking the key for zero.
- **The engine's table list is thread-safe**; the timed tests run alone and the suite in Release
  (ADR-57).
- **The query refusals isolated** by case `QRYGATE`, and the finding that the reference **uses**
  GENERAL tags and answers wrongly, its refusal compiled out; the port keeps refusing (ADR-59).
- **`.NOT.` of a null is false and null**; the reference's sticky `isNull` witnessed by a second,
  fresh-per-record evaluation and not replicated (ADR-61).
- **A tag's expression is resolved for its tag** (`TagContext`): a bare memo at 240, under GENERAL at
  half that (key 240, nullable 239); `ASCEND`/`DESCEND` of a string widened, collated inside the
  conversion and stored uncollated. A tag whose whole expression is such a conversion is sought
  through it, machine and GENERAL alike (ADR-64; new `ConversionSearch`).
- **A tag whose expression disagrees with its header's key length is refused** at first use, as the
  reference refuses the index (E84901). Case `TAGBAD` is the file the reference's `i4tagAdd` writes
  and then cannot open; the writer will follow `i4create` on every path (ADR-63, `CDX-FORMAT.md`
  §8.6). Cases `TAGCTX` and `TAGGEN` hold the tag-context tags.
- **`UPPER`/`LOWER` by the table's own code page** (`CaseMap`; ADR-65): Unicode's invariant mapping,
  same byte length or untouched, multi-byte values walked a whole character at a time; an unmarked
  table ASCII unless the host names its encoding.
- **A filter's string literals are encoded as the table's text is read** (ADR-66); an unencodable
  one refuses the filter instead of matching nothing.
- **A seek on a tag over a nullable field finds its values** — found while closing: the search
  lacked the `0x80` byte the keys begin with, so `Seek` found nothing in any such tag. And a
  candidate tag reserves no null byte, as the reference has it.
- `EXPR-ALIAS` planned as its own capability, step 014 (ADR-62); ADR-60 brought `crap4csharp` and
  `mutate4csharp` into the process.

## Deviations from the design

- Sub-step 7 ran before 2–6, and 7a, 2a, 2b, 5a, 5b and 5c were added at the user's call as findings
  came in. Each is recorded in `STATE.md` with its reason.
- Sub-step 5 found no −510 witness reachable: it waits for `EXPR-ALIAS` (step 014).
- Sub-step 5a's first shape — one table with a GENERAL tag added by `i4tagAdd` — is what found the
  `i4tagAdd` defect; it became three cases built by `i4create`, plus `TAGBAD` keeping the defect.
- Sub-step 6: Decision 1 amended (unmarked tables ASCII, not cp437); the case map planned for the
  query decomposer was dead code and was dropped; the filter-literal fix was added.
- The plan's "expected values from `.cdx.dump.txt`" for sub-step 2 did not hold (the dump records no
  key type); three reference-written sources replaced it.

## What this step proved

Backed by passing corpus assertions now:

- The reference's type-3 memo layout and its 512/513 compression threshold (`CMPMEMO`).
- The reference's optimizer refuses unique and filtered tags and **uses** GENERAL ones (`QRYGATE`).
- `.NOT.` over a null, and the reference's `isNull` carried between records on exactly two of 82
  field expressions (`CDXEXPR.expr.dump.txt`).
- Every tag-context length rule, in parsed nodes and in stored keys: memo 240 / 239, GENERAL memo
  120 (key 240) / 119 (key 239), `ASCEND`/`DESCEND` under GENERAL 40 and collated-then-complemented
  (`TAGCTX`, `TAGGEN`), and `G_ASC`'s keys equal `G_TEXT`'s byte for byte.
- The reference refuses at open a tag whose key length disagrees with its expression, and writes such
  a tag itself through `i4tagAdd` (`TAGBAD.open.dump.txt`: −310); every other corpus tag passes the
  check.
- Visual FoxPro's cp1251 `UPPER`/`LOWER` keys, byte for byte (`vfp/VFPFUNC.CDX`), and its `LOWER`
  answer above 0x7F.

Without an oracle, by decision: the seek through a `DESCEND` conversion (stored keys stand in), the
case maps of every code page but 1251 (derived from `Encoding` in layer 1), and the sticky null not
being replicated.

## Deferred

- The `WRITE` capability inherits ADR-63: one tag-creation routine following `i4create`.
- `EXPR-ALIAS` (step 014): −510 and expressions over several open tables.
- The counted query comparison: step 015.
- Seen and not acted on: `FeatureFlags.UsesLongFieldNames` refuses on any version where the reference
  reads the flags only on `0x31`. Not checked: whether a whole-key `Seek` on a memo tag finds a memo
  shorter than the key, whose stored key is padded with zeros where the search pads with blanks.
- `SearchFor` and the rest of `TableTagCursor` have no unit layer (013's F4).

## Tools

The final `crap4csharp` and `mutate4csharp` runs were made, and their numbers are the ones `STATE.md`
explains: 91 mutants killed and 8 left, every one of them accounted for; one member worse than at the
step's start, `TableTagCursor.SearchFor`, which 013 covers.

## For the next step

Step 013 cleans up what the unit layers do not cover, from `013-coverage-audit/AUDIT.md`. This step
confirms its two priorities: the evaluator, and `TableTagCursor`, where the nullable-seek defect
lived since step 010 because only a golden test could have seen it and none looked.
