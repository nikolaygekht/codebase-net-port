# 013-coverage-audit — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** in progress — sub-steps 1–7 done
**Current sub-step:** PLAN.md step 7a (7a-1 to 7a-5 done; ADR-56 kept and documented 2026-11-12; next the GENERAL-on-other-code-pages row of `DIVERGENCES.md` §1, an open question to the user)

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
- [x] step 7 — F4 + F5, the tag cursor and the public API (2026-11-01). No defect in the library.
  - Tests: `TableTypedSeekTests` (component, a typed table and tag built in memory: a tag sought by
    an int, a double and a date, exactly and to either side; by text through `DESCEND` (ADR-64),
    under GENERAL whole and as a prefix, accents told apart only by a whole seek; behind a nullable
    tag's null byte, text and integer; a whole seek against a prefix; text refused on every
    non-character kind, naming the seek to use). `TablePublicSurfaceTests` (ADR-67, a table with a
    memo file built in memory: the header properties, the typed readers, every memo reader, a memo
    pointer with no memo file refused and an empty one read without it, `FieldCollection`, `Tag`'s
    descriptions, stepping a tag that is not selected from past the end, from no record and from a
    record the tag lacks, the engine's cache settings). `IndexedTableImage` gained a typed table of
    any one field, nullable or not, and `MemoTableImage` is new.
  - **Dead code removed, decided by the user:** `Table.EnsureOpen` (no caller) and
    `Table.CompiledFor` (only two golden tests; the optimizer reads tags through `QueryTags`, which
    those tests now use). Kept, with the reason: `Table.CurrentRecord` and `FunctionTable`'s four
    band getters, read only by golden tests, which have no other way in to evaluate over a record or
    to check the transcribed table against the reference.
  - Coverage: no F4 or F5 member at 0% but `CurrentRecord`, kept as above.
  - **`mutate4csharp`**: `TableTagCursor`'s search members 14/16, then both killed (a whole collated
    seek without tails, a text seek left with no search to continue) — `Indicated`, the nullable-seek
    fix, is killed by the component tests. `Table`'s own logic 11/19, then five killed (a whole seek
    taken as a prefix, three times; an empty memo with no memo file). Equivalent: `:583` `< 1` to
    `< 0` (a position is never zero, `Invalid` is minus one), `:594` and `:600` the record of a move
    that did not move, and `count < 0` to `< 1` for a count of one.
  - Release 2895 (1 skipped) + 1133 green.
