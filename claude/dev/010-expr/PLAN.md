# 010-expr — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after
[`DESIGN.md`](DESIGN.md), before code.

**This is the largest step in the project so far** — a lexer, a parser, a type resolver, an evaluator,
a 193-row table, the §9 key rules, two new corpus artefacts and a change to the seam every existing
tag runs through. It is planned to be **stoppable after sub-step 5**, where the engine is gated on
both axes and nothing is wired into the tag path yet.

## The two axes, and which layer owns each

[`DESIGN.md`](DESIGN.md) §Gate splits the surface where the reference library splits it — `expr4vary`
returns a value, `expr4keyConvert` turns that value into key bytes. Every choice below follows from
that, so it is stated once:

| | **Axis A — what a function computes** | **Axis B — what a value becomes on disk** |
|---|---|---|
| Owned by | **layer 1**, C#-authored expectations read off `EXPRESSIONS.md` | **layer 4**, stored key bytes |
| Oracle | the spec — plus the C dump for the unknowns, for row selection, and for filters | `CDXEXPR.cdx` and the tag headers, and nothing else |
| Driven from | **constant expressions** — the expression *is* the case; a `RecordBuffer` only for what a literal cannot say | **the C library's recorded value**, not our evaluator |
| Reddens when | a kernel, a promotion or an overload row is wrong | a transform, the null indicator, `hasTrim` or a key length is wrong |

**Axis A is tested over constants.** `UPPER("jöhn ")`, `SUBSTR("ABCDEF", 2, 3)`, `STR(2.5, 10, 0)`,
`"AAAA" = "AAA"`, `1/0` — no record dimension, the edge case in the literal, and a failure that cannot
be a field-decode bug in disguise. Fields are required only where no literal reaches the type
(`DESIGN.md` §Gate axis A): `r4num`, `r4date`, `r4int`, `r4currency`, memo, and null in any type — the
field-loader-only types, which are the halves of the two pairs §1 warns a careless design collapses. Row coverage is unharmed:
there is no whole-expression constant folding, so the real overload row still resolves
(`e4parse.c:274-276`).

**Neither may be able to mask the other.** Axis B takes its input from the value dump through the
`IKeyValueSource` seam, so a wrong `UPPER` cannot redden it, and a wrong transform cannot be cancelled
by a compensating kernel. Composed end to end at sub-step 8 they still make the single assertion the
step needs; kept apart until then, a red test names *which half* is wrong.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| **1 Unit** | **The semantic table — axis A's primary artefact:** `constant expression → expected value` over every kernel of §7, written against the spec, covering branches no corpus record reaches; **no `RecordBuffer` at all** for the kernels, and one only for the typed pairs, currency, datetime, memo and null. Beside it: lexer token streams for the trap list of `DESIGN.md` §2b; parse *shape* (precedence, associativity, `.AND.` flattening, `.NOT.` grouping); `TypeResolver`'s promotion-then-advance loop per §6; `expr4keyLen` arithmetic per §9.1 | Every branch of the grammar, of the promotion loop and of each kernel, exhaustively and at memory speed |
| **2 Component** | Compile-and-evaluate over an in-memory `RecordBuffer`; a tag over an expression driven through `TableTagCursor`, corpus bytes loaded into `InMemorySource`; lazy compile, so one bad tag does not break its siblings | Sequencing: that a compiled expression is reused, that a refusal fires where the design says (tag selection, not open), that null propagation survives a whole expression rather than one kernel |
| **3 Fault injection** | Malformed expressions through the public path; a tag whose computed key length disagrees with its header; a key over 240 bytes; a memo operand over its cap; a field name of 11 characters; depth past `E4MAX_STACK_ENTRIES` (65). **Cheaply:** a tag's expression is stored as *text* in the tag header, so patch a copy of `CDXEXPR.cdx` inside `InMemorySource` rather than hand-build a CDX — bytes as *input*, which `CLAUDE.md` allows | What a valid corpus file physically cannot express — every tag in `net/corpus/` parses, by construction |
| **4 Golden / corpus** | **Four gates, in dependency order:** (a) our `FunctionTable` row for row against the C's own `v4functions`; (b) the resolved postfix node array — row, type, length — against `E4INFO`, plus row coverage; (c) recorded values — `[constant expressions]` flat, `[field expressions]` a small cross product — for axis A's oracle set and every filter; (d) every stored key of every `CDXEXPR` tag, rebuilt **from the recorded value**. Plus the existing 1333 tests unchanged | Whether we resolve the same overload row and produce the same bytes as the C library — which layers 1–3 cannot tell us, because they all pass on a self-consistent misreading |

