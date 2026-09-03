# 010-expr — state

**Updated:** 2026-09-03 · **the step is complete: all nine sub-steps are done and the capability is
closed.** An expression tag is an ordinary tag. **Every key of every expression tag in
`CDXEXPR.cdx` is rebuilt byte for byte** — 22 tags, 338 stored keys, every computed key length
agreeing with its tag header — each tag **walks the records the reference stored in its order**, and
moving to a record by number rebuilds that record's key and descends to it. **ADR-28's refusal is
gone** and `CachedReadGoldenTests.KeysABareField` is deleted, and the thirteen functions Visual
FoxPro has that CodeBase does not are supported and gated by Visual FoxPro's own answers. Up from
1450, with no golden expectation changed. **1909 tests.** See [`SUMMARY.md`](SUMMARY.md).

**Gate assertion 7 was finished after the rest**, and the closing note said it held when it only
partly did: the node table and evaluate-at-a-node were tested, but the is-constant predicate was
checked on nine hand-written cases rather than over the dump, and nothing asserted that a `Tag`
keeps its compiled expression. `OptimizerContractGoldenTests` now covers all four clauses — see
below.

## Where the sub-steps stand

| # | sub-step | state |
|---|---|---|
| 1 | Generator: the `H` probe, `CDXEXPR`, the five dumps | **done** |
| 2 | Function table + lexer | **done** — see below |
| 3 | Parser — the whole parse pass | **done** — see below |
| 4 | Types and overload resolution | **done** — see below |
| 5 | Evaluator: kernels and null semantics | **done** — see below. *The stop point.* |
| 5b | The VFP-only functions (ADR-43) | **done** — see below |
| 6 | The text-formatting family — `STR`, `STRZERO`, `DTOC`, `TTOC` | **done** — see below |
| 7 | Axis B in full — key time, and `DESCEND`/`ASCEND`'s kernels | **done** — see below |
| 8 | Wire into the tag path | **done** — see below |

## What sub-step 1 produced

In `net/corpus/`: `CDXEXPR.DBF` + `.cdx` + `.fpt`, their two ordinary dumps,
`CDXEXPR.expr.dump.txt`, and `FUNCTIONS.dump.txt`. In `test-files-generator/src/`: `dump-expr.h`,
`dump-expr.cpp`, `case-cdxexpr.cpp`. Beside this file, [`ORACLE-SUITE.txt`](ORACLE-SUITE.txt) renders
the expression dump flat as `EXPRESSION -> value` — **derived, not a source**: regenerate it from the
dump, assert against the dump.

`CDXEXPR.expr.dump.txt` has five sections. Their meanings are not interchangeable:

| section | what it is | is it an expectation for the port? |
|---|---|---|
| `[constant expressions]` | 109 cases, no record dimension | yes |
| `[field expressions]` | 49 cases × 16 records | yes |
| `[clock-dependent expressions]` | `DATE()`, `TIME()` — **shape only, no value** | type/length/nodes only |
| `[rejections]` | 18 strings CodeBase refuses | yes |
| `[vfp-only]` | 13 names VFP has and CodeBase does not | **no — the port accepts these (ADR-43)**, and VFP is their oracle (ADR-44) |

Each expression also carries its **compiled node array**: per node the row index of `v4functions`
that overload resolution chose. That is the resolution gate, and it is what turns "a missing overload
row silently selects its neighbour" into a mismatched integer.

## What sub-step 2 produced

**The plan's sub-steps 2 and 3 swapped their first halves**, and `PLAN.md` records it. The reason is
in the reference: an operator is not tokenized and then looked up, it is **prefix-matched against
`v4functions` rows 36-106 in row order**, first match wins (`e4parse.c:1195`,
`e4lookupInFunctionArray`). `>=` beats `>` only because row 60 precedes row 84. So the scanner is
table-driven, the table has to exist before it, and a scanner carrying its own operator list would be
the second source of truth `DESIGN.md` §2b forbids. Sub-step 2 is now **function table + lexer**;
sub-step 3 is the parser. Nothing else moved, and the transcription gate simply lands one sub-step
earlier — which the plan's own ordering rationale wanted anyway.

