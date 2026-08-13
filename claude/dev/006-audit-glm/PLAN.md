# 006-audit-glm remediation — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md), for the work
[`DESIGN.md`](DESIGN.md) designs.

**Two plan files, deliberately.** [`REMEDIATION-PLAN.md`](REMEDIATION-PLAN.md) is the response to the
audit — triage, decisions, and what order the *project* does things in. This file is the
`DEV_APPROACH.md` sense of the word: the test pyramid and the ordered sub-steps for the remediation
itself.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | `MemoFileHeader.Parse` with the high bit set in `blockSize`; `LeafGeometry.Parse` with each mask width over its bound and with the sum still correct; `FoxDate.ToJulian` across year 0 and its neighbours; `FoxDateTime.ToDateTime` with an out-of-range millisecond count (item 8); `Table.IsNull` with a bitmap shorter than the field count (item 9) | The entity edges, exhaustively and at memory speed. Items 2, 3 and 5 are pure functions over spans, and this is the only layer that needs to exist for them |
| 2 Component | `TagCursor` over a fake source: a branch pointing at an empty leaf whose sibling holds the entry (item 6); a forward and a backward walk across an empty block, which must still behave as they do today | The **coupling** between descent and the sibling walk. Item 6 is a defect precisely because two code paths disagreed about the same condition, and only a test that exercises both entry points can say they now agree |
| 3 Fault injection | A leaf chain that cycles through empty blocks, with a test timeout (item 4); a source that short-reads an index file and one that short-reads a DBF and a memo (item 7) | **The whole point of items 4, 5 and 7.** Every file in the corpus is valid by construction, so a cyclic chain, an impossible mask width and a truncated read cannot be expressed there at all |
| 4 Golden / corpus | **No new assertions.** All 453 golden tests pass unchanged, and no expectation is edited | That none of this altered what a well-formed file decodes to — which is the entire safety property of a remediation step |

**Corpus coverage — and why no generator case is added.** `DEV_APPROACH.md` §4 says an uncovered code
path is a signal to add a generator case rather than settle for a unit test. That rule is not being
dodged here, it does not apply: items 4, 5 and 7 guard against **files the C library cannot write**. The
generator produces valid indexes, so it cannot emit a cyclic leaf chain or a block declaring
`dupBits = 16`, and a short read is a property of the I/O, not of any file. These are layer 3 by
`DEV_APPROACH.md`'s own definition ("what the corpus physically cannot express, because every file in it
is valid").

**Item 3 turned out not to need one either.** Year zero is reachable from a stored field (a blank year
digit reads as a zero), so a generator case would have been possible — but it would have gated nothing
the arithmetic does not gate itself: `JULIAN4ADJUSTMENT` is the correct anchor only if year zero carries
366 days, so the identity is checkable in a unit test without any file at all. ADR-33 has the reasoning.

**Expected values.**

