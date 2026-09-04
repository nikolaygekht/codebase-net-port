# 011-query-optimizer — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after
[`DESIGN.md`](DESIGN.md), before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| **1 Unit** | **the plan's shape** — `x > 5 .AND. x < 10` is *one* leaf, not two under an `And`, which is the merge algebra's whole stated purpose; `ConstraintValue` comparison (the `const4less` prefix rule and its `<=`/`<` asymmetry); `ConstraintMerge` as an algebra, checked by **accepted-value population** rather than by case; `LeafConstraint`'s canonical invariants; `TagEligibility`'s four gates in both scopes; `SubtreeMatch` over hand-built node arrays; `RecordSet`'s bit operations and its complement flag; the operator-to-slot table in both operand orders | Every branch of the merge algebra and every reflected operator, exhaustively and in microseconds. Layer 4 will exercise perhaps a fifth of these paths, because a corpus filter list is a sample and the algebra is a cross-product |
| **2 Component** | **work counted over a `CountingSource`** — a fully-mapped `Count` reads zero records, a fully-mapped query reads exactly what it returns, a selective query's block reads are a fraction of a scan's; `ConstraintSeeker` over a hand-written `ITagProbe`: the four seek positions, `doCheck` on a degenerate one-record range, the range walk, the half-open range, the empty range (upper below lower), and an exclusion list. `QueryDecomposer` over hand-built `QueryTag` lists. `ResidualFilter` over an in-memory record. `Query`'s two skip strategies over a fake plan | Sequencing and state-machine bugs with no disk: a walk that steps one too far, a seek that lands on the boundary, a skip that re-reads the record it is on. `doCheck` in particular has **no corpus case that isolates it** — it fires only when both ends of a range are exclusive and land on the same record |
| **3 Fault injection** | `ITagProbe` via Moq: a seek that reports no such position mid-range, a step that fails at a block boundary, a tag whose key length disagrees with the constraint's. Hand-built corrupt input: a filter that compiles to a comparison against itself, a plan whose tag ordinal is out of range | What a valid corpus file physically cannot express. Every tag in `net/corpus/` is well-formed, so "the tag ran out under us" is unreachable there |
| **4 Golden / corpus** | The **query dump** — four depths per filter case, against `CDXEXPR`, `CDXBASE` and `CDXDEEP` — plus the equivalence gate over every case | Whether we decompose the way CodeBase does, and whether the record set is right at all |

**The contract-test pair.** `ITagProbe` is a hand-written fake standing in for `TagProbe`
(`DEV_APPROACH.md` §5), so its promises are written once in an abstract `TagProbeContract` and run
against **both** — over a corpus tag for the real one and over the same keys as a sorted list for the
fake. Without it, every layer-2 test above is confident about a probe that does not behave like a
tag.

### Corpus coverage

Gated on three existing tables, each chosen for what it alone can say:

| Table | What it brings to this step |
|---|---|
| `CDXEXPR` | the expression tags a real filter names — `UPPER`, `SUBSTR`, `+`, `DTOS`, `STR` — plus the corpus's **only `H` field**, the type-refused tags (`T_INT`, `T_CUR`, `T_TS`), a **filtered** tag (`T_FILT`), a nullable tag and a deleted record |
| `CDXBASE` | one block per tag over the key shapes: prefixes, blanks, duplicates, **a unique tag** (the eligibility refusal), descending, numeric, `-0.0`, date, integer, filtered |
| `CDXDEEP` | 600 records under two levels of branch, so a **range walk crosses block boundaries** in both directions, a result set is large enough for the bitset's byte-scanning path to run, and equal keys straddle a boundary |

**Paths no corpus case covers today, and the generator cases they want.** These are the additions
sub-step 1 makes; the first three are `QUERY-OPTIMIZER.md` §12's own list, and the fourth is owed
from step 010:

1. **A reflected comparison nested inside an `.AND.`** — `K_INT > 0 .AND. 4 > K_NUM` beside
   `K_INT > 0 .AND. K_NUM < 4`. The only place the reference's `tagPos = 1` defect and its intent can
   disagree (Decision 7), and the case that turns a swapped bound from an argument into a test.
   Because sub-step 1 runs every query twice, this case also **witnesses** the defect — closing
   `QUERY-OPTIMIZER.md` §12 item 1, which records the intent as inferred from four source comments
   and never observed.
2. **A comparison against an `H` field** — `K_FLT > 0`. §3.2 predicts it is **never optimizable**,
   because `E4FLOAT` (row 29) is absent from `e4isConstant`, even though `e4isTag` does not refuse an
   `r4floatBin` tag. `CDXEXPR` has the only `H` field, so the prediction is one line to check and
   currently rests on reading alone.
