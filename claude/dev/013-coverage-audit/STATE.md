# 013-coverage-audit — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** in progress — sub-steps 1–3 done
**Current sub-step:** PLAN.md step 4

## Done so far

- [x] step 1 — fresh baseline (2026-10-31). `crap4csharp` at `9e155ec`, the same build as the audit,
  over all 119 library files at `997adeb`; 2620 unit tests passed, 3 skipped. Filed as
  `baseline.txt` and `baseline-by-step.tsv` (members attributed by `crap-by-step.py` as before;
  012 now has a row, for `CaseMap`, `ConversionSearch` and `TagContext`).
  - **690 members, 79 above CRAP 8.0, 96 at 0%** — against the audit's 662, 93 and 129. The drop is
    012's: its component tests reach part of `Query` and the decomposer, and its layer-1 tests
    `KeyForm`, `KindOf` and `AssignKeyConversionLength`.
  - **F1 as it stands** — 42 members below 100%. At 0%: `Walk.Constrain` (CC 18, CRAP 342),
    `VisitConnective`, `Widens`, `Fold`, `Merged`, `MatchTags`; `QueryExecutor.Reach`, `ProbeFor`;
    `QueryPlan.LeafCount`; `Query.StepByTag`, `Skip`, `Bottom`, `Previous`;
    `TagEligibility.CanExpress`, `IsWhole`; `ConstraintNode.Walk`, `IsLeaf`, `Leaf`, `Branch`;
    `LeafConstraint.ToString`, `ConstraintValue.ToString`, `AsCurrency`; `TagProbe.KeyLength`.
    Partly: `VisitComparison` 55%, `TryMatch` 38%, `QueryPlan.For` 42%, `TagProbe.InTheTagsDomain`
    44% and `KeyFor` 58%, `SubtreeMatch.Same` 73%, `Query.StepByRecord` 62%, `Prepare` 69%,
    `ResidualFilter.Terms` 72%, `ConstraintValue.CompareTo` 57%, `ConstraintSeeker.IsExcluded` 50%.
  - **F2** — 9 below 100%. At 0%: `RecordKey.WriteValue` (the expression overload, CC 8),
    `RecordKey.DoubleOf`, `KeyTransform.FromDateTime`, `DecrementLowByte`. Partly: the other
    `WriteValue` 35%, `SeekConverter.KindOf` 77%, `RefuseIfNotTheSameTwice` 53%,
    `CollationWeights.Refuse` 36%, `TableFor` 78%.
  - **F3** — at 0%: `ExpressionEvaluator.PictureDateText` (CC 10), `FormatNumber`, `Rounded`,
    `TimestampText`, `MemoBytes`, `Count`, `MakeTimestamp`; `ExpressionKey.FoxNumericOf`;
    `ExpressionValue.Scaled`, `Float`, `Whole`, `Currency`, `NumericText`, `DateText`;
    `ExpressionOptions.WithDatePicture`; `FunctionTable.Rows`, `VisualFoxProRows`, `Count`,
    `FirstVisualFoxProRow`. Partly, the ones that matter: `ExpressionKey.WriteValue` 38% (CC 13),
    `Write` 36%, `WriteText` 58%; `ExpressionValue.ToRaw` 50%; `LoadField` 71%; `Equal` 73%;
    `TypeResolver.AssignKeyConversionLength` 67%. Fifty-two members below 100% in all; the rest are
    above 85% and are taken only if the tests above reach them.
  - **F4** — `TableTagCursor.SearchFor` 38% (CC 11, CRAP 39.2), `TypedSeekFor` 0%, `SearchForInt32`,
    `SearchForDate`, `SeekNext`, `SeekPrevious` 0%, `Indicated` 67%, `Land` 87%; `Tag.ToString`,
    `Filter`, `Collation` 0%.
  - **F5** — 41 public members at 0%: `Table`'s `Seek` overloads (six), `SeekPrefix`,
    `SeekPrevious`, `GoPreviousIndexed`, the `Get*` and `GetMemo*` readers (eleven), the header
    properties (six), `ReadMemo` (CC 3), `OpenProbe`, `CompiledFor`; `Query.StepByTag`, `Skip`,
    `Bottom`, `Previous`; `Tag.ToString`, `Filter`, `Collation`; `FieldCollection.Count`,
    `TryGet`; `CodeBaseEngine.BlockCache`, `BlockCacheBlocks`. The full list is the TSV's rows with
    `0.0%` in those classes.
