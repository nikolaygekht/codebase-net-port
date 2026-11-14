# 013-coverage-audit — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.
The input is [`AUDIT.md`](AUDIT.md), the first `crap4csharp` run over steps 001–011.

## Goal

Give every piece of logic that only the Golden layer reaches a test at its own level — the query
pipeline first, then building a key from a value, the expression engine's untested kernels, and
seeking through a table's tag — so that a break there reddens a unit or component test, not only a
corpus gate. **Verified when a `crap4csharp` rerun shows no member of findings F1–F4 at 0% unit
coverage without a recorded reason, `dotnet test` is green, and no golden expectation or corpus file
changed.**

**Capability:** none opened — a remediation numbered like a step, in the shape of
[`007-audit-glm`](../007-audit-glm/) and [`012-p1-tails`](../012-p1-tails/). It hardens `QUERY`,
`COLLATION`, `EXPR` and `CDX-READ`. **Governing specs:** `QUERY-OPTIMIZER.md`, `EXPRESSIONS.md`,
`KEY-COLLATION.md`, `CDX-FORMAT.md` §7.

## What changes, in short

- **Now:** the audit counted 129 members at 0% unit coverage and 93 above CRAP 8.0. The query
  pipeline between a compiled filter and its record set — decomposition, execution, the residual
  filter, the result cursor — is reached only through `QueryHarness` and the golden gates. 012's
  close showed what that costs: `Table.Seek` found nothing in any nullable tag from step 010 on, in a
  method no unit test reaches.
- **After:** each finding F1–F4 has tests at layer 1 or 2, written against the contract the class
  promises, over the seams that already exist. Defects the new tests find are fixed in the step,
  test first. A fresh baseline at the start and a rerun at the close give the before and after.
- **Library code:** changes only where a test finds a defect, or where a member cannot be reached
  without a corpus file — the §4 untestable-design signal, answered with a seam. None is expected:
  the seams below exist already.

## Not in this step

- **F6** — members fully covered and simply long (`CodePageMap.NumberFor`, `ParseFunction`, …). Their
  CRAP equals their CC; there is nothing to test that is not tested. Recorded for the threshold
  decision ADR-60 left open, and left there.
- **A CRAP or mutation threshold as a gate.** ADR-60 defers it until a few steps have numbers; this
  step adds numbers, it does not decide.
- **New corpus cases.** The step tests below the corpus. If a test finds a path no corpus case
  covers and the answer depends on the reference, it becomes a generator case in a later step and
  is recorded, not improvised here. *Exception, at the user's call (2026-11-12): sub-step 7a-1 adds
  two generator cases, below.*
- **Refactoring for its own sake.** A long member that is fully tested stays long (F6). A member is
  split only when its test cannot reach a branch otherwise.
- **The members from `AUDIT.md`'s "Smaller" list** (`DbfOpener.ReadFieldDescriptors`, `LeafGeometry`,
  `TagCursor.Seek`, `BlockResidency.Unchain`, …) — taken if a sub-step's tests reach them on the way,
  otherwise recorded.

## Scope, by finding

The member lists are `AUDIT.md`'s; sub-step 1 re-derives them from a fresh run, because 012 changed
the code under several of them.

| Finding | Classes | Layer | Over which seam |
|---|---|---|---|
| **F1** the query pipeline | `QueryDecomposer` (and its `Walk`), `QueryPlan`, `TagEligibility.CanExpress`/`IsWhole`, `LeafConstraint`/`ConstraintValue`/`ConstraintNode` | 1 | hand-built `FieldDefinition`s and `QueryTag`s compiled from tag expressions — pure |
| | `QueryExecutor`, `ResidualFilter`, `TagProbe` | 2 | `ITagProbe` faked by `SortedKeyProbe`, which `TagProbeContract` already holds to the real probe; `TagProbe` over `IndexImage`-built tags |
| | `Query` — `Top`/`Bottom`/`Next`/`Previous`/`Skip`/`Count`, record and tag order | 2 | `IndexedTableImage`: a table and its CDX in memory |
| **F2** keys from values | `RecordKey`, `CollationWeights`, `KeyTransform.FromDateTime`/`DecrementLowByte`, `SeekConverter.KindOf`/`RefuseIfNotTheSameTwice` | 1 | pure; headers built with `IndexImage` |
| **F3** the expression engine | `ExpressionEvaluator` — `PictureDateText`, `FormatNumber`, `Rounded`, `TimestampText`, `Count`, `MemoBytes`, `LoadField`; `ExpressionKey` — `Write`, `WriteValue`, `Length`; `ExpressionValue.ToRaw` | 1 | constants, and records built in memory as `ExpressionEvaluatorTests` already does |
| **F4** seeking through a tag | `TableTagCursor.SearchFor` (every branch: character whole and prefix, collated whole and prefix, conversion, the null byte, the refusal), `TypedSeekFor`; `Tag`'s properties | 2 | `IndexedTableImage` |
| **Divergences** *(added 2026-10-31)* | every entry of `DIVERGENCES.md`: what the port refuses, skips or answers differently from the reference | — | per entry: a fix with its test, or an ADR and user-facing docs, or a plan row (PLAN 7a) |
| **F5** the public API's forwarding | every public member of `Table`, `Tag`, `FieldCollection`, `CodeBaseEngine` at 0% — one test each that it reaches its target (ADR-67); `Table.QueryTags` and `Table.ReadMemo` for their own logic too | 2 | `TableImage`, `IndexedTableImage` |

