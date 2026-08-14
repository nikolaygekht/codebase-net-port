# CodeBase → C#/.NET Master Porting Plan

**Status:** Authoritative master plan for scope, architecture, and priorities.
(Supersedes an earlier draft analysis, since removed.)
**Where this sits among the docs** (full map in [§9](#9-docs-map)): this plan governs **what** we
build and **at what priority**; `claude/specs/` govern **what the bytes are**;
`claude/DEV_APPROACH.md` governs **how a step is run**; `claude/ARCHITECTURE-DECISIONS.md` records
**why** each choice was made; `CLAUDE.md` carries the standing rules, the technology stack, and the
full map of which document owns what. Where this plan and a spec disagree on an on-disk fact, the spec wins.
**License:** GNU GPL v3 (see `LICENSE`). The original Sequiter release is LGPL v3; this port is a
GPL-v3 derivative as permitted by the LGPL.
**Target runtime:** .NET 8+ (LTS), C# 12, Windows-first with a Unix fallback for locking.

---

## 1. Intent

Port the stand-alone CodeBase 6.x C engine (Sequiter, LGPL) to a modern, idiomatic C# library
that reads and writes **Visual FoxPro-compatible** DBF tables, CDX compound indexes, and FPT memo
files **byte-for-byte**. "Byte-for-byte" is the whole point: files written by the C# port must be
openable and correctly navigable by Visual FoxPro and by the original C library, and vice-versa,
including index key ordering and leaf compression. The library is a rewrite in the spirit of the
original — not a P/Invoke wrapper and not a transliteration of the C. We keep the *file formats and
algorithms* exact and modernize *everything else* (memory model, error handling, I/O, API shape).

Byte-compatibility is the *foundation*, but it is not the point. **The library's main value is the
bitmap query optimizer** — Rushmore-style filter decomposition that answers a query by seeking
index tags and combining record-number sets, instead of scanning the table. Everything in the read
path exists to make that possible — which is why the optimizer is P1 and write support is P2 (§5).

Non-goals of the rewrite: preserving the C source's memory pools, its global-state expression
evaluator, its `errorCode`-return calling convention, or its compile-time `S4*` switch matrix.

---

## 2. Scope

Scope is cut deliberately. The original library is ~117k lines across four index formats, a client/
server stack, a report writer, OLE-DB glue, and half a dozen platforms. We port one coherent slice.

### 2.1 IN scope (v1 target)

- **Stand-alone engine only** — the `S4FOX` + `S4STAND_ALONE` build. No `S4CLIENT`/`S4SERVER`.
- **DBF data files** in Visual FoxPro format: version bytes **0x30 / 0x31** (0x31 = CodeBase-extended
  VFP), read-tolerance for **0x32** (VFP9). Governed by `DBF-FORMAT.md`.
- **VFP field types**: `C, N, F, D, L, M, G, I, B(double), Y(currency), T(datetime), H(binary
  float)` plus the `_NullFlags` nullable-field system (`'0'`) and auto-increment/auto-timestamp
  CodeBase extensions. Governed by `DBF-FORMAT.md` §5–§7.
- **CDX compound indexes** (`S4FOX`): the tag-directory B-tree, interior nodes (big-endian
  recno/child), and **bit-packed compressed leaf nodes**. Governed by `CDX-FORMAT.md`.
- **Compact single-tag `.IDX` files**, *read* — the same S4FOX format entered differently: the tag
  header sits at file offset 0, `typeCode < 0x40` means there is no tag directory, and the tag is
  named after the file (`i4index.c:1694, 1814-1825`). Support costs one strategy resolved at
  open. Non-compact FoxPro 2.x `.IDX` is **not** included — its flags byte carries neither 0x20 nor
  0x40, so `typeCode < 32` and the C library refuses it too (`i4index.c:1706`). Writing `.IDX` is out:
  `i4create` cannot produce one (ADR-25).
- **VFP collation** for index keys, ported as **verbatim byte tables** (machine + GENERAL for
  cp1252/cp437/cp850). Governed by `KEY-COLLATION.md`.
- **FPT memo files** (VFP 4-byte block references, big-endian header/block headers, no free chain,
  monotonic growth + explicit compaction). Governed by `FPT-MEMO.md`.
- **The xBase expression engine** needed for tag key generation and filters. Governed by
  `EXPRESSIONS.md`. (Split between `EXPR` and `WRITE` — see §5.)
- **Single-user and multi-user record/file/append/index/memo locking**, and the transaction/log
  subsystem, at the exact VFP-compatible byte offsets. Governed by `LOCKING-TRANSACTIONS.md`.
- **The bitmap query optimizer** (Rushmore-style): `BITMAP4` + `CONST4` + `L4LOGICAL`
  (`R4RELATE.H:268-307`, implemented in `C4CONST.C` and `m4map.c`), plus the minimal `RELATE4`
  scaffolding needed to reach it for a single table (`relate4init` / `relate4querySet` and
  navigation over the result set). **This is the library's headline feature** — see `QUERY` in §5.
- **Idiomatic public API** with a typed exception hierarchy that preserves the original error code.
  Governed by `API-ERRORS.md`.

### 2.2 OUT of scope

- **Client/server** (`S4CLIENT`/`S4SERVER`) — networking, pipes, connection auth.
- **MDX** (dBase IV compound index) and **NTX** (Clipper single-tag index). *Note the corrected
  naming:* MDX is dBase IV's format; NTX is Clipper's. We do neither. S4FOX only — which since
  §2.1 means CDX plus the compact single-tag `.IDX`, and **not** non-compact `.IDX`.
- **OLE-DB `r5*` field types** (`O, P, Q, R, V(GUID), W, 1, 2, 3, 4, 5, 6`) beyond *read-tolerant
  recognition where a real VFP9 file demands it* — creation of these is not supported in v1.
- **Report writer / styles / totals** (`report4*`, `style4*`, `area4*`, `group4*`, `obj4*`).
- **Compressed DBF data and compressed memos** (type-3 FPT entries) — CodeBase-proprietary, VFP-
  incompatible. Read as an error/opt-in; never written by default.
- **Encryption** (Rijndael DLL), **Palm OS / WinCE / big-endian** targets, the pooled allocator,
  and the intrusive `LIST4`/`mem4` machinery (GC + `List<T>` replace them).
- **`largeFileOffset` proprietary lock scheme** (>4 GB via 64-bit lock offsets) — breaks VFP
  interop; default is `largeFileOffset == 0`.
- **The demo record cap** (`E4DEMO_MAX = 200`, `e4demo`) — must NOT be ported.
- **Dates before AD 1.** No `D` field can hold one: the conversion takes digits and spaces only, so
  there is no sign character and no year below zero is representable (`DBF-FORMAT.md` §6.3). Year 0
  is reachable, but only as the decode of a blank or partially written year, and it is handled as a
  malformed field whose Julian number is reproduced bit-for-bit rather than as a date the library
  supports. No BC semantics, no corpus case, no gate — ADR-33.

### 2.3 LATER maybe

- **Multi-table relations** (`relate4createSlave` and the join/skip machinery in `r4relate.c`).
  The *optimizer itself* is now IN scope (§2.1, `QUERY`); only joins are deferred.
- Unicode (`r5wstr`/`r5wstrLen`) collated index keys (CodeBase extension; VFP has none).
- The **`'7'` datetime-with-milliseconds** field type (CodeBase extension; VFP never writes one, and
  the C library admits it only on `version >= 0x30`). Refused at open — ADR-32.
- Additional VFP collations beyond GENERAL (DUTCH, NORDAN, croatian/spanish/avaya tables) — the
  croatian/spanish/avaya tables exist in `COLL4ARR.C` and could be ported; VFP's own extra drivers
  would have to be captured from VFP itself.
- Disk-loaded custom collations (`coll4inf`/`collate4`/`compres4` DBFs).
- Full VFP9 varchar/varbinary null-bit semantics.
- `largeFileOffset` mode for C#-to-C# >2 GB files (documented as VFP-incompatible).

---

## 3. Architecture

### 3.1 Solution layout

```
net/                               # everything .NET lives here
├─ CodeBase.Net.sln
├─ Directory.Build.props            # settings shared by every project below
├─ CodeBase.Net/                    # the library (net8.0), no NuGet dependencies (ADR-17)
├─ tests/
│  ├─ CodeBase.Net.Tests/           # xUnit v3: unit, component, fault-injection layers
│  ├─ CodeBase.Net.Golden/          # golden-file + round-trip tests against net/corpus/
│  ├─ CodeBase.Net.TestUtils/       # shared test infrastructure; a library, not a test project
│  └─ CodeBase.Net.Benchmarks/      # BenchmarkDotNet
└─ corpus/                          # checked-in golden DBF/CDX/FPT files + expected dumps
test-files-generator/               # developer tool: drives the C library to build the corpus (§6)
claude/specs/                       # format authority (do not edit casually)
claude/dev/                         # per-step design/plan/state/summary (see DEV_APPROACH.md)
claude/PORTING-PLAN.md              # this file
```

**`test-files-generator/` is not part of the solution and not a test dependency.** It is a
Windows/MSVC developer tool that produces `net/corpus/`; its output is checked in. Building or testing
`CodeBase.Net` never compiles or runs C. See §6.

Single assembly `CodeBase.Net` (ADR-14 fixes the casing everywhere), GPL v3 (the licence of this port; the original Sequiter library is
LGPL v3 — see the front matter), **with no NuGet dependencies**. Record text is decoded through
whatever encoding provider the *host* registered — cp437/cp850/cp1252/cp1250, and only for
record-text decoding, never for index keys (§3.3). `System.Text.Encoding.CodePages` is therefore a
test-project reference and a documented host requirement, not a library dependency (ADR-17).

### 3.2 Namespace / module layout and which spec governs each

| Namespace | Contents | Governing spec |
|---|---|---|
| `CodeBase.Net` | `CodeBaseEngine` (was `CODE4`), `Table` (`DATA4`), `Field`, `Tag`, `IndexFile`, `DbExpression`, `Transaction`; the public surface | `API-ERRORS.md` |
| `CodeBase.Net.Dbf` | DBF header, field descriptors, record layout, per-type in-record encode/decode, `_NullFlags`, auto-increment/timestamp | `DBF-FORMAT.md` |
| `CodeBase.Net.Cdx` | CDX file structure, tag directory, `B4STD_HEADER`, interior nodes, **bit-packed leaf codec**, seek/insert/split/remove, free list, version counter | `CDX-FORMAT.md` |
| `CodeBase.Net.Collation` | Verbatim collation byte tables, key transforms (`t4dblToFox`, `t4intToFox`, …), `flags4dateTime` table, pChar rules | `KEY-COLLATION.md` |
| `CodeBase.Net.Memo` | FPT header/block codec (big-endian), allocation-at-EOF, compaction; DBT kept behind an interface but not implemented in v1 | `FPT-MEMO.md` |
| `CodeBase.Net.Expressions` | Lexer, precedence parser, typed AST, evaluator, key-conversion bridge to `Collation` | `EXPRESSIONS.md` |
| `CodeBase.Net.Query` | **Bitmap query optimizer** — `BITMAP4` boolean tree, `CONST4` range constraints, tag-driven leaf evaluation, AND/OR set combination, single-table `RELATE4` scaffolding and result-set navigation | *(no spec yet — derive from `R4RELATE.H`, `C4CONST.C`, `m4map.c`; see §9)* |
| `CodeBase.Net.Locking` | Byte-range lock manager at VFP offsets, in-memory lock registry, transaction log reader/writer, mini-transactions, recovery | `LOCKING-TRANSACTIONS.md` |
| `CodeBase.Net.IO` (internal) | `DbFileStream` — `FileStream` wrapper with region locking at the exact CodeBase offsets, sequential read/write helpers, flush-to-disk | `LOCKING-TRANSACTIONS.md` §5, `DBF-FORMAT.md` §9 |

### 3.3 Cross-cutting architectural rules

1. **Endianness is per *field*, not per format.** The C code writes native x86 structs, so most of
   every format is little-endian with a handful of big-endian islands. Architecturally that means:
   every multi-byte read/write states its endianness explicitly via
   `BinaryPrimitives.ReadUInt32LittleEndian` / `WriteUInt32BigEndian` — never `Marshal.StructToPtr`,
   never `BitConverter` without an endianness decision. **The enumerated list of big-endian islands
   is in `CLAUDE.md` (gotchas) and, authoritatively, in `CDX-FORMAT.md` §5.1/§10 and `FPT-MEMO.md`;
   it is not repeated here.**
2. **Collation is NOT `CultureInfo`/`CompareInfo`.** See §3.4 — this is a hard architectural
   constraint, not a preference.
3. **`Span<byte>`/`Memory<byte>` for all buffers.** Record buffers, node blocks (512 bytes), and
   packed leaf entries are manipulated as spans. No per-field `byte[]` churn on the hot path.
4. **Ownership hierarchy = `IDisposable` tree.** `CodeBaseEngine` owns `Table`s; a `Table` owns its
   `IndexFile`(s), memo handle, and lock state. Disposing a parent disposes children (matching the
   C `code4initUndo` → `d4close` cascade), but *without* the C memory pools.
5. **No global evaluator state.** The C engine serializes all expression evaluation through one
   process-wide critical section (`E4EXPR.C:87-135`) because its operand stack is global. The port
   makes evaluation state per-call so `DbExpression` instances are independently usable.
6. **Raw bytes are preserved even when we can decode them.** The engine does *no* transcoding of
   record data (`DBF-FORMAT.md` §8). We decode strings to `System.String` via the LDID→`Encoding`
   map for user-facing accessors, but the stored bytes remain authoritative for index-key equality
   and round-trip fidelity.

### 3.4 Collation: verbatim tables, never .NET culture (the old plan's worst error)

A tempting shortcut is to treat "key encoding based on locale" as something a .NET culture could
produce. **This is wrong and will silently corrupt index compatibility.**
CDX keys are *stored byte strings* ordered by unsigned `memcmp`; compatibility means reproducing
the exact bytes VFP/CodeBase writes, not an equivalent ordering. Per `KEY-COLLATION.md` §8:

- `CompareInfo.GetSortKey()` yields NLS/ICU sort keys whose binary layout is OS/ICU-version
  dependent and never matches the head-block+tail-block, zero-padded-to-2×len GENERAL layout.
- The GENERAL tables are frozen 1990s FoxPro tables: case-insensitive primaries, accent weights
  placed *after* the whole primary block, hard-coded expansions (œ→OE, ß→SS, þ→TH).
- Numeric/date/datetime keys embed the empirical `flags4dateTimeFlags` ULP-correction bitset (10 802
  bytes) that no general-purpose library reproduces.

**Mandate:** port `COLL4ARR.C`'s 8 translation arrays + 2 compress arrays and the
`flags4dateTimeFlags` table as `static readonly byte[]` verbatim. Implement the transforms of
`KEY-COLLATION.md` §2–§3 exactly. `CultureInfo` may be used only for user-facing, non-index
features (e.g. display upper-casing outside key generation).

---

## 4. API design principles

Decisive settlement of the contradiction in the old docs (which said both "error codes, no
exceptions" *and* "throw with `ErrorCode`"): **the C# port is idiomatic C#.** It throws a typed
exception hierarchy for *errors*, returns typed enums for *status/flow results*, uses
`IDisposable`, properties, `Stream`-based I/O, and `Span<byte>`.

### 4.1 Errors are exceptions; the original code is preserved as a property

`e4*` values (negative, `API-ERRORS.md` §3) are **errors** → exceptions. `r4*` values that denote
*flow/status* (`r4found`, `r4after`, `r4eof`, `r4bof`, `r4locked`, `r4unique`, `r4entry`,
`r4noRecords`, `r4terminate`, transaction statuses) are **return values**, not exceptions.

```csharp
public enum ErrorCode          // exact e4* integer values from API-ERRORS.md §3
{
    Data = -200, FieldType = -220, Index = -310, Unique = -340,
    MemoCorrupt = -1110, TransViolation = -1200, /* … */ CodeBase = -1,
}

public sealed class CodeBaseException : Exception
{
    public ErrorCode Code { get; }          // the e4* value (preserved verbatim)
    public long ExtendedCode { get; }        // errorCode2 (internal E-number)
    public CodeBaseException(ErrorCode code, long extended, string message)
        : base(message) { Code = code; ExtendedCode = extended; }
}

// Subtypes for catch-granularity; all carry Code/ExtendedCode:
public sealed class IndexCorruptException  : CodeBaseException { /* e4index  */ }
public sealed class MemoCorruptException   : CodeBaseException { /* e4memoCorrupt */ }
public sealed class TransactionException   : CodeBaseException { /* e4trans* */ }
```

The C library's *sticky error* semantics (`errorCode < 0` short-circuits later calls) are **not**
the default. An optional `Engine.LastError` mirror is provided only for compatibility diagnostics.

### 4.2 Seek/skip return status, they do not throw

```csharp
public enum SeekResult { Found = 1, After = 2, Eof = 3, Bof = 4, NoRecords = 6 }

SeekResult r = table.Seek("SMITH");        // never throws for "not found"
if (r == SeekResult.Found) { /* … */ }
```

### 4.3 Ownership, properties, Stream I/O, Span buffers

```csharp
using var engine = new CodeBaseEngine { Compatibility = 30, CodePage = CodePage.Cp1252 };
using Table t = engine.OpenTable("customer.dbf");   // auto-opens production .cdx

t.Top();
while (!t.Eof)
{
    ReadOnlySpan<byte> raw = t.RecordBuffer;         // Span over the live buffer
    string name = t.Fields["NAME"].GetString();      // decoded via code page
    decimal bal = t.Fields["BALANCE"].GetDecimal();
    t.Skip(1);
}

t.LockAppend();
t.Blank();
t.Fields["NAME"].SetString("SMITH");
t.Append();                                          // writes keys, unique-checks
```

- `CODE4` config members become `CodeBaseEngine` properties with the C4CODE.C defaults from
  `API-ERRORS.md` §6 (e.g. `LockAttempts = WaitForever(-1)`, `LockDelay = 100` hundredths,
  `UnlockAuto = All`, `Safety = true`, `Optimize = Exclusive`). **Exception:** the C default
  `compatibility == 0`; for a VFP-first port `Compatibility` defaults to **30**, documented as an
  intentional deviation from the C library.
- Field access is typed: `GetString/SetString`, `GetDouble`, `GetInt32`, `GetDecimal` (currency),
  `GetDateTime`, `GetBytes`/`SetBytes` (`Span`), plus `IsNull`/`SetNull`.
- The internal `DbFileStream` wraps `FileStream` and exposes byte-range `Lock/Unlock` at the exact
  CodeBase offsets so a C# process interoperates with a running VFP or C-library process.

### 4.4 Naming

Drop the `c4/d4/f4/i4/t4` prefixes for public types (`CodeBaseEngine`, `Table`, `Field`, …). Keep
the *format-level* names (`B4STD_HEADER`, `T4HEADER`, `flags4dateTime`, `t4dblToFox`) as internal
type/method names so the code cross-references the specs and the C source unambiguously.

---

## 5. What we port, and at what priority

The inventory of v1 capabilities: **what** gets ported and **how much it matters**. This is not a
schedule and not a sequence. Order emerges from what each step actually needs — steps are governed
by [`DEV_APPROACH.md`](DEV_APPROACH.md), and the natural order will reveal itself as we go. What
*is* fixed here is the definition of done: **a capability is complete when its gate passes.**

Priority means **value to the library's purpose** (§1), not urgency:

| Tier | Meaning |
|---|---|
| **P0** | Foundation — nothing else can be *verified* without it |
| **P1** | The headline path: read a table and answer queries through the optimizer |
| **P2** | Write support |
| **P3** | Concurrency and durability |
| **P4** | Hardening and polish |

**Keep the status column current.** When a step closes, its `SUMMARY.md` updates the status of every
capability it advanced (`DEV_APPROACH.md` §6). This table is the project's answer to *what is done*;
`STATE.md` carries the narrative of where we are and what is next.

| ID | Capability | Priority | Status | Chief risk |
|---|---|---|---|---|
| `CORPUS` | Corpus + generator | **P0** | in progress — 11 cases in, 4 of them indexed; mutation and write cases missing | R11 |
| `DBF-READ` | DBF reading | **P1** | **done for reading** — metadata (001), records (002), memo and binary types (003). Writing is `WRITE` | — |
| `CDX-READ` | CDX reading & navigation | **P1** | **done for reading** — decode and traversal (004), seek (005), tags on a `Table` (006). Only a non-field key expression waits on `EXPR` | **R1** retired |
| `COLLATION` | Collation tables & key transforms | **P1** | **done** — `COLL4ARR` tables for cp1252/cp437/cp850 copied verbatim, every numeric transform, `GENERAL` head-and-tail keys, the `flags4dateTime` bitmap, and the partial-seek rules (007 sub-steps 1-5). Gated by **3559 keys** rebuilt from the values that produced them | **R2 retired**, R7 |
| `EXPR` | Expression engine (read subset) | **P1** | not started | R5 |
| `QUERY` | **Bitmap query optimizer** | **P1** | not started — spec unwritten | **R12**, R13 |
| `WRITE` | Single-user write & round-trip | **P2** | not started | R4, R9 |
| `LOCKING` | Locking & multi-user | **P3** | not started | R3, R10 |
| `TRANS` | Transactions & recovery | **P3** | not started | — |
| `HARDENING` | Hardening, benchmarks, API polish | **P4** | not started | — |

Two things the table does not say, deliberately. It does not say what to build next — that is a step
decision, made from what the current step needs and from `STATE.md`. And **"needs X" below is a
technical fact, not a queue**: a capability that needs another cannot be *finished* before it, but
partial work, spikes and design can and often should proceed in parallel.

**Risk is a priority input, not an ordering rule.** `CDX-READ` (R1, bit-packed leaves) and
`COLLATION` (R2, verbatim tables) carry the two highest silent-corruption risks in the port. Every
week they stay unproven is a week other work might be built on a wrong assumption, so there is real
value in attacking them early — but that is a judgement to make per step, not a sequence fixed here.

---

### `CORPUS` — corpus and generator · P0

- **Deliverable:** `test-files-generator/` — a Windows/MSVC tool that links the original C library
  and (a) *generates* golden DBF/CDX/FPT files covering the cases in §6.3 and (b) *dumps* each one
  (field descriptors, every record's raw + decoded fields, every tag walked in key order emitting
  `(rawKeyBytes, recno)`, memo entries, `d4check` result). Files **and** dumps are checked into
  `net/corpus/`. A `validate` mode that opens an arbitrary file and runs the same checks is a useful
  developer aid for `WRITE` but is not wired into any test.
- **Status (index half):** four indexed cases are in — `CDXBASE` (ten tags, one block each),
  `CDXDEEP` (three levels deep), `CDXCOLL` (machine beside GENERAL over one field) and `IDXONE` (one
  tree in both file shapes). The index dump format is settled (ADR-24) and everything in it comes from
  the C library's own structures rather than a re-read of the bytes. What remains is the write-side
  before/after pairs, the `value → key-bytes` table `COLLATION` needs, and corruption cases for
  `HARDENING`.
- **Status:** the build harness is **done and working** — `build-lib.bat` / `build-gen.bat` /
  `config.bat` / `copy-corpus.bat`, 137 translation units, x86, compiled as C++, zero edits to
  `original/source`. Seven no-index DBF cases are generated, dumped and checked in
  (`net/corpus/README.md`): four covering field types and memo shapes, one covering nullable fields
  and the `_NullFlags` bitmap, and two carrying a marked code page — single-byte and multi-byte
  (ADR-18). The dump format's DBF/FPT half is settled (extended by optional tokens only, ADR-16). What remains
  is corpus breadth (§6.3) — above all **indexed cases with multi-level trees** — and the index half
  of the dump format.
- **Gate:** the generator builds from a clean checkout on a machine with only MSVC; regenerating
  produces **byte-identical** files (the DBF header date stamp is frozen by the generator, so there
  is no run-to-run divergence to mask — §8); every generated file is `d4check`-clean and round-trips
  through the generator's own dump.
- **Needs:** nothing. Produces the corpus every other gate reads — which is what makes it P0.

Because the specs note their adversarial re-verification did not complete (see §9), a standing
`CORPUS` task is a **spot-check pass**: confirm a handful of spec claims per format against
generated bytes before building on them — FPT `numChars` = payload-only, CDX interior-node
big-endian recno (needs a case with a multi-level tree; the shipped samples have none), the 263-byte
VFP reserved area, the `t4dblToFox` sign rule.

### `DBF-READ` — DBF reading · P1

- **Deliverable:** `CodeBase.Net.Dbf` open + header parse + field descriptors + record navigation
  (`Top/Bottom/Skip/Go/Eof/Bof/RecNo/RecCount`) + per-type field decode for all IN-scope types +
  `_NullFlags` + deleted-flag semantics. Read-only.
- **Done so far:** open, header, stored descriptors and the resolved field table, with the memo
  companion opened and its header read — `claude/dev/001-dbf-open-and-header/SUMMARY.md`; then record
  navigation and every ordinary field value —
  `claude/dev/002-dbf-records-and-fields/SUMMARY.md`; then memo payloads in both reference
  encodings and the binary types —
  `claude/dev/003-memo-and-binary-types/SUMMARY.md`. **The read half is complete:** the gate asserts
  every field of every record of all seven corpus dumps with nothing skipped. The one refusal is a
  compressed memo entry, which no corpus case can gate yet (ADR-23, open).
- **Gate:** for every DBF in `net/corpus/`, C# decodes every field of every record identically to the
  checked-in dump (byte/value-exact including blank/`00000000` dates, currency ×10⁴, datetime
  julian+ms, 4-byte memo refs read as ints).
- **Needs:** `CORPUS` cases to be gated against.

### `CDX-READ` — CDX reading & navigation · P1

- **Deliverable:** `CodeBase.Net.Cdx` — parse tag directory, tag headers, interior nodes
  (big-endian recno/child), and **decode bit-packed leaf nodes** (`B4NODE_HEADER`, recNumLen/
  dupCntLen/trailCntLen widths, key reconstruction with dup/trail + pChar). Full-tag seek
  (`tfile4seek`), skip, top/bottom, descending traversal. Both file shapes: compound `.CDX` and
  compact single-tag `.IDX` (§2.1). **No expression engine required** — keys are read from disk, not
  recomputed, and a collated tag reads without any collation table because its pad character is fixed
  at `'\0'` (ADR-27). **Key comparison is unsigned bytewise** (`t4cdxCmp` casts to `unsigned char *`,
  `i4init.c:279-299`; branches use `memcmp` via `u4memcmp`, `d4declar.h:162-164`) — and it works only
  because `COLLATION`'s transforms make the stored bytes order-preserving. Bytes are not the whole
  order: the total order is **(key bytes, record number)**, a seek compares the search value minus its
  trailing pad rather than the full `keyLen`, and seeking a *collated* tag needs the tables to
  translate that value, so `COLLATION` bounds the seek half and not the decode half.
- **Done so far (seek):** step [`005-cdx-seek`](dev/005-cdx-seek/) added the seek family —
  `Seek`, `SeekAtOrBefore`, `SeekLast`, `SeekNext`, `SeekPrevious` and exact key-and-record positioning —
  searching by **key bytes**, so no collation table is needed. Gated on 206 recorded search cases and 104
  seek-next runs, plus properties for the three operations the C library does not have. It also settled
  what a search value *means*: with trailing pad it stands for a whole key, without it is a prefix
  (`CDX-FORMAT.md` §7, witnessed).
- **Done so far:** step [`004-cdx-tags-and-traversal`](dev/004-cdx-tags-and-traversal/) built the
  corpus's four index cases and then the reader: the tag directory, tag headers, interior nodes,
  bit-packed leaves, and every tag walked in key order in both directions. **3364 keys, 155 blocks and
  3425 block entries are asserted against the C library's own view of them**, and the bit-packing is
  checked as *encoding* (each entry's stored duplicate and trail counts) and not only through the keys
  it rebuilds. Internal only — nothing connects a tag to a `Table` yet. Seek is step 005.
- **Gate:** for every tag in every `net/corpus/` CDX, walking the C# B-tree in key order yields exactly
  the `(key-bytes, recno)` sequence recorded in the checked-in dump; partial seeks land on the same
  entry; leaf blocks decode bit-identically. The corpus **must** include multi-level trees — the
  shipped `original/examples/DATA/` samples are all single-leaf, so interior nodes are unreachable
  without generated cases.
- **Needs:** `DBF-READ` (a tag entry is only meaningful as a record pointer) and `CORPUS` cases with
  interior nodes, both of which step 004 supplied. **`EXPR` is needed only for the last corner:** the
  pad character a key's trail count stands for depends on the key expression's *type*, which the file
  does not store (ADR-26) — but once a tag has a table, a bare field-name expression types the key from
  the field descriptors exactly (ADR-28), which covers every corpus tag and the great majority of real
  ones. Only a composite expression such as `UPPER(NAME)` waits for `EXPR`.
- **Done so far (a table's tags):** step [`006-tags-on-a-table`](dev/006-tags-on-a-table/) opened the
  production index with the table it belongs to and made a tag an order the cursor can follow:
  `Table.Tags`, `SelectTag`, and the four `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/
  `GoPreviousIndexed` calls that name a tag per call. The pad byte is now **derived** from the field
  descriptors (ADR-28) rather than supplied, and the two surfaces are gated against each other on all
  3364 corpus keys — **with every field of every record visited checked against the table's own record
  dump**, so the index has to deliver records and not merely record numbers. The failure shapes at the
  ends follow `d4skip` (ADR-29, `CDX-FORMAT.md` §7.1).
- **Still waiting on `EXPR`:** a tag whose key expression is not a bare field name (refused when the tag
  is first used, ADR-28), and stepping in a tag's order *from a record the tag does not list* — a
  filtered or unique tag is fully walkable, but mixing `Go(n)` to an unlisted record with a tag-order
  step is refused rather than answered (ADR-30).
- **Why it matters beyond its tier:** this gate retires the single biggest technical risk in the
  port (R1).

### `COLLATION` — collation tables & key transforms · P1

- **Deliverable:** `CodeBase.Net.Collation` — verbatim `COLL4ARR.C` tables + `flags4dateTime`;
  transforms `t4dblToFox`, `t4floatToFox`, `t4intToFox`, `t4unsignedIntToFox`, `t4i8ToFox`,
  `t4curToFox`, `t4dateTimeToFox`, GENERAL subSortCompress + simple char transforms, pChar rules,
  null-byte prefix, descending complement.
- **Gate:** given the *raw stored key bytes* recorded in `net/corpus/` for each tag, the C# transforms
  reproduce them byte-for-byte from the source values (numeric, date, datetime spanning many
  seconds-of-day to exercise the ULP bitset, GENERAL strings with accents/expansions/trailing
  blanks). This proves keys will match before we generate any.
- **Needs:** only `CORPUS` — the generator emits a dedicated `value → key-bytes` table, so this is
  gated **without a working index**. It shares no code with `CDX-READ`; both feed `WRITE`.

### `EXPR` — expression engine, read/navigate subset · P1

- **Deliverable:** `CodeBase.Net.Expressions` lexer + precedence parser + typed AST + evaluator for
  the subset needed to *evaluate filters and simple key expressions* on read: field refs, `+ - * /`,
  comparisons, `.AND./.OR./.NOT.`, `$`, `SUBSTR/LEFT/RIGHT/TRIM/UPPER/STR/DTOS/VAL`, `IIF`,
  date/`EMPTY/DELETED` — plus the key-affecting quirks (prefix `=`, divide-by-zero→0, blank-date
  propagation, `hasTrim` NUL→blank). Bridges to `COLLATION` for `expr4key`.
- **Gate:** for the expression set evaluated over corpus records, C# results match the values the
  generator recorded (via `expr4vary`/`expr4str`/`expr4double`); and re-deriving each stored key
  from its expression matches the on-disk key (ties into the `COLLATION` transforms and
  `EXPRESSIONS.md` §7 text formats).
- **Needs:** `DBF-READ` (field values to evaluate over), `COLLATION` (for the key half of the gate).

### `QUERY` — bitmap query optimizer · P1 — **THE HEADLINE FEATURE**

The main value of the library (§1). It needs only the read path — a query optimizer never writes —
which is why it is P1 and write support is P2.

- **Deliverable:** `CodeBase.Net.Query` — port `BITMAP4` (`R4RELATE.H:268-307`, implemented across
  `C4CONST.C` and `m4map.c`): decompose a filter expression into a boolean tree (`BITMAP4LEAF` /
  `BITMAP4AND`, `andOr`, `children`) whose leaves are per-tag range constraints (`CONST4 lt/le/gt/
  ge/eq` + an `ne` list), evaluate each leaf by seeking its CDX tag, then AND/OR the resulting
  record-number sets. Includes `bitmap4create/evaluate/reduce/redistribute/combineLeafs/seek` and
  the `const4*` comparison helpers. Plus the minimum `RELATE4` scaffolding needed to host it —
  `relate4init(table)` + `relate4querySet(expr)` + `top/skip/bottom/eof/count` over the optimized
  set. **Single table only**; multi-table joins (`relate4createSlave`) stay out of v1 (§2.3).
- **Gate:** an optimizer has a *self-verifying* property, so this gate is strong without any
  reference bytes: for every (table, filter-expression) case, the record set returned via the
  optimized path must equal, in the same order, the set produced by a brute-force full scan
  evaluating the same expression per record. Run it across the corpus with expressions that
  exercise: fully-optimizable filters (every term maps to a tag), partially-optimizable ones
  (some terms have no tag — the remainder must fall back to scanning without dropping records),
  non-optimizable ones, `.AND.`/`.OR.` nesting, negation, open and closed ranges, `ne` lists,
  empty results, and full-table results. Additionally, the generator records the C library's own
  result set per case so we detect where our decomposition diverges from CodeBase's.
- **Correctness rule:** a wrong record set is far worse than a slow one. If the optimizer cannot
  prove a term is safe to optimize, it must fall back to scanning. The full-scan equivalence gate
  above is non-negotiable and runs on every case, always.
- **Needs:** `DBF-READ`, `CDX-READ`, `EXPR` — and `claude/specs/QUERY-OPTIMIZER.md`, which does not
  exist yet and is a stated prerequisite (R13, §9).

### `WRITE` — single-user write & round-trip · P2

- **Deliverable:** create table (+production CDX), `Append`/`Write`/`Blank`/`Delete`/`Recall`,
  field assignment, FPT memo read/write (allocation-at-EOF, big-endian headers, no free chain),
  **CDX leaf insert/split/remove + interior split + free list + version counter**, `Pack`/`Zap`/
  `Reindex`, `d4memoCompress` equivalent, header maintenance (skip byte-0 write window, EOF-byte
  asymmetry). Exclusive access only.
- **Gate:** three checks, none of which runs C at test time. The generator produces **before/after
  pairs** — for each mutation case it checks in the starting file, the operation, and the file the
  C library produced — so novel writes are gated against real reference bytes:
  1. **Reproduce.** C# builds a file from the same case definition the generator used; bytes must
     match the checked-in output (modulo the §8 benign divergences, asserted explicitly).
  2. **Round-trip.** C# opens a corpus file, rewrites it unchanged, bytes must be identical.
  3. **Mutate.** C# applies the case's operation to the "before" file and must produce the "after"
     file byte-for-byte. This is what covers leaf insert/split, interior split, free-list reuse,
     FPT growth, `Pack`/`Zap`/`Reindex` — cases a static corpus alone cannot reach.
- **Honest limitation:** coverage is bounded by the mutation cases the generator was taught. An
  operation nobody generated is not gated. When implementation surfaces an untested path, the fix
  is to add a generator case and regenerate — not to hand-write expected bytes. As a belt-and-braces
  step a developer may run the generator's `validate` mode over C#-written files; that is a manual
  confirmation, not part of the suite.
- **Needs:** `CDX-READ`, `COLLATION`, `EXPR` (you cannot write a key you cannot compute or verify).

### `LOCKING` — locking & multi-user · P3

- **Deliverable:** `CodeBase.Net.Locking` byte-range locks at the exact VFP offsets
  (record `0x7FFFFFFE − n`, append `0x7FFFFFFE`, file `[0x40000000, 0x7FFFFFFE]`, CDX `0x7FFFFFFE`,
  FPT `0x40000000`), in-memory lock registry, `unlockAuto` modes, self-conflict/`WAIT4EVER`
  deadlock rule, retry loop, cache-coherency ordering (flush-before-unlock / invalidate-after-lock),
  CDX version-counter re-validation.
- **Gate:** two parts. (a) *Always-on:* a C#-only multi-process test — two C# processes concurrently
  lock/append/read the same table without corruption, and the byte-range offsets observed on disk
  match the constants above. (b) *Opt-in interop:* a C# process and a second process built from
  `test-files-generator/` alternate as writers over the same table.
- **This is the one place the C build is still needed at test time.** Lock interop cannot be
  reduced to a static corpus — it needs a live second engine. Part (b) is therefore an opt-in test
  category (skipped unless the generator binary is present), not part of the default suite.
  VFP-live interop of the append path remains flagged for external verification.
- **Needs:** `WRITE`.

### `TRANS` — transactions & recovery · P3

- **Deliverable:** `code4tranStart/Commit/Rollback` (two-phase commit, mini-transactions,
  write-ahead log), the `.log` file format (`LOG4HEADER` 32-byte packed layout, entry types,
  backward-scan rollback), deferred index-key removal, clean/dirty-shutdown detection, and a
  recovery pass (replay/undo unfinished transactions).
- **Gate:** kill-and-recover test — abort mid-transaction, reopen, recover, and the table matches
  the pre-transaction state; reference `.log` files generated by the C library and checked into
  `net/corpus/` are parsed correctly by C#, and C#-written logs match the generator's bytes for the
  same transaction sequence; commit/rollback leave locks in the states `LOCKING-TRANSACTIONS.md`
  §4.11 specifies.
- **Needs:** `LOCKING`.

### `HARDENING` — hardening, benchmarks, API polish · P4

- **Deliverable:** fuzz/corruption-handling parity — `net/corpus/` gains deliberately corrupted files
  with the error code the C library returned for each recorded, and C# must reject them the same
  way (`e4data`/`e4index`/`e4memoCorrupt`); BenchmarkDotNet suite vs. representative workloads;
  XML docs; sample programs mirroring `EX1/BANK/EX65`.
- **Gate:** no golden-file regressions; benchmarks within an agreed factor of the C library on
  read and seek; public API reviewed against `API-ERRORS.md` §9 mapping.
- **Needs:** everything above, since it hardens all of it.

**Later (post-v1):** multi-table relations/joins, Unicode collated keys, extra VFP collations, DBT
memo, VFP9 varchar semantics (§2.3).

---

## 6. Testing strategy

The C library remains the ground truth, but it is consulted **offline**. It generates the corpus;
the corpus is checked in; the tests read the corpus. Building and testing `CodeBase.Net` never
compiles or runs C, and needs neither Windows nor MSVC.

### 6.1 The corpus generator (the `CORPUS` capability, P0)

A Windows/MSVC tool that links the original C library and writes golden files plus a dump of what
the C library believes is in them: field descriptors; each record's raw and decoded fields; each
tag walked in key order emitting `(rawKeyBytes, recno)`; memo entries; `d4check` result; and a
standalone `value → key-bytes` table for the `COLLATION` transform gate.

Established facts about the build, verified by construction (details in
`test-files-generator/README.md`):

- **It must be compiled as C++.** `d4declar.h:563-571` declares default arguments under
  `#ifdef __cplusplus` and call sites depend on them. As C it fails immediately with errors that
  look like unrelated configuration rot.
- **x86, MSVC.** 137 translation units compile clean on MSVC 19.51 with **zero edits** to
  `original/source`. The x64 (`S464BIT`) path has unresolved rot in the 64-bit file-offset layer.
- **A Linux/gcc build is not possible from this drop** — `S4UNIX` requires `p4port.h` ("CodeBase
  Portability version"), which is absent. This is why the tool is Windows-only.
- Build switches live in `src/cb-config.h`, force-included ahead of the shipped `D4all.h`, so
  `original/source` stays read-only as CLAUDE.md requires.
- Four files are excluded: `c4long.c`, `COLL4ARR.C`, `e4str2.c` (`#include`d by other translation
  units, not compiled standalone) and `M4MEM2.C` (OLE-DB only, needs the absent `defs5.hpp`).

**Why a generator and not a live oracle.** Making the C build a standing dependency of the test
suite would impose Windows + MSVC + x86 on every contributor and every CI run, to re-derive bytes
that do not change. Generating once and checking in the result gives the same authority at a
fraction of the friction, and makes the expected bytes reviewable in diffs.

**Corpus provenance is CodeBase, not VFP.** CodeBase and Visual FoxPro do not agree byte-for-byte
everywhere — `flags[8]` and `autoIncrementVal` are CodeBase-only, `0x31` is overloaded, and
CodeBase's auto-increment storage is outright VFP-incompatible (`DBF-FORMAT.md` §2.2, §7, and
line 359). Generated cases must therefore stick to genuine-VFP shape (`0x30`, those fields zero)
unless a case exists specifically to cover a CodeBase extension, in which case it is labelled as
such. Facts that only VFP itself can settle are listed in §9 and deferred to external verification.

### 6.2 Golden-file testing (byte-for-byte)

- **Read gate:** for every corpus file, C# must reproduce the checked-in dump exactly — record
  values, key sequences, memo contents.
- **Write gate:** C# rebuilds each case from its definition and the bytes must match the checked-in
  file; plus read-rewrite round-trips must be byte-identical.
- **Mutation gate:** the generator checks in before/after pairs (see `WRITE`), so operations that create
  novel bytes — leaf splits, interior splits, FPT growth, `Pack`/`Reindex` — are gated against real
  reference output rather than self-consistency.
- Differences that are *known-benign* (§8: header date stamp, CDX version counter, reserved zero
  areas, DBC backlink) are asserted explicitly, never ignored wholesale.

### 6.3 Corpus contents

The shipped `original/examples/DATA/` samples are **not sufficient** and are used only as
supplementary real-world input. Measured: 46 DBFs containing only types `C, D, L, M, N` (7 of 12
in-scope types absent), 40 of them dBASE III `0x03`, exactly one VFP `0x30` file, no `0x31`, no
nullable fields; and across 32 CDX files / 56 tags, **zero interior nodes**.

`net/corpus/` must therefore cover: every IN-scope field type; nullable fields; auto-increment/
timestamp; empty/`00000000` dates; datetimes across many seconds-of-day (ULP bitset); currency
signs and rounding; GENERAL-collated strings with accents, expansions, trailing blanks; descending
tags; unique/candidate tags; filtered tags; memos forcing FPT growth and compaction; leaf blocks
driven to the widening/split boundary; **multi-level index trees** (for interior nodes); and record
counts crossing `recNumLen` thresholds; deleted records; and a `0x31` CodeBase-extension case,
labelled as non-VFP. **Done since:** code-page-marked tables, single-byte and multi-byte (ADR-18).

**Named gaps, from the 002–005 audit (2026-08-13).** These are paths the generator *can* produce and was
never taught — unlike the corrupt-file guards, which are layer 3 because no valid file can express them.
Tracked here rather than in a step folder, because corpus contents are this document's to own. Full
disposition in `claude/dev/006-audit-glm/REMEDIATION-PLAN.md` §5.2.

| Gap | Why it matters |
|---|---|
| ~~A tag over a `T` datetime field~~ — **closed 2026-08-14** by `CDXTIME`, which also gates the `flags4dateTimeFlags` bitmap | was the only way to check a 10800-byte verbatim copy |
| **Deleted records** — all **1188** dumped corpus records are `deleted=0` | The `Deleted` accessor and the skip-over behaviour are unit-tested only |
| A **memo block size** other than 0 and 512 | The `blockSize == 0 ⇒ 1` branch and the block arithmetic are only witnessed at one value |
| A **CDX block size** other than 512, and a multiplier above one | `codeBaseNote` extension; unit-tested at `IndexHeaderTests:229`, no golden case |
| **`keyLen` above 240**, with the 16-bit compression counters that go with it | Currently refused; the refusal itself is ungated |
| A **multi-block tag directory** (roughly 40+ tags) | The directory's own chaining is never exercised |
| A tree built by **insert-and-split** rather than the bulk path | Every case appends then `INDEX ON`; only `CDXCOLL`'s second tag uses `i4tagAdd`. Different free-list state |
| A non-empty **free list** | Only a delete path fills it; the reader tolerates a 0-key non-root block untested |
| A **VFP 9 (`0x32`) table with a memo** | Not merely uncovered — `LegacyVariant.cs:10-15` *reproduces a C library defect* here (the memo file is not opened). A case would pin the reproduction |
| `SeekExact` on a **descending tag's duplicate run** | Unit-only; no descending tag has duplicate keys |
| `SeekNext` across a **branch boundary** | Unit-only; `CDXDEEP`'s duplicate runs stay within one leaf level |
| `SeekNext` against the reference on **numeric/date/currency** tags | Needs the value-to-key transforms — gated only once `COLLATION` lands |
| A **deep, multi-block single-tag `.IDX`** | `IDXONE` is single-block; a deep `.IDX` is structurally a deep `.CDX` tag but ungated as one |

Test frameworks and their version constraints are listed once, in `CLAUDE.md` §Technology stack.
How the tests are layered within a step — unit, component, fault injection, golden — is
`DEV_APPROACH.md` §4.

### 6.4 Supplementary cross-check (optional, low value — do not gate on it)

`DotNetDBF` (LGPL-2.1, NuGet) can independently decode plain DBF field data. Disagreement is a
useful signal, but it has **no CDX/index support at all**, DBT rather than FPT memo, lacks `Y`/`T`/
`H`, and defines `B` as *binary* where VFP means *double* — so it can silently agree while meaning
something different. Never use it for keys, indexes, or memos.

---

## 7. Risk register

| # | Risk | Impact | Likelihood | Mitigation |
|---|------|--------|-----------|------------|
| R1 | **CDX leaf-node bit-packed compression** decoded/encoded wrong (recNumLen/dupCnt/trailCnt bit layout, widening loop, split re-encode) | Index unreadable by VFP / wrong navigation; silent | High | De-risk **`CDX-READ`** before anything depends on it; generated leaf-block corpus decoded bit-identically to the checked-in dumps; port `b4leafInit`/`x4putInfo`/`b4key` faithfully (`CDX-FORMAT.md` §6) |
| ~~R2~~ **retired 2026-08-14** | **Collation tables** transcribed incorrectly, or someone reaches for `CultureInfo` | Keys sort differently → seek misses, corrupt index | High | §3.4 hard rule; verbatim `COLL4ARR.C` + `flags4dateTime`; `COLLATION` key-transform differential gate; code review flags any `CompareInfo`/`GetSortKey` in `Collation` |
| R3 | **VFP byte-range lock interop with live VFP apps** — append-path offset must match VFP exactly | Data corruption under real-world concurrent VFP + C# | Medium | Reproduce exact offsets (`LOCKING-TRANSACTIONS.md` §5.3); `LOCKING` two-process interop test; append path flagged for **external live-VFP verification** |
| R4 | **FPT block reuse / monotonic growth** — S4FOX keeps no free chain; wrong allocation orphans or overwrites blocks | Memo corruption; files grow unbounded | Medium | Port allocation-at-EOF + `nextBlock` header semantics exactly (`FPT-MEMO.md` §3.7); implement `d4memoCompress` equivalent; corruption-guard checks; `WRITE` memo round-trip gate |
| R5 | **Expression quirks leaking into index keys** — `STR()`/`DTOS()`/`TTOC()` text formatting, rounding, `*`-overflow; prefix `=`; divide-by-zero→0; blank-date propagation | Keys differ by a byte → silent seek failures | Medium | Port §7 of `EXPRESSIONS.md` bit-exactly; `c4dtoa45` behavior recovered empirically from generated output; `EXPR` expression + re-derived-key gate |
| R6 | **Torn reads under I/O optimization** — read caching on unlocked files can mix old/new bytes; write caching unsafe without record lock | Wrong data returned to app | Medium | Preserve flush-before-unlock / invalidate-after-lock ordering (`LOCKING-TRANSACTIONS.md` §3.7); default to no unsafe read-opt on shared files; document the hazard |
| R7 | **`flags4dateTimeFlags` ULP bitset** transcription error | datetime keys off by 1 ULP → seek equality breaks silently | Medium | Copy the 10 802-byte table verbatim; `COLLATION` gate covers many seconds-of-day; unit test the bit-extraction against generated key bytes |
| R8 | **Missing C bodies** (`c4dtoa45`, `c4ltoa45`, `c4atod`, `c4atoCurrency`, `code4initLow` defaults, `code4logOpen`) absent from the source drop | Numeric/currency text + defaults + log format guessed wrong | Medium | Recover behavior empirically from generated output; adopt documented Sequiter defaults; treat as `[UNVERIFIED]` and gate against corpus bytes |
| R9 | **0x31 → 0x30 version normalization / skip-byte-0 header rewrite** done naively | 0x31 files silently downgraded; extensions lost | Low | Replicate the "never rewrite byte 0" write windows (`DBF-FORMAT.md` §2.5); round-trip test on 0x31 files |
| R10 | **CDX shared-write root-swap (`tfile4swap`) & version-counter races** | Multi-writer index corruption | Low (v1 can restrict to exclusive) | Implement retry-from-root on inconsistency; consider exclusive-only CDX writes in v1; `LOCKING` gate |
| R11 | **Spec claims not adversarially re-verified** (see §9) | Building on a wrong spec fact | Medium | `CORPUS` spot-check pass against generated bytes; every capability gate is itself a spec check against real bytes |
| R12 | **Query optimizer returns a wrong record set** — a term optimized that should not have been, a range boundary off by one, `.OR.`/negation mis-combined, or a partially-optimizable filter silently dropping the unoptimizable remainder | Silently wrong query results — the worst possible failure for the library's headline feature, and invisible without a reference | **High** | `QUERY`'s full-scan equivalence gate runs on *every* case, always (§5); mandatory fall-back-to-scan whenever a term cannot be proven safe; generator records the C library's own result set per case to catch decomposition divergence |
| R13 | **No written spec for the optimizer** — the seven specs cover formats, not `BITMAP4`; it is the only in-scope subsystem being ported without a source-cited spec | Behavioral guesswork on the highest-value feature | Medium | Write `claude/specs/QUERY-OPTIMIZER.md` from `R4RELATE.H`/`C4CONST.C`/`m4map.c` **before** implementing `QUERY`, to the same `FILE.C:line` standard as the others (§9) |

---

## 8. Known-benign divergences to assert (not bugs)

- **DBF header date stamp** (bytes 1-3, `yy mm dd`) is the date the file was last written, so a file
  the port writes today will not match the corpus byte-for-byte there. The **corpus side is frozen**
  — the generator overwrites those three bytes with a constant after closing each table, so the
  checked-in files are byte-stable and regeneration produces no diff. The divergence therefore
  applies only to the `WRITE` gates: assert those three bytes explicitly (the port must write
  *today's* date, exactly as the C library does) and compare the rest.
- CDX tag-directory `version` change-counter (big-endian, file offset 8) and reserved-zero areas
  differ run-to-run — assert-and-mask.
- The per-tag `version` field of a *regular* tag header is never maintained by CodeBase and is
  always `00 00 00 00` (verified across all 33 sample CDX files in `original/examples/DATA/`). The
  port must likewise leave it zero — see `CDX-FORMAT.md` §10 for the `x4reverseShort` write quirk
  that makes any non-zero value diverge from the C library's bytes.
- DBC 263-byte backlink is always zeroed by CodeBase; preserve on modify, never populate.
- **A short fixed-width field at the end of a record.** The C library sizes a `B`/`H`/`Y`/`T`/`7`
  read from the type's natural width and ignores the descriptor's length, so a field declared
  narrower than its type reads into whatever follows. At the *end* of a record that is the C's own
  allocation slack — undefined, not a behaviour, so there is nothing to reproduce. The port clamps at
  the record boundary and treats the missing bytes as zero. Everywhere else the accessor widths are
  copied exactly, including reading a neighbouring field's bytes. (Step 001.)
- The `W`-field blank byte-order upstream quirk (`0x00,0x20` per char) — decide replicate vs fix and
  test the chosen behavior (`DBF-FORMAT.md` §11.2). Out of scope for v1 creation regardless.

---

## 9. Docs map

Which document owns what is defined once, in `CLAUDE.md` §"Where things live". In short: this plan
owns **what** we port and **at what priority**; `claude/specs/` own **what the bytes are**;
`claude/ARCHITECTURE-DECISIONS.md` owns **why** — decisions and rejected alternatives;
`claude/DEV_APPROACH.md` owns **how a step is run**; `STATE.md` owns **where we are**.
`DEV_APPROACH.md` does not override this plan on scope or priority, nor the specs on format facts.

The seven files under `claude/specs/` are the **format authority**. They were authored *from the C
source with `FILE:line` citations*. **Honesty note:** their independent adversarial re-verification
pass did **not** complete — the citations are believed correct but were not double-checked by a
second, hostile reviewer. Therefore a **spot-check verification of the specs against generated bytes is
itself a standing `CORPUS` task** (see §6.1), and every capability gate re-checks the relevant spec facts
against real bytes.

| Spec file | Subsystem it governs |
|---|---|
| `claude/specs/DBF-FORMAT.md` | DBF header, field descriptors, record layout, all field-type encodings, `_NullFlags`, auto-increment/timestamp, code page, large-file, EOF-byte rules |
| `claude/specs/CDX-FORMAT.md` | CDX file structure, tag directory, tree-block headers, interior nodes (big-endian), **bit-packed leaf compression**, seek/insert/split/remove, free list, version counter, locking positions |
| `claude/specs/KEY-COLLATION.md` | Expression-result → sortable key bytes; verbatim collation tables; numeric/date/datetime/currency transforms; `flags4dateTime`; descending/null; **why `CultureInfo` must not be used** |
| `claude/specs/FPT-MEMO.md` | FPT (and DBT) header/block formats, big-endian fields, allocation, no-free-chain growth, compaction, memo references in the record, corruption checks, locking |
| `claude/specs/LOCKING-TRANSACTIONS.md` | Byte-range lock protocol at VFP offsets, in-memory lock registry, `unlockAuto`, transaction/log format, two-phase commit, mini-transactions, recovery, .NET mapping |
| `claude/specs/EXPRESSIONS.md` | Lexer/parser/precedence, function table, type system, key-affecting semantics (STR/DTOS/TTOC/TRIM/UPPER, prefix `=`, divide-by-zero), expression↔key bridge |
| `claude/specs/API-ERRORS.md` | Public API inventory, `r4*`/`e4*` code values, field-type constants, `CODE4` defaults, and the C-group → C# type mapping |
| `claude/specs/QUERY-OPTIMIZER.md` | **NOT YET WRITTEN** — `BITMAP4` tree structure and flags, `CONST4` range constraints, filter→bitmap decomposition rules, which expression forms are optimizable, leaf evaluation via tag seek, AND/OR/negation set combination, and the fall-back-to-scan boundary. Sources: `R4RELATE.H:268-396`, `C4CONST.C`, `m4map.c`. **Writing this is a prerequisite for `QUERY`** (risk R13) |

**The optimizer is the one in-scope subsystem without a spec.** The seven format specs were written
before the optimizer was promoted into v1 scope (§2.1), so it is currently the only headline
component that would be implemented from the C source directly rather than from a source-cited
specification. Write `QUERY-OPTIMIZER.md` to the same standard first.

These seven specs were verified against the C source and against real sample files shipped with the
library (`original/examples/DATA/`), and corrected in place; they are the authoritative record of
on-disk formats.
