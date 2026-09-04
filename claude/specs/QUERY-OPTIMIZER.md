# Bitmap Query Optimizer — CodeBase (S4FOX / Visual FoxPro compatible)

**Scope.** This document specifies, at implementation level, how CodeBase turns a logical filter
expression into a set of record numbers by seeking CDX tags instead of scanning: the `BITMAP4`
constraint tree, the `CONST4` range constraints, the decomposition rules, which expression forms are
optimizable and which are not, how a leaf is evaluated through a tag seek, how AND/OR/negation
combine, and where the engine falls back to evaluating the expression per record. S4MDX / S4CLIPPER
divergences are noted only where the S4FOX path reads differently. S4CLIENT code paths are ignored.

All facts cite `FILE:line` in `original/source`. File names on disk are mixed-case (`m4map.c`,
`C4CONST.C`, `r4log.c`, `R4RELATE.H`, `F4FLAG.H`); the directory is case-insensitive. **Search
case-insensitively** — a `--include='*.[ch]'` glob silently misses `R4RELATE.H` and `C4CONST.C`.

**This document does not own the expression engine.** The compiled-node representation (`E4INFO`,
`EXPR4`, `functionI`, the `v4functions` table, the 47 comparison rows and their promotions) is owned
by [`EXPRESSIONS.md`](EXPRESSIONS.md); key bytes and collation by
[`KEY-COLLATION.md`](KEY-COLLATION.md); `CODE4` defaults and error codes by
[`API-ERRORS.md`](API-ERRORS.md). What is here is only what the optimizer does with them.

**Numeric row indices below are computed** from the arithmetic in `e4expr.h:150-274` and are marked
as such where it matters; the header carries `#error` assertions pinning three of them
(`E4NOT_EQUAL == 51`, `E4LESS == 91`, `E4CHR == 105`), which is how the computation is checked.

---

## 1. Architecture — the pipeline

A query is a single logical expression over one table, set as a string and evaluated lazily. The
whole path, in order:

| Step | Function | File:line |
|---|---|---|
| 1 | `relate4querySet` stores the source string (it does **not** parse) | r4relate.c:4058-4104 |
| 2 | `relate4top` parses it on first use, and **requires `r4log`** | r4relate.c:6036-6043 |
| 3 | `log4bitmapDo` sets `fullyMapped`, builds the data lists, calls the optimizer | r4log.c:503-529 |
| 4 | `bitmap4evaluate` runs decompose → reduce → redistribute → generate | m4map.c:2426-2470 |
| 5 | `bitmap4init` → `bitmap4can` decomposes the compiled expression into a `BITMAP4` tree | m4map.c:331-380, 76-295 |
| 6 | `bitmap4reduce`, `bitmap4redistribute`, `bitmap4redistributeBranch` normalise the tree | C4CONST.C:277-298, 300-383, 458-528 |
| 7 | `bitmap4generate` walks the tree, evaluating leaves by tag seek into an `F4FLAG` bitset | m4map.c:2317-2378 |
| 8 | the bitset is copied into `relate->set` | m4map.c:2465 |
| 9 | `relate4top`/`skip` skip to set bits and call `log4true` per candidate | r4relate.c:6259-6301, 6328 |

Two properties of this shape matter for the port:

- **The bitmap is built once, eagerly, at `relate4top`** — not incrementally as records are read. It
  is a materialised bitset over record numbers, sized `d4recCount + 1` (m4map.c:2219), because
  record numbers are 1-based and there is no record 0 (r4relate.c:6781).
- **The record set is a *candidate* set, not the answer**, unless `fullyMapped` says otherwise. §7
  is the boundary, and it is the single most important rule in this document.

### 1.1 The bitset is not the tree

`BITMAP4` is the **constraint tree** — per-tag ranges and AND/OR structure. The actual bitmap is a
separate type, `F4FLAG` (F4FLAG.H:17-30), a byte-addressed bitset with a **complement flag**:

```c
typedef struct {
   CODE4 *codeBase ;
   unsigned char *flags ;
   unsigned long numFlags ;
   Bool5 isFlip ;              /* the set is the COMPLEMENT of the bits */
   Bool5 discardOutOfRange ;   /* tolerate flagNum > numFlags rather than erroring */
} F4FLAG ;
```

`isFlip` exists so that a constraint matching most of the table can be stored as its small
complement; §6.3 is where it is set. `discardOutOfRange` is passed 1 by the optimizer
(m4map.c:2226) because a tag can legitimately hold keys for records past the bitmap's end under
transactions (m4map.c:2220-2225).

**`BITMAP4LEAF` (0x40) and `BITMAP4AND` (0x20) are dead defines** (R4RELATE.H:268-269) — declared and
never read anywhere in the source. The live discriminators are the `branch` and `andOr` **char
fields** of `BITMAP4`. `PORTING-PLAN.md` §5 names `BITMAP4AND` as if it were a flag in use; it is not.

---

## 2. Core data structures

### 2.1 `CONST4` — one constraint value (R4RELATE.H:271-276)

```c
typedef struct CONST4St {
   LINK4 link ;
   int   offset ;   /* offset into L4LOGICAL.buf */
   int   len ;      /* 0 means "this constraint is not set" */
} CONST4 ;
```

A `CONST4` does not hold its bytes; it points into one growable buffer on the `L4LOGICAL`
(`const4return` = `log->buf + c1->offset`, C4CONST.C:925-929). **`len == 0` is the universal
"absent" test** — every site checks `map->lt.len`, `map->eq.len` and so on rather than a separate
flag, and clearing a constraint is `c4memset(&map->le, 0, sizeof(CONST4))`.

Buffer growth is `const4memAlloc` (C4CONST.C:933-947), which bumps `log->bufPos` and reallocs
`log->buf` when needed. **Note the offset bug surface:** `const4get` sets
`con->offset = log->bufLen - len` (C4CONST.C:993) — from `bufLen`, the *allocated* length, not
`bufPos`, the *used* length. `const4duplicate` does the same (C4CONST.C:966). These agree only when
the buffer is exactly full. See §10.3.

### 2.2 `BITMAP4` — a node of the constraint tree (R4RELATE.H:278-309)

```c
typedef struct BITMAP4St {
   LINK4 link ; LINK4 eval ;
   char  branch ;     /* 1 = interior node, 0 = leaf */
   char  andOr ;      /* 1 = and, 2 = or, 0 = neither (yet) */
   char  evaluated ; char finished ; char saveCount ; char flipBase ;
   char  noMatch ;    /* proven empty by constraint merging */
   L4LOGICAL *log ; RELATE4 *relate ; struct BITMAP4St *base ;
   int   type ;       /* r4num, r4str, ... — the CONSTANT's type, see below */
   /* Branch only */
   LIST4 children ; TAG4FILE *tag ;
   CONST4 lt, le, gt, ge, eq ;
   LIST4  ne ;
} BITMAP4 ;
```

Despite the `/* Branch only */` comment, `tag` and the six constraints are the **leaf** payload;
`children` is the branch payload. A leaf carries exactly one `TAG4FILE` and up to one range.

`type` is **the type of the constant**, taken from the function table's return type for the constant
node — `map->type = v4functions[log->expr->info[pos].functionI].returnType` (C4CONST.C:991). It
drives `const4less`/`const4eq` (§5.4) and the `r4str` special cases in §4.4 and §6.1. A direct
dependency on the function-table transcription: a wrong `returnType` in one row silently changes how
two constants compare.

`evaluated`, `finished`, `saveCount`, `flipBase` and `base` are **never read in the S4FOX path** —
they are set nowhere in `m4map.c` or `C4CONST.C`. Treat them as vestigial.

### 2.3 `L4LOGICAL` and `E4INFO_REPORT` — the parallel array (R4RELATE.H:40-55)

```c
typedef struct {
   struct DATA4LISTSt *relateDataList ;
   TAG4FILE *tag ;   /* The tag whose expression could replace the sub-expression */
} E4INFO_REPORT ;

typedef struct {
   EXPR4 *expr ;
   E4INFO_REPORT *infoReport ;   /* One per E4INFO entry */
   struct RELATION4St *relation ;
   CODE4 *codeBase ;
   char  *buf ; unsigned bufPos, bufLen ;
} L4LOGICAL ;
```

