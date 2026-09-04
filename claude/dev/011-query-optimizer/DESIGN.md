# 011-query-optimizer — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

**A filter expression over one table becomes a record set produced by seeking tags instead of
scanning it.** `UPPER(NAME) = "SM" .AND. SALARY > 50000` decomposes into per-tag range constraints,
each constraint is answered by a CDX seek, the resulting record-number sets are combined, and the
terms no tag can answer are re-evaluated on the candidates that survive. This is what the whole port
exists for (`CLAUDE.md`, `PORTING-PLAN.md` §1).

**How we will know it works: three gates that fail differently.** Two are correctness and run on
every case; the third exists because **neither of them can see an optimizer that never optimizes** —
a query that decomposes nothing and scans every time is perfectly correct (Decision 13).

- **Equivalence.** For every (table, filter) case the optimized record sequence equals, in the same
  order, the sequence a brute-force scan of the same compiled expression produces. It is the check
  that does not need a reference at all, and it is never skipped or flagged off — `CLAUDE.md` ranks a
  wrong record set above a slow one, and the reference agrees, enforcing the same check itself under
  `S4TEST && S4DEBUG` (`QUERY-OPTIMIZER.md` §7.3).
- **The reference's own answers, at four depths — plus its own scan truth.** A new generator dump
  records, per filter, which tag the C library matched to each node, whether it considered the query
  fully mapped, the **candidate bitset it built before any residual filtering**, and the record
  sequence and count it finally returned. Equivalence proves we are self-consistent; this proves we
  decompose the way CodeBase does. And because `RELATION4.bitmapDisable` takes the optimizer out of
  the circuit entirely (m4map.c:341), **the same run records what the filter means to CodeBase's own
  evaluator with no bitmap at all** — which is the only thing that can tell an optimizer defect from
  a port defect. Decision 12.

- **Work counted, not time measured.** The plan's leaf count is the seek count and is asserted
  structurally; a fully-mapped `Count` must read **zero** records; a fully-mapped query must read
  exactly the records it returns; and a selective query's block reads must be a small fraction of a
  full scan's. All four are deterministic and identical on any machine. Decision 13.

**Visual FoxPro is not an oracle for any of this, and it is worth saying why.** ADR-44 gave the port
a second offline oracle for what CodeBase cannot produce, on the rule that VFP witnesses *a value it
computes and the bytes it writes*. The optimizer produces neither: it writes nothing, and three of
the four depths are CodeBase's own internal state, which VFP has no counterpart to. The fourth — a
record sequence — VFP could answer, and the answer would be **wrong for us**: VFP's prefix `=` runs
the opposite way from CodeBase's, which the VFP oracle itself established in step 010 (ADR-45,
ADR-50), and this port replicates CodeBase. `NAME = "SM"` denotes different records in the two
engines, so a VFP result set would redden a correct implementation. The expression semantics
underneath were already gated against both engines by `EXPR`; nothing above them needs VFP.

**Capability:** `QUERY` (`PORTING-PLAN.md` §5) · **Governing spec:**
[`specs/QUERY-OPTIMIZER.md`](../../specs/QUERY-OPTIMIZER.md), with
[`EXPRESSIONS.md`](../../specs/EXPRESSIONS.md) for the compiled form this walks and
[`KEY-COLLATION.md`](../../specs/KEY-COLLATION.md) for turning a constraint value into key bytes.

## Not in this step

- **Multi-table joins.** `relate4createSlave`, slave scans, `relate4exact`/`approx`/`scan` relation
  types. Out of v1 by `PORTING-PLAN.md` §2.3. `log4determineEvaluationOrder` (r4log.c:534-611) goes
  with them — it reorders an `.AND.`'s conjuncts by how many data files each touches, and with one
  table it is inert.
- **Sorting a result set.** `RELATION4`'s `sort*` half — `SORT4`, `sortedFile`, `sortKeyLen`. A query
  comes out in tag order or in record-number order (§7.2) and in no third order.
- **Writing anything.** A query optimizer never writes; that is why `QUERY` is P1 and `WRITE` is P2.
- **The go-around complement optimisation** (`QUERY-OPTIMIZER.md` §6.2) — Decision 7.
- **`CODE4.bitmapMemory`, the `MEM4` pool and the `S4LOW_MEMORY` retry** (§9). They are the C's
  allocator, not behaviour; .NET's is the port's.

