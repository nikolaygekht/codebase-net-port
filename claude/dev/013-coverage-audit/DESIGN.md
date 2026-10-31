# 013-coverage-audit — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.
The input is [`AUDIT.md`](AUDIT.md), the first `crap4csharp` run over steps 001–011.

## Goal

Give every piece of logic that only the Golden layer reaches a test at its own level — the query
pipeline first, then building a key from a value, the expression engine's untested kernels, and
seeking through a table's tag — so that a break there reddens a unit or component test, not only a
corpus gate. **Verified when a `crap4csharp` rerun shows no member of findings F1–F4 at 0% unit
coverage without a recorded reason, `dotnet test` is green, and no golden expectation or corpus file
changed.**

**Capability:** none opened — a remediation numbered like a step, in the shape of
[`007-audit-glm`](../007-audit-glm/) and [`012-p1-tails`](../012-p1-tails/). It hardens `QUERY`,
`COLLATION`, `EXPR` and `CDX-READ`. **Governing specs:** `QUERY-OPTIMIZER.md`, `EXPRESSIONS.md`,
`KEY-COLLATION.md`, `CDX-FORMAT.md` §7.

## What changes, in short

- **Now:** the audit counted 129 members at 0% unit coverage and 93 above CRAP 8.0. The query
  pipeline between a compiled filter and its record set — decomposition, execution, the residual
  filter, the result cursor — is reached only through `QueryHarness` and the golden gates. 012's
  close showed what that costs: `Table.Seek` found nothing in any nullable tag from step 010 on, in a
  method no unit test reaches.
- **After:** each finding F1–F4 has tests at layer 1 or 2, written against the contract the class
  promises, over the seams that already exist. Defects the new tests find are fixed in the step,
  test first. A fresh baseline at the start and a rerun at the close give the before and after.
- **Library code:** changes only where a test finds a defect, or where a member cannot be reached
  without a corpus file — the §4 untestable-design signal, answered with a seam. None is expected:
  the seams below exist already.

## Not in this step

- **F6** — members fully covered and simply long (`CodePageMap.NumberFor`, `ParseFunction`, …). Their
  CRAP equals their CC; there is nothing to test that is not tested. Recorded for the threshold
  decision ADR-60 left open, and left there.
- **A CRAP or mutation threshold as a gate.** ADR-60 defers it until a few steps have numbers; this
  step adds numbers, it does not decide.
- **New corpus cases.** The step tests below the corpus. If a test finds a path no corpus case
  covers and the answer depends on the reference, it becomes a generator case in a later step and
  is recorded, not improvised here.
- **Refactoring for its own sake.** A long member that is fully tested stays long (F6). A member is
  split only when its test cannot reach a branch otherwise.
- **The members from `AUDIT.md`'s "Smaller" list** (`DbfOpener.ReadFieldDescriptors`, `LeafGeometry`,
  `TagCursor.Seek`, `BlockResidency.Unchain`, …) — taken if a sub-step's tests reach them on the way,
  otherwise recorded.

## Scope, by finding

The member lists are `AUDIT.md`'s; sub-step 1 re-derives them from a fresh run, because 012 changed
the code under several of them.