**Corpus coverage.** Gated on a new `CDXEXPR.DBF` + `.cdx`, plus `VFPNULL` gaining a tag over a
nullable field, plus every existing indexed case as regression. Three paths this step touches that
**no** corpus case covers today, each needing a generator case rather than a unit test:

- **A tag over an expression** — the whole point; nothing in the corpus has one.
- **A nullable field's key**, for the `0x80` indicator, its length, and the candidate-key exception.
- **An `H` field — of any kind.** `grep type= net/corpus/*.dump.txt` returns
  `0 B C D F G I L M N T X Y Z` and **no `H`**, yet `FieldValueDecoder.cs:117,148` decodes one and
  `FieldResolver` admits it. So `r4floatBin` is in scope, reachable, and **completely ungated today**
  — a pre-existing hole this step inherits, and one `PORTING-PLAN.md` §6.3 should name, since it
  already requires "every IN-scope field type". Whether the C library will *create* one is sub-step
  1's first question (`DESIGN.md` Decision 2).

**Where expectations come from.** Axis A's layer-1 table is written by us, against
[`specs/EXPRESSIONS.md`](../../specs/EXPRESSIONS.md) — that is what makes it a specification and not a
transcript, and `CLAUDE.md`'s bar on hand-written expectations is about on-disk **bytes**, which a
kernel's result is not. Everything at layer 4 comes from the C library: the function-table dump, the
node dump, the value dump, the rejection dump, or the stored key bytes in `CDXEXPR.cdx`. The one place
tempting a hand-typed number is `STR`'s midpoint rounding — it is answered by a corpus case and never
by a figure read off the spec.

## Steps

