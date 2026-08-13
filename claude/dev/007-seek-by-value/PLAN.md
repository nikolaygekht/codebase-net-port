# 007-seek-by-value — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md), for what [`DESIGN.md`](DESIGN.md)
designs.

**This is a large step** — five deliverables, of which two (`GENERAL` collation, and the `Synchronize`
rewrite) would each be a respectable step alone. The sub-steps below are ordered so that **the tables
and transforms land and are gated before anything depends on them**, and so the step can be stopped
after sub-step 5 with something coherent shipped if it runs long.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | Every transform against hand-picked values: `t4dblToFox` across sign, zero, **`-0.0`**, subnormals and infinities; `t4intToFox` / `t4curToFox` sign-bit flips; `t4convertSubSortCompressChar` over accents, expansions, trailing blanks, and the partial-seek tail suppression. Plus `KeySearch.Into` reusing a buffer | The byte layer exhaustively and at memory speed. A wrong key is invisible at every higher layer until it makes a seek miss, and then it looks like a seek bug |
| 2 Component | `Table` over `TableImage` with an index: each seek method's contract — exact miss leaves no record, `SeekAtOrAfter` lands and says `After`, `SeekPrefix` matches `SMITHSON`, `SeekNext` walks a duplicate run and stops; `Synchronize` after `Go(n)`; the active search cleared by a non-seek move | The **contracts**, which are this step's real product. Only here can "a failed exact seek leaves no record" be stated without a real file |
| 3 Fault injection | A seek with no tag selected; a value whose type the tag cannot take; a `GENERAL` tag on a table whose code page disagrees; a record with no entry (null field, filtered tag) reaching `Synchronize` | The refusals. Every one is a case where answering plausibly would be worse than refusing |
| 4 Golden / corpus | **Value in, stored key out** for every indexed field of all four indexed tables — the transform's output compared against the key the C library actually wrote. Then: seek every key in every tag and land on the record the dump names | Whether the transforms reproduce the reference. This is the only layer that can say so, and the corpus already holds both halves |

**Corpus coverage.** No new generator case is needed, which is the happy accident that makes this step
gateable: **every tag's stored keys already sit beside the field values they were computed from**
(`CDXBASE`, `CDXDEEP`, `CDXCOLL`, `IDXONE`). `CDXCOLL` in particular gates `GENERAL` at `keyLen` 20 and
40, with accents and the `oe` / `ss` / `th` expansions.

Two paths the corpus cannot reach, named rather than discovered later: a **code-page/collation
mismatch** (every corpus table is cp1252 or unmarked — this stays layer 3 until the `S4CODEPAGE_850`
generator case named in `PORTING-PLAN.md` §6.3 exists), and **`considerPartialSeek` against the
reference**, since driving the C library's partial-seek path needs a generator case that records one.
Sub-step 4 gates the tail suppression as an invariant instead, and says so.

**Expected values.**

| Deliverable | Expectation comes from |
|---|---|
| Transforms | The corpus: each tag's stored key bytes, beside the field value in the same dump. Never bytes typed in from `KEY-COLLATION.md` |
| `COLL4ARR` tables | Copied verbatim from `COLL4ARR.C`. **Not** re-derived, and not from `CompareInfo` — a table that disagrees shows up as a wrong key in sub-step 4 |
| Seek contracts | The promise itself, stated in layer 2. No file involved |
| `Synchronize` | An **equivalence**: the record reached after `Go(n)` then `Skip(1)` must equal what the old walk reached. The rewrite is a speed change and must be provably not an answer change |

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **The `COLL4ARR.C` tables**, ported verbatim as static byte arrays — translate and compress, nothing else | Layer 1: *the arrays have the lengths the C declares*; *spot entries match `COLL4ARR.C` by line*. No behaviour yet — this is data, and it lands alone so a table typo cannot hide inside a transform bug |
| 2 | **The arithmetic transforms** — `t4dblToFox`, `t4floatToFox`, `t4intToFox`, `t4i8ToFox`, `t4curToFox`, and the date/datetime forms that build on them | Layer 1 across sign, zero and the boundaries, with **`-0.0` asserted to produce all zeros** and to sort below every negative. **Golden:** every numeric, date, integer and currency tag's keys reproduced from the field values in the same dump |
| 3 | **`t4convertSubSortCompressChar`** — heads, tails, expansions, trailing-blank stripping, zero padding to `2 x lenIn` | Layer 1 on each rule separately. **Golden:** `CDXCOLL`'s two `GENERAL` tags, both key lengths, every key reproduced from its value. This is the step that closes E14 and risk R2 |
| 4 | **`considerPartialSeek`** — tails suppressed when `maxKeyLen > verifyLen + hasNull` | Layer 1: *a prefix search over a `GENERAL` tag omits the tails*; *a whole-key search keeps them*; *the suppression is what makes `SeekPrefix("MU")` match a key whose tail weights differ*. Ungated against the reference — named in `SUMMARY.md` as this step's weakest gate |
| 5 | **`SeekConverter` and `RecordKey`** — the `tfile4initSeekConv` selection, and `IKeyValueSource` with its one implementation | Layer 1: *every key type selects the transform the C's table names*; *a type with no transform is refused*. Layer 3: *an expression-based tag throws `NotSupported` by name* — ADR-28's refusal, now load-bearing |
| 6 | **The cursor's key buffer** — `KeySearch.Into`, and `TableTagCursor` owning one buffer sized to the tag | Layer 1: *`Into` does not copy and reuses the buffer across calls*. Layer 2: *a thousand seeks on one cursor allocate one buffer* — asserted by allocation count, which is the only way to state it |
| 7 | **`Seek` and `SeekPrefix`** (exact, both axes) | Layer 2: *a hit positions and reports Ok*; *a miss reports NoRecord and leaves the cursor on no record with the record blanked*; *`Seek("SMITH")` does not match SMITHSON and `SeekPrefix("SMITH")` does*. **Golden:** every key of every tag sought by value, landing on the record its dump names |
| 8 | **`SeekAtOrAfter` and `SeekAtOrBefore`** | Layer 2: *a hit says Found*; *a miss lands on the neighbour and says After or Before*; *past either end says Eof or Bof*; *the pair walked together covers a closed range exactly once* |
| 9 | **`SeekNext` and `SeekPrevious`**, and the active search | Layer 2: *a duplicate run is walked and stops*; *the search survives a `SeekNext` but is cleared by `Top`, `Go` or `SelectTag`*; *`SeekNext` with no active search is refused rather than guessing* |
| 10 | **Code page and collation must agree** | Layer 3: *a `GENERAL` tag on a table whose code page is not the collation's is refused when selected, naming both*; *the table's other tags still work* — the same shape ADR-28's refusal has |
| 11 | **`Synchronize` becomes O(log n)** — derive the key, `SeekExact` | Layer 2 **equivalence**: for every corpus tag, `Go(n)` then `Skip(1)` reaches the same record the walking version reached, for every `n`. Layer 3: *a record with no entry still refuses* (ADR-30). Then delete the walk — leaving it as a fallback would hide a derivation bug behind a slow correct answer |
| 12 | **Documents** | `PORTING-PLAN.md` §5 marks `COLLATION` done and `CDX-READ`'s value-seek half done; ADRs for the seek surface split and the `Synchronize` rewrite; `README.md` gains the seek example, since this is the feature the library exists for; `SUMMARY.md`, `claude/dev/README.md`, root `STATE.md` |