## What is already in place, and what it costs us

Step 010 built this step's input deliberately, and `OptimizerContractGoldenTests` already gates it —
so four things `QUERY-OPTIMIZER.md` §3 demands need no work here:

| §3 demands | Where it is | Gated by |
|---|---|---|
| a retained postfix node table with all six fields | `CompiledExpression.Nodes`, `ExpressionNode` | `EveryNodeCarriesAllSixFieldsTheOptimizerReads`, `EachNodesSubtreeSizeSpansExactlyItsOperands` |
| operator classification by row band | the table transcribed in row order, `FunctionRows` | `AComparisonIsFoundByItsRowBandAndEachOperatorByItsSubRange` |
| evaluate at a node, with no record when the subtree is constant | `ExpressionEvaluator.Evaluate(int, EvaluationContext)`, `IsConstant(int)` | `AConstantOperandOfEveryComparisonEvaluatesWithNoRecord`, `NothingThatReadsAFieldIsEverCalledConstant` |
| a `Tag` that keeps its `CompiledExpression` | `Tag.Compiled`, `Table.CompiledFor` | `ATagsCompiledExpressionIsTheSameOneTheKeysAreBuiltFrom` |

What is **not** in place, and is this step's: structural equality between two node arrays, tag
eligibility, the `BITMAP4` tree, the constraint-merge algebra, the four seek positions, the `F4FLAG`
bitset, the residual filter and the public surface.

Step 008 gives the seek surface §11.4 says to reuse: `SeekAtOrAfter` is location 1 and
`SeekAtOrBefore` is location 2, each answered directly rather than by inverting the tag as
`bitmap4seek` does (C4CONST.C:584-589). Locations 0 and 3 are one step off one of those.

## Architecture — five stages, and where the boundary sits

The pipeline is `QUERY-OPTIMIZER.md` §1's, with one deliberate difference in shape: **everything up
to and including the plan is pure**, and the only stage that touches a file is leaf evaluation.

```
filter text ─┬─► CompiledExpression        (step 010, unchanged)
             │
             ├─► QueryPlanner ────────────► QueryPlan          pure: no file, no record
             │     TagEligibility                              (tree of constraints,
             │     SubtreeMatch                                 trusted terms, fullyMapped)
             │     QueryDecomposer
             │     ConstraintMerge
             │
             ├─► QueryExecutor ───────────► RecordSet          the only stage that seeks
             │     ConstraintSeeker  ── ITagProbe ──► TagCursor
             │
             └─► Query ───────────────────► records            residual filter + the two skips
                   ResidualFilter
```

That the plan side is pure is not tidiness: it means decomposition, eligibility, node matching and
the whole merge algebra are testable at layer 1 with no CDX file anywhere near them, and it means
**this step cannot return a wrong record set from the half of it that has no record set**.

## Classes