**`infoReport` is index-parallel to the compiled expression's node array.** Every fact the optimizer
records about a node is written at the node's own postfix position, and `infoReport[i].tag` is the
answer to "does some tag's key expression equal the subexpression rooted at node `i`?". This
parallel-array shape is the whole interface between the expression engine and the optimizer, and §3
is its contract.

---

## 3. What the optimizer reads from a compiled expression — the contract

The optimizer never sees the filter text. It walks `log->expr->info`, the postfix `E4INFO` array,
**from the root backwards**; the root is the last node (`log->expr->infoN - 1`, m4map.c:2429,
r4relate.c and m4map.c:2508). Three things are read per node, and a port whose compiled form cannot
answer all three cannot host this algorithm.

### 3.1 Row-index ranges over the function table

Node classification is **arithmetic on `functionI`**, the index into `v4functions` that overload
resolution chose. The relevant landmarks (e4expr.h:150-215, values computed):

| Constant | Value | Meaning |
|---|---|---|
| `E4LAST_FIELD` (= `E4FIELD_MEMO`) | 26 | last field-loader row |
| `E4DOUBLE` / `E4STRING` / `E4FLOAT` | 27 / 28 / 29 | literal loaders |
| `E4TRUE` / `E4FALSE` | 30 / 32 | logical literals (each a 2-row group) |
| `E4OR` / `E4AND` | 36 / 37 | the two variadic connectives |
| `E4COMPARE_START` (= `E4NOT_EQUAL`) | 51 | first comparison row — **pinned by `#error`** |
| `E4COMPARE_END` | 97 | last comparison row |
| `E4CHR` (= `E4FIRST_FUNCTION`) | 105 | first named-function row — **pinned by `#error`** |
| `E4STOD` / `E4CTOD` | 134 / 135 | the two date-from-string functions |
| `E4CALC_FUNCTION` | 148 | first named-calculation row |

Comparison rows 51-97 are **six contiguous operator blocks**, and the optimizer identifies each
operator by a half-open index range. The block layout — operator order `#`/`<>`, `>=`, `<=`, `=`,
`>`, `<`, of sizes 9, 8, 8, 8, 7, 7 — is owned by [`EXPRESSIONS.md`](EXPRESSIONS.md) §7.7, where it
is witnessed against the oracle. The ranges used here (m4map.c:198-278):

| Operator | Test | Rows |
|---|---|---|
| `!=` | `>= E4NOT_EQUAL(51) && < E4GREATER_EQ(60)` | 51-59 |
| `>=` | `>= E4GREATER_EQ(60) && < E4LESS_EQ(68)` | 60-67 |
| `<=` | `>= E4LESS_EQ(68) && < E4EQUAL(76)` | 68-75 |
| `==` | `>= E4EQUAL(76) && < E4GREATER(84)` | 76-83 |
| `>` | `>= E4GREATER(84) && < E4LESS(91)` | 84-90 |
| `<` | `>= E4LESS(91) && < E4CHR(105)` | 91-97 |

The `<` test's upper bound is **loose** — `E4CHR` is 105, but rows 98-104 are the eight rows between
`E4COMPARE_END` and the first function (`E4FIRST_FUNCTION = E4COMPARE_END + 8`, e4expr.h:157). It is
safe only because the outer guard at m4map.c:154 has already required `functionI <= E4COMPARE_END`.
A port that reorders the function table, or that stores operators as an enum rather than as row
indices into the ported table, must reproduce these six *sets*, not these six *arithmetic ranges*.

**Consequence for the port's function table:** row order is load-bearing. `DESIGN.md`'s decision to
transcribe `v4functions` as data rather than code (`claude/dev/010-expr/DESIGN.md:294`), and to gate
the resolved row indices against the C's recorded `functionI`, already preserves it — but it does so
as a side effect of the resolution gate, not by stated intent. State it.

### 3.2 `e4isConstant` — is this subtree a compile-time constant? (r4log.c:27-52)

```c
switch( infoPtr->functionI ) {
   case E4DOUBLE: case E4STRING:
   case E4TRUE: case E4TRUE+1: case E4FALSE: case E4FALSE+1:
      return 1 ;
   case E4STOD: case E4CTOD:
      for ( pos = infoPtr->numEntries - 1 ; pos >= 0 ; pos-- )
         if ( (infoPtr-pos)->fieldPtr != 0 || (infoPtr-pos)->functionI >= E4CALC_FUNCTION )
            return 0 ;
      return 1 ;
}
return 0 ;
```

Three things to take from it:

1. **The test is a single node's row, not a recursive walk** — except for `STOD`/`CTOD`, where it
   walks the node's own `numEntries` span **backwards** and rejects if any node in it loads a field
   or is a calculation. So `CTOD("01/15/99")` is a constant, `CTOD(DTOC(K_DATE))` is not.
2. **`E4FLOAT` (row 29) is absent.** A `r4floatBin` literal is therefore **not** a constant to the
   optimizer. `EXPRESSIONS.md` §7.7 witnesses that `K_FLT > 0` promotes the literal to row 29 and
   compares at row 90 — so **a comparison against an `H` field is never optimizable**, even though
   `e4isTag` does not refuse `r4floatBin` tags. The refusal comes from the constant side. This is
   checkable against the corpus and is listed in §12 as unwitnessed.
3. **Arithmetic cannot appear** — the reference has no arithmetic functions at all
   (`EXPRESSIONS.md` §5.1), so `K_NUM > 3 * 2` is not a question that arises. `3 * 2` does not parse.

### 3.3 `e4isTag` — does a tag's key expression equal this subtree? (r4log.c:61-255)

**It is called from exactly two places, and both are in `bitmap4can`'s comparison branch**
(m4map.c:165, 172) — never from a sweep over the node array. Only **one operand of a comparison whose
other operand is a constant** is ever tested, and only for comparisons the decomposition walk
actually reaches. So `infoReport[i].tag` is a property of **the walk**, not of "which subexpressions
some tag happens to equal", and a port that fills the array by testing every node against every tag
will fill entries the reference leaves empty. Two consequences are witnessed in the corpus
(`CDXEXPR.query.dump.txt`):

- **Nothing under a `.NOT.` is ever tested.** `.NOT. K_NUM > 0` leaves the whole array empty, because
  `E4NOT` is neither connective nor comparison and the walk returns before descending (§4.1).
- **An `.OR.` is abandoned at the first operand it cannot use, and operands are visited backwards**
  (m4map.c:107, and the destroy loop at m4map.c:127-141). So `K_NUM > 0 .OR. K_LOG` leaves the array
  **empty** — `K_LOG` is visited first and kills the branch before the comparison is reached — while
  the mirror image `K_LOG .OR. K_NUM > 0` **records `T_NUM`** on a branch that is then thrown away.
  Same records, same absent bitmap, different parallel array. Nothing but reproducing the visit order
  gets both right.

The comparison itself is a **structural comparison of two compiled node arrays**. For each tag of the table, in `d4tagNext` order (r4log.c:103-105), the tag is skipped
unless it passes the four gates of §3.4; then the tag's own root node
(`tagFile->expr->info + tagFile->expr->infoN - 1`, r4log.c:146) is compared against the candidate
node, walking **both arrays backwards in lockstep** for `numEntries` nodes (r4log.c:147-151):

| Per-node check | Rule | File:line |
|---|---|---|
| shape | `len`, `numEntries`, `numParms` must all be equal | r4log.c:154-158 |
| field-ness | `fieldPtr` must be null in both or non-null in both | r4log.c:161-172 |
| field owner | the field's `DATA4` must be the queried table — **no slave's tag may be used** | r4log.c:173-186 |
| field identity | `memcmp` over the `FIELD4`'s `shortName + len + dec + type + offset`, plus same `dataFile` | r4log.c:210-217 |
| long names | when `longFieldNamesSupported`, compare from `fieldPtr+11` over `len+dec+type+offset` and `strcmp` the `longName` | r4log.c:188-208 |
| constants | for `E4DOUBLE`, `E4STRING`, `E4CTOD`, `E4DTOC`, `E4DTOC+1`: `memcmp` the two expressions' constant pools at `i1` over `tagInfo->len` | r4log.c:221-228 |
| other nodes | `i1` must be equal | r4log.c:230-232 |
| row | `functionI` must be equal **unless both are `<= E4LAST_FIELD`** | r4log.c:234-236 |

