# 011-query-optimizer — state

Live during phase 6 only. Short, current, and deleted-by-replacement rather than appended to
forever. Project-level state goes to the root [`STATE.md`](../../../STATE.md) and decisions to
[`ARCHITECTURE-DECISIONS.md`](../../ARCHITECTURE-DECISIONS.md), not here.

**Status:** **done 2026-09-04 — all nine sub-steps.** The record of the step is
[`SUMMARY.md`](SUMMARY.md); this file is the working log behind it.
**Current sub-step:** none. `QUERY` is closed. **The step order changed after sub-step 2** — see Deviations.

## Done so far

- [x] phases 1-5 — [`DESIGN.md`](DESIGN.md) and [`PLAN.md`](PLAN.md) written, 2026-09-04
- [x] step 1 — the query oracle, 2026-09-04 — **74 filters, five depths each, run and committed**
- [x] step 2 — eligibility and subexpression identity, 2026-09-04 — **depth 1 green, five mutations**
- [x] step 3 — decomposition, 2026-09-04 — **depth 2 green, four of five mutations red**
- [x] step 4 — leaf evaluation, 2026-09-04 — **the contract pair passes, 41 filters reach depth 3**
- [x] step 5 — combination, 2026-09-04 — **depth 3 green on all 74, four mutations red**
- [x] step 6 — the merge algebra, 2026-09-04 — **depth 3 stayed green, leaf counts fall**
- [x] step 7 — residual filter and public surface, 2026-09-04 — **depth 4 green, both orders**
- [x] step 8 — equivalence, widened scope, efficiency, 2026-09-04 — **Gate A found two wrong record sets**
- [x] step 9 — close, 2026-09-04 — the selectivity sweep settled Decision 8 with a number
- [ ] step 3 — decomposition
- [ ] step 4 — the merge algebra
- [ ] step 5 — leaf evaluation
- [ ] step 6 — combination
- [ ] step 7 — the residual filter and the public surface
- [ ] step 8 — the equivalence gate, and the widened scope
- [ ] step 9 — close

## What Gate A landed, and what it caught

`QueryEquivalenceGoldenTests`: every filter's record sequence compared against a **scan of the same
compiled filter**, in record-number order and in every tag's order, in **both** scopes — over four
tables, `CDXCOLL` included, which has no query dump at all and needs none because the expectation is
computed. **3246 tests green** (785 golden, 2461 unit). No corpus file changed.

**It went red on its first run, only under `AllSeekable`, and it was right twice.**

**The first was a crash rather than a wrong answer.** `TagProbe.KeyFor` built a key from what the
constant *is* instead of from what the tag's keys *are*: comparing an integer field against a number
promotes the field to a double, so the constant arrives as one, and writing it as a double put eight
bytes into a four-byte integer key. Fixed by restating the constant in the tag's domain before
building the key — which also had to keep going through `ExpressionKey.Write`, because bypassing it
dropped the null-indicator byte and quietly broke `T_NULL` under the **default** scope.

**The second is the one that matters, and it retires half of ADR-52.** Over `CDXCOLL` the filter
`K_TEXT > "M"` **lost six records** — `alpha`, `cafe`, `café`, `Æther`, `æon`, `aeon`. Each begins
with a byte above `M`'s, so the filter admits it; `GENERAL` collation sorts each *below* `M`, so a
range seek of the collated tag started past them. ADR-52 had widened the collation gate on the
argument that the reference's reason "is inert here, because the two orders never meet in one
answer". They do not have to: **the filter's meaning is byte order and the tag is another order**, and
answering the filter from the tag swaps them — with nothing downstream to put the records back,
because a term answered by a tag is never checked again. **ADR-54** makes collation structural,
refused in both scopes, and `AllSeekable` now widens the **type gate only**.

**Widening the type gate needed a refusal of its own**, which `Compatible` never reaches: the constant
must be expressible in the tag's key domain **exactly**. `K_INT >= 2.5` is not `K_INT >= 2`, so a
fractional constant against an integer tag falls back to being checked per record. The reference has
no equivalent because it never converts into those domains at all — which is the very thing it gives
those tags up for.