Namespace `CodeBase.Net.Query`, except `Query` itself and the engine setting.

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `QueryTag` | Entity | What the decomposer needs to know about one tag | ordinal, name, key length, key type, collation, filtered, unique, and its `CompiledExpression`. A projection built once from `Table.Tags`, so the decomposer needs no open index — the counterpart of step 010's `IFieldMetadata`, as data rather than an interface because there is no behaviour to fake |
| `QueryTagScope` | Entity | Which tags the optimizer may use | `Compatible` (default) or `AllSeekable` — Decision 1 |
| `TagEligibility` | Entity | Whether a tag passes the four gates of §3.4, under a scope | pure predicate over a `QueryTag`; the whole of CodeBase's collation, type, filter and unique refusals in one place, and the one place the widening option is read |
| `SubtreeMatch` | Entity | Whether the subtree at node *i* of one compiled expression equals the whole of another | `e4isTag`'s inner comparison (r4log.c:147-236), backwards in lockstep. Where the `memcmp` over a `FIELD4` prefix becomes explicit structural equality — Decision 3 |
| `ConstraintValue` | Entity | One constraint constant: its bytes, its **declared** length, its type | `CONST4` + `BITMAP4.type`. Carries its own bytes; there is no shared buffer and no offset arithmetic — Decision 4. Comparison follows `const4less`/`const4eq` (C4CONST.C:998-1059), prefix over the shorter length for `r4num`/`r4str`, as `double` for `r4numDoub`/`r4dateDoub` |
| `LeafConstraint` | Entity | What one tag is constrained to | a closed union: `Equal(v)`, `Range(lower?, upper?, exclusions)` or `Empty` — Decision 2. `Empty` is `noMatch` |
| `ConstraintMerge` | Entity | Two constraints on one tag folded into one | `And` and `Or`. The twelve `bitmap4combine*` routines (m4map.c:411-1450) as two functions over the union, because the union already excludes what their `E4ANALYZE` assertions assert |
| `ConstraintNode` | Entity | A node of the constraint tree | leaf (a `QueryTag` and a `LeafConstraint`) or branch (`And`/`Or` and children). `BITMAP4` without its five vestigial fields (§10.5) |
| `QueryPlan` | Entity | The whole decomposition | the root `ConstraintNode` or none, the **trusted-term set**, and `FullyMapped`. Immutable |
| `QueryDecomposer` | Entity | A compiled expression and a tag list, out comes a plan | `bitmap4can` (m4map.c:76-295) plus the reduce/redistribute passes, which are merge placement and not I/O. Pure: it evaluates constant subtrees, and a constant subtree needs no record |
| `RecordSet` | Entity | A bitset over record numbers, with a complement flag | `F4FLAG` (F4FLAG.H:17-30). Sized `RecordCount + 1` because record numbers are 1-based; a set bit past the end is discarded rather than an error (`discardOutOfRange`, m4map.c:2226). `And`, `Or`, `Flip`, `SetAll`, `Count`, `NextSetFrom` |
| `ITagProbe` | Boundary | The keys of one tag: seek to a position, step, say where you are | the step's one seam. Four seek positions, `Step(±1)`, `Top`/`Bottom`, `CurrentRecord`, `KeyLength`, and converting a `ConstraintValue` to key bytes |
| `TagProbe` | Boundary | `ITagProbe` over a real `Tag` | the only place this step touches a file; built at the composition root over `TagCursor` and `SeekConverter` |
| `ConstraintSeeker` | Controller | One leaf into a `RecordSet` | `bitmap4seek`'s four locations and `doCheck` (C4CONST.C:530-695), `bitmap4flagRange`'s walk (m4map.c:1487-1755), and the four dispatch modes of `bitmap4flagGenerate` (§6.3). Takes an `ITagProbe` |
| `QueryExecutor` | Controller | A plan into a `RecordSet` | `bitmap4generate` (m4map.c:2317-2378): leaves seek, branches fold with `And`/`Or` |
| `ResidualFilter` | Controller | Whether a candidate record really satisfies the filter | `log4true` (r4log.c:707-783): split the root `.AND.`, evaluate the conjuncts the plan does not vouch for — Decision 5 |
| `QueryPlanner` | Controller | Filter text into a plan, over a table | the composition root of the pure half: compile, project the tags, decompose |
| `Query` | Boundary | The public object a caller holds | `Top`, `Bottom`, `Next`, `Previous`, `Skip`, `RecordNumber`, `Eof`, `Bof`, `Count`. Both skip strategies (§7.2), because which one runs decides what order records come out in |

**No `L4LOGICAL`, no `E4INFO_REPORT`, no `CONST4` buffer.** The reference needs all three because its
compiled expression is a bare array and its constants live in one growable buffer addressed by
offset. Here the plan is a value that carries what it knows, which is what removes §10.3's
`bufLen`-versus-`bufPos` question rather than answering it.

## Public surface

Small, and it is the first time this port exposes anything above a cursor.

```csharp
using var table = engine.OpenTable("employee.dbf");

Query q = table.Query("UPPER(NAME) = \"SM\" .AND. SALARY > 50000");

for (GoResult go = q.Top(); go == GoResult.Ok; go = q.Next())
    Console.WriteLine(table.GetString(table.Fields["NAME"]));

int howMany = q.Count;        // without reading a record, when the plan is fully mapped
```

