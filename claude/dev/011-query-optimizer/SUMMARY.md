# 011-query-optimizer — summary

**A filter over one table becomes a record set produced by seeking indexes instead of reading them.**
`UPPER(NAME) = "SM" .AND. SALARY > 50000` decomposes into per-tag range constraints, each is answered
by a CDX seek, the record-number sets are combined, and only the terms no tag could answer are
evaluated — on the candidates that survive, not on the table. This is what the whole port exists for
(`CLAUDE.md`, `PORTING-PLAN.md` §1). Capability `QUERY`, **done 2026-09-04**.

```csharp
table.SelectTag(table.Tags["NAME"]);
Query q = table.Query("UPPER(NAME) = \"SM\" .AND. SALARY > 50000");

for (GoResult go = q.Top(); go == GoResult.Ok; go = q.Next())
    Console.WriteLine(table.GetString(name));

int howMany = q.Count;      // reads no record at all when the tags answer the whole filter
```

## The gate, and what it says

Three gates that fail differently. Two are correctness; the third exists because **neither of them
can see an optimizer that never optimizes** — a query that decomposes nothing and scans every time is
perfectly correct.

| Gate | Assertion | Result |
|---|---|---|
| **A** — equivalence | every filter's records equal a scan of the same compiled filter, in record order and in every tag's order, in **both** tag scopes | 81 corpus filters + 18 written, over **four** tables |
| **B** depth 0 | the reference's own scan truth, its optimizer disabled | the baseline the rest are read against |
| **B** depth 1 | per node, the tag matched equals the reference's `infoReport[i].tag` | every node of all 81 filters |
| **B** depth 2 | `FullyMapped` equals the reference's, asserted unconditionally | all 81 |
| **B** depth 3 | the candidate set equals `relate->set`, **before any residual filtering** | all 81 |
| **B** depth 4 | the record sequence and `Count` equal `relate4top`/`skip`/`count` | all 81, both orders, and backwards |
| **C** — work | a fully mapped `Count` reads **zero** records; a fully mapped walk reads **exactly what it returns**; a selective query costs a fraction of a scan; leaf count is seek count | exact numbers, not thresholds |

**3260 tests green** (799 golden, 2461 unit), up from 1909 at the start of the step.

## What shipped

`net/CodeBase.Net/Queries/` — `QueryTagScope` (public), `QueryTag`, `TagEligibility`, `SubtreeMatch`,
`ConstraintValue`, `LeafConstraint`, `ConstraintMerge`, `ConstraintNode`, `QueryPlan`,
`QueryDecomposer`, `RecordSet`, `ITagProbe`, `TagProbe`, `ConstraintSeeker`, `QueryExecutor`,
`ResidualFilter` — and the public `Query` beside `Table`, with `Table.Query`,
`CodeBaseEngine.QueryTagScope` and `ErrorCode.Relate`.

The corpus gained three `*.query.dump.txt` files (81 filters over `CDXEXPR`, `CDXBASE`, `CDXDEEP`),
written by `test-files-generator/src/dump-query.cpp`. **No existing corpus file changed at any point
in the step** — every regeneration was byte-identical outside the query dumps.

## Deviations from the design

- **The merge algebra moved from sub-step 4 to sub-step 6**, so that depth 3 was green before it
  landed. Merging is an optimization, not a correctness requirement: unmerged, two terms over one tag
  are two leaves that seek twice and intersect. It paid off the day it was meant to — see below.
- **The decomposition walk landed in sub-step 2, not 3.** `e4isTag` is called only from
  `bitmap4can`'s comparison branch, so the tag array is a property of the walk and depth 1 cannot be
  satisfied without it.
- **The namespace is `CodeBase.Net.Queries`**, because a class cannot share a name with its own
  namespace and the caller-facing `Query` is the name a reader sees.
- **The oracle records five depths, not four** — depth 0, the reference's own scan truth, was added
  during design (Decision 12) and is what makes ADR-53 checkable.
