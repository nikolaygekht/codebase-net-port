# 010-expr — design

The xBase expression engine, in the subset a reader needs: evaluate a tag's key expression against a
record, and evaluate a filter.

Phase 2 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written before any `.cs` file is opened.

## Goal

**A tag stops having to be a bare field name.** `UPPER(NAME)`, `STR(ID)`, `DTOS(HIRED)`,
`NAME+CITY` — the expressions real applications index by — become tags this library can type, key,
navigate and seek, exactly as a bare field is today.

That is the last thing `CDX-READ` owes, and the last prerequisite for `QUERY`, which decomposes a
filter expression and therefore cannot exist without one.

## What `EXPR` actually owes — one refusal, not two

The plan has said for three steps that two refusals wait on `EXPR`. Checking them against what 007
built, **only one does**.

**ADR-28 — a key expression that is not a bare field name.** Genuine. `KeyTypeResolver` reads the
expression as text and can only match it against a field name; anything else cannot be typed, so its
pad byte and key kind are unknown. This is what 008 closes.

**ADR-30 — stepping in tag order from a record the tag does not list.** **Its premise is false**, and
has been since 007. The ADR says "without `EXPR` this port cannot derive that key; it can only look
for the record itself, by walking the tag." But `RecordKey.Write` derives exactly that key for a
bare-field tag, and `Synchronize` already calls it. The refusal survives only because `Synchronize`
asks `SeekExact` for the key *and* the record number, so a record the tag omits misses — where the C
library seeks with the key alone and carries on from the nearest position (d4skip.c:1245-1275).

**This is the third time this premise has been wrong** — the audit said `Synchronize` needed `EXPR`,
`STATE.md` repeated it, and this folder's predecessor repeated it again. It is worth stating the rule
that keeps catching us out: **ADR-28 restricts a selectable tag to a bare field name, so anything of
the form "derive this record's key" is already available.** Only *typing an unknown expression* needs
the engine.

**The fix, concretely.** `Synchronize` asks `SeekExact` for the key *and* the record number, so a
record the tag omits misses and the step throws. The fallback is to position with the key alone —
`cursor.Seek(key)`, the first entry at or after it — and report success, so stepping continues from
the nearest place in the tag's order.

**One end-case must be read rather than inferred.** Landing "at or after" an absent record means the
entry reached **is already the next record in tag order**, so a forward `Skip(1)` should stop *on* it
rather than step past it — the opposite of the ordinary case, where the cursor sits on its own entry
and moves off. That asymmetry is exactly the kind of end-case step 006 guessed wrong four times before
reading `d4skip` closely, so `d4skip.c:1245-1275` gets read before this is written.

It needs nothing else from this step and could land alone; it is here because this is where ADR-30 is
revisited, and it supersedes rather than amends it.

## Scope — the whole function table, not a reading subset

`PORTING-PLAN.md` §5's `EXPR` row scopes this to "the subset needed to evaluate filters and key
expressions on read". **That narrowing is dropped.** The full table is smaller than its 193 rows
suggest, and splitting it would cost more in seams and second steps than it saves.

Of the 193: **27 are internal field loaders** the parser emits for a field reference and no caller
names; the rest are constant loaders, the operator overloads (`+` eight ways, `-` five, comparisons
forty-seven, arithmetic seven), and rows 105-191, which are **36 distinct named functions**:

```
ALLTRIM  ASCEND  CHR    CTOD   DATE   DATETIME  DAY   DEL     DELETED  DESCEND
DTOC     DTOS    EMPTY  IIF    KEYEQUAL  L2BIN  LEFT  LTRIM   MONTH    PADL
PADR     PAGENO  RECCOUNT  RECNO  RIGHT  SPACE  STOD  STR     STRZERO  SUBSTR
TIME     TRIM    TTOC   UPPER  VAL    YEAR
```

Thirty-six functions and one operator table is an honest step. It also removes a seam that would
otherwise have to exist and then be removed: `QUERY` needs the filter vocabulary — `EMPTY`, `DELETED`,
`RECNO`, `IIF`, `$` — and shipping keys-only would mean revisiting the evaluator immediately.

**Still out, and refused by name:** the Unicode `r5wstr` loaders and functions (a CodeBase extension
VFP has none of, `PORTING-PLAN.md` §2.3), the OLE-DB `r5*` types (§2.2), the calculation objects of
`EXPRESSIONS.md` §10, and user-defined functions. An expression using one is refused when the tag is
selected, the shape ADR-28's refusal has today, so a table's other tags keep working.

## The one genuinely unspecified behaviour: how `STR` rounds a midpoint

`c4dtoa45` and its siblings are absent from this source drop (`EXPRESSIONS.md` §14 risk 1, §7.1), and
`STR(n)` is one of the commonest index keys there is. That sounds worse than it is, so it is worth
being precise about what is actually unknown.

**Pinned already:** right-justification and blank padding; fixed `dec` decimals; the parse-time
clamping of `len` and `dec` (`e4parse.c:1409-1483`); and overflow filling the whole width with `*`,
which the comment at `e4functi.c:2545-2547` states outright (`StrZero(100,2)` gives `"**"`).

**Unknown: exactly one thing.** Whether a midpoint rounds half away from zero or half to even. C's
`printf` does the latter through IEEE; .NET's `ToString("F0")` does not necessarily agree; and
`c4dtoa45` is hand-rolled, so neither is authoritative for it.

**And it is settled by one corpus case**, with values sitting exactly on midpoints — 2.5 and -2.5 at
zero decimals, 0.125 and 0.135 at two — indexed by `STR`. One regeneration says which rule the C
library used, and the tests hold it there afterwards.