- **`Table.Query(string filter)` returns a `Query`.** It does not parse eagerly — `relate4querySet`
  stores the string and `relate4top` parses it (r4relate.c:4058-4104, 6036-6043) — but it does
  refuse a filter whose result type is not logical, which is the reference's `e4relate` / `E80905`.
  Decision 10 has why the plan is nonetheless built once and eagerly, at `Top()`.
- **`Query` moves the table's cursor**, as `relate4top` does, so its methods take the same
  interlocked claim `Table`'s 22 entry points take (ADR-41). It holds no OS handle and is not
  `IDisposable`; it is a view over a table that outlives it.
- **`CodeBaseEngine.QueryTagScope`** is the second public setting after `ExpressionOptions`
  (ADR-51) and `BlockCache*`. Decision 1.
- **The expression engine stays internal.** ADR-51 said the engine becomes public "when `QUERY` needs
  a filter argument, and that is its step's decision" — and the answer is that it does not: the
  argument is a `string`, exactly as `relate4querySet` takes one.

## Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| the keys of one tag | `ITagProbe` | a hand-written probe over a sorted `(key, record)` list | **the whole of leaf evaluation at layer 2** — the four positions, `doCheck`, the range walk, the empty range, the half-open range — with no CDX file, no collation and no disk |
| failures and awkward answers mid-seek | `ITagProbe` | Moq | layer 3: a seek that lands nowhere, a step that fails at a boundary, a tag whose keys ran out mid-walk |
| a table's tags | `QueryTag` list | hand-built values | **the whole of the plan side at layer 1** — eligibility, node matching, decomposition and merging, with no table and no index |
| the record under a residual term | `EvaluationContext` over an in-memory `RecordBuffer` | built from a field list | residual filtering and the trust rule at layer 2 |
| the C library's own answers | the generator's query dump | committed corpus | the gate |

`ITagProbe` is a hand-written fake standing in for something with a real implementation, so
`DEV_APPROACH.md` §5's rule applies: **it needs a contract test class run against both the fake and
`TagProbe`**, the way `RandomAccessSourceContract` does for `IRandomAccessSource`. A fake that
answers "no such position" where a real tag answers "the last key" would make every layer-2 test
above it confidently wrong.

## Decisions

### 1. Tag eligibility replicates CodeBase by default and widens by an explicit option

`e4isTag` skips a tag unless it is unfiltered, not unique-or-candidate under the default
`errDefaultUnique`, collated `none` or `machine`, and of key type `r4double`, `r4charBin` or
`r4memoBin` (§3.4). That excludes the integer, currency, datetime and `GENERAL` tags steps 004-008
built and gated.

**`QueryTagScope.Compatible` is the default and replicates all four refusals.**
**`QueryTagScope.AllSeekable` widens the type and collation gates** to every tag this port can seek.
The filter and unique gates are **not** widened in either scope: a filtered tag indexes a subset of
the table, so seeking it answers a different question, and a `unique` tag stores one key per
duplicate group, so it cannot enumerate a range's records at all. Those two are structural, not
conservatism.

Why both: the default is what makes depth 1 and depth 3 of the gate meaningful — a record set can
only be compared against CodeBase's if the two used the same tags. The widening is what makes the
optimizer useful, and the reason it is safe is that equivalence, not the reference, is the real
check; unlike CodeBase's, ours is not behind a debug flag (§11.6).

**Why the reference's stated reason does not bind us.** r4log.c:71-91 refuses integer and currency
tags because "the constant `6` cannot be converted to an int which would be able to be looked up in
the index file". Step 008 converts exactly that, gated on 3364 keys. The collation refusal is
different and is a real correctness argument — the residual comparison in `log4true` uses `memcmp`
order and a `GENERAL` tag does not — but here it is inert, because Decision 5's trust rule means a
term backed by a tag is never re-compared, and one that is not backed by a tag never consults the
tag's order.

*Recorded as ADR-52, closing the first half of ADR-46.*

**Rejected — widening by default.** It would leave depth 1 and depth 3 of the gate with nothing to
compare against on precisely the tags the port is best at, which is where a decomposition bug is
most likely and least visible.

**Rejected — replicating with no way out.** The refusals would make the headline feature unusable on
most of the corpus, for a reason the port has already disproved.

