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
