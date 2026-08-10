# 001-dbf-open-and-header — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

Open a DBF file — and, when its header says one exists, its companion memo file — and expose the
table's *metadata*: the 32-byte header, the field descriptors as stored, and the field table as the
engine resolves it after open. **Verified by:** for all five tables in `net/corpus/`, the values C#
reports equal the `header`, `[descriptors]` and `[fields]` sections of the checked-in `.dump.txt`.

**Capability:** `DBF-READ` (partial — metadata half) (`PORTING-PLAN.md` §5)
**Governing specs:** `specs/DBF-FORMAT.md` §1–§5, §8; `specs/FPT-MEMO.md` §3.1, §3.5;
`specs/API-ERRORS.md` §3 (error codes)

This is the first C# in the repository, so the step also lands the solution skeleton
(`net/CodeBase.Net.sln`, `net/CodeBase.Net`, `net/tests/CodeBase.Net.Tests`,
`net/tests/CodeBase.Net.Golden`) and the corpus-dump test harness that every later golden test
reuses.

## Not in this step

| Deferred | To |
|---|---|
| Record navigation (`Top/Bottom/Skip/Go/Eof/Bof/RecNo`) and per-type field **value** decode | step 002 — gated on the `[records]` section of the same dumps |
| Memo **payload** reading (FPT blocks, `numChars`, block arithmetic) | a later step; 001 opens the FPT and decodes only its 8-byte file header |
| Anything that writes — including the header date stamp on close | `WRITE` (P2) |
| CDX / production index — beyond reporting the `hasMdxMemo & 0x01` bit | `CDX-READ` |
| Locking, open modes, share semantics — 001 opens read-only | `LOCKING` (P3) |
| String decoding of record text (the code page is *identified* here — Decision 13 — but nothing decodes yet) | step 002 |
| Long-field-name descriptor layout (`DBF-FORMAT.md` §4.2) | detected and **rejected**, not decoded — see Decision 9 |
| Large-file (>4 GB) offsets, compressed data/memos | out of v1 scope or later (`PORTING-PLAN.md` §2.2) |
| Benchmarks project | `HARDENING` |

## Classes

`CodeBase.Net` = public surface · `CodeBase.Net.Dbf` = DBF layout · `CodeBase.Net.Memo` = FPT ·
`CodeBase.Net.IO` = internal boundary (`PORTING-PLAN.md` §3.2).

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `DbfHeader` | Entity | Decodes the 32-byte file header from a span. | pure; all LE (`DBF-FORMAT.md` §2) |
| `FieldDescriptor` | Entity | Decodes one 32-byte on-disk field descriptor. | pure; carries the `C`/`Z` 16-bit length split (`dec` = high byte) |
| `FieldDescriptorTable` | Entity | Splits the descriptor region into descriptors, stopping at the `0x0D` terminator. | pure; missing terminator ⇒ `e4data` |
| `FieldDefinition` | Entity | The open-time view of one field: resolved type, length, record offset, binary/nullable/system flags, null-bit ordinal. | pure value; this is the `[fields]` view |
| `FieldResolver` | Entity | Turns stored descriptors + variant into `FieldDefinition`s — recomputed record offsets, restored `X`/`Z` API types, null-bit ordinals, per-type length validation. | pure; the only place type rules live |
| `IDbfFormatVariant` | Entity | Answers the three per-version questions the open path asks. | 3 members — see Decision 3 |
| `VisualFoxProVariant` | Entity | `IDbfFormatVariant` for `0x30` and `0x31`. | normalizes `0x31` → `0x30` |
| `LegacyVariant` | Entity | `IDbfFormatVariant` for every other version byte (`0x03`, `0xF5`, `0x32`, …). | memo presence from `version & 0x80`; carries its own version byte so the type gate stays `>= 0x30` |
| `MemoFileHeader` | Entity | Decodes the 8-byte FPT file header. | pure; **big-endian** `nextBlock`, `blockSize` |
| `CodePageMap` | Entity | Maps the header's language-driver byte to a `CodePage` value and, on demand, an `Encoding`. | pure lookup; **registers no encoding provider** — Decision 13 |
| `ErrorCode` / `CodeBaseException` | Entity | Carries the original `e4*` value out of a failure. | no behaviour (`PORTING-PLAN.md` §4.1) |
| `IRandomAccessSource` | Boundary | Reads bytes at an absolute file offset. | `Length`, `Read(long, Span<byte>)` — nothing else |
| `FileRandomAccessSource` | Boundary | `IRandomAccessSource` over a `FileStream`. | the **only** place `FileStream` appears |
| `IRandomAccessSourceFactory` | Boundary | Opens a path as an `IRandomAccessSource`. | one method |
| `ICompanionFileResolver` | Boundary | Finds a companion file whose extension differs from the requested one only in case. | one method; exists because of the `.DBF`/`.fpt` asymmetry |
| `CodeBaseEngine` | Boundary | Public entry point; owns the tables it opens and disposes them with itself. | composition root — the only place concrete boundaries are constructed |
| `Table` | Boundary | Public read-only handle exposing one open table's metadata. | owns its sources; `IDisposable` |
| `DbfOpener` | Controller | Sequences the reads that open a table and produces its metadata. | takes the three boundary interfaces; owns no handles, no byte layout |