| Item | Expectation comes from |
|---|---|
| 1 `'7'` | Nothing new. The existing golden runs are the expectation, unchanged |
| 2 signed `blockSize` | A round-trip/sign invariant: bytes `0x80 0x00` read back as −32768, not 32768. No spec bytes typed in |
| 3 year zero | **An identity in the library's own constants**, not a typed-in number: `ToJulian` of 0000-12-31 must equal `JulianAdjustment` exactly, which holds only if year zero is leap. Backed by `D4DATE.C:324, 346-361`, cited in `DBF-FORMAT.md` §6.3 |
| 4, 5, 7 | The expectation is a refusal — "this throws `ErrorCode.Index`" — not a value. Hand-built malformed bytes are **input**, which `DEV_APPROACH.md` §4 permits explicitly |
| 6 | The entry the walk finds, taken from the same hand-built tree the test builds — a structural invariant, not a byte |
| 8 milliseconds | Two different sources: the day-rolling behaviour is pinned as **reproduced** (it is what the C does), the out-of-range case as a **type contract** — a `CodeBaseException`, per `API-ERRORS.md` |
| 9 `IsNull` | The promise itself: a bit past the end of the bitmap reads as not-null. No file involved |

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | ~~**Settle year zero against `D4DATE.C`**~~ · **done 2026-08-13** | The C computes year 0 as leap and the anchor constant depends on it; the port was already right and had faithfully copied the C's own wrong comment. `DBF-FORMAT.md` §6.3 has the cited fact, ADR-33 has the scope decision |
| 2 | **The comment fix** (item 3) · **done 2026-08-13** — no code changed | `FoxDate`'s class summary, the `DayOfYear` comment and the `YearToDays` comment now state what the code does and why; `ToDate`'s says there is no year zero to report. Still owed: layer 1 tests pinning *`ToJulian("    0229")` is 1721119* and *0000-12-31 is exactly `JulianAdjustment`*, which is what makes the anchor argument executable rather than only written down |
| 3 | **Remove `'7'`** (item 1, ADR-32) · **done** | Done, but the exit criterion was wrong: `grep` must **not** return nothing. `FieldResolverTests` keeps its `'7'` case, because the refusal at open is what ADR-32 preserves. Two sites the plan missed — the `RefuseAsNumber` theories — also had to go, and the suite caught them |
| 4 | **`MemoFileHeader.BlockSize` reads signed** (item 2) · **done** — no `MemoReader` guard needed, `MemoReader.cs:83` already had one, and `MemoReader`'s offset arithmetic is checked for what it does with a negative | Layer 1: *`0x80 0x00` reads as −32768*; *512 and 0 are unchanged*. Layer 3: *a negative block size surfaces as a `CodeBaseException`, not an `ArgumentOutOfRangeException` out of `IRandomAccessSource`* — adding the guard in `MemoReader` if it is missing. **Golden:** the 153 memo entries across five tables stay green, which is what says the signedness change is a no-op for real files |
| 5 | **`LeafGeometry.Parse` bounds its mask widths** (item 5) · **done** | Layer 1: *`recordBits = 33`, `dupBits = 16`, `trailBits = 16` are each refused as `ErrorCode.Index` even when the sum still matches `infoLength * 8`*; *every geometry the corpus actually contains still parses* |
| 6 | **`SourceReader.ReadExactly` takes an `ErrorCode`** (item 7) · **done** | Layer 3: *an index short read reports `ErrorCode.Index`*; *a DBF one and a memo one still report `Data`*. The second half is the real assertion — it says the parameter reached the two index callers and only those |
| 7 | ~~**Lift the empty-block skip into a helper**~~ · **done differently** — no lift needed; `SeekFirstAtOrAbove` short-circuited on a zero count and never called `StepPhysical`, so deleting that one clause is the whole fix | Layer 2: *a descent landing on an empty leaf finds the entry in the next sibling instead of reporting `Eof`*; *`StepPhysical`'s forward and backward walks over an empty block are unchanged*. **Golden:** the 3364-key traversal gate stays green, which is what says the lift did not change the path that already worked |
| 8 | **Bound the walk** (item 4) · **done** — bounded by the file's own block count | Layer 3, with an xUnit timeout: *a leaf chain cycling through empty blocks is refused as `ErrorCode.Index` naming the tag and node, rather than hanging*. **This is deliberately after sub-step 7**: bounding first and lifting second would mean writing the bound twice and reconciling two copies |
| 9 | **Bound the `'T'` millisecond count** (item 8) · **done** — day-rolling pinned as reproduced, only the out-of-calendar case changed | Layer 1: *a count above 86,400,000 still rolls the day, because the C library does* — pinned, not fixed; *a count large enough to pass `DateTime.MaxValue` raises a `CodeBaseException`, not an `ArgumentOutOfRangeException`*. The second is the only behaviour change |
| 10 | **Pin `IsNull` against a short bitmap** (item 9) · **done** | Layer 1: *a field whose null bit lies past the end of `_NullFlags` reads as not null*. No production code changes; this states a promise that is currently only implied |
| 11 | **Documents** · **done** | `SUMMARY.md` records what shipped **and the three audit claims that did not survive** — E2 with its `f4field.c:135-168` citation especially, so it is not raised a third time. `audit.md` is left as written (it is a record of what the audit said, not a document to correct). `claude/dev/README.md` and the root `STATE.md` say where things stand; `PORTING-PLAN.md` §5 gains **no row**, which is the point of this not being a step |