| # | Step | Verify by |
|---|---|---|
| **1** ✅ | **Generator: the probe, the case, the four dumps.** *Done 2026-08-28: the `H` probe passed (`d4create` forces 4 bytes), `CDXEXPR` builds with 22 tags, the existing corpus regenerates byte-identical, and the dumps answered five of `DESIGN.md`'s open questions.* *First* a ~20-line probe that `d4create` accepts an `H` field — `DESIGN.md` Decision 2 rests on it and no corpus case has one. Then `CDXEXPR` with the field shapes and the designed record matrix of `DESIGN.md` §Gate, its tags, the `FOR` filter, a nullable tag on `VFPNULL`, and a new `dump-expr.cpp` beside `dump.cpp`/`dump-index.cpp` emitting `[functions]`, `[expressions]` (node array + `type`/`len`/`keyLen`/`hasTrim`), `[constant expressions]`, `[field expressions]` and `[rejections]`. **Three mechanics, so they are not discovered:** parse through `expr4parseLow(d4, text, tagPtr)` (`e4parse.c:1057`) — const-correct and carrying the tag context that makes `e4tagExpr` and `expr4keyLen` answer for a *tag*; `E4PARM_HIGH` refuses `d4 == 0` (`:1065-1070`), so even a constant expression needs an open table; and `expr4parseLow` bails on a sticky `error4code` (`:1073`), so the rejection loop must `error4set(&cb, 0)` between strings or every case after the first records a phantom failure. Regenerate and publish | The `H` probe answers before anything is built on it — if it refuses, Decision 2 reopens as a refusal. The corpus regenerates **byte-identical** except the new files; `[functions]` carries 193 rows; the dumps parse in C# and are non-empty; `net/corpus/README.md` and `test-files-generator/README.md` updated |
| **2** | **Lexer.** Tokens, with the whole trap list of `DESIGN.md` §2b | Layer 1: a token-stream table over ~60 expressions, plus every rejection case from the dump reaching the lexer's own error |
| **3** | **Function table + parser.** The 193 rows as data; precedence read from the table; AST; `.AND.`/`.OR.` flattening | **Layer 4, the transcription gate:** our table equals `[functions]` row for row — name, code, priority, return type, parameter count and types. Layer 1 on parse shape; **layer 4 on the rejection dump**, error code mapped explicitly |
| **4** | **Types and overload resolution** — `e4massage`'s loop, the six promotions, result types and lengths | **Layer 4, the resolution gate:** for every expression, the postfix `(row, type, length)` sequence equals the C's `E4INFO` array — *before a single value is computed*. Plus **row coverage**: every row of our table covered by the dump or explicitly declared unreachable |
| **5b** | **The VFP-only functions** (`DESIGN.md` Decision 11) — `ABS`, `INT`, `ROUND`, `MOD`, `MIN`, `MAX`, `SQRT`, `SIGN`, `LEN`, `AT`, `ASC`, `LOWER`, `REPLICATE`, marked as VFP-sourced rows in the table | Layer 1 only, against VFP's documented behaviour — there is no oracle and the plan says so. Axis B needs nothing new for the numeric ones (`r4numDoub`, an already-gated key route); the two string ones are gated by the computed-versus-stored key length of sub-step 7 |
| **5** | **Evaluator: kernels and null semantics.** Arithmetic, both concat forms, the comparison family incl. prefix `=` and `$`, `IIF`, the date/`EMPTY`/`DELETED`/`RECNO` family, §6 null propagation | **Layer 1 is the gate:** the semantic table over every kernel, spec-authored. **Layer 4 confirms**, and is the *only* oracle for filters and for the unknowns of `DESIGN.md` §Gate axis A. **This is the stop point** — both axes closed over the engine, nothing wired in |
| **6** | **The text-formatting family** — `STR`, `STRZERO`, `DTOC`, `DTOS`, `TTOC`, and the `c4dtoa45` semantics behind them | **Layer 4 leads here**, because this is axis A's unknown set: the value dump's formatting cases, with the midpoint question **answered by the corpus**, and the summary stating the claim as "matches every case the corpus holds" |
| **7** | **Axis B in full** — `expr4keyLen`, the null indicator with its candidate-key exception, `hasTrim`, `ASCEND`/`DESCEND`, the 240 cap | Layer 1 on the arithmetic; **layer 4 driven from recorded values, evaluator out of the circuit:** every stored key of every `CDXEXPR` tag rebuilt from the C's value for that record; computed key length equals `expr4keyLen` for every expression in the dump, and the header's for **every** tag of every corpus index, old and new |
| **8** | **Wire into the tag path** — typed `IKeyValueSource`, `SeekConverter` absorbing `KeyTypeResolver`, the three call sites of `DESIGN.md` §5, the `KeyLength / 2` fix, key-legal refusal at tag selection | **Pure wiring, with its own gate:** the same keys sub-step 7 produced, now end to end through the evaluator; **all 1333 existing tests still green with no golden expectation changed**; ADR-28's refusal is gone |

**Why this order.** The generator comes first because without it sub-steps 3–7 have no oracle and the
step becomes a large self-consistent guess. Then each layer-4 gate is the next one's precondition:
the table (3) must be right before row selection (4) means anything, and the row must be right before
a value (5) is worth comparing — a wrong row produces a *plausible* value, and comparing rows first
localises that to one pass. Axis B lands complete at 7, from recorded values, so sub-step 8 is the
only place that can turn an existing test red and the only thing it can be red about is wiring.

**Expect a second generator run.** Sub-steps 4–7 will find expressions the dump should have recorded.
That is cheap (~2 min build, seconds to run) and planned for, not a slip.

## Gate

`dotnet test` green, with the new golden classes, on a corpus that regenerates byte-identical.
Concretely, the assertions that close the step, in the order they must hold:

