# 013-coverage-audit — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** in progress — sub-steps 1–6 done
**Current sub-step:** PLAN.md step 7

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
- [x] step 4 — F1c, the result cursor (2026-11-01).
  - Tests: `QueryCursorTests` (component, a table and tag built in memory): record order and tag
    order, forwards and back, off either end and in again, `Skip` by one and by many, record 1
    reached from either direction, a top and a bottom that find nothing, an empty table, the count
    with and without a residual term. Every walk is bounded by the table's size, so a cursor that
    never reaches an end fails rather than hangs.
  - **Defects found and fixed in `Query`**, each red first: `Eof`/`Bof` were not cleared after a
    successful step; `Skip(-N)` from past the end stopped short of the start; in tag order a step
    forwards from before the start found nothing (the table's cursor was left past the end, so the
    step now starts from the tag's first key); and a `Top` or `Bottom` that found nothing set the
    mirror-image flags — now as the reference means them (`QUERY-OPTIMIZER.md` §7.5, ADR-69,
    decided by the user; `DIVERGENCES.md` §3).
  - **`mutate4csharp`**: 68 mutants, 64 killed, 4 survivors all equivalent — `:86` `NextFrom(1)` to
    `(0)` (`RecordSet.NextFrom` clamps to 1), `:148`/`:149` `count > 0` to `>=` (zero has returned
    already), `:230` `record >= 1` to `>= 0` (`Contains(0)` is false). The first run, before the new
    tests, left 14, eight of them real holes. Eight mutants loop; the watchdog ended each test
    process (ADR-70, below), and none was left behind.
  - Release 2731 (1 skipped) + 1133 green.
- [x] step 5 — F2, keys from values (2026-11-01).
  - Tests: `RecordKeyTests` (a nullable tag's null and value keys per KEY-COLLATION.md §5.3; a
    collated key found by seeking its value, case-blind under GENERAL; an accent kept in the tails),
    `KeyTransformTests` (datetime rounding, every second of four days against the double one step
    down where flagged, ordering across midnight; currency's extremes accepted and refused past
    them), `SeekConverterKindTests` (kind per result type, the not-same-twice refusals),
    `CollationWeightsTests`. No defect in the library; two of the tests' first expectations were
    wrong (GENERAL is case-blind in heads and tails alike) and were corrected against the spec.
  - **Dead code removed, decided by the user:** the byte overload of `RecordKey.WriteValue` (CRAP 72),
    `DoubleOf`, and with them `SeekConverter.Field`, had had no library caller since 012 (`33ca4fe`,
    ADR-58). `KeyTransformGoldenTests`' every-key gate, their one caller, now builds each key as the
    library does — the tag's expression evaluated over the record through `ExpressionValueSource`,
    then `RecordKey.Write` — with the same 3563 keys and the same expected bytes (deviation, below).
  - Coverage: every F2 member at 100% but `SeekConverter.KindOf` (76.5%), whose throw is
    unreachable: the field loaders beyond DBF-READ's types are transcribed but no expression
    produces one.
  - **`mutate4csharp`**: `CollationWeights` 6/6, `SeekConverter` 24/24, `KeyTransform` 25/30 then
    the currency bounds killed — `:38`/`:57` `+ 0x80` to `- 0x80` and `:236` `Round(…, 0)` to
    `(…, 1)` before a truncating cast are equivalent; `RecordKey` 6/7 then the tails killed. Every
    `DecrementLowByte` mutant killed.
  - Release 2776 (1 skipped) + 1133 green.
- [x] step 6 — F3, the expression engine (2026-11-01). Tests:
  `ExpressionEvaluatorTests` (STR and STRZERO as EXPRESSIONS.md §7.1 witnesses them, DTOC by
  picture and of a blank date, TTOC of a timestamp and a date), `ExpressionFieldLoaderTests` (every
  field type read from a record laid down in memory, memo cases, the null bitmap past its first
  byte), `ExpressionKeyTests` (the null byte, numbers and dates held as text, a trimmed tail),
  `TypeResolverTests` (each conversion's key width; the memo halving inside an expression and not
  behind a narrow first node). Every member the plan names is off 0% except `FunctionTable`'s four
  band getters, read only by the golden gates that check the table against the reference.
  **`mutate4csharp`** over the lines the tests reach: `ExpressionKey` 23/23, `TypeResolver` 21/22 then
  the memo condition killed, `ExpressionEvaluator` 64/71 then six killed — `:1047` milliseconds
  starting at 1 rather than 0 is equivalent (under a second); `ExpressionValue` 2/8 then the null
  flags killed — four `0` to `1` in `NumericText`/`DateText`'s number slots are equivalent, as every
  row taking a text-held number or date reads its bytes.
  - **Defects found and fixed**, each red first:
    - DTOC's result length was fixed at 8 whatever picture was configured, against e4parse.c:500-509
      and `ExpressionOptions.DatePicture`'s own doc. A ten-character picture was cut, and a filter
      comparing the cut text admitted every day sharing its first eight characters — a wrong record
      set (`FilterOptionsTests`). `TypeResolver.Resolve` now takes the options and the picture's
      length, and `Table.CompileFilter` and `SeekConverter.For` pass theirs; a DTOC tag under a
      ten-character picture now opens.
    - A `YYYY` picture with no `C` wrote the wrong digits (`8706-06-15` for 15 June 1987). It now
      writes the whole year, as `CCYY` does, and reads back through the same picture as the same
      day (ADR-71, decided by the user; the reference writes `87YY-06-15`; `DIVERGENCES.md` §3).
  - Their mutants: three survivors, all equivalent — the guard for a result longer than its picture
    (no longer reachable), `<= 2` against `< 2` at two letters, and the blank test reading the
    second digit rather than the first.
  - Release 2867 (1 skipped) + 1133 green.
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
- `BlockCacheSpeedupTests` failing now and then in Release, met during sub-step 4: measured to be the
  JIT tier, not the neighbouring assembly — cached walk ~470 µs tiered against ~110 µs fully
  optimized, a 2.7× ratio against 8.8×. The unit-test project now runs with tiered compilation off
  (ADR-68, decided by the user); four full Release runs in a row green.
- Orphaned test processes, met re-running `mutate4csharp` over `Query.cs` in sub-step 4: a mutant
  that loops a cursor survives the tool's timeout as a `CodeBase.Net.Tests.exe` whose parents are
  gone, spinning until killed by hand; an xUnit `Timeout` does not stop it either. Every unit test
  now runs under a watchdog that kills the process past one second, or past a test's own
  `[MaxRunTime]` (ADR-70, decided by the user); the timed cache gates carry ten.
- `tests/CodeBase.Net.Golden` changed in sub-step 5, against the plan's gate that it stay as it is:
  `KeyTransformGoldenTests.EveryKeyOfEveryTagIsRebuiltFromTheRecordItNames` rebuilt the corpus's
  keys through a `RecordKey.WriteValue` overload the library no longer called, so it gated code
  nothing ran. It now builds them through the library's own path, with the same keys and the same
  expectations; the overload is deleted (decided by the user).

## Blockers

None.
