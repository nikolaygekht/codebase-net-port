# 005-cdx-seek — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** done
**Current sub-step:** none — closed, see [`SUMMARY.md`](SUMMARY.md)

## Done so far

- [x] phases 1-5 — `DESIGN.md` and `PLAN.md` written
- [x] step 1 — the `[seeks]` **and `[seeknext]`** dump sections. **206 seek cases** and **104
      seek-next runs visiting 1003 records**, over 11 identity-transform tags of 18. Both sections were
      written in one generator pass rather than two (a sequencing deviation from the plan, which had
      `[seeknext]` arriving with sub-step 8): same file, same build, same regeneration, and the
      "did the old sections move" check then runs once instead of twice. **They did not move** — every
      pre-existing line of all sixteen dumps is preserved in order, insertions only
- [x] step 2 — coverage read off the dump; Q1 and Q3 closed, and one gap closed with them
- [x] step 3 — `KeySearch` (which absorbed `KeyIncrement`) and the two block searches
- [x] step 4 — `TagCursor.Seek` and the descent
- [x] step 5 — descending seek
- [x] step 6 — `SeekExact(key, record)`
- [x] step 7 — `SeekAtOrBefore`, the primitive
- [x] step 8 — `SeekLast`, `SeekNext` and `SeekPrevious`
- [x] step 9 — seeking and traversing compose
- [x] step 10 — the gate, with eight mutation checks
- [x] step 11 — documents

## What sub-step 2 found

Four things, all from reading the generated cases rather than from the source:

- **Q3: yes, `T_BIN` reaches the sub-pad-byte path.** Its `below-all` case — a single `0x00` byte —
  *found* the key `\x00\x01\x02…` with result 0, a prefix match on a byte below the pad character, and
  its `all-pad` case found the all-blank key. Both are the handling `b4leafSeek` has explicit code for.
- **Q1 exposed a real gap, now closed.** The only descending tag was `T_TEXTD`, which is single-block, so
  a descending seek's step back *into the previous block* was unreachable — a named risk of this step.
  `CDXDEEP`'s `D_PFX` is now descending, which costs nothing (keys are stored ascending either way) and
  makes it the corpus's only multi-block descending tag. **Byte-level confirmation came free:** the whole
  `CDXDEEP.cdx` differs from before in **exactly one byte**, offset 2550, which is `D_PFX`'s `descending`
  short — so `CDX-FORMAT.md` §7's "physically stored ascending, the flag only inverts traversal" is now
  witnessed rather than sourced.
- **An all-`0xFF` search on a descending tag returns `r4eof`, not the greatest key.** `T_TEXTD`'s
  `above-all` case proves it: the key cannot be incremented, so `tfile4seekDescendKey` sets no flag and
  the seek takes its "otherwise want an eof type condition" branch (I4TAG.C:2341-2350). A port that
  reasoned "above everything descending means the beginning" would be wrong, and would look right on
  every other case.
- **The same search value can mean two different things at the two API levels.** A zero-length search at
  the tag level is the all-blank prefix path and returns 0; through the data file's API it means
  *seek for .NULL.* and is converted to a full-length key of zero bytes
  (`data4seekConvertKeyToTagFormat`, D4SEEK.C:951-956), which finds nothing. Visible in the dump as
  `empty … rc=0` beside `empty … seek=2`. The internal operation this step ports follows the tag level;
  007's public `Seek` has to decide about the null convention, and now knows it exists.

Also settled by looking: `tfile4seek` can return `r4after` **with the cursor past the end** (`T_DUP`'s
`above-all`), while the data-file wrapper normalises that to `r4eof`. So `SeekOutcome` must be decided
from the pair `(result code, at end)` and not from the code alone — the dump records both.

## What the implementation found

Five things the design did not predict, each caught by a failing gate rather than by reading:

- **A search value means one of two things, and the corpus decides which.** With no trailing pad it is a
  prefix; with trailing pad it stands for the whole key and its pad bytes take part in the comparison.
  The second is why `"AB      "` does not match the stored key `"AB\x00…"` — a NUL is below a space —
  and a pure-prefix reading of the specification's pseudocode gets it wrong while passing every
  exact-length case. Now in `CDX-FORMAT.md` §7, witnessed rather than argued.
- **The increment of a padded value lands on its last pad byte**, so the successor of `"MIDDLE      "` is
  `"MIDDLE     !"`. Incrementing the content to `"MIDDLF"` steps over `"MIDDLE-EARTH"`, which the padded
  value sorts below — one mutation, seven failures.
- **The record-number tie-break inverts on a descending tag.** A run is walked from the highest record
  number down, so `SeekExact` testing "have I passed it" the ascending way gives up on the first entry of
  every descending run. Blast radius of the mutation: exactly one file.
- **A latent bug from step 004**, found by a seek test: stepping back from an end-of-file landing moved
  *past* the last entry instead of onto it. Traversal never noticed because a walk that stops at the end
  never steps back into the tag. Fixed for both ends, and the rule is the same one the record cursor
  already follows (d4skip.c:1197-1202).
- **One of my own tests asserted a tautology.** The adjacency check compared two expectations computed
  from the dump rather than the two cursor landings, so making `SeekAtOrBefore` identical to `Seek` left
  it green. Caught by running that mutation; the test now compares where the cursors actually land, and
  the mutation fails it.

## Notes for the next session

Nothing outstanding. The three findings a future session would otherwise rediscover are in
`CDX-FORMAT.md` §7: what a search value means with and without trailing pad, the all-0xFF descending
branch, and the two API levels disagreeing about a zero-length value.

## Deviations from the plan

- **`[seeknext]` was generated in sub-step 1** rather than with sub-step 8, so the corpus was regenerated
  once instead of twice and the "did the old sections move" check ran once. Recorded in the sub-step list.
- **`KeyIncrement` did not become its own entity.** It is three lines over the value a `KeySearch` already
  holds, and it needs that value's padding rule to know *what* to increment — a separate class would have
  had to be handed both. `KeySearch.TryIncrement` is where it lives.
- **`D_PFX` became descending** (sub-step 2), which the design did not ask for. It closed a named risk
  that no corpus case could otherwise reach, and cost one byte of `CDXDEEP.cdx`.
- **One test was rewritten after a mutation exposed it as vacuous** — the adjacency check. Recorded above
  under what the implementation found, because a test that cannot fail is worse than no test.

## Blockers

None. 004 supplies everything this step reads.
