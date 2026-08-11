# 002-dbf-records-and-fields — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | Each decoder over spans: `FoxNumeric`, `FoxDate`, `FoxDateTime`, `FoxCurrency`, the `FieldValueDecoder` matrix, `RecordBuffer` bounds, and **every `RecordPosition` transition** as a pure state machine | Edges no corpus table has: an **empty** table (BOF and EOF at once), a one-record table, skipping ±1 across both ends, and every refused type/accessor pair. All seven corpus tables have 32 records, so the interesting positions are exactly the ones the corpus cannot show |
| 2 Component | `RecordReader` over `InMemorySource` on a hand-built image: offset arithmetic for records 1, 2 and *n*; `Table`'s verbs end to end against that image | That the offset formula and the cursor agree. A decoder can be perfect while every record is read one record too early |
| 3 Fault injection | `FaultySource`: a file truncated mid-record, an `IOException` on the record read, a `headerLen` that puts record 1 past EOF | That a short read **refuses** rather than zero-filling — a zero-filled record decodes as a plausible record of zeros and blanks |
| 4 Golden / corpus | The `[records]` section of all seven dumps, every ordinary field of every record, through the public API | Whether we match the real bytes at all. Layers 1-3 can pass on a self-consistent misreading of the currency scale or the datetime epoch |

**Corpus coverage.** Gated on all seven cases: `DB3TYPE` (`C N D L`), `VFPTYPE` (`C N F D L I B Y T`,
and the numeric extremes plus `-0.0`), `VFPNULL` (`null=1` and `_NULLFLAGS` per record, nullable
non-memo fields), `CP1251`/`CP936` (text decoding under a marked code page, the cut character, the
undefined byte), and the **non-memo fields of** `F2XMEMO`/`VFPMEMO`.

Touched but **uncovered by any corpus case**, and to be listed as ungated in `SUMMARY.md`: the `H` and
`7` field types; a table with a **deleted** record — all 224 corpus records are `deleted=0`, verified,
so the deletion flag is gated only in its false state. (A `Y` field with decimals other than 4 is *not*
on this list any more: Q1 is closed and `d4create` cannot produce one.) A
generator case with deleted records is the honest fix and is cheap; decide during execution whether it
belongs to this step or to `WRITE`.

