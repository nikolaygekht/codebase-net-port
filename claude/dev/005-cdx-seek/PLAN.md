# 005-cdx-seek — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | `KeySearch`'s effective length and its `Matches`: trailing pad stripped, all-pad falling back to the original length, a value longer than the key clamped, a prefix matching and a near-miss not; **`KeyIncrement`**: the last byte below 0xFF raised and the 0xFF tail after it zeroed, a value of all 0xFF having no successor; `BranchBlock.Seek` binary search; `LeafBlock.Seek` scan including a hit inside a run of equal keys | The boundaries of one block, exhaustively: seeking every key of a block, plus the value just below and just above each — which no dump case list would enumerate |
| 2 Component | All five operations over hand-built images: a hit in the only leaf, a hit two levels down, `After` on the first greater key, `Eof` past everything, a descending tag, `SeekExact` inside a run, a **run of equal keys crossing a leaf boundary walked forwards with `SeekNext` and backwards with `SeekLast` then `SeekPrevious`**, and `SeekAtOrBefore` landing on the entry before a value that is absent — including a value below every key, which is `Bof` | The descent, the descending inversion, and the run walk as wholes. A block-level test cannot show that a run spanning two blocks is followed across the boundary in either direction, nor that stepping back from the first entry of a block reaches the previous block rather than reporting nothing |
| 3 Fault injection | A branch whose entries are not ordered; a leaf whose scan would run past its entry array; a search on a tag whose root is a leaf with no keys; `SeekNext` and `SeekPrevious` on a cursor that was never positioned; an unbounded `Next` from a cursor a seek left past the end | That a corrupt tree makes a seek **refuse** rather than answer, and that the run operations on an unpositioned cursor behave as the C does (degrade to a seek) rather than throwing |
| 4 Golden / corpus | The `[seeks]` section for every tag of all five corpus index files, and the `[seeknext]` section for the ten tags whose key transform is the identity: result codes, landing entries, and the whole record sequence a `SeekNext` run visits | Whether our seek agrees with the reference implementation's, which is the only thing that settles the special cases and the degrade-to-seek rule |
| 4b Property | `SeekAtOrBefore`, `SeekLast` and `SeekPrevious` against 004's recorded `[keys]` sequence, for all 18 tags and every search case — plus the **adjacency** property that ties `SeekAtOrBefore` to the reference-gated `Seek` | These three have **no counterpart in the C library** (Decision 9), so there is nothing to compare against — only a definition to hold them to. Kept as its own row so the difference stays visible, and the adjacency property is what keeps it from resting on that definition alone |

**Corpus coverage.** Ten derived search cases per tag over 18 tags — roughly 170 assertions, each
pinning a result code *and* a landing entry. Plus `[seeknext]` runs over the ten identity-transform
tags, whose record sequences cover the duplicate-heavy tags `T_DUP` (32 keys over 5 values), `D_DUP`
(600 over 10, with runs crossing leaf boundaries) and `T_UNIQ` (where every run has length one). Plus
the properties of Decisions 8 and 9, run for every case against 004's key walk.

Touched but **uncovered by any corpus case**, to be listed as ungated in `SUMMARY.md`:

- **A seek on a `CBnnnnn`-collated tag** — refused at open since 004, so unreachable.
- **`SeekNext` on a numeric, date or currency tag gated against the reference.** Driving `d4seekNext`
  needs a value the library will transform, which is 007's work; here those tags get the property check
  of Decision 9 only. Named because it is the one place the two halves of this step have different
  strength.
- **`SeekAtOrBefore`, `SeekLast` and `SeekPrevious` against reference bytes**, which cannot exist: the C
  library has no such operations (Decision 9). What stands in their place is the property table there,
  and the adjacency tie to `Seek`.
- **A range walk as an operation**, deliberately left as the composition of the two bounds until
  `QUERY` has a caller for it (Decision 14).
- **A seek that races a writer** — `tfile4outOfDate`'s retry, which belongs to `LOCKING`.
- **A key longer than 240 bytes**, and a **non-512 block size**, both refused at open.
- Whatever the ten derived cases turn out not to reach per tag, which sub-step 2 reads off the dump
  rather than assuming — Q1 and Q3 are exactly this, and both are settled by looking.

