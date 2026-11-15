# 013-coverage-audit — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | the decomposer's plans over hand-built tags; `CanExpress` per key kind; constraint descriptions; `RecordKey`/`KeyTransform`/`CollationWeights` invariants and refusals; the evaluator's formatting and picture kernels over constants; `ExpressionKey` lengths and writes | a wrong plan or a wrong key with no table in sight: the rule itself, every branch, at memory speed |
| 2 Component | `QueryExecutor` over `SortedKeyProbe`; `ResidualFilter` verdicts per record; `Query` moving over an `IndexedTableImage` in record and tag order, `Skip`, `Count`, a residual term; `TableTagCursor.SearchFor` per branch; one test per public forwarding member that it reaches its target (ADR-67); `Table.QueryTags` and `ReadMemo` | sequencing — where a cursor lands after a move, what a count reads, which records the residual admits — without a corpus file |
| 3 Fault injection | only where a member's untested branch is a refusal of malformed input (a tag that cannot be typed, an index entry naming a record the table lacks) | the inputs no valid corpus file holds |
| 4 Golden / corpus | **unchanged** — no expectation edited, no corpus file regenerated | still the witness that the rules match the reference |

**Corpus coverage.** None needed: every expectation below the corpus comes from the specification's
contract, an invariant, or corpus bytes at a cited offset. A test that would need the reference's own
answer for a path no case covers is not written; the path is recorded as a future generator case.

**Expected values.** Plans and record sets: the contract in `QUERY-OPTIMIZER.md`, stated as ranges and
record numbers over data the test builds. Keys: bytes sliced from `CDXBASE`/`CDXTIME`/`CDXCOLL`
corpus files with the offset cited, or round-trip and ordering invariants. Evaluator values: the
corpus value dump (`CDXEXPR.expr.dump.txt`), cited, as `ExpressionEvaluatorTests` already does.

## Steps

Each sub-step runs test first, then `crap4csharp` on the files it touched, and records any defect
found and fixed.

