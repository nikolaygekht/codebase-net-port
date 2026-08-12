# 005-cdx-seek — summary

**Closed:** 2026-08-12. **Gate passed.** Capability advanced: `CDX-READ` — **seek done**, which leaves
only the `Table` wiring (006) and, for a machine-collated tag, the pad byte `EXPR` will supply.

No commit hash here, for the same reason the root `STATE.md` header carries none: a file cannot name the
commit it is part of. `git log` over this folder is the record.

## What shipped

**Finding a key instead of walking to it, in five operations.** **972 tests green**, up from 900: 551
unit, component and fault-injection, 421 golden.

```csharp
KeySearch search = KeySearch.For("SMITH"u8, tag.KeyLength, tag.PadByte);
TagCursor cursor = tag.OpenCursor();

cursor.Seek(search);            // first entry not before the value, in the tag's order
cursor.SeekAtOrBefore(search);  // last entry not after it — a range's other end
cursor.SeekLast(search);        // last entry that still matches
cursor.SeekNext(search);        // next match, or NoEntry when the run ends
cursor.SeekPrevious(search);    // previous match
cursor.SeekExact(search, 42);   // that key *and* that record number
```

Still `internal`: the public surface arrives with a `Table` behind it.

**Corpus** — two new dump sections, no new files. `[seeks]` records what `tfile4seek` returned and where
it left the cursor for **206** search values derived from the tags' own keys; `[seeknext]` records **104**
`d4seekN`-then-`d4seekNextN` runs visiting **1003** records, for the eleven tags whose key transform is
the identity. Both were added in one generator pass, and **no pre-existing line of any dump moved** —
checked as "insertions only" across all sixteen dumps, because 004's gate reads those sections.

`CDXDEEP`'s `D_PFX` became **descending**, closing the one gap sub-step 2 found: without a multi-block
descending tag, a descending seek's step back into the previous block is unreachable.

**Library** — `KeySearch` (the two comparison rules, and the increment), `SeekOutcome`,
`BranchBlock.Seek` (binary search), `LeafBlock.Seek` (forward scan), and the five operations plus the
byte-level primitives `SeekFirstAtOrAbove`/`SeekLastAtOrBelow` on `TagCursor`.

**Tests** — `KeySearchTests`, `SeekTests`, and on the golden side `SeekGoldenTests`, `SeekCorpus`,
`DumpSeekCase`, `DumpSeekNextRun`.

## What this step proved

- **206 seek cases** across all 18 tags and the four directories: result code, landing record, landing
  key, and end-of-file state, against what the reference implementation answered.
- **104 seek-next runs**, including runs that cross a leaf boundary in `D_DUP` (600 records over ten
  distinct values) and runs of length one in the unique tag.
- **3364 `SeekExact` assertions** — every key of every tag found by its exact key-and-record pair.
- **Properties for the three operations the C library does not have**, over its own recorded key
  sequence, plus the **adjacency** tie that puts them in the reference gate's shadow: where a value is
  absent, `Seek` and `SeekAtOrBefore` land on neighbouring entries; where it is present, they bracket
  the run.
- **Seeking and traversing compose**: from every one of the 206 landings, an unbounded walk yields
  exactly the rest of the recorded sequence.
- **Mutation-checked eight ways**, and the radii are the evidence:

  | Mutation | Failed | Where |
  |---|---|---|
  | Leaf scan starts at entry 1 | 43 | Everything |
  | Branch search takes last-not-greater | 27 | Everything |
  | `SeekAtOrBefore` made identical to `Seek` | 20 | The three added operations only — reference-gated assertions stay green |
  | Every value compares padded | 11 | The prefix cases, including the reference-gated `Seek` |
  | `SeekLast` returns the run's first | 7 | Only the tests that walk a run backwards |
  | Increment ignores the pad width | 7 | The padded-value cases |
  | Descending record tie-break not inverted | 1 | `SeekExact` on the one file with a descending tag |
  | Re-entry from an end steps past it | 1 | The one test that steps back from end of file |

## What the corpus overturned