1. **Transcription** — our `FunctionTable` equals the C library's `v4functions`, row for row.
2. **Resolution** — for every expression in the dump, the resolved postfix sequence of
   `(row, result type, result length)` equals the C's `E4INFO` array; and every row of the table is
   covered by some expression or explicitly declared unreachable. Coverage reports three counts, not
   one: **oracle-gated** rows, rows **unreachable by construction** (calc, total, the `r5*` family,
   `r4dateTimeMilli`, and the six dead `r4currency` comparison rows of `EXPRESSIONS.md` §7.7), and
   **VFP-only** rows, which have no C row at all and are gated by C#-authored semantics plus the
   key-length check.
3. **Rejections** — this port accepts exactly what CodeBase accepted **in the `[rejections]` section**,
   and its exception maps to the recorded `error4code` through a stated mapping. The `[vfp-only]`
   section is **not** part of this assertion: it records that CodeBase refuses those thirteen names,
   and per `DESIGN.md` Decision 11 this port accepts them. A gate that asserted the refusal would
   contradict the decision.
4. **Values (axis A)** — the layer-1 constant semantic table passes; every line of
   `[constant expressions]` matches on bytes, result type and length; and so does every
   (expression, record) pair of `[field expressions]`.
5. **Keys (axis B)** — every stored key of every `CDXEXPR` tag, built **from the recorded value**,
   equals the bytes on disk; every computed key length equals `expr4keyLen`; and every existing
   corpus tag still keys as it did.
6. **End to end** — the same keys through the evaluator, and the 1333 existing tests unmoved.

`CDXEXPR`'s field and tag shapes, so sub-step 1 has a checklist:

| | for |
|---|---|
| `K_TEXT C(20)`, `K_CITY C(15)` | `UPPER`, `TRIM`, `SUBSTR`/`LEFT`/`RIGHT`/`PADL`/`PADR`, `+` and `-` concat, `$` |
| `K_INT I`, `K_NUM N(12,3)`, `K_DBL B` | `STR`, `STRZERO`, `VAL`, arithmetic, the `r4num`/`r4numDoub` pair |
| `K_FLT H` | `r4floatBin` — the type that has no corpus case at all today, and the probe of sub-step 1 |
| `K_DATE D`, `K_TS T` | `DTOS`, `DTOC`, `TTOC`, `DAY`/`MONTH`/`YEAR`, the `r4date`/`r4dateDoub` pair, date arithmetic |
| `K_CUR Y`, `K_LOG L`, `K_MEMO M` | `r4currency`; a **tag over a logical**, which settles the open question in `DESIGN.md`; `EMPTY` over a memo |
| tags, by function | `UPPER(K_TEXT)`, `STR(K_INT)`, `STR(K_NUM,12,3)`, `DTOS(K_DATE)`, `DTOC(K_DATE)`, `K_TEXT+K_CITY`, `K_TEXT-K_CITY`, `TRIM(K_TEXT)`, `SUBSTR(K_TEXT,1,5)`, `DESCEND(K_TEXT)`, one `FOR` filter using `.AND.`, `$` and a function |
| tags, by key type | one per result type (`DESIGN.md` §Gate): `UPPER(K_TEXT)` `r4str`, `VAL(K_TEXT)` `r4numDoub`, `CTOD(K_TEXT)` `r4dateDoub`, `DATETIME(…)` `r4dateTime`, `EMPTY(K_TEXT)` `r4log`, `K_FLT * 1` `r4floatBin` — plus bare `K_NUM`, `K_DATE`, `K_INT`, `K_CUR`, `K_TS`, because **no function returns those four types** |
| records | **only what a literal cannot say** (`DESIGN.md` §Gate): a null in every nullable column, `N`/`I` at zero, negative and their extremes, currency at four decimals, a datetime, a blank date, memo empty and past 240 bytes, `H` at its extremes, both logicals, and trailing-blank text for `TRIM` and concat, which is key-affecting. A dozen records, not two hundred |
| expressions | **constants first** — every named function and its edge cases as literals, including the `STR` midpoints (2.5, −2.5 at 0 dec; 0.125, 0.135 at 2), empty and all-blank strings, and `1/0`; then the field expressions for the types no literal reaches; the comparison rows generated mechanically by looping the operators over typed operand pairs; every existing corpus tag's expression, for its node array; `DATE`/`TIME`/`DATETIME` **for shape only** |

