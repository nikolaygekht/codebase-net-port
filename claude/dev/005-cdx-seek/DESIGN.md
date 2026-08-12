# 005-cdx-seek — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

Find a key in a tag instead of walking to it, and then move between the entries that match it.
Searching is by **key bytes**, which is what makes this step gateable now — turning a *value* into key
bytes is `COLLATION`'s job and step 007's.

Five operations, because a duplicate key is the normal case and a single seek is not enough to use it —
and because a *range* needs both of its ends:

| Operation | Answers |
|---|---|
| `Seek` | the **first** entry whose key is not less than the search value |
| `SeekAtOrBefore` | the **last** entry whose key is not greater than the search value |
| `SeekLast` | the **last** entry whose key still matches the search value |
| `SeekNext` | the **next** entry that still matches, from wherever the cursor is |
| `SeekPrevious` | the **previous** entry that still matches |

`Seek` alone finds one of a run of eleven equal keys and gives no way to reach the other ten. `Seek`
with `SeekNext` walks a run forwards; `SeekLast` with `SeekPrevious` walks it backwards. And the two
bounds together are a **range**: `Seek(low)` to `SeekAtOrBefore(high)`, walked with the ordinary cursor,
is a closed interval — which is exactly the shape the bitmap optimizer's per-tag range constraints
(`CONST4`'s `ge`/`le`) will ask a tag for, and the reason `PORTING-PLAN.md` §1 calls the optimizer the
point of the library.

### Two pairs of steps, and where each one stops

Stepping from one entry to the next comes in two kinds, and both are wanted (Decision 13):

| Stepping | Stops when | Reports | Whose |
|---|---|---|---|
| `SeekNext` / `SeekPrevious` | the key **stops matching** the search value | `NoEntry` | this step, from `d4seekNext` |
| `Next` / `Previous` | the **tag** ends | `Eof` / `Bof` | step 004's traversal, already shipped |

The match-bounded pair walks a run of duplicates. The unbounded pair walks the tag, and it is what
continues a range after a seek has found its start — seek the low bound, then step until the high bound
goes past. **Both already exist**, the second since 004, so what this step adds is not a third pair of
methods but the promise that the two compose: a cursor left anywhere by a seek — on a match, on a
greater key, past the end — is a valid place to keep walking from. That promise is what makes
traversing a whole table in index order and traversing one key's duplicates the same machinery, and it
is gated as such.

The two bounds are also what the backwards operations are built from, so adding `SeekAtOrBefore`
*shrinks* this step rather than growing it: `SeekLast` becomes `SeekAtOrBefore` plus one comparison
(Decision 10), and the byte-incrementing trick both need is then written once.

**Gate:** new `[seeks]` and `[seeknext]` sections in the corpus index dumps record, for a set of
search values per tag, what `tfile4seek` returned and where it left the cursor, and what a
`d4seek`-then-`d4seekNext` run visits. For every one of those cases the C# must return the same result
and land on the same entry. `SeekAtOrBefore`, `SeekLast` and `SeekPrevious` **have no counterpart in the
C library at all** and are gated as properties over the key sequence step 004 recorded, tied back to the
reference by an adjacency check — see Decision 9, which is the one place this step cannot lean directly
on the reference implementation. The composition of seeking with traversal is gated the same way, per
case and over a whole tag (Decision 13).

**Capability:** `CDX-READ` — the half step 004 held back  ·  **Governing spec(s):**
`specs/CDX-FORMAT.md` §5.3, §7, §12

Step 004 retired the decoding risk (R1). This step is where that decode gets *used*, and its own risk
is different in kind: a seek that lands one entry off does not throw, it answers.

## Not in this step

| Deferred | To | Why |
|---|---|---|
| **Seeking by a *value*** — `Table.Seek("SMITH")`, `SeekDouble`, `SeekDate` | **007** | The value has to be transformed into key bytes first, which is `COLLATION`: `t4dblToFox` and its siblings for the numeric family, and the weight tables for a collated character key. None of that is needed to seek by bytes, and separating them keeps this step's gate free of a second subsystem |
| **Anything public** | 006 and 007 | This step extends the internal `TagCursor`. The public surface arrives with a `Table` behind it |
| **`SeekNext` from the *record's* key rather than from a given one** | 007 | `d4seekNext` re-derives the current record's key through the expression when the cursor and the tag have drifted apart (`d4seekSynchToCurrentPos`, D4SEEK.C:1141). That needs `EXPR`; the tag-level operation this step ports takes the search value from its caller instead |
| **The write-side use of a seek** — `tfile4go(…, goAdd = 1)` and the `curDupCnt` by-product the insert path relies on (b4block.c:1182) | `WRITE` | The by-product is computed here anyway; nothing reads it yet |
| **The one-second retry on an inconsistent descent** (`tfile4outOfDate`, I4TAG.C:1878-1922) | `LOCKING` | It exists for a reader racing a writer. Reads here are exclusive |

---

## What the C library actually does

Three functions, and the shape of each matters to the port.

**`b4seek` — inside one block** (b4block.c:2123-2168 for a branch, 2192-2474 for a leaf). A branch is a
plain array, so it is a **binary search** with `u4memcmp` over the first `len` bytes, leaving the
position on the first entry that is not less than the search value and returning 0 or `r4after`. A leaf
cannot be binary-searched, because a key only exists relative to the one before it — so it is a
**forward scan** that uses the duplicate counts to avoid re-comparing bytes it already knows match.

**`tfile4seek` — the whole tag** (I4TAG.C:2203-2359). Up to the root, then `b4seek` and descend until
the block is a leaf; `r4after` in a branch simply means "descend that entry". The returned code is
`0` for found, `r4after` (2) when it landed on a greater key, `r4eof` (3) when there is nothing at or
after the search value.

**Descending tags seek by inverting, not by comparing backwards** (I4TAG.C:2295-2356). The search key
is **incremented by one** (`tfile4seekDescendKey`), the ordinary ascending seek runs, and then the
cursor **steps back one entry** — because the first key *not less than* `key+1` is one past the last
key *equal to* `key`. The tail cases are the interesting part: landing on entry 0 of a block has to
step into the previous block, an exact hit goes to the top of the equal run, and running off the front
becomes `r4eof`.

**The leaf scan's three special cases** are where a port earns or loses this step:

1. **Trailing pad in the *search value* is stripped first** (`b4calcBlanks`, b4block.c:140-153), so a
   seek for `"SMITH"` in a 20-byte character tag compares five bytes, not twenty. Comparing the padded
   form would find `"SMITH"` and miss nothing — but a seek for `"SMI"` would then wrongly fail, and
   that is the whole point of a partial seek.
2. **Bytes below the pad character** get explicit handling (b4block.c:2245-2416, 2436-2461), because a
   stored key can hold a byte *smaller* than the pad byte the search value was trimmed of.
3. **An all-blank search value** compares over its original length instead
   (`allBlank`, b4block.c:2211-2216).

**And the comparison is a prefix comparison, not a full-key one.** `u4keycmpPartial` short-circuits to
`memcmp` over the *search* length for machine collation (u4util.c:2986-2987); only a collated key needs
its head-and-tail dance. So machine-collated seeks need no collation table, exactly as reading did.

**`d4seekNext` is three steps, and only the middle one is new** (`d4seekNextN`, D4SEEK.C:1053-1240):

1. **Compare the *current* entry's key against the search value** over the search length. If it does
   not match — or the cursor is at either end — `d4seekNext` degrades to a **plain seek**. So
   "seek next" on a cursor that is nowhere useful is just "seek", which makes the operation safe to
   call blind and is worth reproducing rather than tidying.
2. **Skip one entry** with the direction-aware `tfile4dskip` (D4SEEK.C:1212). On a descending tag that
   moves towards lesser keys, so "next" is the tag's next and not the file's.
3. **Compare again.** Still matching means success; no longer matching means `r4entry` (5) — "no index
   entry", a *status* and not an error — and the cursor is left where it landed
   (D4SEEK.C:1222-1238).

**There is no seek-prior in the library, and no at-or-before either.**
`grep -rniE "seekprev|seekprior|seeklast|seekback"` over the whole drop returns nothing: neither
iterating a run of duplicates backwards nor asking for the greatest key below a value is something the C
API offers. What the C *does* have is the machinery — `tfile4seekDescendKey` increments a search key and
`tfile4seek` then steps back one, which is `SeekAtOrBefore` in all but name (I4TAG.C:2295-2340). So the
three added operations are new *surface* over ported *mechanics*, and Decision 9 spells out what that
means for how they are gated.

---

## Part A — the corpus

No new tables. The four index cases already hold every key shape a seek can be aimed at; what is
missing is the **search cases**, which are a new dump section rather than new bytes.

### A.1 The `[seeks]` section

Appended to each `[tag …]` block of `<NAME>.cdx.dump.txt`, after `[keys]`, so the existing sections do
not move (ADR-16: the dump grows by optional tokens and sections, never by new columns):

```
[seeks]
  "SMITH               " 20 -> 2 rec=17 key="SMITHERS            "
  "SMITH"                5 -> 0 rec=12 key="SMITH               "
  "ZZZZ"                 4 -> 3 eof
```

Each line is: the search bytes, the search length, the result code `tfile4seek` returned, and then
where the cursor ended up — the record number and full key, or `eof`. Written from
`tfile4seek`/`tfile4key`/`tfile4recNo`/`tfile4eof`, so it is the reference implementation's answer and
not our reading of one.

### A.2 Which searches, and why those

Per tag, generated from that tag's own keys so the cases stay meaningful when the data changes — the
generator derives them rather than a human listing them:

| Case | Built as | What it catches |
|---|---|---|
| **Exact, first key** | the tag's first key, whole | the trivial hit, and that a descending tag's "first" is its greatest |
| **Exact, last key** | the tag's last key, whole | the hit at the far end of the tree |
| **Exact, a key in a middle leaf** | the key at count/2 | a hit that needs a real descent |
| **Exact, first of a run of equal keys** | the first key that repeats | that a seek lands on the *first* of an equal run, not any of them |
| **Prefix** | a middle key truncated to half its length | the partial-seek path, and that trailing pad is stripped from the search value |
| **Between two keys** | a middle key with its last significant byte incremented | `r4after`, the most common non-hit |
| **Before everything** | a single 0x00 byte | landing on the very first entry |
| **After everything** | 0xFF repeated to the key length | `r4eof` |
| **Empty search** | length 0 | the all-blank path, and a length no ordinary caller passes |
| **All pad** | the pad byte repeated | the all-blank comparison over the original length |

Ten cases across the corpus's 18 tags is roughly 180 search assertions, each of which pins a result code *and* a
landing entry.

### A.3 The `[seeknext]` section, and the one tag kind it can cover

`d4seekNext` takes a **value**, converts it through the tag's own transform and then seeks — so driving
it from the generator normally needs the transforms this step is keeping out. There is one exception,
and it is exactly the interesting one: for a **machine-collated character tag the transform is the
identity** (`t4noChangeStr`, i4init.c:583-592), so the value bytes *are* the key bytes and
`d4seekN`/`d4seekNextN` can be driven with raw byte strings.

That covers ten of the eighteen tags — `T_TEXT`, `T_TEXTD`, `T_DUP`, `T_UNIQ`, `T_BIN`, `D_WIDE`,
`D_PFX`, `D_DUP`, `C_MACH` and `IDXONE`'s tag — including **every tag that holds duplicates**, which is
where next and previous earn their place. The numeric tags' `SeekNext` is covered by the property of
Decision 9 here and gated against the reference in 007, when the transforms exist.

```
[seeknext]
  "RED       " 10 -> 1 6 11 16 21 26 31
  "BLUE      " 10 -> 3 8 13 18 23 28
  "MISSING   " 10 -> 5
```

Each line is the search bytes, the search length, and then the record numbers a `d4seekN` followed by
repeated `d4seekNextN` visits until the library stops matching. The last case shows the shape of a miss:
a plain seek lands somewhere (`r4after`) and the first `seekNext` reports `r4entry`, so only the landing
record is listed and the `->` sequence is what the *port* must reproduce entry for entry.

Note the `d4seekNextN` calls must use the **length-taking** form: `T_BIN`'s keys hold `0x00`, and
`d4seek`'s string form would stop at the first one.

Two things the generator has to be careful about, both learned in 004: **`tfile4seek` on a descending
tag mutates the caller's key buffer** (it increments it and then undoes that, I4TAG.C:2295-2340), so
each case gets a fresh copy; and **the search bytes are written escaped like every other byte string**,
because a search value is bytes and several of these are not text.

---

## Part B — the reader

### Classes

| Class | Role | Responsibility |
|---|---|---|
| `BranchBlock` (extended) | Entity | Gains `Seek(searchKey)`: binary search over the entries, returning the position of the first entry not less than the search value |
| `LeafBlock` (extended) | Entity | Gains `Seek(searchKey)`: the forward scan of `b4leafSeek`, returning the position and whether it was an exact hit |
| `SeekOutcome` | Entity | What a seek found: `Found`, `After`, `Before`, `Eof`, `Bof`, `NoEntry`. Five carry an `r4*` number (`r4success`, `r4after`, `r4eof`, `r4bof`, `r4entry`); **`Before` has none**, because it is the outcome of an operation the C library does not have — a landing short of the search value. Named rather than numbered, and flagged as ours |
| `KeySearch` | Entity | A search value and its effective length: the caller's bytes, with trailing pad stripped and the all-pad case remembered. One place for `b4calcBlanks` and the "compare over the original length" rule. Also answers `Matches(key)`, the prefix comparison every one of the four operations ends with |
| `TagCursor` (extended) | Controller | Gains `Seek`, `SeekAtOrBefore`, `SeekLast`, `SeekNext`, `SeekPrevious` and `SeekExact(key, record)`. Descending inversion lives here, because it is about the tag rather than about a block |
| `KeyIncrement` | Entity | Adds one to a search value in the format's own way: increment the last byte below 0xFF and zero the 0xFF tail after it (`tfile4seekDescendKey`, I4TAG.C:2092-2151). Used by the descending seek and by `SeekAtOrBefore`, which is why it is a thing of its own rather than a private helper of either |

No new boundary and no new interface: seeking reads blocks through the `CdxTag` that traversal already
reads them through.

### Seams

Unchanged from 004 — `IRandomAccessSource` faked by `InMemorySource`, blocks built by `IndexImage`.
`IndexImage` gains nothing: the shapes seek needs are the shapes traversal needed.

---

## Decisions

1. **Seek takes key bytes, and says so in its name.** `TagCursor.Seek(KeySearch)` where `KeySearch`
   wraps a `ReadOnlySpan<byte>`. The alternative — taking a `string` or a `double` — would drag the key
   transforms into this step, and they are a different subsystem with a different gate.
   *Rejected — a `Seek(object value)` that transforms internally.* It hides which conversions exist and
   makes the failure "no overload" instead of "this tag's key type needs `COLLATION`".

2. **A partial seek is a prefix comparison over the search length, and the pad is stripped first.**
   `KeySearch` computes the effective length once: trailing pad bytes removed, and if nothing is left,
   the original length is used instead. Both rules come straight from `b4leafSeek`
   (b4block.c:140-153, 2211-2216), and putting them in one entity keeps the branch and leaf paths from
   disagreeing about them.

3. **The branch search is a binary search and the leaf search is a scan, deliberately.** They are not
   interchangeable: a leaf's keys do not exist independently of each other, so there is nothing to
   binary-search. Reproducing the asymmetry also reproduces the C's *positions*, which is what the gate
   compares.
   *Rejected — rebuilding all of a leaf's keys and binary-searching them.* It would give the same
   answer for a well-formed block and a different one for a corrupt block, and it throws away the
   duplicate-count skipping that makes the scan cheap.

4. **Descending seek is "increment, seek, step back", ported as such.** The temptation is to seek
   ascending and then adjust, which is what this *is* — but the increment has to happen on the key
   bytes (last byte below 0xFF, with 0xFF tails zeroed) and the step-back has to handle landing on
   entry 0 of a block. Both are in the C and neither is guessable.
   *Rejected — reversing the comparison instead.* It gives the wrong answer for a partial seek, because
   a prefix that is "less" ascending is not "greater" descending in the same way.

5. **`SeekExact(key, record)` is a separate operation, not a flag.** `tfile4go2fox`
   (I4TAG.C:1339-1458) seeks the key then walks forward while the record number is below the target.
   It answers a different question — "is this exact entry present" — and the write path will need it
   for a different reason again.

6. **A seek reports where it landed even when it did not find anything.** `After` leaves the cursor on
   the first greater entry, which is what makes range scans possible and is exactly what the optimizer
   will do with it. `Eof` leaves the cursor past the end. This is `PORTING-PLAN.md` §4.2's rule — a
   miss is a status, never an exception.

7. **Nothing here needs a collation table, and the reason is worth writing down.** For machine
   collation `u4keycmpPartial` is `memcmp` over the search length (u4util.c:2986-2987). For a *collated*
   tag, seeking by **key bytes** also needs no table — the caller supplies bytes that are already in
   key form. What needs the table is turning a *value* into those bytes, which is 007. So `CDXCOLL`'s
   `C_GEN` is gated here like any other tag, and the boundary lands in the honest place.

8. **The keys-must-not-decrease invariant from 004 becomes a seek property too.** For every case the
   gate also asserts that the landing entry is the *first* one not less than the search value, by
   comparing against the walk 004 already produces. That is a check the dump cannot give us — it is a
   property, verified against the corpus's own key sequence — and it catches an off-by-one that
   happened to agree with the C library on the recorded cases.

9. **Two of the five are ported and three are added, and they are gated differently.** This is the
   honest division and it should be visible in the tests, not buried:

   - **`Seek` reproduces `tfile4seek`** and **`SeekNext` reproduces `d4seekNext`**, including the part
     that looks like a rough edge: when the current entry does not match the search value, `SeekNext`
     degrades to a plain seek rather than reporting nothing (D4SEEK.C:1195-1210). Both are gated against
     dump sections the C library wrote.
   - **`SeekAtOrBefore`, `SeekLast` and `SeekPrevious` do not exist in the C library.** Nothing can be
     compared against, so they are gated as **properties over the key sequence step 004 recorded** —
     which the C library wrote, so the *data* is still the reference's even where the *operation* is
     ours:

     | Operation | Property, over 004's recorded `[keys]` list |
     |---|---|
     | `SeekAtOrBefore` | lands on the **last** entry whose key is not greater than the search value, and reports `Bof` exactly when there is none |
     | `SeekLast` | lands on the **last** entry whose key matches, and reports `NoEntry` exactly when none matches |
     | `SeekPrevious` | lands on the entry immediately before the current one, and reports `NoEntry` exactly when that entry stops matching |

   - **And one property ties the added operations to the ported one**, which is the part worth having:
     for every search case with no exact match, `SeekAtOrBefore` and `Seek` must land on **adjacent**
     entries — the one immediately before the other in the recorded sequence. `Seek` is gated against
     the reference, so adjacency drags `SeekAtOrBefore` into that gate's shadow instead of leaving it
     resting on our own definition alone. Where there *is* an exact match, the pair must bracket the run:
     `Seek` on its first entry and `SeekAtOrBefore` on its last.

   *Rejected — leaving backwards operations out because the C library has none.* The optimizer needs both
   ends of a range and needs to walk from either (`WHERE x <= 5` is the same tree read from the top down),
   and `PORTING-PLAN.md` §1 makes that the point of the library rather than an extra.

10. **`SeekAtOrBefore` is the primitive; `SeekLast` is one comparison on top of it.** Both are "increment
    the search value, seek, step back one" — the trick the C's descending seek already performs
    (`tfile4seekDescendKey` plus the step-back at I4TAG.C:2309-2320), which is why `KeyIncrement` is its
    own entity. They differ only in what they do with where they landed: `SeekAtOrBefore` reports
    `Found` or `Before`, while `SeekLast` reports `Found` or `NoEntry`, because a key that is merely
    *less* is not a match. Implementing the pair this way means the increment and its 0xFF tail rule are
    written once and used three times.
    *Rejected — implementing `SeekLast` by walking forward while the key matches.* It is `O(run length)`
    against `O(log n)`, and it duplicates a rule that has to exist anyway. It is instead what the tests
    use as an **independent cross-check**: a run of about sixty equal keys in `D_DUP` makes the two
    disagree loudly if either is wrong.

11. **All five are defined in the tag's own order, not in byte order.** For a descending tag "before"
    means earlier in traversal, which in byte terms is *greater* — the same inversion `Seek` already has,
    since `tfile4seek` on a descending tag returns the first key not *greater* than the value. Defining
    two of the five in tag order and three in byte order would be the kind of quiet inconsistency that
    only shows up in a range query on a descending tag. `T_TEXTD` is the tag that proves it: every one of
    its sequences must come out as the reverse of `T_TEXT`'s over the same data.

13. **Two stopping rules, two pairs of names, and no third pair.** `SeekNext`/`SeekPrevious` stop where
    the key stops matching; `Next`/`Previous` stop where the tag ends. Both are needed — the first walks
    a run of duplicates, the second walks a range or a whole table — and the second **already exists**
    from step 004. So this step does not add an unconditional `SeekNextAny`: it would be a second name
    for `Next`, and two names for one behaviour is how a caller ends up unsure which one respects the
    search value.
    What this step *does* add is the **composition promise, and its gate**: a cursor left by any seek
    outcome is a valid position to keep stepping from, in either direction, with either pair. Concretely
    — continuing forward from an `After` landing walks the rest of the tag; `Previous` from an `Eof`
    landing re-enters the tag at its last entry and `Next` from a `Bof` landing at its first; and
    `SeekNext` from a landing that already fails to match degrades to a fresh seek, which is the C's own
    behaviour (Decision 9). Without that promise, seeking and traversing would be two subsystems that
    happen to share a cursor.
    *Rejected — making `Next`/`Previous` match-aware once a seek has happened*, so that a cursor
    "remembers" its search value. It would make the same call mean different things depending on
    history, and a range walk — which must cross out of the matching run on purpose — would need a way
    to switch it off again.

14. **A range is the composition of the two bounds, and stays a composition for now.**
    `Seek(low)` … `SeekAtOrBefore(high)`, walked with the cursor's own `Next`, is a closed interval, and
    the two outcomes say whether each end was found or merely bracketed. That is everything `CONST4`'s
    `ge`/`le`/`gt`/`lt` constraints need from a tag. A `SeekRange` method that returned a positioned pair
    would be API ahead of its only caller (ADR-22), and `QUERY` is where the caller and its own far
    stronger gate — full-scan equivalence — both live.
    *Kept in view rather than deferred silently:* if 006 or 007 finds itself writing that composition
    twice, it belongs in `TagCursor` at that point.

## Open questions

| # | Question | Status |
|---|---|---|
| Q1 | **Does a descending tag's seek result need its own dump cases, or do the shared ten suffice?** The increment-and-step-back path has tail cases — an exact hit at the greatest key, a search value above everything, entry 0 of a block — that the generic ten may not reach on `T_TEXTD` specifically. | **Leaning add three descending-only cases** (greatest key exactly, one above it, one below the least) once the shared ten are generated and their coverage of `T_TEXTD` can be read from the dump |
| Q2 | **Should `KeySearch` refuse a search longer than the key length, or truncate as the C does?** `tfile4seek` clamps `lenPtr` to `keyLen` silently (I4TAG.C:2233-2234). | **Leaning clamp, matching the C**, with a component test that says so — a caller passing a longer value has made an error, but refusing it would diverge from the reference for no gain |
| Q3 | **Is the sub-pad-byte handling reachable from a machine-collated character tag at all?** `T_BIN` holds keys with 0x00 and 0x1F, and its pad byte is a space, so a search value trimmed of spaces can compare against a stored byte below 0x20. | **Answered by generating it.** `T_BIN`'s ten cases should exercise it; if the dump shows they do not, the eleventh case is a search value ending in a byte below 0x20 |
| Q4 | ~~Should `SeekAtOrBefore` land in this step?~~ | **Answered: yes, and it became the primitive.** It is a range's other end, which the optimizer needs, and building `SeekLast` on top of it removed a duplicate implementation of the increment rule rather than adding one (Decisions 10 and 14). The earlier lean was to defer it to `QUERY`; that was wrong on the cost — it makes the step smaller, not larger |
| Q5 | **Does `SeekNext`'s degrade-to-plain-seek behaviour deserve to survive into the public API in 007?** It makes the operation safe to call blind, which is convenient, and it also means a typo in a caller's loop silently becomes a fresh search rather than an error. | **Open — decide in 007, where the API is public.** The internal operation reproduces the C faithfully either way; what 007 may want is a stricter public wrapper that reports `NoEntry` instead of re-seeking |