### 2. The canonical leaf is a closed union, not six nullable slots

§5.3's `E4ANALYZE` assertions (m4map.c:1363-1401) state six pairwise exclusions over
`lt/le/gt/ge/eq/ne`. A normalised leaf is therefore exactly one of three things, so it is modelled
as three:

```
LeafConstraint = Equal(ConstraintValue)
               | Range(lower?, upper?, IReadOnlyList<ConstraintValue> exclusions)
               | Empty
```

where each bound carries its own inclusivity, so `gt` xor `ge` and `lt` xor `le` hold by
construction. Every one of the six assertions becomes unrepresentable rather than checked. `Empty`
is `noMatch` and `Range(null, null, [])` is the unconstrained tag.

Exclusions live on `Range` and not beside it because §5.3's table shows `ne` excluding only `eq`, and
`bmf4AndOt` evaluates "the range, then each `ne`" (m4map.c:1792-1872) — the two genuinely coexist.

### 3. Structural equality is defined over named fields, and promotion is why rows may differ

`e4isTag` compares two node arrays backwards in lockstep and, per node, requires equal `len`,
`numEntries` and `numParms`; both field or both not; the same field by a `memcmp` over the `FIELD4`
prefix; equal constants; and **equal rows unless both rows are field loaders** (r4log.c:234-236).

There is no `memcmp` here, so the field half is stated: **same name, length, decimals, type, offset
and owning table.** `FieldDefinition` carries all six.

The row exemption is not a tolerance, it is the promotion accommodation, and step 010 proved the
matched pair: promotion is an **in-place row rewrite** targeting exactly the field-loader rows
(`ExpressionNode.WithRow`, `DESIGN.md` §2b of step 010), so `K_NUM > 0` compiles its field node to
row 8 where the tag on `K_NUM` compiled it to row 9. Without the exemption no numeric tag would ever
match. With it, any two field-loader rows are interchangeable — and what keeps that honest is the
field identity, not the row.

**The length that must match is the declared length**, and step 010 found that a promoted numeric
field still reports the field's width rather than the promoted row's. That is why this compares
`ExpressionNode.Length` and not something recomputed.

### 4. A constraint carries its bytes; the reference's offset arithmetic is not reproduced

`const4get` and `const4duplicate` set `con->offset = log->bufLen - len` — from the *allocated*
length, not the used one (C4CONST.C:966, 993) — and §10.3 cannot settle whether that is latent or
actual because `u4allocAgain` is not in this source drop. §10.3's own recommendation is to index
from used length. This goes one further: there is no shared buffer, so there is no index. A
`ConstraintValue` is a value.

What must survive the change is the thing the offset was never the point of: **the declared length**.
`const4get` reads `info[pos].len`, and that number is what decides prefix behaviour — `"SM"` has
declared length 2 against a `keyLen` of 20, and handing the seeker a padded runtime width instead
collapses every prefix range into a wrong equality.

### 5. A term is trusted to the bitmap when it produced a leaf — which trusts more than the reference does, deliberately

§7.3's rule is "trust a term iff `e4isTag` matched one of its operands", and the reference expresses
it as `(infoReportPtr-1)->tag == 0 && (infoReportPtr-2)->tag == 0` (r4log.c:770). **Those two
positions are not the operand roots.** `infoReportPtr-1` is always the right operand's root, but
`-2` is the node before it, which is the left operand's root only when the right operand is a single
node. For a term like `K_TEXT+K_CITY = UPPER("x")` — left operand tag-matched, right operand two
nodes — both positions read zero and the reference **re-evaluates a term its own bitmap already
answered**. Slower, never wrong.

Here the trust rule is a property of the plan: **a term is trusted exactly when decomposition
produced a surviving leaf for it.** That trusts strictly more terms than the reference, and only
where the reference was being conservative by accident of positional addressing.

Trusting a term is safe only if its leaf is *exact* rather than a superset, and the two places that
could make it a superset both turn out not to:

- **A dropped conjunct widens the branch, not a leaf** (§4.1). The dropped conjunct produced no leaf,
  so it is not trusted, so it is re-evaluated. The rule handles it by construction.
- **The partial-string widening is exact, not conservative** — see Decision 6.