This is the route `FoxNumeric` already took for `c4atod`, and the one that closed three of §14's eight
risks in 007 — the `flags4dateTime` bitmap, `t4curToFox`, and the collation tables. Port the behaviour,
gate it against keys the C library actually wrote, and say in the summary that the claim is "matches
every case the corpus holds" rather than "proved correct".

## Architecture

Four pieces, in the order data flows. `EXPRESSIONS.md` §13 recommends exactly this shape and it is
adopted rather than reinvented.

| Class | Role | Responsibility |
|---|---|---|
| `ExpressionLexer` | Entity | Text to tokens. The `[...]`/`'...'`/`"..."` string forms, `.AND.`-style operators, no `{}` date literals, no unary minus outside a numeric literal (§3) |
| `ExpressionParser` | Entity | Tokens to a typed AST by precedence (§4), left-associative, with `.AND.`/`.OR.` flattened n-ary |
| `ExpressionValue` | Entity | The evaluated value: fixed-length `Char`, `Number`, `Logical`, `Date`, `DateTime`, `Currency`. A discriminated union, not `object` |
| `ExpressionEvaluator` | Controller | An AST and a record, out comes a value. Holds no state between calls |

**No global state, unlike the reference.** The C engine serializes every evaluation through one
process-wide critical section because its operand stack is global (`E4EXPR.C:87-135`). A compiled
expression here is immutable and an evaluation carries its own stack, so two cursors over one tag do
not contend.

## The seam from 007 needs generalising, and this is where that shows

007 built `IKeyValueSource` for exactly this moment, and its shape is nearly right but not quite.

```csharp
ReadOnlySpan<byte> Read(RecordBuffer record);   // 007: the field's stored bytes
```

That works because a bare field's stored bytes *are* the value, and `RecordKey.WriteValue` then reads
them according to the field's type — `FoxNumeric.ToDouble` for `N`, raw for `B`, and so on. An
expression has no field, so there is nothing to look the interpretation up from, and
`UPPER(NAME)+STR(ID)` has no stored form at all.

**So the seam becomes typed:**

```csharp
ExpressionValue Read(RecordBuffer record);      // 008
```

`FieldValueSource` decodes its field into an `ExpressionValue` — which it can, because the field's
type says how — and `ExpressionValueSource` evaluates. `RecordKey` then converts from a value rather
than from bytes, which is also what `expr4keyConvert` does (§9.2). The transforms of 007 are
untouched; only their input side moves.

**Say this plainly in the summary:** 008's design claimed the seam would take an expression
implementation "without anything above it moving". That was half right. The interface changes shape
once, here, and everything above `RecordKey` is genuinely unaffected.

## Two key-time rules 007 did not need and 008 does

**Key length has to be checked, not assumed** (§9.1). A tag header records its key length; the
expression must produce that length or the tag is corrupt or misread. For strings the length is the
expression's own length times the collation's per-character growth, plus a null indicator; for the
fixed kinds it is 8, 4 or 1. Computing it and comparing against the header is a cheap, strong check
that the expression was parsed as the C library parsed it.

**A nullable expression grows its key by a leading byte** (§9.2 item 3): `0x80` before the value, or
the whole key zero when the value is null. 007 never met this because no corpus tag is over a
nullable field. It is in scope here because `expr4keyLen` counts it, so getting it wrong makes every
key of such a tag the wrong length.

## Corpus

**A new case is required, and this step cannot be gated without it.** Every tag in the corpus today
is a bare field name — that is precisely the gap. Proposed `CDXEXPR.DBF`, one table with tags over:

- `UPPER(K_TEXT)` — the commonest real key, and the one ADR-28 names
- `STR(K_INT)` and `STR(K_NUM, 12, 3)` — the unverifiable formatting kernel, against real keys
- `DTOS(K_DATE)` — date as sortable text
- `K_TEXT + K_CITY` — concatenation, whose key length is the sum
- `SUBSTR(K_TEXT, 1, 5)` and `LEFT`/`RIGHT`
- `TRIM(K_TEXT)` — the `hasTrim` NUL-to-blank rule (§7.4)
- `DESCEND(K_TEXT)` — pre-collated, which skips the collation growth in §9.1
- a `FOR` filter more interesting than `CDXBASE`'s `K_I > 0`: one using `.AND.`, `$` and a function

Plus a nullable field with a tag over it, for the `0x80` indicator.

The gate is then the same one 007 used and the one that has worked every time: **rebuild every stored
key from the record it names**, now through the evaluator rather than through a field read.

## Settled

**The full function table is in scope** — 36 named functions and the operator table, not a
keys-and-filters subset. `QUERY` needs the filter vocabulary anyway, and a narrower step would mean
reopening the evaluator immediately.

**`STR` ships, gated empirically.** It is not refused pending a VFP virtual machine (ADR-04, ADR-11).
The single unknown is midpoint rounding, one corpus case answers it, and the summary will state the
claim as what it is.

## Also settled

**ADR-30 was closed inside 007** rather than here, as ADR-34. `EXPR` now owes exactly one thing:
typing a key expression that is not a bare field name. Everything in this design about deriving a
record's key already works.

**The null indicator byte gets a real corpus tag.** `expr4keyLen` counts it (§9.1), so a wrong
indicator makes every key of such a tag the wrong *length* — which no hand-built case should be
trusted to settle. `VFPNULL` has the nullable fields already; the generator needs to put a tag on one.