**A search value means one of two things, and the specification's pseudocode suggested only one.** With
no trailing pad it is a prefix, compared over its own length — `"CUSTOMER-A"` finds
`"CUSTOMER-ACCT-0599  "` and reports a match. With trailing pad it stands for the whole key and its pad
bytes take part, so `"AB      "` does **not** match the stored key `"AB\x00\x00\x00\x00\x00\x00"`: a NUL
is below a space, so that key sorts *before* the value and the seek lands on `"AB      "` instead. A pure
prefix reading passes every exact-length case and fails these, which is exactly why it took 206 recorded
cases to find. `CDX-FORMAT.md` §7 now carries the rule, witnessed.

Three consequences, all now in the spec:

- **The increment of a padded value lands on its last pad byte.** The successor of `"MIDDLE      "` is
  `"MIDDLE     !"`, not `"MIDDLF"` — which would step over `"MIDDLE-EARTH"`, a key the padded value sorts
  below.
- **A value that cannot be incremented takes a different branch.** On a descending tag an all-`0xFF`
  search returns `r4eof` rather than the tag's first entry (I4TAG.C:2341-2350). A value merely *above*
  every key, but incrementable, does land on the first entry with `r4after`.
- **`tfile4seek` can return `r4after` with the cursor already past the end**, while `d4seek` normalises
  that to `r4eof`. A status has to be decided from the pair (code, at end).

**And the same value can mean two different things at the two API levels.** A zero-length search matches
every key at the tag level; through `d4seekN` it means *seek for .NULL.* and becomes a full-width key of
zero bytes (D4SEEK.C:951-956). The dumps carry both, side by side, and the golden test skips that one
case with the citation rather than pretending they are comparable.

## Deviations from the design

- **`[seeknext]` was generated with `[seeks]`** in one pass rather than in a later sub-step: one
  regeneration, one "did the old sections move" check.
- **`KeyIncrement` did not become its own entity.** It needs the padding rule of the value it is
  incrementing, so a separate class would have had to be handed both; it is `KeySearch.TryIncrement`.
- **`D_PFX` became descending**, which the design did not ask for and sub-step 2 showed was needed.
- **Decision 9's mutation claim was wrong and is corrected.** It predicted that removing
  `SeekAtOrBefore`'s step-back would leave every reference-gated assertion green. It does not: the
  step-back is shared with the descending `Seek`, which *is* reference-gated, so removing it fails those
  too. The isolating mutation is "make `SeekAtOrBefore` identical to `Seek`", and that one behaves as the
  decision described.
- **One test was rewritten after a mutation exposed it as vacuous.** The adjacency check compared two
  expectations computed from the dump instead of the two cursor landings, so the isolating mutation left
  it green. It now compares where the cursors actually land.
- **A latent bug in step 004's traversal was fixed here.** Stepping back from an end-of-file landing moved
  *past* the last entry rather than onto it; a walk that merely stops at the end never notices. Both ends
  now re-enter the tag, as the record cursor already does (d4skip.c:1197-1202).

## Ungated — no corpus case exists

- **`SeekNext` against the reference on a numeric, date or currency tag.** Driving `d4seekNext` needs a
  value the library will transform, which is 007's work; those tags have the property check only.
- **`SeekAtOrBefore`, `SeekLast` and `SeekPrevious` against reference bytes**, which cannot exist: the C
  library has no such operations. What stands in their place is the property table and the adjacency tie.
- **A range walk as an operation**, left as the composition of the two bounds until `QUERY` calls it.
- **A seek that races a writer** (`tfile4outOfDate`'s retry) — `LOCKING`.
- **A seek on a `CBnnnnn`-collated tag**, refused at open since 004.
- **A partial seek on a `GENERAL`-collated tag using a *value*.** Seeking `C_GEN` by key bytes is gated
  like any other tag; turning a value into those bytes needs the weight tables, and the C library's
  `considerPartialSeek` flag exists precisely because tail weights interfere with a partial match
  (D4SEEK.C:1157-1166). That is 007's problem and it is now named.

## For the next step

- **006 is next**, and unaffected by this step: navigating records in tag order needs traversal, not seek.
  Its gate joins the index dumps' key sequences to the table dumps' field values.
- **007 inherits three named questions**: the `.NULL.` convention for an empty public seek, whether
  `SeekNext`'s degrade-to-seek should survive into the public API (Q5), and `considerPartialSeek` for
  collated partial seeks.
- **`KeySearch` is where a value becomes searchable**, so it is the seam the value-to-key transforms plug
  into. Nothing in it knows about collation, which is why a collated tag seeks by bytes today.