- **`AllSeekable` widens the type gate only** — ADR-54 supersedes ADR-52's collation half.

## What this step proved

- **The reference has a defect that returns a disjoint record set**, and it is witnessed rather than
  inferred: `K_INT > 0 .AND. 4 > K_NUM` returns `{3, 9, 11, 12, 14, 15}` optimized and `{5, 6, 8}`
  scanned. The bound was *inverted*, not loosened. One filter of 81. ADR-53's choice is evidence now.
- **An `H`-field comparison is never optimizable**, and a **date or logical tag is** — the type gate
  is a blacklist of four, not the shortlist of three its own comment names.
- **A skipped tag does not end the search**: the walk continues past a refusal to the next candidate.
- **The short-constant widening is exact, not a safety margin**, which is what lets a widened leaf be
  trusted with no residual re-check.
- **A leaf's set does not depend on the connective above it** — every leaf starts from a zeroed
  bitmap — so one routine does what the reference needs four for.
- **A collated tag may never answer a query**, in either scope, and the measurement says why.

## Deferred

- **Multi-table relations.** `PORTING-PLAN.md` §2.3. The one place in the code that would need
  widening is named in `SubtreeMatch.SameField`.
- **The go-around complement optimisation** (Decision 8), and the selectivity sweep over `PERF10K`
  settles it with a number rather than a preference. Reads against a full scan of 10 000 records:
  0.1% selectivity → **0.001×**, 10% → **0.101×**, 50% → 0.506×, and **100% → 1.012×**. The
  crossover is at *total* selectivity and the loss there is **1.2%** — the index descent and the leaf
  walk, about 121 reads. The go-around could recover at most that. Not worth building.
- **`bmf4OrOt` is still untranscribed**, but its *answers* are now witnessed on all five shapes the
  corpus reaches (`QUERY-OPTIMIZER.md` §12 item 7, narrowed twice).
- ~~A field qualified by the table's own alias is refused~~ — **fixed 2026-09-04**, after the step
  closed. The table's own name now reaches the scanner in both paths, so `CDXEXPR->K_NUM` resolves
  and a foreign alias still fails with the reference's own code. Fixing it also found that
  `QueryHarness` carried its **own copy** of the compile path, which is how the suite stayed green
  while the real one was broken; it goes through `Table.CompileFilter` now.
- **Two corpus gaps named rather than papered over:** the filtered-tag refusal cannot be isolated by
  any dump (every filtered tag shares its field with a plain one), and a collated tag can only be
  reached where no machine tag shadows it.
  *Corrected 2026-10-29 (step 012):* the first gap did not exist — tags are visited alphabetically,
  `T_FILT` precedes `T_NUM`, so `CDXBASE` isolated the filtered refusal all along; it was the
  **unique** refusal nothing showed (`T_DUP` precedes `T_UNIQ`). And the reference never refuses a
  collated tag at all: that refusal is compiled out (ADR-59). `QRYGATE` now isolates all three.

## For the next step

**Gate A is the one to keep pointing at new work.** It computes its own expectation, so it is the
only check that applies where the reference has no answer — and on its first run it found a wrong
record set that every reference-backed gate was blind to, because the tag involved is one the
reference refuses. `PORTING-PLAN.md` §5 calls it non-negotiable; it earned that.

**The reorder is the transferable lesson.** Building the merge algebra *after* the record sets were
green meant depth 3 went red the moment merging landed, and it caught two real bugs on the spot — one
of them a shared-buffer alias that nothing before merging could have exposed, because a range with
two ends is the first thing that holds two comparands at once.

**Three mutations this step were invalid before they were valid** — unreachable code the compiler
refused, and a field read while its own initializer was still running. A mutation that does not
execute proves nothing, and an empty failure list looks identical to a pass. Every mutation run
should distinguish a build failure from a green suite.
