# CodeBase xBase Expression Engine — Implementation Specification (S4FOX / VFP-compatible build)

Scope: the expression compiler/evaluator in the CodeBase C library (Sequiter), as needed to port it
to C#. Primary sources: `e4parse.c` (parser), `e4functi.c` (function table + operator kernels),
`E4EXPR.C` (evaluator, key conversion), `e4expr.h` (constants/structs), `d4data.h` (EXPR4/E4INFO),
`d4defs.h` (type codes), `i4conv.c` (FOX key transforms), `D4DATE.C` (date kernel),
`E4CALC.C`/`E4CALC_2.C` (named calculations). The build configuration analyzed is **S4FOX**
(stand-alone; S4CLIENT paths are ignored). S4CLIPPER/S4MDX divergences are noted briefly in §11.

Citation convention: `(FILE:line)` refers to the line in
`/mnt/d/develop/components/Codebase.Net/original/source`.

---

## 1. Architecture overview

CodeBase expressions are **compiled once** into a linear postfix program and then **executed by a
tiny stack machine**; the same compiled form is used for filter evaluation, general evaluation
(`expr4vary`) and index-key generation (`expr4key`).

Pipeline:

1. `expr4parseLow(DATA4*, const char* source, TAG4FILE* tag)` — operator-precedence parse of the
   source text into a postfix array of `E4INFO` records + a constants blob (E4PARSE working state)
   (e4parse.c:1057-1162).
2. `e4massage(E4PARSE*)` — type checking, operator-overload resolution, implicit conversions,
   length inference, result-buffer layout (`resultPos` assignment), and enforcement of the
   constant-parameter restrictions (e4parse.c:152-980).
3. Runtime — `expr4vary` walks the `E4INFO` array from 0..infoN-1 calling each entry's function
   pointer (E4EXPR.C:1186-1190). Each kernel manipulates:
   * `char **expr4` — global operand stack of pointers (max `E4MAX_STACK_ENTRIES` = **65** entries,
     e4expr.h:19; declared per call as `char *pointers[E4MAX_STACK_ENTRIES]`, E4EXPR.C:1165),
   * `char *expr4buf` — the expression work buffer (`EXPR4.exprWorkBuf`), where every node has a
     pre-assigned byte offset `resultPos` (e4parse.c:914-931),
   * `expr4constants` — pointer to the constants blob,
   * `expr4infoPtr` — the currently executing `E4INFO`,
   * `expr4ptr` — the current `EXPR4` (E4EXPR.C:22-25, e4expr.h:129-136).
4. The final result is `pointers[0]` after the walk; its length is `EXPR4.len` and its type
   `EXPR4.type` (E4EXPR.C:1194-1202).

Execution is protected by a global critical section (one expression evaluates at a time per
process) because the stack/registers are globals (E4EXPR.C:87-135). A C# port should make this
state per-evaluation instead.

`expr4execute(expr, pos, &result)` evaluates only the sub-expression that *ends* at info index
`pos` — it starts at `pos - info[pos].numEntries + 1` (E4EXPR.C:184-226). `numEntries` is 1 + the
sum of the parameters' `numEntries` (e4parse.c:941-946), i.e. the size of the postfix subtree; this
is what the relate/query optimizer uses to slice expressions.

---

## 2. Core data structures

### 2.1 E4INFO — one compiled postfix node (d4data.h:3587-3610)

Members must not be reordered (memcmp used in `e4isTag`, d4data.h:3585-3586, 3600-3602).

| Member | C type | Meaning |
|---|---|---|
| `fieldNo` | short | 1-based field number in the DATA4, for re-binding (d4data.h:3589) |
| `fieldPtr` | FIELD4* | bound field, 0 if not a field node (d4data.h:3590) |
| `tagPtr` | TAG4FILE* | set for ASCEND/DESCEND/KEYEQUAL nodes when parsing a tag expression (d4data.h:3592, e4parse.c:334-337, 375-378) |
| `localData` | int | true if `fieldPtr` belongs to the expression's own DATA4 (d4data.h:3594, e4parse.c:2475-2476) |
| `p1` | char* | multipurpose: `&record` pointer-pointer for field/DEL/DELETED nodes (e4parse.c:2478-2483, 516-521, 754-762); DATA4FILE* for RECCOUNT (e4parse.c:736-737); EXPR4CALC*/TOTAL4* for calc/total (e4parse.c:2090-2091); tie-break flag (0/1) for `<`/`>` on equal-prefix strings (e4parse.c:801-822) |
| `len` | int | result length in bytes (e4parse.c:940) |
| `numEntries` | int | postfix subtree size incl. this node (e4parse.c:941-946) |
| `numParms` | int | actual parameter count (variable for .AND./.OR., e4parse.c:1022-1024, 1293-1298) |
| `resultPos` | int | byte offset of this node's result in `exprWorkBuf` (e4parse.c:914-916) |
| `i1` | int | multipurpose: constants-blob offset for constants (e4parse.c:996), field offset in record for fields (e4parse.c:2485), compare length for comparisons (e4parse.c:791), decimals for STR (e4parse.c:1444), SUBSTR/LEFT/RIGHT offset, PADL/PADR pad count (e4parse.c:570-572), DATETIME time-of-day in ms (e4parse.c:1820), IIF true-branch length (e4parse.c:471) |
| `i2` | int | IIF false-branch length (e4parse.c:472); runtime: trimmed length when `varLength` (d4data.h:3603) |
| `varLength` | Bool5 | true → use `i2` (actual trimmed length) instead of `len` (d4data.h:3604); macro `expr4infoPtrLen` (e4functi.c:313) |
| `functionI` | int | index into `v4functions[]` (d4data.h:3605) |
| `isNull` | Bool5 | S4FOX/CLIENT builds only: runtime null flag for the node (d4data.h:3606-3608) |
| `function` | S4OPERATOR* | resolved function pointer = `v4functions[functionI].functionPtr` (d4data.h:3609, e4parse.c:962-966) |

### 2.2 EXPR4 — a compiled expression (d4data.h:3614-3641)

| Member | Meaning |
|---|---|
| `info`, `infoN` | postfix array and its count (d4data.h:3616-3617) |
| `source` | original text (copy stored after constants; d4data.h:3618, e4parse.c:1144-1153) |
| `constants` | constants blob (d4data.h:3619) |
| `len`, `type` | final result length and type code (d4data.h:3620, set e4parse.c:976-977) |
| `tagPtr`, `data`, `dataFile`, `codeBase` | binding context (d4data.h:3621-3624) |
| `lenEval` | required size of work buffer (d4data.h:3626, e4parse.c:937-938, +1 at 968) |
| `numParms` | parameter stack positions used (d4data.h:3627) |
| `hasTrim` | **special case for key evaluation** — set when TRIM/LTRIM/ALLTRIM appears (d4data.h:3628, e4parse.c:553-557); see §9.4 |
| `keyDec`, `keyLen` | CLIPPER-only key length/decimals (d4data.h:3630, e4parse.c:1336-1339, 2460-2463) |
| `exprBufLen`, `exprWorkBuf` | work buffer (d4data.h:3632-3636) |
| `hasMemoField` | expression touches a memo field (d4data.h:3637-3640, e4parse.c:436) |

The whole compiled expression is one allocation: `EXPR4` header + `E4INFO[infoN]` + constants +
source copy (e4parse.c:1126-1153).

### 2.3 E4FUNCTIONS — a function-table row (e4expr.h:73-87)