**Expected values.** Result codes, landing records and landing keys come from the `[seeks]` section the
C library wrote; `SeekNext` record sequences from `[seeknext]`, likewise written by it. `SeekLast` and
`SeekPrevious` are held to a definition rather than to recorded bytes, and the definition is checked
against 004's `[keys]` — which the C library also wrote, so the *data* is still the reference's even
where the *operation* is ours. Search values are **input**, derived by the generator from each tag's own
keys rather than typed in. Hand-built images in layers 1-3 are input, never expectation.

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **The `[seeks]` section in the generator**: derive the ten search values per tag, call `tfile4seek`, record the result and the landing entry | The section for `CDXBASE`, read once by a human end to end: *the exact-key cases return 0 and land on themselves*; *the prefix case returns 0 on a character tag*; *the after-everything case returns 3*; *`T_TEXTD`'s cases are inverted with respect to `T_TEXT`'s*. Regeneration stays byte-identical, and the existing `[keys]`/`[blocks]` sections must not move |
| 2 | **Read the coverage off the dump** and close Q1 and Q3 | Grep the new section: does any `T_BIN` case land through the sub-pad-byte path, and does `T_TEXTD` exercise the step-back-into-the-previous-block case? Add the named extra cases if not, and say in `SUMMARY.md` which ones were added and why |
| 3 | **`KeySearch` and the two block searches** | Layer 1: *trailing pad is stripped and an all-pad value keeps its length*; *a search longer than the key is clamped* (Q2); *the branch search lands on the first entry not less than the value*; *the leaf scan finds the **first** of a run of equal keys*; *seeking every key of a block finds that key, and the value between any two lands on the later one*. **Mutation check:** comparing over the full key length instead of the search length must break every prefix case |
| 4 | **`TagCursor.Seek` and the descent** | Layer 2: *a hit two levels down positions on the key and reports `Found`*; *a value between two keys reports `After` and leaves the cursor on the greater one*; *a value above everything reports `Eof`*; *a value below everything lands on the first key*. Layer 3: *an unordered branch is refused*; *a scan that would leave its block is refused* |
| 5 | **Descending seek** | Layer 2: *seeking the greatest key of a descending tag finds it and positions at the tag's top*; *a value between two keys lands on the greater in the tag's own order*; *a value above everything is the tag's beginning, not its end*; *landing on entry 0 steps into the previous block*. **Mutation check:** removing the increment must break the descending cases and no others |
| 6 | **`SeekExact(key, record)`** | Layer 2: *an exact pair inside a run of equal keys is found*; *a record number below the run's first reports the first*; *one above the run's last moves past it*. **Golden:** *for every key of every corpus tag, seeking that exact pair lands on it* — which is 3364 assertions from data the corpus already holds |
| 7 | **`KeyIncrement` and `SeekAtOrBefore`**, the primitive the rest are built on | Layer 1: *the increment raises the last byte below 0xFF and zeroes the tail after it*; *an all-0xFF value has no successor and says so*. Layer 2: *a value equal to a key lands on the **last** entry of its run*; *a value between two keys lands on the lesser*; *a value below every key reports `Bof`*; *a value above every key lands on the last entry of the tag*; *on `T_TEXTD` all of that runs in the tag's order* (Decision 11). **Property:** for every search case, `SeekAtOrBefore` and `Seek` land on adjacent entries when the value is absent and bracket the run when it is present (Decision 9) |
| 8 | **`SeekLast`, `SeekNext` and `SeekPrevious`**, and the two stopping rules side by side | Layer 2: *`SeekLast` agrees with a forward walk of the run, which is the independent cross-check of Decision 10*; *`SeekLast` reports `NoEntry` where `SeekAtOrBefore` reports `Before`, on the same search value* — the one-comparison difference between them; *`Seek` then repeated `SeekNext` visits a run once each and then reports `NoEntry`*; *`SeekLast` then repeated `SeekPrevious` gives the same run reversed*; *`SeekNext` on a non-matching current entry degrades to a plain seek*; and the pairing of Decision 13: *from the same position, `SeekNext` reports `NoEntry` at a run's edge where `Next` steps out of the run and keeps going*, which is the one test that says the two are not the same operation. **Golden:** the `[seeknext]` sequences for the ten identity-transform tags. **Property:** the rest of Decision 9's table over all 18 tags |
| 9 | **Seeking and traversing compose** (Decision 13) | Layer 2: *continuing forward with `Next` from an `After` landing walks the rest of the tag*; *`Previous` from an `Eof` landing re-enters at the last entry and `Next` from a `Bof` landing at the first*; *a `Skip` of several from a seek landing moves as far as the tag allows*. **Property, over 004's `[keys]`:** for every search case, `Seek(value)` followed by `Next` to exhaustion yields exactly the tail of the recorded sequence from the landing entry onwards — and `SeekAtOrBefore(value)` followed by `Previous` to exhaustion yields exactly the head of it, reversed. That is a whole-tag assertion per case rather than a single landing, and it is what makes index-order traversal from an arbitrary starting point trustworthy |
| 10 | **The gate**: extend `CorpusIndexDump` and the golden suite | The gate below, plus Decision 8's property: for every seek case, the landing entry is the first in 004's recorded walk that is not less than the search value. **Mutation checks:** the branch search returning the last-not-greater entry instead of the first-not-less; the leaf scan starting from entry 1; `SeekLast` returning the first of a run instead of the last (which must fail `T_DUP` and `D_DUP` and nothing else); `SeekNext` comparing over the full key length instead of the search length; **`SeekAtOrBefore` omitting the step-back**, which makes it `Seek` and must therefore fail the adjacency property on every case while leaving every reference-gated assertion green — the sharpest illustration of why that property exists |
| 11 | **Documents**: `CDX-FORMAT.md` §7 gains the `d4seekNext` three-step rule and whatever the descending tail cases turn out to be; `net/corpus/README.md` and the generator README gain both new sections; `PORTING-PLAN.md` §5 marks `CDX-READ` complete except for the pad byte | `SUMMARY.md` lists the ungated paths verbatim — above all which operations are gated against the reference and which against a property — and `claude/dev/README.md` and the root `STATE.md` say what shipped |