| Finding | Classes | Layer | Over which seam |
|---|---|---|---|
| **F1** the query pipeline | `QueryDecomposer` (and its `Walk`), `QueryPlan`, `TagEligibility.CanExpress`/`IsWhole`, `LeafConstraint`/`ConstraintValue`/`ConstraintNode` | 1 | hand-built `FieldDefinition`s and `QueryTag`s compiled from tag expressions — pure |
| | `QueryExecutor`, `ResidualFilter`, `TagProbe` | 2 | `ITagProbe` faked by `SortedKeyProbe`, which `TagProbeContract` already holds to the real probe; `TagProbe` over `IndexImage`-built tags |
| | `Query` — `Top`/`Bottom`/`Next`/`Previous`/`Skip`/`Count`, record and tag order | 2 | `IndexedTableImage`: a table and its CDX in memory |
| **F2** keys from values | `RecordKey`, `CollationWeights`, `KeyTransform.FromDateTime`/`DecrementLowByte`, `SeekConverter.KindOf`/`RefuseIfNotTheSameTwice` | 1 | pure; headers built with `IndexImage` |
| **F3** the expression engine | `ExpressionEvaluator` — `PictureDateText`, `FormatNumber`, `Rounded`, `TimestampText`, `Count`, `MemoBytes`, `LoadField`; `ExpressionKey` — `Write`, `WriteValue`, `Length`; `ExpressionValue.ToRaw` | 1 | constants, and records built in memory as `ExpressionEvaluatorTests` already does |
| **F4** seeking through a tag | `TableTagCursor.SearchFor` (every branch: character whole and prefix, collated whole and prefix, conversion, the null byte, the refusal), `TypedSeekFor`; `Tag`'s properties | 2 | `IndexedTableImage` |
| **F5** the public API's forwarding | every public member of `Table`, `Tag`, `FieldCollection`, `CodeBaseEngine` at 0% — one test each that it reaches its target (ADR-67); `Table.QueryTags` and `Table.ReadMemo` for their own logic too | 2 | `TableImage`, `IndexedTableImage` |

## Classes

No new production class is planned. Test-side, as needed:

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `QueryDecomposerTests` *(test)* | — | what plan a filter decomposes into over given tags | layer 1 |
| `QueryExecutorTests` *(test)* | — | the record set a plan produces over probes | layer 2, `SortedKeyProbe` |
| `ResidualFilterTests` *(test)* | — | which terms a plan leaves to the per-record check, and its verdicts | layer 1/2 |
| `QueryCursorTests` *(test)* | — | how a query moves over its result, in both orders | layer 2, `IndexedTableImage` |
| test helpers | — | building `QueryTag`s and compiled filters from text over hand-built fields | in `CodeBase.Net.Tests`, not `TestUtils`, unless the golden suite needs them too |

## Public surface

**None changed**, unless a defect fix needs it — and then the fix says so.

## Seams

All exist; this step uses them.

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| a tag's keys, for the executor | `ITagProbe` | `SortedKeyProbe` (TestUtils), contract-tested against the real probe | execution, merged ranges, empty and full sets |
| a table with an index | `IRandomAccessSource` behind `CodeBaseEngine`'s internal constructor | `IndexedTableImage`, `TableImage` | `Query`, `TableTagCursor`, `Table.QueryTags` |
| a tag header | `IndexHeader.Parse` over bytes | `IndexImage.SingleTag` | `SeekConverter`, `TagProbe` |
| field metadata | `IFieldMetadata` / `FieldDefinition` | constructed directly | parsing, resolution, decomposition |

## Decisions

1. **Tests assert the contract, from the specification or the corpus.** A decomposer test states the
   plan a filter becomes — which terms a tag answers and over what range — as `QUERY-OPTIMIZER.md`
   defines it; a key test takes its expected bytes from a corpus file at a cited offset, or states a
   round-trip or ordering invariant, never bytes typed from a spec (§4). Where only the reference
   could say, the test is not written here and the case is recorded.
2. **A defect a new test finds is fixed in this step**, test first, and recorded with its cause —
   as 012 fixed the nullable seek. If the fix needs the reference's answer and no corpus case has it,
   it is recorded and goes to a generator case instead.
3. **The fresh baseline replaces `AUDIT.md`'s numbers for this step.** `AUDIT.md` stays as written;
   the new run is filed beside it and the gate compares against it.
4. **F5 — decided by the user (ADR-67).** Nothing becomes public for a test; tests reach internal
   classes through `InternalsVisibleTo`, and any seam this step needs is `internal`. **Every public
   member that forwards gets at least one test** proving it reaches the right place — a call through
   the public member whose result only the intended target produces, never a `Verify` on a mock —
   and exhaustive coverage of the target stays in the target's own tests. Members with logic of their
   own (`Table.QueryTags`, `Table.ReadMemo`) get tests of that logic as well.

## Open questions

1. Whether the fresh run moves anything between findings — answered by sub-step 1.