On a match it records `reportPtr->tag = tagOn->tagFile` and calls `t4versionCheck` (r4log.c:245-247).

Two of these are subtle enough to call out:

- **The row-equality exemption is the promotion accommodation.** A differing `functionI` is
  tolerated only when *both* rows are field loaders (≤ 26). That is exactly what promotion produces:
  `EXPRESSIONS.md` §7.7 witnesses `K_NUM` alone loading row 9 but row 8 when compared against a
  number. Without the exemption, `K_NUM > 0` would never match a tag on `K_NUM`. With it, any two
  field-loader rows are interchangeable — including, in principle, loaders of *different types* over
  the same `FIELD4`; the field-identity `memcmp` is what actually keeps that honest.
- **`memcmp` of `FIELD4` prefixes is why `d4data.h:3602` orders a struct field.** The comment there
  reads "must be at last position due to `memcmp()` in `e4isTag()`". A C# port has no `memcmp` over
  a managed field descriptor and must define structural equality explicitly — over name, length,
  decimals, type, offset and owning file — and must decide where promotion nodes sit in it. This is
  a **sub-step 3/4 decision in step 010**, not a step-011 decision.

### 3.4 Which tags the optimizer will use at all

Before any matching, `e4isTag` skips a tag entirely under four conditions:

| Gate | Rule | File:line |
|---|---|---|
| collation | unless `CODE4.useGeneralTagsInRelate` is set, a tag whose `collateName` is **neither `collate4none` nor `collate4machine`** is skipped | r4log.c:115-120 |
| type | S4FOX: a tag whose `tfile4type` is `r4currency`, `r4int`, `r4dateTime` or `r4dateTimeMilli` is skipped — **a blacklist of four, not a shortlist**, see below | r4log.c:127-139 |
| filter | a tag with a filter (`tagFile->filter != 0`) is skipped | r4log.c:144 |
| unique | a tag for which `t4unique(tagOn) == r4uniqueContinue` is skipped | r4log.c:144 |

The first two carry the source's own reasoning, and both are worth reading in full at
r4log.c:64-108. Paraphrased:

- **Type.** "CodeBase is also not able to optimize on tags in the new FoxPro field formats. This is
  because we only have simple field support, so, eg: `INT_FIELD = 6` — `INT_FIELD` can be converted
  to a double as an expression check, but the constant `6` cannot be converted to an int which would
  be able to be looked up in the index file." The comment goes on to name the allowed set as
  `r4double`, `r4charBin`, `r4memoBin` — **and the code does not implement that.** It is a `switch`
  that `continue`s on four types and falls through on everything else, so **date, logical and binary
  float keys are eligible**. Witnessed: `K_DATE = STOD("20200101")` matches a date tag and
  `EMPTY(K_TEXT) = .T.` matches a tag over a logical expression, both fully mapped
  (`CDXEXPR.query.dump.txt`). Per `CLAUDE.md`'s source-of-truth rule the code is authoritative and
  the comment is noise, as in §10.4. A port that transcribed the comment would refuse three kinds of
  tag the reference uses.
- **Collation.** "problem was using general tags in relation but the relate module normally uses
  machine collation so results could differ depending on if a tag was available" (AS 04/26/00). The
  seek machinery *does* collate the constant (§6.1 calls `expr4keyConvert` with the tag), so this is
  a deliberate refusal on the grounds that the *residual* comparison in `log4true` uses `memcmp`
  ordering and the two could disagree — a correctness refusal, not a missing feature.

**Both defaults exclude.** `useGeneralTagsInRelate` is not among the fields `code4initLow` sets, so
it is 0 by the constructor's `memset` ([`API-ERRORS.md`](API-ERRORS.md) §6, C4CODE.C:874).
`errDefaultUnique` **defaults to `r4uniqueContinue` (25)** (API-ERRORS.md:361, C4CODE.C:984), and
`tfile4unique` returns that stored action only when the tag is unique or candidate
(`typeCode & 0x05`), else 0 (I4TAG.C:3327-3337). So:

- a plain tag → `t4unique` = 0 ≠ 25 → **eligible**
- a `unique` or `candidate` tag → `t4unique` = 25 → **skipped**, under the default

**A skipped tag does not end the search.** `CDXBASE` carries both a plain tag (`T_DUP`) and a unique
one (`T_UNIQ`) over the same field, and `K_DUP = "CHERRY"` matches `T_DUP`
(`CDXBASE.query.dump.txt`) — so the `d4tagNext` walk continues past the refusal to the next
candidate rather than stopping at the first tag whose expression would have matched. A table with
one tag per field could not distinguish the two behaviours.

The net effect is that **CodeBase's optimizer can only use plain, unfiltered,
machine-or-none-collated tags whose key type is not integer, currency or datetime.** For this port that excludes the integer,
currency, datetime and `GENERAL` tags that steps 004-008 built and gated. Whether the port keeps the
restriction is a decision, not a fact; §11.1 states the case each way, and **ADR-52 decided it**:
replicate all four by default, widen the type and collation gates under an explicit setting, never
widen the filter and unique gates.

---

## 4. Decomposition — `bitmap4can` (m4map.c:76-295)

`bitmap4can(log, savePos, relate)` is a recursive descent over the postfix array, returning a
`BITMAP4` subtree or 0 for "not optimizable". `*savePos` enters at a node's root index and is
advanced past the node's whole span (`*savePos -= infoPtr->numEntries`, m4map.c:86) before any
recursion, so siblings are visited by walking backwards over spans.

**Any node that is neither a connective nor a comparison sets `fullyMapped = 0` and returns 0**
(m4map.c:288-292). That single `else` is the entire "not optimizable" rule: a bare logical field, a
`DELETED()` call, `EMPTY(x)`, an `IIF`, anything at all outside `.AND.`/`.OR.`/comparison, is
unoptimizable and falls to the scan.

### 4.1 `.AND.` and `.OR.` branches (m4map.c:90-149)

`functionI == E4AND || functionI == E4OR` creates a branch with `andOr` 1 or 2 and recurses once per
`numParms`. Both connectives are **variadic** in the compiled form, so `a .AND. b .AND. c` is one
node with three parameters, not two nested nodes.

The two connectives fail differently, and this asymmetry is the correctness core:

| | a child returns 0 | result |
|---|---|---|
| `.AND.` (`andOr = 1`) | that conjunct is dropped; `fullyMapped = 0` | the branch **survives** with its remaining children — a superset of the answer |
| `.OR.` (`andOr = 2`) | the whole branch is destroyed and 0 returned | **nothing** is optimized for this disjunction |

m4map.c:117-142. The reason is set logic: dropping a conjunct widens the candidate set, which is
safe because §7's residual evaluation removes the extra records; dropping a disjunct would *narrow*
it, losing records that no later step can recover. A branch that ends with no children at all is
destroyed too (m4map.c:144-148).

A child with `andOr == 0` — every leaf, which `bitmap4create` gets called with `andOr = 0` at
m4map.c:176 — **inherits its parent's** `andOr` (m4map.c:113-114). That inherited value is what
selects the leaf's evaluation mode in §6.3, so a leaf under an AND and the same leaf under an OR
evaluate by different code.

One oddity: at m4map.c:93-94, an `E4AND` node with `relate == 0` takes its `RELATE4` from
`infoReport[*pos].relateDataList->pointers[0]`. For the single-table case this is always the master
relate, and m4map.c:369-372 calls `bitmap4can` with the same argument in both arms of an `if` whose
branches are identical — vestigial, and safe to collapse in a port.

### 4.2 Comparison leaves (m4map.c:152-286)

For a comparison node, the two operand subtrees are located positionally:

```c
infoPtr-- ;                                       /* right operand's root */
tagPos   = *pos - 1 ;
tagPos2  = tagPos - infoPtr->numEntries ;         /* left operand's root */
infoTwo  = infoPtr - infoPtr->numEntries ;
```

**One side must be a constant and the other must match a tag** (m4map.c:163-175). Both orders are
accepted: if the right operand is the constant, the left must be a tag; otherwise the left must be
the constant *and* the right a tag. If neither arrangement holds, the leaf returns 0 — note this
early return happens **before** `fullyMapped` is touched, so a non-optimizable comparison inside an
AND leaves `fullyMapped` at whatever the connective set (m4map.c:117-121 does set it).