## Classes

No new production class is planned. Test-side, as needed:

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `QueryDecomposerTests` *(test)* | — | what plan a filter decomposes into over given tags | layer 1 |
| `QueryExecutorTests` *(test)* | — | the record set a plan produces over probes | layer 2, `SortedKeyProbe` |
| `ResidualFilterTests` *(test)* | — | which terms a plan leaves to the per-record check, and its verdicts | layer 1/2 |
| `QueryCursorTests` *(test)* | — | how a query moves over its result, in both orders | layer 2, `IndexedTableImage` |
| test helpers | — | building `QueryTag`s and compiled filters from text over hand-built fields | in `CodeBase.Net.Tests`, not `TestUtils`, unless the golden suite needs them too |

## Public surface

**None changed**, unless a defect fix needs it — and then the fix says so.

## Seams

All exist; this step uses them.

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| a tag's keys, for the executor | `ITagProbe` | `SortedKeyProbe` (TestUtils), contract-tested against the real probe | execution, merged ranges, empty and full sets |
| a table with an index | `IRandomAccessSource` behind `CodeBaseEngine`'s internal constructor | `IndexedTableImage`, `TableImage` | `Query`, `TableTagCursor`, `Table.QueryTags` |
| a tag header | `IndexHeader.Parse` over bytes | `IndexImage.SingleTag` | `SeekConverter`, `TagProbe` |
| field metadata | `IFieldMetadata` / `FieldDefinition` | constructed directly | parsing, resolution, decomposition |

## Decisions

1. **Tests assert the contract, from the specification or the corpus.** A decomposer test states the
   plan a filter becomes — which terms a tag answers and over what range — as `QUERY-OPTIMIZER.md`
   defines it; a key test takes its expected bytes from a corpus file at a cited offset, or states a
   round-trip or ordering invariant, never bytes typed from a spec (§4). Where only the reference
   could say, the test is not written here and the case is recorded.
2. **A defect a new test finds is fixed in this step**, test first, and recorded with its cause —
   as 012 fixed the nullable seek. If the fix needs the reference's answer and no corpus case has it,
   it is recorded and goes to a generator case instead.
3. **The fresh baseline replaces `AUDIT.md`'s numbers for this step.** `AUDIT.md` stays as written;
   the new run is filed beside it and the gate compares against it.
4. **F5 — decided by the user (ADR-67).** Nothing becomes public for a test; tests reach internal
   classes through `InternalsVisibleTo`, and any seam this step needs is `internal`. **Every public
   member that forwards gets at least one test** proving it reaches the right place — a call through
   the public member whose result only the intended target produces, never a `Verify` on a mock —
   and exhaustive coverage of the target stays in the target's own tests. Members with logic of their
   own (`Table.QueryTags`, `Table.ReadMemo`) get tests of that logic as well.

## Open questions

1. Whether the fresh run moves anything between findings — answered by sub-step 1.

---

## 7a-1 — long field names, read *(added 2026-11-12, at the user's call)*

The first entry of `DIVERGENCES.md` taken in 7a. The user decided **support**, not keep, and that the
feature flags follow the reference: **read on version `0x31` only**.

### Goal

A table in CodeBase's long-field-name layout (`flags[4]`, `DBF-FORMAT.md` §4.2) opens and reads as
the reference reads it: its fields by their long names, filters and tag expressions naming them, its
tags opened, sought and used by the optimizer. And a table of any version but `0x31` has its bytes
12–19 ignored, as the reference ignores them. **Verified when** two new corpus cases, `LONGNAME` and
`FLAGS30`, are gated by the golden suite, every pre-existing corpus file regenerates byte-identical,
and `dotnet test` is green.

