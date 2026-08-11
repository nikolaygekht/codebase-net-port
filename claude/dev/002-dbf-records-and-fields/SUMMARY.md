# 002-dbf-records-and-fields — summary

Written when the step closes. **Assume this is the only file from this folder anyone reads again.**

**Closed:** 2026-08-11. **Gate passed.** Capability advanced: `DBF-READ`, record half — only memo
payloads and the binary-marked types remain, and those are step 003.

No commit hash here, for the same reason the root `STATE.md` header carries none: a file cannot name
the commit it is part of. `git log` over this folder is the record.

## What shipped

A table now has a cursor and every ordinary field can be read through it. **630 tests green**, up
from 341: 378 unit and component, 252 golden.

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");

for (GoResult go = table.Top(); go == GoResult.Ok; )
{
    FieldDefinition name = table.Fields["NAME"];
    string text = table.GetString(name);       // code page decoded, blanks kept
    bool missing = table.IsNull(name);
    if (table.Skip(1) != SkipResult.Moved) break;
}
```

**Library:** `RecordPosition` (the cursor triple as a pure state machine), `RecordBuffer` (the one
bounds-checked accessor), `RecordReader` (record number to file offset), `BlankRecord` (the
type-aware blank template), `FoxDate`, `FoxNumeric`, `FoxDateTime`, `FoxCurrency`,
`FieldValueDecoder` (the per-type matrix), the `GoResult` and `SkipResult` enums, and on `Table`:
`Go`, `Top`, `Bottom`, `Skip`, `RecordNumber`, `Eof`, `Bof`, `Deleted`, `GetRawBytes`, `GetString`,
`GetBoolean`, `GetInt32`, `GetDouble`, `GetDecimal`, `GetDate`, `GetDateTime`, `IsNull`.

**Tests:** `CorpusDump` reads `[records]` (`DumpRecord`, `DumpValue`, `DumpEscape`) — no dump section
is deferred any more. `RecordGoldenTests` is the gate; `FoxNumericGoldenTests`, `TextGoldenTests` and
`TableScanGoldenTests` sit beside it. The golden project registers the code-page provider, which is the host's job and not
the library's (ADR-17).

## Deviations from the design

- **`BlankRecord` is a class the design did not name.** Blanking is per-field, not one fill: the
  fixed binary types blank to zero and everything else to spaces (`f4blank`, F4FIELD.C:135-169). The
  C library builds the template once at open and copies it (D4OPEN.C:440-457), and this mirrors that
  rather than putting a type switch inside `RecordBuffer`.
- **`RecordPosition` needed two transitions the design did not list.** `d4skip` clears the
  beginning-of-table flag before it decides where to go (d4skip.c:1149) and sets it again in three
  separate places. Decision 1 said a successful `Go` was the only thing that cleared a flag; that was
  incomplete, and the design was corrected during the review rather than during execution.
- **`FoxDateTime.ToText` exists for the gate.** The dump records a datetime as the string `f4dateTime`
  produces, which rounds the seconds and renders a blank date as spaces. Reproducing that rendering
  is how the stored value is gated; `GetDateTime` returns the unrounded moment.
- **Sub-step 8a concluded "no API".** See ADR-22.
- **Two flag-raises the C library performs were dropped as provably dead.** `d4skip`'s empty-table
  path sets `bofFlag` explicitly, twice, and both are redundant: the end-of-file position of an empty
  table *is* record one, and moving there raises the flag by the one rule that governs it. The single
  input that would tell them apart — a negative record count — is refused when the table is opened
  (`DbfHeader.cs:165`). Found by mutation: deleting them failed nothing. Behaviour is identical and
  the empty-table flags are asserted after `Top`, `Bottom` and a skip either way.

## What this step proved

Backed by a passing corpus assertion over **all seven tables and every one of their 32 records**:

- **The record offset formula.** Verified by mutation: shifting it by one record turns 28 golden
  tests red across every table.
- **A table walked four ways gives the same records in the same order** — by record number, forwards
  from the top, backwards from the bottom, and backwards starting from past the end — with each
  record identified by a field the dump shows to be unique across all thirty-two, and the traversals
  ending where they should (past the last record forwards, *on* record one backwards). Mutation
  checked against four traversal bugs that every per-record assertion would have passed: a skip that
  silently jumps a record, a `Bottom` landing one early, a wrong end-of-file position, and a walk
  stopping one short. Each fails 7 to 14 of these tests.
- **Every cursor-flag transition is load-bearing**, verified by mutating each one in turn: dropping
  the skip's flag-clear fails 1 test, `Go` no longer clearing end-of-file fails 4, no longer clearing
  the beginning flag fails 3, an empty table no longer reporting both ends fails 5, and a backwards
  skip no longer restoring the end-of-file flag fails 2.
- **`double.Parse` reproduces `c4atod` bit for bit** on all **224** numeric values (`N` and `F`
  fields), compared on the bit pattern so that `-0.0` and a wrong last bit cannot pass. This was the
  step's chief risk and **Q2 is closed**. It matters more than it looks: the body of `c4atod` is
  **not in this source drop**, so the design's fallback of "port it verbatim" was never available.
- Raw bytes of every ordinary field of every record; `long=` for every `I`; `str=[...]` for every `D`
  and every `T`; `dbl=` for every `N`, `F`, `B` and `Y`; the `deleted=` flag; every `null=1` mark and
  the `_NULLFLAGS` bitmap they come from.
- **Text decodes to the generator's documented input** — `Привет, мир`, `Компьютеры`, `中文测试`,
  `乗亅丄亊丂俓` — space-padded to the declared width, under both a single-byte and a multi-byte code
  page, with an unmarked table falling back to cp437.
- **A character cut in half by a field boundary yields its whole characters plus U+FFFD**, in all 32
  records of `CP936.CUT`, and never throws.
- **The Julian arithmetic round-trips every day from 1800 to 2200** against .NET's own calendar
  (146,000 days), which is where a leap-year rule off by one would show.

## Deferred

- **Memo payloads and the binary-marked types `X`, `G`, `Z`** — step 003. The gate counts what it
  skips, so the skip cannot grow silently.
- **A `GetTrimmedString` accessor** — evaluated and declined for now (ADR-22).

**Ungated — no corpus case exists, so these rest on reading the C:**

- The `H` and `7` field types. No table has one.
- **The deletion flag in its true state.** All 224 corpus records are `deleted=0`. Covered by
  component tests over hand-built images only.
- **The blank record.** No dump shows one, so `BlankRecord` is unit-tested against `f4blank` as read.
- **Millisecond rounding in a datetime.** All 64 corpus datetimes fall on a whole second, so the
  round-up-at-half rule is exercised only by unit test.
- **A currency field with a scale other than four** cannot exist — `d4create` hard-codes `(8, 4)`.
  This is closed, not a gap.

**Two more conversion bodies are missing from `original/source/`**, alongside `c4atod`: `c4atoi` and
`c4atol` (declarations only, d4declar.h:227-228), as are `c4currencyToA` and `c4ltoa45`. Anywhere
their exact edge behaviour matters and the corpus does not witness it, the port is a reasonable
reading rather than a port. The one place this shows is `FoxDate`'s treatment of a space inside a
date, which is taken as a zero.

## For the next step

- **`CorpusDump` already parses the memo lines** — `ref=`, `len=` and the payload are all read and
  kept. Step 003 adds assertions, not parsing.
- **The gate counts its skips.** `RecordGoldenTests` asserts that fields asserted plus memo fields
  skipped equals the field count, so wiring memo decoding in means the skip count drops and the
  assertion holds it honest.
- **`FieldValueDecoder` is where the type matrix lives.** `X`, `G` and `Z` join it there, and its
  `NaturalWidths` table is what decides how many bytes a memo reference occupies.
