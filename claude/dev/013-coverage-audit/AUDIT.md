# 013 — what the unit layers do not cover: a CRAP audit of steps 001–011

**Recorded:** 2026-10-30. **The step itself starts after 012 closes**: this file is its input, and
its `DESIGN.md` and `PLAN.md` come from the template then. Clean this up first, move on after.

## What was measured, and how

[`crap4csharp`](../../../tools/README.md) at `9e155ec`, run once from `net/` over all 119 library
files, on the tree as committed in `5bdba5a` (whose `net/` is that of `9d09d1f`). The tool runs
`CodeBase.Net.Tests` only — layers 1–3 — in Debug: 2491 passed, 3 skipped, the clock gates among the
skipped. **The Golden layer is not counted**, by design: §4 of `DEV_APPROACH.md` says code that only
layer 4 reaches has an untestable design, and that is what this measures.

Each member is attributed to **the step whose commit created its file**. That is coarse in one known
way: a file later steps extended counts under the step that created it. `Table.cs` is the case that
matters — created in 001, extended by nearly every step since — so its members are read as the
public facade (F5), not as 001's debt. Steps 007 and 012 created no library file and have no row.

| File | What |
|---|---|
| `crap-report.txt` | The tool's report, as printed, with the test-run line above it |
| `crap-by-step.tsv` | One row per member: step, file, member, class, CC, coverage, CRAP |
| `crap-by-step.py` | Joins the report to the steps; its docstring has the commands that feed it |

Not measured: **mutation**. `mutate4csharp` has only been run with `--scan`.

## By step

| Step | Members | CRAP over 8 | At 0% | Highest |
|---|---|---|---|---|
| 001 | 141 | 6 | 40 | `CodePageMap.NumberFor` — CC 28, 100% |
| 002 | 47 | 3 | 1 | `FoxDate.DayOfYear` — CC 11, 100% |
| 003 | 9 | 2 | 0 | `MemoReference.ReadDigits` — CC 12, 100% |
| 004 | 79 | 3 | 1 | `TagCursor.StepPhysical` — CC 26, 90% |
| 005 | 6 | 1 | 0 | `KeySearch.Compare` — CC 10, 95% |
| 006 | 43 | 4 | 11 | `TableTagCursor.TypedSeekFor` — CC 5, 0% |
| 008 | 36 | 9 | 10 | `RecordKey.WriteValue` — CC 8, 0% |
| 009 | 46 | 0 | 1 | `CachingRandomAccessSource.Read` — CC 7, 94% |
| 010 | 161 | 33 | 21 | `ExpressionEvaluator.Apply` — CC 114, 88% |
| 011 | 94 | 32 | 44 | `QueryDecomposer.Walk.Constrain` — CC 18, 0%, CRAP 342 |
| **all** | **662** | **93** | **129** | |

The over-8 column mixes two different things, which the findings separate: complexity no unit test
reaches (F1–F4), and complexity that is fully tested and simply large (F6).

## Findings

### F1 — the query pipeline has no unit test (011)

Everything between a compiled filter and its record set is reached only through Golden
(`QueryHarness`, `DecompositionGoldenTests`):

- `QueryDecomposer` — `Decompose`, `Fold`, `Merged`, and all of `Walk`: `Constrain` (CC 18),
  `VisitComparison` (9), `VisitConnective` (8), `Visit`, `TryMatch`, `Widens`;
- `QueryExecutor` — `Reach`, `Evaluate`, `ProbeFor`; `QueryPlan.For`; `ResidualFilter` — `Terms`,
  `Admits`;
- `Query` — `Move`, `StepByRecord` (9), `StepByTag` (8), `Skip`, `Count`, `Prepare`, `Settle`,
  `Top`, `Bottom`, `RecordNumber`;
- `TagEligibility.CanExpress` (11) and `IsWhole` — the tests cover only `Allows`;
- `TagProbe.InTheTagsDomain` at 44%, `KeyFor` at 58%; `SubtreeMatch.Same` at 73%;
- `LeafConstraint.ToString` (11), `ConstraintValue.ToString`, `ConstraintNode.Walk`.