**Capability:** `DBF-READ` (the descriptor layout) and `EXPR` (names past ten characters).
**Governing specs:** `DBF-FORMAT.md` §2.1–2.4, §4.2; `EXPRESSIONS.md` (field names, e4parse.c:2404-2417).

### What the reference does

| Fact | Source |
|---|---|
| `flags[]` are read only when the version byte is `0x31`; on any other version bytes 12–19 are never looked at | D4OPEN.C:2150-2200 |
| Each descriptor: the 21 bytes of `FIELD4IMAGE` from `type` on, then `i16 LE` `nameLen` (name length + 1), then the name and its NUL. `_NullFlags` is written the same way | D4CREATE.C:1635-1680 |
| The run ends at a `0x0D` where the next descriptor would begin, **or** two bytes before the end of the descriptor region; an offset past `headerLen` or negative is `e4data` | D4OPEN.C:2709-2731 |
| No 263-byte reserve; the header ends `0x0D 0x1A` | D4CREATE.C:1693-1702 |
| On open every name is upper-cased (`c4upper`); lookup upper-cases the asked-for name and compares whole | D4OPEN.C:316, d4field.c:96-108 |
| `_NullFlags` is recognised by `memcmp` of 10 bytes of the stored name, as in the short layout | D4OPEN.C:2611 |
| An expression may name a field longer than ten characters only when the table holds long names; otherwise `e4unrecValue` | e4parse.c:2404-2417 |
| FoxPro cannot read this layout | DBF-FORMAT.md §4.2 |

### What changes, in short

- **Now:** `FieldDescriptorTable.Parse` refuses the layout outright. `FeatureFlags.Parse` validates
  the flag bytes only on `0x31` but *reports* them on every version, so a `0x30` table with byte 16 =
  1 is refused for long names — a table the reference opens. The compressed-data refusal (ADR-56)
  escapes only because `DbfOpener.RefuseIfCompressed` checks the version itself, a second guard the
  long-name path lacks. The test that should have caught it, `Parse_TableThatIsNotExtended_…`, sets
  the bytes to **9**, which no flag reads as set. And no compile path passes the long-name flag to the
  lexer: `Table.CompileFilter`, the index's pad-byte resolution at open (`KeyTypeResolver`), and
  `SeekConverter` (from `Table` and `TableTagCursor`) all take the default, `false`.
- **After:** the header reports every feature flag false unless the version is `0x31`. The descriptor
  reader decodes both layouts into the same `FieldDescriptor`s. The flag reaches every expression
  compiled over the table, carried by the field metadata itself, so a compile path cannot forget it.

### Classes

| Class | Role | Change |
|---|---|---|
| `FeatureFlags` / `DbfHeader` | Entity | `Parse` returns all-false flags unless the version is `0x31` (the validation there is unchanged). `RefuseIfCompressed`'s own version check becomes redundant and stays — not this change's to remove |
| `FieldDescriptorTable` | Controller | `Parse` keeps the 32-byte run and gains the long run: per descriptor, a 21-byte tail, a length, a name; ends as the reference ends it. Refuses (`ErrorCode.Data`) a length below 1 or a descriptor or name running past the region |
| `FieldDescriptor` | Entity | one internal factory from the 21-byte tail plus a name, shared by both layouts so the two cannot decode the tail differently. `Name`'s XML doc no longer says "up to ten characters" |
| `IFieldMetadata` / `TableFieldMetadata` | Boundary | gains `LongFieldNames`, a **required** constructor argument — so the compiler finds every site that builds metadata, the library's and the tests' |
| `ExpressionParser` | Controller | takes the flag from the metadata; its own `longFieldNames` parameter, which no caller sets, is removed |
| `DbfOpener`, `Table`, `TableTagCursor` | Controller | pass the header's flag where they build metadata. Where a signature carries `(fields, alias)` only to build metadata, it may carry the metadata instead — per site, smallest diff |

No new production class. Name text stays ASCII, as `FieldDescriptor.ReadName` already decides for
short names.

### Public surface

None changed. `FieldDefinition.Name` can now be longer than ten characters; its doc says so.

### Seams

| Seam | Faked how | Used to test |
|---|---|---|
| the descriptor region | bytes built in the test — **input** only | the long run's decoding, its end and its refusals |
| a whole table | a long-layout table image, built beside `TableImage` and read by the real opener | a filter and a field lookup by a long name, without the corpus |
| the reference's answer | corpus cases `LONGNAME`, `FLAGS30` | everything the reference must say |

### Corpus cases