**Stoppable after 5.** Sub-steps 1 to 5 are the transforms and their selection, fully gated against the
corpus, with nothing public. If the step runs long that is a coherent place to stop: `COLLATION` is
closed, E14 and R2 are retired, and the public surface follows in its own step.

## Gate

```
dotnet test net/CodeBase.Net.sln
git status --porcelain net/corpus/
```

Green, **453 golden tests still passing plus the new ones, and the second command printing nothing.**

The gate that matters most is not a count: **for every tag in the corpus, the key this port computes
from a field value must equal the key the C library stored for that record** — all 3364 of them, across
22 tags, including `GENERAL`. That is what says the transforms are right, and it needs no new corpus
case because both halves are already in the dumps.

**Mutation checks** — copy aside, restore by `md5sum`, never `git checkout` while uncommitted
(`DEV_APPROACH.md` §4):

| Break | Must fail | And must leave green |
|---|---|---|
| Use `bits \| 0x8000…` instead of the byte-add in `t4dblToFox` | Exactly the `-0.0` cases | Every other numeric key |
| Drop the tail weights from the `GENERAL` transform | `CDXCOLL`'s golden keys | Every machine-collated tag |
| Suppress tails unconditionally, not only on a partial seek | The whole-key `GENERAL` seeks | The prefix ones |
| Make `Seek` behave as `SeekAtOrAfter` on a miss | The exact-miss contract test | `SeekAtOrAfter`'s own tests — proving the two are genuinely separate |
| Restore the O(n) walk in `Synchronize` | Nothing — **and that is the point.** The equivalence test must pass both ways, which is what proves the rewrite changed speed and not answers | Everything |

## Risks

| Risk | Why it would make this step *wrong* | Cheapest early exposure |
|---|---|---|
| **A `COLL4ARR` table typo** | One wrong weight is one wrong key for one character. It will not show on ASCII text and will show on the one accented name in production | Sub-step 1 lands the tables **alone**, with lengths and spot entries checked, before any transform can absorb the error. Sub-step 3's golden run over `CDXCOLL` is the real proof |
| **`-0.0`** | The naive `bits \| 0x8000…` is not bit-exact, and `-0.0` must sort below every negative. A tag containing it would order wrongly and a range would silently omit records | `CDXBASE` has a `-0.0` tag deliberately (004). It is in the golden set from sub-step 2, and it is a named mutation |
| **The build transform and the seek transform confused** | `Synchronize` reconstructs a key that exists on disk; a seek searches for one. For `GENERAL` they differ, because a partial seek suppresses tails. Using the seek form in `Synchronize` would corrupt exactly the `GENERAL` tags | They are separate methods from sub-step 5, not a flag. Sub-step 11's equivalence test runs over `CDXCOLL` |
| **`Synchronize`'s rewrite changes answers, not just speed** | It is the one change here that touches a path 006 already gated. A derivation bug would move records | The equivalence test in sub-step 11 compares against the walking version over every corpus tag, and the walk is deleted only after it passes |
| **The public surface arrives wider than it can be gated** | Four methods times eight overloads is thirty-two entry points, and the corpus can only witness the types it holds | The overload set is generated from the tag's key type, so an unused overload is a refusal rather than an untested path. Sub-step 12 checks the shipped surface against the design's list |
| **`considerPartialSeek` is ungated against the reference** | The one rule here with no corpus witness, and it changes what a `GENERAL` prefix seek matches | Named in `SUMMARY.md` as the weakest gate, tested as an invariant in sub-step 4, and listed in `PORTING-PLAN.md` §6.3 as wanting a generator case |
| **Scope: this step is large** | Five deliverables, two of them step-sized. Running long risks a half-finished public surface | Sub-steps 1 to 5 are self-contained and leave nothing public; the step is stoppable there with `COLLATION` closed |
