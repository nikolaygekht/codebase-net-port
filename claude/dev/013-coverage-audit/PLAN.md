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