Depth 4 of the gate is what proves the divergence is the safe direction: our record sequence must
equal the reference's even though our residual filter ran on fewer terms.

*Recorded as part of ADR-53.*

### 6. §12 item 2 is resolved: the widening asymmetry is a consequence, not a second defect

`QUERY-OPTIMIZER.md` §12 item 2 leaves open why only two of the six operator branches widen a short
string constant. Reading the four reflected branches together (m4map.c:211-283) answers it:

| Path | Slot written | Widens? |
|---|---|---|
| `>` tag left | `gt` | **yes** |
| `<` constant left | `gt` | **yes** |
| `<` tag left | `lt` | no |
| `>` constant left | `lt` | no |
| `>=` tag left / `<=` constant left | `ge` | no |
| `<=` tag left / `>=` constant left | `le` | no |

**Exactly the two paths that produce `gt` widen, and they are the only two that produce it.** The
other four write a slot where a short constant needs no adjustment. So the asymmetry is complete
rather than partial, and it is not a defect.

And it is *exact*, not a safety margin, which is what Decision 5 leans on. `NAME > "SM"` on a `C(20)`
field compares a 20-wide left operand against a 2-wide right one, and `EXPRESSIONS.md` §8 — the
tie-break the corpus found in step 010 — says the strict comparisons break a tie on width, so
`"SM" + 18 blanks > "SM"` is **true**. The expression admits the whole `"SM…"` group; a byte-exclusive
`gt` seek would skip it; `ge` restores exactly it and nothing more. An equal-width constant does not
reach the widening at all, because `hold.len < keyLen` is false.

This closes §12 item 2, and it is the same prefix rule ADR-50 settled for `=`, arriving from the
other side.

### 7. The reference's defect at m4map.c:216/243/251/278 is not replicated — the intent is

`if ( tagPos = 1 && constPos == 0 )` is an assignment. C evaluates the condition as `constPos == 0`
alone and clobbers `tagPos`, which is never read again. C# will not compile the letter of it, so this
must be decided rather than inherited.

**The port implements the intent: reflect when the constant is the left operand.** The four
`AS Feb 19/13` comments state it unambiguously, and the two readings agree for every comparison whose
operands are single nodes. They diverge only for a comparison nested inside a larger expression —
which is why the oracle case list opens with `.AND.`-nested `4 > K_NUM` beside `K_NUM < 4`, checked to
return the same records. Getting it wrong swaps `lt` for `gt`, and a swapped bound is a wrong record
set, which is R12's most concrete instance.

*Recorded as ADR-53, closing the second half of ADR-46.*

### 8. The go-around complement optimisation is omitted, but the complement flag is kept

`bitmap4flagRange` flags the *complement* when a range covers more than half the tag, returning
`doFlip = -1` (m4map.c:1668-1684). It is pure speed — bounding cost by the smaller of a range and its
complement — and §12 item 5 records that `tfile4position`'s normalisation was not transcribed. A
wrong threshold is a wrong complement flag, and a wrong complement flag is a wrong record set for
half the table.

So: **not implemented in this step, and the reason is written down rather than the omission being
silent.** `RecordSet` still carries `IsComplement`, because `bmf4OrNe` genuinely needs it — an OR over
an exclusion flags the excluded value and then complements (m4map.c:1875-1899) — and because
`bmf4AndOt`'s "no matches" case is expressed as set-all-and-flip (m4map.c:1834-1839).

Depth 3 of the gate compares record **sets**, not the flag, so omitting the optimisation cannot make
it red. That is the point: it is a speed decision, and it is reversible without touching the gate.

### 9. OR-combination of same-tag leaves is limited to what is provably one constraint

`bmf4OrOt` (m4map.c:1902-2188) is the largest routine in the module, handles OR ranges with their own
position and flip logic, and §12 item 7 records that it was **read for shape, not transcribed** —
its rules are *believed* to mirror `bmf4AndOt` with the polarity inverted.

Building on a belief is what this project's gates exist to prevent. So `ConstraintMerge.Or` folds two
leaves on one tag only where the result is unambiguously a single constraint — overlapping or
adjacent ranges, a value inside a range, two exclusion lists — and **leaves everything else as two
leaves that union at the bitset**. `x < 1 .OR. x > 9` stays two seeks and one `Or`.