The leaf then records `map->tag = infoReport[tagPos].tag` (m4map.c:184) and evaluates the constant
side into `hold` via `const4get` (m4map.c:191-197), which **executes** that subexpression once
(`expr4execute`, C4CONST.C:975) and copies its result into the shared buffer.

### 4.3 Operator to constraint slot — and a defect that inverts ranges

Each operator writes `hold` into one of the six slots. The mapping is the obvious one **when the tag
is on the left**; when the constant comes first (`4 > K_NUM`, meaning `K_NUM < 4`) the operator must
be reflected. The reflection is guarded by, at all four sites:

```c
if ( tagPos = 1 && constPos == 0 )
```

m4map.c:216, 243, 251, 278. **That is an assignment, not a comparison.** C parses it as
`tagPos = (1 && (constPos == 0))`, which assigns `constPos == 0 ? 1 : 0` to `tagPos` and then tests
the assigned value. So:

- the condition actually evaluated is **`constPos == 0`** alone — the intended `tagPos == 1` half is
  discarded;
- `tagPos` is clobbered. It is not read again in that branch (`map->tag` was set at m4map.c:184), so
  the clobber is inert.

Whether the surviving condition is *equivalent* to the intent is the question the port must answer.
For a two-operand comparison whose operands are single nodes, the constant being at postfix position
0 and the tag at 1 are the same statement, and the two agree. They can only diverge when the
constant's subtree is not at position 0 — a comparison nested inside a larger expression, where the
operand spans start higher. The `AS Feb 19/13` comments show the author reasoning about exactly the
`val > tag` case, so the intent is unambiguous.

**The port should implement the intent** (the constant is the left operand), and gate it with a
corpus case for both operand orders inside an `.AND.` — because getting this wrong swaps `lt` for
`gt` and returns a *wrong record set*, which is the one failure class `CLAUDE.md` ranks above
slowness. Recorded in §10.1 and §12.

The mapping, reading the four reflected branches with the condition as intended:

| Operator | tag on left | constant on left (reflected) | File:line |
|---|---|---|---|
| `!=` | append a copy to the `ne` list | same (symmetric) | m4map.c:198-210 |
| `==` | `eq` | same (symmetric) | m4map.c:211-212 |
| `>` | `gt`, or `ge` when partial — §4.4 | `lt` | m4map.c:213-239 |
| `>=` | `ge` | `le` | m4map.c:240-247 |
| `<` | `lt` | `gt`, or `ge` when partial — §4.4 | m4map.c:248-274 |
| `<=` | `le` | `ge` | m4map.c:275-283 |

`!=` is the only operator that accumulates: each one appends a heap-allocated `CONST4` to
`map->ne`, so `x != 1 .AND. x != 2` yields a two-element list.

**The partial-string widening is applied asymmetrically.** Only two of the six branches consult the
key length: `>` with the tag on the left (m4map.c:222-238) and `<` with the constant on the left
(m4map.c:252-271). The other four reflected cases copy straight into the opposite slot with no
widening. Whether that asymmetry is deliberate or an oversight is unresolved — see §12.

### 4.4 Partial-string widening — a short constant turns `>` into `>=`

For a leaf whose `type` is `r4str`, if the constant is **shorter than the tag's key length**, `>` is
recorded as `ge`:

```c
keyLen = map->tag->header.keyLen ;
if ( map->type == r4str && hold.len < keyLen )   /* same as >= since a partial > */
   c4memcpy( &map->ge, &hold, sizeof( CONST4 ) ) ;
else
   c4memcpy( &map->gt, &hold, sizeof( CONST4 ) ) ;
```

m4map.c:222-236. The reasoning is prefix semantics: `NAME > "SM"` must still admit `"SM"` itself
padded out to the key width — `"SM      "` is *not* greater than `"SM"` under a prefix comparison,
so an exclusive lower bound would wrongly drop the whole `"SM…"` group. Widening to `>=` keeps them
and lets §7's residual comparison decide.

**Which paths widen, and why exactly those.** The widening is applied at two of the six operator
branches, and the reason is the slot each writes, not the operator each names:

| Path | Slot written | Widens? |
|---|---|---|
| `>` tag left | `gt` | **yes** (m4map.c:222-236) |
| `<` constant left | `gt` | **yes** (m4map.c:252-271) |
| `<` tag left, `>` constant left | `lt` | no |
| `>=` tag left, `<=` constant left | `ge` | no |
| `<=` tag left, `>=` constant left | `le` | no |

Only an exclusive *lower* bound can wrongly drop the prefix group, so only the two `gt`-producing
paths need the adjustment, and they are the only two that produce it. §12 item 2 asked whether the
remaining four were a second defect of §10.1's family; they are not.

**And the widening is exact, not a safety margin.** `NAME > "SM"` on a `C(20)` field compares a
20-wide left operand against a 2-wide right one, and [`EXPRESSIONS.md`](EXPRESSIONS.md) §8's
tie-break — the strict comparisons break a tie on width — makes `"SM" + 18 blanks > "SM"` **true**.
So the expression itself admits the whole `"SM…"` group; an exclusive byte seek would skip it, and
`ge` restores exactly it and nothing more. An equal-width constant never reaches the widening,
because `hold.len < keyLen` is false. This matters beyond the seek: it is what makes a widened leaf
safe to trust without a residual re-check (§7.3).

This is the same prefix rule that **ADR-45** settled for the `=` operator, arriving from the
optimizer's side. It has a direct consequence the plan already anticipated: `NAME = "SM"` is a
**prefix range**, not an equality. Note that `bitmap4can` itself does *not* widen `eq` — it copies a
short constant straight into `map->eq` (m4map.c:211-212) — so for `=` the prefix behaviour lives
entirely in the seek, where a short `len` is passed to `tfile4seek` and matches on the prefix (§6.1).

`S4VFP_KEY` halves `keyLen` for a double-byte VFP key before the comparison (m4map.c:223-234); that
build is not ours.

---

## 5. Normalisation — reduce, redistribute, combine

`bitmap4evaluate` runs three passes over the tree before any I/O (m4map.c:2438-2455). Their purpose
is to turn *n* separate constraints on one tag into one range, so the tag is seeked once:
"if a bitmap is `(val < 10 && val > 5)`, we want to create a simple bitmap between 5 and 10, instead
of having 2 different bitmaps" (C4CONST.C:300-307).

### 5.1 `bitmap4reduce` — drop redundant nesting (C4CONST.C:277-297)

Bottom-up; for each node, recurse into children, then apply `bitmap4redistributeShrink`
(C4CONST.C:110-136), which **replaces a branch that has exactly one child with that child**,
propagating the child's `tag` up if the parent had none. This exists because the parsed expression
carries whatever parenthesisation the user wrote.

### 5.2 `bitmap4redistribute` — combine siblings, or split so they can be (C4CONST.C:300-383)

Leaves return unchanged. For a branch: redistribute all children first, then **bail out entirely if
`parent->andOr != map->andOr`** (C4CONST.C:340-342) — no combining across a mixed AND/OR boundary —
then `bitmap4redistributeCombine`.

`bitmap4redistributeCombine` (C4CONST.C:170-276) walks adjacent child pairs and decides
**combine or split** for each:

| Condition | Action |
|---|---|
| `rightChild->tag != childMap->tag` | **split** |
| `rightChild->andOr != childMap->andOr` | **split** |
| `map->andOr != rightChild->andOr` | **split** |
| one child has `eq` and the other a non-empty `ne`, whose lengths differ but whose common prefix matches | **split** — "that instance is very complicated" (AS Aug 9/04) |
| either child is a branch | skip to the next pair |
| otherwise | **combine** via `bitmap4combineLeafs` |

A split calls `bitmap4redistributeSplit` (C4CONST.C:139-168), which lifts the right child into a new
sibling branch of the same `andOr`, creating a parent if there was none — the transform drawn in the
comment there. The effect is that leaves on the *same* tag become adjacent under one parent and
combine, while leaves on different tags are pushed into separate branches that stay separate.

`bitmap4redistributeLeaf` (C4CONST.C:386-456) is the reverse direction: it breaks a leaf's `ne` list
into an AND-branch of one map per constant. `bitmap4redistributeBranch` (C4CONST.C:458-528) is the
final pass, recursing over branches and calling `bitmap4redistributeLeaf` on combinable leaf pairs.