3. **An OR range** — `K_NUM < 4 .OR. K_NUM > 9`, and an overlapping pair that *can* combine. Decision
   9 leaves the first as two leaves; the dump says what the reference's set was, which is what the
   comparison is against.
4. **A run of three or more `.AND.`/`.OR.` operators**, over constants and over fields. The whole
   `CDXEXPR` dump holds only two-term runs, so the variadic collapse §4.1 depends on is gated by
   C#-authored tests alone — found by a mutation at step 010's sub-step 3 that layer 4 did not catch.

Beyond those four, the filter list must reach every row of §8's optimizable/not table: each of the
six operators in both operand orders, a short character constant (the prefix range), an exclusion
list of two, `.NOT.`, a bare logical field, `DELETED()`, `EMPTY()`, both sides constant, both sides
fields, a term over each refused tag type, a term over the filtered tag, a term over the unique tag,
a partially-optimizable `.AND.`, a partially-optimizable `.OR.`, an empty result and a full-table
result.

**Also folded into the same generator run**, because it is one Windows trip and step 010 owes it:
the **second rejection pass with a real `TAG4FILE`**, so `e4tagExpr` (−510) and the alias refusals
stop being unobservable.

### Expected values

- **Depths 1–4** come from the new `*.query.dump.txt`, written by the C library from its own
  `RELATION4`. Nothing here is typed from the spec.
- **The equivalence gate's expectation is computed, not stored**: a full scan of the same compiled
  expression over the same table. It is a round-trip invariant in `DEV_APPROACH.md` §4's sense — two
  routes to one answer — and it is the one expectation that needs no oracle at all.
- **Layer 1's merge tests** assert an accepted-value **population**, not a shape:
  `And(a, b)` accepts exactly the values `a` and `b` both accept, over an enumerated domain. That is
  a promise about what a constraint means, so it survives any rewrite of how the merge is done.
- **Layer 2's probe tests** take their keys from a corpus tag, read through the real reader, so the
  fake's data is the file's data even when the fake is not the file.

## Steps

Nine. Sub-steps 1–4 build the pure half and cannot produce a record set; 5–8 build the half that can,
each closing under a deeper part of the same gate.

| # | Step | Verify by |
|---|---|---|
| 1 ✅ | **The query oracle.** *(done 2026-09-04 — 70 filters, 48 of 48 corpus files byte-identical, 1936 tests green)* `dump-query.h/.cpp` in the generator, plus filter lists on `CDXEXPR`, `CDXBASE` and `CDXDEEP`. Per filter, **each query run twice** — optimized, and again with `RELATION4.bitmapDisable` set — giving the four depths plus **depth 0, the reference's own scan truth** (`DESIGN.md` Decision 12). Also answers `DESIGN.md`'s open questions 1–3. **No C# of the optimizer.** | The three `*.query.dump.txt` files exist and parse into typed cases; **every other corpus file regenerates byte-identical**; the C# reader round-trips the dump; and the dump **names every filter where the two readings disagree**, which is the reference witnessing its own §10.1 defect rather than us inferring it |
| 2 | **Eligibility and subexpression identity.** `QueryTag`, `QueryTagScope`, `TagEligibility`, `SubtreeMatch` | **Depth 1**: for every filter, every node and every tag of the table, our answer to "does this tag's key expression equal this subtree?" equals the reference's `infoReport[i].tag`, under `Compatible`. Plus layer 1 over hand-built node arrays for the promotion exemption and the field-identity rule |
| 3 | **Decomposition.** `ConstraintValue`, `LeafConstraint`, `ConstraintNode`, `QueryPlan`, `QueryDecomposer` — no merging yet | **Depth 2**: `FullyMapped` equals the reference's `fullyMapped` on every case. Plus layer 1 on the operator-to-slot table in both operand orders, and on the partial-string widening (Decision 6) |
| 4 | **The merge algebra.** `ConstraintMerge`, and the reduce/redistribute placement that decides which leaves meet | Layer 1, two assertions that fail differently. **Correct:** by accepted-value population — `And`/`Or` of two constraints accept exactly the intersection/union of what the inputs accept, over an enumerated domain per type; the §5.3 invariants hold for every result; `Empty` is produced for every provably-empty pair. **And useful:** the plan for `x > 5 .AND. x < 10` has **one leaf**, and for a two-tag filter exactly two — because the population test passes on a merge that is correct and never merges, and leaf count is seek count (`DESIGN.md` Decision 13, tier 1) |
| 5 | **Leaf evaluation.** `ITagProbe`, `TagProbe`, `RecordSet`, `ConstraintSeeker` | The `TagProbeContract` pair passes for both implementations; layer 2 covers the four positions, `doCheck`, the empty and half-open ranges, and the exclusion list. First end-to-end signal: a single-leaf plan's set matches **depth 3** |
| 6 | **Combination.** `QueryExecutor` — the `And`/`Or` fold, the complement flag, the `noMatch` short-circuit | **Depth 3 in full**: our candidate bitset selects exactly the records `relate->set` selects, for every case, under `Compatible`. This is the assertion that covers decomposition, merging, seeking and combining as one, and it runs before any residual filtering exists to hide a mistake |
| 7 | **The residual filter and the public surface.** `ResidualFilter`, `QueryPlanner`, `Query`, `Table.Query`, `CodeBaseEngine.QueryTagScope`, both skip strategies, `Count` | **Depth 4**: the record sequence and the count equal the reference's, in tag order with a tag selected and in record-number order without one. This is also where Decision 5's divergence is proved safe — our residual runs on fewer terms and must still agree |
| 8 | **The equivalence gate, the widened scope, and the efficiency gate.** `AllSeekable`; the full-scan comparison as a permanent, always-on assertion over every case; and `CountingSource` wired into the test engine to count the work | **Gate A** green in **both** scopes on all three tables. Under `AllSeekable` the integer, currency, datetime and `GENERAL` tags become eligible and have no reference answer — equivalence is their only check, which is the arrangement Decision 1 argues for, and **Gate C is the only thing that can see them being optimized at all rather than merely answered correctly**. **Gate C** green: the three exact counts and the block-read ratio |
| 9 | **Close.** The selectivity sweep over `PERF10K` (0.01% to 100% matched, block reads and wall time on both arms) recorded in `SUMMARY.md` — measured, **not** gated, and `PERF10K` stays benchmark data; mutation checks; then `PORTING-PLAN.md` §5 (`QUERY` done, **R12** addressed), ADR-52 and ADR-53, `QUERY-OPTIMIZER.md` §12's resolved items, `net/corpus/README.md`, `test-files-generator/README.md`, `SUMMARY.md`, `dev/README.md`, root `STATE.md` | Every planned mutation reddens **exactly** the tests named for it, and the restore is verified by checksum from a copy — never by `git` (`DEV_APPROACH.md` §4) |