- **`LONGNAME.DBF`** (`0x31`, `flags[4] = 1`), built by `d4create` with names given in mixed case:
  one of exactly 10 characters and one of 11 (the boundary), a long character field, a long numeric,
  a long nullable date (so `_NullFlags` is in the long layout), a long memo, and a short `ID`.
  A CDX by `i4create` with tags over the long character and numeric fields (one through `UPPER`).
  Dumps: header and descriptors, records, the index, and a few filters through `dump-query.cpp` —
  by long name, qualified by the alias, one answered by a tag and one per record. `dump.cpp` learns
  to print the long layout; it prints nothing new for any other file.
- **`FLAGS30.DBF`** (`0x30`): an ordinary table whose bytes 12–19 the generator sets to `1` after
  `d4create`, then reopens and dumps. Witnesses that the reference reads it as an ordinary table.

### Decisions

1. **The flag lives on the metadata, not on each `Parse` call.** The defect here was a parameter with
   a default that every caller left at the default. A required constructor argument cannot be left.
2. **The run's end follows the reference**, `0x0D` or two bytes before the region's end, rather than
   the stricter short-layout rule. A file the reference opens must open.
3. **No write path.** The port writes no tables yet; when `WRITE` comes it writes this layout as
   D4CREATE.C does. Recorded in the ADR.
4. **ADR-72** records the support and the version rule; `DIVERGENCES.md`'s row becomes "supported".

### Open questions

None.

---

## 7a-2 — a tag over `RECNO()` *(added 2026-11-12, at the user's call)*

`DIVERGENCES.md` §1, ADR-49. The user decided: **`RECNO()` is supported in a tag; `DATE()`, `TIME()`
and `RECCOUNT()` stay refused**, and are documented as divergences.

### Goal

A tag whose expression reads `RECNO()` opens, walks, seeks and synchronizes with the table's cursor
as the reference's does, and the optimizer may answer a filter from it. **Verified when** corpus case
`RECNOTAG` is gated — every stored key rebuilt from its record, the seeks, the query dumps — and the
three other functions are still refused, with that refusal documented to callers.

### What the reference does

| Fact | Source |
|---|---|
| `RECNO()` is `d4recNo(data)`, the current record's number, as a double | E4FUNCTI.C:2509-2516, row 146 |
| Building a tag sets `recNum` to each record's number before its key is evaluated, so a `RECNO()` key is that record's own number — the same every time for the same record | R4REINDX.C:387-391, r4reinde.c:762-767 |
| `DATE()`, `TIME()`, `RECCOUNT()` read the clock or the table's size: a key rebuilt later differs from the stored one | ADR-49 |

### What changes, in short

- **Now:** `SeekConverter.RefuseIfNotTheSameTwice` refuses all four. And the value source a tag
  rebuilds a record's key with (`ExpressionValueSource.Read`) replaces the record and its deletion flag
  in the context but **not its number**, so a `RECNO()` key would be rebuilt as 0 — the cursor's
  synchronization after a move by record number would land on the wrong entry.
- **After:** `RECNO()` leaves the refused set. `IKeyValueSource.Read` and `RecordKey.Write` take the
  record's number beside its buffer, and the key is evaluated with it. The other three stay refused,
  and the refusal is documented on `Table.SelectTag` and in `README.md` "Differences from CodeBase".

### Classes

| Class | Change |
|---|---|
| `SeekConverter` | `RefuseIfNotTheSameTwice` no longer lists `RecordNumber`; its doc says why `RECNO()` is the same twice |
| `IKeyValueSource` / `ExpressionValueSource` | `Read(record, recordNumber)`: the number goes into the context with the buffer and the deletion flag |
| `RecordKey.Write` | takes the record number and passes it on |
| `TableTagCursor.Synchronize` | passes the record it was given |

### Corpus case

**`RECNOTAG.DBF`** (`0x30`): `ID` and `NAME C(10)`, ten records with names repeating. Tags by
`i4create`: `R_RECNO` over `RECNO()`, `R_NAMENO` over `NAME + STR(RECNO(), 4)` (the idiom that makes
a name tag unique). Filters through `dump-query.cpp`: a range over `RECNO()`, one walked in
`R_NAMENO`'s order, one beside a name term.

### Decisions

1. **The number travels with the buffer**, not through the context the cursor was built with: the
   context is built once per cursor, the number changes per record.
2. **No `DESCEND(RECNO())`**: a descending numeric tag is untested in this port and is its own question.
3. **ADR-49 is superseded in part** by a new ADR: `RECNO()` key-legal, the three others not.

---

## 7a-3 — a tag keyed through the date picture *(added 2026-11-12, at the user's call)*