**Why nothing else could have found either.** Every other gate here compares against what the C
library answered, and under `Compatible` a collated tag is refused — so no reference-backed check can
say anything about one. Gate A computes its own expectation, which is exactly why it is the only
check that can be pointed at tags the reference would not use. It is also the slowest thing in the
suite by far, at 46 seconds, because it reads every record for every filter over every tag; that is
what it costs to have an expectation that owes the reference nothing.

## What sub-step 7 landed

`ResidualFilter`, the public `Query` with both skip strategies, `Table.Query`, `Table.CompileFilter`,
`Table.EvaluationContext`, `CodeBaseEngine.QueryTagScope` and `ErrorCode.Relate`. **Depth 4 green on
its first run** — all 81 filters, in record-number order and in tag order, with counts and backwards
walks. **3225 tests green** (764 golden, 2461 unit). No corpus file changed.

**The public class is `Query` and the namespace became `Queries`.** They collided; the caller-facing
name won, and `Expressions` beside it already sets the plural precedent. A deviation from `DESIGN.md`,
which named the namespace `CodeBase.Net.Query`.

**ADR-53's second divergence is proved.** The port's residual checks **strictly fewer** terms than
the reference's — it asks the plan rather than reading two fixed positions beside each term — and
returns the same records on every filter of the corpus.

**The mutation results have a shape worth reading, and three of five did not redden.** That is not a
gap: the trust rule fails in only one direction. Trusting a term no leaf answered **loses the
records the term would have excluded** (12 tests red), and a count that skips the residual keeps
candidates that are not answers (2 red). Trusting *nothing*, or refusing to split the outer
conjunction, only makes the port check terms the bitmap already answered — which can only agree with
it, so no record moves and nothing can see it. Gated where being wrong loses records, provably inert
where being wrong costs time.

**Which is exactly why the fully-mapped short-circuit needed an assertion of its own.** Writing one
found that the per-term rule did not see it: a filter rooted at an `.OR.` has its leaves recorded
against the comparisons inside, so asking whether that one term is answered says no even when every
part of it is. Whole-filter and per-term are the same question at two scales and the coarse one has
to be asked first. Now `ResidualFilter` short-circuits on `FullyMapped`, and the assertion that a
fully mapped plan leaves no work is the only thing that can see it — removing the short-circuit
reddens three tests and no record set.

**Two mutations were invalid before they were valid**, and both were caught by insisting a run
distinguish a build failure from a pass. One read a field while its own initializer was still
running and threw; the earlier form of another was unreachable code the compiler refused. A mutation
that does not execute proves nothing, and an empty failure list looks identical either way.

## What sub-step 6 landed

`ConstraintMerge` (the algebra), a fold pass in `QueryDecomposer`, `LeafConstraint.Range`, and a leaf
that answers **several** terms rather than one. Tests: `MergeGoldenTests` for the leaf counts and
`ConstraintMergeTests` for the algebra as sets. **3209 tests green** (748 golden, 2461 unit), up from
2066. `CDXEXPR.query.dump.txt` is the only corpus file that changed.

**The reorder paid off twice.** Depth 3 went **red the moment merging landed**, and both causes were
real. The first was a bounds bug: one helper conflated *which end wins* with *what a missing end
means*, so narrowing an open upper bound against a real one kept the open side and every conjunction
came back too wide. The second is the one worth carrying — `TagProbe.KeyFor` returned searches over a
**shared buffer**, so a second call silently clobbered the first. Nothing before merging ever held two
comparands at once; a range with two ends is the first thing that does. `TagProbeContract` gained the
promise that would have caught it, and it is the kind a stand-in passes by accident (it allocates)
while an implementation careful about allocation fails.

**Two terms over one tag now become one seek**, and the gate for that is structural, because no
correctness check can see it: a merge that is perfectly correct and never merges passes every
record-set assertion in the suite. `MergeGoldenTests` counts leaves.

**Where the reference shuffles, this groups.** It can only fold *adjacent* children, so it lifts
non-matching leaves into sibling branches until same-tag ones sit together; grouping by tag reaches
the same pairs with no shuffle, because a branch here is an array rather than a list to reorder.
Nothing crosses a connective, which the reference refuses too.