### The mutations planned, and what each must redden

A gate that has never failed is a gate nobody has checked. Named up front so the check cannot become
a formality:

| Mutation | Must redden |
|---|---|
| Reflect the wrong way for a constant-left comparison (Decision 7) | **depth 0** and Gate A on the `.AND.`-nested `4 > K_NUM` case, **and only there** — the un-nested cases agree under both readings, which is the whole reason that case exists. Note it must *not* redden depth 3 there, since the mutation reproduces what the reference's optimized run did |
| Drop the partial-string widening (`ge` back to `gt`) | depth 3 on the short-constant `>` case; equivalence on the same |
| Drop an `.OR.` disjunct instead of destroying the branch | equivalence — records lost, which is the failure class `CLAUDE.md` ranks worst |
| Trust a term that produced no leaf | equivalence on every partially-optimizable filter |
| Stop tightening `le` to `lt` when an exclusion equals the bound (§5.4) | layer 1's population test, and equivalence by exactly one record |
| Widen eligibility under `Compatible` | depth 1 by tag name, immediately |
| Count from 0 rather than 1 in `RecordSet` | depth 3, everywhere at once — which is the sign the gate is **not** localised enough, so this one is a control |
| **Make the optimizer inert** — decompose nothing and fall back to scanning always | Gate B depths 1 and 2 under `Compatible`, **and Gate C under both scopes** — while Gate A stays green throughout, which is the entire argument for Gate C existing |
| Stop merging two same-tag leaves, so each seeks separately | **tier 1's leaf-count assertion and Gate C's read ratio only** — every correctness gate stays green, because two seeks return the same records as one |

## Gate

**Three mechanical checks, all run by `dotnet test`, none skippable.** A and B are correctness;
C exists because neither of them can see an optimizer that never optimizes.

**Gate A — full-scan equivalence, every case, always.** For every (table, filter) case, the record
sequence from `Query` equals, in the same order, the sequence from a brute-force scan evaluating the
same `CompiledExpression` per record. Run without a selected tag (record-number order) and with each
eligible tag selected (tag order), in **both** `QueryTagScope` settings. No reference bytes are
involved: it is the check that stays valid on tags CodeBase would refuse.

**Gate B — the reference's own answers**, under `QueryTagScope.Compatible`:

| Depth | Assertion | Covers |
|---|---|---|
| **0** | the record sequence equals the reference's **`bitmapDisable` run** — its own scan truth | what the filter *means* to CodeBase, with the optimizer out of the circuit |
| 1 | per node, the matched tag equals `infoReport[i].tag` | `TagEligibility`, `SubtreeMatch` |
| 2 | `QueryPlan.FullyMapped` equals `relation->fullyMapped` | the fall-back boundary |
| 3 | the candidate set equals `relate->set` — the **effective** set, its bits read through `isFlip` | decomposition, merging, seeking, combination |
| 4 | the record sequence and `Count` equal `relate4top`/`skip`/`count` | the residual filter, the two skip orders |