- [x] step 2 — F1a, decomposition (2026-10-31). No library change; no defect found.
  - `QueryDecomposerTests` (layer 1, 46 cases) over hand-built fields and `QueryTag`s compiled from
    their expressions, each stating a rule of `QUERY-OPTIMIZER.md` §3–4 as a plan — the tag and
    constraint of each leaf under its connectives, children sorted since the walk's order is not a
    promise: every operator and its reflection; the prefix widening, lower bounds only; terms no tag
    answers (bare field, negation, two fields, an untagged expression); an and keeping what it can,
    an or abandoned from either side, an and with nothing left; two tags under an or; folding under
    an and, and under an or only into one range; nothing folded across a connective; a refused tag
    not ending the search; the widened scope; constants a tag cannot hold exactly; a constant read
    under the decomposer's date policy; `MatchTags` showing the or's untried operand; the constraint
    and value descriptions; text comparison by common bytes, then width.
  - `TagEligibilityTests` gained `CanExpress` per key kind (whole, out of range, infinite, currency
    precision, float exactness, a timestamp only for a datetime tag).
  - Coverage (unit), against `baseline.txt`: `Walk.Constrain` 0% → 96% (the `_ => null` arm is
    unreachable: only comparison rows reach it), `VisitComparison` 55% → 95% (`left < 0` is
    unreachable on a well-formed tree), `VisitConnective` 0% → 100%, `TryMatch` 38% → 100%,
    `Fold`, `Merged`, `Widens`, `MatchTags`, `QueryPlan.For`/`LeafCount`, `CanExpress`/`IsWhole`,
    `ConstraintNode.Walk`/`IsLeaf`/`Leaf`/`Branch`, both `ToString`s 100%; `ConstraintValue.CompareTo`
    57% → 100% with the text cases. Left: `ConstraintValue.AsCurrency`, sub-step 3's (`TagProbe`).
  - **`mutate4csharp`**, copy-aside, md5 checked: `TagEligibility.cs` 17 of 17 killed;
    `QueryDecomposer.cs` 57 of 59, the two survivors equivalent — `:207` `FullyMapped = false` set
    true for a non-comparison term (a whole-filter one has no root, and a connective sets it false
    again at once), `:242` a one-child connective returned as a branch (`Merged` folds it back to
    the leaf).
  - Release 2680 + 1133 green.
- [x] step 3 — F1b, execution (2026-10-31).
  - Tests: `QueryExecutorTests` (layer 2, over `SortedKeyProbe`: an empty plan admits all, a leaf
    reaches its range, and intersects, or unites, a tag sought twice opened once);
    `ResidualFilterTests` (layer 1: nothing to check when wholly answered, only unanswered
    conjuncts checked, a non-conjunction checked whole, null fails); `TagProbeKeyTests` (layer 2,
    tags built with `IndexImage`: a value is sought by the key a record holding it stores — checked
    with `KeySearch.Matches` against `RecordKey`'s key and against a neighbour's — for integer,
    currency, single, double and logical; a short text a prefix; a nullable tag's `0x80`; a collated
    prefix its heads).
  - Two first-run failures were my expectations, not the code: a strict comparison breaks a tie by
    width, so `"B   " > "B"` (QUERY-OPTIMIZER.md §4.4), and `KeySearch.Bytes` leaves out trailing pad.
  - **Defect found and fixed — `TagCursor.StepPhysical`'s re-entry rule on a descending tag.** The
    cursor's `Eof`/`Bof` name the tag's ends, but the rule that steps back in from an end read the
    physical direction; on a descending tag it was mirrored, so a cursor stepping out of an end and
    out again came back in, and one stepping in skipped the boundary key. `Table` positions on its
    own and was unaffected (checked through the public API: both orders of `CDXBASE` behave
    alike); `TagProbe.Step` after the end repeated the last key instead of reporting it. Found by a
    new contract subclass over `CDXBASE.T_TEXTD` (`DescendingTagProbeContractTests`), pinned by
    `TagCursorTests.Walk_SteppingBackInFromAnEndLandsOnThatEndsKey`, both red first; fixed by reading
    the step in tag order. **Mutation:** the old rule restored → exactly those two red; the golden
    suite does not see it.
  - Coverage (unit): `QueryExecutor.Reach`/`ProbeFor`, `ResidualFilter.Terms`/`Admits`,
    `TagProbe.KeyFor`/`InTheTagsDomain`/`KeyLength`, `ConstraintValue.AsCurrency` all 100%.
  - **`mutate4csharp`**: `QueryExecutor.cs` 3/3, `ResidualFilter.cs` 13/13; `TagProbe.cs` 8/12, then
    the three `Step` survivors killed by the descending contract tests. Two stay: `:121` `&&` → `||`
    is equivalent (a step that moved is always on a key, one that did not never is); `:64` `<` → `<=`
    sits in the collated branch of `KeyFor`, which no query reaches — collated tags are refused in
    both scopes (ADR-54, ADR-59) — and which would, for a full-width value, build the key from raw
    bytes rather than weights. **Decided by the user:** the branch stays as it is, and why the
    optimizer refuses collated tags — with equality on one as a safe untrusted candidate set — is
    future work, recorded in `PORTING-PLAN.md` §2.3 with the full-width defect.
  - Release 2711 + 1133 green.
- [ ] step 4 — F1c, the result cursor
- [ ] step 5 — F2, keys from values
- [ ] step 6 — F3, the expression engine
- [ ] step 7 — F4 + F5, the tag cursor and the public API
- [ ] step 7a — the divergences, each resolved or documented (added at the user's call)
- [ ] step 8 — close

## Notes for the next session

The gate compares against `baseline.txt`, not `AUDIT.md`. To list a finding's members again:
`awk -F'	' '$4 ~ /Queries/ && $6 != "100.0%"' baseline-by-step.tsv` (class in column 4, coverage
in 6). `crap-by-step.py` writes its TSV beside the report it is given, so run it on a copy elsewhere
or it overwrites the audit's.

## Deviations from the plan

- `DIVERGENCES.md` added at the user's request (2026-10-31): every place the port refuses, skips or
  answers differently from the reference, with where each was decided. Not in the plan; prompted by
  sub-step 3's finding that the optimizer never uses collated tags. Sub-step 7a added the same day,
  at the user's call, to resolve or document each entry; `PLAN.md` and `DESIGN.md` updated.

## Blockers

None.