**Expected values.** Per-field raw bytes, `dbl=`, `long=`, `str=[...]`, `deleted=` and `null=` all come
from `net/corpus/<NAME>.dump.txt`, which the C library wrote. The `_NULLFLAGS` line gives the stored
bitmap. **Two exceptions, both declared:** decoded *strings* for character fields are not in the dump
(the C transcodes nothing), so `Привет, мир` / `Компьютеры` / `中文测试` / `乗亅丄亊丂俓` come from the
generator's own documented test data — permitted by `DEV_APPROACH.md` §4, and the comments in
`case-cp1251.cpp` / `case-cp936.cpp` name the text beside every byte array. Hand-built table images in
layers 1-3 are **input**, never expectation.

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **`CorpusDump` reads `[records]`.** Per record: number, `deleted`, each field's escaped bytes and its optional `dbl=`/`long=`/`str=`/`null=` token, and the trailing `_NULLFLAGS` line. `[records]` leaves `DeferredSections`; memo lines are **parsed and kept**, not skipped, so 003 adds assertions rather than parsing | Unit, against the real dumps: *`VFPTYPE.dump.txt` yields 32 records of 9 fields*; *`\xHH` and `\\`/`\"` unescape to the right bytes*; *`VFPNULL` record 1 carries `_NULLFLAGS "\xFF\x03"` and ten `null=1` tokens*; *a record line the parser does not understand is refused*. Nothing else can land until this does — it is the gate's only source of truth |
| 2 | **`RecordBuffer` and `RecordPosition`.** The bounds-checked span accessor, and the cursor as a pure state machine | Unit: *a field span is clamped to the record, never past it* (property test over fuzzed offsets); *an empty table is BOF and EOF together*; *EOF is `RecordCount + 1`*; *skipping back past record 1 lands **on** record 1 with `Bof` true and the old `Eof` preserved* (Decision 3); *a successful `Go` clears **both** flags and nothing else does* (Decision 1); *the invalid position reports `RecordNumber` -1 and leaves `Eof`/`Bof` as they were* (Decision 14); *skip ±1 across both ends of a one-record table* |
| 3 | **`RecordReader` fetches record *n*.** Offset from `HeaderLength` and `RecordLength`; a short read refuses | Component over `InMemorySource`, layer 3 for the failures: *record 1 starts at `HeaderLength`*; *record *n* is `HeaderLength + (n-1) * RecordLength`*; *a file truncated mid-record throws rather than zero-filling*; *the source is not read at all when the position is EOF* |
| 4 | **Navigation on `Table`:** `Go`, `Top`, `Bottom`, `Skip`, `RecordNumber`, `Eof`, `Bof`, `Deleted` | Component: *`Go` past the end returns `NoRecord`, blanks the record and leaves the position invalid* (Decision 2/4); *`Go(0)` throws `ArgumentOutOfRangeException`* (Decision 5); *`Skip` before positioning throws* (Decision 6); *`Deleted` is true for any byte other than a space* (Decision 12). **Golden:** *walking all seven tables top to bottom visits `RecordCount` records in dump order with matching `deleted=`* — the first golden test of this step, and it needs no decoder at all |
| 5 | **The simple decoders and the type matrix:** `FoxDate`, the logical, the integer, the raw-bytes accessor, and `FieldValueDecoder`'s dispatch including its refusals | Unit: *`YYYYMMDD` decodes, `"        "` is none, `"20260231"` is none rather than a throw*; *`'T'`/`'t'`/`'Y'`/`'y'` are true and everything else false*; *`I` is 4 bytes little-endian, `-2147483648` and `2147483647` both*; *`GetDouble` on a logical throws* and *on a date returns `date4long`* (Decision 9). **Golden:** *`long=` for every `I` field of every record*, and *`str=[...]` for every `D` field* |
| 6 | **`FoxNumeric`, and the question Decision 7 poses.** `N`/`F` to double | Unit first, then **golden immediately**: *every `dbl=` value for every `N` and `F` field of all seven tables matches bit for bit*, compared on the bit pattern rather than with `==` so `-0.0` and the last-bit cases cannot slip through (Decision 8). If any value disagrees, `c4atod` gets ported verbatim before anything else proceeds — this is the step's chief risk and it is answered by the fifth sub-step, not the last |
| 7 | **`FoxDateTime` and `FoxCurrency`.** The Julian-plus-milliseconds pair and the scaled 8-byte integer, including `f4double`'s currency path through a 4-decimal string | **Golden:** *`str=[...]` for every `T` field*, including the zero datetime whose dump form is `[        00:00:00]`; *`dbl=` for every `Y` and `B` field*. Q1 is **closed** — the scale is always 10⁴ and no generator case is needed — so add the range check instead: *`-922337203685477.5808` and `922337203685477.5807` round-trip, and the decoder does not overflow* |
| 8 | **Text: `GetString` and the code page**, per **ADR-21** — Q3-Q6 all closed, so this sub-step is now implementation with nothing left to decide | Unit: *a lead byte with no trail byte yields the complete characters plus U+FFFD, and does not throw*; *a byte the code page leaves undefined passes through*; *an unmarked table decodes as cp437*; *a field narrower than its content is not over-read*. **Golden**, all strings **space-padded to the declared width** (Decision 16): *`CP1251` record 1 `TEXT` is `Привет, мир` + 9 spaces and `EXACT` is `Компьютеры` exactly, filling its `C(10)`*; *`CP936` record 1 `TEXT` is `中文测试` + 12 spaces, `TRAIL` is `乗亅丄亊丂俓` filling its `C(12)`, and `CUT` — `C(7)` given eight bytes — is `中文测` + U+FFFD in every one of its 32 records*; *every character field's raw bytes match the dump for all seven tables* |
| 8a | **Evaluate an opt-in automatic trim** (ADR-21, Decision 16). An evaluation, not an implementation: weigh a table- or engine-wide `TrimTrailingBlanks` flag against a second accessor (`GetTrimmedString`) against leaving it to the caller's `TrimEnd()`, and record the conclusion as an ADR — including "none of them, the call site is enough", which is a real outcome | The ADR itself. The two things it must confront: a mode flag reintroduces exactly the objection Decision 4 raised against porting `errGo` — every call site becomes ambiguous about what it returns, and a library that reads differently depending on a setting made elsewhere is hard to reason about; and whatever lands, **the gate keeps asserting the padded form**, so this can add a surface but can never change what `GetString` returns by default. If it does produce an API, it needs its own unit tests and a `PORTING-PLAN.md` §4 line; if it does not, say so and the question is closed for good |
| 9 | **The gate: `RecordGoldenTests`.** One data-driven suite over all seven tables asserting the whole `[records]` section, ordinary fields only, with memo fields explicitly skipped and *counted* so the skip cannot grow silently | The gate below. Plus a **mutation check**: change the record-offset formula by one record and confirm the suite goes red across every table, as sub-step 5 of step 001 did |

## Gate

```
dotnet test net/CodeBase.Net.sln
```

green, with `RecordGoldenTests` asserting, for **all seven** corpus tables and every one of their 32
records: the raw bytes of every ordinary field, the `dbl=` double bit-for-bit, the `long=` integer, the
`str=[...]` date and datetime forms, `deleted=`, every `null=1`, and the `_NULLFLAGS` bitmap. The suite
must report a non-zero count of records and fields asserted — a data-driven gate that discovers nothing
reports success having proved nothing, which is how step 001's property test was found vacuous.

## Risks

| Risk | Why it would make this step *wrong* | Cheapest early exposure |
|---|---|---|
| **`c4atod` differs from `double.Parse`** | Every numeric value in the library would be subtly off, and the optimizer's range comparisons would inherit it | Sub-step 6 runs the full golden comparison the moment the decoder exists — before datetime, currency or text work |
| ~~**The currency scale is wrong** (Q1)~~ — **retired.** `Y` is fixed at 4 decimals (`D4CREATE.C:1569-1571`, `F4FIELD.C:1653-1697`, `DBF-FORMAT.md` §6.9) | — | — |
| **The datetime epoch or the millisecond field is off by a day or a unit** | Every `T` value shifted, plausibly | The zero datetime and the `[        00:00:00]` blank form are both in the corpus; assert those two first |
| **Reading one record too early or late** | Every field of every record wrong in a way that still *parses* — the most plausible-looking failure available | Sub-step 4's walk asserts record numbers against the dump before any decoder exists, and the mutation check in sub-step 9 proves the suite can see it |
| **The deletion flag is gated only false** | A deleted record might read as live | Named in `SUMMARY.md` as ungated, and a generator case decided during execution |
| ~~**Text behaviour chosen by `Encoding`'s defaults rather than by us**~~ — **retired**, chosen deliberately in **ADR-21**: best-effort recovery, cp437 unmarked, `GetRawBytes` as the escape hatch | — | — |
| ~~**Trailing blanks** (Q6)~~ — **retired.** Padded, matching `f4str` (`F4STR.C:206-243`); an opt-in trim is evaluated in 8a and cannot change the default | — | — |