Depth 3 is the one to watch: it compares the set **before** residual filtering, so a decomposition
error cannot be masked by a residual pass that happens to remove the same records. It compares the
*effective* set rather than the stored bits, because the reference flags the complement whenever a
range covers more than half a tag and the port does not implement that (Decision 8) — 599 of
`CDXDEEP`'s 600 records come back as **one** raw bit. `isFlip` is a cost decision, not an answer.

**Depths 3 and 4 are asserted against the reference's optimized run only where its two runs agree.**
Where they disagree the reference has demonstrated its own §10.1 defect on that filter, and depth 0
is the expectation instead — which is also, independently, what Gate A requires. The set of
disagreeing filters is **read from the dump, never listed in the C#**: a carve-out the test knows by
name is a carve-out that silently widens, and one the oracle reports cannot.

**Gate C — the optimizer actually optimizes**, counted rather than timed (`DESIGN.md` Decision 13).
Over `CDXDEEP`, with `CountingSource` behind the engine:

| Assertion | Value |
|---|---|
| records read by `Count`, plan fully mapped | **exactly 0** |
| residual evaluations, plan fully mapped | **exactly 0** |
| records read, plan fully mapped | **exactly the records returned** |
| plan leaf count for `x > 5 .AND. x < 10` | **exactly 1** |
| block reads, a selective query versus a full scan | a ratio with headroom, set the way ADR-40 set its 2× |

Three of the five are exact numbers rather than thresholds, so they cannot drift. **Gate A stays
green under every failure Gate C catches** — an optimizer that decomposes nothing, or that never
merges two leaves on one tag, returns exactly the right records. That is the whole reason this gate
is separate rather than folded into the other two.

## Risks

| Risk | Why it could make this step wrong rather than late | Cheapest thing that exposes it early |
|---|---|---|
| **R12 — a wrong record set** | The named risk of the whole project, and every failure mode here lands in it: a bound off by one, a reflected operator, a dropped disjunct, a trusted term that should not have been | Gate A on every case from sub-step 5 onward, and depth 3 bit-for-bit at sub-step 6 — before the residual filter exists to hide anything |
| **`bmf4OrOt` is believed, not transcribed** (§12 item 7) | An OR range is the one area where the spec records a belief rather than a reading | Decision 9 refuses to build on it; the OR-range corpus case at sub-step 1 says what the reference actually produced, so the belief becomes a comparison |
| **The oracle needs a Windows/MSVC run** | Sub-step 1 produces nothing testable without it, and sub-steps 2–8 are each gated by part of it | Sub-step 1 lands first and separately, exactly as step 010's did; the `.dump.txt` files are committed before any C# of the optimizer is written |
| **Depth 3 assumes `relate->set` is readable and stable** | If it is mutated by the scan, depth 3 compares against the wrong thing and looks green | `DESIGN.md` open question 1: the dump takes it twice and compares. Answered inside sub-step 1, before anything depends on it |
| **The reference's optimized answer is not always its true one** | §10.1's defect can invert a bound, so on the nested reflected case a *correct* port must disagree with `relate->set`. A gate that demands agreement everywhere would be red for the right reason and get patched with a hand-written exception | Decision 12: every query is dumped twice, so the disagreement is data rather than a carve-out, and depth 0 is the expectation exactly where the two readings differ |
| **Decision 5 trusts more terms than the reference** | If the reasoning about widening being exact is wrong anywhere, we skip a residual check the reference performs, and lose no records but gain some | Depth 4 compares the final sequence, where a gained record shows immediately; and the tie-break it rests on is already witnessed in `EXPRESSIONS.md` §8 |
| **The optimizer is correct and inert** | Every correctness gate passes on a `QUERY` that decomposes nothing and scans every time, because a null bitmap answers yes for every record (r4relate.c:82-91). It is ADR-40's failure repeating in a second module | Gate C, and the two mutations named for it. Under `Compatible` Gate B depths 1-2 also catch it, but under `AllSeekable` there is no reference and Gate C is the only guard |
| **The optimizer is slower than a scan on a wide query** | Index descent plus a tag walk plus nearly every record anyway. The reference answers this with the go-around complement, which Decision 8 omits | The sub-step 9 selectivity sweep over `PERF10K` locates the crossover and is what would reverse Decision 8 — with a measurement rather than a preference |
| **A leaf's constraint value is built from a runtime width** | Every prefix range silently becomes an equality, and the query returns almost nothing | Decision 4; the short-constant cases in the filter list, and step 010's `OptimizerContractGoldenTests` already pin the declared length as a property of the node |