```
void (*functionPtr)(void); char *name; short code; unsigned char nameLen;
char priority; char returnType; signed char numParms /* -1 = variadic */;
char type[E4MAX_PARMS /* 4 */];
```
(e4expr.h:18, 73-87). `code` groups overloads of the same source-level operator: during massage the
engine walks forward (`info->functionI++`) through consecutive rows **with the same `code`** until
one's parameter types match (e4parse.c:207-293). Row order in the table is therefore semantic.

### 2.4 Type codes (result/parameter types) (d4defs.h:2775-2821)

| Code | Char | Meaning / runtime representation |
|---|---|---|
| `r4bin`/`r4double` | `'B'` | binary/double field (note: **'B' is double**, d4defs.h:2774-2775) |
| `r4str` | `'C'` | fixed-length byte string (d4defs.h:2776) |
| `r4dateDoub` | `'d'` | date as C `double` Julian day number (d4defs.h:2777) |
| `r4date` | `'D'` | 8-byte `CCYYMMDD` text (d4defs.h:2778) |
| `r4float` | `'F'` | numeric (text) field, treated as `r4num` (d4defs.h:2779, e4parse.c:2283-2285) |
| `r4floatBin` | `'H'` | 4-byte IEEE float (d4defs.h:2781) |
| `r4gen` | `'G'` | general (OLE) — treated as memo (d4defs.h:2782, e4parse.c:2296-2298) |
| `r4int` | `'I'` | 4-byte little-endian int (d4defs.h:2783) |
| `r4log` | `'L'` | logical; runtime value is a C `int` 0/1 (d4defs.h:2784; e.g. e4functi.c:1035-1036) |
| `r4memo` | `'M'` | memo (d4defs.h:2785) |
| `r4numDoub` | `'n'` | numeric as C `double` (d4defs.h:2786) |
| `r4num` | `'N'` | numeric as text of field length (d4defs.h:2787) |
| `r5wstrLen` | `'O'` | wide string + trailing length word (d4defs.h:2795) |
| `r5ui4`/`r5i2`/`r5ui2` | `'P'/'Q'/'R'` | 4-byte uint / 2-byte int / 2-byte uint (d4defs.h:2797-2799) |
| `r4dateTime` | `'T'` | 2×32-bit little-endian: Julian date, ms-since-midnight (d4defs.h:2804; e4functi.c:913-917) |
| `r5wstr` | `'W'` | UTF-16 wide string (d4defs.h:2807) |
| `r4currency` | `'Y'` | 8-byte scaled int (×10⁻⁴), CURRENCY4 (d4defs.h:2810) |
| `r5i8` | `'1'` | 8-byte int (d4defs.h:2814) |
| `r5dbDate`/`r5dbTime`/`r5dbTimeStamp` | `'2'/'3'/'4'` | OLE-DB structs, 6/6/16 bytes (d4defs.h:2815-2817, e4parse.c:713-722) |
| `r4dateTimeMilli` | `'7'` | dateTime with ms precision (d4defs.h:2821) |

Runtime buffer sizes assigned by massage: doubles/dates/dateTime/currency-as-double → 8;
`r4log` → `sizeof(int)`; `r4int`/`r5ui4`/`r5i2`/`r5ui2` → 4; `r5i8` → 8; `r5dbDate/Time` → 6;
`r5dbTimeStamp` → 16; `r4floatBin` → 4; `r4num` → field length (e4parse.c:696-747, 826).

---

## 3. Lexical grammar

`expr4parseValue` dispatches on the first non-blank character (e4parse.c:2518-2574). Whitespace =
chars ≤ `' '` (e4parse.c:2531, 1187).

* **String constants**: delimited by `'…'`, `"…"`, or `[…]` (e4parse.c:2549-2553, 2166-2171).
  No escape mechanism; scanner searches for the matching close delimiter (e4parse.c:2174).
  Unterminated string is an error `e4unterminated` (e4parse.c:2175-2184). Result: an `E4STRING`
  constant node; text copied verbatim into the constants blob (e4parse.c:2188-2189).
* **Numeric constants**: `[0-9 . + -]` sequences parsed by `c4atod` into an 8-byte double stored
  as an `E4DOUBLE` constant (e4parse.c:2558-2565, 2195-2248). A leading `+`/`-` is consumed as part
  of the literal (e4parse.c:2558). Disambiguation: a `.` followed by `AND`/`OR`/`NOT.` stops the
  number (e4parse.c:2218-2221); a digit-run followed by a letter is re-parsed as a data-alias name
  (e4parse.c:2222-2242). **There is no unary minus operator for non-literals** — `-` binds only as
  binary subtract/concat [inference from e4parse.c:2558-2565 and operator table].
* **Logical constants / prefix not**: `.TRUE.`, `.T.`, `.FALSE.`, `.F.` are niladic table entries;
  `.NOT.` and `!` are prefix operators handled specially by `expr4parseValueLogical`
  (e4parse.c:2540-2548, 2124-2149; table rows 30-35, e4functi.c:81-86).
* **Date/datetime literals `{...}`: NOT supported.** `expr4parseValue` has no `{` case
  (e4parse.c:2535-2573); dates are produced by `CTOD()`, `STOD()`, `DATE()`, `DATETIME()`.
* **Identifiers**: characters accepted by `u4nameChar` (letters/digits/underscore) form a name;
  if followed (after optional blanks) by `(` it is a function call, else a field reference
  (e4parse.c:2492-2514).
* **Function names** are matched case-insensitively, max 8 characters (lookup buffer `char [9]`,
  e4parse.c:1224-1233; e4lookup e4parse.c:120-128).
* **Field references**: `FIELD`, `ALIAS->FIELD` (e4parse.c:2371-2379), and — **S4FOX only** —
  `ALIAS.FIELD` (e4parse.c:2357-2369). Alias is resolved via `tran4dataName` among open DATA4s
  (e4parse.c:2252-2272). Field names > 10 chars are an error unless the DBF supports long field
  names (e4parse.c:2404-2417). In a *tag* expression an alias other than the indexed table itself
  is an error `e4tagExpr` (e4parse.c:2385-2387).

---

## 4. Syntactic grammar and precedence

```
expression := value { binary-op value }        (e4parse.c:1304-1400)
value      := '(' expression ')'               (e4parse.c:2537-2539, 2097-2120)
            | '.NOT.' value | '!' value        (e4parse.c:2137-2144)
            | .T. | .F. | .TRUE. | .FALSE.
            | string | number
            | name '(' [expression {',' expression}] ')'   (e4parse.c:1858-1918, 1984-2093)
            | [alias ('->'|'.')] fieldname
```

Parsing is the classic **shunting-yard**: operators are pushed on an op stack; before pushing, any
stacked operator with `priority >= new.priority` is popped to the postfix output (left
associativity; e4parse.c:1364-1394). `(` and `,` act as stack barriers (E4L_BRACKET/E4COMMA,
e4expr.h:302-306). Consecutive identical `.AND.`/`.OR.` operators are merged into one variadic
node (`E4ANOTHER_PARM` marker) to help the relation optimizer (e4parse.c:1374-1381; kernels loop
over `numParms`, e4functi.c:432-440, 2340-2351).

**Precedence (higher binds tighter)** — the `priority` column of `v4functions` (e4functi.c:81-177):