`DIVERGENCES.md` §1, ADR-42. Reviewing the entry found that ADR-42's refusal was never implemented,
and that the gap gives a **wrong record set**. The user decided: implement it as ADR-42 wrote it.

### The defect

`CDXEXPR`'s `T_DTOC` is keyed `DTOC(K_DATE)` under the reference's picture `MM/DD/YY`. Opened with the
engine's default options (picture `CCYYMMDD`), the port selects the tag without complaint — both
pictures are eight characters, so the key-length check cannot tell them apart — and the optimizer
answers from the stored keys while the per-record check reads through the default picture:

| Filter | `Query.Count` | per record |
|---|---|---|
| `DTOC(K_DATE) = "01/01/00"` | 2 | 0 |
| `DTOC(K_DATE) = "20000101"` | 0 | 1 |

No gate saw it: no query case filters on `DTOC`, and every golden test over `T_DTOC` configures the
reference's picture.

### Goal

A tag whose key passes through the date picture — `DTOC` or `CTOD` anywhere in its expression — is
refused when selected unless the caller has chosen the engine's `ExpressionOptions`, and so is never
used by the optimizer under the default. Once chosen, the tag works as it does now. **Verified when**
under the default the tag is refused naming the function and a `DTOC` filter's `Query.Count` equals the
per-record count, and under the reference's picture the same filters count what `T_DTOC`'s stored keys
(the reference's bytes) say.

### Classes

| Class | Change |
|---|---|
| `ExpressionOptions` | an internal `Unchosen` instance — `Invariant`'s values, marked not chosen — and `IsChosen`; every public instance, and every `With*` result, is chosen |
| `CodeBaseEngine` | its `ExpressionOptions` defaults to `Unchosen`, so the getter's values are unchanged |
| `SeekConverter` | refuses, beside `RefuseIfNotTheSameTwice`, an expression with a `DateToText` or `TextToDate` node when the options are not chosen — `ErrorCode.NotSupported`, naming the function and the setting |

No plumbing: the options already reach `SeekConverter.For`, and `Table.QueryTags` already leaves out a
tag whose cursor cannot be built.

### Decisions

1. **"Chosen" is a property of the options object, not a flag on the engine.** It needs no new
   parameter anywhere, and a caller who assigns `ExpressionOptions.Invariant` on purpose has chosen it
   — ADR-42's word is "explicitly configured", not "configured to a reference picture".
2. **No new corpus case.** The reference's keys for `T_DTOC` are in `CDXEXPR.cdx.dump.txt`; the gate
   counts them.
3. **ADR-42 is not superseded**; this implements it. A new ADR-74 records that it had not been, and
   how "explicitly" is decided.

---

## 7a-4 — the `'7'` field, a datetime with milliseconds *(added 2026-11-12, at the user's call)*

`DIVERGENCES.md` §1, ADR-32. The user decided: **full support** — read, expressions, tags.

### What it is

A CodeBase extension (`r4dateTimeMilli`, d4defs.h:2821) that VFP never writes and cannot open. The
same eight bytes as `'T'` — Julian day, then milliseconds since midnight, both `i32 LE` — but the
milliseconds are kept where `'T'` keeps whole seconds. Admitted only on `version >= 0x30` (D4OPEN.C:
2598, 2625), by `'T'`'s rules — its length is not checked either. A caller who created one wanted
milliseconds.

### What the reference does

| Fact | Source |
|---|---|
| Blank is eight zero bytes, as `'T'` | F4FIELD.C:140-150 |
| Loaded into an expression by its own row, `E4FIELD_DTTIME_MILLI`, type `r4dateTimeMilli`; compared by its own six rows | e4functi.c:54, 118-161 |
| Text form `CCYYMMDDHH:MM:SS.MMM` | F4FIELD.C:1924-1940 |
| Key: `t4dateTimeToFoxMilli` — `day + ms / 86400000.0` with the milliseconds **not** rounded, then the same `flags4dateTime` decrement as `'T'`, looked up by the **truncated** second (`ms / 1000 + 1`); `t4dblToFox`. Under `S4DATA_ALIGN`/`S4BYTE_SWAP` the milliseconds are dropped instead; the Windows build keeps them, which the corpus witnesses | i4conv.c:2292-2364 |
| Key length 8 | E4EXPR.C:994-1015 |

### What changes, in short

- **Now:** `FieldResolver` refuses `'7'` (ADR-32). And the port already half-knows it: the expression
  type `TimestampWithMilliseconds` and its comparison rows exist, but its key goes through
  `KeyTransform.FromDateTime`, which **rounds** — a `'7'` tag would key 12:00:00.600 as 12:00:01.
- **After:** `'7'` reads as `'T'` does, through the same public API (`GetDateTime`, with its
  milliseconds), blanks to zeros, loads into an expression as `TimestampWithMilliseconds`, renders
  its text with `.MMM`, and keys without rounding.

### Classes

| Class | Change |
|---|---|
| `FieldResolver` | `'7'` readable; Visual-FoxPro-or-later only; lengths as `'T'` |
| `BlankRecord`, `FieldValueDecoder` | `'7'` beside `'T'`: zero blank, natural width, refused as a number, read as a datetime |
| `FoxDateTime.ToText` | an argument for the `.MMM` tail (ADR-32 removed it as dead; now it has a caller) |
| `ExpressionParser.LoaderRow`, `FunctionRows`, `ExpressionEvaluator`, `ExpressionValue` | `'7'` loads by row 18 into a `TimestampWithMilliseconds` value |
| `KeyTransform.FromDateTime` | a `keepMilliseconds` flag: no rounding, flag by the truncated second |
| `ExpressionKey`, `SeekConverter` | the flag set for a `TimestampWithMilliseconds` result, so a seek by `DateTime` and a rebuilt key agree with the stored one |

### Corpus case

**`DTMILLI.DBF`** (`0x30`): an `ID` and a `'7'` field over a few hundred moments spread across the
day — milliseconds 0, 1, 499, 500, 501, 999 and others, on seconds both flagged and not in
`flags4dateTime`, the day's edges, one blank. Tags by `i4create`: ascending and descending over the
field. Gates: the decoded values and text, every stored key rebuilt from its record (the transform),
the seeks. No query case: no constant has the type, so no filter compares one.

### Decisions

1. **The public API does not grow a type.** A `'7'` field is a datetime to a caller, as `'T'` is.
2. **ADR-32 is superseded** by ADR-75.

## 7a-5 — the `'W'` and `'O'` Unicode fields *(added 2026-11-12, at the user's call)*

`DIVERGENCES.md` §1 (field types the port refuses), `PORTING-PLAN.md` §2.2. The user decided:
**support `W` and `O`** — read, expressions, machine-order tags, seeks — told apart from VFP 9's
Blob `'W'` by the table version; **`STR(W)` by the table's code page**, with Windows' best-fit
reproduced; **`GetString` returns the stored characters as they are**. Collated Unicode keys stay
deferred.

### What they are

CodeBase's wide strings: `r5wstr` `'W'` (`r4unicode`) and `r5wstrLen` `'O'` (d4defs.h:2795-2808). The
field holds UTF-16 code units in the machine's order — little-endian on Windows, which is the
corpus — copied verbatim from the caller's `WSTR5` (F4WSTR.C:80-120). `'O'` adds two trailing bytes,
a `u16` count of characters, which **nothing in the reference ever reads back**: expressions, keys
and seeks all see the field less those two bytes. VFP 9 also writes `'W'`, meaning Blob — a memo-like
reference into the FPT — on a `0x32` table. The reference reads every `'W'` as `r5wstr` whatever the
version (D4OPEN.C:2555), so it would misread a VFP 9 Blob; the port does not (decision 1).

### What the reference does

| Fact | Source |
|---|---|
| Length as `'C'`: the low byte in the length, the high byte in the decimals; `W` created even (`even4up`), `O` with two bytes more; flag `0x04` (binary) | D4CREATE.C:1558-1564, D4OPEN.C:2555-2562 |
| Admitted on **any** version at open — no version test (creation needs `compatibility >= 30` for `W` only) | D4OPEN.C:2555; D4CREATE.C:988-1003 |
| Blank is `00 20` per two bytes — U+2000 in little-endian, a space only to a big-endian reader. An assignment shorter than the field blanks it first, so the unassigned tail is U+2000 too; for `O` the two length bytes are blanked the same way before being written | F4FIELD.C:92-108, F4STR.C:155-166, F4WSTR.C:95-116 |
| Loaded by its own row — `E4FIELD_WSTR` (`r5wstr`, the field's length) and `E4FIELD_WSTR_LEN` (`r5wstrLen`, the length **less two**) | e4functi.c:36-37, e4parse.c:397-418 |
| No comparison rows for either type: a filter cannot compare one, only `STR()` of one | e4functi.c (no `r5wstr` operand among rows 42-105) |
| `STR(<bare W field>)`, one argument: row 109, `WideCharToMultiByte(CP_ACP, 0, …)` over `len / 2` code units into `len / 2` bytes — the **host's** code page, Windows' best-fit, `?` for the rest. `STR(O)` takes row 109 too and then fails its parameter type | e4parse.c:1404-1427, e4functi.c:2694-2716 |
| Key, machine collation (the default `collateNameUnicode`): every code unit byte-swapped, so the key is UTF-16 big-endian and sorts by code unit; length = the expression's; pad byte NUL | i4conv.c:184-189, 588-602; E4EXPR.C:668-681; i4init.c:620-632; C4CODE.C:1076 |
| Key, any other collation: the Unicode weight tables — **deferred** | E4EXPR.C:684-710 |
| Seek: the value's code units swapped the same way (`d4seekN` with raw wide bytes) | D4SEEK.C:1116-1130, 1320-1340 |

### Windows' best-fit and .NET

The reference's `STR()` is `WideCharToMultiByte`. A sweep of the BMP on this machine compared it
with .NET's `Encoding.GetEncoding(cp)` (`System.Text.Encoding.CodePages`, default fallbacks) for the
twenty single-byte code pages `CodePageMap` names: they agree everywhere except **0 to 33 code points
per page, all in General Punctuation** (U+2000-U+2044, U+20A4, U+2236), which Windows best-fits — a
space, `-`, `'`, `"`, `.`, `/`, `:`, NBSP — and .NET turns into `?`. On cp1251 the list includes
**U+2000, the blank of a `W` field**, so without the supplement every padded value's `STR()` would
differ. Surrogates agree (each code unit becomes `?`). The supplement is captured by a small Windows
tool, kept beside the generator, and checked in as data — as the collation tables are.

### What changes, in short

- **Now:** `FieldResolver` refuses `'W'` and `'O'` as types it does not read; the expression engine
  carries `WideString`/`WideStringWithLength` but nothing can reach them.
- **After:** both read; `'W'` on a `0x32` table is refused as VFP 9's Blob; `GetString` decodes the
  stored code units (`'O'` less its length bytes), blank U+2000 included; an expression loads either;
  a machine-collated tag over one keys by swapped code units; `Seek(string)` on such a tag seeks the
  UTF-16 form; `STR(W)` narrows by the table's code page with the supplement.

### Classes

| Class | Change |
|---|---|
| `DbfVersion` | `VisualFoxPro9 = 0x32`, compared against the variant's normalized version (which keeps `0x32`) — no new variant member |
| `FieldResolver` | `'W'`, `'O'` readable on any version; `'W'` refused on `0x32` (Blob); lengths by the default rule |
| `BlankRecord` | `'W'`, `'O'` blank to `00 20` pairs |
| `FieldValueDecoder` | the bytes of a wide field: its code units, `'O'` less two; refused as a number |
| `Table.GetString` | decodes a wide field as UTF-16LE before the binary refusal |
| `ExpressionParser` | loader rows 1 and 2; `STR(<bare W field>)` selects the wide row (the port's 108) with length `len / 2` |
| `TypeResolver` | lengths: `W` its length, `O` its length less two |
| `ExpressionEvaluator`, `ExpressionValue` | loads a wide value; the wide `STR` row narrows through the context's `WideNarrowing` |
| `WideNarrowing` *(new)* | the code page's best-fit encoding plus the supplement; one byte per code unit; refuses a code page that is not single-byte or not available |
| `WindowsBestFitSupplement` *(new, generated data)* | per code page, the code points Windows best-fits and .NET does not |
| `EvaluationContext`, `Table` | the context carries the table's `WideNarrowing`, built on first use as `CaseMap` is |
| `SeekConverter`, `KeyTypeResolver` | `KeyKind.WideCharacter` for a wide result under machine collation, refused under any other; pad byte NUL; `ASCEND`/`DESCEND` over a wide operand refused |
| `ExpressionKey` | a wide value keys by swapping each code unit |
| `TableTagCursor.SearchFor`, `Table` seeks | a wide tag takes the text as UTF-16, swapped; exact pads with U+2000, prefix does not |

### Seams

None new. `WideNarrowing` is a pure function of a code page and a string; it is tested directly
with the code-page provider the test projects already register.

### Corpus case

**`WSTR.DBF` + `.cdx`** (`0x30`, code-page mark `0xC9`, cp1251): `ID` `I`, `K_W` `W` of 20 bytes,
`K_O` `O` of 12 (stored 14). A few dozen records: ASCII, Cyrillic, mixed, full-width values, short
ones (U+2000 tails), one blank, characters from the supplement list (U+2010, U+2024, U+2236), others
cp1251 holds directly (U+2013, U+2116), Windows best-fit outside the list (U+0100), unmappable
(U+4E2D), a surrogate pair, an embedded U+2000, repeats. Tags by `i4create`: `W_ASC`, `W_DESC` over
`K_W`, `O_ASC` over `K_O`, `S_ASC` over `STR(K_W)`. Query cases over `STR(K_W)` (equality, a range, a
Cyrillic literal). **The case needs the generating machine's ANSI code page to be 1251**, because the
reference narrows by it: it checks `GetACP()` and fails loudly otherwise. Gates: the dump's raw
bytes, `GetString`, every stored key rebuilt (the `S_ASC` keys witness the narrowing, the others the
swap), the key-level seeks, a seek by each record's own text, the queries.

### Decisions

1. **`'W'` on a `0x32` table is VFP 9's Blob and refused**, on every other version CodeBase's wide
   string — the reference reads it as a wide string everywhere and would misread a VFP 9 Blob.
   `'O'` is not a VFP type and is read on any version, as the reference does.
2. **`STR(W)` narrows by the table's code page, not the host's** (ADR-77). On a Windows host whose
   ANSI code page is the table's they agree, which the corpus witnesses; elsewhere the port is the
   deterministic one. Windows' best-fit is reproduced by the supplement. A multi-byte code page is
   refused.
3. **`GetString` returns the stored characters**, U+2000 tail included, as it returns a `'C'`
   field's blanks; the documentation says what the tail is.
4. **Only the bare field and `STR()` of a bare `W` are evaluated over a wide value.** Concatenation,
   `ASCEND`, `DESCEND` over one are refused rather than left unwitnessed; collated Unicode keys stay
   deferred (`PORTING-PLAN.md`).
5. **ADRs:** ADR-76 (the two types read and keyed, `W` told from Blob by version), ADR-77 (`STR(W)`
   by the table's code page with Windows' best-fit).

## 7a-6 — the VFP oracle, run unattended *(added 2026-11-14, at the user's call)*

Until now a VFP oracle run needed the user: start the VM, `DO` the script by hand, copy `out\` back.
That made every VFP question cost a round trip, and it is why 7a-7 (a `T` table) and 7a-9 (GENERAL
weights) were not on the table before. VMware Workstation's `vmrun` can do all of it from the host.

### What changes, in short

- **New:** `test-files-generator/vfp/run-in-vm.ps1 [-Script vfpfunc.prg] [-TimeoutSeconds 600]`.
  It checks the VM is running (it does not start it — the VM is the user's), clears the guest's job
  folder, copies the script in with a generated wrapper, runs `vfp9.exe -t` on the wrapper in the
  logged-in session, waits, and copies the job's `out\` back to the host's `vfp\out\`, which
  `copy-vfp-corpus.bat` already publishes from. It exits non-zero, saying why, on any failure.
- **The wrapper** is written by the `.ps1`, not checked in: `TRY … DO <script> WITH "<guest out>"
  … CATCH` into `done.txt` (`OK` or the error, procedure and line), `SET ALTERNATE` into
  `console.txt`, then `QUIT`. Without it, an error VFP does not trap opens a dialog nobody can see
  and the run hangs.
- **A timeout:** a run past `-TimeoutSeconds` has its `vfp9.exe` killed in the guest and fails —
  a dialog the wrapper could not prevent (a `MESSAGEBOX`, a "file not found") must not hang the host.
- **The second `vfpfunc.prg` run** goes into the corpus: `VFPFVAL.DBF` changes in records 65, 66 and
  77 only (the three `CTOD` rows changed after the first run), the two dumps in their `CTOD` lines
  and `VFPFUNC`'s `generated` date. Nothing in the suite reads those rows: they reach no row of the
  Visual FoxPro band, and the oracle's counts (77 records, 71 cases) do not move.

### Configuration

Nothing about the user's machine goes into the repository. `VFP_VM_VMX` names the `.vmx`;
`VFP_VM_USER` and `VFP_VM_PASS` are the guest's logged-in user, which `-interactive` requires;
`VFP_VM_EXE` overrides the guest's `vfp9.exe` (default `C:\Program Files\Microsoft Visual FoxPro
9\vfp9.exe`); `VMRUN` overrides the host's `vmrun.exe` (default VMware Workstation's install path).
A missing one is named, not guessed.

### Decisions

1. **The script never starts or stops the VM.** Starting it needs no credentials but takes minutes,
   and the guest must also have its user logged in, which only the user can see to.
2. **The wrapper is generated, not committed**, because it carries the guest paths and nothing worth
   reviewing. The oracle scripts stay runnable by hand exactly as before.
3. **`out\` on the host is emptied before the copy**, so a file left by another script's run cannot
   be published by `copy-vfp-corpus.bat` under this one's name.