## Gate

```
dotnet test net/CodeBase.Net.sln
```

green, with the index golden suite asserting:

- **for every search case of every tag of every corpus index file** — the result code, the landing
  record number, the landing key bytes, and the end-of-file state;
- **for every key of every tag** — that `SeekExact` on that exact pair finds it (3364 of them);
- **for the ten identity-transform tags** — that a `Seek`-then-`SeekNext` run visits exactly the record
  sequence `[seeknext]` records, including the runs that cross a leaf boundary in `D_DUP`;
- **for all 18 tags** — Decision 9's property table: `SeekAtOrBefore` lands on the last entry of 004's
  recorded key sequence that is not greater than the search value and reports `Bof` when there is none;
  `SeekLast` lands on the last that still matches; `SeekLast` followed by `SeekPrevious` to exhaustion is
  the reverse of `Seek` followed by `SeekNext` to exhaustion;
- **and for every case** — that `Seek` and `SeekAtOrBefore` land on adjacent entries where the value is
  absent, and bracket the run where it is present, which is what ties the added operations to the gated
  one;
- **and, for every case, that seeking and traversing compose** — `Seek` then `Next` to exhaustion is the
  recorded key sequence from the landing entry onwards, and `SeekAtOrBefore` then `Previous` to
  exhaustion is the part before it, reversed (Decision 13).

Counted arithmetically as the 004 gate is: cases compared must equal the cases the dumps hold, and be
non-zero.

## Risks

| Risk | Why it would make this step *wrong* | Cheapest early exposure |
|---|---|---|
| **A seek lands one entry off** | It does not throw, it answers — with a neighbouring record. Every range the optimizer builds on it is then off by one row at one end | Sub-step 3's exhaustive per-block test (every key, and between every pair), before any tree is involved |
| **The search value is compared over the full key length** | Exact seeks all pass and every partial seek fails, so a suite without partial cases would look healthy | The named mutation in sub-step 3, and the prefix case exists for every tag |
| **Trailing pad is stripped from the *stored* key instead of the search value** | Symmetrical-looking and wrong: it breaks keys whose real data ends in the pad byte, which `T_BIN` holds deliberately | `T_BIN`'s cases in the gate, and Q3 checks they reach it |
| **Descending seek adjusts instead of inverting** | Off by one at every equal run, and wrong for prefixes in a way that looks right for exact hits | Sub-step 5's mutation: dropping the increment must fail descending cases only |
| **A corrupt tree answers a seek** | The failure mode this subsystem cannot afford, and the one a valid corpus cannot provoke | Layer 3 in sub-step 4, written before the golden cases exist to be reassured by |
| **The new dump sections move the old ones** | 004's gate silently re-baselines, and every key assertion becomes a comparison against whatever we now write | Sub-step 1 diffs the regenerated `[keys]` and `[blocks]` sections against the checked-in ones and requires zero change |
| **`SeekLast` returns the first of a run rather than the last** | Backwards iteration then yields one entry and stops, which looks like "no duplicates" rather than like a defect — and no reference bytes exist to contradict it | The named mutation in sub-step 8, whose blast radius must be exactly the two duplicate-heavy tags; and the two independent implementations of Decision 10 cross-checked against each other |
| **`SeekAtOrBefore` and `SeekLast` are conflated** | They agree on every value that *is* present and differ only where it is absent — one reports the predecessor, the other reports nothing — so a suite whose cases all hit would never tell them apart | Sub-step 8 asserts the difference on the same search value, and the "between two keys" and "below everything" cases exist for every tag |
| **The two stopping rules get conflated** | A `Next` that quietly respected the last search value, or a `SeekNext` that ran past a run's edge, would each look correct in the operation's own tests and break the *other* use — a range scan that stops early, or a duplicate walk that never stops | Sub-step 8 asserts both from the same position, and sub-step 9's whole-tag property fails immediately if `Next` ever declines to leave a matching run |
| **The five operations are not all in the tag's order** | Two defined in tag order and three in byte order looks right on an ascending tag and inverts on a descending one, where only a range query would notice | Decision 11, and every one of `T_TEXTD`'s cases running in the tag's order in sub-steps 7 and 8 |