| Priority | Operators |
|---|---|
| 9 | `^`, `**` (power) (e4functi.c:169-170) |
| 8 | `*`, `/` (e4functi.c:173-176) |
| 7 | `+`, `-` (add/subtract/concatenate) (e4functi.c:95-108) |
| 6 | `=`, `<>`, `#`, `>=`, `=>`, `<=`, `=<`, `>`, `<`, `$` (e4functi.c:111-177) |
| 5 | `.NOT.`, `!` (prefix) (e4functi.c:85-86) |
| 4 | `.AND.` (e4functi.c:93) |
| 3 | `.OR.` (e4functi.c:92) |
| 0 | named functions (call syntax) |

Operator token lookup scans table rows `E4FIRST_OPERATOR`(36)..`E4LAST_OPERATOR`(106)
(e4parse.c:1195, e4expr.h:153-154); logical-constant lookup rows `E4FIRST_LOG`(30)..`E4LAST_LOG`(35)
(e4parse.c:2543, e4expr.h:151-152); function lookup starts at `E4FIRST_FUNCTION`(105)
(e4parse.c:1954, e4expr.h:157). If a name is not in the table it is looked up among registered
**calcs/totals** (`expr4calcLookup`) before failing with `e4unrecFunction` (e4parse.c:1922-1980).

**Operator meaning by operand type** (overload resolution, §6):

* `+` : numeric add; date+num / num+date (blank date → blank result, e4functi.c:394-428); string
  concatenation; float add.
* `-` : numeric subtract; date−date → number of days; date−num → date (blank-date short-circuit,
  e4functi.c:2789-2824); **string `-` = trim-concatenate**: trailing blanks of the left operand are
  moved to the end of the combined string (`e4concatTwo` → `e4concatSpecial(' ')`,
  e4functi.c:444-531).
* `$` : substring containment `a $ b` — true if the left string occurs anywhere in the right
  string; scan compares `aLen` bytes at every offset 0..(bLen−aLen) (e4functi.c:535-564). Uses the
  trimmed length of `a` when `a` is a TRIM result (e4functi.c:541-545).
* `^` / `**` : `pow(a,b)` on doubles (e4functi.c:2447-2466).

---

## 5. The complete built-in function table `v4functions[193]`

Defined at e4functi.c:32-301; count `EXPR4NUM_FUNCTIONS` = **193** (e4expr.h:142). Index shown is
the 0-based array index (= `E4INFO.functionI`); the source comments number rows 1-based. Columns:
code / name (0 = internal, selected by parser/massage, not by name) / prio / return / parms →
parameter types. All citations are the row's line in e4functi.c.

**Field loaders (0-26) — emitted by the parser for field references** (e4functi.c:35-74)

| # | Handler | code | Return | Emitted for | Cite |
|---|---|---|---|---|---|
| 0 | e4fieldAdd | 0 | r4str | `E4FIELD_STR` char field (pointer into record, no copy) | :35 |
| 1 | e4fieldAdd | 0 | r5wstr | `E4FIELD_WSTR` | :36 |
| 2 | e4fieldCopy | 0 | r5wstrLen | `E4FIELD_WSTR_LEN` (copies into buffer) | :37 |
| 3 | e4fieldCopy | 1 | r4str | `E4FIELD_STR_CAT` char field copied to buffer (operand of concat/trim/etc., e4parse.c:358-364) | :38 |
| 4 | e4fieldCopy | 2 | r5wstr | `E4FIELD_WSTR_CAT` | :39 |
| 5 | e4fieldLog | 3 | r4log | `E4FIELD_LOG`: char in {Y,y,T,t} → 1 else 0 (e4functi.c:1085-1090) | :40 |
| 6 | e4fieldDateD | 4 | r4dateDoub | `E4FIELD_DATE_D`: `date4long(text)` → double (e4functi.c:1041-1065) | :41 |
| 7 | e4fieldAdd | 5 | r4date | `E4FIELD_DATE_S` raw 8-char date | :42 |
| 8 | e4fieldNumD | 6 | r4numDoub | `E4FIELD_NUM_D`: `c4atod(text,len)` (e4functi.c:1327-1354) | :43 |
| 9 | e4fieldAdd | 7 | r4num | `E4FIELD_NUM_S` raw numeric text | :44 |
| 10 | e4fieldAdd | 440 | r4currency | `E4FIELD_CUR` | :45 |
| 11 | e4fieldDoubD | 441 | r4numDoub | `E4FIELD_DOUB` 8-byte double field (Intel order; e4functi.c:1360-1400) | :48 |
| 12 | e4fieldAdd | 442 | r4int | `E4FIELD_INT` | :49 |
| 13 | e4fieldAdd | 443 | r5ui4 | `E4FIELD_UNS_INT` | :50 |
| 14 | e4fieldAdd | 444 | r5i2 | `E4FIELD_SHORT` | :51 |
| 15 | e4fieldAdd | 445 | r5ui2 | `E4FIELD_UNS_SHORT` | :52 |
| 16 | e4fieldAdd | 446 | r4dateTime | `E4FIELD_DTTIME` | :53 |
| 17 | e4fieldAdd | 447 | r4dateTimeMilli | `E4FIELD_DTTIME_MILLI` | :54 |
| 18 | e4fieldIntD | 448 | r4numDoub | `E4FIELD_INT_D` int→double (e4functi.c:1283-1323) | :55 |
| 19 | e4fieldCurD | 449 | r4numDoub | `E4FIELD_CUR_D` currency→double via string round-trip for last-digit fidelity (e4functi.c:1146-1193) | :56 |
| 20 | e4fieldAdd | 450 | r5i8 | `E4FIELD_I8` | :57 |
| 21 | e4fieldAdd | 451 | r5dbDate | | :58 |
| 22 | e4fieldAdd | 452 | r5dbTime | | :59 |
| 23 | e4fieldAdd | 453 | r5dbTimeStamp | | :60 |
| 24 | e4fieldAdd | 454 | r4floatBin | `E4FIELD_BINFLOAT` | :62 |
| 25 | e4fieldIntF | 456 | r4float | `E4FIELD_INT_F` int→float (e4functi.c:1198-1237) | :65 |
| 26 | e4fieldMemo | 7 | r4str | `E4FIELD_MEMO` memo loaded, truncated/zero-padded to `len` (e4functi.c:1098-1142) | :73 |

**Constant loaders (27-29)** (e4functi.c:76-78)

| # | Handler | code | Return | Meaning | Cite |
|---|---|---|---|---|---|
| 27 | e4copyConstant | 8 | r4numDoub | `E4DOUBLE` numeric literal (copies from constants blob at `i1`) | :76 |
| 28 | e4copyConstant | 9 | r4str | `E4STRING` string literal | :77 |
| 29 | e4copyToFloat | 10 | r4floatBin | `E4FLOAT` double literal narrowed to float (e4functi.c:581-593) | :78 |

**Logical constants & operators (30-37)** (e4functi.c:81-93)

| # | Handler | Name | code | prio | Return | Parms | Cite |
|---|---|---|---|---|---|---|---|
| 30 | expr4trueFunction | `.TRUE.` | 14 | 0 | r4log | 0 | :81 |
| 31 | expr4trueFunction | `.T.` | 14 | 0 | r4log | 0 | :82 |
| 32 | e4false | `.FALSE.` | 16 | 0 | r4log | 0 | :83 |
| 33 | e4false | `.F.` | 16 | 0 | r4log | 0 | :84 |
| 34 | e4not | `.NOT.` | 18 | 5 | r4log | 1 (r4log) | :85 |
| 35 | e4not | `!` | 18 | 5 | r4log | 1 (r4log) | :86 |
| 36 | e4or | `.OR.` | 20 | 3 | r4log | −1 variadic (r4log…) | :92 |
| 37 | e4and | `.AND.` | 22 | 4 | r4log | −1 variadic (r4log…) | :93 |