In `net/CodeBase.Net/Expressions/`: `ExpressionType.cs` (the ten reachable type codes and the
fourteen transcribed ones, valued as the C's own characters so a row compares directly),
`FunctionRow.cs`, `FunctionTable.cs` (**193 rows**, typed from `e4functi.c:32-301`),
`ExpressionTokenKind.cs`, `ExpressionToken.cs`, `ExpressionLexer.cs`. Four codes were added to
`ErrorCode`: −450, −480, −490, −500.

**The transcription gate passed on the first run**, all 193 rows, every field. Two independent
readings: the port typed from the C source text, the dump walked out of the compiled library.
Two mutations were run and both reddened by name — a swapped parameter type on row 64 and a changed
priority on row 99.

**The scanner is a self-driving two-state pull scanner**, which is what the reference is without the
parser wrapped round it. It has to be: a minus begins a literal where a value is expected and is
subtraction where an operator is expected, and a dot introduces `.T.`/`.NOT.` in the first place and
`.AND.`/`.OR.` in the second. One extra flag — *what comes next is the start of a parameter* — buys
two more of the reference's behaviours: `DATE()` scans and `DATE( )` does not, and `UPPER(` is
`e4rightMissing` rather than `e4unrecValue`. *(Sub-step 3 widened that flag to apply after a comma as
well as after the opening bracket, which is where the reference re-enters the same loop; see below.)*

**Seven of the eighteen recorded rejections are the scanner's**, and its golden gate asserts both
halves: those seven carry the code the C gave, and the other eleven **scan cleanly**, because
refusing them here would answer at the wrong pass. All 162 expressions the C compiled scan, and so do
all 13 `[vfp-only]` names, which is what makes ADR-43 possible at all. The `-FIELD` mutation from
`PLAN.md` was run and reddened the rejection gate.

**Three things the scanner settled that the spec had wrong or did not say**, now fixed in
[`specs/EXPRESSIONS.md`](../../specs/EXPRESSIONS.md) §3 and §4:

- **Whitespace is three rules, not one.** `s4scanRange` takes an inclusive range and the three call
  sites hand it three different ones: before a **value**, `' '` only (`e4parse.c:2531`); before an
  **operator**, `\x01`-`' '` (`:1187`); between a name and a possible `(`, `\x00`-`' '` (`:2506`).
  The spec said "chars ≤ `' '`" and cited 2531 for it, which is the one site where that is false. A
  tab before an operator is a blank; a tab before a value is `e4unrecValue`.
- **An empty parameter list is empty only when nothing at all is in it.** `expr4parseParamaters`
  tests for the closing bracket before skipping anything (`:1871-1890`), so `DATE( )` is a refusal.
- **A field name over ten characters is `e4unrecValue`**, not a name-specific code. Which also means
  the `[rejections]` comment on `NOSUCHFIELD` — "unknown name" — names the wrong cause: the name is
  eleven characters, and an *unknown* field of ten or fewer is `e4fieldName` (−210), as
  `OTHER->K_TEXT` records.

**One reference quirk was transcribed rather than tidied**, and has a test saying so: `E4LAST_OPERATOR`
is 106, two rows past the last real operator, so `CHR` and `DEL` are matched where an *operator* is
expected. Narrowing it would make this port refuse text the reference accepts, which is the one thing
`DESIGN.md` §2b's rule forbids.

## What sub-step 3 produced

**The parse pass is bigger than `PLAN.md`'s one line said, and the line was moved rather than the
work.** Reading `e4parse.c` before writing any of it showed that `expr4parseLow` runs the
shunting-yard to completion and *then* calls `e4massage`, and that five behaviours sit on the parse
side of that boundary. `DESIGN.md` §2b gained a subsection for them, `PLAN.md`'s sub-step 3 was
rewritten, and [`specs/EXPRESSIONS.md`](../../specs/EXPRESSIONS.md) §4 now records them as format
facts rather than as a note here:

- **A field's loader row comes from the field's type**, at parse time, so the parser needs field
  metadata. That is the new seam, `IFieldMetadata`, which `DESIGN.md` §Seams had not listed.
- **The parameter-count check is parse-time**, and it happens *twice* with two different conditions:
  parse time refuses a mismatch when the row declares `numParms >= 0`, `e4massage` only when `> 0`.
- **`DTOC` and `TTOC` given two parameters are rewritten, not refused** — the picture's node is
  popped and `DTOC` becomes `DTOS`. It is the one place the parse pass changes a row it has chosen.
- **Nine functions read a constant parameter and remove its node.** `STR(K_NUM,12,3)` is **two**
  nodes, not four. `SPACE` is the exception that reads its constant without popping it.
- **A top-level `)` or `,` ends the expression and the rest of the text is ignored**, so `K_LOG)`
  compiles to `K_LOG`.

**And the fact that made the sub-step gateable on its own.** Promotion rewrites a node's row and its
length and nothing else, so `numParms`, `numEntries` and `fieldNo` are settled by the parser and never
touched again — and the dump records all three per node. So the layer-4 gate is a **shape** equality
against the C's own `E4INFO` arrays, taken *before* any type system exists: node count plus the
per-node `(numParms, numEntries, fieldNo)` sequence, over all 162 expressions the C compiled. It
passed on the second run, the first failure being a wrong error code and not a wrong shape. `numEntries`
is computed in the parser here and in `e4massage` there, which is a divergence in *where* and not in
*what*: it follows from `numParms` and the postfix order alone, and moving it is what buys the gate.

In `net/CodeBase.Net/Expressions/`: `FunctionRows.cs` (the row indexes the engine names, transcribed
absolute where `e4expr.h` derives them by addition — with a unit test in place of the C's four
compile-time assertions), `FieldReference.cs`, `IFieldMetadata.cs`, `TableFieldMetadata.cs`,
`ExpressionConstant.cs`, `ExpressionNode.cs`, `ParsedExpression.cs`, `ExpressionParser.cs`, plus
`FunctionTable.FindByName`. Five codes were added to `ErrorCode`: −210, −400, −425, −430, −460, −470.

**Two things the reference does that a sensible parser gets wrong**, both now tested:

- **`STR` and `STRZERO` report a non-constant width as `e4typeSub` (−460), not `e4notConstant`
  (−425)**, while the other eight functions report −425. The C's `expr4parseStrFunction` describes
  −460 and *returns* −425, with a comment saying the message type was deliberately changed
  (`e4parse.c:1434-1436`). The corpus records both codes, and this was the one thing the shape gate's
  first run got wrong.
- **A parameter list is re-tested at every comma, not only at its opening bracket.** So
  `UPPER(K_TEXT,)` is `UPPER(K_TEXT)` with one parameter, and `UPPER(K_TEXT,` is `e4rightMissing`.
  **This corrected the scanner**, whose sub-step 2 flag only applied at the opening bracket: it is now
  `parameterStart` and is set after a comma too, which is exactly where `expr4parseParamaters`
  re-enters its loop. Both halves are unit-tested and the scanner's golden gate is unmoved.

**One divergence left standing, and it is narrower than it first looked.** Text that *ends* at a
top-level comma — `K_TEXT,` — is accepted by the reference, which stops at the comma and compiles
`K_TEXT`, and refused here with `e4unrecValue`, because after any comma the scanner expects a value
and there is none. **A comma anywhere else at the top level already matches**: `K_TEXT,K_CITY`
compiles to `K_TEXT` here as it does there, and so does `K_TEXT + K_CITY) .AND. K_LOG` — both are
tested. That closes the one case that sounded reachable, a legacy tag keyed on `LAST,FIRST` where
`LAST+FIRST` was meant, which CodeBase silently indexes on `LAST` and this port now does too.
No source is known for a stored expression that ends at a comma: a tag header's expression is
NUL-terminated, and a truncation lands inside a parameter list, where the codes already agree. So it
stays as it is, in the direction `DESIGN.md` §2b's rule tolerates — refusing text the reference
accepts, never accepting text it refuses.

## What the mutations showed, including a corpus gap

Four mutations, run under the copy-aside rule and restored from a checksummed copy:

| Mutation | Reddened |
|---|---|
| Keep a folded constant's node instead of removing it | the shape gate, on node count, plus 4 unit cases |
| Every subtree size is one | the shape gate, on `numEntries`, plus 5 unit cases |
| A field node always reports field one | the shape gate, on `fieldNo`, plus 1 unit case |
| Stop counting a variadic operator's repeats | **layer 1 only — the shape gate stayed green** |

**The last one is a corpus gap, not a gate defect.** `CDXEXPR` holds no expression with a run of three
or more identical `.AND.`/`.OR.` operators — the only three in the whole dump are two-term — so a
variadic node's `numParms` is never anything but 2 in the oracle, and the collapse that
`QUERY-OPTIMIZER.md` §4.1 maps a bitmap branch's children onto is gated by C#-authored tests alone.
The second generator run `PLAN.md` budgets for should add `A .AND. B .AND. C`, a four-term `.OR.`, and
a mixed `A .AND. B .OR. C .OR. D`, over fields as well as constants.

## What sub-step 4 produced

**The resolution gate passed on its first run**, all 212 expressions, every node's row and length and
the conditional's second length, plus each expression's result type and `hasTrim`. In
`net/CodeBase.Net/Expressions/`: `TypeResolver.cs`, `CompiledExpression.cs`, `ExpressionNode` gaining
`Type`, `Immediate2` and `WithRow`, and 20 more named rows in `FunctionRows`. Two codes were added to
`ErrorCode`: −440 and −460.

**The one thing `DESIGN.md` had wrong, and it is a detail with consequences.** §2b said a promotion
"changes a node's row and its length". The second assignment is to the *simulated stack*, not to the
node: the producer's `len` was written when the walk visited it and the walk never returns
(`e4parse.c:940` against `:230-231`). So **a promoted numeric field still reports the field's width** —
`K_NUM + 0`'s field node is row 8 with length 12, not 8 — which the oracle shows directly and which
§6's optimizer reads as the node's declared length. Corrected in `DESIGN.md` §2b and recorded in
[`specs/EXPRESSIONS.md`](../../specs/EXPRESSIONS.md) §6, together with the related fact that a
promotion does not update the stack's recorded *type*, so a producer promoted during a failed attempt
at one overload stays promoted when the next is tried.

**Where the parameter-count check really lives: both passes.** Sub-step 3 found it at parse time;
`e4massage` checks again, and with a narrower condition — parse time refuses a mismatch when the row
declares `numParms >= 0`, resolution only when `> 0`. Both are implemented, and the spec's §6 item 1
now says which is which.

**One default is not in the source drop.** A memo operand's expression length is 1024, and
`code4initLow` — where `CODE4`'s defaults are set — has no body in `original/source/`. The corpus is
the only witness, which `specs/EXPRESSIONS.md` §11 already recorded and the resolver now cites.

## The second generator run, which closed the gate's row coverage

`PLAN.md` budgeted for this and it was needed exactly where it said: **sub-step 4's own gate assertion
2** requires every row of the table to be oracle-gated or declared unreachable, and on the first
reading only **98 of 193** were reached. 46 of the remaining 95 were reachable and simply absent — a
row a wrong overload could have been selected into with nothing to compare against.

46 expressions were added to `case-cdxexpr.cpp` and the generator re-run. **Every one landed on the
row it was written for**, and coverage went 98 → 144. What was missing, by group:

| Group | Rows | Why nothing reached them |
|---|---|---|
| The long spellings `.TRUE.`, `.FALSE.`, `!` | 30, 32, 35 | name lookup matches on length, so `.T.` never reaches `.TRUE.` |
| A variadic run of three or more terms | — | the whole dump had only two-term `.AND.`/`.OR.`, the gap a sub-step 3 mutation found |
| Date arithmetic in the other orders | 43, 48, 49 | only `date + num` was present |
| The binary float's own rows | 25, 45, 50, 59, 67, 75, 83, 97, 103 | only `H * n` was present |
| Date-as-double and timestamp comparisons | 54, 57, 65, 71, 73, 95 | four spellings were covered, six were not |
| The conditional's non-string overloads | 129, 130, 131 | only the string form was present |
| Stored-date overloads of `TTOC` and `MONTH` | 117, 139 | a date-as-double argument walks straight past them |
| `EMPTY` over timestamp, date-as-double, logical | 153, 154, 155 | only the character and numeric forms were present |
| Key conversion, one row per type | 157-162, 165, 176-183 | a fifth of the table, and only its two character rows were reached |

**The 49 that remain are unreachable by construction, and the gate names all three reasons.** 39 rows
name a field type this library does not open — wide strings, the OLE-DB structs, the sized integers,
the millisecond timestamp — so no expression can produce one. 6 are the currency comparisons, dead in
the reference itself because a currency operand promotes to a double before its own exact-type row is
reached (`specs/EXPRESSIONS.md` §7.7). 4 are the report writer's two rows, the calculation row and the
terminator.

**The corpus regenerated byte-identical except the expression dump** — one file changed out of the
whole tree, which is what adding expressions rather than tags buys. The two READMEs now say that
the expression lists are sized by row coverage, so the next person to add a case knows what the size
is for.

## What the mutations showed

Four against the resolver, under the copy-aside rule and restored from a checksummed copy:

| Mutation | Reddened |
|---|---|
| Drop one promotion from the loop | the resolution gate (3 golden), plus 6 unit cases |
| Advance one row past the matching overload | the resolution gate (3 golden), plus 30 unit cases |
| **Wrap the producer in a conversion node instead of rewriting its row** | the resolution gate **on sequence length** (1 golden), plus 7 unit cases including node-count invariance |
| A promotion also rewrites the producer's declared length | the resolution gate (1 golden), plus 1 unit case |

The third is the one `PLAN.md` singles out, and it is the shape `DESIGN.md`'s own earlier draft would
have shipped. The fourth exists because the correction above had to be provable, not merely stated.

**A restore went wrong and was caught.** The mutation-B run left `TypeResolver.cs` unrestored, because
the restoring `cp` ran with a working directory the earlier `cd` had changed — the copy-aside rule was
followed and the *path* was still wrong. It was restored from the checksummed copy and verified by
`md5sum`. `DEV_APPROACH.md` §4 already says the rule is per-restore; the failure mode here was the
path, not the copy, and verifying the checksum is what turned it into a non-event.

## What sub-step 5 produced

**Both gates are green.** The layer-1 semantic table — the deciding one, because a table of
expression and expected value reads as a specification while a transcript of the reference's answers
only confirms our own reading — is **168 cases** over the kernels. The layer-4 value gate confirms
it: **every one of the 131 constant expressions and all 77 field expressions at all 16 records match
the reference's raw result bytes**, minus two families named by row rather than by name.

In `net/CodeBase.Net/Expressions/`: `ExpressionValue.cs` (the closed union over the ten types, with
`ToRaw` for the runtime form), `ExpressionOptions.cs` (**ADR-42**, made real), `EvaluationContext.cs`,
`ExpressionEvaluator.cs`. Plus `FoxDate.FromJulian` and `MonthAndDay` — the julian-to-text direction
the reader never needed — and an internal `Table.CurrentRecord`, which sub-step 8 needs anyway.

**Two families are deliberately outstanding, and the rule that defers them is computed from the rows
rather than written as a list of expression names**, so finishing a family empties it by itself:
`STR`/`STRZERO`/`DTOC`/`TTOC` are sub-step 6's, and `DESCEND`/`ASCEND` are sub-step 7's because they
turn a value into key bytes. **`DTOS` moved from 6 to 5** and `PLAN.md` records it: it is the
julian-to-text conversion `DAY`/`MONTH`/`YEAR` need anyway and its answer is stated, so splitting it
off would have been artificial. It also means the eight century-pivot cases are gated now.

### The two things the corpus caught, and what each was worth

**A memo operand is NUL-padded, not blank-padded** — and `specs/EXPRESSIONS.md` §11 already said so.
The value gate found it on the first run at all 16 records. It matters beyond the byte: key time
turns a key's trailing zero bytes into blanks, so a memo operand keys as though it had been trimmed.
Recorded before, misread by me, caught by the gate — which is the gate working as designed.

**The inclusive comparisons do not break a tie on width, and the strict ones do.** This is new, and it
is the most interesting thing in the sub-step. `p1` — the tie-break flag — is assigned during massage
under `if (functionI == E4GREATER)` and `if (functionI == E4LESS)`, the two *named head* rows 84 and
91 (`e4parse.c:800-822`). A `>=` or `<=` node carries neither row, so its flag is never written; then
`e4lessEq` calls `e4greater()` **on its own node** and negates the answer (`e4functi.c:2012-2034`),
reading that unwritten flag as zero. So `"AAAA" > "AAA"` is true **and** `"AAAA" <= "AAA"` is true:
the same two strings compare differently depending on which operator asks. Now in
`specs/EXPRESSIONS.md` §8 with a table and the witness.

**Only two records in the whole corpus distinguish it** — the two whose `C(20)` text begins with `M`,
against `K_TEXT <= "M"`. Nothing in the constant set could have found it, because a constant
comparison has two operands of equal width and the tie-break never fires. That is the argument for
the field half of the value gate, made concrete.

### What is owed, and one deviation

- **The Windows ANSI upper-case mapping is not implemented.** The reference chooses between it and
  the ASCII mapping by the table's code page (`e4functi.c:2878-2911`), and the corpus witnesses only
  the ASCII path — on a table with no code-page mark, bytes `C9` and `E8` pass through `UPPER`
  unchanged. A table marked 1252 or 1250 needs the other mapping and **there is no corpus case for
  it**: the generator would need a 1252 table with an `UPPER` tag over accented text. Until then
  `UPPER` is ASCII-only, which is right for every corpus table and wrong for a 1252 one.
- **`<>` over one absent operand is ungated.** `EXPRESSIONS.md` §6 says the forced answer is true
  where every other comparison's is false, and it is implemented that way; the corpus has
  `K_NNUM = 0` over null rows and no `K_NNUM <> 0`. One generator case would close it.
- **ADR-42 said presets mirror the reference's five language builds; two are implemented** — the
  invariant default and the reference's own. The other four are a picture string each and are in
  `specs/EXPRESSIONS.md` §7.3's table; none is gated, so adding them now would be shipping untested
  data. Named here rather than silently skipped.

### What the mutations showed

Five, under the copy-aside rule and restored from a checksummed copy. Every one reddened **both**
layers, which is the point — the layer-1 table is not a restatement of the implementation:

| Mutation | Reddened |
|---|---|
| Prefix equality compares the full width | 1 golden, 4 unit |
| A trim pads with blanks instead of zero bytes | 2 golden, 3 unit |
| The inclusive comparisons break a tie on width | 1 golden, 2 unit |
| Division by zero raises instead of answering zero | 1 golden, 4 unit |
| Date arithmetic does not short-circuit on a blank date | 1 golden, 1 unit |

The first is `PLAN.md`'s own "prefix `=` must redden axis A only". Axis B does not exist yet, so the
second half of that mutation becomes a real check at sub-step 7 rather than a trivial one now.

## What sub-steps 6 and 7 produced — and the answer to "do we build the same keys?"

**Yes.** `ExpressionKeyGoldenTests` compiles the expression each of `CDXEXPR.cdx`'s **22 tags stores
in its own header**, evaluates it at every record the tag indexes, converts the value to a key, and
compares against the key bytes the C library recorded through its own navigation. **338 keys, all
byte-identical**, and every computed key length equals the length in the tag header.

Sub-step 6 was four functions and it passed on the first run: `STR`, `STRZERO`, `DTOC`, `TTOC`.
`STR`'s midpoints came out right because the rounding goes through a **decimal** — the reference
rounds half away from zero *on the decimal digits*, so 0.135 at two decimals is 0.14 even though the
nearest double to 0.135 is below it, and rounding the binary value gives 0.13. Overflow fills the
whole field with asterisks. `STRZERO` keeps a negative sign where the digits start rather than in
front of the zeros. `DTOC` of a blank date is the picture with blanks in it, not eight blanks, and
`TTOC` of a blank timestamp is eight blanks and six zeros.

Sub-step 7 was smaller than it looked, because **every §9.3 transform already existed** — they were
ported for bare-field tags at step 008 and are gated against stored keys already. What was new:
`ExpressionKey` choosing among them from a result *type* rather than a field's declared type,
`expr4keyLen` computed from the type, the leading present-or-absent byte, the `hasTrim` rewrite, and
`DESCEND`/`ASCEND`'s kernels, which are just the key form and the key form complemented.

**The two-axis design paid off exactly as designed, and `PLAN.md`'s two axis-B mutations prove it:**

| Mutation | Predicted | Observed |
|---|---|---|
| Skip the `hasTrim` NUL-to-blank rewrite | axis B only, on `TRIM(K_TEXT)`'s keys **and only those** | axis B reddened, axis A fully green, and the only tag that reddened was **`T_TRIM 'TRIM(K_TEXT)'`** |
| Omit the present-or-absent byte | axis B reddens on key *length* | axis B reddened, axis A fully green |

**And the gate runs twice, which is the whole point of the split.** Once end to end through this
port's evaluator, and once driven from **the values the reference recorded**, with our evaluator out
of the circuit — so a wrong kernel cannot be cancelled by a wrong transform. Because the value gate
had already passed on its own, agreement here cannot be two errors meeting in the middle. The
value-driven run skips the nullable tag, and for a stated reason: a recorded value is raw result
bytes and carries no marking for absence, so its three absent records would come back present. Those
are covered end to end, where the marking survives and their keys are all zero bytes.

**One thing the header settles independently.** Bit `0x02` of a tag's type code says the key carries
the leading byte, so the file states directly what this port works out from the expression's fields.
That is asserted for all 22 tags and it is what makes `T_NULL`'s key length of 9 a check rather than
a coincidence.

## What sub-step 8 produced

**Pure wiring, and it changed four things.** `IKeyValueSource` now hands over an
`ExpressionValue` rather than stored bytes, because an expression result has no field to look its
interpretation up from; one `ExpressionValueSource` serves every tag, a bare field included, since a
bare field is an expression that compiles to a single loader node. `SeekConverter` takes its kind
from the **compiled expression's result type** and **keeps the compiled expression** rather than
discarding it once the kind, the length and the pad byte are read off it — which is §6 point 4, and
what makes step 011's tag matching possible. `KeyTypeResolver` does the same: the pad byte follows
from the result type, so it compiles rather than looking for a field of that name, and
`SeekConverter` reads it off the compile it already did — one compile, one resolution, one refusal.
And the `KeyLength / 2` in the collated whole-key search became the expression's own result length;
the two agreed only because a collation that adds a byte per character doubles a bare field's width.

**The refusal moved, and got stricter.** The cursor's constructor used to force the pad byte so that
an untypable tag failed at selection rather than part-way through a walk; it now forces the whole
converter, which subsumes that and also catches an expression the engine cannot read. Removing the
pad byte's own copy of ADR-28's refusal had briefly moved the failure to the first key build, which a
pre-existing test caught by name.

**Decision 5 is implemented**: a tag whose expression uses `DATE`, `TIME`, `RECNO` or `RECCOUNT` is
refused when the tag is selected, and the message names the function. A filter over the same
functions evaluates normally, which is the point — it is only a stored key that cannot survive them.

**ADR-42 needed one public type.** `ExpressionOptions` is public and settable on `CodeBaseEngine`,
because a caller who wants the reference's answer for a two-digit year has no other way to say which
century, and without it a tag over `CTOD` is refused rather than keyed differently on a different
machine. `DESIGN.md`'s "public surface: almost none" said *almost*; this is the exception and it is
the one ADR-42 requires.

**Four pre-existing tests changed, and none of them was a golden expectation.** All four asserted
ADR-28's refusal, which this sub-step removes: a logical, memo or general field tag now has a pad
byte, an expression that is not a field name now compiles, and `UPPER(TEXT)` over a table that has a
`TEXT` field is now selectable. Each was rewritten to assert the new behaviour, and a new one keeps
the *shape* of the old guarantee with an expression the engine genuinely cannot read (`-TEXT`):
refused at selection, table still usable, record order still working.

**Two mutations on the wiring's own decisions.** Keying a stored date as numeric text reddened axis B
only; padding text with NUL instead of blanks reddened twelve golden tests and six unit tests, which
is the leaf decoder's trailing-pad counts noticing.

## What sub-step 5b produced

**The thirteen functions Visual FoxPro has and the reference does not** — `ABS INT ROUND MOD MIN MAX
SQRT SIGN LEN AT ASC LOWER REPLICATE` — are supported (ADR-43) and gated by **Visual FoxPro's own
answers** (ADR-44), which is the only oracle there can be: the reference refuses every one of them,
so the corpus generator *is* the thing that cannot answer.

**They are a second band, not additions to the table.** The transcribed 193 stay exactly 193 with the
terminator last, so the transcription gate is untouched and every row index the reference names keeps
its value; the fifteen new rows (`MIN` and `MAX` have two each, for numbers and for strings) are
indexed on from 193 and their codes start at 1000, well past any the reference uses, so overload
resolution's walk through a run of equal codes cannot wander from one band into the other. Name
lookup searches the reference's band first and the new one only after, so nothing shadows a reference
row — and `TOOLONGFN` is still refused, because a lookup requires the recorded name length to equal
the name's and no reference row records nine.

**Row coverage now reports the three counts `PLAN.md` asked for**: **144** rows the reference's own
dump gates, **49** nothing can reach, and **15** the reference has no equivalent of at all. Plus a
check that every one of the fifteen is actually exercised, so the third count is a gate and not a
label.

### Five rules that are not what a reader would guess

All five witnessed in the oracle's **stored bits**, not in its formatting, and all five now in
[`specs/EXPRESSIONS.md`](../../specs/EXPRESSIONS.md) §5.2:

- **`MOD` takes its sign from the divisor**, not the dividend — floored, not truncated. `MOD(5,-3)`
  is −1 where the remainder operator of most languages gives 2. A mutation to the C-like reading
  reddens both layers.
- **`INT` truncates towards zero on both signs**, and `INT(-0.5)` is stored as a **positive** zero.
  That is not cosmetic: the bits differ from a negative zero's, no formatting distinguishes them, and
  a value that becomes a key has to carry the bits the engine that wrote the index had. `Math.Truncate`
  gives −0.0, so it is normalised, with the witness cited.
- **`ROUND` rounds half away from zero on the decimal digits** — the same rule `STR` follows, which is
  why both go through a `decimal` rather than agreeing by accident. `ROUND(0.135,2)` is 0.14 although
  the nearest double to 0.135 is below it. Negative places round to tens.
- **`AT` counts from one and answers zero for absent**, with an optional third operand for which
  occurrence. The parser **supplies a literal one** where a caller leaves it out, rather than folding
  the operand away, so `AT(a,b,n)` works with any `n`.
- **`MIN` and `MAX` are variadic and have a string form**, chosen by overload resolution from what
  the operands turned out to be.

**`REPLICATE`'s count is folded at parse time**, as `SPACE`'s is in the reference, because the result
is that many times the operand's width and a key cannot be sized without it. Visual FoxPro's own
header agrees: a `C(20)` repeated twice keys to 40.

### The gate, and the one case excluded

**36 recorded cases** compared on stored bits, and **16 tag key lengths** against Visual FoxPro's own
headers — which is ADR-43's named risk answered: a length computed from the operand is the length
Visual FoxPro wrote. Seventeen tags exist rather than eighteen, because `STRTRAN`'s failed at build
time on the first row whose result did not fit the width VFP had already written a header for; of the
seventeen, `PROPER` is the one this port does not have, and it is skipped by name.

**One value case is excluded by name**: `LOWER("AB1c" + CHR(201))`, on a code-page-1251 table where
0xC9 lowers to 0xE9. `LOWER` has exactly the gap `UPPER` has — Visual FoxPro maps by the table's code
page and only the ASCII path is implemented — so this is **one gap, not two**, and closing it needs a
transcribed case table per code page rather than a culture-aware routine (`CLAUDE.md` forbids the
latter for keys).

**Two unwitnessed answers, recorded rather than guessed quietly**: `MOD` by zero answers zero, as
division does, where Visual FoxPro raises; and `SQRT` of a negative answers zero, where it also
raises. The oracle deliberately wrapped `SQRT` in `ABS` to avoid the second, which is why there is no
witness for it.

## The optimizer contract, closed properly

`QUERY-OPTIMIZER.md` §3 is the one part of the gate with **no oracle**: the reference exposes none of
it through a dump, because it is the shape of a data structure rather than an answer. So the corpus
supplies the **cases** and not the expectations — every expression the reference compiled, used as a
population to check properties over. Ten tests, and what keeps them from being restatements of the
implementation is that two of the properties can genuinely disagree with each other.

**1. The node table.** Every node of every expression carries all six fields the optimizer reads, and
one of them is a real constraint rather than a presence check: a field reference sits on a node
**exactly when** its row is one of the sixteen field-loader rows. A loader without a field would make
a tag unmatchable; a non-loader with one would make it match wrongly. Beside it, each node's subtree
size is checked to span exactly its operands, which is what makes a subexpression addressable by the
index of its root and what every other clause rests on.

**2. Evaluate at a node, without a record.** Evaluating at the root equals evaluating the whole
expression, over every expression in the dump. And **every node the predicate calls constant is
evaluated with no record at all** — over a hundred of them — which is the tie that makes the
predicate mean something.

**3. The predicate, checked against behaviour rather than against its own rule.** Two directions.
Nothing whose subtree reads a field is ever called constant, over every node of every expression:
that is the direction that matters for correctness, because calling such a subtree constant would
have the optimizer evaluate it with no record and get an answer belonging to no row. And the
predicate is **narrower than the truth, deliberately** — `1+1` is not constant because no row says
so, and neither is the narrowed float literal, which is why a comparison against a binary float field
is predicted never optimizable. The test says that in those words, and a mutation that "fixes" the
narrowness reddens it.

**4. A `Tag` keeps its compiled expression.** This needed code, not just a test: `Tag` gained an
internal `Compiled`, `Table` a `CompiledFor(tag)`, and `CursorFor` now hands the tag what typing it
produced. Asserted three ways — null before a tag is used (ADR-28's shape), non-null after, and the
**same instance** the key path builds keys from, so the optimizer compares against what the index is
actually keyed by rather than a second reading of the header text.

**Three mutations, each reddening its own clause.** Calling a character field loader constant reddens
the reads-a-field direction; reporting the narrowed float literal as constant reddens the
deliberate-narrowness test; making the tag's `Remember` a no-op reddens the retention tests and
nothing at layer 1, which is right — retention is a wiring property.

## The second oracle, for sub-step 5b

ADR-43 accepts thirteen functions CodeBase refuses, and nothing in `net/corpus/` can gate them — the
generator *is* CodeBase. **ADR-44** settles that: Visual FoxPro 9 is a second offline oracle, under
ADR-01's discipline (generate offline, commit the output, never let the build run it), and its
scripts live in [`test-files-generator/vfp/`](../../../test-files-generator/vfp/README.md) with the
output published into **`net/corpus/vfp/`** — a subdirectory, because `Corpus.TableNames` is not
recursive and these tables have no C-written dump beside them.

`vfpfunc.prg` **was run on 2026-08-29** and its output is committed: `VFPFUNC.DBF` + `.CDX` with
18 tag groups (the thirteen `[vfp-only]` names in this dump's order, plus `PROPER` and `STRTRAN` for
the per-function *length* rule, `TRIM` and `STR` as divergence probes, and `UPPER` as a control) and
`VFPFVAL.DBF`, 77 constant expressions whose results are stored in fields of their own type.

**The `.DBF`/`.cdx` are the oracle, not the text dumps beside them.** Forced rather than chosen: the
C library cannot dump a VFP-built CDX, because opening a tag parses its expression
(`i4ntag.c:1390-1395`), so a tag over `LOWER(…)` never opens. It proved to matter — the key lengths
below are read out of the tag headers, and VFP does not expose them through any function.

**What it answered, and what it changed:**

- **`T_STRTRN`.** VFP sizes a value-dependent result at the **operand's** width (20), writes the tag
  header, and then fails at *build* time on the first row whose result does not fit. `DESIGN.md`'s
  key-length rules can therefore keep computing from the operand. The refused tag's three blocks stay
  in the file, so **the tag directory is the only authority on which tags exist**.
- **`T_VTRIM` does not diverge**: `TRIM(k_text)` keys to 20, the source width, as CodeBase does. The
  reading hazard ADR-43 could not settle is not there.
- **`STR` agrees** on all four midpoint probes, closing ADR-43's open question.
- **`STRZERO` is not a VFP function at all**, which is why two `VFPFVAL` rows are refusals.
- **Two divergences are now decided, as ADR-45**: prefix `=` (and it runs the **opposite** way from
  what `e4parse.c:59-62` says, which `DESIGN.md` Decision 8 had repeated — corrected in place), and
  the century pivot, where VFP has a rollover and CodeBase has none.

Sub-steps 2-5 never waited on it, and still do not.

## Things owed to the corpus

- **A second rejection pass with a real `TAG4FILE`.** Everything is parsed with `tagPtr = 0`, so
  `e4tagExpr` (−510) never appears and `OTHER->K_TEXT` returns `e4fieldName` (−210). The tag-only
  refusals are unobservable until this exists — including whether `DESIGN.md` §3's
  "a TRIM result may not feed `SUBSTR`" restriction bites at all. It **does not** without a tag.
- **`CachedReadGoldenTests.KeysABareField` is deleted at sub-step 8**, with ADR-28's refusal. It is
  the one place the suite currently averts its eyes, and it exists only because `CDXEXPR` had to land
  before the engine that reads its tags.
- ~~**A run of three or more `.AND.`/`.OR.` operators**~~ — **closed** by the second generator run at
  sub-step 4, together with 45 other rows the resolution gate had nothing to compare against.
- **The thirteen Visual FoxPro names are refused as unknown functions until sub-step 5b**, which is
  where their rows enter the table. The scanner reads them as calls, which is what ADR-43 needs; the
  parser cannot yet find a row for them, and `PLAN.md`'s gate assertion 3 deliberately makes no claim
  about them either way.

## What `QUERY-OPTIMIZER.md` changed in this step (2026-09-03)

[`specs/QUERY-OPTIMIZER.md`](../../specs/QUERY-OPTIMIZER.md) was written before sub-step 2 and read
back against this step's design. It **corrected one thing and added three**, all in `DESIGN.md` §6
(new) and §2b, mirrored in `PLAN.md`'s sub-steps 3, 4, 5 and 8.

**The correction, which stands on its own.** `DESIGN.md` §2b said a promotion would be "wrapping a
child in a conversion node". It is not: all six are an in-place rewrite of the producer's row and
length (`e4parse.c:227-289`), so **node count is invariant under promotion**. A wrapper node would
have failed `PLAN.md` sub-step 4's own gate, which is a *sequence* equality against the C's `E4INFO`
array — so this was a contradiction inside the step, independent of `QUERY`. A mutation now proves
it: wrap one promotion, and the resolution gate must redden on sequence length.

**Why the optimizer cares about the same fact.** `e4isTag` (`r4log.c:61-255`) matches a subexpression
to a tag by comparing node counts first, then rows, tolerating a differing row **only when both are
field loaders** (`r4log.c:234-236`) — and the six promotions target exactly the field-loader rows
(6, 8, 18, 19, 25) plus `E4FLOAT` for the constant case. Row-rewrite promotion and that exemption are
a matched pair. With wrapper nodes, `K_NUM > 0` would never match a tag on `K_NUM`, and every
promoted comparison would silently fall back to scanning.

**The three additions**, all cheap now and expensive after sub-step 4 freezes the node shape:

| # | Addition | Sub-step |
|---|---|---|
| 1 | `CompiledExpression` **retains** the node table — row, result type, declared length, parameter count, subtree size, field. The first three the resolution gate already needs; the last three are free to keep and cannot be rebuilt later | 3-4 |
| 2 | The evaluator answers **at a node index**, and needs **no record** when that subtree is constant; plus a per-node **is-constant** predicate mirroring `e4isConstant` | 5 |
| 3 | A `Tag` **keeps** its `CompiledExpression` rather than discarding it once `SeekConverter` has taken kind, length and pad | 8 |

One detail worth not rediscovering: the length the optimizer reads is the node's **declared** length,
not the value's runtime width. `"SM"` is declared 2 against a `keyLen` of 20, and that is what makes
`NAME = "SM"` a prefix range rather than a wrong equality.

**Structural equality itself is step 011's**, as are the `BITMAP4` tree, the merge algebra, the four
seek positions and the `F4FLAG` bitset. Nothing in them constrains this step.

## The shape of the gate, in one paragraph

Two axes, because they fail differently and the reference splits there too — `expr4vary` returns a
value, `expr4keyConvert` turns it into key bytes. **Axis A** (what a function computes) is
C#-authored at layer 1 and **tested over constants**, with the C dump as oracle only for the
unknowns, for row selection and for filters. **Axis B** (what a value becomes on disk) is stored key
bytes only, and is **driven from the C library's recorded value** rather than from our evaluator, so
a wrong kernel and a wrong transform cannot cancel. The mutations that prove the separation are in
`PLAN.md`: breaking prefix `=` must redden axis A only; skipping the `hasTrim` rewrite, axis B only.

## Decisions taken here

**ADR-42** — the engine takes an explicit date policy, defaulting to invariant.
**ADR-43** — Visual FoxPro's functions are supported, though CodeBase has none of them.
**ADR-44** — Visual FoxPro 9 is the second offline oracle, in its own frozen corner (supersedes
ADR-04, unblocks ADR-11).
**ADR-45** — prefix `=` and division by zero replicate CodeBase; the oracle also corrects what the
source says VFP does.
**ADR-47** — the scanner is table-driven, so the function table is transcribed before it.
**ADR-46** *(open)* — the optimizer's spec is written, and it leaves two decisions the port must
make; it also records that `EXPR` constrains `QUERY` at sub-steps 3-4, which is why `DESIGN.md` §6
exists.

Decisions 1, 3, 5 and 8 of `DESIGN.md` were written at close as **ADR-48**, **ADR-49**, **ADR-50**
and **ADR-51**, following step 009's pattern.