**The disjunction side was deliberately partial (Decision 9) and layer 4 did not reach it — so the
corpus was extended, and that turned out to be the most productive thing in the sub-step.** Seven
filters were added to `CDXEXPR` (43 to 54; only its query dump changed, everything else regenerates
byte-identical). Five of them join two terms over one tag by an `.OR.` whose union really is one
constraint, one is a bounded range with a value struck out of its middle, and one is a range whose
two exclusive ends bracket no key at all.

**What they said, in order of value:**

- **The reference does fold `.OR.` ranges, and its answers are exactly what the cautious fold
  produces** — on all five shapes. Decision 9 refused to build on `bmf4OrOt` because it was never
  transcribed; its *answers* are now witnessed even though its body is not, and `QUERY-OPTIMIZER.md`
  §12 item 7 narrows a second time.
- **`doCheck`'s case is in the corpus and the port needs no `doCheck`.** `K_NUM > 0.125 .AND.
  K_NUM < 0.135` names two adjacent stored values with nothing between; the reference returns
  nothing, and so does a walk that seeks the lower end and tests the upper, because the first key it
  looks at is already past the range. §6.1 says so now.
- **Extending found a gap, and closing the gap found a bug.** `K_NUM > 0 .OR. K_NUM = 0` did not
  fold: an exact value was only ever treated as sitting *inside* a range, never as extending its
  edge. Reading an exact value as a range whose ends have met removed three special cases instead of
  adding one — and immediately broke everything, because `IsUnbounded` tested only the two ends, and
  an exact value keeps its value in its own field and leaves both ends null. Two exact values took
  the path meant for two sets of struck-out values and came back as **everything**. Caught by the
  population test and by depth 3 together; re-introducing it reddens thirteen tests.

**A prediction in the plan was wrong, and the run said so.** Omitting the rule that moves an
inclusive bound past a value struck out at it was supposed to cost one record. It costs none: a range
walk that also drops excluded values drops this one whether or not the bound moved first. So it is a
cost and tidiness rule rather than a correctness one, gated by the structural assertion and by
nothing else. `QUERY-OPTIMIZER.md` §5.4 and `PLAN.md`'s mutation table both now say that.

**Five mutations, all red, and two exactly as designed.** Never merging reddened **only** the leaf
counts and left every correctness gate green — which is the argument for having them. Folding across
tags, folding a disjunction with the conjunction rules, and widening where a conjunction must narrow
all reddened depth 3.

## What sub-step 5 landed

`QueryExecutor` and `CandidateSetGoldenTests`. **Depth 3 is green on all 74 filters across three
tables** — the candidate set this port builds holds exactly the records the reference's own bitmap
holds, compared **before** any residual filtering and **before** any merging exists. **2066 tests
green** (732 golden, 1334 unit). No corpus file changed.

**The optimizer is now complete and correct**, seeking one tag twice where it will later seek once.
That is what the reorder after sub-step 2 was for: the merge algebra lands next against a gate that
is already green, so anything it moves is its own.

**The executor is one routine where the reference has four**, and the reason is worth carrying:
`bitmap4generate` hands every leaf a **freshly zeroed** bitmap (m4map.c:2328-2333), so the mode that
picks between the and-side and or-side routines chooses *how* a leaf's set is computed and never
*what it means*. A leaf's set does not depend on the connective above it. Now `QUERY-OPTIMIZER.md`
§6.5 — and it **narrows §12 item 7**: `bmf4OrOt` being untranscribed cannot affect evaluating a leaf,
only merging two `.OR.` leaves on one tag, which is Decision 9's territory anyway.

**A spec correction that would have cost the whole gate.** §6.4 quoted the reference's own header
comment approvingly: an `.AND.` map "marks as true all records which meet **at least one**
condition". The code beside it folds an `.AND.` branch with `f4flagAnd` — an **intersection**. Built
from the comment, the executor would union where it must intersect and return far too many records.
The comment's parenthesis is the true part: an `.AND.` branch is a superset because a conjunct that
could not be optimized was dropped, and the residual check is what removes what it would have
excluded.

**And one observation recorded rather than replicated.** A leaf whose constraint merging proved empty
short-circuits asymmetrically (m4map.c:2267-2283): under an `.AND.` it leaves the set empty, which is
right, and under an `.OR.` it makes **every record succeed**, which under §6.4's or-semantics would
turn the query into the whole table. Whether it is reachable is not settled — `noMatch` comes from a
merge, and whether an `.OR.` merge can prove emptiness is a `bmf4OrOt` question — and no corpus case
reaches it. A port following set logic cannot produce it by accident, so §6.6 exists to stop someone
producing it deliberately.

## What sub-step 4 landed

`ITagProbe` (the step's one seam), `TagProbe`, `RecordSet` and `ConstraintSeeker`, plus
`Table.OpenProbe`. Tests: `TagProbeContract` run against **both** implementations, `ConstraintSeekerTests`
at layer 2, `RecordSetTests` at layer 1, and `LeafEvaluationGoldenTests` — the **first end-to-end
signal**, where **41 single-leaf filters** (24 on `CDXEXPR`, 9 on `CDXBASE`, 8 on `CDXDEEP`) reach
exactly the records the reference's own bitmap holds. **2054 tests green** (720 golden, 1334 unit).
No corpus file changed.

**The probe opens its own cursor.** A query walks tags while a caller may be part-way through a seek
on the same one; sharing a cursor would have each move the other. The reference has the same hazard
and works around it by saving and restoring the tag's traversal flag around every leaf — a second
cursor costs one descent and removes the question.

**The seam is stored order, not tag order**, and that is the whole of what `TagProbe` decides. Keys
sort ascending whatever a descending tag's flag says, so stepping up in stored order means stepping
that cursor backwards. `CDXDEEP`'s `D_PFX` is descending and is the only tag over its field, so the
corpus exercises it: walking in tag order instead reddens both `CDXDEEP` golden tests.

**The range walk seeks one end, not two.** The reference reaches a range by seeking both ends — two
of its four seek positions by inverting the tag and seeking forwards on the reversed view — and
walking between the recorded positions. This port seeks the lower end and tests each key against the
upper bound as it walks up. Same records, one descent instead of two, and no inversion. An exclusive
bound is not a different seek either: the walk steps over the keys equal to it, which is the same
records and one fewer thing to get wrong. An empty range needs no rule at all — the first key tested
is already past the upper bound.

**An exclusion on its own builds the complement**, which is the one place the flag is created rather
than carried: `K_TEXT <> "X"` is nearly the whole table, so naming what it leaves out and saying so
beats walking every key. Combining two sets normalises the flag away first.

**Four mutations reddened; the fifth found dead code.** An exclusive bound admitting its own value,
an inclusive upper bound excluding it, walking a descending tag in tag order, and padding a short
character constant to the key width — all red. Scrubbing the spare bits after a complement **changed
nothing**, because every read of the set goes through its range check, so a bit outside one to the
record count is not a record however it came to be set. The scrubbing was removed and the invariant
now lives in one place, with a note for whoever later counts set bits a byte at a time and would need
to mask it themselves. The test that claimed to cover it was renamed to say what it actually catches:
an off-by-one in the complement.

## What sub-step 3 landed

`net/CodeBase.Net/Query/` gained `ConstraintValue`, `LeafConstraint` (with `Bound` and
`ConstraintKind`), `ConstraintNode`, `QueryPlan`, and `QueryDecomposer` grown from matching tags to
building constraints; `FunctionRows` gained the three band starts the operator mapping reads.
`DecompositionGoldenTests` and a `QueryHarness` extracted from sub-step 2's tests. **2006 tests
green** (714 golden, 1292 unit), up from 1968. **No corpus file changed.**

**Depth 2 green on all 74 filters, asserted unconditionally** — the reference's range-inverting
defect changes which slot a constant lands in, not which terms were optimized, so `fullyMapped`
agrees even on the one filter whose records are wrong.

**Four of five mutations reddened, and the fifth is the finding.** Reflecting `>` the wrong way
reddened eight tests; never widening a short constant, one; not clearing `fullyMapped` on an
unoptimizable conjunct, one; an `.OR.` keeping the operands it could optimize, three.

**The fifth — taking the evaluated width instead of the declared length — changed nothing, and that
is a fact about the language rather than a hole in the gate.** `DESIGN.md` Decision 4 keeps the
declared length because it is what decides whether a character constant is a prefix. Across all 74
filters the corpus reaches **93 constants and the two numbers never differ**, because only six kinds
of node are constants at all — a number, a string, the four logical words, and the two functions that
read a date out of text — and each is compiled at its own natural width, so none truncates. The
mutation is inert rather than uncaught. `AConstantIsAsWideAsItsNodeSaysItIs` now pins it, so the day
the two diverge the distinction stops being theoretical and something goes red.

**Two decisions are now code and both were free of surprises.** The operator-to-slot mapping reads
the row band and reflects when the constant came first (ADR-53's intent, not the reference's
assignment), and the short-constant widening applies to the two paths that produce a lower bound and
to no others — which the tests state as *meaning* rather than as which field was set, so a
well-formed constraint pointing the wrong way still fails.

**Nothing merges.** `K_NUM > 0 .AND. K_NUM < 100` is two leaves that will seek one tag twice and
intersect, which is the right answer reached the slow way, and it is what lets sub-step 5 gate the
record sets before any merge code exists.

## What sub-step 2 landed

`net/CodeBase.Net/Query/` — `QueryTagScope` (public), `QueryTag`, `TagEligibility`, `SubtreeMatch`
and `QueryDecomposer`; plus `Table.QueryTags()` and `TableTagCursor.KeyKind` to project a table's
tags into what the planner needs. Tests: `TagMatchGoldenTests` (depth 1) and, at layer 1,
`TagEligibilityTests` and `SubtreeMatchTests`. **1968 tests green** (676 golden, 1292 unit), up
from 1936.

**Depth 1 passed on the first run** — every node of all 74 filters across three tables, matching the
reference's own parallel array. **Five mutations were run and all five reddened it**: widening
eligibility under `Compatible`, visiting a connective's operands forwards, an `.OR.` that does not
abandon its branch, dropping the field-loader row exemption, and dropping the field-identity check.
Restores were from a checksummed copy, verified, never from `git`.

**The walk landed here rather than in sub-step 3, and it had to.** `e4isTag` is called from exactly
two places, both inside `bitmap4can`'s comparison branch (m4map.c:165, 172) — never from a sweep over
the node array. Only one operand of a comparison whose other operand is a constant is ever tested,
and only for comparisons the walk actually reaches. So the tag array is a property of the **walk**,
and depth 1 cannot be satisfied without it. `PLAN.md`'s sub-step 2 row says so now.

**Two corpus filters exist only to gate that**, and they are the sharpest pair in the dump:
`K_NUM > 0 .OR. K_LOG` and `K_LOG .OR. K_NUM > 0` select the same fourteen records and neither is
optimized — but one records `T_NUM` and the other records nothing, because operands are visited
**backwards** and an `.OR.` dies at the first one it cannot use. Nothing but the visit order gets
both right, and a port that filled the array by testing every node against every tag would fill both.

**A spec correction, and it would have cost three kinds of tag.** `QUERY-OPTIMIZER.md` §3.4 said the
type gate allows `r4double`, `r4charBin` and `r4memoBin` — which is what the *comment* at r4log.c:71
says. The **code** is a `switch` that refuses four types and falls through on everything else, so
**date, logical and binary-float tags are eligible**. Four filters were added to `CDXEXPR` to witness
it: `K_DATE = STOD("20200101")` and `EMPTY(K_TEXT) = .T.` both match a tag and are fully mapped.
`STOD` is the clock-free way to write a date constant — `CTOD` infers the century from the system
date, which would make the dump churn every year. Same pattern as ADR-33 and §10.4: the code is
authoritative, the comment is noise.

## What sub-step 1 landed

`test-files-generator/src/dump-query.h/.cpp` and query filter lists on three cases — **43 on
`CDXEXPR`, 15 on `CDXBASE`, 12 on `CDXDEEP`** — plus, on the C# side,
`net/tests/CodeBase.Net.Golden/`'s `CorpusQueryDump`, `DumpQueryCase` and `QueryOracleGoldenTests`.
**No C# of the optimizer.** `dotnet test` is green on **1936 tests** (666 golden, up from 639; 1270
unit unchanged), and **every pre-existing corpus file regenerated byte-identical** — 48 of 48, with
the three query dumps as pure addition. Two independent runs produce identical bytes.

The gate is 12 assertions over the oracle, and it asserts nothing about the port because no port
code exists: that the dumps parse into the number of cases they declare, that the reader's
recomputed verdict matches the generator's own (which is the round-trip), and that the reference's
decisions are internally consistent.

## What the run answered

**The defect is witnessed, and it is worse than "a bound off by one".**
`K_INT > 0 .AND. 4 > K_NUM` returns **{3, 9, 11, 12, 14, 15}** optimized and **{5, 6, 8}** under
`bitmapDisable` — **disjoint**. The bound was *inverted*, not loosened, and the dump shows the
mechanism: the leaf holds the same raw bits as the correct `K_NUM < 4` leaf with `isFlip` set, so
the complement was taken. The residual filter then trusted the term, because its operand had matched
a tag, and nothing corrected it. Written tag-first, or un-nested, the reference is correct. It is
**the only one of the corpus's 70 filters** where the reference contradicts itself, which is exactly
what §4.3 predicted. ADR-53's choice is now evidence rather than inference.

**Three more predictions confirmed, each of which had never been checked:**

- **An `H`-field comparison never optimizes** — `K_FLT > 0` builds no bitset and matches no tag,
  confirming §3.2's reading that the refusal comes from the constant side. §12 item 3 closed.
- **A skipped tag does not end the search.** `CDXBASE` has a plain and a unique tag over one field,
  and `K_DUP = "CHERRY"` matches the plain one — so the walk continues past a refusal. A table with
  one tag per field could not have distinguished that.
- **The short-constant widening changes no records.** `UPPER(K_TEXT) > "S"`, `>= "S"` and
  `"S" < UPPER(K_TEXT)` return the same four records, which is Decision 6's "exact, not a safety
  margin" made a test — and it is what ADR-53's trust rule rests on.

**Two things the design had wrong, both now corrected in the spec:**

- **`bitmapsFreed` is not "there is no bitmap".** It means bitmapping *failed*. Ten corpus filters
  optimize nothing at all and **not one sets it**; across all 70 it is never set. A port treating it
  as the fall-back signal would be reading a condition that never occurs. Correctness on that path
  comes from the per-term tag test alone. `QUERY-OPTIMIZER.md` §7.1.
- **The go-around complement fires constantly**, so depth 3 must compare the *effective* set and not
  the raw bits. `K_NUM > 0` on `CDXEXPR` — 12 records of 16 — is already stored as its 4-bit
  complement; `CDXDEEP`'s `K_N > -400000` stores 599 records as **one** raw bit. `DESIGN.md`
  Decision 8 said depth 3 compares sets and not the flag, so the design was right and `PLAN.md`'s
  "bit for bit" was the loose wording; it now says effective. §6.2.

**Open question 1 is answered: the bitset survives the walk.** The dump takes it twice and compares,
and `setStable` is yes for all 70.

## Notes for the next session

**A field qualified by the table's own alias is refused, in filters and in tag expressions alike.**
`CDXEXPR->K_NUM > 0` comes back `e4fieldName` (−210), and so would the same text as a tag expression.
The cause is one line in two places: `ExpressionParser.Parse` takes the set of aliases a field may be
qualified by and **neither `Table.CompileFilter` nor `SeekConverter.For` passes it**, so the scanner
never learns the table's own name and the qualified form never resolves. It is a step 010 gap rather
than a step 011 one — the two paths are consistent with each other — and it fails safe, refusing
rather than answering wrongly. Worth fixing where `EXPR` is next touched; the alias machinery is also
what multi-table relations would need, and it is inert until then.


**Two decisions were taken during design and are already ADRs**, so they do not need re-deriving:
tag eligibility replicates CodeBase by default and widens under an explicit `QueryTagScope`
(**ADR-52**), and the reference's `tagPos = 1` defect is not replicated — the intent is
(**ADR-53**). Both close [ADR-46](../../ARCHITECTURE-DECISIONS.md), which was open on exactly them.

**Two things were settled by reading the C during design, and are worth not re-discovering:**

- **`QUERY-OPTIMIZER.md` §12 item 2 is resolved.** The partial-string widening looks asymmetric
  because only two of six branches apply it, but those two are exactly the two that write the `gt`
  slot, and they are the only two that write it (m4map.c:211-283). Not a defect. `DESIGN.md`
  Decision 6.
- **The reference's residual trust test reads the wrong positions.** `log4true` tests
  `(infoReportPtr-1)->tag` and `-2`, which are the operand roots only when the right operand is a
  single node (r4log.c:770, read directly). Where they are not, the reference re-evaluates a term its
  own bitmap answered — conservative, never wrong. The port trusts per *term* instead and so trusts
  more. `DESIGN.md` Decision 5; depth 4 of the gate is what proves it safe.

**One thing the corpus still cannot isolate: the filtered-tag refusal.** Every filtered tag in the
corpus shares its field with a plain tag that is found first — `CDXBASE`'s `T_FILT` sits over `K_N`
beside `T_NUM`, `CDXEXPR`'s over `UPPER(K_TEXT)` beside `T_UPPER` — so no dump distinguishes "the
filtered tag was refused" from "a plain tag was found first". Isolating it needs a filtered tag over
an expression nothing else indexes, which means a **new tag** and therefore a rewritten `.cdx` and
every index dump with it. Not worth that: the refusal is one branch of a pure predicate and sub-step
2 tests it exhaustively at layer 1. Named so it is a decision and not an oversight.

**Two things step 010 owed the corpus are still owed.** The second rejection pass with a real
`TAG4FILE` (so `e4tagExpr`, −510, and the alias refusals stop being unobservable) was not folded into
this run — it is a rejection-dump change, not a query one, and mixing them would have made the
byte-identical check ambiguous. The three-or-more `.AND.`/`.OR.` run **is** now covered, by two query
filters. Both were listed together in `PLAN.md`; only the second is discharged.

**`DESIGN.md` open questions 2 and 3 are still open.** Question 2 (what the reference reports for a
query with no optimizable term) is effectively answered by the `bitmapsFreed` finding above — the
dump distinguishes `set absent` from an all-set bitset — so it can be closed when sub-step 3 reads
it. Question 3 is the `e4tagExpr` one above.

## Deviations from the plan

- **`PLAN.md`'s Gate B depth 3 said "bit for bit"; it is the *effective* set.** The reference stores
  a set as its complement whenever it covers more than half a tag, and that fires on most corpus
  filters — so raw-bit equality would fail a correct port that omits the go-around (Decision 8).
  `DESIGN.md` Decision 8 already said depth 3 compares sets and not the flag, so this is `PLAN.md`
  catching up with `DESIGN.md`, not a change of intent. Both now say effective.
- **The public class is `Query` and its namespace is `CodeBase.Net.Queries`, not `.Query`.** A class
  cannot share a name with its own namespace. The caller-facing name won because it is the one a
  reader sees, and `Expressions` beside it already sets the plural precedent; the folder was renamed
  to match, since every other folder here does.
- **The merge algebra moved from sub-step 4 to sub-step 6, so depth 3 arrives before it.** The
  first plan gated sub-step 3 on `FullyMapped` alone — one bit per filter, blind to a swapped bound —
  and left the operand-order reflection unchecked until sub-step 6. Merging turns out to be an
  optimization rather than a correctness requirement (unmerged, `K_NUM > 0 .AND. K_NUM < 100` is two
  leaves that seek separately and intersect, which is the right answer), so decomposition, seeking
  and combination form a complete optimizer and **depth 3 can be green on all 74 filters before any
  merge code exists**. The merge then lands against a green end-to-end gate and gets a sharper gate
  of its own: the record sets must not move and the leaf count must fall. `PLAN.md`'s steps table,
  its mutation table and the note under it all say so.
- **The decomposition walk landed in sub-step 2, not sub-step 3.** Depth 1 is sub-step 2's gate and
  depth 1 is a property of the walk, so `QueryDecomposer` exists now and returns the tag array;
  sub-step 3 grows the same walk to build constraints rather than writing a second one.
- **The oracle records five depths, not four.** Depth 0, the reference's own scan truth under
  `bitmapDisable`, was added to `DESIGN.md` as Decision 12 during the design pass and is what
  sub-step 1 actually built.

## Blockers

None. Sub-step 8 needs no generator run either.