**`+` overloads (38-45), `-` overloads (46-50)** (e4functi.c:95-108)

| # | Handler | Name | code | prio | Return | Parm types | Cite |
|---|---|---|---|---|---|---|---|
| 38 | e4parmRemove | `+` | 25 | 7 | r4str | r4str, r4str (records adjacent → just pop) | :95 |
| 39 | e4parmRemove | — | 25 | 7 | r5wstr | r5wstr, r5wstr | :96 |
| 40 | e4wstrLenCon | — | 25 | 7 | r5wstrLen | r5wstrLen ×2 (e4functi.c:506-524) | :97 |
| 41 | e4concatTrim | — | 25 | 7 | r4str | r4str ×2 — `E4CONCAT_TRIM`, used when an operand comes from TRIM/memo/calc: left operand's trailing NULs removed, right part shifted up, tail NUL-filled (e4functi.c:444-502) | :98 |
| 42 | e4add | — | 25 | 7 | r4numDoub | r4numDoub ×2 | :99 |
| 43 | e4addDate | — | 25 | 7 | r4dateDoub | r4numDoub, r4dateDoub | :100 |
| 44 | e4addDate | — | 25 | 7 | r4dateDoub | r4dateDoub, r4numDoub | :101 |
| 45 | e4addFloat | — | 25 | 7 | r4floatBin | r4floatBin ×2 | :102 |
| 46 | e4concatTwo | `-` | 30 | 7 | r4str | r4str ×2 — trailing blanks of left moved to end (e4functi.c:528-531, 444-495) | :104 |
| 47 | e4sub | — | 30 | 7 | r4numDoub | r4numDoub ×2 | :105 |
| 48 | e4subDate | — | 30 | 7 | r4numDoub | r4dateDoub ×2 (days difference) | :106 |
| 49 | e4subDate | — | 30 | 7 | r4dateDoub | r4dateDoub, r4numDoub | :107 |
| 50 | e4subFloat | — | 30 | 7 | r4floatBin | r4floatBin ×2 | :108 |

**Comparison overloads (51-97)** — all return r4log, prio 6, 2 parms (e4functi.c:111-162).
Pattern per relation: str, [log], numDoub, dateDoub, currency, dateTime, dateTimeMilli, floatBin.

| # range | Name | code | Handlers in row order | Cite |
|---|---|---|---|---|
| 51-59 | `#` / `<>` (both spellings rows 51,52) | 50 | e4notEqual(str), e4notEqual(str), e4notEqual(numDoub), e4notEqual(dateDoub), e4notEqual(log), e4notEqualCur, e4notEqualDtTime, e4notEqualDtTime(milli), e4notEqual(floatBin) | :111-119 |
| 60-67 | `>=` / `=>` | 60 | e4greaterEq(str)×2, e4greaterEqDoub(numDoub), e4greaterEqDoub(dateDoub), e4greaterEqCur, e4greaterEqDtTime×2, e4greaterEqFloat | :121-128 |
| 68-75 | `<=` / `=<` | 70 | e4lessEq(str)×2, e4lessEqDoub×2, e4lessEqCur, e4lessEqDtTime×2, e4lessEqFloat | :130-137 |
| 76-83 | `=` | 40 | e4equal(str), e4equal(log), e4equal(numDoub), e4equal(dateDoub), e4equalCur, e4equalDtTime×2, e4equal(floatBin) | :139-146 |
| 84-90 | `>` | 80 | e4greater(str), e4greaterDoub×2, e4greaterCur, e4greaterDtTime×2, e4greaterFloat | :148-154 |
| 91-97 | `<` | 90 | e4less(str), e4lessDoub×2, e4lessCur, e4lessDtTime×2, e4lessFloat | :156-162 |

**Arithmetic operators (98-104)** (e4functi.c:165-177)

| # | Handler | Name | code | prio | Return | Parms | Cite |
|---|---|---|---|---|---|---|---|
| 98 | e4power | `^` | 100 | 9 | r4numDoub | r4numDoub ×2 | :169 |
| 99 | e4power | `**` | 100 | 9 | r4numDoub | r4numDoub ×2 | :170 |
| 100 | e4multiply | `*` | 102 | 8 | r4numDoub | r4numDoub ×2 | :173 |
| 101 | e4multiplyFlt | `*` | 102 | 8 | r4floatBin | r4floatBin ×2 | :174 |
| 102 | e4divide | `/` | 105 | 8 | r4numDoub | r4numDoub ×2 — **division by zero yields 0.0, not error** (e4functi.c:753-756) | :175 |
| 103 | e4divideFlt | `/` | 105 | 8 | r4floatBin | r4floatBin ×2 (same zero rule, e4functi.c:794-797) | :176 |
| 104 | e4contain | `$` | 110 | 6 | r4log | r4str ×2 | :177 |

**Named functions (105-191)** (e4functi.c:181-299)

