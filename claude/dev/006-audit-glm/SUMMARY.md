# 006-audit-glm — summary

**Closed 2026-08-13.** An independent audit of steps 002–005, its triage, and the remediation it
prompted. Carried out here rather than under a step number: it opened no capability and
`PORTING-PLAN.md` §5 gained no row.

**Result: 1054 tests** (601 unit and component, 453 golden), up from 1039. **No golden expectation
changed and no corpus file was touched** — the gate for a remediation, since none of this may alter
what a well-formed file decodes to.

## What the audit was worth

It found no wrong-record-set bug, which matches its own verdict. Of its 22 ranked findings, **five were
real defects**, three were already-correct or already-known, and the rest were coverage and performance
observations that now have homes ([`REMEDIATION-PLAN.md`](REMEDIATION-PLAN.md) §5 is the full
disposition).

**Three of its claims did not survive re-checking**, recorded here so they are not raised a third time:

- **E2, `'H'` blanking** (ranked **C, Medium**) — a **false positive**. `f4blank`
  (`original/source/f4field.c:135-168`) lists its zero-filled types explicitly and `r4floatBin` is not
  among them; it falls to `default: c4memset(…, ' ', …)`. A blank `'H'` **is** four spaces, which is
  what `BlankRecord.ZeroBlankTypes` already produced. `DBF-FORMAT.md` §6.14 transcribes this correctly.
- **E1, `'7'`** — real, but its stated justification was false. `'7'` is a CodeBase extension gated
  behind `version >= 0x30` signed, so the "real FoxPro 2.x table" that motivated **C, Medium** cannot
  exist. And "the decoder is ready" was wrong: `BlankRecord` had no `'7'` entry while the C zero-fills
  `r4dateTimeMilli`, so admitting the type would have blanked it to spaces where the C writes zeros.
- **E3, `FoxDate` year zero** — the **code was right and the C library's own comment was wrong**. See
  below; this one turned out to be the most interesting thing in the audit.

## What shipped

| # | Change | Files |
|---|---|---|
| 1 | `'7'` removed — ADR-32 | `FieldValueDecoder`, `FoxDateTime`, 4 test sites |
| 2 | `MemoFileHeader.BlockSize` reads **signed** per `FPT-MEMO.md`:109 | `MemoFileHeader` |
| 3 | Year zero settled and documented — ADR-33 | `FoxDate` comments, `DBF-FORMAT.md` §6.3 |
| 4 | Leaf chain **bounded** — a cycle of empty blocks no longer hangs | `TagCursor`, `CdxTag`, `NodeReader` |
| 5 | `LeafGeometry` bounds its **mask widths**, not only their sum | `LeafGeometry` |
| 6 | `SeekFirstAtOrAbove` steps over an empty leaf like the walk does | `TagCursor` |
| 7 | An index short read reports `ErrorCode.Index` | `SourceReader`, `NodeReader`, `IndexFileReader` |
| 8 | A `'T'` millisecond count that leaves the calendar is a library error | `FoxDateTime` |
| 9 | `IsNull` past the end of a short bitmap is pinned as not-null | test only |

**Mutation-checked four ways**, restoring by `md5sum` under `DEV_APPROACH.md` §4's new rule and never
by `git checkout` while uncommitted: the empty-leaf descent, the cycle bound, the `IsNull` bounds
check, and the index error code. Each reddened exactly its own test and nothing else. The `IsNull` one
is the most informative — inverting it left **all 453 golden tests green**, which is the proof that the
corpus never covered that branch.

## Year zero, which was the real find

`c4ymdDoY` (`D4DATE.C:323-324`) carries this:

```c
// AS 06/21/00 it turns out that year 0 is really 1BC, and is not a leap year!
isLeap = ( ((year%4 == 0) && (year%100 != 0)) || (year%400 == 0) ) ? 1 : 0 ;
```