Covered at their own level, for contrast: `ConstraintMerge`, `ConstraintSeeker`,
`LeafConstraint.Accepts` — all 96–100%. The merge algebra was unit-tested; the decomposition and the
execution around it were not. Step 011 closed on three strong gates, all of them at layer 4.

### F2 — building a key from a value (008)

`RecordKey.WriteValue` — one overload at 0% (CC 8), the other at 35%; `RecordKey.DoubleOf` 0%.
`SeekConverter.KindOf` at 41% with CC 16, `RefuseIfNotTheSameTwice` at 53%. `CollationWeights` —
`Refuse`, `Supports`, `TableFor`, `ExpansionsFor` — all 0%. `KeyTransform.FromDateTime` and
`DecrementLowByte` 0%.

### F3 — the expression engine (010)

At 0%: `TypeResolver.AssignKeyConversionLength` (CC 11); in `ExpressionEvaluator`,
`PictureDateText` (10), `FormatNumber`, `KeyForm`, `Rounded`, `TimestampText`, `Count`, `MemoBytes`.
Partly: `ExpressionKey.WriteValue` 38% (CC 13), `ExpressionKey.Length` 69% (16), `ExpressionKey.Write`
36%, `ExpressionValue.ToRaw` 50%, `ExpressionEvaluator.LoadField` 71% (16).

### F4 — seeking through a table's tag (006)

`TableTagCursor.TypedSeekFor` 0%, `SearchFor` 37% with CC 8 — the method 012's sub-step 2 changed.
`Tag.ToString`, `Tag.Unique`, `Tag.Filter`, `Tag.Filtered`, `Tag.Collation` 0%.

### F5 — the public facade

About 40 members of `Table`, `Tag`, `FieldCollection` and `CodeBaseEngine` at 0%, nearly all CC 1:
the `Seek*` family, `Get*`, `GetMemo*`, `Query`, `CompileFilter`, the header properties. Each
forwards to a class that is unit-tested; only Golden calls the forwarding itself. CRAP is 1–6, so
**no threshold would ever flag them** — they show only in the coverage column. Two are not one-line
forwards: `Table.QueryTags` and `Table.ReadMemo`, CC 3 each.

### F6 — large and fully tested

CRAP over 8 at or near 100%: `CodePageMap.NumberFor` (CC 28), `ExpressionParser.ParseFunction` (31),
`TypeResolver.AssignStringLength` (34), `ExpressionLexer.ReadValue` (19), `ExpressionParser.LoaderRow`
(16), `ConstraintMerge.Narrow` (16), `FieldResolver.ResolveSize` (14), `MemoReference.ReadDigits`
(12), `FoxDate.DayOfYear` (11), and others in the TSV. At full coverage CRAP equals CC, so the
fixed 8.0 measures only the length of a `switch` over a format's cases. Not a coverage finding: it
matters only to whether a threshold ever becomes a gate, and says such a gate would need these
decoders exempted or counted apart. `ExpressionEvaluator.Apply` (CC 114, 88%) belongs here and in F3
both.

### Smaller

`DbfOpener.ReadFieldDescriptors` 36%; `LeafGeometry.Parse` 67% (CC 11); `TagCursor.Seek` 69%;
`BlockResidency.Unchain` 47%; `NodeReader`, `RecordPosition`, `Block` one member each at 0%.

## For 013's design to decide

Proposed, not decided:

- **Scope.** F1 to F4, by order of risk: F1 first — it is the library's headline feature and its
  logic has no test at its own level — then F2, F3, F4. Where a member cannot be tested without a
  table and a corpus file, that is §4's untestable-design signal, and the answer is a seam, not a
  Golden test.
- **F5** — unit tests for the forwarding, or a recorded decision that the facade is proved by
  Golden. Either way, `QueryTags` and `ReadMemo` get tests.
- **F6** — nothing to do; recorded here for the threshold decision.
- **Mutation.** The first full `mutate4csharp` runs, on the files F1–F4 touch, once their tests
  exist.
- **Gate.** `crap4csharp` rerun against this baseline: no member of F1–F4 left at 0% without a
  recorded reason, and no golden expectation or corpus file changed.
