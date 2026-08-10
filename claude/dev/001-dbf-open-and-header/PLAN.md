# 001-dbf-open-and-header — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

Every test here states a **promise**, not a mechanism (`DEV_APPROACH.md` §4, "Tests assert the
contract, not the implementation"). Where a sub-step below lists tests, the wording *is* the
contract: if the sentence stays true after a rewrite, the test must stay green.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| **1 Unit** | `DbfHeader`, `FieldDescriptor`, `FieldDescriptorTable`, `FieldResolver`, `CodePageMap`, `MemoFileHeader`, the variant strategy — each from a `byte[]` in memory | Every branch and edge of the byte layer, exhaustively and in µs: 16-bit `C` lengths, the `X`/`Z` restore, null-bit ordinals, `0x31` flag validation, per-type length rules, the `0x32` split between type gate and flag gate |
| **2 Component** | `DbfOpener` over an in-memory `IRandomAccessSource`, usually holding real corpus bytes | The open *sequence*: header before descriptor region, companion resolved only when the version says so, `Table` disposing what it owns. No disk |
| **3 Fault injection** | Hostile input — corrupt content as hand-built bytes through an in-memory source, hostile I/O through Moq (`MockBehavior.Strict`). Enumerated below | What no corpus file can express, because every corpus file is valid: a bad type character, an impossible length, a header contradicting its own descriptors — plus short reads and `IOException` |
| **4 Golden / corpus** | `TableMetadataGoldenTests` — all five tables, opened through the public API, against the `header`, `[descriptors]` and `[fields]` sections of their dumps | **Whether we read the format correctly at all.** Layers 1-3 can pass on a self-consistent misreading of an offset; only this layer disagrees with the C library |

### Layer 3 in full — hostile input

Corrupt content is fed as bytes; only the last group needs a mock. Each row is one promise, and the
error each names is the one the C library raises for the same file, unless marked **stricter**.

**Header contradicts itself** — decided from the 32 bytes alone, so these belong to sub-step 2

| Injected | Promise |
|---|---|
| File shorter than 32 bytes | `ErrorCode.Data` |
| Zero-length file | `ErrorCode.Data` — not an empty table |
| `recordLen == 0` | `ErrorCode.Data`, and no division is attempted (`DBF-FORMAT.md` §2.6) |
| `headerLen < 33` | `ErrorCode.Data` — no room for even a terminator |
| `numRecs` negative — it is a **signed** i32 | `ErrorCode.Data` |
| `0x31` with `flags[5..7]` non-zero, or `flags[0..4]` outside {0,1} | `ErrorCode.Data` (Decision 9) |
| Any version other than `0x31`, with arbitrary flag bytes | Opens, flags ignored — the C library reads them for no other version (`D4OPEN.C:2151`) |
| `0x31` with `flags[4]` (long field names) | **Reported, not refused.** The header states the layout is in use; whether that layout can be read is the descriptor region's question, so the `NotSupported` rejection lives in sub-step 3 (Decision 10) |

**Header contradicts the file** — needs the file's length, so these belong to sub-step 7

| Injected | Promise |
|---|---|
| `headerLen` past EOF | `ErrorCode.Data` |
| `numRecs` larger than `(fileLen - headerLen) / recordLen` | `ErrorCode.Data` (Decision 6) |
| `headerLen + recordLen × numRecs` overflowing 32 bits | `ErrorCode.Data`, computed in 64-bit so it cannot wrap into a plausible value |

**Descriptor region contradicts the header**

| Injected | Promise |
|---|---|
| No `0x0D` terminator before the region ends | `ErrorCode.Data` |
| Terminator at offset 32 — zero fields | `ErrorCode.Data` (`D4OPEN.C:2748`) |
| Σ field lengths + 1 ≠ `recordLen` | `ErrorCode.Data` (`D4OPEN.C:2671`) |
| Type `'~'`, `'\0'`, or any unknown character | `ErrorCode.FieldType` |
| Type `W O V P Q R 1 2 3 4 5 6` — real to CodeBase, out of v1 scope | `ErrorCode.FieldType` (Decision 8) |
| Type `B H Y T 7 0` on a `0x03`/`0xF5` table | `ErrorCode.Data` — VFP-only types need `version >= 0x30` |
| Lowercase `'c'` in the type byte | **Not an error** — the type is upper-cased on read (`c4upper`, `D4OPEN.C:319`), so it is a `C` field |

**Field length is wrong for its type.** The point here is **not** to fail loudly — it is that a wrong
length can never produce a wrong value or a read outside the record (Decision 18). Rejection is for
compatibility with the C library, and it is a thin list (Decision 19).

*Lengths a release build of the C library rejects — so do we:*

| Injected | Promise |
|---|---|
| `I` or `P` of length ≠ 4 | `ErrorCode.Data` |
| `R` of length ≠ 2; `Q` of length ≠ 2 unless version `0x32` | `ErrorCode.Data` |
| `V` of length ≠ 16 unless version `0x32`; `5`, `1`, `6` of length ≠ 8 | `ErrorCode.Data` — moot in v1, since Decision 8 rejects these types earlier |

*Lengths the C library **accepts** — so do we, and the value stays contained:*

| Injected | Promise |
|---|---|
| `D` of length 7 | Opens. `D` is a text-shaped type, so the C reads the declared 7 bytes, fails to parse, and blanks — we do the same |
| `T`, `Y`, `B` or `H` declared narrower than its natural width | Opens. Reads the **natural** width (8/4/8/8), taking bytes from the following field, exactly as `f4dateTime` and friends do (`F4FIELD.C:1966-1998`) |
| The same, on the **last** field, so the natural width would leave the record | Reads to the end of the record and treats the rest as zero. The only case with no C behaviour to copy — CodeBase reads its own allocation slack there |
| `L` of length 2, `C` of length 0, `N`/`F` with `dec >= len` | Opens; each decodes exactly as the matching C accessor does |
| `M` or `G` of length ∉ {4, 10} | Opens — the familiar 4-or-10 rule is `#ifdef E4MISC`, debug-only (`D4OPEN.C:2453-2463`) |
| `C` with `dec = 0x01` — the 16-bit length high byte | Opens with length 256+; `dec` is not a decimal count for `C`/`Z` |

*The containment property, which none of the above may break:*

| Property | Promise |
|---|---|
| Fuzzed descriptor bytes — arbitrary types, lengths and counts | **Either open throws, or every returned field satisfies `1 <= RecordOffset` and `RecordOffset + Length <= RecordLength`. Never neither.** A property test over generated inputs, not an example test |
| The same, then every field decoded | No read leaves the record buffer, for any input. A decode may reach into the *next* field when the C library does; it can never reach past the record |
| The same, with every optional length check disabled | Unchanged — containment rests on `1 + Σ len == RecordLength` alone (Decision 18) |

**`_NullFlags` is inconsistent** — the group with the most ways to be quietly wrong

| Injected | Promise |
|---|---|
| A `'0'` field not named `_NullFlags` (byte-exact, case-sensitive) | `ErrorCode.Data` (`D4OPEN.C:2611`) |
| A `'0'` field on a `0x03`/`0xF5` table | `ErrorCode.Data` |
| Nullable fields present, `_NullFlags` field absent | Opens; every such field reads as **not null** — there is no bitmap to index |
| `_NullFlags` shorter than `(nullableCount + 7) / 8` | Opens; the bit is read from the record at `nullFlagsOffset + byteNum` as the C does, and reads as **not null** only when that byte falls outside the record |
| A `'0'` field that is not the last field | Parsed as an ordinary field and counted in `Fields`, matching `d4numFields`, which only subtracts a **trailing** `'0'` (`d4declar.h:594`) |

**Memo companion**

| Injected | Promise |
|---|---|
| Header declares a memo, no companion file | `ErrorCode.Data` (`D4OPEN.C:2359-2360`) |
| Companion shorter than 8 bytes | `ErrorCode.Data` |
| FPT `blockSize == 0` | **Not an error** — byte granularity, and legal (`FPT-MEMO.md` §3.3) |
| Header declares no memo, a `.fpt` sits beside the table | It is never opened, and `HasMemo` is false |
| Companion named `.FPT`, `.Fpt` or `.fpt` on a case-sensitive filesystem | Resolved either way |
| Two companions differing only in case | Deterministic choice, documented — exact-case first, then case-insensitive |
| A **directory** named `TABLE.fpt` | `ErrorCode.Data`, not an unhandled exception |

**Hostile I/O — the only group needing Moq**

| Injected | Promise |
|---|---|
| Read returns fewer bytes than asked, at header / descriptor region / FPT header | `ErrorCode.Data` — a short read is never treated as zero-filled |
| `IOException` mid-descriptor | It propagates, and every source already opened is disposed |
| `Length` reports more than reads can deliver | `ErrorCode.Data` |
| Both the DBF and FPT source throw on `Dispose` | Both are attempted; neither is skipped because the other threw |
| Path missing, or a directory | The .NET exception, unwrapped and documented — not silently mapped to `ErrorCode.Data` |
| `TextEncoding` read with no provider registered | An exception naming `CodePagesEncodingProvider.Instance` (Decision 13) |

**No behavioural divergence on malformed lengths.** We copy each C accessor's width choice per type
(`DESIGN.md` Decision 18) rather than trusting a descriptor length that a corrupt file may have got
wrong — a short length is not evidence the *data* is short. The one place with nothing to copy is a
short fixed-width field at the end of the record, where CodeBase reads its own allocation slack;
there we clamp at the record end and treat the missing bytes as zero. That single case goes in
`SUMMARY.md` and `PORTING-PLAN.md` §8 at step close.

**What we deliberately do *not* reject**, because the C library does not and over-validation is its
own divergence: every "accepted length" row above; `N`/`F` wider than `F4MAX_NUMERIC` (a *create*-time
limit, `D4CREATE.C:1545`, not checked on open); duplicate field names — the first match wins, as
`d4fieldNumber` does; empty or all-NUL field names; a non-zero DBC backlink in the 263-byte reserved
area; a stray trailing `0x1A`; and an unrecognized `codePage` byte (Decision 13). Each gets a test
asserting the file **opens**, so a later hardening impulse cannot quietly tighten them without a red
suite.

**Corpus coverage.** Gated on all five cases: `DB3TYPE` (`0x03`, no memo, no reserved area),
`VFPTYPE` (`0x30`, the non-memo VFP types), `F2XMEMO` (`0xF5` — memo presence from `version & 0x80`
with `hasMdxMemo = 0x00`, the case a naive bit test fails), `VFPMEMO` (`X`/`Z` stored as `M`/`C`),
`VFPNULL` (`_NullFlags`, 14 descriptors against 13 fields, null-bit ordinals, two-byte bitmap).

Paths this step implements with **no corpus case behind them**, carried knowingly (`DESIGN.md` Q1):
version `0x31` + `flags[]` validation, a non-zero `codePage`, the `H` type, and the long-field-name
rejection. Each is covered at layer 1 only, and each is listed in `SUMMARY.md` as ungated so the
next person does not mistake a green suite for proof. The code-page map matters **before step 002**;
the rest can wait for a generator case.

**Expected values.** Header/descriptor/field expectations come from the corpus `.dump.txt` via the
parser built in sub-step 5 — never typed in from a spec. The FPT header is the one exception allowed
by `DEV_APPROACH.md` §4: bytes sliced from `net/corpus/VFPNULL.fpt` offsets 0-7, cited in the test.
Unit tests feed **hand-built input** buffers (fine) and assert the values a caller gets back; that
they read the *right* offsets is settled at layer 4, not by those tests.

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **Solution skeleton.** `net/CodeBase.Net.sln`; `net/CodeBase.Net` (net8.0, GPL v3 header, `GenerateDocumentationFile`, **no package references** — ADR-17); `net/tests/CodeBase.Net.Tests` and `net/tests/CodeBase.Net.Golden` (xUnit v3, AwesomeAssertions, Moq ≥ 4.20.2, `coverlet.collector`, `LiquidTestReports.Markdown` per `FOR-DEVELOPERS.md`); `InternalsVisibleTo` for both; a `Corpus` helper that finds `net/corpus/` from the repo root | `dotnet build` clean; a test asserting the corpus helper resolves a directory containing exactly the five expected `.DBF` files — so a broken path fails loudly instead of running zero cases later |
| 2 | **`DbfHeader` decodes the 32-byte header.** Every field, little-endian, plus `flags[8]` and `autoIncrementVal`. Brings `ErrorCode` and `CodeBaseException` with it, being the first code that can fail | Unit: *a header whose bytes say version `0x30`, 32 records, 87-byte records reports exactly that*; *a span shorter than 32 bytes is rejected with `ErrorCode.Data`*; *`flags[5..7]` non-zero, or `flags[0..4]` outside {0,1}, is rejected on a `0x31` header and ignored on a `0x30` one* (Decision 9); plus the **Header contradicts itself** rows above |
| 3 | **`FieldDescriptor` + `FieldDescriptorTable` decode the descriptor region.** One 32-byte descriptor; the scan to `0x0D`; the `C`/`Z` 16-bit length split across `len`/`dec` | Unit: *a `C` descriptor with `len=0x20, dec=0x01` reports length 288*; *a region with three descriptors then `0x0D` yields three*; *a region with no terminator is rejected with `ErrorCode.Data`*; *a `flags[4]` long-name layout is rejected with `ErrorCode.NotSupported`* (Decision 10) |
| 4 | **The format variant answers the four version questions.** `NormalizedVersion`, `HasMemo`, `AllowsVisualFoxProTypes`, `InterpretsDescriptorFlags` | Unit, as one theory over `0x30 0x31 0x03 0xF5 0x32`: *`0x31` normalizes to `0x30`*; *`0xF5` with `hasMdxMemo = 0x00` has a memo, `0x30` with `0x00` does not*; *`0x32` allows VFP types but has no memo and ignores descriptor flags* (Decisions 2, 3) |
| 5 | **`CorpusDump` parses a checked-in dump into typed expectations**, and the `header` and `[descriptors]` sections are gated against it at once. Header lines, `[descriptors]`, `[fields]`; optional trailing tokens (ADR-16); every section either read or declared unread, and an unknown one refused | Unit, against the real files: *`VFPNULL.dump.txt` yields 14 descriptors and 13 fields*; *`nullable=1` is read where present and absent elsewhere*; *a dump naming a section the parser does not know is refused*; *a dump missing a section or a header value it needs is refused*. **Golden:** *for all five tables, the values decoded from the real bytes equal the dump's `header` and `[descriptors]` sections* — which is what retires the self-consistency caveat hanging over sub-steps 2 and 3 |
| 6 | **`FieldResolver` produces the open-time field table.** Recomputed offsets, `X`/`Z` restored, upper-cased names, null-bit ordinals, `_NullFlags` split out, per-type length and type-set validation | Unit: *record offsets accumulate from 1 and ignore the stored offset*; *a descriptor storing `M` with flag `0x04` reports type `X` and stored type `M`*; *null bits number the nullable fields in physical order, so an interleaved plain field does not consume one*; *`_NullFlags` is absent from the field list and present as `NullFlags`*; *a `W` field is rejected with `ErrorCode.FieldType`*; *an `I` of length 3 is rejected with `ErrorCode.Data`*; *a `D` of length 7 is **accepted***; *field lengths summing to something other than `recordLen - 1` is rejected*; and the **containment property** — *for fuzzed descriptor bytes, either resolution throws or every field satisfies `1 <= offset` and `offset + len <= recordLen`* (Decisions 1, 7, 8, 14, 15, 18, 19). **Golden:** *for all five tables, the resolved fields equal the dump's `[fields]` section* — name, type, length, decimals and nullability, including the 14-against-13 asymmetry of `VFPNULL` |
| 7 | **`DbfOpener` opens a table over the boundaries, and `CodeBaseEngine`/`Table` expose it.** Includes `CodePageMap`, `MemoFileHeader`, the companion resolver | Component (in-memory source over corpus bytes): *opening `VFPMEMO` reports 6 fields, a memo, and block size 512*; *opening a table whose header declares no memo never asks the resolver for one*; *disposing the engine disposes the table's sources*. Plus every whole-file and I/O row of **Layer 3 in full** above |
| 8 | **The gate: `TableMetadataGoldenTests`.** One test case per corpus table via `[MemberData]`, comparing every value in the three sections against the dump, through the public API only | `dotnet test` green with five golden cases executed — asserted by the count guard from step 1, so an empty data set cannot pass as success |

**Where the layer-3 rows live.** Next to the code that owns the promise, not in one dumping ground:
the header rows go with sub-step 2, the descriptor rows with 3, the length and `_NullFlags` rows with
6, and the whole-file, memo-companion and I/O rows with 7. A fault test that has to construct a whole valid table to
exercise a one-field rule is a sign it was attached too high.

**Throughout, not as a step:** every public member gets docgen-conformant XML docs as it is written
— **load the `docgen-skill` skill before the first `///`** (ADR-15). `SonarQube.Analysis.xml` needs
review once the projects exist (`CLAUDE.md` housekeeping); expect no exclusions, but check.

## Gate

```bash
dotnet test net/CodeBase.Net.sln
```

Green, with `TableMetadataGoldenTests` reporting **five** executed cases. That second clause is part
of the gate, not commentary: a data-driven suite that silently discovers nothing is the most likely
way this step passes while proving nothing.

## Risks

| Risk | Cheapest thing that exposes it early |
|---|---|
| **A golden suite that runs zero cases** — bad corpus path, empty `[MemberData]`, a parser silently skipping a section | The step-1 helper test asserting the five files exist, plus the executed-case count in the gate. Both land before any decoding is written |
| **The dump parser is too tolerant** and quietly drops what it cannot read, so the gate compares nothing | Sub-step 5 asserts exact counts (14/13, 9/9) against real files rather than "parses without throwing" |
| **We reproduce a spec misreading** — the specs were never adversarially re-verified (R11) | Layer 4 is exactly this check, on five files. The dump parser is deliberately early, at sub-step 5, so that every later sub-step is gated against real bytes as it lands rather than four sub-steps afterwards |
| **Contract drift into implementation tests** — mocks asserting call order, tests re-deriving expected values from the code's own logic | The rule now in `DEV_APPROACH.md` §4; the tests above are phrased as promises, and `Verify` appears only in the three fault cases where the interaction *is* the requirement |
| **Ungated paths mistaken for proven** — `0x31` flags, marked code pages, `H`, long field names | Listed under Corpus coverage above and repeated in `SUMMARY.md`; the code-page one is flagged as blocking step 002, not this step |
| **Over-validation** — rejecting files the C library and VFP open happily, which makes us *less* compatible while looking more careful. The first draft of this plan did exactly that, for eight field types | The "what we deliberately do not reject" list above is itself a test group asserting those files **open**. Any future tightening turns it red, which is the point |
| **Containment silently coming to depend on a validation check**, so relaxing one for compatibility quietly opens a read past a field | The property test runs with the optional length checks disabled (Decision 18). If containment ever needs them, that test fails and says so |
| **Copying an accessor's width from memory instead of from the source** — the C is not uniform, and the per-type table in `DESIGN.md` Decision 18 is the whole contract | Each row cites the C function it came from; step 002 re-checks them against `F4FIELD.C`/`F4LONG.C` before the decoders are written |