The expression returns **1** for year 0 — the comment was never applied to the line beneath it. The
same author's sibling note that same day, `c4ytoj`'s negative-year correction, *was* applied. This port
had reproduced the expression **and the un-applied comment**, faithfully, which is how the audit came to
read it as a defect.

**The code must not change**, and the proof is arithmetic rather than calendrical: `c4ytoj(0)` is −366,
so year 0 runs 366 days, and `−366 + 366 + 1721425` lands exactly on `JULIAN4ADJUSTMENT`, documented as
0000/12/31. Make year 0 common and every date from AD 1 onward shifts by a day. The test states that as
an identity needing no magic constant — the last day of year 0 minus its first is 365, and year 1 begins
one day later.

Dates before AD 1 are now **out of scope** (ADR-33), which costs nothing: a date field holds digits and
spaces, so no year below zero is representable. Year 0 survives only as the decode of a **malformed**
field — a space counts as a zero digit, so `"    0229"` is year 0 — and its number still has to match,
because a date tag's index key is built from it.

## Where execution contradicted the plan

Six, per `DEV_APPROACH.md` §6:

1. **Item 6 needed no helper lift.** `DESIGN.md` proposed extracting the empty-block skip from
   `StepPhysical` so `SeekFirstAtOrAbove` could share it. Reading the code, the skip was already
   reachable — `SeekFirstAtOrAbove` short-circuited on `current.Count == 0` and never called
   `StepPhysical` at all. Deleting that one clause is the whole fix. **The design over-engineered a
   one-clause defect**, and the structural work it planned would have been churn.
2. **Item 2 needed no new guard.** `MemoReader.cs:83` already refuses a negative offset as a
   `CodeBaseException`, so the design's open question ("does an `ArgumentOutOfRangeException` escape?")
   answered itself. Only the read changed.
3. **Sub-step 3's exit criterion was wrong.** `PLAN.md` said `grep "'7'"` must return nothing. It must
   not: `FieldResolverTests` keeps a `'7'` case, because the **refusal at open** is what ADR-32
   preserves. Two further sites the plan had not listed — the `RefuseAsNumber` theories — had to go, and
   the tests caught them.
4. **Item 8 split in two.** Rolling into the next day on a corrupt millisecond count is what the C
   library does, so it is **pinned, not fixed**. Running off the end of `DateTime` is this port's own
   arithmetic failing and now raises `CodeBaseException` instead of `ArgumentOutOfRangeException`. Only
   the second changed behaviour.
5. **The xUnit `Timeout` attribute does not save the cycle test.** It cannot interrupt a synchronous
   loop, so with the bound removed the whole run hung and had to be killed externally rather than
   failing at ten seconds. The test proves the bound works when present; it does not convert the
   unbounded case into a clean failure. Worth knowing before relying on `Timeout` elsewhere.
6. **The mask-width bound needed justifying, not just adding.** `MaxInfoLength`'s own doc mentions
   "16-bit counts", which would have made an 8-bit bound refuse valid files. `CDX-FORMAT.md` §6.5
   resolves it: the 16-bit form is a *runtime* derivation for keys longer than 255 bytes, and this port
   refuses those at the tag header (`IndexHeader.MaxKeyLength` is 240). The bound is now documented as
   depending on that.

## What this deliberately did not do

- **P1, the block cache** — step 007 measures, and the cache is designed against `QUERY`'s access
  pattern rather than guessed at now.
- **P2, P5, P6, P7** — step 007, measure-first. P5's copy is *defensive*, P6 runs over tens of tags, and
  P7 is already a documented decision in `LeafBlock.Seek`'s own remarks. Expect to keep at least two.
- **Child-pointer block alignment** — `HARDENING`. It needs a rule about what a legal child offset *is*,
  which is `BlockAddressing`'s to state; that is a design question, not a guard.
- **Twelve corpus gaps** — named in `PORTING-PLAN.md` §6.3, because corpus contents belong to that
  document. They want generator cases, not unit tests standing in for them.