### 5.3 The canonical leaf

`bitmap4combineLeafs` (m4map.c:1314-1450) dispatches to twelve merge routines — six for AND, six for
OR — trying `map2`'s populated slots in order and folding them into `map1`:

- **AND order:** `le`, `ge`, `lt`, `gt`, `ne`, `eq` (m4map.c:1336-1359)
- **OR order:** `lt`, `gt`, `le`, `ge`, `ne`, `eq` (m4map.c:1413-1435)

Any merge may return 1, meaning the conjunction is provably empty; the parent is then
`bitmap4collapse`d (m4map.c:383-407) — all children destroyed, `noMatch = 1` set.

The `E4ANALYZE` assertions after the AND merges (m4map.c:1363-1401) state the **invariants of a
normalised leaf**, and they are the cleanest specification of the canonical form:

| Slot present | Excludes |
|---|---|
| `eq` | `le`, `ge`, `lt`, `gt`, `ne` — all of them |
| `ne` | `eq` |
| `ge` | `gt`, `eq` |
| `gt` | `ge`, `eq` |
| `le` | `lt`, `eq` |
| `lt` | `le`, `eq` |

So a normalised leaf is exactly one of: a single `eq`; an `ne` list; or a range with **at most one
lower bound** (`gt` xor `ge`) and **at most one upper bound** (`lt` xor `le`). Nothing else is
reachable, and a port can make that a type rather than six nullable fields.

### 5.4 The merge algebra

The twelve routines are mechanical case analysis; `bitmap4combineAndLe` (m4map.c:411-490) is
representative. Folding `map2->le` into `map1`:

| `map1` holds | Rule | Result |
|---|---|---|
| `eq` | `map2->le < map1->eq` → empty | else drop `map2->le` (the `eq` is already tighter) |
| `gt` | `map2->le <= map1->gt` → empty | — |
| `ge` | `map2->le < map1->ge` → empty; `map2->le == map1->ge` → collapse to `eq` at that value (empty if an existing `eq` disagrees) | — |
| `ne` | drop every `ne` entry below `map2->le`; if one **equals** it, tighten `le` to `lt` | — |
| `lt` | if `map2->le < map1->lt`, replace `lt` with `le` | — |
| `le` | keep the smaller | — |
| nothing | take `map2->le` | — |

and `map2->le` is cleared in every path. The `ne`-equals-bound case turning `le` into `lt` is the
only place the algebra strengthens an operator.

**Whether omitting it costs a record depends on the seeker, and for a faithful one it does not.**
`bmf4AndOt` walks the range and then removes each `ne` (§6.3), so a value struck out at an inclusive
bound is dropped by the exclusion pass whether or not the bound was tightened first. Tested by
omitting it in a port whose range walk also drops excluded values: **no record set moved**, on any
corpus filter. What the tightening buys there is that the walk can *start* past the value instead of
visiting and rejecting it, and that the merged constraint has one canonical form rather than two
equivalent ones. It is a cost and tidiness rule, not a correctness one — which matters when deciding
what gates it, because no record-set comparison ever will.

Constant comparison is by `map->type`, and **for `r4num`/`r4str` it is a prefix comparison over the
shorter length** (`const4less`, C4CONST.C:998-1027):

```c
case r4num: case r4str:
   if ( p1->len < p2->len ) {
      if ( c4memcmp( p1, p2, p1->len ) <= 0 ) return 1 ;   /* note <= */
   } else
      if ( c4memcmp( p1, p2, p2->len ) <  0 ) return 1 ;   /* note <  */
```

The `<=` in the shorter-left arm is what makes `"SM" < "SMITH"` true: equal on the common prefix,
and shorter, so less. `const4eq` (C4CONST.C:1029-1059) is deliberately **asymmetric on length** —
`p1->len < p2->len` returns 0 outright, otherwise `memcmp` over `p1->len`. `r4numDoub` and
`r4dateDoub` compare as `double`; anything else is an `e4info` error, so **the merge algebra supports
only four constant types**: `r4numDoub`, `r4dateDoub`, `r4num`, `r4str` (plus `r4log` in `const4eq`'s
`E4ANALYZE` list). Consistent with §3.4's type refusal, from the other end.

`const4addNe` (C4CONST.C:1093-1110) de-duplicates on insert via `const4eq` and **zeroes the source**
`CONST4` after taking it.

---

## 6. Evaluation — from a leaf to a record set

### 6.1 `bitmap4seek` — the four seek positions (C4CONST.C:530-695, S4FOX)

```c
/* location = 0 if seek_before, 1 if seek 1st, 2 if seek last, 3 if seek_after */
unsigned long bitmap4seek( BITMAP4 *map, const CONST4 *con, const char location,
                           const unsigned long check, const int doCheck ) ;
```

Returns the record number found, or `ULONG_MAX` for "no such position".

**The constant is converted to key bytes first.** For `map->type != r4str`, and on S4FOX **also for
`r4str`**, the constant goes through `expr4keyConvert(tag->expr, &result, con->len, map->type, tag)`
(C4CONST.C:548, 562) — the same transform the index-maintenance path uses, so collation and the
numeric transforms of [`KEY-COLLATION.md`](KEY-COLLATION.md) apply. `len` comes back from the
conversion and **may differ from `con->len`**; a short character constant stays short, which is what
makes a prefix seek a prefix seek. Non-FOX builds use `con->len` raw for strings (C4CONST.C:580).

**Locations 2 and 3 are implemented by temporarily inverting the tag** (C4CONST.C:584-589):

```c
if ( location > 1 ) tfile4descending( tag, 1 ) ; else tfile4descending( tag, 0 ) ;
tfile4seek( tag, result, len ) ;
tfile4descending( tag, 0 ) ;
```

so that "the last key `<=` v" and "the first key `>` v" are both reached by a *forward* seek on a
reversed view — "This works because we always do forward seeking (we change the descend flag as
required)" (C4CONST.C:601-602). The adjustments per location:

| `location` | Wanted | Post-seek adjustment | Fails (`ULONG_MAX`) when | File:line |
|---|---|---|---|---|
| 0 | last key `<` v | `tfile4skip(-1)` | already at top | C4CONST.C:607-618 |
| 1 | first key `>=` v | none | at eof | C4CONST.C:619-628 |
| 2 | last key `<=` v | none (descending seek landed there) | `doCheck` collision, verified by `u4keycmp` | C4CONST.C:629-651 |
| 3 | first key `>` v | `tfile4skip(+1)`, or `tfile4top` if at eof | skip/top fails — no records beyond | C4CONST.C:652-682 |

These four map cleanly onto what the port already has from step 008 — `SeekAtOrAfter` is
location 1, `SeekAtOrBefore` is location 2, and 0 and 3 are each one step off one of them. The port
does not need `tfile4descending` inversion if its own `SeekAtOrBefore` already answers directly.

**`doCheck` guards a degenerate one-record range.** When both ends of a range seek land on the same
record — the `>3 .AND. <4` case, where the `>3` seek already found key 4 — the second seek returns
`ULONG_MAX` rather than producing a range of one wrong record (C4CONST.C:591-605, 640-650). The
caller passes `doCheck = 1` **only when neither end is inclusive**: `bmf4AndOt` sets it from
`!(map->ge.len || map->le.len)` (m4map.c:1802-1805).

### 6.2 `bitmap4flagRange` — flag everything between two seeks (m4map.c:1487-1755)

Given a start and an end constraint, seek both and flag every record between. **`startCon` is the
*upper* value of the range and `endCon` the lower** — the comment at m4map.c:1509-1536 is explicit,
and the S4FOX walk runs **backwards** (`tfile4skip(tag, -1L)`, m4map.c:1698) from the lower seek up
to the recorded upper record number.

Either constraint may be null, giving a half-open range walked to the end of the tag.

Two mechanisms deserve porting attention:

- **Emptiness is detected by tag position, not by key comparison.** `tfile4position(tag)` returns a
  normalised 0..1 double; `pos1 > pos2` means the upper bound sits below the lower and **no record
  matches** (m4map.c:1666). This is how `4 <= x <= 2` is recognised without comparing keys.