| # | Name | code | Return | Parms | Semantics / notes | Cite |
|---|---|---|---|---|---|---|
| 105 | CHR | 120 | r4str(1) | 1: r4numDoub | byte value of number (e4functi.c:941-947) | :181 |
| 106 | DEL | 130 | r4str(1) | 0 | deletion-flag char of current record (e4functi.c:692-697) | :182 |
| 107 | STR | 140 | r4str | 1..3: r4numDoub[,len[,dec]] | see §7.1 | :183 |
| 108 | STR | 140 | r4str | 1: r5wstr | wide→ANSI narrow copy, out len = in len/2 (e4parse.c:1421-1426; e4functi.c:2694-2715) | :184 |
| 109 | STRZERO | 145 | r4str | 1..3 like STR | STR with leading blanks replaced by '0' (e4functi.c:2539-2577) | :186 |
| 110 | SUBSTR | 150 | r4str | 1: r4str (+const 2nd/3rd) | see §7.2 | :187 |
| 111 | TIME | 160 | r4str(8) | 0 | "HH:MM:SS" local time (D4DATE.C:703-742; len 8, e4parse.c:550-552) | :188 |
| 112 | UPPER | 170 | r4str | 1: r4str | uppercase; under CDX + codepage 1252/1250 uses Windows `CharUpper` (ANSI) else `c4upper` (e4functi.c:2878-2911) | :189 |
| 113 | DTOS | 180 | r4str(8) | 1: r4date | copy of CCYYMMDD; **blank date "&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;0" → 8 blanks** (e4functi.c:625-645; len=8, e4parse.c:477-479) | :190 |
| 114 | (DTOS) | 180 | r4str(8) | 1: r4dateDoub | `date4assign` julian→CCYYMMDD (e4functi.c:951-955) | :191 |
| 115 | DTOC | 200 | r4str | 1: r4date | format per CODE4 dateFormat picture; result len = strlen(dateFormat) (e4functi.c:889-896; e4parse.c:500-509). `DTOC(x,1)` ≡ DTOS (parse rewrites 2-parm DTOC to DTOS, e4parse.c:2067-2073; doc e4parse.c:36-39) | :192 |
| 116 | (DTOC) | 200 | r4str | 1: r4dateDoub | dtosDoub then dtoc (e4functi.c:923-927) | :193 |
| 117 | TTOC | 205 | r4str(14) | 1: r4date | CCYYMMDD + "000000" (e4functi.c:821-827; len 14, e4parse.c:482-487). Only the `TTOC(x,1)` index form is supported; the 2nd parm is popped (e4parse.c:2067-2070; doc e4parse.c:40-43) | :195 |
| 118 | (TTOC) | 205 | r4str(14) | 1: r4dateTime | CCYYMMDD+HHMMSS from julian+ms pair (e4functi.c:831-877) | :196 |
| 119 | (TTOC) | 205 | r4str(14) | 1: r4dateDoub | dtosDoub + "000000" (e4functi.c:881-885) | :197 |
| 120 | SPACE | 210 | r4str(n) | 1: const r4numDoub | n blanks; n must be constant (e4parse.c:1618-1644, 2030-2031) | :200 |
| 121 | TRIM | 220 | r4str | 1: r4str | logical rtrim: sets `varLength`, `i2 = len - trailingBlanks` (via `c4trimNLow`); bytes not physically shortened (e4functi.c:2860-2865) | :203 |
| 122 | LTRIM | 230 | r4str | 1: r4str | shifts left, NUL-fills tail, sets `i2` (e4functi.c:2112-2131) | :204 |
| 123 | ALLTRIM | 235 | r4str | 1: r4str | LTRIM then TRIM (e4functi.c:2135-2146) | :205 |
| 124 | LEFT | 240 | r4str | 1: r4str (+const n) | implemented by e4substr with i1=0 (e4parse.c:1721-1748) | :206 |
| 125 | RIGHT | 245 | r4str | 1: r4str (+const n) | e4substr special-case E4RIGHT; 1st parm must be field or constant (e4parse.c:20-21, 1562-1613; e4functi.c:2828-2849) | :207 |
| 126 | PADL | 246 | r4str(n) | 1: r4str (+const n) | pad/truncate to n, blanks left; trailing NULs from trims converted (e4functi.c:2390-2418; e4parse.c:559-574, 1648-1676) | :209 |
| 127 | PADR | 247 | r4str(n) | 1: r4str (+const n) | pad right with blanks (e4functi.c:2422-2443) | :210 |
| 128 | IIF | 250 | r4str | 3: r4log, r4str, r4str | branch lengths may differ; result len = max, shorter branch blank-padded (e4parse.c:466-476; e4functi.c:1723-1745) | :212 |
| 129 | (IIF) | 250 | r4numDoub | 3: r4log, r4numDoub ×2 | e4iif memmove (e4functi.c:1711-1719) | :213 |
| 130 | (IIF) | 250 | r4dateDoub | 3: r4log, r4dateDoub ×2 | | :214 |
| 131 | (IIF) | 250 | r4log | 3: r4log ×3 | | :215 |
| 132 | DATETIME | 259 | r4dateTime | 3..7: const numerics | y,m,d[,h,min,s[,ms]]; time folded to constant ms in `i1` at parse (e4parse.c:1752-1824, 2058-2059); runtime builds julian+ms (e4functi.c:900-919) | :219 |
| 133 | (DATETIME) | 259 | r4dateTimeMilli | 7 parms | ms variant | :220 |
| 134 | STOD | 260 | r4dateDoub | 1: r4str | CCYYMMDD text → julian double (e4functi.c:2520-2535) | :221 |
| 135 | CTOD | 270 | r4dateDoub | 1: r4str | parse using CODE4 dateFormat picture (`date4init`, e4functi.c:649-658; picture pushed as constant, e4parse.c:731-735) | :222 |
| 136 | DATE | 280 | r4dateDoub | 0 | today as julian double (e4functi.c:662-668) | :223 |
| 137 | DAY | 290 | r4numDoub | 1: r4date | (e4functi.c:672-678) | :224 |
| 138 | (DAY) | 290 | r4numDoub | 1: r4dateDoub | (e4functi.c:682-688) | :225 |
| 139 | MONTH | 310 | r4numDoub | 1: r4date | (e4functi.c:2150-2155) | :226 |
| 140 | (MONTH) | 310 | r4numDoub | 1: r4dateDoub | (e4functi.c:2159-2168) | :227 |
| 141 | YEAR | 340 | r4numDoub | 1: r4date | (e4functi.c:2947-2954) | :228 |
| 142 | (YEAR) | 340 | r4numDoub | 1: r4dateDoub | (e4functi.c:2958-2965) | :229 |
| 143 | DELETED | 350 | r4log | 0 | record deleted flag == '*' (e4functi.c:701-714) | :230 |
| 144 | RECCOUNT | 360 | r4numDoub | 0 | (e4functi.c:2470-2505) | :232 |
| 145 | RECNO | 370 | r4numDoub | 0 | (e4functi.c:2509-2516) | :233 |
| 146 | VAL | 380 | r4numDoub | 1: r4str | `c4atod(str, len)` (e4functi.c:2930-2943) | :234 |
| 147 | L2BIN | 385 | r4str(4) | 1: r4numDoub | Clipper: number → 4-byte binary long (e4functi.c:2915-2926; len e4parse.c:575-576) | :237 |
| 148 | (calc) | 390 | (calc's type) | 0 | `E4CALC_FUNCTION` — invocation of a named EXPR4CALC; nested evaluation (e4functi.c:3089-3107; type/len from calc e4parse.c:390-394, 522-525) | :242 |
| 149 | (total) | 400 | r4numDoub | 0 | `E4TOTAL` report total value (e4functi.c:3204-3234) | :246 |
| 150 | PAGENO | 410 | r4numDoub | 0 | report page number (e4functi.c:3195-3200) | :247 |
| 151 | EMPTY | 415 | r4log | 1: r4str | all blanks (memo: `f4long==0`) → true (e4functi.c:3111-3145) | :255 |
| 152 | (EMPTY) | 415 | r4log | 1: r4numDoub | value==0 (e4functi.c:3149-3159) | :256 |
| 153 | (EMPTY) | 415 | r4log | 1: r4dateTime | (e4functi.c:3163-3177) | :257 |
| 154 | (EMPTY) | 415 | r4log | 1: r4dateDoub | e4emptyNum | :258 |
| 155 | (EMPTY) | 415 | r4log | 1: r4log | (e4functi.c:3181-3191) | :259 |
| 156-172 | DESCEND | 420 | r4str | 1 of: r4str, r4num, r4dateDoub, r4log, r4dateTime, r4int, r4currency, r5wstr, r5wstrLen, r4numDoub, r5i8, r5ui4, r5ui2, r5i2, r5dbDate, r5dbTime, r5dbTimeStamp (17 rows, `E4NUM_DESCEND_FUNCTIONS`=17, e4expr.h:284) | ascend to key form then one's-complement every byte (e4functi.c:3081-3085; c4descend i4conv.c:249-265) | :262-278 |
| 173-190 | ASCEND | 430 | r4str | 1 of: r4str, r5wstr, r5wstrLen, r4num, r4date, r4dateDoub, r4log, r4int, r4dateTime, r4currency, r4numDoub, r5ui4, r5ui2, r5i2, r5i8, r5dbDate, r5dbTime, r5dbTimeStamp (18 rows, `E4NUM_ASCEND_FUNCTIONS`=18, e4expr.h:285) | converts value to its FOX index-key byte form (e4functi.c:2969-3067; e4applyAscend i4conv.c:108-195) | :279-296 |
| 191 | KEYEQUAL | 440 | r4log | 2: r4str ×2 | equality after converting both operands to tag-key form (collation-aware partial match; e4functi.c:1827-1880) | :299 |
| 192 | (terminator) | −1 | — | — | end sentinel `E4TERMINATOR` (e4expr.h:369) | :300 |

Positional invariants checked at compile time: `E4NOT_EQUAL==51`, `E4LESS==91`, `E4CHR==105`,
`E4DATETIME==132`, `EXPR4NUM_FUNCTIONS==E4KEYEQUAL+2` (e4expr.h:210-300).

---

## 6. Type system, overload resolution and implicit conversions

`e4massage` walks the postfix array with a simulated typed stack (`types[]`, `length[]`,
`position[]`; e4parse.c:156-193). For each node:

1. Parameter-count check against the table (variadic = −1 allowed; e4parse.c:198-205).
2. For each parameter, if `types[parmPos] != typeShouldBe`, try these **implicit field/constant
   promotions** (rewriting the *producer* node) (e4parse.c:227-285):
   * r4date → r4dateDoub (producer becomes `E4FIELD_DATE_D`) (:229-234)
   * r4num → r4numDoub (`E4FIELD_NUM_D`) (:235-240)
   * r4int → r4numDoub (`E4FIELD_INT_D`) (:241-246)
   * r4currency → r4numDoub (`E4FIELD_CUR_D`) (:255-260)
   * r4numDoub constant → r4floatBin (`E4FLOAT`) (:274-279)
   * r4int → r4floatBin (`E4FIELD_INT_F`) (:280-285)
3. If promotion fails, advance to the next overload row: `info->functionI++` and retry; if the
   `code` changes, the expression is a type error `e4typeSub` (e4parse.c:207-214, 286-292).

Result type = `v4functions[functionI].returnType` (calc nodes take the calc expression's type)
(e4parse.c:388-394). Result lengths are then assigned per §2.4 plus function-specific rules
(concat sum, IIF max, DTOC picture length, etc., e4parse.c:395-833). `resultPos` slots are packed
left-to-right; `r4num`/`r4date`/`r4int` values < 8 bytes reserve 8 bytes so an in-place double
conversion is possible (e4parse.c:922-931).

Parser-time constant folding requirements ("Restrictions", e4parse.c:17-32): STR/STRZERO 2nd & 3rd
parms, SUBSTR 2nd & 3rd, LEFT 2nd, RIGHT 2nd (and its 1st must be field or constant), PADL/PADR
2nd, SPACE 1st, DATETIME hour..ms must all be **numeric constants**; otherwise error
`e4notConstant` (e4parse.c:1404-1854). TRIM-family results cannot feed SUBSTR (documented
restriction, e4parse.c:23-26).

Null handling (S4FOX): every kernel checks operand `isNull`; for arithmetic, null in → null out
with value 0; for comparisons, `isNull(a) != isNull(b)` forces result false (true for `<>`) and
marks the node null (pattern e4functi.c:330-340, 966-976, 2296-2305).

---

## 7. Function semantics that index keys depend on (must match byte-for-byte)

### 7.1 STR / STRZERO formatting

`STR(n [,len [,dec]])`: len default **10**, dec default **0**; parse-time: `if(len<0) len=10; if
(len <= dec+1) dec = len-2; if (dec<0) dec=0` (e4parse.c:1409-1483). Runtime calls
`c4dtoa45(double, out, len, dec)` (e4functi.c:2675-2690): right-justified, blank-padded, fixed
`dec` decimals. **The `c4dtoa45`/`c4ltoa45`/`c4atod`/`c4clip`/`c4upper` implementations are not in
this source drop** (declared d4declar.h:1895-1899, 224; no defining .c file present) — behavior
[UNVERIFIED at implementation level]; observable contract from call sites: overflow fills with
`*` (`StrZero(100,2)` → `"**"` per comment e4functi.c:2545-2547), `c4ltoa45(v,p,-n)` writes n
digits zero-filled (usage D4DATE.C:404-406, comment e4functi.c:870-875). STRZERO = STR then leading
blanks → '0' (e4functi.c:2569-2575). **Port must replicate VFP STR() rounding/overflow exactly**,
since `STR(numfield)` is a common index key.

### 7.2 SUBSTR / LEFT / RIGHT

`SUBSTR(s, start [,count])`: start/count constants; `i1 = start-1` clamped to [0, len(s)];
`len = count` clamped to remaining length (e4parse.c:1680-1717, 1828-1854, 539-549). Runtime:
`memmove(result, result + i1, len)` — the operand was materialized at this node's `resultPos`
by the STR_CAT field loader (e4functi.c:2828-2849). `LEFT(s,n)` = SUBSTR with i1=0.
`RIGHT(s,n)`: `i1 = len(s) - n` (shift), `len = n`; if operand is variable-length (trimmed),
shift = max(0, trimmedLen − n) at runtime (e4functi.c:2832-2843; e4parse.c:1562-1613, 526-534).

### 7.3 DTOS / DTOC / TTOC / date kernel

* Date text format is `CCYYMMDD` (c4encode picture, D4DATE.C:436-445).
* `date4long("CCYYMMDD")` → Julian day (days since Jan 1 4713 BC; Jan 1 1981 = 2444606); blank or
  `"00000000"` → 0; invalid chars → −1 (D4DATE.C:670-699). Julian = `c4ytoj(year) + dayOfYear +
  1721425` (`JULIAN4ADJUSTMENT`, D4DATE.C:659-666; d4defs.h:2715).
* `date4assignLow(ptr, julian, 0)`: julian ≤ 0 → 8 blanks; else formats year(4)month(2)day(2)
  zero-filled (D4DATE.C:364-409).
* DTOS: 8-byte copy; input equal to `"       0"` produces 8 blanks (FoxPro/MDX verified, comment
  e4functi.c:635-641).
* DTOC uses the CODE4 `dateFormat` picture (stored per CODE4, `trans.dateFormat[20]`,
  d4data.h:1172, LEN4DATE_FORMAT d4defs.h:1710; getter D4DATE.C:223-239). Default picture value is
  set outside this source drop [UNVERIFIED — treat "MM/DD/YY" as the documented CodeBase default
  and make it configurable].
* TTOC(x,1): 14 chars `CCYYMMDDHHMMSS`; from r4dateTime the time part is decomposed from
  milliseconds (e4functi.c:846-876); from date types the time is `"000000"` (e4functi.c:821-827).

### 7.4 Trim family and `hasTrim`

TRIM does not shrink the buffer; it records the logical length in `i2`/`varLength`
(e4functi.c:2860-2865). Concatenation of trimmed operands uses `E4CONCAT_TRIM`
(movePchar = NUL): trailing NULs of the left part are squeezed out and the tail NUL-padded
(e4functi.c:444-502). `EXPR4.hasTrim` is set whenever TRIM/LTRIM/ALLTRIM occurs
(e4parse.c:553-557) and at key-generation time all trailing NUL bytes of the key are converted to
**blanks** so `TRIM(f1)+TRIM(f2)` keys stay fixed-length and blank-padded like VFP
(E4EXPR.C:736-748; also pre-collation at 697-702). Documented quirks: `$` and comparisons with
TRIM can differ from VFP (e4parse.c:44-51).

### 7.5 UPPER

Under CDX with data codepage 1252/1250, uses Windows ANSI `CharUpper` (accented letters map like
VFP); otherwise ASCII-style `c4upper` (e4functi.c:2878-2911). Port note: for VFP-compatible keys,
implement Win-1252 uppercase mapping.

---

## 8. Comparison semantics (padded strings, ties)

All string comparisons use `u4memcmp` — by default a plain `memcmp` (`#define u4memcmp c4memcmp`,
d4declar.h:178-182; only special-cased for S4GERMAN non-FOX language builds).

At massage time for every comparison node: `i1 = min(len(a), len(b))` and a length-status flag is
computed; for `>` the flag (stored in `p1` as 0/1) is 1 iff `len(a) > len(b)`; for `<` it is 1 iff
`len(a) < len(b)` (e4parse.c:780-824). Runtime:

* `=`  : `!memcmp(a, b, i1)` — **compares only the common prefix**, so `"AAAA" = "AAA"` and
  `"AAA" = "AAAA"` are both true (e4functi.c:1007-1027; documented divergence from VFP, where only
  the second is true with SET EXACT OFF — e4parse.c:59-62).
* `<>`/`#` : `memcmp(a,b,i1) != 0` (e4functi.c:2315-2336).
* `>` : memcmp on i1; if equal prefix, result = p1 flag (longer left wins) (e4functi.c:1479-1512).
* `<` : symmetric (e4functi.c:1884-1916). `>=`/`<=` are negations of `<`/`>`
  (e4functi.c:1608-1630, 2012-2034).
* Numeric/date comparisons compare doubles (e4functi.c:1634-1668, 2038-2071); currency uses
  `currency4compare`; datetime uses `date4timeCompare` with the same tie flag mechanism
  (e4functi.c:1404-1475).
* Logical `=`/`<>` compare the 4-byte ints via the numDoub-position overload with `i1 =
  sizeof(int)` [len of r4log is sizeof(int), e4parse.c:826].

---

## 9. Expression ↔ index-key interaction (S4FOX/CDX)

### 9.1 Key length — `expr4keyLen` (E4EXPR.C:1049-1100) and `expr4keyLenFromType` (E4EXPR.C:928-1045)

For S4FOX (stand-alone): r4num, r4date, r4numDoub, r4currency, r4dateTime, r4dateTimeMilli → **8**;
r4int, r5ui4, r5ui2, r5i2 → **4**; r4log → **1**; everything else (strings) → collation-dependent
(−2): key len = `nullIndicatorLen + expr4len * (1 + keySizeCharPerCharAdd)` for non-machine
collations (skipped when the tag has ASCEND/DESCEND, which pre-collates) (E4EXPR.C:1067-1095).
`nullIndicatorLen` = 1 if any referenced field is nullable, except for candidate keys
(`typeCode & 0x04`) (E4EXPR.C:879-925, 894-899). Max FOX key size: `I4MAX_KEY_SIZE` = **240**
(d4defs.h:2128).

### 9.2 Key generation — `expr4key` (E4EXPR.C:755-770) → `expr4keyConvert` (E4EXPR.C:777-812)

1. Evaluate (`expr4vary`).
2. Convert by result type: strings via `expr4keyConvertIndexStr` (collation), others via
   `expr4keyConvertIndexDependent` (E4EXPR.C:782-785).
3. Null-byte handling (S4FOX): if the expression *can* be null, the key grows by one leading byte —
   value null → whole key zero bytes; not null → prefix byte `0x80` before the value bytes
   (E4EXPR.C:795-807). Key is NUL-terminated in the buffer (E4EXPR.C:809).

### 9.3 FOX binary key transforms (i4conv.c) — exact algorithms

All produce byte sequences that sort ascending with unsigned memcmp:

* **double → key** `t4dblToFox` (i4conv.c:2432-2466): if value ≥ 0, reverse the 8 IEEE-754 bytes to
  big-endian and add `0x80` to byte 0; if negative, write the one's complement of the reversed
  bytes.
* **float → key** `t4floatToFox`: same scheme over 4 bytes (i4conv.c:2383-2417).
* **int32 → key** `t4intToFox` (i4conv.c:1012-1035): byte-reverse; then `+0x80` on byte 0 if value
  > 0 else `−0x80` (equivalent to flipping the sign bit).
* **uint32 → key** `t4unsignedIntToFox`: plain byte-reverse (i4conv.c:1041-1051).
* **int64 → key** `t4i8ToFox`: byte-reverse, then sign-bit flip on byte 0 (i4conv.c:1096-1115).
* **currency → key** `t4curToFox` (i4conv.c:1360) [body not read in detail]; doubles destined for
  currency tags go through `t4dblToCurFox` = double→CURRENCY4 (string round-trip via
  `c4dtoa45(...,20,4)`) → `t4curToFox` (i4conv.c:2370-2377, 226-235). NOTE: in the S4FOX build
  `expr4currency()` compiles to `return 0` (`#ifndef S4CLIENT_OR_FOX` around the body,
  E4EXPR.C:852-872), so the r4numDoub→currency path is effectively dead in FOX stand-alone; pure
  currency-typed expressions use `t4curToFox` directly (E4EXPR.C:445-449).
* **datetime → key** `t4dateTimeToFox` (i4conv.c:2209-2287): value = julianDate +
  (ms rounded to whole seconds, ≥500 rounds up)/86400000.0 as double; then a **FoxPro compatibility
  fixup**: for second-of-day values flagged in a precomputed bitset (`flags4dateTime`) the low
  mantissa byte is decremented (with borrow) before `t4dblToFox` (i4conv.c:2262-2286). The ms
  variant skips rounding (i4conv.c:2292-2364).
* **date (text) → key**: `t4dblToFox(date4long(text))` (E4EXPR.C:468-471).
* **num (text) → key**: `t4dblToFox(c4atod(text,len))` (E4EXPR.C:459-466).
* **logical → key**: 1 byte, `'T'` or `'F'` (E4EXPR.C:512-536). (ASCEND() of a logical instead
  produces `'1'`/`'0'`, i4conv.c:121-126.)
* **strings**: machine collations copy bytes as-is (unicode fields get per-char byte swap,
  E4EXPR.C:666-712); general collations translate via `collate4convertCharToKey`
  (E4EXPR.C:686-709, 818-848), possibly expanding per char.

### 9.4 hasTrim at key time

Before/after collation translation, trailing NUL bytes of a trimmed string result are rewritten to
`' '` in the key (E4EXPR.C:697-702, 736-748) — this is what makes TRIM-based index expressions
produce fixed-length blank-padded VFP-compatible keys.

### 9.5 DESCEND/ASCEND

`ASCEND(x)` converts x into exactly the FOX key byte form described in §9.3 (e4functi.c:2969-3067;
e4applyAscend i4conv.c:108-195); `DESCEND(x)` = ASCEND then one's-complement of every byte
(e4functi.c:3081-3085; c4descend i4conv.c:249-265 — the `-b` variant is Clipper-only). Tags whose
expression uses ASCEND/DESCEND set `tagPtr->hasAscendOrDescend`, which suppresses further collation
of the already-collated bytes (e4parse.c:617-620; E4EXPR.C:673-682, 1090-1091). Memo fields in a
simple tag expression are limited to `I4MAX_KEY_SIZE_COMPATIBLE (240) − nullByte` and
`memSizeMemoExpr` (e4parse.c:431-458).

---

## 10. Named calculations (EXPR4CALC) and totals

A calc is a named, reusable sub-expression: `code4calcCreate(c4, expr, name)` (E4CALC.C:122),
looked up during parsing by name (e4parse.c:1958-1979), invoked via table row 148: the calc's own
E4INFO program is evaluated re-entrantly with the globals saved/restored, and its result pointer is
pushed (e4functi.c:3089-3107). `expr4calcResultPos` relocates the calc result into the caller's
buffer (E4CALC.C:244). Related API: `expr4calcLookup` (E4CALC.C:198), `expr4calcNameChange`
(E4CALC.C:273), `expr4calcRemove` (E4CALC_2.C:26), `expr4calcModify` (E4CALC_2.C:93),
`expr4calcMassage` (single numeric field calc → double, E4CALC.C:22-35). TOTAL4 wraps a calc for
report totals: sum/count/average/highest/lowest (e4expr.h:30-69; total4value e4functi.c:3213-3234).
For the DB-engine port, calcs are optional; totals are report-writer only.

---

## 11. Compatibility-mode divergences (brief)

Selected by exactly one of S4FOX / S4CLIPPER / S4MDX / S4NDX at build time.

* **S4CLIPPER (NTX)**: numeric keys are *text*: `r4num` keys copied+`c4clip`ped to the tag's
  keyLen/keyDec; `r4numDoub` keys formatted with `c4dtoaClipper(value, key, keyLen, decimals)`
  where defaults come from `CODE4.numericStrLen`/`decimals` (E4EXPR.C:561-639;
  e4parse.c:1334-1339); date keys are 8-char `CCYYMMDD` text (E4EXPR.C:621-625); `EXPR4.keyLen/
  keyDec` track the source field (e4parse.c:2460-2463); DESCEND uses arithmetic negation `-b` per
  byte for Clipper compatibility (i4conv.c:251-257) and a separate `e4descendBinary`
  (e4functi.c:3071-3077).
* **S4MDX (dBase IV)**: numeric keys are 12-byte BCD (`C4BCD`, E4EXPR.C:244-271); date keys are
  raw doubles of the julian value with blank date → `1.0E300` (E4EXPR.C:273-299).
* **S4FOX**: as specified throughout; `storedKey` scratch always gets `+1` for the null byte
  (e4parse.c:879-881).

---

## 12. Limits

| Limit | Value | Cite |
|---|---|---|
| Max operand-stack entries (parse + eval) | 65 (`E4MAX_STACK_ENTRIES`) | e4expr.h:19; e4parse.c:950-952 |
| Max declared parms per table entry | 4 (`E4MAX_PARMS`) | e4expr.h:18 |
| Function name length | 8 chars | e4parse.c:120, 1224 |
| Field name | 10 chars unless long-field-name DBF | e4parse.c:2404-2417 |
| Calc name | 64 + NUL (`E4MAX_CALC_NAME` 65) | e4expr.h:20 |
| Constants blob | 512 bytes initial, grows by 256 (heap) | e4parse.c:1061, 2621-2648 |
| Operator stack | 128 bytes (32 ints) initial, growable | e4parse.c:1060, 1092-1100 |
| Expression source length | no explicit limit (scanned by strlen) | e4parse.c:2662-2667 |
| FOX max key size | 240 bytes | d4defs.h:2128 |
| Date format picture | < 20 chars (`LEN4DATE_FORMAT`) | d4defs.h:1710; D4DATE.C:251-253 |

---

## 13. Recommendations for the C# port

1. **Front end**: replace the shunting-yard + table walk with a conventional lexer → Pratt/precedence
   parser → typed AST, but keep the *published* grammar identical: precedence table of §4,
   left-associativity, `.AND.`/`.OR.` n-ary flattening, `[...]`/`'...'`/`"..."` strings, no `{}`
   date literals, no unary minus except in numeric literals, `!`/`.NOT.` prefix.
2. **Evaluation**: a visitor over the AST returning a discriminated `Value` (Char(fixed len),
   NumericDouble, DateJulianDouble, Logical, DateTime(julian,ms), Currency(int64 ×10⁻⁴),
   Int/Float/…) is cleaner than the pointer stack machine; but preserve these VFP/CodeBase
   behavioral quirks exactly because filters and keys depend on them:
   * fixed-length blank-padded string model, concat `+` length = sum of operand lengths;
     `-` moves left-operand trailing blanks to the end (§4, e4functi.c:444-531);
   * prefix-only `=` comparison of unequal-length strings (§8) — consider a "VFP-exact" switch;
   * divide-by-zero → 0.0 (e4functi.c:753-756);
   * blank-date propagation through date± (e4functi.c:409-425);
   * null semantics of §6.
3. **Index keys**: implement §9 transforms bit-exactly (t4dblToFox sign-flip/complement scheme,
   int/i8 sign-bit flip + big-endian, 'T'/'F' logicals, 0x80 null prefix byte, hasTrim NUL→blank,
   datetime rounding + `flags4dateTime` fixup) — CDX compatibility is decided entirely by these
   bytes plus STR()/DTOS()/TTOC() text formats (§7).
4. **Deterministic formatting**: port `c4dtoa45` semantics (right-justify, blank pad, fixed
   decimals, `*` overflow) with `MidpointRounding` matched against VFP STR() output; validate with
   golden files against VFP-created CDX/DBF (the exact C implementation is absent from this drop —
   §7.1).
5. **Keep the parse-time constant restrictions** (§6) at least in "compatibility" mode, so any
   expression accepted by the port is also accepted by CodeBase/VFP and vice versa.
6. Make `dateFormat` (CTOD/DTOC picture) a per-session setting stored with the engine object
   (equivalent of CODE4), defaulting to a VFP-compatible format.

---

## 14. Open questions / risks for the C# port

1. **`c4dtoa45`, `c4ltoa45`, `c4atod`, `c4clip`, `c4upper`, `c4encode` implementations are missing
   from this source drop** (declared d4declar.h:1888-1899, 224-225; no defining translation unit
   present). Their exact rounding, overflow (`*` fill) and clipping behavior must be recovered from
   the CodeBase DLL, the docs, or empirically from VFP — STR()-based index keys depend on it.
2. **Default `dateFormat` value** is initialized outside this drop [UNVERIFIED]; DTOC()-based key
   lengths depend on it (key len = picture length, e4parse.c:500-509).
3. **`flags4dateTime` bitset** (`flags4dateTimeInit`, used i4conv.c:2218-2219, 2263) encodes which
   seconds-of-day need the mantissa-decrement FoxPro fixup; the generator was not located in the
   e4/i4 files read — must be extracted (search F4FLAG.C / f4flag usage) before datetime index keys
   can be produced bit-exactly.
4. **`t4curToFox` body** (i4conv.c:1360) and `currency4compare`/`date4timeCompare` internals were
   not transcribed; needed for currency/datetime tags.
5. **Collation tables** (`coll4arr.c`, `COLLATE4`, `collate4subSortCompress` etc.,
   i4conv.c:287-330; E4EXPR.C:818-848) are a separate sizable work item for general/machine
   collations; machine collation (memcmp order) is enough for a first VFP-compatible milestone.
6. **Known engine≠VFP divergences** documented in the source (e4parse.c:35-63): prefix `=`
   comparison symmetry, TRIM vs `$`/comparisons, DTOC(x,1) laxness, TTOC only in index form —
   decide whether the port replicates CodeBase behavior (for CDX interop, yes for key-affecting
   ones) or fixes toward VFP.
7. **Thread-safety**: the C engine serializes all expression evaluation through one global critical
   section (E4EXPR.C:87-135); the port should use per-evaluation state, but beware calc re-entrancy
   (e4functi.c:3089-3107) when doing so.
8. `expr4currency()` returning constant 0 in FOX builds (E4EXPR.C:852-872) looks intentional but
   surprising; verify against a real VFP CDX with a `numDoub` expression over currency fields
   before assuming the `t4dblToCurFox` path is dead.