Correct in every case, slower in a few, and invisible to the gate: depth 3 compares the resulting
set, not the tree that produced it. If a corpus case ever shows the reference doing better, the
routine can be transcribed then, against a case that proves it.

### 10. The plan is built once, eagerly, at `Top()`

Not incrementally as records are read (§1). `Table.Query(…)` stores the string and validates its
result type; the first `Top()` compiles, decomposes, evaluates every leaf and materialises the
bitset. It is what the reference does, it is what makes `Count` answerable without reading a record
when the plan is fully mapped (r4relate.c:6780-6781), and the alternative — seeking lazily per skip —
would re-seek a tag once per record and lose the whole point.

### 11. Both skip strategies are implemented, because the order is user-visible

With a tag selected, step the *tag* and take records whose bit is set: results come out in **tag
order**. Without one, jump to the next set bit by scanning the bitset a byte at a time and `Go`
there: results come out in **record-number order**, and whole runs of unset bits cost no record read
(§7.2, §11.3). That second path is where the speed comes from and the first is where the ordering
comes from; neither substitutes for the other.

### 12. The reference is dumped twice per filter, because its optimized answer is not always its true one

`RELATION4.bitmapDisable` is a plain struct member, and setting it makes `bitmap4can` return 0 at
once, set `bitmapsFreed` and clear `fullyMapped` (m4map.c:341). `log4true`'s `|| bitmapsFreed` arm
then re-evaluates **every** conjunct per record, and `f4flagIsSetFlip` answers yes for every record
from a null `flags` pointer (r4relate.c:82-91). So the same query, run twice, gives CodeBase's
optimized answer and **CodeBase's own scan truth** — the filter evaluated per record by its own
evaluator, with the optimizer entirely out of the circuit.

**This is not a nicety, it is what makes Decision 7 checkable.** The reference's `tagPos = 1` defect
can invert a bound on an `.AND.`-nested reflected comparison, and the port implements the intent
instead — so on that case the port is *expected to disagree* with the reference's optimized answer.
Without a second reading there is no way to tell "we decompose differently" from "the reference is
wrong here", and a gate that cannot tell those apart is a gate that gets a carve-out written into it
on faith.

With it, the case resolves itself. Where the two reference readings **agree**, the port must match
both, and depths 3 and 4 assert exactly that. Where they **disagree**, the reference has witnessed
its own defect, and the port must match the scan truth — which is also what Gate A independently
requires, from a third direction.

**And it turns `QUERY-OPTIMIZER.md` §12 item 1 from an open question into a settled one.** That item
records that the `tagPos = 1` intent is "inferred, not witnessed", supported only by four source
comments. Two dumps of one query witness it.

### 13. Efficiency is gated by counting, not by timing — because a correct optimizer that never optimizes passes everything else

**Both correctness gates are blind to an inert optimizer.** `f4flagIsSetFlip` answers yes for every
record when there is no bitmap (r4relate.c:82-91), so a `QUERY` that decomposes nothing, seeks
nothing and scans every time is **perfectly correct**: Gate A compares it against a scan and finds a
scan. This is ADR-40's lesson arriving in a second module — every gate in step 009 passed on a block
cache that was correct and never hit — and the answer is the same one: **gate the speed-up itself.**

**Part of it is already covered, and it is worth knowing which part.** Gate B depths 1 and 2 compare
against the reference's own `infoReport[i].tag` and `fullyMapped`, so an optimizer that falls back
everywhere fails them on the first case. What that does **not** cover is `QueryTagScope.AllSeekable`,
where there is no reference at all, and the whole question of whether a *correctly decomposed* plan
was then executed sensibly — a plan can match every tag and still walk them stupidly.

So efficiency is evaluated on three tiers, and only the first two are gates.