- **The "go around" complement optimisation.** If the range spans more than half the tag
  (`pos2 - pos1 > 0.5`) and no flip is in force, the code flags the **complement** — walking outwards
  from both ends — and returns `doFlip = -1` so the caller inverts (m4map.c:1668-1684). Cost is
  bounded by the smaller of the range and its complement.

  **It fires readily, so a port that omits it will differ in the stored bits.** `K_NUM > 0` on
  `CDXEXPR` — 12 records of 16 — is already stored as its 4-bit complement, and on `CDXDEEP`
  `K_N > -400000` stores 599 records as **one** raw bit. A gate over these dumps must therefore
  compare the *effective* set (the bits read through `isFlip`), not the raw bits; `isFlip` itself is
  a cost decision and not an answer.

`doFlip` is the return value and threads through everything: 0 = nothing flipped yet, 1 = flags mean
what they say, −1 = flags are the complement, −2 = **no matches at all**, −999 = handled, −1000 =
error. A port is free to represent this as a `(bitset, isComplement)` pair, which is what `F4FLAG`
already is.

### 6.3 Per-leaf dispatch — `bitmap4flagGenerate` (m4map.c:2191-2313)

Allocates the bitset on first use (`d4recCount + 1`, `discardOutOfRange = 1`), then dispatches on
the leaf's inherited `andOr`:

| Mode | Condition | Routine |
|---|---|---|
| 1 (and) | `eq` present | `bmf4AndEq` — start = end = `eq`, locations 1 and 2 (m4map.c:1758-1789) |
| 1 (and) | otherwise | `bmf4AndOt` — the range, then each `ne` (m4map.c:1792-1872) |
| 2 (or) | `ne` non-empty | `bmf4OrNe` — flag the value, then `f4flagFlipReturns` (m4map.c:1875-1899) |
| 2 (or) | otherwise | `bmf4OrOt` (m4map.c:1902-2188) |

`bmf4AndOt` chooses locations from which bound is present: `gt` → 3, `ge` → 1, `lt` → 0, `le` → 2
(m4map.c:1807-1829) — exactly §6.1's table. A `-2` return (no matches) becomes
`f4flagSetAll` + `isFlip = 1`, i.e. **the empty set expressed as the complement of everything**
(m4map.c:1834-1839).

A leaf with `noMatch` set short-circuits: for an OR it makes everything succeed, for an AND it
returns having changed nothing (m4map.c:2267-2283). And the tag's `descending` state is saved and
restored around the whole call (m4map.c:2214, 2297-2299).

### 6.4 Branch combination — `bitmap4generate` (m4map.c:2317-2378)

Leaves produce a fresh `F4FLAG`; branches recurse and fold children pairwise with `f4flagAnd` or
`f4flagOr` according to `andOr` (m4map.c:2368-2371). The result of the root is copied byte-wise into
`relate->set` (m4map.c:2465).

The comment at m4map.c:2318-2323 states the semantic difference that §7 turns into a rule:

> For 'and' maps the meaning is that the bitmap marks as true all records which meet at least one
> condition for the map (**but they still need to be checked** against other possibly not
> bitmappable expressions). For an or, if the entry is set, then the record is in the set, **so no
> further checking is needed**.

**Its first clause is wrong and the code beside it is right.** An `.AND.` branch folds its children
with `f4flagAnd` (m4map.c:2368-2371) — an **intersection**, not "at least one condition". What is
true is the parenthesis: an `.AND.` branch's set is a *superset* of the answer, because a conjunct
that could not be optimized was dropped from the branch entirely (§4.1) and only the residual check
removes the records it would have excluded. A port that implemented the comment's first clause
literally would union where it should intersect and return far too many records. Read the fold.

### 6.5 A leaf's set does not depend on the connective above it

Worth stating because it collapses four routines into one. `bitmap4generate` hands every leaf a
**freshly zeroed** `F4FLAG` (m4map.c:2328-2333) before calling `bitmap4flagGenerate`, so the mode
that selects between `bmf4AndEq`/`bmf4AndOt` and `bmf4OrNe`/`bmf4OrOt` is choosing *how to compute*
the leaf's set and never *what it means*. The two families must therefore agree, and inspection says
they do: an exclusion reached through the or path is flagged and inverted, and the same exclusion
reached through the and path is an unbounded range walked with that value dropped. Same records.

**Consequence for §12 item 7.** `bmf4OrOt` was never transcribed, and that is a smaller gap than it
looks: it cannot mean anything a leaf under an `.AND.` does not already mean, so a port needs one
leaf routine and not four. What the untranscribed routine still bears on is *merging* two `.OR.`
leaves on one tag, which is a different question and the one a port should stay cautious about.

### 6.6 An empty leaf under an `.OR.` — an observation, not a settled defect

`bitmap4flagGenerate` short-circuits a leaf whose constraint merging proved empty (`noMatch`), and
the two arms are not symmetric (m4map.c:2267-2283). Under an `.AND.` it returns with the flag left
zeroed, which is the empty set and correct. Under an `.OR.` it makes **every record succeed** — the
comments say so explicitly ("so make all succeed"). Since an `.OR.` branch unions its children and
§6.4 says a set bit in an `.OR.` needs no further checking, a provably-empty disjunct would turn the
whole query into every record.

Whether that is reachable is **not settled here**: `noMatch` is set by `bitmap4collapse` after a
merge proves a conjunction empty, and whether an `.OR.` merge can ever prove one is a question about
`bmf4OrOt`'s family that this document does not answer. No corpus case reaches it. A port following
set logic — an empty disjunct contributes nothing, an empty conjunct empties the branch — cannot
produce the behaviour by accident, so the note is here to stop someone reproducing it deliberately.

---

## 7. Consumption — and the fall-back-to-scan boundary

### 7.1 `f4flagIsSetFlip` — no bitmap means every record is a candidate

```c
static int f4flagIsSetFlip( F4FLAG *flagPtr, const unsigned long r )
{
   if ( flagPtr->flags == 0 ) return 1 ;
   if ( flagPtr->isFlip ) return ! f4flagIsSet( flagPtr, r ) ;
   else                   return   f4flagIsSet( flagPtr, r ) ;
}
```

r4relate.c:82-91. **An unbuilt bitmap answers "yes" for every record**, so the pure-scan fallback
needs no special-casing at any call site. `relation->bitmapsFreed` is set when `log4bitmapDo` fails
(r4relate.c:6045-6046) and the same code path then scans.

**`bitmapsFreed` does not mean "there is no bitmap", and the corpus shows it.** It means bitmapping
*failed* — the memory pool, the record-count guard, or `bitmapDisable`. A filter that simply holds
nothing optimizable leaves it **clear** while `set.flags` stays null: across all 70 corpus filters,
ten optimize nothing at all and **not one sets the flag** (the three `*.query.dump.txt` files).
Correctness on that path is carried entirely by §7.3's per-term tag test, which reads zero for every
term when no tag matched anything. A port that treated the flag as "no bitmap" would be reading a
condition that never occurs.

### 7.2 Skipping — two strategies

`relate4top` (r4relate.c:6259-6301) and the skip paths (r4relate.c:3195, 3688) advance to the next
candidate two different ways:

- **With a selected tag** (`relate->dataTag != 0`): step the *tag* forward with `tfile4dskip` until a
  record whose flag is set. Results come out **in tag order**.
- **Without one**: `f4flagGetNextFlip(&relate->set, …)` jumps directly to the next set bit by
  byte-scanning the bitset (r4relate.c:96-…), then `d4go`. Results come out in **record-number
  order**.

The second is where the speed comes from: whole runs of unset bits are skipped a byte at a time
without reading a record.

### 7.3 `log4true` — the residual filter, and which terms it trusts (r4log.c:707-783)

For each candidate the scan calls `log4true`, which splits the root `.AND.` into its conjuncts and,
per conjunct:

| Conjunct shape | Evaluated? | File:line |
|---|---|---|
| `numParms < 2` — not a comparison | **always** | r4log.c:747-753 |
| `numParms >= 2` and **neither** operand matched a tag | **yes** | r4log.c:770-776 |
| `numParms >= 2` and either operand matched a tag | **no** — trusted to the bitmap | r4log.c:770 |
| any, when `relation->bitmapsFreed` | **yes** | r4log.c:770 |

Returning 0 rejects the record. So the rule, stated once:

> **A term is trusted to the bitmap exactly when `e4isTag` matched one of its operands. Everything
> else is re-evaluated per candidate record.**

