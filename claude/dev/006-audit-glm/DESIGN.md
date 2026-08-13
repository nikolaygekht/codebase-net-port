# 006-audit-glm — remediation design

**Executed and closed 2026-08-13.** See [`SUMMARY.md`](SUMMARY.md), which records six places where
execution contradicted this document — item 6 in particular turned out to be a one-clause fix rather
than the structural change designed below.

The work [`REMEDIATION-PLAN.md`](REMEDIATION-PLAN.md) §2.1 describes, designed. It is carried out in this
folder rather than under a step number: it opens no capability, and `PORTING-PLAN.md` §5 gains no row.
`007` stays free for the next genuinely new step, which by §3 #7 is the performance pass.

## Goal

Close the five verified correctness findings and the three bounded robustness guards from the 002–005
audit, leaving the read path saying exactly what it means: no dead branches behind a refusal, no field
read at the wrong signedness, no comment that contradicts its code, and no unbounded loop reachable from
a corrupt file.

**Success criterion.** The suite is green at ≥ 1039 tests (the count drops by one where a `'7'` test is
deleted and rises with the new guards); every new guard has a test that fails without it; and no golden
expectation changes, because none of this alters what a well-formed file decodes to.

## Not in this step

- **The block cache and the per-entry allocation** (P1, P2) — step 007, and P1 is deferred past it
  (§3 #7).
- **Block-alignment validation for child pointers** — needs a rule about what a legal child offset *is*,
  which is `BlockAddressing`'s to state. That is a design question, not a guard, and it stays on
  `HARDENING` (§2.1 item 6).
- **`TableTagCursor.Synchronize`'s O(n)** (P3) — needs `EXPR`.
- **Admitting `'7'`, compressed memo entries, long field names, `CBnnnnn` collations** — all refusals with
  an ADR or a plan entry behind them.

## No new classes

This is the design statement, not an omission: every item edits a class that already has an ECB role, and
none of them changes it. Nothing is added, nothing moves, no seam is introduced.

| Class | Role (unchanged) | What changes |
|---|---|---|
| `FieldValueDecoder` | Entity | Three `'7'` branches removed |
| `FoxDateTime` | Entity | `includeMilliseconds` parameter and its two branches removed |
| `MemoFileHeader` | Entity | `blockSize` read signed |
| `FoxDate` | Entity | Year-zero leap rule settled against the C |
| `LeafGeometry` | Entity | Mask widths bounded, not only summed |
| `TagCursor` | Controller | Empty-block walk bounded; `SeekFirstAtOrAbove` delegates its skip |
| `SourceReader` | Boundary helper | Caller states which error code a short read is |
| `FoxDateTime` | Entity | An out-of-range millisecond count cannot escape as a non-library exception |
| `Table` | Controller | Nothing — item 9 is a test that pins existing behaviour |

## The nine items

### 1. Remove `'7'` — ADR-32

Deletions only: `FieldValueDecoder.cs:33` (natural width), `:62` (`RefuseAsNumber`), `:199` (the
`is not ('T' or '7')` guard becomes `is not 'T'`); `FoxDateTime.ToText`'s `includeMilliseconds` parameter
and the two branches that read it (`FoxDateTime.cs:72,94,102`); the unit test
`ToText_KeepsTheMillisecondsForTheVariantThatHasThem`; and the `field.Type == '7'` argument at
`RecordGoldenTests.cs:161`, which becomes a bare `ToText(...)` call.

`ToText` keeps the ≥ 500 ms round-up, which is `'T'`'s own behaviour (`F4FIELD.C:1868-1873`) and the only
one the corpus witnesses. `BlankRecord` is **not** touched — it is already correct for every type the
port admits (`ZeroBlankTypes` omits `'H'` and `'7'`, matching `f4blank`).

### 2. `MemoFileHeader.BlockSize` reads signed — `FPT-MEMO.md`:109

`BinaryPrimitives.ReadUInt16BigEndian` → `ReadInt16BigEndian`. The property is already `int`, so only the
read changes.

**The design question this exposes** is not the read but what the rest of the code does with a negative
value, and the answer has to be the C library's. `MemoReader` computes `memoId * blockSize` for its
offsets; with a negative block size that is a negative offset. Two candidate behaviours:

- **Refuse at open** — clean, and wrong: it makes this port reject a file the C library opens.
- **Carry the value and let the offset fail** — faithful, provided the failure is a `CodeBaseException`
  about a corrupt file and not an `ArgumentOutOfRangeException` leaking out of `IRandomAccessSource`.

Take the second, and check what `MemoReader` does with a negative offset today; if it reaches the source
unguarded, add the guard there. That is the sub-step, and it is the only part of item 2 with any width.

### 3. `FoxDate`'s year zero — settled, and it is a comment fix · **done**

**Resolved 2026-08-13 by reading `D4DATE.C`; ADR-33.** The C library computes year 0 as leap
(`c4ymdDoY`, D4DATE.C:324) and *depends* on it: `c4ytoj(0)` is -366, so year 0 runs 366 days, and
`-366 + 366 + 1721425` lands exactly on `JULIAN4ADJUSTMENT` = 0000/12/31. A common year zero would
shift every date from AD 1 onward by a day. The comment above the line (D4DATE.C:323) asserts the
opposite and was never applied, while the same author's sibling fix that day -- `c4ytoj`'s
negative-year correction -- was.

**So the port's code is correct and must not change.** The port had reproduced the C's expression
*and* its wrong comment, faithfully. The work is a comment fix plus the scope statement, both landed:
`DBF-FORMAT.md` §6.3, `PORTING-PLAN.md` §2.2, `FoxDate`'s class summary and its two inline comments.

Dates before AD 1 are now **out of scope** (ADR-33) and that costs nothing: a date field holds digits
and spaces only, so no year below zero is representable at all. Year 0 survives as the decode of a
*malformed* field -- a space counts as a zero digit, so a blank or partial year such as `"    0229"`
comes out as year 0 -- and its Julian number still has to match, because a date tag's index key is
built from it.

### 4. Bound the empty-block walk — `TagCursor.cs:449`

`StepPhysical` follows the sibling chain through empty leaves with `while (true)`. Three candidate
bounds:

- **A constant, like `MaxDepth = 32`** — wrong. A post-delete index can legitimately hold a long run of
  empty blocks, and a constant would refuse a valid file.
- **A visited-node set** — exact, but allocates on a hot path for a case that never happens.
- **A count bounded by how many blocks the file contains** — exact enough, allocation-free, and needs no
  new state: a chain that visits more blocks than exist has cycled, whatever the shape of the cycle.

Take the third. The bound is derivable from the source length and the block size, both of which the tag
already reaches. The refusal is `ErrorCode.Index`, naming the tag and the node the walk was on.

### 5. `LeafGeometry` bounds its mask widths

`Parse` checks `recordBits + dupBits + trailBits == infoLength * 8` and nothing else, so a block declaring
`dupBits = 16` passes and then unpacks wrongly and silently — the masks are read as a `UInt32` for the
record and single bytes for dup and trail. Add `recordBits <= 32`, `dupBits <= 8`, `trailBits <= 8`,
refusing as `ErrorCode.Index`. Pure entity change, testable with hand-built bytes as input.

### 6. `SeekFirstAtOrAbove` skips empty leaves the way `StepPhysical` does

`TagCursor.cs:349` sets `Eof` and returns false when a descent lands on a `Count == 0` leaf, while
`StepPhysical` correctly steps over such a block. The two disagreeing is the defect; the fix is that
`SeekFirstAtOrAbove` **delegates to the same skip** rather than growing a second copy of it. That means
lifting the skip loop out of `StepPhysical` into a private helper both call — the one piece of structural
work in this step, and an SRP fix rather than an addition.

### 7. A short read from an index says `ErrorCode.Index`

`SourceReader.ReadExactly` hard-codes `ErrorCode.Data` for all seven of its callers. Give it an
`ErrorCode` parameter defaulting to `Data`, and have the two index-side callers —
`IndexFileReader.cs:85` and `NodeReader.cs:73` — pass `Index`.

The three `DbfOpener` callers and the two `MemoReader` ones keep `Data` deliberately: there is no
`ErrorCode.Memo`, and `Data` is the data-file code that a memo short read correctly falls under. Only the
index has a code of its own to be classified into.

### 8. A `'T'` field's millisecond count is bounded — audit E6

`FoxDateTime.ToDateTime` (`FoxDateTime.cs:57`) does `AddMilliseconds(milliseconds)` against the julian
day with no check. Two separate consequences, and only one is a defect:

- **Rolling into the next day.** A corrupt count above 86,400,000 shifts the date. The C library has the
  same blind spot, so this is **reproduced, not fixed** — pin it with a test and leave the behaviour.
- **Escaping the exception hierarchy.** A large enough count pushes past `DateTime.MaxValue` and
  `AddMilliseconds` throws `ArgumentOutOfRangeException`, which is not a `CodeBaseException`. *That* is
  the defect: `API-ERRORS.md`'s contract is that a corrupt file surfaces as this library's own exception
  type. Convert it.

The split matters. "Reproduce the C library's arithmetic, but never let a corrupt file throw something
the caller cannot catch by type" is the rule, and only the second half changes code.

### 9. `IsNull` with a short `_NullFlags` bitmap — audit E9

`Table.cs:832` reads `byteIndex < bitmap.Length && (bitmap[byteIndex] & (1 << (bit % 8))) != 0`, so a
bitmap shorter than the field count reports every field past its end as **not null**. That is almost
certainly correct — it is the defensive reading, and a missing bit is better treated as "no null flag
set" than as an error — but it is ungated, and it decides a field's *value* rather than merely
hardening a path.

**No code changes here.** The item is a test that states the promise, so that a later refactor cannot
silently invert it.


## Seams

Nothing new. Every guard is reachable through `IRandomAccessSource`, which already has fakes, and the
entity changes are pure functions over spans. `DEV_APPROACH.md` §4 permits hand-built malformed bytes as
test **input**, which is what items 4, 5, 6 and 7 need — none of them has an expectation beyond "this is
refused, with this code".

## Test pyramid

| Item | Layer | Expectation comes from |
|---|---|---|
| 1 `'7'` | none new — deletions | existing golden runs unchanged |
| 2 signed `blockSize` | 1 (entity) + 1 for the negative-offset guard | round-trip: a stored `0x8000` reads back as −32768 |
| 3 year zero | 1 | `D4DATE.C`, cited into the spec; plus a neighbouring-years invariant |
| 4 cycle bound | 2 (fake source, hand-built cyclic chain) | it refuses rather than hangs |
| 5 mask widths | 1 (hand-built block) | it refuses |
| 6 empty-leaf seek | 2 (fake source, branch → empty leaf) | it finds the entry in the next sibling |
| 7 error code | 2 (short-reading fake source) | `ErrorCode.Index` |

Item 4's test needs a timeout, because the failure mode it guards against is a hang rather than a wrong
answer — xUnit's per-test timeout, not a wall-clock assertion.

## Open questions

- **Item 2's negative-offset behaviour** — resolved by reading `MemoReader` during the step, not now. If
  the C library turns out to refuse a negative block size at open after all, item 2 changes shape and
  says so in `SUMMARY.md`.
- **Item 3's outcome is genuinely unknown** until `D4DATE.C` is read: the fix may be to the code, or it
  may be that the code is right and the comment is misplaced. Either is a valid close.
- **Whether item 6's lifted helper wants to live on `TagCursor` or move to a small collaborator** — decide
  when the two call sites are visible together. Default is the private helper; a new class needs a reason.