**Tier 1 — the plan's shape. Layer 1, free, exact, no I/O.** The merge algebra exists for one stated
reason: "if a bitmap is `(val < 10 && val > 5)`, we want to create a simple bitmap between 5 and 10,
instead of having 2 different bitmaps" (C4CONST.C:300-307) — *so the tag is seeked once*. The
accepted-value population test of sub-step 4 proves a merge is **correct**; it says nothing about
whether merging ever **happens**, and a `bitmap4redistribute` that never brings two same-tag leaves
together is correct and useless. So the plan is asserted structurally: `x > 5 .AND. x < 10` is **one
leaf with one range**, not two leaves under an `And`; `x = 1 .OR. x = 1` is one leaf; a filter over
two tags is two leaves and no more. **Leaf count is the seek count**, so this is an efficiency
assertion that needs no clock and no file.

**Tier 2 — counted work. Deterministic, machine-independent, and a gate.** Step 009 left the
instrumentation: `CountingSource` and `CodeBaseEngine`'s factory constructor let a test count every
block read that reaches a file. Four quantities, counted over `CDXDEEP` (600 records, already a gate
table, deep enough for the ratios to mean something):

| Counted | Assertion | Why it is exact rather than a threshold |
|---|---|---|
| records read by `Count` when the plan is fully mapped | **zero** | `relate4count` returns `f4flagCount` without reading a record (r4relate.c:6780-6781). The reference's headline claim, and it is a number, not a ratio |
| residual evaluations on a fully-mapped query | **zero** | every term is trusted, so `ResidualFilter` must not run at all |
| records read on a fully-mapped query | **exactly the records returned** | no candidate is read and rejected, by definition of fully mapped |
| block reads, selective query versus full scan | a ratio, gated with headroom | the only one that needs a threshold, and the one that catches "decomposed right, executed badly" |

The ratio is the only judgement call, and it is set the way ADR-40 set its 2×: far enough below the
measured figure that it does not flap, far enough above zero that an inert optimizer cannot pass.
**Counted and not timed**, for the reason step 009 established the hard way — a figure that moves
when only its position moves is measuring the runtime, not the code — and because a read count is
identical on every machine, inside WSL and out.

**Tier 3 — measured, recorded, not gated: where the optimizer stops winning.** An optimizer can be
*worse* than a scan. A filter matching most of the table pays for the index descent and the tag walk
and then reads nearly every record anyway. The reference answers that with the go-around complement
(§6.2), which **Decision 8 omits** — so the honest way to hold that omission is to name the
measurement that would overturn it. A selectivity sweep over `PERF10K` — 0.01% to 100% matched, block
reads and wall time on both arms — locates the crossover and goes in `SUMMARY.md`. `PERF10K` stays
what `net/corpus/README.md` says it is: benchmark data, **not** a gate. If the sweep shows a bad
regression at high selectivity, Decision 8 gets reversed with a measurement behind it rather than a
preference.

## Open questions

1. **Is `relate->set` populated and readable at the moment the generator wants to dump it?** The
   bitset is copied into `relate->set` at m4map.c:2465 during `relate4top`, and `RELATE4.set` is a
   plain `F4FLAG` member of a struct the header exposes — so the read is straightforward. What is not
   certain is whether it survives to the end of the scan unchanged, or whether the two skip paths
   mutate it. **Answered by sub-step 1**, by dumping it immediately after `relate4top` and again
   after the scan and comparing. If it does not survive, the dump takes it at the first point only.
2. **What does the C library report for a query with no optimizable term at all?** `bitmapsFreed`
   is set and `f4flagIsSetFlip` answers yes for every record from a null `flags` pointer
   (r4relate.c:82-91). The dump must distinguish "the bitset is every record" from "there is no
   bitset", because the port's `QueryPlan` distinguishes them and only one of them is `FullyMapped`
   false with a root of none. Sub-step 1.
3. **Does `e4tagExpr` (−510) become reachable now?** Step 010 left it named as unreachable because
   every recorded rejection was parsed with `tagPtr = 0`. A filter expression is still parsed without
   a tag context, so probably not — but the second rejection pass step 010 owes the corpus is cheap
   to fold into this step's generator run, and sub-step 1 is the moment to do it.
4. **Does the corpus need a table wide enough for the bitset to matter?** Every gate here is
   correctness and `CDXEXPR`'s 16 records answer all of them. `PERF10K` exists and is not a gate, so
   a `Query` benchmark is available for free if wanted — but ADR-40's performance gate is on the read
   path, and extending it to queries is not this step's promise.