That is the fall-back boundary, and it is per *term*, not per query. A filter with three conjuncts of
which one is optimizable seeks one tag and evaluates the other two on every candidate.

**But the code reads the wrong two positions, and is conservative as a result.** The test is
`(infoReportPtr-1)->tag == 0 && (infoReportPtr-2)->tag == 0` (r4log.c:770). `infoReportPtr-1` is
always the right operand's root; **`-2` is the node before it, which is the left operand's root only
when the right operand occupies a single node.** For a term whose left operand matched a tag and
whose right operand is more than one node — `K_TEXT+K_CITY = UPPER("x")`, where the constant subtree
is two nodes — both positions read zero and the term is re-evaluated per candidate even though the
bitmap already answered it. Slower, never wrong, because re-evaluating a term the bitmap got right
removes nothing.

A port should express the rule as §11.5 says — a property of the plan, "trusted iff decomposition
produced a surviving leaf for this term" — which trusts strictly more terms and is safe for the
reason §4.4 gives: the only leaf that could be a superset is a widened one, and the widening is
exact. **ADR-53** records the divergence.

**CodeBase ships the equivalence check we planned.** Under `S4TEST && S4DEBUG`, `log4true` evaluates
a tag-backed term anyway and raises `e4info` if it comes out false (r4log.c:756-768) — the reference's
own version of the full-scan equivalence gate, and independent support for making ours permanent
rather than optional.

### 7.4 `fullyMapped` — when no evaluation is needed at all

`RELATION4.fullyMapped` is "set to true if the bitmap corresponds 1 to 1 with the expression"
(R4RELATE.H:262). It starts at 1 and is cleared:

| Cleared when | File:line |
|---|---|
| the master's selected tag has a filter | r4log.c:511-514 |
| a node is neither connective nor comparison | m4map.c:288-291 |
| an `.AND.` child is not optimizable | m4map.c:117-121, 137 |
| the whole decomposition returns 0 | m4map.c:375-377 |
| bitmap memory is unavailable, or the record-count guard trips | m4map.c:341-356 |

Its one consumer is `relate4count`, which when `fullyMapped == 1` returns `f4flagCount(&relate->set, 1)`
— **the answer without reading a single record** (r4relate.c:6780-6781). The count starts at 1
because flags are indexed by record number and there is no record 0.

---

## 8. Which expression forms are optimizable

Collecting §3 and §4 into one table. "Optimizable" means the term contributes a tag seek; everything
else is evaluated per candidate record (§7.3), which is always correct and never wrong — only slow.

| Form | Optimizable | Why / File:line |
|---|---|---|
| `TAGEXPR <op> constant`, `constant <op> TAGEXPR` | **yes**, for the six operators of §4.3 | m4map.c:152-286 |
| `.AND.` of optimizable terms | **yes** | m4map.c:90-114 |
| `.AND.` with some terms unoptimizable | **partly** — the optimizable ones seek, the rest re-evaluate | m4map.c:117-121 |
| `.OR.` where **every** disjunct is optimizable | **yes** | m4map.c:122-142 |
| `.OR.` where any disjunct is not | **no** — the whole disjunction falls back | m4map.c:127-141 |
| `.NOT.` | **no** — `E4NOT` (row 34) is neither connective nor comparison | m4map.c:288-291 |
| `!=` | yes, as an `ne` list | m4map.c:198-210 |
| a bare logical field or `.T.`/`.F.` | **no** | m4map.c:288-291 |
| `DELETED()`, `EMPTY()`, `IIF()`, `RECNO()`, any function call at the top of a term | **no** | m4map.c:288-291 |
| both sides constant (`1 = 1`) | **no** — neither side matches a tag | m4map.c:163-175 |
| both sides fields | **no** — no constant | m4map.c:163-175 |
| comparison against an `H` (`r4floatBin`) constant | **no** — `E4FLOAT` is not in `e4isConstant` | r4log.c:31-40; §3.2 |
| a term over a tag of type `I`, `Y`, `T` or `r4dateTimeMilli` | **no** | r4log.c:127-139 |
| a term over a **date** or **logical** tag | **yes** — the type gate is a blacklist of four, whatever its comment says | r4log.c:127-139; §3.4 |
| a term inside an `.OR.` written before an unoptimizable one | **no**, and it is never even *tested* — operands are visited backwards and the branch dies at the first failure | m4map.c:107, 127-141; §3.3 |
| a term over a `GENERAL`-collated tag | **no** by default (`useGeneralTagsInRelate == 0`) | r4log.c:115-120 |
| a term over a filtered tag | **no** | r4log.c:144 |
| a term over a `unique`/`candidate` tag | **no** by default (`errDefaultUnique == r4uniqueContinue`) | r4log.c:144; I4TAG.C:3327-3337 |
| a term over a **slave** table's tag | **no** | r4log.c:173-186 |
| `CTOD("…")`/`STOD("…")` as the constant | **yes**, if the subtree touches no field | r4log.c:41-48 |
| a short character constant | **yes**, as a **prefix** range — §4.4 | m4map.c:222-236 |

**The query expression as a whole must be `r4log`**, else `e4relate` / `E80905` (r4relate.c:6042-6043).

---

## 9. Limits and disabling

| Limit | Value | File:line |
|---|---|---|
| `RELATION4.bitmapDisable` | test-only kill switch; sets `bitmapsFreed`, `fullyMapped = 0` | m4map.c:340-346 |
| `CODE4.bitmap_disable` | separate debug flag, read only by `log4true`'s `S4TEST` check | r4log.c:757 |
| record-count guard | disables bitmapping when `d4recCount/16 >= (UINT_MAX-2)/2` | m4map.c:340 |
| `CODE4.bitmapMemory` | lazily created `MEM4` pool, 10 initial / 5 expand; failure disables optimization | m4map.c:348-356 |
| bitset size | `d4recCount + 1` bytes-worth of bits, allocated per leaf evaluation | m4map.c:2219-2226 |
| `S4LOW_MEMORY` retry | on allocation failure, suspends the optimizer's buffer pool and retries once | m4map.c:2229-2249 |

The record-count guard **can never fire on a real DBF**: the threshold is `recCount >= 16 ×
2 147 483 646` ≈ 3.4 × 10¹⁰, while a DBF's record count is a 4-byte field capped at 4 294 967 295.
It is defensive only.

---

## 10. Defects and divergences in the reference

### 10.1 `tagPos = 1` — assignment where comparison was meant

Four sites, m4map.c:216, 243, 251, 278. §4.3 has the analysis. The effective condition is
`constPos == 0`; the intended one is `tagPos == 1 && constPos == 0`. **Consequence if replicated
naively in C#** (where `if (tagPos = 1 && …)` will not compile, so a port must *decide*): a reflected
comparison could be filed under the wrong bound, producing a wrong record set. The port should
implement the intent and gate both operand orders.

### 10.2 `bitmap4can`'s identical `if` arms

m4map.c:369-372 branches on `functionI == E4AND` and calls `bitmap4can` identically in both arms.
Harmless; collapse it.

### 10.3 `const4get`/`const4duplicate` compute `offset` from `bufLen`

C4CONST.C:993 and C4CONST.C:966 set `con->offset = log->bufLen - len` after `const4memAlloc` advanced
`bufPos` by `len`. `const4memAlloc` (C4CONST.C:933-947) only grows `bufLen` when `bufPos + len`
exceeds it, so `bufLen` and `bufPos` diverge as soon as a realloc over-allocates or a second constant
is taken. `const4return` then reads at `buf + offset`. Whether this is latent or actual depends on
`u4allocAgain`'s exact-fit behaviour, which is **not** in this source drop. Recorded in §12 —
**a port should index from its own used-length and not reproduce this arithmetic.**

### 10.4 The `e4isTag` comment does not match its code

r4log.c:144's comment reads "if unique a filter, than cannot bitmap optimize" for a condition that
tests a filter **and** the unique *error action*. §3.4 has what the code does. Per `CLAUDE.md`'s
source-of-truth rule and the precedent of ADR-45, the code is authoritative and the comment is noise.

### 10.5 Vestigial fields and dead defines

`BITMAP4.evaluated`, `finished`, `saveCount`, `flipBase`, `base`; `BITMAP4LEAF`, `BITMAP4AND`. None
is read on the S4FOX path. §1.1, §2.2.

---