- [ ] step 7a — the divergences, each resolved or documented (added at the user's call)
  - [x] 7a-1 — long field names, read (2026-11-12). The user decided **support**, and the feature
    flags read on `0x31` only, as the reference (ADR-72). Committed `ce0ba23`.
    - **a, flags.** `FeatureFlags.Parse(bytes, extended)` returns all clear unless the version is
      `0x31`. The old not-extended test set the bytes to 9, which no flag reads as set, so it passed
      on the defect; it sets 1 now and checks all five flags. Layer 2: VFPTYPE with byte 16 = 1
      reads its ordinary descriptors. Mutating the version check reddens both.
    - **b, the long run.** `FieldDescriptorTable.ParseLong`, ending at `0x0D` or two bytes before
      the region's end, refusing a length below 1 or a name or tail past the region;
      `FieldDescriptor.FromParts` decodes the 21 shared bytes for both layouts. 12 layer-1 tests over
      `DescriptorBytes.Long`/`LongRegion`.
    - **c, the flag on the metadata.** `IFieldMetadata.LongFieldNames`; `TableFieldMetadata` takes it
      as a required argument (the user chose this over inferring it from the names, which would
      differ from the reference only in one error code). `ExpressionParser.Parse` lost its unused
      `longFieldNames` parameter. `KeyTypeResolver.PadByteFor`, `SeekConverter.For` and
      `TableTagCursor` take the flag; `DbfOpener` and `Table` pass the header's. 44 test call sites
      pass `false`, or the table's own flag where a table is open (`Table.LongFieldNames`, internal).
      Layer 2: `LongFieldNameTableTests` over `TableImage.OpenWithLongNames` — a filter by a long
      name, qualified by the alias, in mixed case. Each of the four library sites mutated to `false`
      reddens a test: the filter path the layer-2 tests (3 of 4), the open-time pad byte, the
      optimizer probe and the tag cursor the golden suite (15, 12, 13).
    - **d, the generator.** `case-longname.cpp`, `case-flags30.cpp`, `dump.cpp` printing the long
      layout. Every existing corpus file regenerated byte-identical; nine files added. The first
      `LONGNAME` run had a filter using `LEN`, which CodeBase lacks (`querySet` −470, e4unrecFunction)
      and the port has as a Visual FoxPro row (ADR-43) — a known divergence, not this one's; replaced
      by `TRIM(Customer_Name) = "Smith"` and regenerated.
    - **e, golden.** Both tables join every corpus-driven gate automatically; `LONGNAME` added to the
      index, seek, seek-by-value and seven query gates; coverage counts raised by exactly the new
      cases (tables 18 → 20, memo tables 9 → 11, indexed 9 → 10, tags 57 → 61, numeric values 936 →
      944, seeks 321 → 369, seek-next runs 141 → 161), and `LeafEvaluation`'s floor for `LONGNAME`
      is its three single-term filters. Two new golden tests: the long layout keeps the case it was
      created with; `FLAGS30`'s flag bytes are all 1 in the file and none reads as set.
    - **f, records.** ADR-72; `DBF-FORMAT.md` §2.2 and §4.2 (stored case, `_NullFlags`, the run's
      end, `headerLen` spot-checked at 308 against the bytes); `PORTING-PLAN.md` `DBF-READ`; both
      corpus READMEs; `DIVERGENCES.md`.
    - **`crap4csharp`** on the changed files: `ParseLong` CC 7, 97%; nothing new at 0%.
      **`mutate4csharp`**: `FeatureFlags.cs` 20/20. `FieldDescriptorTable.cs` 19/29, then two killed
      by new tests (an empty name is read; a length of 0 followed directly by the terminator is
      refused). Eight left, equivalent: `:70`, `:108`, `:117` arithmetic in a message's text; `:104`
      and `:113` `>` to `>=`, each moving the same `ErrorCode.Data` refusal one check earlier; `:66`
      the same in the short run, which predates this step.
    - Release 2913 passed, 1 skipped, + 1233 golden, green.
  - [x] 7a-2 — a tag over `RECNO()` (2026-11-12). The user decided: `RECNO()` supported, `DATE()`,
    `TIME()`, `RECCOUNT()` kept refused and documented (ADR-73, superseding ADR-49 in part).
    - **The reference keys `RECNO()` by the record's own number**: building a tag sets `recNum` before
      each key (R4REINDX.C:387-391, r4reinde.c:762-767), so the key is stable.
    - **a, library.** `SeekConverter.RefuseIfNotTheSameTwice` no longer lists `RECNO`.
      `IKeyValueSource.Read` and `RecordKey.Write` take the record's number beside its buffer: the
      value source replaced the record and its deletion flag in the context but not its number, so a
      `RECNO()` key would have been rebuilt as 0 and the cursor's synchronization would have landed
      wrong. Tests: `SeekConverterKindTests` (`RECNO()` keys as a double, `K_TEXT + STR(RECNO(), 4)`
      as text, the three others still refused); `RecordKeyTests` (a `RECNO()` key is the number of the
      record it is built for). Each change mutated reddens its test.
    - **b, generator.** `case-recnotag.cpp`: `R_RECNO` over `RECNO()`, `R_NAMENO` over
      `NAME + STR(RECNO(), 4)`, ten records, three filters — the reference answers all three from
      `R_RECNO`. Existing corpus byte-identical; five files added.
    - **c, golden — and a seek defect it found.** `R_NAMENO`'s `prefix-half` case, `"beta   "` in a
      14-byte tag, is found by the reference and was missed by the port: a search value with trailing
      pad was compared over the whole key length, the reference compares it over the length given
      (b4block.c:2303-2325). Every earlier witness had a value as long as the key, where the readings
      agree; this is the corpus's first key with pad inside it. Fixed in `KeySearch` (the loop's width
      and the successor's width are the value's), test first: `KeySearchTests` gained the shorter
      padded value and its successor. CDX-FORMAT.md §7 corrected. `SeekTypeGoldenTests`' helper
      learned a top-level `+`, its type taken from the reference's function table by the left
      operand's type. Counts raised by exactly the new case (tables 21, indexed 11, tags 63, seeks
      396, seek-next runs 170, key rebuilds `RECNOTAG` 20); `LeafEvaluation`'s floor for it is 2.
      Mutated, the number in the context reddens `KeyTransformGoldenTests` for `RECNOTAG`, and the
      comparison width seven seek gates.
    - **d, records.** ADR-73; CDX-FORMAT.md §7; `README.md` gained "Differences from CodeBase" with
      this entry; `Table.SelectTag`'s exception doc (its old text, "not a plain field name", predated
      expression tags); both corpus READMEs; `DIVERGENCES.md`.
    - **`mutate4csharp`**: `RecordKey.cs` 7/7. `KeySearch.cs` 41/47, then two killed by new
      assertions (a key differing in its first byte; one holding a non-pad byte just past the value's
      length). Four equivalent: `:145` and `:156` `>=` to `>` (equal lengths give the same answer by
      either branch; the second is a fast path only), `:166` `<` to `<=` (the buffer holds the pad
      byte at `Length` anyway), `:169` `<` to `<=` (reached only when the bytes differ).
    - Seen, not acted on: `TableTagCursor.Synchronize`'s comment says a selectable tag's expression
      "is always a bare field name (ADR-28)", which stopped being true in step 010.
  - [x] 7a-3 — a tag keyed through the date picture (2026-11-12). Reviewing ADR-42's entry found its
    refusal had never been written, and the gap gave a **wrong record set**: `CDXEXPR`'s `T_DTOC`
    (keyed under `MM/DD/YY`) was used under the default picture `CCYYMMDD`, so
    `DTOC(K_DATE) = "01/01/00"` counted 2 by the tag and 0 by the records. The user decided: implement
    ADR-42 as written (ADR-74).
    - **a, library.** `ExpressionOptions.IsChosen` and an internal `Unchosen` instance (`Invariant`'s
      values, unmarked) as `CodeBaseEngine`'s default; every public and derived instance is chosen.
      `SeekConverter.RefuseIfThroughAnUnchosenPicture` refuses a key with a `DTOC` (either row) or
      `CTOD` node under unchosen options, naming the function and the setting. `QueryTags` already
      skips a tag that cannot be selected, so the optimizer is covered with no change. Tests:
      `SeekConverterKindTests` (four expressions refused unchosen, accepted under `CodeBase` and under
      `Invariant` chosen; `DTOS` and a bare date need no choice), `ExpressionOptionsTests`.
    - **b, golden.** `DatePictureTagGoldenTests` over `CDXEXPR`: under the default, `T_DTOC` is refused
      and absent from `QueryTags`, and every `DTOC` filter counts what the records say; under
      `ExpressionOptions.CodeBase`, each filter naming a stored `T_DTOC` key counts exactly the records
      the reference stored under it, by tag and per record. `QueryEquivalenceGoldenTests`' every-tag
      gate and `SeekTypeGoldenTests` select every tag, `T_DTOC` and `T_CTOD` among them, and now set
      the picture and century the dump was taken under, as `ExpressionTagGoldenTests` already did.
    - **c, records.** ADR-74; `CodeBaseEngine.ExpressionOptions` and `Table.SelectTag` docs;
      `README.md` "Differences from CodeBase"; `DIVERGENCES.md`.
    - **Mutations.** The check disabled: 3 unit and 2 golden tests red; the engine's default chosen
      again: 1 unit and 2 golden red. `mutate4csharp --lines` over the new check: its one site, the
      second `DTOC` row, survived — no test reached `DTOC` over a computed date — and is killed by
      `DTOC(K_DATE + 1)`.
    - A note for the mutation checks by hand: a golden run with `--no-build` after building only the
      unit project reads a stale library from the golden project's own `bin`; build the solution.
    - Release 2924 passed (1 skipped) + 1305 golden, green.
  - [x] 7a-4 — the `'7'` field, a datetime with milliseconds (2026-11-12). The user decided full
    support (ADR-75, superseding ADR-32).
    - **What it is.** CodeBase's `r4dateTimeMilli`: `'T'`'s eight bytes with the milliseconds kept.
      Opened by `'T'`'s rules; VFP never writes one. The port already had the expression type and its
      comparison rows, but keyed it through the `'T'` transform, which rounds.
    - **a, read.** `FieldResolver` (readable, VFP-or-later, `'T'`'s lengths), `BlankRecord` (zeros),
      `FieldValueDecoder` (width, refused as a number, read as a datetime), `FoxDateTime.ToText`'s
      `includeMilliseconds` back (ADR-32 had removed it as dead): no rounding, `.MMM` only when
      non-zero. Public API unchanged — `GetDateTime` keeps the milliseconds; `GetString` refuses it as
      it refuses `'T'` (binary).
    - **b, expressions and keys.** Loader row 17 (`FieldTimestampWithMilliseconds`), a
      `TimestampWithMilliseconds` value, `KeyTransform.FromDateTime`'s `keepMilliseconds` (no rounding,
      flag by the truncated second), set by `ExpressionKey` for that type. Tests: `KeyTransformTests`
      (agrees with `'T'` on a whole second, sorts a fraction between its seconds, orders by
      millisecond), `ExpressionKeyTests`, `ExpressionEvaluatorTests` (the loader keeps the fraction),
      decoder, blank, resolver and text tests.
    - **c, generator.** `case-dtmilli.cpp`: 300 moments, seconds by a stride of 7919, ten millisecond
      values, the day's edges, a repeat, a blank; `M_ASC`/`M_DESC`. `dump.cpp` prints `r4dateTimeMilli`
      as `r4dateTime`. Existing corpus byte-identical; four files added.
    - **d, golden.** `DTMILLI` in the layout, index, seek and key-rebuild lists; the record gate's text
      check covers `'7'`. **All 600 stored keys rebuild byte for byte**, which witnesses the
      millisecond branch of `t4dateTimeToFoxMilli` as built on Windows. Counts by exactly the case
      (tables 22, indexed 12, tags 65, seeks 425, key rebuilds 600).
    - **e, records.** ADR-75; `PORTING-PLAN.md` §2.1 and §2.3; `DBF-FORMAT.md` §5; `KEY-COLLATION.md`
      §2.8; both corpus READMEs; `DIVERGENCES.md`; `ExpressionType` and `FieldResolver` docs that said
      the type was unreachable.
    - **Mutations.** Rounding forced in the transform: 5 unit, 1 golden red; the key path rounding: 1
      and 1; the field loaded as `'T'`: 1 and 1. `mutate4csharp --lines` over `FromDateTime`: 5/5.
    - Release 2944 passed (1 skipped) + 1346 golden, green.
  - [x] 7a-5 — the `'W'` and `'O'` UTF-16 fields (2026-11-12). The user decided: both supported,
    `W` told from VFP 9's Blob by version `0x32`; `STR(W)` by the table's code page with Windows'
    best-fit reproduced; `GetString` as stored. ADR-76, ADR-77.
    - **a, the supplement.** A BMP sweep found .NET's code-page encodings and `WideCharToMultiByte`
      apart on 0-33 code points per single-byte code page (General Punctuation; U+2000 among them on
      cp1251, the blank of a wide field). `test-files-generator/bestfit/` (C#, Windows only) writes
      `WindowsBestFitSupplement.cs`: 260 entries over the 20 single-byte code pages `CodePageMap` names.
    - **b, read.** `DbfVersion.VisualFoxPro9`; `FieldResolver` reads `W`/`O` on every version, `W` on
      `0x32` refused as a Blob; `BlankRecord` `00 20` pairs; `FieldValueDecoder.WideText`/`IsWide`, `O`
      less its count, both refused as numbers; `Table.GetString` decodes UTF-16 first.
    - **c, expressions.** Loader rows 1 and 2; `STR(<bare W/O field>)` gets `len / 2` in the parser and the
      wide row by overload, `STR(W, n)` width `n` (the reference's own behaviour, found writing the case);
      `TypeResolver` lengths (`O` less two), a width past the field refused, any other wide-consuming row
      refused; `WideNarrowing` (lazy, a 64K map per code page, multi-byte refused); the context's
      `Narrowing`, wired into both of `Table`'s contexts.
    - **d, keys and seeks.** `KeyKind.WideCharacter` (machine only; a collated wide tag refused), pad NUL,
      `ExpressionKey` swaps each unit, `TableTagCursor.WideSearch` (whole-key seeks pad with U+2000),
      `Table.Search` encodes UTF-16 for a wide tag.
    - **e, generator.** `case-wstr.cpp` (cp1251, 26 rows spelled as code units, four tags, an expression
      dump, six filters; stops unless `GetACP()` is 1251). Existing corpus byte-identical; six files added.
      The dumps confirmed the design: blank `00 20`, the count bytes, the reference's `STR` keys equal
      to the supplement's answers, its optimizer answering `STR(K_W)` filters from `S_ASC`.
    - **f, golden.** `WSTR` in every list; `CorpusExpressionDump.Load(table)` parameterized; query
      filters now decoded with the table's code page (a cp1251 literal); `SeekTypeGoldenTests` lets wide
      tags take text. New `WideStringGoldenTests`: the expression dump's shapes, values at every record
      and refusals; `GetString`; a seek by each record's assigned text; a step from every record on all
      four tags. **All 104 keys rebuild byte for byte.** Counts: tables 23, indexed 13, indexes 12, tags
      69, seeks 474, runs 180.
    - **Mutations.** Unit/component: 7 read, 9 expression, 8 key/seek, all killed. Golden: supplement,
      swap, `O` length, cursor `Narrowing`, seek pad, `STR` width all killed — the swap and the cursor's
      `Narrowing` only after two gaps were closed (WSTR missing from the key test's own table list; no
      golden path used the tag cursor's context, now the step test). `mutate4csharp` on
      `WideNarrowing.cs`: 22/22, the file restored and verified.
    - **g, records.** ADR-76, ADR-77; `DBF-FORMAT.md` §5, §6.11, §11.2 (the blank question resolved);
      `KEY-COLLATION.md` §3.6; `EXPRESSIONS.md` row 108; `PORTING-PLAN.md` §2.1, §2.2 and the W-blank
      item; README "Differences from CodeBase"; both corpus READMEs; `DIVERGENCES.md`; XML docs of
      `GetString`, `Seek(string)`, `ExpressionType`.
    - Release 3011 passed (1 skipped) + 1429 golden, green.
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
- `tests/CodeBase.Net.Golden` changed again in sub-step 7: `OptimizerContractGoldenTests`' two
  tests of a tag keeping its compiled expression read it through `Table.CompiledFor`, which nothing
  in the library called; they now read it through `QueryTags`, the optimizer's own path, with the
  same expectations (decided by the user).

- **The corpus grew in sub-step 7a-1**, against the step's "no new corpus cases" — at the user's
  call, by addition only: `LONGNAME` and `FLAGS30`, every existing file byte-identical. Golden lists
  and coverage counts changed by exactly those cases.

## Blockers

None.