The split that matters: `DbfOpener` decides *what to read next and what to reject*; it never touches
a byte layout, and every layout class is pure enough to test from a `byte[]`.

**Why `FieldDescriptor` and `FieldDefinition` are two classes.** The dump has two sections for a
reason: `d4create` rewrites `X`→`M` and `Z`→`C` and records "binary" in the flags byte, so the
stored type is not the type the engine reports (`VFPMEMO`: `BINMEMO` is stored `M`/`0x04` and
reported `X`). One merged class would silently lose that distinction — the corpus proves it exists,
so the model keeps it.

## Public surface

```csharp
// The host registers encoding providers, not the library — Decision 13.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

using var engine = new CodeBaseEngine();       // .DefaultEncoding: unmarked/unknown tables only
using Table t = engine.OpenTable("net/corpus/VFPMEMO.DBF");

t.Version;              // byte, as stored (0x30, 0xF5, …)
t.LastUpdate;           // (int Yy, int Mm, int Dd) — raw, see Decision 4
t.RecordCount;          // header numRecs
t.RecordLength;
t.HeaderLength;
t.CodePage;             // CodePage enum — Unmarked, Cp437, Cp850, Cp1252, Cp1250, Unknown
t.CodePageByte;         // the raw LDID, preserved even when unrecognized
t.TextEncoding;         // lazy; needs a registered provider. Nothing consumes it until 002
t.HasMemo;
t.MemoBlockSize;        // from the FPT header; null when there is no memo
t.HasProductionIndex;   // hasMdxMemo & 0x01
t.Fields;               // IReadOnlyList<FieldDefinition>, physical order,
                        //   excluding _NullFlags — Decision 14
t.Fields["NAME"];       // case-insensitive lookup (+ TryGet)
t.NullFlags;            // the _NullFlags field, or null — Decision 14
t.Descriptors;          // IReadOnlyList<FieldDescriptor> as stored — Decision 17
```

Deliberately **not** exposed yet: any record or field *value*, any navigation, anything that writes,
and the memo file's contents. `IRandomAccessSource` and friends stay `internal`
(+ `InternalsVisibleTo` for the two test projects) — they are seams, not yet an extension point.

Public members get docgen-conformant XML docs; **load the `docgen-skill` skill before writing the
first `///`** (`CLAUDE.md`, ADR-15).

### What client code looks like

The whole of this step, from a caller's point of view — open a table and print its structure:

```csharp
using CodeBase.Net;

using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("net/corpus/VFPNULL.DBF");

Console.WriteLine($"version 0x{table.Version:X2}, {table.RecordCount} records "
                + $"of {table.RecordLength} bytes, code page {table.CodePage}");

foreach (FieldDefinition f in table.Fields)
    Console.WriteLine($"  {f.Name,-10} {f.Type} ({f.Length},{f.Decimals}) @{f.RecordOffset}"
                    + (f.IsNullable ? $" null:bit{f.NullBit}" : ""));
```

```
version 0x30, 32 records of 87 bytes, code page Unmarked
  ID         I (4,0) @1
  N_C        C (10,0) @5 null:bit0
  N_N        N (10,2) @15 null:bit1
  ...
  N_I        I (4,0) @52 null:bit5
  ...
  N_M        M (4,0) @80 null:bit9
  TAIL       L (1,0) @84
```

Everything else is a property on the same two objects:

```csharp
if (table.Fields.TryGet("N_C", out FieldDefinition name))   // case-insensitive
    Console.WriteLine(name.Length);

table.HasMemo;                  // true — companion .fpt resolved and opened
table.MemoBlockSize;            // 512, from the FPT's big-endian header
table.NullFlags?.Length;        // 2 — the _NullFlags field is not in Fields
table.Descriptors[13];          // the stored _NullFlags descriptor, type '0'

// The X/Z rewrite is visible without digging into descriptors:
table.Fields["BINMEMO"].Type;         // 'X' — what the field is
table.Fields["BINMEMO"].StoredType;   // 'M' — what the byte says
```

Failures are exceptions carrying the original code, so a caller can be as coarse or as precise as
it likes:

```csharp
try { using Table t = engine.OpenTable(path); }
catch (CodeBaseException ex) when (ex.Code == ErrorCode.Data) { /* truncated, bad width, … */ }
```

**Read of this API surface:** the common case is two `using` lines and a `foreach`, with no
configuration, no builder, and no options object. The one thing a caller must know that a modern
format would not require — registering an encoding provider — is only needed when reading *text*
(Decision 13), so the structure-reading path above works with nothing registered.

## Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| file reads | `IRandomAccessSource` | in-memory fake over a `byte[]` (usually loaded from a corpus file) | open sequencing, header/descriptor decode — layer 2 |
| read failures | `IRandomAccessSource` | **hand-written `FaultySource`**, not Moq — see the note below | truncation mid-header, short read, `IOException` — layer 3 |
| path → source | `IRandomAccessSourceFactory` | fake dictionary `path → byte[]` | companion open without touching a disk — layer 2 |
| companion lookup | `ICompanionFileResolver` | fake dictionary | `.fpt` found beside `.DBF`; memo declared but **absent** ⇒ `e4data` — layers 2 and 3 |

**`IRandomAccessSource` cannot be mocked, and that is structural.** Its `Read` takes a
`Span<byte>`, and a mocking library cannot express a ref-struct parameter in an expression tree at
all — `It.IsAny<Span<byte>>()` does not compile. So the one boundary that most wanted a mock is the
one that cannot have one, and its fault injection is a hand-written `FaultySource` instead.
No loss: the resulting tests assert `source.IsDisposed` after the failure, which states the outcome
rather than the call that produced it, and is what `DEV_APPROACH.md` §5 asks for anyway. Moq stays in
the stack for the boundaries still to come (`IClock`, `IFileLocks`), which take no spans.

Layer-4 tests use the real `FileRandomAccessSource` against `net/corpus/` and no doubles at all
(`DEV_APPROACH.md` §5).

## Decisions

1. **The descriptor's stored `offset` is ignored; record offsets are recomputed** by accumulation
   from 1 (`DBF-FORMAT.md` §3, D4OPEN.C:432-433). The corpus happens to agree, and a test asserts
   that agreement — but the recomputed value is what we use, as the C library does.