## 11. Recommendations for the C# port

### 11.1 Decide the tag-eligibility set explicitly

§3.4 shows CodeBase optimizing only plain, unfiltered, machine-collated `r4double`/`r4charBin`/
`r4memoBin` tags. Two defensible positions:

- **Replicate.** The refusals are correctness-motivated: the integer/currency/datetime refusal is
  because the constant cannot be converted to the tag's key domain, and the collation refusal is
  because the residual comparison in `log4true` uses `memcmp` order. Replicating means the port's
  record sets match CodeBase's for the same query, which is the only thing a compatibility gate can
  check.
- **Extend.** The port already has typed seek by value for `int`, `double`, `DateOnly` and collated
  tags, gated on 3364 keys (step 008) — so the stated reason for the type refusal ("the constant
  cannot be converted to an int") **does not hold here**. Extending makes the optimizer useful on
  exactly the tags this port is best at, at the cost of having no reference answer to compare with.

This is an ADR, not a spec decision. Note that it interacts with the equivalence gate: extending is
safe *because* the full-scan gate is the real check, and unlike CodeBase's, ours is not behind a
debug flag.

**Decided by ADR-52: both, selected by `CodeBaseEngine.QueryTagScope`.** `Compatible` (the default)
replicates, which is what keeps the reference usable as an oracle at three of the four gate depths;
`AllSeekable` widens the type and collation gates. The **filter and unique gates are never widened**
— a filtered tag indexes a subset of the table and a unique tag stores one key per duplicate group,
so both refusals are structural rather than conservative.

### 11.2 Model the canonical leaf as a type

§5.3's invariants make a leaf one of: `Eq(value)`, `NotIn(values)`, or
`Range(lower?, upper?, lowerInclusive, upperInclusive)`. Six nullable `CONST4` fields with six
pairwise exclusions is a C idiom; a discriminated union removes every one of the `E4ANALYZE`
assertions by construction.

### 11.3 Keep the two-strategy skip

§7.2's tag-order and record-order paths are not an optimization detail — they determine the **order
records come out in**, which is user-visible. Both are needed.

### 11.4 Reuse step 008's seek surface

§6.1's four locations are already the port's `SeekAtOrAfter` / `SeekAtOrBefore` plus a one-step
adjustment, and the port answers them directly rather than by inverting the tag. `doCheck` (§6.1) is
the one piece with no equivalent yet.

### 11.5 Make the residual filter's trust rule explicit

§7.3's rule — trust a term iff a tag matched an operand — should be a property of the compiled query
plan, not an inference from a null check on a parallel array. It is what makes the whole thing
correct, and it is currently expressed in the reference as `(infoReportPtr-1)->tag == 0 &&
(infoReportPtr-2)->tag == 0`.

### 11.6 Keep the equivalence gate always on

`CLAUDE.md`: a wrong record set is far worse than a slow one. The reference agrees and only enforces
it under `S4TEST && S4DEBUG` (§7.3). `PORTING-PLAN.md` §5's `QUERY` gate already says always; §7.3
is independent support.

---

## 12. Open questions / risks for the C# port

1. **The `tagPos = 1` intent is inferred, not witnessed.** §4.3 and §10.1. The reading is supported
   by the four `AS Feb 19/13` comments, but no corpus case exercises a reflected comparison inside a
   larger expression, which is the only place the buggy and intended conditions differ. **Wants a
   generator case**: `.AND.`-nested `4 > K_NUM` and `K_NUM < 4`, checked to return the same records.
   **And it can be witnessed rather than inferred**, because `RELATION4.bitmapDisable` is a plain
   struct member and setting it makes `bitmap4can` return 0 at once (m4map.c:341), after which
   `log4true`'s `|| bitmapsFreed` arm re-evaluates every conjunct per record. Running the same query
   twice — optimized and disabled — asks the reference for its optimized answer and for **its own
   scan truth**, and where those differ on this case the defect has demonstrated itself. Planned as
   step 011's sub-step 1 (`dev/011-query-optimizer/DESIGN.md` Decision 12).
   **RUN 2026-09-04, and the defect is real.** `net/corpus/CDXEXPR.query.dump.txt` holds both
   readings of `K_INT > 0 .AND. 4 > K_NUM`: optimized it returns records **{3, 9, 11, 12, 14, 15}**
   and under `bitmapDisable` it returns **{5, 6, 8}** — *disjoint*, so the bound was **inverted and
   not merely loosened**. The mechanism is visible in the dump: the leaf carries the same raw bits
   as the correct `K_NUM < 4` leaf but with `isFlip` set, i.e. the complement. Written tag-first
   (`K_INT > 0 .AND. K_NUM < 4`) and un-nested (`4 > K_NUM`) the reference is correct, which is what
   confines the defect to the nested reflected form exactly as §4.3 predicted. It is the **only**
   filter of the corpus's 70 where the reference contradicts itself. R12's most concrete instance,
   demonstrated in the reference rather than argued from four comments.
2. ~~**The partial-widening asymmetry (§4.3) is unexplained.**~~ **RESOLVED 2026-09-04, and it is
   not a defect.** Reading the four reflected branches together (m4map.c:211-283): the two paths that
   widen are `>` tag-left and `<` constant-left, and those are **exactly the two that write the `gt`
   slot — the only two that write it.** The other four write `lt`, `ge` or `le`, where a short
   constant needs no adjustment. The asymmetry is complete rather than partial. See §4.4.
3. ~~**`E4FLOAT` absent from `e4isConstant` (§3.2) predicts that no `H`-field comparison
   optimizes.**~~ **WITNESSED 2026-09-04.** `K_FLT > 0` builds **no bitset at all** and matches no
   tag on any node (`CDXEXPR.query.dump.txt`), even though `e4isTag` does not refuse an `r4floatBin`
   tag — so the refusal does come from the constant side, as §3.2 read it.
4. **`u4allocAgain` is not in this source drop**, so §10.3's `bufLen`-vs-`bufPos` question cannot be
   settled from the source. It does not matter if the port indexes from used length, which it should.
5. **`tfile4position`'s exact normalisation** (§6.2) is what the `> 0.5` go-around test compares
   against; it is read here as a 0..1 fraction of the tag, but the function body was not transcribed.
   The go-around is an optimisation, so a port may omit it entirely and lose only speed — but if it
   is implemented, the threshold semantics must match or the *complement flag* is wrong, which is a
   wrong record set.
6. **Multi-table order selection is out of v1 scope but shares this module.**
   `log4determineEvaluationOrder` (r4log.c:534-611) reorders an `.AND.`'s conjuncts so the term
   touching the fewest data files evaluates first. With one table it is inert. Named here so that
   nobody porting `relate4createSlave` later assumes it is absent.
7. **`bmf4OrOt` (m4map.c:1902-2188) was read for its shape, not transcribed line by line.** It is the
   largest single routine in the module and handles OR ranges with their own position/flip logic. Its
   rules are believed to mirror `bmf4AndOt` with the flip polarity inverted; **verify before
   implementing OR ranges.** **Narrowed 2026-09-04:** §6.5 shows it cannot *mean* anything different
   from the and-side routine, because every leaf starts from a zeroed bitmap — so evaluating a leaf
   needs one routine whichever connective is above it, and this item bears only on **merging** two
   `.OR.` leaves on one tag.
8. ~~**No corpus case gates any of this yet**~~ — **as of 2026-09-04 seventy filters do**, across
   `CDXEXPR` (43), `CDXBASE` (15) and `CDXDEEP` (12), each recorded at five depths including the
   reference's own scan truth. **Visual FoxPro cannot supply one.** ADR-44's oracle
   witnesses a value VFP computes and the bytes it writes; the optimizer produces neither, and three
   of the four things worth recording here (`infoReport[i].tag`, `fullyMapped`, `relate->set`) are
   CodeBase's own internal state. The fourth, a record sequence, VFP would answer under *its* prefix
   `=` semantics, which ADR-45 established run the opposite way from CodeBase's — so a VFP result set
   is not this port's expected answer. CodeBase is the only oracle for this module, and item 1 above
   is how it becomes two. `CDXEXPR` (step 010) gates expression *evaluation*;
   nothing gates decomposition, merging, or the record sets. The `QUERY` gate in `PORTING-PLAN.md` §5
   is the plan, and §7.3 shows the reference validating itself the same way.