## Risks

| Risk | Why it is worse than late | Cheapest early exposure |
|---|---|---|
| **The parser accepts a superset.** `-FIELD`, a `{}` literal, an escape in a string — and we write a tag CodeBase and VFP cannot read | Silent, and it surfaces in someone else's application, not our suite | The rejection dump, at sub-step 3 — before any evaluation exists |
| **A wrong overload row.** Types collapsed (`r4num`/`r4numDoub`, `r4date`/`r4dateDoub`) or a promotion missed | Produces a plausible value and a wrong key; every unit test still passes | Sub-step 4's resolution gate compares the row *index* the C chose, not an inference from the value |
| **A compensating pair** — a wrong kernel and a wrong transform that cancel on the tags the corpus happens to hold | A single stacked key gate passes, and both bugs ship | Axis B is driven from the *recorded* value, so the two can never cancel; the mutations below prove it |
| **Constants are assumed to cover everything.** `r4num`, `r4date`, `r4int`, `r4currency`, memo and null have no literal form — and no *function* returns the first four either | The kernels would look exhaustively gated while the typed pairs — the actual risk — were never evaluated at all | The reachability table in `DESIGN.md` §Gate axis A drives sub-step 1's field list; row coverage at sub-step 4 names any row no expression reached |
| **`STR` midpoint rounding** is unknowable from this source drop (§14 risk 1) | `STR(n)` is one of the commonest index keys there is | Sub-step 1 puts midpoints in the corpus; sub-step 6 reads the answer off it |
| **`DTOC`'s key length depends on a `dateFormat` default outside the drop** (§14 risk 2) | A wrong picture length makes every key of such a tag the wrong size | A `DTOC` tag in `CDXEXPR`; the header's key length is the answer |
| **`d4create` may refuse an `H` field**, on which `DESIGN.md` Decision 2 rests | The decision would be discovered wrong at sub-step 5, with the case built around it | The ~20-line probe that opens sub-step 1 |
| **A non-deterministic expression in the value dump** makes the corpus stop regenerating byte-identical | Breaks the project's oldest standing invariant, and looks like a generator bug | `DATE`/`TIME`/`DATETIME` recorded for shape only, decided at sub-step 1 |
| **Sub-step 8 breaks existing tags.** Three live call sites plus `SeekConverter`'s rewrite sit under every seek the library performs | Would regress a closed, gated capability | 1333 tests already cover it; run them at every commit of sub-step 8, and mutate `SeekConverter` to prove they bite |
| **The step is too big to finish in one pass** | A half-wired seam is worse than none | The stop point after sub-step 5 is real: both axes gated, `SeekConverter` untouched, nothing regressed |

## Mutations to prove the gate

Per `DEV_APPROACH.md` §4, and restored from a checksummed copy — **never** from `git`. The last two
are the ones that prove the axes are actually separate; if either reddens both gates, the decoupling
of sub-step 7 has not been built as designed.

- Perturb one row of the transcribed table — a priority, a parameter type → **the transcription gate**
  must redden, at sub-step 3.
- Swap two adjacent overload rows → **the resolution gate** must redden; the value gate need not.
- Drop one promotion from the resolver's loop → the resolution gate must redden at sub-step 4, before
  any value exists.
- Accept `-FIELD` in the lexer → the rejection gate must redden.
- Make prefix `=` compare full length instead of the common prefix → **axis A only**; axis B stays
  green.
- Skip the `hasTrim` NUL-to-blank rewrite → **axis B only**, on `TRIM(K_TEXT)`'s keys and only those;
  axis A stays green.
- Omit the null indicator byte → axis B reddens on key *length*, not content.
