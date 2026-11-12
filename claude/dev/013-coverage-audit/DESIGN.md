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