## Gate — **passed 2026-08-13**: 601 unit and component, 453 golden, corpus untouched

```
dotnet test net/CodeBase.Net.sln
git status --porcelain net/corpus/
```

Green, **453 golden tests still passing, and the second command printing nothing.** A remediation that
changed what a well-formed file decodes to would show up as an edited expectation or a touched corpus
file; neither is allowed here, and that is the whole gate.

**Mutation checks** — under `DEV_APPROACH.md` §4's new rule: copy aside, restore by `md5sum`, or run
after the commit. Never `git checkout <file>` while this work is uncommitted.

| Break | Must fail | And must leave green |
|---|---|---|
| Remove the cycle bound (8) | The cyclic-chain test, on its timeout | Everything else |
| Remove a mask-width bound (5) | Exactly that width's test | The other two widths, and every corpus geometry |
| Revert the index `ErrorCode` to `Data` (6) | The index short-read test | The DBF and memo short-read tests — which is what proves the parameter was threaded to two callers, not seven |
| Revert `SeekFirstAtOrAbove` to its own `Eof` (7) | The empty-leaf descent test | `StepPhysical`'s walk tests — proving the two paths really were separate before the lift |
| Invert `IsNull`'s bounds check to report null (10) | The new short-bitmap test | The `VFPNULL` golden suite — proving the corpus alone never covered this |

## Risks

| Risk | Why it would make this step *wrong* | Cheapest early exposure |
|---|---|---|
| ~~**The year-zero answer cannot be gated by the corpus**~~ · **retired** | Was the step's weakest gate, because the expectation rested on reading C source with no corpus witness | **Dissolved by the research.** The expectation no longer rests on interpreting the C: `JULIAN4ADJUSTMENT` = 1721425 is only the correct anchor if year 0 carries 366 days, so the arithmetic checks itself. The layer 1 test asserts that identity directly, which is stronger than any corpus date could have been |
| **The cycle bound refuses a valid file** | A post-delete index can hold a long run of empty blocks; a bound that is too tight turns a readable file into an error, which is worse than the hang it prevents | The bound is "more blocks than the file contains", which a valid file cannot exceed *by construction* rather than by estimate. No corpus case has an empty block at all, so this is stated as a known in `SUMMARY.md` rather than claimed as gated |
| **Lifting the skip helper changes `StepPhysical`** | Item 6's fix is the only structural change here, and `StepPhysical` is on the path every traversal takes. A subtle change would move 3364 keys | Sub-step 7 runs the full traversal gate, and the mutation check requires that reverting `SeekFirstAtOrAbove` alone reddens *only* the new test |
| **Signed `blockSize` breaks memo reads** | The change touches every FPT open | Every corpus FPT has a small positive block size, so the change must be a no-op for all 153 memo entries. If any golden memo test moves, the read was wrong before or after |
| **The `'7'` deletion removes something load-bearing** | Deleting a parameter with a default touches every call site silently | `RecordGoldenTests.cs:161` is the only live call site and it is edited by hand; the compiler finds the rest, and the golden suite proves `'T'` still renders as it did |
| **Scope creep into the cache** | The work sits in `NodeReader`/`LeafBlock`/`TagCursor`, one file away from P1 and P2. A block cache added "while we are here" would be an un-measured optimization, against the rule that produced the current code | `DESIGN.md`'s "Not in this step" names both, and the gate forbids expectation changes — a cache serving a stale block would show up there first |
