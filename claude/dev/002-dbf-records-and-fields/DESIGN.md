# 002-dbf-records-and-fields — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

Position on a record and read the value of every ordinary field, matching what the C library reports
for the same bytes. **Gate:** for all seven corpus tables, the `[records]` section of the dump — every
field's raw bytes, the `dbl=` value for `N`/`F`/`B`/`Y`, `long=` for `I`, `str=[...]` for `D` and `T`,
the `deleted=` flag, and the `null=1` tokens with the `_NULLFLAGS` bitmap they come from. That section
is the one thing `CorpusDump` declares deliberately unread today, so this step is exactly "make the
other half of the corpus mean something".

**Capability:** `DBF-READ`, record half (`PORTING-PLAN.md` §5)  ·  **Governing spec(s):**
`specs/DBF-FORMAT.md` §5-6, `specs/API-ERRORS.md` §3

## Not in this step

| Deferred | To | Why |
|---|---|---|
| **Memo payloads** — the FPT reader, block chains, payloads spanning blocks | **003** | A second subsystem with its own spec (`FPT-MEMO.md`). Two subsystems in one step is against `DEV_APPROACH.md` §1 |
| **The binary-marked types `X`, `G`, `Z`** and the in-record memo *reference* | **003** | All three live in `F2XMEMO`/`VFPMEMO`, so 003 gets one coherent slice: everything memo-backed plus the binary variants. `Z` needs no FPT, but splitting it from its siblings buys nothing |
| Index-ordered navigation (`d4skip`'s `tagSelected` branch) | `CDX-READ` | No index reader exists. This step is the **natural-order** path only, which is the branch taken when no tag is selected |
| Writing, appending, `d4delete`, locking, transactions | `WRITE`, `LOCKING` | Read path only |
| Record caching and `hiPrio` optimization | never, probably | `d4skip` toggles `dataFile->hiPrio` around its `d4go`; that is a buffer-pool hint with no observable behaviour |

`H` and `7` are also not exercised: no corpus table has one. Their decoders are written where the
per-type table demands them, but they are **ungated** and must be listed as such in `SUMMARY.md`.

## Classes

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `RecordBuffer` | Entity | Holds one record's bytes and hands out a span for a field, clamped to the record | The single bounds-checked accessor Decision 18 of step 001 promised. Containment is a property of *this* type |
| `RecordPosition` | Entity | The cursor triple — record number, EOF flag, BOF flag — and the transitions between them | Pure: no I/O, so every navigation edge is a unit test |
| `RecordReader` | Controller | Reads the bytes of record *n* through an `IRandomAccessSource` into a `RecordBuffer` | Owns no handles; computes the offset from header length and record length |
| `FoxNumeric` | Entity | Turns the ASCII digits of an `N`/`F` field into a double, bit-for-bit as `c4atod` does | Decision 7 — the step's chief risk lives here |
| `FoxDate` | Entity | `YYYYMMDD` to a date, and blank to none | |
| `FoxDateTime` | Entity | The 4-byte Julian day plus 4-byte milliseconds pair to a date and time | |
| `FoxCurrency` | Entity | The 8-byte scaled integer to a decimal | |
| `FieldValueDecoder` | Entity | Dispatches a field's bytes to the decoder its type calls for, and refuses combinations the C refuses | One place holding the per-type matrix, so the matrix is testable as a table |
| `Table` (extended) | Controller | Gains the navigation verbs and the typed value accessors | Already the public façade; no new public entry point |

## Public surface

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");

table.RecordCount;                       // already exists, from the header
GoResult r = table.Go(1);                // Ok | NoRecord
table.Top(); table.Bottom();
SkipResult s = table.Skip(1);            // Moved | Eof | Bof
table.RecordNumber;                      // long; RecordCount + 1 at EOF
table.Eof; table.Bof; table.Deleted;

FieldDefinition f = table.Fields["NAME"];
table.GetString(f);                      // code page decoded, trailing blanks kept (Decision 16)
table.GetRawBytes(f);                    // the field's bytes, verbatim
table.GetDouble(f); table.GetInt32(f);
table.GetDate(f); table.GetDateTime(f);  // DateOnly? / DateTime? — null when blank
table.GetBoolean(f); table.GetDecimal(f);
table.IsNull(f);
```

**Deliberately not exposed yet:** any memo accessor, `GetBytes` for binary types, a record-object
abstraction (`table.CurrentRecord`), or an enumerator over records. The last one is tempting and
cheap, but `foreach` over a table with a mutable cursor underneath is a design question of its own —
and nothing needs it to pass the gate.

## Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| record reads | `IRandomAccessSource` | `InMemorySource` over a hand-built table image | every navigation edge on a table of 0, 1 and 3 records, layer 2 |
| read failures | `IRandomAccessSource` | `FaultySource` (hand-written — no mocking library can proxy a `Span<byte>`) | a file truncated mid-record, an `IOException` on the record read, layer 3 |
| the record itself | `RecordBuffer` | constructed from a `byte[]` | every decoder, layer 1, with no file anywhere |

The decoders take spans and return values, so **layer 1 needs no seam at all** — which is the point of
keeping them entities.

## Decisions

1. **Position is an explicit triple, copied from the C.** `d4bof`/`d4eof` return *stored flags*
   (`d4data.c:263`, `d4data.c:350`), not computed predicates, and the flags do not follow from the
   record number alone — an empty table is **both** BOF and EOF (`d4goEof`, `d4go.c:475-478`). So
   `RecordPosition` carries all three and the transitions are the unit under test. **The reset rule is
   part of the machine:** a *successful* `d4go` clears both flags together (`data->bofFlag =
   data->eofFlag = 0`, `d4go.c:326`), and nothing else clears them — so `Bof` survives until the next
   successful positioning, and `d4goEof` sets `eofFlag` while leaving `bofFlag` alone except on the
   empty-table path.

2. **EOF is a real position with a blank record.** `d4goEof` sets `recNum = recCount + 1`, sets the
   flag, and **blanks the record buffer** (`d4go.c:472-480`). So `RecordNumber` is `RecordCount + 1`
   at EOF and every field there reads as blank rather than as stale bytes from the last record. Copied
   exactly: a reader that forgets the blanking returns the previous record's data at EOF, which is the
   worst kind of wrong — plausible.

3. **BOF leaves the cursor *on record 1*.** Skipping backwards past the start goes to record 1, sets
   `bofFlag`, restores the previous EOF flag, and returns `r4bof` (`d4skip.c:1195-1203`). So `Bof` is
   true while record 1 is readable — not a separate empty position. Non-obvious, and a reader that
   models BOF as "before everything, nothing readable" diverges.

4. **Out of range is a *return value*, not an exception.** `d4go` past the end blanks the record, sets
   the position invalid, and returns `r4entry` — raising `e4read` only if `CODE4.errGo` is set
   (`d4go.c:234-243`). `PORTING-PLAN.md` §4 maps `r4*` flow values to return-value enums, so `Go`
   returns `GoResult.NoRecord` and throws nothing. **`errGo` is not ported:** a host that wants an
   exception checks the result, and a library-wide "make this throw" switch is a global mode that
   makes every call site ambiguous.
   *Rejected — throwing.* Reading past the end while scanning is ordinary control flow, and
   exceptions for it would make the common loop cost a `try`.

5. **`Go(0)` and `Go(-1)` are rejected, and this is a deliberate divergence.** The C's range guard is
   `recNo < 0 || d4recCountLessEq( data, recNo ) == 0`, and `d4recCountLessEq` returns 1 when
   `count <= recCount` (`d4data.c:644-657`) — so it rejects only `recNo > recCount`. **Zero passes**,
   and the position arithmetic then yields `headerLen - recordLen`, reading *header bytes as a record*.
   The `E4PARM_HIGH` block above rejects `recNo < 0` only, and that switch **is on in the shipped
   configuration** (`D4all.h:80`, undefined only under `S4SPEED_TEST`) — worth contrasting with step
   001's `E4MISC` finding, where the checks were off by default. We throw
   `ArgumentOutOfRangeException` for `n <= 0`: record numbers are 1-based, and decoding the header as a
   record is not a behaviour to reproduce, it is a bug to refuse.

6. **`Skip` from an invalid position is an error.** `d4skip` returns `e4info` when `recNum < 1`
   (`d4skip.c:1115-1122`), so it is not a silent no-op — and `errSkip` gates only whether the error is
   *raised*, never the returned `e4info`. Ported as `CodeBaseException`, since it means the caller has
   not positioned yet — a programming error, not a data condition. The asymmetry with Decision 4 is
   deliberate and follows `PORTING-PLAN.md` §4: `r4entry` is an `r4*` flow value and becomes a return
   value, `e4info` is an error code and becomes an exception.

7. **The numeric parse must produce the same `double` as `c4atod`, bit for bit — and this is the
   step's chief risk.** The gate compares against `dbl=%.17g` values that came out of the C's own
   hand-rolled ASCII-to-double: `dbl=-9999999.9989999998` for `"-9999999.999"`. .NET's parser is
   correctly rounded; `c4atod` may not be. **Plan:** implement with `double.Parse` first and compare
   every corpus value; on any disagreement, port `c4atod` verbatim and keep the comparison as the
   regression test. Deciding this by reading the C is slower and less certain than letting 224 real
   values answer it.

8. **`-0.0` must survive.** `VFPTYPE` record 2 stores `F_B` as `-0.0` and the dump says `dbl=-0`. Any
   normalization through `Math.Abs`, a `0.0 +` or a formatting round-trip destroys it, and
   `KEY-COLLATION.md` shows negative zero also takes the positive path when keys are built. Asserted
   on the sign bit, not with `==`, because `-0.0 == 0.0` is true.

9. **Typed accessors refuse the combinations the C refuses.** `f4double` on a logical or datetime field
   raises `e4parm` and returns 0 under `E4PARM_HIGH` (`F4DOUBLE.C:279-292`) — the shipped
   configuration. So the per-type matrix has *three* outcomes per pair, not two: the natural decode, a
   documented cross-type conversion the C does perform, or a refusal. Two cross-type conversions are
   real and must be kept: `f4double` on a **date** returns `date4long` of it (`F4DOUBLE.C:296-297`),
   and on a **currency** it goes through `f4currency(field, 4)` and re-parses the string
   (`F4DOUBLE.C:333-335`) — which is how the corpus's `dbl=` values for `Y` were produced, so the gate
   depends on reproducing that path and not on a cleaner one.

10. **Per-type widths stand as step 001's Decision 18 wrote them, now partly verified.** Text-shaped
    types honour the declared length; `B`/`H`/`Y`/`T`/`7` read their natural width and ignore it, taking
    bytes from the following field where the descriptor is short. `f4double`'s dispatch confirms the
    fixed-width half (`F4DOUBLE.C:294-340`). The re-check STATE.md called for is **done for the
    accessors this step needs** and recorded here; `H` and `7` remain unverified because no corpus
    table has one.

11. **Null-ness is separate from the value, and reading it never blanks anything.** `VFPNULL` proves
    assignment is not undone by nulling — `N_C` holds `"ALPHA     "` with `null=1`. So `IsNull(f)`
    reads the `_NullFlags` bitmap by the field's null-bit ordinal, and the value accessors are
    unaffected by it. A caller that wants "null means no value" writes that itself; a library that
    blanked the bytes would lose data the file still holds.

12. **`Deleted` is `record[0] != ' '`, not `== '*'`.** `d4deleted` returns `*data->record != ' '`
    (`d4data.c:344`); only an `E4MISC` build complains about a third value. A file with a stray byte
    there reads as deleted, which is what the reference does.

13. **Value accessors live on `Table`, keyed by a `FieldDefinition`.** `FieldDefinition` stays a pure
    value object with no back-reference to its table.
    *Rejected — accessors on the field, as `PORTING-PLAN.md` §4's example shows
    (`t.Fields["NAME"].GetString()`).* That is the C's shape (`FIELD4` holds a `DATA4*`) and reads
    better at the call site, but it means a public value object holding its container, and step 001's
    property test builds `FieldDefinition`s with no table at all — they would gain accessors that throw.
    **Consequence:** `PORTING-PLAN.md` §4's worked example needs updating when this lands. Flagged for
    review, because it is the one decision here that changes a documented API shape.

14. **The invalid position is a *fourth* state, not a flavour of EOF.** Both of `d4go`'s failure paths
    set `data->recNum = -1` and **leave `eofFlag`/`bofFlag` untouched** — the out-of-range guard
    (`d4go.c:236`) and a failed `dfile4goData` (`d4go.c:317`). So after `GoResult.NoRecord` the record
    is blank, `RecordNumber` is **-1**, and `Eof`/`Bof` still report whatever they reported before the
    call: EOF is *not* implied by falling off the end through `Go`. This is the state Decision 6's
    throwing `Skip` tests for, and `RecordPosition` models it explicitly rather than deriving it.

15. **Text decoding is settled by ADR-21, not by `Encoding`'s defaults.** An unmarked or unrecognized
    table decodes as **cp437**; decoding is **best-effort and never throws**, so a character the field
    boundary cut in half yields its complete characters plus U+FFFD and an undefined byte passes
    through as the code page maps it; and the gate asserts **decoded strings**, not only raw bytes.
    `GetRawBytes` is the escape hatch for a caller who must tell data from damage. This closes Q3, Q4
    and Q5 below.

16. **`GetString` returns the field's full declared width, trailing blanks and all** — what `f4str`
    does (`F4STR.C:206-243`, no trimming and no `f4trim` in this drop). Padding is information the file
    holds; `TrimEnd()` at the call site is one call, and un-trimming is impossible. **An opt-in
    automatic trim is deferred to an evaluation, `PLAN.md` step 8a** — deliberately an evaluation and
    not an implementation, because the obvious shape (a table- or engine-wide `TrimTrailingBlanks`
    flag) is the same global-mode objection Decision 4 raised against porting `errGo`: every call site
    becomes ambiguous about what it returns. Whatever it concludes, the gate keeps asserting the padded
    form, because that is what the bytes are. Closes Q6.

## Open questions

| # | Question | Status |
|---|---|---|
| Q1 | **Is currency scaled by the declared decimals or always by 4?** | **Closed — always 4, by citation.** `Y` is a fixed 4-decimal type: `d4create` hard-codes the descriptor to `(8, 4, 0x04)` (`D4CREATE.C:1569-1571`), so a `Y(8,2)` table is not something the reference can even produce; `f4currency` never reads `field->dec` — its `numDec` is the *caller's*, capped at 4 (`F4FIELD.C:1653-1697`); and `DBF-FORMAT.md` §6.9 already records the layout as int64 LE × 10⁴. Range is therefore `-922337203685477.5808` to `922337203685477.5807`, which the decoder's overflow test should use. **No generator case needed.** Note the design's original resolution path was a dead end anyway: `c4currencyToA`'s body is absent from this source drop (declaration only, `d4declar.h:455`), as `DBF-FORMAT.md` §6.9 flags |
| Q2 | **Does `double.Parse` agree with `c4atod` on all 224 numeric corpus values?** Decision 7. | **Open** — the gate itself answers it, on first run. The count is exact: `N`/`F` fields only, `F_N` 32 + `F_F` 32 + `N_N` 32 + `N_F` 32 + `PRICE` 32 + `QTY` 64. The other 128 `dbl=` tokens are `B` and `Y`, which do not reach `c4atod` through an ASCII field |
| Q3 | **Is the unmarked-table default cp437 or cp1252?** | **Closed — cp437** (ADR-21, Decision 15). `i4init.c:387`'s Windows ANSI is a *collation* default for a GENERAL tag, not a text default |
| Q4 | **What does a `C` field yield when a multi-byte character is cut in half, and when a byte is undefined in the code page?** | **Closed — recover as much as the code page allows, never throw** (ADR-21, Decision 15): complete characters plus U+FFFD for the dangling byte, and an undefined byte passes through. `GetRawBytes` is how a caller tells data from damage |
| Q5 | **Does the gate cover `C` field *text*, or only its bytes?** | **Closed — text as well as bytes** (ADR-21, Decision 15). Expected strings come from the generator's documented input (`DEV_APPROACH.md` §4 permits this: the generator's own test data, not bytes we invented). `Привет, мир` and `中文测试` are the witnesses |
| Q6 | **Does `GetString` trim the field's trailing blanks?** Newly opened by the design review — nothing in the design had cited a C counterpart for `GetString`. | **Closed — no, it returns the padded width** (ADR-21, Decision 16), matching `f4str` (`F4STR.C:206-243`). So the gate expects `CP936.TEXT` = `中文测试` + 12 spaces and `CP1251.TEXT` = `Привет, мир` + 9. An **opt-in** automatic trim is a separate question, deferred to the `PLAN.md` step 8a evaluation |