| # | Step | Verify by |
|---|---|---|
| 1 | **Fresh baseline.** `crap4csharp` over the whole library at the step's start, filed as `baseline.txt` and `baseline-by-step.tsv` beside `AUDIT.md`; F1–F4's member lists re-derived from it | the files exist; `STATE.md` lists F1–F4 as they stand now |
| 2 | **F1a — decomposition.** `QueryDecomposer` (through `Decompose`), `QueryPlan.For`, `TagEligibility.CanExpress`/`IsWhole`, `ConstraintNode.Walk`, the constraint descriptions | every F1a member off 0%; a mutation in `Constrain` and one in `VisitConnective` redden a layer-1 test |
| 3 | **F1b — execution.** `QueryExecutor` over `SortedKeyProbe`, `ResidualFilter.Terms`/`Admits`, `TagProbe.KeyFor`/`InTheTagsDomain` | every F1b member off 0%; a mutation in `Reach` and one in `Terms` redden a layer-2 test |
| 4 | **F1c — the result cursor.** `Query` over `IndexedTableImage`: `Top`, `Bottom`, `Next`, `Previous`, `Skip`, `Count`, record and tag order, a residual term | every `Query` member off 0%; a mutation in `StepByTag` reddens a layer-2 test |
| 5 | **F2 — keys from values.** `RecordKey`, `CollationWeights`, `KeyTransform.FromDateTime`/`DecrementLowByte`, `SeekConverter.KindOf`/`RefuseIfNotTheSameTwice` | off 0%; a mutation in `DecrementLowByte` reddens a layer-1 test, not only `KeyTransformGoldenTests` |
| 6 | **F3 — the expression engine.** The evaluator's 0% kernels, `ExpressionKey`'s partial members, `ExpressionValue.ToRaw` | off 0%, or a recorded reason per member |
| 7 | **F4 + F5 — the tag cursor and the public API.** `TableTagCursor.SearchFor` per branch, `TypedSeekFor`; one test per public forwarding member of `Table`, `Tag`, `FieldCollection` and `CodeBaseEngine` that it reaches its target (ADR-67), plus `Table.QueryTags` and `Table.ReadMemo`'s own logic | off 0%; the nullable-seek fix's mutation reddens a layer-2 test, not only the golden one; no public member left at 0% |
| 7a | **The divergences — each resolved or documented.** *(Added 2026-10-31, at the user's call.)* Go through `DIVERGENCES.md` entry by entry with the user, and for each decide one of: **fix** it (test first, as any defect here), **keep** it as a divergence by decision, or **defer** it to a named later step. A kept one gets an ADR where it has none (long field names first), and the user-facing documentation it lacks — the XML docs of the API that meets it and a "Differences from CodeBase" section in `README.md`. A deferred one gets its row in `PORTING-PLAN.md`. `DIVERGENCES.md` is rewritten to say, per entry, which it became and where | every entry in `DIVERGENCES.md` names its outcome and its record (ADR, docs, test or plan row); each fix has a failing-first test and a mutation that reddens it; `README.md` lists every kept divergence a caller can meet |
| 7a-1 | **Long field names, read** *(added 2026-11-12, the first 7a entry; the user decided support)*. Its own plan below | its own gate below |
| 7a-2 | **A tag over `RECNO()`** *(added 2026-11-12; the user decided `RECNO()` supported, the other three refused)*. Its own plan below | its own sub-steps below |
| 7a-3 | **A tag keyed through the date picture** *(added 2026-11-12; ADR-42's refusal, never implemented — a wrong record set found while reviewing it)*. Its own plan below | its own sub-steps below |
| 7a-4 | **The `'7'` field, full support** *(added 2026-11-12, at the user's call)*. Its own plan below | its own sub-steps below |
| 7a-5 | **The `'W'` and `'O'` Unicode fields** *(added 2026-11-12, at the user's call: read, keys, seeks, `STR(W)` by the table's code page)*. Its own plan below | its own sub-steps below |
| 8 | **Close.** `crap4csharp` over the library against `baseline.txt`; `mutate4csharp` on `QueryDecomposer.cs`, `QueryExecutor.cs`, `Query.cs` and `TableTagCursor.cs`; `SUMMARY.md`, `claude/dev/README.md`, root `STATE.md` | the gate |

## Gate

```bat
cd net
dotnet test CodeBase.Net.sln -c Release
git status --short corpus tests/CodeBase.Net.Golden   &:: nothing — no expectation, no corpus file
```

Green, nothing listed, and the `crap4csharp` rerun shows no F1–F4 member at 0% unit coverage that
`STATE.md` does not explain.

## Risks

- **Tests that agree with the code instead of the contract.** The easy way to raise coverage is to
  assert what the code does. Each test names the promise from the spec it checks, and the close's
  mutation runs are the check that the tests notice a break.
- **A stand-in that drifts from the real thing.** `SortedKeyProbe` is held to `TagProbeContract`;
  `IndexedTableImage` builds real bytes and reads them with the real reader. A new helper gets the
  same treatment or is not used.
- **Defects found mid-step growing the step.** Each is fixed test first and kept small; one that
  needs the reference's answer becomes a recorded generator case, not a guess.
- **The baseline moving under the step.** The gate compares against sub-step 1's run, not `AUDIT.md`'s.

---

## 7a-1 — long field names, read

Design: `DESIGN.md` §7a-1. The one place in this step where the corpus grows: by addition, and every
existing file regenerates byte-identical.

### Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | `DbfHeaderTests`: on `0x30`, `0x03`, `0xF5`, `0x32` with bytes 12–19 set to **1**, every flag reads false (the existing test sets 9, which proves nothing); on `0x31` unchanged. `FieldDescriptorTableTests`: the long run decoded — names of 10, 11 and 40 characters, `_NullFlags` in it, the tail's fields equal to the short layout's for the same field; the end at `0x0D` and at two bytes before the region's end; refused for a length below 1, a name past the region, a tail past the region. The parser: a name past ten characters resolves when the metadata says long names and is refused otherwise | the decoding rule and every branch of the run's end, with no file |
| 2 Component | a long-layout table image read by the real opener: `Fields["…"]` by a long name in any case, `Query`/`CompileFilter` over a long name and over `ALIAS->LONG_NAME`. `DbfOpener` over a `0x30` image with byte 16 = 1 opens instead of being refused | that the flag reaches the compile path — the defect the unit layer cannot see |
| 3 Fault injection | covered at layer 1: the region's malformed shapes | — |
| 4 Golden | `LONGNAME`: header and descriptors, records, the index (keys equal the reference's), the query dumps at every depth the harness compares. `FLAGS30`: header, descriptors and records equal to the reference's | that the layout and the names are read as the reference reads them, and the tags over them open and answer |

**Expected values.** Layer 1 and 2 expectations are the values the test put into its own input —
names, types, lengths — never bytes typed from the spec. Every reference fact comes from the two new
corpus cases.

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | **Flags on `0x31` only.** Correct the not-extended header test to set the bytes to 1 (red), then `DbfHeader`/`FeatureFlags` (green). Layer-2: a `0x30` table image with byte 16 = 1 opens and reads its short descriptors | red, then green; mutating the version check reddens a layer-1 test |
| b | **The long run.** `FieldDescriptorTableTests` for the long layout (red; the old "rejected" test is replaced), then the shared tail factory and the long run (green) | green; `crap4csharp` on `FieldDescriptorTable.cs`, `FieldDescriptor.cs` |
| c | **The flag on the metadata.** `TableFieldMetadata`'s required argument, `ExpressionParser` reading it, the four library sites passing the header's flag, test sites passing `false` or the table's flag. Layer-2 tests over the long-layout image (red before the sites pass it) | green; removing the flag at any one site reddens a test |
| d | **The generator.** `case-longname.cpp`, `case-flags30.cpp`, `dump.cpp` printing the long layout; registered in `cases.h`/`main.cpp`. Built and run on Windows; the corpus regenerated | `git status net/corpus`: only the new cases' files appear; every existing file byte-identical |
| e | **The golden gates** for both cases, through the existing golden tests' shapes (header/descriptor, records, index, query) | green; with step b's long run reverted to the refusal, `LONGNAME` reddens |
| f | **Records.** ADR-72; `DBF-FORMAT.md` §2.2 (the version rule stated as the port now follows it) and §4.2 (the run's end); `EXPRESSIONS.md` if its field-name row needs it; `net/corpus/README.md`, `test-files-generator/README.md`; `DIVERGENCES.md`'s row → supported; `PORTING-PLAN.md` §5 for `DBF-READ`; `mutate4csharp` on `FieldDescriptorTable.cs` and `FeatureFlags.cs` | every survivor explained |

### Gate

```bat
cd net
dotnet test CodeBase.Net.sln -c Release
git status --short corpus      &:: only LONGNAME.* and FLAGS30.* — no existing file changed
```

### Risks

- **The flag reaching some paths and not others.** The required constructor argument is the guard;
  `LONGNAME`'s tags exercise the open-time and seek paths, its filters the compile path.
- **The `0x30` change unrefusing something the port relies on.** Only two library sites read a
  flag: the long-name layout and ADR-56's compressed-data refusal, which already requires `0x31`.
  `FLAGS30` sets every byte, so the corpus witnesses both.
- **`dump.cpp` changing an existing dump.** Its new branch is taken only on `0x31` with `flags[4] = 1`;
  sub-step d's byte-identical check catches the rest.

---

## 7a-2 — a tag over `RECNO()`

Design: `DESIGN.md` §7a-2.

### Tests

| Layer | Tests | Catches |
|---|---|---|
| 1 Unit | `SeekConverterKindTests`: a `RECNO()` tag resolves (numeric, 8 bytes); `DATE()`, `TIME()`, `RECCOUNT()` still refused, each naming its function | the refused set |
| 2 Component | a `RECNO()` key rebuilt for a record is that record's number, and differs between two records (over `ExpressionValueSource`) | the number reaching the context — what layer 4 alone would show only as a mislanded cursor |
| 4 Golden | `RECNOTAG`: every stored key rebuilt from its record (`KeyTransformGoldenTests`), index, seek and seek-type gates, the query gates | the reference's keys and answers |

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | Tests for the refused set and the key's number (red), then `SeekConverter`, `IKeyValueSource`, `RecordKey`, `TableTagCursor` (green) | removing the number from the context reddens the layer-2 test |
| b | Generator: `case-recnotag.cpp`; regenerate; existing files byte-identical | `git status net/corpus`: additions only |
| c | Golden: `RECNOTAG` into the index, seek, key-rebuild and query lists; counts raised by exactly the new case | green; reverting `Read`'s number reddens `KeyTransformGoldenTests` |
| d | Records: ADR-73 (supersedes ADR-49 in part); `Table.SelectTag` XML doc; `README.md` "Differences from CodeBase"; `EXPRESSIONS.md` if it lists the refused set; corpus READMEs; `DIVERGENCES.md`; `mutate4csharp` on `SeekConverter.cs`'s refusal and `RecordKey.cs` | survivors explained |

---

## 7a-3 — a tag keyed through the date picture

Design: `DESIGN.md` §7a-3.

### Tests

| Layer | Tests | Catches |
|---|---|---|
| 1 Unit | `SeekConverterKindTests`: `DTOC(K_DATE)` and `CTOD(K_TEXT)` refused under the unchosen options, naming the function; accepted under `ExpressionOptions.Invariant` and `.CodeBase` chosen; an expression without either accepted unchosen. `ExpressionOptions`: the default is unchosen, every factory and `With*` result chosen | the rule |
| 4 Golden | over `CDXEXPR`: default engine — `T_DTOC` refused, absent from `QueryTags`, and a `DTOC` filter's count equal to the per-record count; reference picture — for each distinct stored `T_DTOC` key, the filter naming it counts as many records as the reference stored under it, by tag and per record alike | the wrong record set, against the reference's keys |

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | The tests above (red), then `ExpressionOptions`, `CodeBaseEngine`, `SeekConverter` (green) | each red first; mutating the check reddens both layers |
| b | Golden tests that select every tag under default options, if any now meet the refusal: configure the picture the dump was taken under, as `ExpressionTagGoldenTests` does | Release green |
| c | Records: ADR-74; `CodeBaseEngine.ExpressionOptions` and `Table.SelectTag` docs; `README.md` "Differences from CodeBase"; `DIVERGENCES.md`; `mutate4csharp` on `SeekConverter.cs`'s new check | survivors explained |

---

## 7a-4 — the `'7'` field

Design: `DESIGN.md` §7a-4.

### Tests

| Layer | Tests |
|---|---|
| 1 Unit | `FieldResolver` admits `'7'` on `0x30` and refuses it on `0x03`/`0xF5`; `FieldValueDecoder` and `BlankRecord` treat it as `'T'`; `FoxDateTime.ToText` with milliseconds; `KeyTransform.FromDateTime` keeping milliseconds differs from rounding at 600 ms and agrees at 0 |
| 2 Component | a `'7'` table image: `GetDateTime` keeps milliseconds; an expression over the field loads `TimestampWithMilliseconds`; a key rebuilt for a record keeps its milliseconds |
| 4 Golden | `DTMILLI`: records and text against the dump, every key rebuilt (`KeyTransformGoldenTests`), index, seek and seek-type gates |

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | Read: resolver, decoder, blank, text (tests red, then green) | mutating each reddens a test |
| b | Expressions and keys: loader row, value type, `FromDateTime`'s flag, `ExpressionKey`/`SeekConverter` | the 600 ms key test red before |
| c | Generator: `case-dtmilli.cpp`, `dump.cpp` printing `r4dateTimeMilli` like `r4dateTime`; regenerate, existing files byte-identical | `git status net/corpus` additions only |
| d | Golden: `DTMILLI` into the lists, counts by exactly the case | green; dropping the flag reddens the key rebuild |
| e | Records: ADR-75 (supersedes ADR-32); `DBF-FORMAT.md` §5, `KEY-COLLATION.md` §2.8 as witnessed; corpus READMEs; `DIVERGENCES.md`; `PORTING-PLAN.md` §2.3; `mutate4csharp` on `KeyTransform.cs` | survivors explained |

## 7a-5 — the `'W'` and `'O'` Unicode fields

Design: `DESIGN.md` §7a-5.

### Tests

| Layer | Tests |
|---|---|
| 1 Unit | `FieldResolver`: `W`/`O` admitted on `0x30`, `0x31`, `0x03`; `W` refused on `0x32`, `O` admitted there. `BlankRecord`: `00 20` pairs. `FieldValueDecoder`: `O` less two bytes. `WideNarrowing`: one byte per code unit, a supplement code point (U+2000 on 1251 gives a space), best-fit through the encoding (U+0100), `?` for the unmappable and for each half of a surrogate pair, a multi-byte code page refused; and that every supplement entry is one .NET does not already map (the supplement's own premise). `ExpressionKey`: a wide value swaps each unit. `SeekConverter`: a wide tag under GENERAL refused; `ASCEND(K_W)` refused. Parser and resolver: `K_W`, `K_O` load with their lengths; `STR(K_W)` length `len / 2`, `STR(K_W, 3)` length 3 (the reference folds the width and picks the wide row by type, found while writing the case); `STR(K_O)` and `STR(K_W, 11)` (past the field) refused |
| 2 Component | a wide table image: `GetString` with its U+2000 tail; an expression over `STR(K_W)` on a cp1251 image; a key rebuilt for a record; `Seek` exact and prefix on a wide tag |
| 4 Golden | `WSTR`: layout, records (raw bytes and `GetString`), every key of all four tags rebuilt, index and key-level seeks, a seek by each record's own text, the queries |

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | Supplement tool: `test-files-generator/bestfit/` (C# console, Windows only) writes `WindowsBestFitSupplement.cs`; its README | its output checked in; rerun reproduces it |
| b | Read: variant, resolver, blank, decoder, `GetString` (tests red, then green) | mutating each reddens a test |
| c | Expressions: loader rows and lengths, `STR(W)` parse, `WideNarrowing`, context wiring | the U+2000 narrowing test red without the supplement |
| d | Keys and seeks: `KeyKind.WideCharacter`, pad byte, `ExpressionKey`, the refusals, `SearchFor` and the text seek | the swap test red before |
| e | Generator: `case-wstr.cpp` (checks `GetACP() == 1251`), queries; regenerate, existing files byte-identical | `git status net/corpus` additions only |
| f | Golden: `WSTR` into the lists; counts by exactly the case | green; dropping the swap or the supplement reddens a golden test |
| g | Records: ADR-76, ADR-77; `DBF-FORMAT.md` §5 (W, O as witnessed), `KEY-COLLATION.md` (wide machine keys), `EXPRESSIONS.md` (`STR(W)`); corpus READMEs; `DIVERGENCES.md`; `PORTING-PLAN.md` §2; README "Differences from CodeBase" (`STR(W)` by the table's code page, Blob on `0x32`); `mutate4csharp` on `WideNarrowing.cs` | survivors explained |

### Risks

- **The supplement assumes .NET's code-page tables.** A later `System.Text.Encoding.CodePages` that
  best-fits more would make an entry redundant, not wrong; one that best-fits less would be caught by
  the premise test only for entries already listed. The golden `S_ASC` keys catch the cp1251 case.
- **The case depends on the generating host.** Regenerating on a machine whose ANSI code page is not
  1251 fails at `WSTR` by design rather than writing different keys.

## 7a-6 — the VFP oracle, run unattended

Design: `DESIGN.md` §7a-6. No library code and no .NET test: the tool is verified by running it.

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | `run-in-vm.ps1` | run on `vfpfunc.prg`: `done.txt` is `OK`, and `out\` equals the hand run of 2026-11-14 byte for byte but the dump's `generated` line |
| b | Failure paths | a script with an untrapped error exits non-zero and prints VFP's message; a script stopped at a `MESSAGEBOX` is killed at a short `-TimeoutSeconds` and leaves no `vfp9.exe` in the guest; an unset variable is named |
| c | Publish the second run: `copy-vfp-corpus.bat` | `git status net/corpus` shows `VFPFVAL.DBF` and the two dumps only; the suite green in Release |
| d | Records: `vfp/README.md` (running it, and what the second run settled), `FOR-DEVELOPERS.md`, `net/corpus/README.md` if it dates the run, this step's `STATE.md` | — |

### Risks

- **`vmrun` blocks on a hung guest program** whatever its own options say; the timeout is the
  `.ps1`'s, around the `vmrun` process, and the kill goes through `vmrun killProcessInGuest`.
- **The guest user name may not be ASCII** (it is not on the user's VM); it is passed from the
  environment as a string, never through a code-page conversion of the script's own text.

## 7a-7 — a VFP-written `T` table

Design: `DESIGN.md` §7a-7.

### Tests

| Layer | Tests |
|---|---|
| 4 Golden | `VisualFoxProDateTimeGoldenTests`: every stored key rebuilt from its record; the oracle's shape (count, every second of the base day once, seven days, both bitmap halves in each binade) |

No layer 1–3 test: nothing in the library changes unless the oracle disagrees, and then the step
stops for the user's decision before any code.

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | `vfptime.prg`; run by `run-in-vm.ps1` | `done.txt` OK; sizes measured — over ~2 MB widens the stride and reruns |
| b | Publish: `copy-vfp-corpus.bat` | `git status net/corpus` shows the three new files only |
| c | Golden: `VisualFoxProDateTimeGoldenTests` | green; flipping one bit of the port's bitmap copy reddens the rebuild test, and so does dropping the half-second rounding |
| d | Records: `vfp/README.md` (what the run settled), `net/corpus/README.md`, `KEY-COLLATION.md` §2.8 and §9 item 1 (witnessed or not), `DIVERGENCES.md` if a key disagreed, this step's `STATE.md` | — |

### Risks

- **The CDX is bigger than estimated**: measured at (a), the stride is the knob.
- **VFP disagrees.** Not a failure of the step but its point; it changes (c) into a decision first.

### 7a-7, after the run (2026-11-14): the library changes

| Layer | Tests |
|---|---|
| 1 Unit | `KeyTransform`: a `T` key on every second of 2000-01-01 equals the bitmap path (the empirical fact the oracle found), the bitmap test kept for `'7'`, chronological order kept; `FoxDateTime.ToDateTime` rounds half up when asked and keeps milliseconds when not |
| 4 Golden | `VFPTIME`: every key rebuilt; `GetDateTime` a whole second on every record; `CDXTIME` and `DTMILLI` unchanged |

| # | Step | Verify by |
|---|---|---|
| c1 | `FromDateTime`'s `T` path; unit tests adjusted first (red on the bitmap test's other days) | VFPTIME rebuild green; CDXTIME, DTMILLI green; dividing instead of multiplying, or keeping the bitmap, reddens VFPTIME |
| c2 | `GetDateTime` rounds `T` | unit red then green; golden on VFPTIME; keeping milliseconds reddens it |
| d | Records as planned, plus ADR-78, ADR-79, `KEY-COLLATION.md` §2.8 and §9 item 1 rewritten, `DBF-FORMAT.md` on VFP's `.999` | — |

## 7a-8 — `Seek(decimal)` and `Seek(DateTime)`

Design: `DESIGN.md` §7a-8.

### Tests

| Layer | Tests |
|---|---|
| 2 Component (`TableTypedSeekTests`, table images) | each overload finds its key; a wrong kind of tag refused naming the kind; text on a currency or datetime tag names the new overload; a fifth decimal and a sub-millisecond `'7'` value refused with the cursor unmoved; a `T` seek rounds 10:00:00.500 to 10:00:01 and .499 down; a `'7'` seek keeps milliseconds; `SeekAtOrAfter`/`SeekAtOrBefore` between two keys land After/Before |
| 4 Golden (new `CurrencyAndDateTimeSeekGoldenTests`) | every record of `CDXEXPR.T_CUR`, `CDXTIME.T_TS` and `T_TSD`, `DTMILLI.M_ASC` and `VFPTIME.TS` sought by its own value through the public API, landing on a record with that value — VFPTIME's 100 422 including every second where the old bitmap was off; positional seeks half a step past each `CDXTIME` value |

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | Component and golden tests, red | they fail to compile, then fail |
| b | `SearchForCurrency`, `SearchForDateTime`, the six overloads, `TypedSeekFor` | green; mutations: dropping the `'7'` branch, dropping the fifth-decimal refusal, rounding `T` down — each reddens a test |
| c | Records: ADR-80; `README.md` and `STATE.md` §1 where they list the overloads; XML docs | `crap4csharp` on the changed files; `mutate4csharp` on `TableTagCursor.cs` |

## 7a-9 — VFP's own functions where they have no ordinary answer

Design: `DESIGN.md` §7a-9. Every expectation below is a row of `VFPBAND.vfp.dump.txt` or a key in
`VFPBAND.CDX`; a test cites the row it restates.

### Tests

| Layer | Tests |
|---|---|
| 1 Unit (`VisualFoxProFunctionTests`, and a new `LazyEvaluationTests`) | `MOD` by `0`, `0.0` and `-0` raises `ExpressionEvaluationException` with `Code` `Result` and `VisualFoxProError` 1307; `MOD(5, 1E-300)` does not; `SQRT` of `-1` and `-1E-300` raises with 61, `SQRT(-0)` is 0; every `[guard]` row in the port's syntax (`.AND.`, `.OR.`, `.NOT.`) — the unchosen `IIF` branch and the operand after a deciding one are not evaluated, a raising left operand raises, `MIN` evaluates all; over a record whose nullable fields are null, each of the thirteen in each argument position answers null with the zero or blank value, and `MOD(N_, 0)` raises; an `.AND.` of three evaluates left to right |
| 2 Component (`ResidualFilter`, a table image with a negative, a zero and a null) | `K >= 0 .AND. SQRT(K) > 1` and `K <> 0 .AND. MOD(10, K) = 0` count 2 and 0 with no tag; written the other way round they raise; `IIF(K < 0, 0, SQRT(K)) > 1` counts 2; the exception reaches the caller of `Query.Count` and `Query.Top` unwrapped |
| 4 Golden (new `VisualFoxProBandGoldenTests`) | every key of `VFPBAND`'s thirteen tags rebuilt from its record through the library's path; the oracle's shape — six records, thirteen live tags, a null in each field — so a reader that found nothing fails |
| 4 Golden (all) | the whole suite unchanged: the lazy walk must not move a single value |

### Sub-steps

| # | Step | Verify by |
|---|---|---|
| a | Publish the oracle: `copy-vfp-corpus.bat`; `vfpband.prg` checked in | `git status net/corpus` shows `VFPBAND.DBF`, `.CDX`, `.vfp.dump.txt` only |
| b | Tests above, red | they fail to compile, then fail — the golden on exactly the null records |
| c | The recursive walk, still evaluating every operand | the whole suite green, unchanged — the refactor alone |
| d | Laziness in `IIF`, `.AND.`, `.OR.`; `MOD` and `SQRT` raise; `ExpressionEvaluationException`, `ErrorCode.Result` | layer 1 green; mutations: evaluating both branches, or not stopping in `.AND.`, reddens the guard tests; testing null before `MOD`'s zero reddens `MOD(N_, 0)` |
| e | Null through the thirteen | `VFPBAND` golden green; dropping the null test from any one function reddens its tag |
| f | `ResidualFilter` in source order | layer 2 green; walking backwards reddens it |
| g | Records: ADR-81, ADR-82; `EXPRESSIONS.md` (evaluation order, the raising pair, null in the band); `DIVERGENCES.md` row closed; `vfp/README.md` (what `vfpband.prg` settled), `net/corpus/README.md`; XML docs (docgen rules) on the exception, `ErrorCode.Result`, `Query`'s movement and `Count`; this step's `STATE.md` | `crap4csharp` on the changed files. No `mutate4csharp` here — the hand-placed mutations of d–f are the check on a sub-step; the tool runs at the step's close (8), as `DEV_APPROACH.md` §4 has it (the user, 2026-11-15) |

### Risks

- **The walk costs time per record.** A recursion where there was a loop, on the path every residual
  check takes. Measured with `CodeBase.Net.Profiling` on a `PERF10K` filter no tag answers, before c
  and after d; recorded in `STATE.md`. A loss beyond noise goes to the user before f.
- **Operand order is not source order somewhere.** The decomposer visits operands backwards
  (`QUERY-OPTIMIZER.md` §3); evaluation must not. The `.AND.`-of-three test pins it.