2. **Memo presence follows the version, not one bit.** `hasMemo = (version == 0x30) ? (hasMdxMemo &
   0x02) : (version & 0x80)` (`FPT-MEMO.md` §3.5), evaluated on the *normalized* version so
   `0x31` behaves as `0x30` — verbatim `D4OPEN.C:2351-2361`, read during this design. The corpus
   makes this concrete: `F2XMEMO.DBF` is `0xF5` with `hasMdxMemo = 0x00` and *does* have a memo — a
   naive `hasMdxMemo & 0x02` test would miss it. **Quirk, deliberately reproduced:** a `0x32` (VFP9)
   table takes the `else` branch, and `0x32 & 0x80 == 0`, so the C library opens a VFP9 table
   *without* its memo file. We match that rather than "fix" it; a divergence here would be invisible
   and untestable (this settles the design's Q2).
3. **Version behaviour is one strategy object resolved at open** (`DEV_APPROACH.md` §3.2), with four
   members: `NormalizedVersion`, `HasMemo(header)`, `AllowsVisualFoxProTypes`, and
   `InterpretsDescriptorFlags`. The last two look alike and are not: the type gate is
   `version >= 0x30`, but the descriptor flag byte — nullable `0x02`, binary `0x04`,
   auto-increment `0x08`, auto-timestamp `0x10` — is read only under `d4version(d4) == 0x30`
   exactly (`D4OPEN.C:324-380`). So a `0x32` table gets VFP field *types* while its nullability and
   binary flags are ignored outright, and a `0x03`/`0xF5` table has no nullable fields whatever its
   flag bytes say. Both fall out of the class split for free.
   The split is `normalizedVersion == 0x30` (`VisualFoxProVariant`) versus everything else
   (`LegacyVariant`) — **not** "VFP versus FoxPro 2.x", because `0x32` sits on both sides: the C
   library treats it as legacy for the memo rule and `compatibility` (which becomes 25,
   `D4OPEN.C:2205-2213`) while still allowing VFP-only field types, gated on `version >= 0x30`
   (`D4OPEN.C:2588-2612`). Giving `LegacyVariant` its own version byte reproduces both halves with
   no `0x32` special-casing anywhere. *Rejected:* an enum plus `switch` at each site — precisely the
   scattered `if (version == 0x30)` the design rules forbid. `Compatibility` (30 vs 25) joins this
   object in step 002, where it first matters (memo-reference width).
4. **`LastUpdate` is exposed as the raw `yy mm dd` triple, not a `DateOnly`.** The FOX build writes
   `year % 100` (`DBF-FORMAT.md` §2, u4util.c:1010-1027), so the century is not recoverable from
   the file. Inventing one would be a lie in the public API; the dump records the raw triple too.
5. **`CodeBaseEngine` is introduced now, minimal**, as the composition root and ownership parent
   (`PORTING-PLAN.md` §3.3 rule 4). Its only setting in this step is `DefaultEncoding`
   (Decision 13). *Rejected:* a static `Table.Open(path)` — engine-level configuration exists from
   the first step, so a static entry point would already be wrong here, and it leaves no place to
   construct the concrete boundaries once.
6. **Record-count validation is `numRecs <= (fileLen - headerLen) / recordLen`**, throwing `e4data`
   above it (the header claims records the file cannot hold). *Rejected:* the C library's exact
   equality check — that applies only to `OPEN4DENY_WRITE`/`DENY_RW` opens (`DBF-FORMAT.md` §2.6),
   and open modes arrive with `LOCKING`. Floor division already tolerates a stray trailing `0x1A`
   (`DBF-FORMAT.md` §3.1). Revisit when open modes exist.
7. **Structural validation done at open:** `recordLen != 0`; descriptor region terminated by `0x0D`;
   `1 + Σ field lengths == recordLen`; per-type length rules (`M`/`G` ∈ {4,10}, `I` = 4, `T`/`Y`/`B`
   = 8, …); VFP-only types (`B` as double, `H`, `Y`, `T`, `7`, `0`) rejected when
   `version < 0x30` ⇒ `e4data`; unknown type character ⇒ `e4fieldType` (`DBF-FORMAT.md` §5).
8. **The recognized type set is v1 scope, not the C library's full set.** `C N F D L M G I B Y T H`
   plus the `X`/`Z` stored aliases and `'0'` `_NullFlags` are accepted; the CodeBase/OLE-DB
   extensions `W O V P Q R 1 2 3 4 5 6` are rejected with `e4fieldType`. `PORTING-PLAN.md` §2.2 puts
   them out of v1, and accepting them would mean shipping the C library's `0x32`-only length
   relaxations (`D4OPEN.C:2479-2492, 2536-2549`) with no corpus case behind them.
9. **`0x31` header flags are validated as the C does** — a byte-wise comparison, so `flags[0..4]`
   must each be exactly `0` or `1` *and* `flags[5..7]` must be `0`; anything else ⇒ `e4data`
   (`D4OPEN.C:2152-2187`, read during this design). The spec's summary ("flags[5..7] must be 0")
   is a subset of the real check.
10. **The long-field-name layout is detected and rejected** with `e4notSupported` rather than decoded.
    No corpus case exists, so decoding it would mean asserting our reading of a spec against itself —
    which `DEV_APPROACH.md` §4 forbids. It becomes a step of its own once a generator case exists.
11. **The corpus dump is parsed into a model, not diffed as text.** A `CorpusDump` reader in
    `CodeBase.Net.Golden` parses `<NAME>.dump.txt` section by section and exposes typed expectations.
    *Rejected:* re-emitting the dump text from C# and comparing strings — it would couple the port's
    public API to a C++ tool's formatting, and it forces every section to be implemented at once,
    which is exactly the step split we chose. The parser is written once here and extended in 002.
    **The reader is strict, not tolerant.** Every section is either read or named as deliberately
    unread; one it has never heard of is refused, as is a dump missing a section or a header value
    it needs, or one whose sections are present but empty. *Rejected:* skipping unknown sections for
    forward compatibility. The DBF format is frozen so there is nothing to be forward-compatible
    with, while the **dump** format is ours and will grow an index half (ADR-13) — and a section that
    arrives unnoticed leaves a golden test comparing an empty list and passing. A gate that cannot
    tell "nothing to compare" from "compared and agreed" is the one failure a golden suite must not
    have.
12. **The FPT file is opened and its 8-byte header decoded** at table open, because the C library
    does and because `blockSize` is metadata of the open table. No dump section covers it, so it is
    gated at layers 1–2 against bytes sliced from `net/corpus/VFPMEMO.fpt` offsets 0–7 (an allowed
    expectation, `DEV_APPROACH.md` §4) — not at layer 4.
13. **The code page is identified at open; the `Encoding` object is materialized on first use.**
    The header's language-driver byte becomes the typed `CodePage` and the raw `CodePageByte` at
    open — those are facts about the file and are decided once. The map is `DBF-FORMAT.md` §8
    verbatim: `0x00` unmarked, `0x01` cp437, `0x02` cp850, `0x03` cp1252, `0x04` the back-compat
    placeholder, `0xC8` cp1250. `Table.TextEncoding` is lazy, because of the registration rule below.
    - **An unrecognized byte does not fail the open.** `CodePage` reports `Unknown` and
      `CodePageByte` keeps the value. The C library never validates this byte, and refusing to open
      an otherwise-readable table over a *display* concern would be a worse divergence than a wrong
      display encoding. Nothing about index keys depends on it (`PORTING-PLAN.md` §3.4).
    - **The fallback is configurable, on the engine.** `CodeBaseEngine.DefaultEncoding` supplies the
      encoding for tables whose byte is unmarked (`0x00`) or unrecognized — the two cases where the
      file itself does not say. It defaults to `null`, meaning **cp437**, which is how the C library
      treats unmarked files, so out-of-the-box behaviour matches the original. A *recognized* marker
      always wins over the engine setting: the file is authoritative about itself.
    - **The library never calls `Encoding.RegisterProvider`.** Registering an encoding provider is a
      process-wide side effect, and a library that performs it silently changes behaviour for code
      that never asked. The requirement is **documented** instead — `CodePagesEncodingProvider` must
      be registered by the host application if it reads text from a table whose code page is
      cp437/cp850/cp1252/cp1250, which on .NET 8 includes the cp437 default. Documented in
      `README.md`, in `FOR-DEVELOPERS.md`, and in the XML docs on `TextEncoding` and
      `DefaultEncoding`. Setting `DefaultEncoding` to an `Encoding` instance the host already holds
      sidesteps the provider entirely for unmarked files.
    - **Hence lazy.** If the lookup were done at open, a host that only wants metadata from a
      cp1252-marked table would need the provider it never uses. Deferring it means the failure
      arrives at the point of use, from the call that actually needs an encoding, and carries a
      message naming `CodePagesEncodingProvider.Instance` and the one line that fixes it.
    - **Field names are still decoded as ASCII**, independent of all this: they are upper-cased,
      NUL-padded and code-page-independent in every in-scope case.
    - **Ungated until a corpus case exists.** Every corpus table has `codePage = 0x00`, so only the
      unmarked branch is gated at layer 4 — see Q1.
    - **The library therefore has no NuGet dependencies at all** — see Decision 16.
14. **`Table.Fields` excludes the `_NullFlags` system field**, matching `d4numFields`, which
    subtracts it when the last field's type is `'0'` (`d4declar.h:594`, read during this design).
    The new `VFPNULL` corpus case makes this a hard gate rather than a preference: its dump lists
    **14 `[descriptors]` and 13 `[fields]`**. The bitmap is exposed as `Table.NullFlags` (length and
    presence only in this step; per-field `IsNull` arrives with records in 002). This settles Q3.
15. **`FieldDefinition.Name` is upper-cased, as the C library does** (`c4upper` on every field name
    at open, `D4OPEN.C:316`) — while the `_NullFlags` *detection* compares the stored descriptor
    bytes case-sensitively (`D4OPEN.C:2611`). `VFPNULL` proves both at once: the descriptor stores
    `_NullFlags` and the field list reports `_NULLFLAGS`.
16. **`System.Text.Encoding.CodePages` moves off the library and onto the test projects.** It was
    listed as a library dependency (`CLAUDE.md` §Technology stack, `PORTING-PLAN.md` §3.1) back when
    the library was expected to register the provider. Decision 13 removed that, and
    `Encoding.GetEncoding(int)` consults whatever the *host* registered — so the library never
    references the package, and **`CodeBase.Net` ships with no NuGet dependencies at all**. The test
    projects reference it, because they must register the provider to exercise decoding. Both
    documents are updated to say so; `README.md` and `FOR-DEVELOPERS.md` carry the host-side
    requirement. *Rejected:* keeping the reference "just in case" — an unused dependency on a
    library that deliberately does not use it is a standing invitation to call
    `RegisterProvider` from inside and undo Decision 13.
17. **`Table.Descriptors` is public**, not internal. Two reasons. The gate reproduces the dump's
    `[descriptors]` section, and a layer-4 test that reached it through `InternalsVisibleTo` would
    be gating something other than the API a caller has. And the stored descriptor is a genuine fact
    of a byte-fidelity library — the `_NullFlags` entry has no other reachable home, since it is not
    in `Fields`. The digested facts a normal caller wants (`Type` vs `StoredType`, `IsBinary`,
    `IsNullable`, `NullBit`) are on `FieldDefinition`, so `Descriptors` is for tooling and
    diagnostics, not the common path. *Rejected:* internal + `InternalsVisibleTo` — it moves the
    gate off the public surface to save one property.
18. **Containment is structural. Validation is only a diagnostic.** A malformed descriptor must not
    be able to make any later code read outside the record it belongs to, and that guarantee must
    **not depend on a validation check existing** — because Decision 19 removes most of them for
    compatibility, and the C library's own are thinner than they look. Three parts:
    - **One invariant, enforced at open.** `1 + Σ FieldLength == RecordLength`
      (`D4OPEN.C:2671-2674` — unconditional, not a debug-only check). With offsets accumulated from
      1, this alone makes every field's span a subrange of the record, by construction.
    - **A published guarantee.** `Table` never returns a `FieldDefinition` that violates
      `1 <= RecordOffset && RecordOffset + Length <= RecordLength`. Either open throws, or the
      invariant holds for every field — never neither. That is a property test, not an example test.
    - **One accessor, and the record is the boundary.** Every field access goes through a single
      bounds-checked helper that starts at the field's stored offset and **cannot leave the record
      buffer**. Containment is a property of the record, not of the individual field span — which is
      what lets us match the C library exactly (below) while still being unable to read out of
      bounds.

    **The C library's accessors are not uniform, and we copy each one.** Text-shaped types honour the
    descriptor's length; fixed-width binary types ignore it and dereference a typed pointer:

    | Types | What the C accessor reads | So we |
    |---|---|---|
    | `C` `Z` `N` `F` `D` | the declared length (`f4ptr` + `f4len`) | the same — a `D(7)` decodes 7 bytes, fails to parse, and is blank |
    | `L` | one byte | the same |
    | `M` `G` | branches **on** the declared length: 4 → binary id, else ASCII (`FPT-MEMO.md` §3.4) | the same |
    | `I` `P` | 4 bytes, ignoring the length — but the length **is** validated at open, so it is always 4 | the same |
    | `B` `H` `Y` `T` `7` | the type's natural width (8/4/8/8/8), **ignoring the declared length** (`f4dateTime`, `F4FIELD.C:1966-1998`) — and these are exactly the types with no open-time length check | the same |

    So a `T` declared 4 bytes reads 8, taking four bytes from the next field, precisely as CodeBase
    does. A short descriptor length is not evidence the *data* is short, and inventing a narrower
    read would return a different value from the reference implementation for no gain.

    **The one case with no C behaviour to copy** is a short fixed-width field at the *end* of the
    record: the natural-width read would leave the record buffer, where CodeBase reads its own
    allocation slack — undefined, not a behaviour. We clamp at the record end and treat the missing
    bytes as zero, which is what that slack holds in practice. Documented, and the only place the two
    implementations can differ.

    Under `WRITE` the same invariant is what a record write will trust, so it is load-bearing well
    beyond reading.
19. **We validate exactly the lengths a *release* build of the C library validates — no more.**
    Reading `D4OPEN.C:2448-2649` field-type switch case by case, the length checks that actually
    fire in `S4FOX` release are: `I`/`P` = 4, `R` = 2, `Q` = 2 (relaxed at `0x32`), `V` = 16
    (relaxed at `0x32`), `5` = 8, `1`/`6` = 8. **That is all.** `D`, `L`, `N`, `F`, `C`, `B`, `H`,
    `Y`, `T`, `7` and `'0'` have **no** open-time length check, and the familiar `M`/`G` ∈ {4,10}
    rule is `#ifdef E4MISC` — debug builds only (`D4OPEN.C:2453-2463`).
    Rejecting more than this would make us *less* compatible while looking more careful: files that
    VFP and CodeBase open, we would refuse. Decision 18 is what makes accepting them safe, so the
    two decisions only work as a pair. `DBF-FORMAT.md` §5's "open-time length validation" paragraph
    reads as though these checks are unconditional and needs the `E4MISC` qualification — added to
    the spec follow-ups below.

## Open questions

| # | Question | Answered by |
|---|---|---|
| Q1 | *Resolved for nullables* — `VFPNULL.DBF` was generated and checked in (2026-08-09), so the `_NullFlags` descriptor, null-bit ordinals and the two-byte bitmap are gated at layer 4. **Still ungated:** version `0x31` + `flags[]`, a non-zero `codePage`, and the `H` type. The code-page map (Decision 13) has no marked-table case behind it — which matters **before step 002**, not for this step's gate. | A `CORPUS` decision (`PORTING-PLAN.md` §6.3, ADR-10). The generator pipeline is warm, so a marked table is cheap to add now |
| Q2 | *Answered during this design* — see Decision 2. `D4OPEN.C:2351-2361` branches on `d4->version == 0x30` after `0x31` normalization, so `0x32` takes the `version & 0x80` path and its memo file is never opened. Reproduced deliberately. | — |
| Q3 | *Answered by the corpus* — see Decision 14. `d4numFields` hides `_NullFlags` (`d4declar.h:594`) and `VFPNULL.dump.txt` shows 14 descriptors against 13 fields, so `Table.Fields` excludes it. | — |
| Q4 | *Decided* — see Decision 16. `System.Text.Encoding.CodePages` is a test-project reference, not a library one; `CLAUDE.md` and `PORTING-PLAN.md` §3.1 updated. | — |

**None open.** Q1's remaining corpus gaps (`0x31` + flags, a marked code page, the `H` type) are
tracked where they belong — `PORTING-PLAN.md` §6.3 and ADR-10 — and none of them gates this step.

**Spec follow-ups this design produced** (`CLAUDE.md` housekeeping — a format fact belongs in the
spec, not here). Small corrections to fold into `claude/specs/` before or during execution:

- `DBF-FORMAT.md` §2.2 says "flags[5..7] must be 0"; the actual check also rejects `flags[0..4]`
  values other than 0/1 (Decision 9).
- `FPT-MEMO.md` §3.5 states the memo rule without noting its `0x32` consequence (Decision 2).
- `DBF-FORMAT.md` §2.1 and §5 state the Visual-FoxPro-types gate as `version >= 0x30` without saying
  the comparison is **signed**. The C library holds the version in a plain `char` (`d4data.h:3220`),
  signed on the compilers it is built with, so every byte from `0x80` up compares as negative and
  fails the test — including `0xF5`, an everyday FoxPro 2 table with a memo. Read as unsigned, the
  rule silently admits `T`, `Y` and `B` fields to tables the reference implementation refuses them
  on. Found by a failing test in sub-step 4.
- `DBF-FORMAT.md` §5's open-time length validation paragraph reads as unconditional. Most of it is
  not: `M`/`G` ∈ {4,10} is `#ifdef E4MISC`, and `D`, `L`, `N`, `F`, `C`, `B`, `H`, `Y`, `T`, `7` are
  never length-checked at open at all (Decision 19). This one matters — it is the difference between
  a reader that opens real files and one that refuses them.
- `DBF-FORMAT.md` §4 does not mention that field names are upper-cased on open (`D4OPEN.C:316`)
  while `_NullFlags` is matched against the stored bytes case-sensitively (Decision 15).
- `DBF-FORMAT.md` §4.1 / `FPT-MEMO.md` §3.4: a nullable **memo** cannot hold both content and a null
  bit — the deferred flush writes the block id through `f4assignLong`, which clears the bit
  (`f4memo.c:801-807`). Discovered while building `VFPNULL`; record 7 of that case is the witness.
