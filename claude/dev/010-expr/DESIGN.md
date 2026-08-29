# 010-expr — design

Phases 1 and 2 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

*Rewritten 2026-08-27.* The first draft scoped this step by listing function names, which put the
argument on the wrong axis and left the type system unstated. It also predated the 2026-08-19
renumbering and still prescribed work that ADR-34 had already shipped. Superseded whole; `git log`
has it.

## Goal

**A tag stops having to be a bare field name.** `UPPER(NAME)`, `STR(ID)`, `DTOS(HIRED)`,
`NAME+CITY` — the expressions real applications index by — become tags this library can type, key,
navigate and seek, exactly as a bare field is today. And a filter expression can be evaluated over a
record, which is the vocabulary `QUERY` decomposes.

**How we will know it works: on two axes, because the reference library itself splits there.**
`expr4vary` returns an evaluated value; `expr4keyConvert` turns that value into key bytes
(`e4expr.h:332, 363`). **What a function computes** is ordinary semantics, asserted in C# against the
spec and gated by the C library only where the source yields no answer. **What a value becomes on
disk** is byte-exactness, gated against stored keys and nothing else. The corpus dump records the
boundary between them, so each axis has its own oracle and neither can mask the other — see
[Gate](#gate).

**Capability:** `EXPR` (`PORTING-PLAN.md` §5) · **Governing spec:** [`specs/EXPRESSIONS.md`](../../specs/EXPRESSIONS.md),
with `KEY-COLLATION.md` §3 for the key half and `DBF-FORMAT.md` §5 for the field types

## Not in this step

- **Writing** an index or a record from an expression — `WRITE`'s job. This step evaluates and keys;
  it never stores.
- **`QUERY` itself.** This step gives it the evaluator and the filter vocabulary; decomposition into
  a bitmap is step 011+ and needs `claude/specs/QUERY-OPTIMIZER.md`, which does not exist yet.
- **Named calculations (`EXPR4CALC`) and report totals** — `E4CALC_FUNCTION`, `E4TOTAL` and
  `PAGENO` are the report writer's, which is out of scope project-wide (`PORTING-PLAN.md` §2.2).
- **User-defined functions.** No hook exists and none is wanted.

## 1. The type system, which is derived and not chosen

This is the spine of the step. Everything else falls out of it, so it is settled first.

**An expression's type set is a closure, not a preference.** A value can enter an expression through
exactly three doors — a field the port opens, a literal the lexer builds, or a function's return over
those — so the set is fixed by what `DBF-READ` already accepts (`Dbf/FieldResolver.cs:33`), not by
anything this step decides.

| field type | expression type(s) it produces | rows |
|---|---|---|
| `C`, and binary `C` reported as `Z` | `r4str` | 0, 3 |
| `M` / binary `X`, `G` | `r4str` — memo loaded, truncated or zero-padded to `len` | 26 |
| `N`, `F` | **`r4num`** (text of field width) *and* **`r4numDoub`** (double) | 9, 8 |
| `D` | **`r4date`** (8-char `CCYYMMDD`) *and* **`r4dateDoub`** (julian double) | 7, 6 |
| `L` | `r4log` | 5 |
| `I` | `r4int` *and* `r4numDoub` | 12, 18 |
| `B` | `r4numDoub` | 11 |
| `Y` | `r4currency` *and* `r4numDoub` | 10, 19 |
| `T` | `r4dateTime` | 16 |
| `H` | `r4floatBin` | 24 |
| `0` `_NullFlags` | none — a hidden system field, never nameable in an expression | — |

Literals add nothing new (`r4numDoub`, `r4str`, `r4floatBin`; §3), and no function returns outside
the set. **Ten types**, and `ExpressionValue` has exactly those cases.

**Nine of the ten are required by the compatibility target.** Every VFP field type that can produce a
value — `C M G N F D L I B Y T`, plus the binary-marked `Z`/`X` — is already read. The tenth,
`r4floatBin`, arrives only from `H`, which is a **CodeBase extension VFP never writes**
(`DBF-FORMAT.md` §5, `D4CREATE.C:1620-1622`). It is kept rather than refused because the port already
reads an `H` field and hands its value to `GetDouble`; refusing an *expression* over a field the
library will happily decode is a worse contract than the handful of overload rows it costs, and
unlike the types below it is fully gateable, because the C library writes it.

**Two of the pairs above are the ones a careless design collapses**, and collapsing them is not a
scope reduction but a wrong key: §9.3 gives `r4num` and `r4numDoub` different key routes
(`t4dblToFox(c4atod(text, len))` against `t4dblToFox(value)`), and `DTOS`, `DAY`, `MONTH`, `YEAR` and
`TTOC` each carry separate rows for `r4date` and `r4dateDoub`. They are two states of one ordinary
field, selected by what the parser needs — not exotic types.

### What is outside the closure, and why it is unreachable rather than excluded

`r5wstr`, `r5wstrLen`, `r5ui4`, `r5i2`, `r5ui2`, `r5i8`, `r5dbDate`, `r5dbTime`, `r5dbTimeStamp`,
`r4dateTimeMilli` each require a field type of `W O P Q R 1 2 3 4 7`, and **not one is in
`ReadableTypes`** — the opener refuses them, one layer below this engine. So no expression can
produce one, no corpus case could gate one, and code for one would be dead on arrival. `EXPR`
inherits the boundary rather than restating it.

**If `DBF-READ` ever widens, this step needs no rework**: the closure recomputes and the engine grows
by construction. That is the whole reason to derive the set rather than list it.

**One boundary worth stating, because it is version-dependent.** VFP 9's `V` (varchar), `Q`
(varbinary) and `W` (blob) collide by letter with CodeBase's GUID, `r5i2` and `r5wstr`, and `P`
collides with FoxPro's Picture. The port refuses all four, which is the safe reading of both
meanings. They cannot be added by wanting them: **the reference library does not implement them
either** — `D4OPEN.C:2479-2492` and `:2536-2549` merely relax the length check for a `0x32` file and
go on reading the field as `r5guid` / `r5i2`, with the source comment admitting *"appears to be
implemented as a static character field of some type"*. There is therefore no reference behaviour to
port and no oracle to gate against; the corpus method cannot reach them, and closing that gap needs
the live VFP that ADR-04 defers. `PORTING-PLAN.md` §2.3 already parks them.
**[Updated 2026-08-29 — ADR-44 supersedes ADR-04: a VFP 9 VM is available and a `V`/`Q`/`W` table is
item 4 of its run order. The gap is now reachable; it is not yet closed, and §2.3 still parks them.]**

## 2. Overload resolution — where the 193 rows come from

`v4functions[193]` is 193 **rows**, not 193 functions: 27 internal field loaders, 3 constant loaders,
8 logical constants and operators, 13 `+`/`-` overloads, 47 comparison overloads, 7 arithmetic and
`$`, 87 named rows and a terminator (§5). Collapse the named rows by name and there are **36**, two of
which are internal (calc, total). **Nothing is being trimmed** — the whole table is in scope. It is
large because it is `(operation × type)`, which is why §1 comes first.

The mechanism is `e4massage` walking the postfix array with a simulated typed stack
(`e4parse.c:156-193`), and it is ported as written:

1. Parameter-count check against the row (variadic `−1` allowed).
2. Where a parameter's type does not match, try the **six implicit promotions**, rewriting the
   *producer* node (`e4parse.c:227-285`): `r4date → r4dateDoub`, `r4num → r4numDoub`,
   `r4int → r4numDoub`, `r4currency → r4numDoub`, `r4numDoub` constant `→ r4floatBin`,
   `r4int → r4floatBin`.
3. Otherwise advance to the next row (`functionI++`) and retry; if the row's `code` changes first,
   it is a type error `e4typeSub` (`e4parse.c:207-214, 286-292`).

**A missing overload row does not fail loudly — it selects the neighbouring row.** That is the reason
the table is ported entire rather than by demand, and the reason the gate below has to see values and
not only keys.

Result type is the matched row's; result *lengths* follow §2.4 plus the function-specific rules
(concat sum, `IIF` max with the shorter branch blank-padded, `DTOC` picture length —
`e4parse.c:395-833`).

**`ASCEND`/`DESCEND` are 35 of the 87 named rows and are nearly free**: pure type-dispatch onto the
FOX key transforms step 008 already ported (§9.3, §9.5, `Cdx/KeyTransform.cs`). They are worth naming
because they look like a fifth of the work and are not.

**Null propagation is part of the type system, not an afterthought** (§6): every kernel checks
`isNull`; arithmetic is null-in/null-out with value 0; a comparison whose operands disagree on
nullness is false (true for `<>`) and marks its node null.

## 2b. The front end — three passes, and precedence read from the table

§13 recommendation 1: replace the C's shunting-yard with a conventional lexer, precedence parser and
typed AST, **keeping the published grammar identical**. That is safe because the grammar is tiny —
`expression := value { binary-op value }`, seven precedence levels, left-associative, one prefix
operator, no unary minus — so precedence climbing produces the same grouping as popping while
`priority >= new.priority` (§4). The tree is the same; only the container differs.

```
text ──Lexer──▶ tokens ──Parser──▶ untyped AST ──TypeResolver──▶ CompiledExpression ──Evaluator──▶ ExpressionValue
```

**What must match is not the parser but its output shape**: which overload row each node resolves to,
and each node's result length. The C's `resultPos` slot packing and its "reserve 8 bytes so an
in-place double conversion is possible" (`e4parse.c:922-931`) are artefacts of a stack machine
returning pointers, and carry nothing observable for a value-returning evaluator.

**The resolver is a separate pass on purpose.** The parser cannot know types — a bare name is a field
whose type comes from the *table* — and resolution **rewrites producer nodes** to insert §6's
promotions, which on an AST is wrapping a child in a conversion node. Separating it means the pass
that has to be checked row-for-row against `e4massage` is drivable at layer 1 with no file near it.

**Precedence is read from the ported function table, not hard-coded in the parser** — as the C does,
comparing `v4functions[i].priority` on its operator stack. One source of truth, so a transcription
slip in the table shows up as a parse difference instead of hiding behind a parser that happens to
agree with it.

### The lexer is where the bugs will be

Every one of these is specified, and every one is what a *sensible* lexer gets wrong by being
sensible:

- **A `.` followed by `AND`/`OR`/`NOT.` terminates a number** (`e4parse.c:2218-2221`) — `1.AND.X` is
  three tokens, not a malformed float.
- **A digit run followed by a letter is re-parsed as a data-alias name** (`e4parse.c:2222-2242`).
- **A leading `+`/`-` is part of a numeric literal, and there is no unary minus otherwise**
  (`e4parse.c:2558-2565`). `-1` is a literal; `-FIELD` is a parse error.
- **No `{}` date literals** — `expr4parseValue` has no `{` case; dates come from `CTOD`, `STOD`,
  `DATE`, `DATETIME`.
- **Three string delimiters, no escapes**, scan to the matching close; unterminated is
  `e4unterminated`.
- **Whitespace is any char ≤ `' '`**; function names 8 chars, case-insensitive; field names 10 unless
  the DBF has long names.
- **`ALIAS->FIELD`, and — S4FOX only — `ALIAS.FIELD`**, which collides head-on with the dotted-operator
  rule. In a *tag* expression an alias other than the indexed table is `e4tagExpr`.

The parser itself is quieter: `(` and `,` are stack barriers in the C, which here is just a
parenthesised expression and arguments parsed at precedence 0; named functions sit at priority 0
because they are call syntax; consecutive identical `.AND.`/`.OR.` flatten into one variadic node at
parse time (`E4ANOTHER_PARM`). One grouping worth a test of its own: `.NOT.` is priority 5 and
comparison is 6, so `.NOT. A = B` groups as `.NOT. (A = B)`.

### The rule that keeps the front end honest: never accept a superset

§13 recommendation 5 — anything this port accepts, CodeBase and VFP must accept. **Decision 11
narrows this to what the port *writes*, and to VFP as the authority**: the rule's whole force is that
a tag we write must be readable, and a function VFP has is readable by VFP. Accepting `ABS` is
therefore not a superset in the sense the rule means; accepting `-FIELD`, which neither engine
parses, still is. It is stronger than
it sounds: if we accept `-FIELD` and someone indexes on it, we write a tag neither can read, and the
failure surfaces in somebody else's application. **Refusing correctly is therefore a gated behaviour,
not a nicety** — which is why the generator records its rejections as well as its values.

**A compiled expression is built once per tag, lazily**, the way `CdxTag.PadByte` already resolves on
first use, so a table with one unreadable tag keeps its other tags working — the shape ADR-28's
refusal has today.

## 3. Key-legal and filter-legal are different sets

The first draft treated functions as one flat list. They are not, and the reference already says so
in two ways:

- **Parser-time constant folding** (§6, "Restrictions", `e4parse.c:17-32`): the 2nd and 3rd parameters
  of `STR`/`STRZERO`/`SUBSTR`, the 2nd of `LEFT`/`RIGHT`/`PADL`/`PADR`, the 1st of `SPACE`, and
  `DATETIME`'s hour-to-ms must be numeric **constants**, or it is `e4notConstant`. `RIGHT`'s first
  parameter must itself be a field or a constant. A TRIM-family result may not feed `SUBSTR`.
- **Determinism.** `DATE()`, `TIME()`, `DATETIME()`, `RECNO()` and `RECCOUNT()` are legal in a filter
  and meaningless in a stored key: a key built from `DATE()` changes value every time it is
  evaluated, and `RECNO()` depends on where the cursor is. The C library does not forbid them in a
  tag expression; it simply produces a tag nobody can maintain.

This port makes the distinction explicit — a compiled expression knows whether it is **key-legal** —
and refuses a non-deterministic expression when a *tag* is selected, with the message naming which
function made it so. In a filter the same expression evaluates normally. This is a divergence from
the reference and is recorded as such (Decision 5).

## 4. Key time — what §9 requires that a bare field never needed

**Key length is computed and checked, never assumed** (§9.1). For the fixed kinds it is 8
(`r4num`, `r4date`, `r4numDoub`, `r4currency`, `r4dateTime`), 4 (`r4int`) or 1 (`r4log`); for strings
it is `nullIndicatorLen + expr4len × (1 + keySizeCharPerCharAdd)`, with the collation growth skipped
when the expression uses `ASCEND`/`DESCEND`, which pre-collates (`E4EXPR.C:1067-1095`). Comparing the
computed length against the tag header's is a cheap, strong check that the expression was parsed the
way the C library parsed it — and the first thing to run when a tag misbehaves.

**The null indicator, with its exception.** If any referenced field is nullable the key grows by a
leading byte — `0x80` before the value, or the whole key zero when the value is null
(§9.2 item 3) — **except for candidate keys (`typeCode & 0x04`)** (`E4EXPR.C:879-925, 894-899`).
Both halves matter: getting either wrong makes every key of such a tag the wrong *length*, which no
hand-built case should be trusted to settle.

**`hasTrim` at key time** (§9.4): trailing NUL bytes of a trimmed string result are rewritten to
`' '` in the key, which is what makes `TRIM`-based expressions produce fixed-width VFP-compatible
keys. It interacts with the `+` concat rows — row 41 `E4CONCAT_TRIM` shifts the right operand up and
NUL-fills the tail when an operand came from a trim, a memo or a calc.

**Limits are refusals, not assumptions:** FOX max key size **240** (`d4defs.h:2128`), and a memo field
in a simple tag expression is capped at 240 − nullByte (§9.5). A date-format picture is under 20
characters.

## 5. The seam that changes, and the three call sites that go with it

Step 008 built `IKeyValueSource` for this moment and its shape is nearly right:

```csharp
ReadOnlySpan<byte> Read(RecordBuffer record);   // today: the field's stored bytes
```

That works only because a bare field's stored bytes *are* the value, and `RecordKey.WriteValue` then
interprets them using the field's declared type. An expression has no field to look the
interpretation up from, and `UPPER(NAME)+STR(ID)` has no stored form at all. So the seam becomes
typed:

```csharp
ExpressionValue Read(RecordBuffer record);      // this step
```

`FieldValueSource` decodes its field into an `ExpressionValue` — which it can, because the field's
type says how — and `ExpressionValueSource` evaluates. `RecordKey` then converts from a value rather
than from bytes, which is what `expr4keyConvert` does (§9.2). **The transforms of step 008 are
untouched; only their input side moves.**

**Say plainly what step 008's design got wrong**, because this is the second document to repeat it:
it claimed the seam would take an expression implementation "without anything above it moving". Three
live call sites move, all of them turning on `SeekConverter.Field` being null for an expression tag:

| site | today | why it moves |
|---|---|---|
| `Cdx/RecordKey.cs:138` | `converter.Field!.Type == 'B'` | how a binary double is told from `N`/`F` text — §9.3's two key routes. Becomes the value's own kind |
| `Dbf/TableTagCursor.cs:239` | `new FieldValueSource(Converter.Field!)` | the source is chosen by whether there is a field |
| `Dbf/TableTagCursor.cs:134` | `RecordKey.WriteValue(resolved, value, into)` | the **seek** path, where `value` is a user-supplied seek value in stored form, not an expression result |

And **`SeekConverter` is the class this step actually rewrites**, which the first draft never
mentioned: it holds both the typing (`KindOf`, from the field's type) and ADR-28's refusal, and it
must now take its kind from the compiled expression's result type. `KeyTypeResolver.PadByteFor`
performs the same field lookup independently, for the pad byte; the two collapse into one resolution
with one refusal.

**One latent break to fix while here:** `Dbf/TableTagCursor.cs:120` pads a collated whole-key search
to `stackalloc byte[resolved.KeyLength / 2]`, inferring the value's natural width from the tag. For an
expression key that inference is wrong — §9.1 makes the width `expr4len`, and only the *key* is
`expr4len × (1 + perCharAdd)`. The width has to come from the compiled expression.

## Classes

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `ExpressionLexer` | Entity | Text to tokens | the `[…]`/`'…'`/`"…"` forms, `.AND.`-style operators, **no `{}` date literals** and **no unary minus outside a numeric literal** (§3) |
| `ExpressionParser` | Entity | Tokens to a postfix node array by precedence (§4) | left-associative, `.AND.`/`.OR.` variadic |
| `TypeResolver` | Entity | Assigns each node its row and its result type | §6's promotion-then-advance loop; where `e4typeSub` is raised. Exposes an `internal` **postfix projection** — per node, its row, result type and length — which is what the resolution gate compares against the C's `E4INFO` array |
| `ExpressionValue` | Entity | One evaluated value | a discriminated union over the ten types of §1, carrying `isNull` and a length — never `object` |
| `FunctionTable` | Entity | The rows, as data | one row per `(name, code, prio, return, parameter types)`; the table is transcribed, not coded |
| `CompiledExpression` | Entity | An immutable compiled expression | result type, key length, key-legal, referenced fields |
| `ExpressionEvaluator` | Controller | A compiled expression and a record, out comes a value | holds no state between calls |
| `ExpressionValueSource` | Controller | `IKeyValueSource` over an evaluator | the seam of §5 |
| `SeekConverter` *(changed)* | Entity | The tag's key kind, length and pad, from a compiled expression | absorbs `KeyTypeResolver`; keeps the bare-field shortcut as a fast path |

**No global state, unlike the reference.** The C engine serialises every evaluation through one
process-wide critical section because its operand stack is global (`E4EXPR.C:87-135`, §14 risk 7). A
compiled expression here is immutable and an evaluation carries its own stack, so two cursors over
one tag do not contend — and it costs nothing, given ADR-41's contract already forbids concurrent use
of one table.

## Public surface

**Almost none, deliberately.** The engine is `internal`; what changes for a caller is that
`table.Tags["X"]` stops throwing when `X`'s expression is not a bare field name, and everything
already built on a tag — `SelectTag`, the `…Indexed` four, the whole seek family — works over it
unchanged. The expression engine becomes public when `QUERY` needs a filter argument, and that is
its step's decision, not this one's.

## Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| the record under an expression | `RecordBuffer` | built in memory from a field list | evaluation over *fields*, layers 1–2 — needed for the typed pairs, currency, datetime, memo and null, and for nothing else, since the kernels are tested over constants |
| the value behind a key | `IKeyValueSource` | hand implementation returning a fixed `ExpressionValue` | **axis B's gate itself** — a value the C library recorded, converted and compared to stored key bytes, with the evaluator out of the circuit |
| the C library's own answers | the generator's dump | committed corpus file | the gate |

The evaluator needs no I/O seam at all: it takes a record buffer and returns a value. That is the
point of splitting it from the tag machinery — and the second row is what keeps the two axes of the
gate from being able to cancel each other.

## Gate

**Two axes, because they fail differently and the reference splits there too.** `expr4vary` hands
back an evaluated value; `expr4keyConvert` turns that value into key bytes (`e4expr.h:332, 363`). A
gate that only checks the final key cannot tell a wrong `UPPER` from a wrong transform, and lets a
compensating pair of the two pass.

### Axis A — what a function computes

**Semantics are asserted in C#, against the spec, at layer 1.** `UPPER`, the `TRIM` family,
`SUBSTR`/`LEFT`/`RIGHT`/`PADL`/`PADR`, `CHR`, `SPACE`, `IIF`, `DAY`/`MONTH`/`YEAR`, `DTOS`,
`DELETED`, `RECNO`, `RECCOUNT`, the arithmetic and the logical operators are ordinary computations
whose answers `EXPRESSIONS.md` states. *(An earlier draft of this list named `LOWER`, `AT`, `LEN`,
`ASC` and `REPLICATE`. **None of them exist** — the engine has 35 named functions and no others; see
`EXPRESSIONS.md` §5.1.)* A table of
`expression → expected value` **is the specification**, reads as one, and covers branches no corpus
record happens to reach. `CLAUDE.md`'s rule bars hand-written expected *bytes* — on-disk facts — and a kernel's
result is not one. *(An earlier draft of this section claimed otherwise and put thirty functions on
the corpus's back for no reason.)*

**And the logic is tested over constants, not over fields.** `UPPER("jöhn ")`,
`SUBSTR("ABCDEF", 2, 3)`, `STR(2.5, 10, 0)`, `PADL("X", 5)`, `"AAAA" = "AAA"`, `1/0` — the
expression *is* the test case. There is no record dimension, the edge case sits in the literal where
it reads as specification, and a failure cannot be a field-decode bug wearing a kernel's clothes,
because that path belongs to `DBF-READ` and is gated already.

**The literal set decides where a field is still required**, and §1 already fixes it: literals add
only `r4numDoub`, `r4str` and `r4floatBin`, plus `.T.`/`.F.` for `r4log`.

| reachable from constants | needs a field |
|---|---|
| `r4str`, `r4numDoub`, `r4log`, `r4floatBin`; `r4dateDoub` through `CTOD`/`STOD` over a string literal; `r4dateTime` through `DATETIME` over numeric literals | **`r4num`, `r4date`, `r4int`, `r4currency`** — memo, and **null in any type** |

**The field-only set is exactly the field-loader-only set**, read off the table: no named function
returns `r4num`, `r4date`, `r4int` or `r4currency`, so they enter an expression through rows 8, 10, 11
and 13 (`e4functi.c:39-49`) or not at all. And `CTOD`, `STOD` and `DATE` return **`r4dateDoub`, not
`r4date`** (`e4functi.c:221-223`) — the stored 8-character form has no function that produces it.
Which is pointed, because `r4num`/`r4numDoub` and `r4date`/`r4dateDoub` are exactly the pairs §1 warns
a careless design collapses: in both, the half no literal reaches is the half carrying the separate
key route of §9.3.

**Constants do not weaken row coverage, because there is no whole-expression folding.** A literal
stays an `E4DOUBLE`/`E4STRING` loader node with its function node intact above it — visible at
`e4parse.c:274-276`, where the `r4numDoub → r4floatBin` promotion is implemented by rewriting a
*constant loader's* row from `E4DOUBLE` to `E4FLOAT`. So a constant-only expression resolves through
the real overload row. That promotion is in fact observable **only** on a constant operand, and is
constant-only testable by construction.

**The C library is the oracle for exactly three things inside this axis**, each a place where a
C#-authored expectation would only confirm our own misreading:

1. **The unknowns**, where reading the source yields no answer: `STR`'s midpoint rounding and its
   overflow behaviour (`c4dtoa45` absent, §14 risk 1), `DTOC`'s picture and the default `dateFormat`
   (risk 2), `VAL` on non-numeric text, `CTOD` on an invalid date, `EMPTY` per type (the header
   comment restricts it to `r4str` "for now"), `TTOC`'s format, currency scaling, and
   `expr4currency()`'s constant 0 (risk 8). Decisions 8 and 9 join them — readable in `e4functi.c`,
   but they decide record sets, so they are gated rather than read.
2. **Which row runs at all** — upstream of semantics, and §2's central risk: a missing overload row
   selects its neighbour silently and produces a *plausible* value, which a test written against the
   wrong row will happily confirm. The resolution gate below is the answer.
3. **Filters**, which have no observable output at all: a `FOR` tag already contains only the records
   that matched, so nothing in the read path ever evaluates one.

### Axis B — what a value becomes on disk

**Stored keys, and nothing else.** §9.3's two numeric routes, the collation growth and its
`ASCEND`/`DESCEND` skip, the null indicator with its candidate-key exception, `hasTrim`'s
NUL-to-blank rewrite, concat row 41's shift-and-NUL-fill, the 240 cap, and the computed key length
against the header.

**Driven from the recorded value, not from our evaluator.** The test reads the C library's value for
`(expression, record)` out of the dump, hands it to `RecordKey` as an `ExpressionValue`, and compares
the result against the bytes in `CDXEXPR.cdx` — through the `IKeyValueSource` row of
[Seams](#seams), which exists for exactly this. With the evaluator out of the circuit, axis B reddens
for transform reasons only, and the two axes compose end to end in sub-step 8 without ever having been
able to cancel.

### What the generator must record

Four sections. The first is table-independent and can land before `CDXEXPR` exists.

**1. The function table itself.** `v4functions[193]` is `extern` and `e4functions()` returns it
(`e4expr.h:142-144, 336`); every field but the function pointer — `name`, `code`, `nameLen`,
`priority`, `returnType`, `numParms`, `type[4]` — prints in about fifteen lines of generator code.
`FunctionTable` is then compared row for row against the C library's own array, and "the table is
transcribed, not coded" stops being a hope. It also unblocks the parser, since precedence is read from
the table (§2b) — a transcription slip there would otherwise surface as a parse difference only for
the rows some expression happens to reach.

**2. The compiled node array — the resolution gate.** `EXPR4.info[0..infoN-1]` survives the parse
(`d4data.h:3614-3641`), and each `E4INFO` carries **`functionI`**, the index into `v4functions` that
overload resolution chose, beside `len`, `numParms` and `fieldNo` (`d4data.h:3587-3610`). Recording
the sequence turns §2's silent-neighbour risk into a diff on an integer, and gates the parser's output
shape and the promotion rewrites at the same time, since a promotion is an extra node with a row of
its own. Per expression it also records `type`, `len`, `hasTrim`, `hasMemoField` and `expr4keyLen`
(`e4expr.h:359`) — which gives axis B's key-length half over the whole expression list, instead of
over the dozen tags one corpus file can hold.

**Row coverage becomes a number.** The `functionI` values the dump reaches, asserted against our
table: every row either covered or explicitly unreachable (calc, total, the `r5*` family of §1), and
anything that is neither fails by name. The comparison rows reach coverage mechanically — the
generator loops the operators over typed field pairs, rather than anyone hand-typing 47 strings.

**3. Values, for axis A's oracle set — in two sections, on the constant/field line above.**
`[constant expressions]` is **flat**: one line per expression, no record dimension, and independent
of every corpus case's shape, so it can be written before `CDXEXPR` is designed. `[field
expressions]` is the cross product, now small and purposeful — the typed pairs, currency, datetime,
memo and null, and nothing a literal could have said instead.

Both record the raw result bytes, the result type and the length. **Raw bytes, escaped — never
formatted text.** `expr4vary` hands back the result buffer, and a `%.17g` round-trip for `r4numDoub`,
`r4dateDoub`, `r4currency` and `r4floatBin` would make formatting a second thing to get right while
hiding exactly the low bits `t4dblToFox` depends on.

**Non-deterministic expressions are recorded for shape only.** `DATE()`, `TIME()` and `DATETIME()`
are filter-legal by Decision 5, and a recorded *value* for one changes every run — against the
standing requirement that the corpus regenerate byte-identical. They stay in the expression list for
their type, length, key length and node array, all of which are stable; their values are asserted
C#-side against the clock. `RECNO()` and `RECCOUNT()` are deterministic per record and need no
exception.

**4. Rejections.** "We refuse what CodeBase refuses" is otherwise an assertion nobody checks, and it
is exactly the half a hand-written suite flatters itself about. A list of strings — `-FIELD`,
`{01/01/2000}`, `'unterminated`, a 9-character function name, `SUBSTR(TRIM(X),1,2)`, `STR(X, N)` with
a non-constant width — and for each, whether `expr4parse` accepted it and the `error4code` if not.
The code is recorded and compared through an **explicit mapping** onto this port's exception types,
not merely accept-versus-reject: `e4notConstant`, `e4typeSub`, `e4unterminated` and `e4tagExpr`
(`d4defs.h:1982-1991`) are what distinguish a real bug from a generic parse failure.

### The corpus case

A new `CDXEXPR.DBF` + `.cdx` whose tags are the shapes a bare field cannot reach, every stored key
rebuilt from the recorded value of the record it names:

- `UPPER(K_TEXT)` — the commonest real key, and the one ADR-28 names
- `STR(K_INT)` and `STR(K_NUM, 12, 3)` — the unverifiable formatting kernel, against real keys
- `DTOS(K_DATE)` — date as sortable text, and `DTOC(K_DATE)` for the picture-length rule
- `K_TEXT + K_CITY` and `K_TEXT - K_CITY` — the two concat rows, whose key lengths differ in where
  the blanks go
- `TRIM(K_TEXT)` — the `hasTrim` NUL-to-blank rule (§9.4)
- `SUBSTR(K_TEXT, 1, 5)`, `LEFT`, `RIGHT`, `PADL`, `PADR`
- `DESCEND(K_TEXT)` — pre-collated, which skips the collation growth of §9.1
- a `FOR` filter more interesting than `CDXBASE`'s `K_I > 0`: one using `.AND.`, `$` and a function
- **a tag over a nullable field**, for the `0x80` indicator and its length — `VFPNULL` has nullable
  fields already and the generator need only put a tag on one
- **a midpoint case for `STR`**: 2.5 and −2.5 at zero decimals, 0.125 and 0.135 at two

**A second axis for the tags: one per key type, function-produced wherever a function can produce
one.** The list above is organised by *function*, and exercises the kernels that reach a key; this one
is organised by *result type*, and exercises §9.3's conversion routes — which is what axis B is
actually about. Read off `v4functions`, only six of the ten types have any function that returns them:

| key type | key len (§9.1) | tag expression | |
|---|---|---|---|
| `r4str` | computed | `UPPER(K_TEXT)` | function |
| `r4numDoub` | 8 | `VAL(K_TEXT)`, `YEAR(K_DATE)` | function |
| `r4dateDoub` | 8 | `CTOD(K_TEXT)` | function |
| `r4dateTime` | 8 | `DATETIME(…)` over literals — one distinct key, so degenerate; the real route is a bare `K_TS` | function |
| `r4log` | 1 | `EMPTY(K_TEXT)` | function — and it settles the logical-key open question below |
| `r4floatBin` | **§9.1 does not say** | `K_FLT * 1` | function — and the tag header is where the answer is |
| `r4num` | 8 | bare `K_NUM` | **no function returns it** |
| `r4date` | 8 | bare `K_DATE` | **no function returns it** |
| `r4int` | 4 | bare `K_INT` | **no function returns it** |
| `r4currency` | 8 | bare `K_CUR` | **no function returns it** |

Two things fall out of that. **The four bare-field rows are not a gap this step can close** — they are
the entire reachable surface for those types, the step-008 bare-field path already covers them, and
all that is new for them is §4's null indicator and its candidate-key exception. And **`r4floatBin`
has no key length in §9.1**, so `K_FLT * 1`'s tag header is the only place the answer exists — which
makes the `H` probe of sub-step 1 gate two things rather than one.

**Its records supply what a literal cannot, and stop there.** With the kernels tested over
constants, the matrix is no longer the whole edge-case space — it is the typed inputs and the null
states: a null in every nullable column, an `N` and an `I` at zero, negative and their extremes,
currency at four decimals, a datetime, a blank date, a memo empty and a memo past 240 bytes, an `H` at
its extremes, both logicals, and text with trailing blanks for `TRIM` and the concat rows, which are
key-affecting. A dozen designed records, not two hundred arbitrary ones.

**Layer 5 owes nothing here.** This step adds no performance claim; the 2× cache gate of ADR-40 keeps
running unchanged.

## Decisions

**1. The type set is derived, not chosen** — the closure of §1 over `FieldResolver.ReadableTypes`.
The CodeBase `r5*` family is **unreachable**, not excluded, and VFP 9's `V`/`Q`/`W` are unreachable
for a second and stronger reason: the reference misreads them too, so there is no oracle. *ADR
candidate* — it is the argument that will otherwise be re-litigated every time someone counts the
193 rows.

**2. `H` / `r4floatBin` stays in.** The one member of the closure VFP does not require. Kept because
the port already reads the field, and it is gateable. *Rejected —* refusing an expression over a field
`GetDouble` will happily decode: a worse contract than the overload rows it saves. **"Gateable" is a
claim, not a fact:** no corpus case has an `H` field, and whether `d4create` in this build accepts one
is unverified. `PLAN.md` sub-step 1 probes it first; if it refuses, this decision reopens as a
refusal, on day one rather than at sub-step 5.

**3. The whole table, resolved by overload, never by demand.** A missing row selects a neighbouring
row rather than failing, so partial coverage is a silent wrong key. *Rejected —* a keys-only subset:
`QUERY` needs the filter vocabulary immediately, so the evaluator would be reopened within one step.

**4. `STR` ships, gated empirically, rather than being refused pending a VFP VM** (ADR-04, ADR-11;
ADR-44 has since made the VM real — `STR`'s rows are in `vfpfunc.prg`, which does not change this
decision, only what can be said about the residue).
The formatting kernel `c4dtoa45` is absent from this source drop (§14 risk 1); everything about `STR`
is pinned except midpoint rounding, one corpus case answers it, and the summary states the claim as
*matches every case the corpus holds* rather than *proved correct*. This is the route `FoxNumeric`
took for `c4atod` and the one that closed three of §14's eight risks in step 007.

**5. Key-legal is enforced; the reference does not enforce it.** A non-deterministic expression
(`DATE`, `TIME`, `DATETIME`, `RECNO`, `RECCOUNT`) is refused when a **tag** is selected and evaluated
normally in a filter. *ADR candidate*, because it is a deliberate divergence: the C library allows the
tag and produces one nobody can maintain.

**6. The engine keeps no global state**, against the reference's process-wide critical section
(`E4EXPR.C:87-135`). Free, and consistent with ADR-41.

**7. `EntryAt`-style local shapes stay local.** `SeekConverter` absorbing `KeyTypeResolver` is a
refactor the step needs, not a decision worth an entry.

**10. The engine takes an explicit date policy, defaulting to invariant.** What the reference calls a
language is exactly two settings — the `dateFormat` picture and how a two-digit year gets its century
— and it takes both implicitly: the picture from a compile-time switch (`c4code.c:1126`), the century
from `GetLocalTime` (`D4DATE.C:580-601`). The port takes an `ExpressionOptions` instead, defaulting
to a `CCYYMMDD` picture with **no** century inference, so the clock-dependent path is unreachable
unless a caller asks for it; presets mirror the reference's five language builds. **Recorded as
ADR-42.** It is a divergence: `DTOC` output and `CTOD` parsing both change unless configured.

**Its unfixable half, which is why the default is not enough.** `DTOC`'s key length *is* the picture
length, so the picture is byte-affecting — but a tag header records only the length. `MM/DD/YY` and
`DD.MM.YY` are both 8 characters and give different bytes for the same date, so **the picture cannot
be recovered from the file and §4's computed-versus-stored key-length check cannot catch a wrong
one.** A German-built `DTOC` tag read under any 8-character picture yields wrong keys of the right
length, silently. Therefore: a **tag** whose expression is picture-dependent is **refused unless the
picture is explicitly configured** — the shape of ADR-28's refusal — while a **filter** simply uses
the configured picture. Decision 5's key-legal/filter-legal split, on a second axis.

**11. Visual FoxPro's functions are supported, even though CodeBase has none of them.** The reference
has 35 named functions and **no arithmetic at all** (`EXPRESSIONS.md` §5.1). VFP has `ABS`, `INT`,
`ROUND`, `MOD`, `MIN`, `MAX`, `LOWER`, `LEN`, `AT`, `ASC`, `REPLICATE` and more, and **a CDX written
by VFP can carry any of them in a tag expression, stored as text in the tag header**. Refusing them
is therefore a hole in the stated compatibility target — "files VFP writes must be read correctly
here" — and not a safeguard. Three tiers:

| | a VFP-only function |
|---|---|
| in a **filter** | supported freely — nothing is written, and `QUERY` needs the vocabulary |
| **reading** an existing tag | supported — otherwise valid VFP indexes are unreadable |
| **writing** a new tag | refused; that is where the superset rule bites, and it is `WRITE`'s call |

**It adds no new key route**, which is the point that makes it cheap: key length and key bytes follow
the **result type**, never the function (§9.1 switches on type; `expr4keyConvert` takes a key type).
`ABS(X)` is `r4numDoub` → length 8 → `t4dblToFox`, already gated by `T_VAL`'s tag. Only the *value*
is new, and values are axis A, which is C#-authored regardless.

**The residual is string-returning functions**, whose parse-time result *length* is per-function —
`LOWER(C(20))` → 20, `REPLICATE("A",3)` → n × operand. Get that wrong and every key of such a tag is
the wrong size. **The existing check is the gate**: §4's computed-versus-stored key length, which for
a VFP-written tag reads "we parsed this the way VFP did".

**Its one blind spot, which the corpus proves.** That check cannot separate the five 8-byte types
from each other — `CDXEXPR` has `T_NUM` (r4num), `T_DATE` (r4date), `T_VAL` (r4numDoub), `T_CUR`
(r4currency) and `T_TS`/`T_DTIME` (r4dateTime), **all `keyLen=8`** — and their transforms differ. A
wrong result *type* among those is invisible to it, while a wrong result *length* on a string is
caught at once. So start with functions whose result type is unambiguous; the arithmetic set is.

Two rules go with it. **On any name both engines define, CodeBase's behaviour is authoritative**,
because it is the gated one. And **every VFP-only row is marked as such in the function table**, so
row coverage reports "35 oracle-gated, N gated by C#-authored semantics only" instead of failing on
rows no dump can reach.

**Recorded as ADR-43.** It is a deliberate divergence, it changes what `Table.Tags` will open, and it
makes VFP a second source of truth beside the C library.

### Two semantic divergences that have to be decided, not implemented by accident

Both are §14 risk 6 — behaviours the source documents as CodeBase-not-VFP — and both are
**key- or record-set-affecting**, so silence is a decision either way.

**8. Prefix `=` compares only the common prefix** (§8, `e4functi.c:1007-1027`): `"AAAA" = "AAA"` and
`"AAA" = "AAAA"` are *both* true, where VFP with `SET EXACT OFF` makes only **the first** true — it
compares to the end of the right operand. `e4parse.c:59-62` claims the other one, and the VFP oracle
refutes it; see ADR-45. Recommendation: **replicate CodeBase**, because a `FOR` filter's semantics decide
which records a tag contains and a stored index must agree with the engine that built it. It has a
direct consequence for `QUERY`: `NAME = "SM"` is a **prefix range**, not an equality, and decomposing
it as equality would silently lose records. *ADR candidate, and the one `QUERY` will lean on.*

**9. Division by zero yields 0.0, not an error** (row 102, `e4functi.c:753-756`). Recommendation:
replicate, same reasoning, no exception. *Fold into 8.*

## What the first generator run settled

`CDXEXPR` and its dumps exist (sub-step 1), and the oracle answered several things this document had
listed as unknown. Recorded here because they were decisions waiting on evidence; the durable form is
in [`specs/EXPRESSIONS.md`](../../specs/EXPRESSIONS.md) §7.1, §7.3 and §9.1.

- **`STR` rounds half away from zero, on the decimal digits.** `STR(2.5,10,0)` → `3`,
  `STR(-2.5,10,0)` → `-3`, `STR(0.125,10,2)` → `0.13`, and `STR(0.135,10,2)` → `0.14` *even though
  the nearest double to 0.135 is below it*. Overflow fills with `*`. Neither .NET's `ToString("F0")`
  (half to even) nor a round-on-the-binary-value implementation reproduces all four.
- **The default `dateFormat` picture is 8 characters** — `T_DTOC`'s `keyLen=8`, and a DTOC key length
  is the picture length.
- **`r4floatBin`'s key length is 4**, which §9.1 had never stated.
- **A tag over a logical expression exists and its key is one byte.** `EMPTY(K_TEXT)` built without
  complaint, `keyLen=1`. `KeyTypeResolver`'s refusal of `'L'` is not defensible and goes.
- **The null indicator is one leading byte and is marked by `typeCode` bit 0x02** — `T_NULL` over a
  nullable `N(12,3)` has `keyLen=9`, `typeCode=0x62`.
- **`TRIM` returns the source width, NUL-padded, with `hasTrim=1`.** `TRIM("abc   ")` is six bytes,
  `"abc\x00\x00\x00"` — not three. A spec-authored C# expectation would have said `"abc"`, which is
  the single best argument for keeping the value dump for the unknown set.
- **Prefix `=` and division by zero are confirmed as Decisions 8 and 9 state them**: `"AAAA" = "AAA"`
  and `"AAA" = "AAAA"` are both true, and `1/0` evaluates to 0.0.
- **A logical *value* is four bytes** (`\x01\x00\x00\x00`) even though a logical *key* is one.
- **`CTOD` is picture-driven and reads a two-digit year.** `CTOD("01/15/2000")` is **2020**, not
  2000, and `STOD("20000115")` on the same day is 2000 — the two disagree by twenty years. An
  unparseable date is julian **−1**.
- **`VAL` takes the leading numeric prefix**: `VAL("01/15/2000")` → 1.0, `VAL("99/99/9999")` → 99.0.
- **`STRZERO(-42,8)` → `"00000-42"`.** The sign stays where `STR` put it; only the leading blanks
  become zeros.
- **`UPPER` is ASCII-only on a table with no code-page mark** — bytes `C9 E8` pass through unchanged.
  The mapping is selected by the table's code page, so a culture-aware uppercase is wrong here.
- **`DESCEND` is a plain byte complement** (`0xFF − c`) over machine-collated text.
- **The null indicator is driven by the *referenced fields*, not by the result type** —
  `EMPTY(K_NNUM)` has `keyLen=2`: one byte for `r4log` plus the null byte.
- **A memo operand's expression length is 1024**, NUL-padded, whatever the payload — so §4's 240 cap
  is a tag-time refusal, not an expression-time one.
- **A blank date collapses**: `DAY`/`MONTH`/`YEAR` give 0.0 and `K_DATE + 1` gives julian 0, while
  `DTOC` renders the *picture* (`"  /  /  "`) and `DTOS` renders 8 blanks.
- **There is no arithmetic function in the engine.** The named set is 35 — `ALLTRIM ASCEND CHR CTOD
  DATE DATETIME DAY DEL DELETED DESCEND DTOC DTOS EMPTY IIF KEYEQUAL L2BIN LEFT LTRIM MONTH PADL
  PADR RECCOUNT RECNO RIGHT SPACE STOD STR STRZERO SUBSTR TIME TRIM TTOC UPPER VAL YEAR` — with no
  `ABS`, `INT`, `ROUND`, `MOD`, `MIN`, `MAX`, `SQRT` or `SIGN`, and no `LOWER`, `LEN`, `AT`, `ASC` or
  `REPLICATE` either. The whole numeric surface is `+ - * / ** ^`. It is a key-building vocabulary,
  not a general expression language — which also bounds what `QUERY` can ever be asked to decompose.
- **The thirteen absent VFP names are refused with `e4unrecFunction`** (−470), so "CodeBase has no
  `ABS`" is gated rather than claimed. And three easily-missed members of the 35 are now covered:
  `L2BIN(n)` writes the long's four raw little-endian bytes, `DEL()` is the deletion flag as text
  (`" "` / `"*"`), and **`KEYEQUAL("ABC","AB")` is true** — it follows the prefix rule, not exact
  equality.
- **`CTOD` has no century pivot: it reads the century off the system clock** (`D4DATE.C:580-601`).
  Every two-digit year maps to the current century — in a 2026 run, `00` through `99` all became
  20xx, so **no 20th-century date is reachable through `CTOD`** with the default picture. It also
  makes any recorded `CTOD` value clock-dependent, which is why the dump now records the picture and
  the generation-time century in its header, and why Decision 10 exists.
- **The comparison block is six operators over eight types, and promotion beats the exact-type row.**
  `K_CUR > K_CUR2` resolves the `n,n` row, not `Y,Y`, because `r4currency → r4numDoub` is tried
  first — so the six currency comparison rows are **dead**, which is `EXPRESSIONS.md` §14 risk 8
  arriving from a second direction. `r4dateTime` has no promotion and its rows are live. `r4log`
  compares only with `=` and `<>`/`#`: `.T. > .F.` is `e4typeSub`. Detail in `EXPRESSIONS.md` §7.7.
- **The parser refuses with nine distinct codes, not four**: `e4fieldName` (−210), `e4notConstant`
  (−425), `e4numParms` (−430), `e4rightMissing` (−450), `e4typeSub` (−460), `e4unrecFunction` (−470),
  `e4unrecOperator` (−480), `e4unrecValue` (−490), `e4unterminated` (−500). The mapping onto this
  port's exception types has nine cases to cover.

**One defect the run found in the case itself, and it is worth keeping visible.** The first
`CDXEXPR` stored the ASCII text `"0.00"` in its `H` field, because **no `f4assign*` reaches
`r4floatBin`**: the type appears in exactly one assignment path in the whole library, the same-type
memcpy of `f4assignField` (`f4ass_f.c:99`), and `f4assignDouble` formats decimal text into the four
bytes instead. The generator now writes the bytes through `f4assignPtr`, and the corpus holds real
float32 — `0.0`, `-0.0`, `±1.0`, `FLT_MAX`, `FLT_MIN`. That the reference can only assign an `H`
field by copying another of the same type says something about how finished the extension is, and it
would have made the port's only `r4floatBin` gate meaningless.

**And one contradiction, which is why the rejection dump exists.** §3 lists "a TRIM-family result may
not feed `SUBSTR`" among the parser's restrictions, on the authority of the comment at
`e4parse.c:17-32`. **`SUBSTR(TRIM(K_TEXT),1,2)` parses**, and yields `type='C' len=2`. The comment
describes an intent the code does not enforce — at least when parsed with no tag context, which is
how the dump parses it. Until a tag over that expression is tried, treat the restriction as
*unconfirmed* rather than either true or false, and do not implement a refusal for it.

## Open questions

- **`r4float` versus `r4floatBin` in the spec.** §2.4 says `r4float`/`'F'` is a *numeric text* field
  "treated as `r4num`", while row 25 (`e4fieldIntF`) returns `r4float` for an int-to-float conversion.
  Those cannot both hold. Resolve against `d4defs.h:2779-2781` before writing the type table. What is
  now known: an `H` field is created **4 bytes** wide (`d4create.c:1620-1622`) and keys to 4 bytes, so
  `r4floatBin` is a 32-bit float and not a double — which the port's `FieldValueDecoder` should be
  checked against.
- **`expr4currency()` returns a constant 0 in FOX builds** (`E4EXPR.C:852-872`, §14 risk 8). It looks
  intentional and it makes the `r4numDoub → currency` path dead. `T_CUR` and the `K_CUR` value lines
  are now in the corpus, so this is answerable by reading them rather than by reasoning.
- **Does the `SUBSTR`-over-`TRIM` restriction bite in a tag context?** The dump parses with no tag;
  a tag over `SUBSTR(TRIM(K_TEXT),1,2)` would say. Cheap, and it decides whether §3's restriction
  list has one entry too many.
- **Does VFP's `STR` round the way CodeBase's does?** We witnessed CodeBase rounding half away from
  zero on the decimal digits. Decision 11 makes VFP a second source of truth, so for every name the
  two engines share, "match CodeBase" and "match VFP" stop being obviously the same instruction. The
  corpus cannot answer it and ADR-04 defers the live VFP that could. Latent today, and it becomes a
  real question the moment a VFP-written index is read.
  **[Answered 2026-08-29 — the run happened. `STR` agrees: VFP returns `3`, `-3`, `0.13` and `0.14`
  for the four midpoint probes, so on this shared name the two instructions are the same one. `TRIM`
  agrees on key length too. `=` does **not** agree, and that one is ADR-45.]**
- **The rejection list cannot currently observe a tag-only refusal.** Every string is parsed with
  `tagPtr = 0`, so `e4tagExpr` (−510) never appears and `OTHER->K_TEXT` comes back as `e4fieldName`
  (−210) rather than as the tag refusal §2b describes. A second rejection pass with a real
  `TAG4FILE` is owed before the port implements any tag-context refusal.

## The suite, flat

[`ORACLE-SUITE.txt`](ORACLE-SUITE.txt) is `CDXEXPR.expr.dump.txt` rendered as
`EXPRESSION -> value`, one line per case: 64 constants, 38 field expressions at all 16 records, and
the 16 rejections. It is **derived and not a source** — regenerate it from the dump rather than
editing it, and assert against the dump.
