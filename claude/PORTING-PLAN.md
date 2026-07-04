# CodeBase → C#/.NET Master Porting Plan

**Status:** Authoritative master plan for all planning, scope, architecture, and roadmap
decisions. (Supersedes an earlier draft analysis, since removed.)
**Format authority:** the seven source-cited specifications under `claude/specs/` (see
[§9 Docs Map](#9-docs-map)). This plan governs *how* we build; the specs govern *what the bytes
are*. Where this plan and a spec disagree on an on-disk fact, the spec wins.
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
- **VFP collation** for index keys, ported as **verbatim byte tables** (machine + GENERAL for
  cp1252/cp437/cp850). Governed by `KEY-COLLATION.md`.
- **FPT memo files** (VFP 4-byte block references, big-endian header/block headers, no free chain,
  monotonic growth + explicit compaction). Governed by `FPT-MEMO.md`.
- **The xBase expression engine** needed for tag key generation and filters. Governed by
  `EXPRESSIONS.md`. (Split across milestones — see §5.)
- **Single-user and multi-user record/file/append/index/memo locking**, and the transaction/log
  subsystem, at the exact VFP-compatible byte offsets. Governed by `LOCKING-TRANSACTIONS.md`.
- **Idiomatic public API** with a typed exception hierarchy that preserves the original error code.
  Governed by `API-ERRORS.md`.

### 2.2 OUT of scope

- **Client/server** (`S4CLIENT`/`S4SERVER`) — networking, pipes, connection auth.
- **MDX** (dBase IV compound index) and **NTX** (Clipper single-tag index). *Note the corrected
  naming:* MDX is dBase IV's format; NTX is Clipper's. We do neither. CDX only.
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

### 2.3 LATER maybe

- Relations / query optimizer (`relate4*`, bitmap optimizer) — high value, deferrable to v2.
- Unicode (`r5wstr`/`r5wstrLen`) collated index keys (CodeBase extension; VFP has none).
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
Codebase.Net.sln
├─ src/
│  ├─ CodeBase.Net/                 # the library (net8.0)
│  └─ CodeBase.Net.Oracle/          # (test-only) P/Invoke to the compiled C oracle, see §6
├─ tests/
│  ├─ CodeBase.Net.Tests/           # xUnit v3 unit tests
│  ├─ CodeBase.Net.Differential/    # golden-file + round-trip differential tests
│  └─ CodeBase.Net.Benchmarks/      # BenchmarkDotNet
├─ oracle/                          # C harness sources + build script (Milestone 0)
├─ corpus/                          # generated golden DBF/CDX/FPT files + manifests
├─ claude/specs/                    # format authority (do not edit casually)
└─ PORTING-PLAN.md                  # this file
```

Single assembly `CodeBase.Net`, LGPL v3. `System.Text.Encoding.CodePages` is a dependency (for
cp437/cp850/cp1252 *record-text decoding only* — never for index keys, see §3.3).

### 3.2 Namespace / module layout and which spec governs each

| Namespace | Contents | Governing spec |
|---|---|---|
| `CodeBase.Net` | `CodeBaseEngine` (was `CODE4`), `Table` (`DATA4`), `Field`, `Tag`, `IndexFile`, `DbExpression`, `Transaction`; the public surface | `API-ERRORS.md` |
| `CodeBase.Net.Dbf` | DBF header, field descriptors, record layout, per-type in-record encode/decode, `_NullFlags`, auto-increment/timestamp | `DBF-FORMAT.md` |
| `CodeBase.Net.Cdx` | CDX file structure, tag directory, `B4STD_HEADER`, interior nodes, **bit-packed leaf codec**, seek/insert/split/remove, free list, version counter | `CDX-FORMAT.md` |
| `CodeBase.Net.Collation` | Verbatim collation byte tables, key transforms (`t4dblToFox`, `t4intToFox`, …), `flags4dateTime` table, pChar rules | `KEY-COLLATION.md` |
| `CodeBase.Net.Memo` | FPT header/block codec (big-endian), allocation-at-EOF, compaction; DBT kept behind an interface but not implemented in v1 | `FPT-MEMO.md` |
| `CodeBase.Net.Expressions` | Lexer, precedence parser, typed AST, evaluator, key-conversion bridge to `Collation` | `EXPRESSIONS.md` |
| `CodeBase.Net.Locking` | Byte-range lock manager at VFP offsets, in-memory lock registry, transaction log reader/writer, mini-transactions, recovery | `LOCKING-TRANSACTIONS.md` |
| `CodeBase.Net.IO` (internal) | `DbFileStream` — `FileStream` wrapper with region locking at the exact CodeBase offsets, sequential read/write helpers, flush-to-disk | `LOCKING-TRANSACTIONS.md` §5, `DBF-FORMAT.md` §9 |

### 3.3 Cross-cutting architectural rules

1. **Little-endian by default, explicit big-endian where the spec says so.** The C code writes
   native x86 structs. We use `BinaryPrimitives.ReadUInt32LittleEndian`/`WriteUInt32BigEndian`
   explicitly, never `Marshal.StructToPtr`, never `BitConverter` without an endianness decision.
   Known big-endian islands: CDX tag-header `version` counter; CDX interior-node recno + child
   pointers; FPT header `nextBlock`/`blockSize` and block header `type`/`numChars`.
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

## 5. Milestone roadmap (verification-gated, not calendar-based)

Progression is gated by *tests that must pass*, not by weeks. Each milestone lists its deliverable,
its verification gate (which golden-file / differential test must pass), and its dependencies. The
ordering front-loads the two highest-risk items: **CDX leaf bit-packing** and **collation tables**.

### M0 — Reference Oracle (BUILD FIRST)

- **Deliverable:** compile the original C library under WSL/Linux from `original/source` as
  `S4FOX` + `S4STAND_ALONE`; build a small C harness (`oracle/`) that (a) *generates* golden
  DBF/CDX/FPT corpora from declarative manifests and (b) *validates* a file (open, walk every tag
  in key order, dump records/keys, run `d4check`). Wrap it for the test suite (CLI + optional
  P/Invoke via `CodeBase.Net.Oracle`).
- **Gate:** oracle builds; round-trips its own generated corpus; `d4check` clean; a checked-in
  manifest → deterministic bytes across two runs.
- **Depends on:** nothing. Blocks every differential gate below.

### M1 — DBF reading

- **Deliverable:** `CodeBase.Net.Dbf` open + header parse + field descriptors + record navigation
  (`Top/Bottom/Skip/Go/Eof/Bof/RecNo/RecCount`) + per-type field decode for all IN-scope types +
  `_NullFlags` + deleted-flag semantics. Read-only.
- **Gate:** for every DBF in the oracle corpus, C# decodes every field of every record identically
  to the oracle's dump (differential, byte/value-exact including blank/`00000000` dates, currency
  ×10⁴, datetime julian+ms, 4-byte memo refs read as ints).
- **Depends on:** M0.

### M2 — CDX reading & navigation (DE-RISK EARLY)

- **Deliverable:** `CodeBase.Net.Cdx` — parse tag directory, tag headers, interior nodes
  (big-endian recno/child), and **decode bit-packed leaf nodes** (`B4NODE_HEADER`, recNumLen/
  dupCntLen/trailCntLen widths, key reconstruction with dup/trail + pChar). Full-tag seek
  (`tfile4seek`), skip, top/bottom, descending traversal. **No expression engine required** — keys
  are read from disk, not recomputed. Machine collation only for key *comparison* (unsigned memcmp).
- **Gate:** for every tag in every corpus CDX, walking the C# B-tree in key order yields the exact
  `(key-bytes, recno)` sequence the oracle emits; partial seeks land on the same entry; a
  fuzz corpus of leaf blocks decodes bit-identically to the oracle. This gate retires the single
  biggest technical risk.
- **Depends on:** M1. Independent of M4/M5.

### M3 — Collation tables & key transforms

- **Deliverable:** `CodeBase.Net.Collation` — verbatim `COLL4ARR.C` tables + `flags4dateTime`;
  transforms `t4dblToFox`, `t4floatToFox`, `t4intToFox`, `t4unsignedIntToFox`, `t4i8ToFox`,
  `t4curToFox`, `t4dateTimeToFox`, GENERAL subSortCompress + simple char transforms, pChar rules,
  null-byte prefix, descending complement.
- **Gate:** given the *raw stored key bytes* the oracle produced for each corpus tag, the C#
  transforms reproduce them byte-for-byte from the source values (numeric, date, datetime spanning
  many seconds-of-day to exercise the ULP bitset, GENERAL strings with accents/expansions/trailing
  blanks). This proves keys will match before we generate any.
- **Depends on:** M0. Can run in parallel with M2 (shares no code; both feed M6).

### M4 — Expression engine: read/navigate subset

- **Deliverable:** `CodeBase.Net.Expressions` lexer + precedence parser + typed AST + evaluator for
  the subset needed to *evaluate filters and simple key expressions* on read: field refs, `+ - * /`,
  comparisons, `.AND./.OR./.NOT.`, `$`, `SUBSTR/LEFT/RIGHT/TRIM/UPPER/STR/DTOS/VAL`, `IIF`,
  date/`EMPTY/DELETED` — plus the key-affecting quirks (prefix `=`, divide-by-zero→0, blank-date
  propagation, `hasTrim` NUL→blank). Bridges to M3 for `expr4key`.
- **Gate:** for a corpus of expressions evaluated over corpus records, C# results match the oracle
  (`expr4vary`/`expr4str`/`expr4double`); and re-deriving each stored key from its expression
  matches the on-disk key (ties into M3's transforms and `EXPRESSIONS.md` §7 text formats).
- **Depends on:** M1, M3.

### M5 — Single-user write & round-trip (DBF+CDX+FPT)

- **Deliverable:** create table (+production CDX), `Append`/`Write`/`Blank`/`Delete`/`Recall`,
  field assignment, FPT memo read/write (allocation-at-EOF, big-endian headers, no free chain),
  **CDX leaf insert/split/remove + interior split + free list + version counter**, `Pack`/`Zap`/
  `Reindex`, `d4memoCompress` equivalent, header maintenance (skip byte-0 write window, EOF-byte
  asymmetry). Exclusive access only.
- **Gate (bidirectional):** (a) C#-created DBF/CDX/FPT open cleanly in the oracle, `d4check`-clean,
  and every record/key/memo matches; (b) files the oracle created and then the C# port modified
  re-validate in the oracle. Insert/split stress test: build a large index in C# and in the oracle
  from the same key stream → identical file bytes (modulo the documented benign version-counter /
  reserved-area differences, asserted explicitly).
- **Depends on:** M2, M3, M4.

### M6 — Locking & multi-user (shared access)

- **Deliverable:** `CodeBase.Net.Locking` byte-range locks at the exact VFP offsets
  (record `0x7FFFFFFE − n`, append `0x7FFFFFFE`, file `[0x40000000, 0x7FFFFFFE]`, CDX `0x7FFFFFFE`,
  FPT `0x40000000`), in-memory lock registry, `unlockAuto` modes, self-conflict/`WAIT4EVER`
  deadlock rule, retry loop, cache-coherency ordering (flush-before-unlock / invalidate-after-lock),
  CDX version-counter re-validation.
- **Gate:** a C# process and an oracle process concurrently lock/append/read the same table without
  corruption; lock byte offsets observed on disk match; interop test with the two engines
  alternating writers. (VFP-live interop of the append path flagged for external verification.)
- **Depends on:** M5.

### M7 — Transactions & recovery

- **Deliverable:** `code4tranStart/Commit/Rollback` (two-phase commit, mini-transactions,
  write-ahead log), the `.log` file format (`LOG4HEADER` 32-byte packed layout, entry types,
  backward-scan rollback), deferred index-key removal, clean/dirty-shutdown detection, and a
  recovery pass (replay/undo unfinished transactions).
- **Gate:** kill-and-recover test — abort mid-transaction, reopen, recover, and the table matches
  the pre-transaction state; log files written by C# are readable by the oracle's log-examination
  API and vice-versa; commit/rollback leave locks in the states `LOCKING-TRANSACTIONS.md` §4.11
  specifies.
- **Depends on:** M6.

### M8 — Hardening, benchmarks, API polish

- **Deliverable:** fuzz/corruption-handling parity (`e4data`/`e4index`/`e4memoCorrupt` on the same
  inputs the oracle rejects), BenchmarkDotNet suite vs. representative workloads, XML docs, sample
  programs mirroring `EX1/BANK/EX65`.
- **Gate:** no differential regressions; benchmarks within an agreed factor of the C library on
  read and seek; public API reviewed against `API-ERRORS.md` §9 mapping.
- **Depends on:** M7.

**Later (post-v1):** relations/query optimizer, Unicode collated keys, extra VFP collations, DBT
memo, VFP9 varchar semantics.

---

## 6. Testing strategy

Three layers, all anchored to the **reference oracle** — the C library is the ground truth.

### 6.1 Reference oracle (Milestone 0, the spine of everything)

Compile `original/source` as `S4FOX`/`S4STAND_ALONE` under WSL/Linux. Build a C harness that:

- **Generates** golden corpora from checked-in declarative manifests (JSON/text describing fields,
  records, tag expressions, memo blobs, code page, compatibility). Deterministic output.
- **Validates/dumps** any file: open, list fields, walk each tag in key order emitting
  `(rawKeyBytes, recno)`, dump each record's raw + decoded fields, dump memo entries, run `d4check`.
- Exposed to the C# suite as a CLI (stdout dumps) and, where convenient, via P/Invoke through
  `CodeBase.Net.Oracle` (test-only).

Because the specs note their adversarial re-verification did not complete (see §9), **an early task
in M0 is a spot-check pass**: pick a handful of spec claims per format (e.g. FPT `numChars` =
payload-only, CDX interior big-endian recno, the 263-byte VFP reserved area, `t4dblToFox` sign
rule) and confirm them against oracle-produced bytes before building on them.

### 6.2 Differential testing (both directions, byte-for-byte)

- **C reads C# / C# reads C:** every corpus file is produced by one engine and validated by the
  other; record values, key sequences, and memo contents must match exactly.
- **Byte-for-byte file compare** where the algorithm is deterministic (bulk index build from a
  fixed key stream, table create with fixed records). Differences that are *known-benign* (version
  counter, reserved zero areas, DBC backlink) are asserted explicitly, not ignored wholesale.
- **Key-transform differential:** feed source values through both the oracle's `t4*ToFox` and the
  C# transforms; compare bytes (this is how M3 is gated independently of a full index).

### 6.3 Corpus

A versioned `corpus/` of manifests covering: every IN-scope field type; nullable fields; auto-
increment/timestamp; empty/`00000000` dates; datetimes across many seconds-of-day (ULP bitset);
currency signs and rounding; GENERAL-collated strings with accents, expansions, trailing blanks;
descending tags; unique/candidate tags; filtered tags; memos forcing FPT growth and compaction;
leaf blocks driven to the widening/split boundary; large record counts crossing recNumLen
thresholds. Frameworks: **xUnit v3**, **AwesomeAssertions** (Apache-2.0 — *not* FluentAssertions,
which is commercial since v8), **BenchmarkDotNet**, `System.Text.Encoding.CodePages`.

---

## 7. Risk register

| # | Risk | Impact | Likelihood | Mitigation |
|---|------|--------|-----------|------------|
| R1 | **CDX leaf-node bit-packed compression** decoded/encoded wrong (recNumLen/dupCnt/trailCnt bit layout, widening loop, split re-encode) | Index unreadable by VFP / wrong navigation; silent | High | De-risk in **M2** before anything depends on it; leaf-block fuzz corpus decoded bit-identically to oracle; port `b4leafInit`/`x4putInfo`/`b4key` faithfully (`CDX-FORMAT.md` §6) |
| R2 | **Collation tables** transcribed incorrectly, or someone reaches for `CultureInfo` | Keys sort differently → seek misses, corrupt index | High | §3.4 hard rule; verbatim `COLL4ARR.C` + `flags4dateTime`; M3 key-transform differential gate; code review flags any `CompareInfo`/`GetSortKey` in `Collation` |
| R3 | **VFP byte-range lock interop with live VFP apps** — append-path offset must match VFP exactly | Data corruption under real-world concurrent VFP + C# | Medium | Reproduce exact offsets (`LOCKING-TRANSACTIONS.md` §5.3); M6 two-process interop test; append path flagged for **external live-VFP verification** |
| R4 | **FPT block reuse / monotonic growth** — S4FOX keeps no free chain; wrong allocation orphans or overwrites blocks | Memo corruption; files grow unbounded | Medium | Port allocation-at-EOF + `nextBlock` header semantics exactly (`FPT-MEMO.md` §3.7); implement `d4memoCompress` equivalent; corruption-guard checks; M5 memo round-trip gate |
| R5 | **Expression quirks leaking into index keys** — `STR()`/`DTOS()`/`TTOC()` text formatting, rounding, `*`-overflow; prefix `=`; divide-by-zero→0; blank-date propagation | Keys differ by a byte → silent seek failures | Medium | Port §7 of `EXPRESSIONS.md` bit-exactly; `c4dtoa45` behavior recovered empirically vs oracle; M4 expression-differential + re-derived-key gate |
| R6 | **Torn reads under I/O optimization** — read caching on unlocked files can mix old/new bytes; write caching unsafe without record lock | Wrong data returned to app | Medium | Preserve flush-before-unlock / invalidate-after-lock ordering (`LOCKING-TRANSACTIONS.md` §3.7); default to no unsafe read-opt on shared files; document the hazard |
| R7 | **`flags4dateTimeFlags` ULP bitset** transcription error | datetime keys off by 1 ULP → seek equality breaks silently | Medium | Copy the 10 802-byte table verbatim; M3 gate covers many seconds-of-day; unit test the bit-extraction against oracle |
| R8 | **Missing C bodies** (`c4dtoa45`, `c4ltoa45`, `c4atod`, `c4atoCurrency`, `code4initLow` defaults, `code4logOpen`) absent from the source drop | Numeric/currency text + defaults + log format guessed wrong | Medium | Recover behavior empirically from oracle output; adopt documented Sequiter defaults; treat as `[UNVERIFIED]` and gate against oracle bytes |
| R9 | **0x31 → 0x30 version normalization / skip-byte-0 header rewrite** done naively | 0x31 files silently downgraded; extensions lost | Low | Replicate the "never rewrite byte 0" write windows (`DBF-FORMAT.md` §2.5); round-trip test on 0x31 files |
| R10 | **CDX shared-write root-swap (`tfile4swap`) & version-counter races** | Multi-writer index corruption | Low (v1 can restrict to exclusive) | Implement retry-from-root on inconsistency; consider exclusive-only CDX writes in v1; M6 gate |
| R11 | **Spec claims not adversarially re-verified** (see §9) | Building on a wrong spec fact | Medium | M0 spot-check pass against oracle; every milestone gate is itself a spec check against real bytes |

---

## 8. Known-benign divergences to assert (not bugs)

- CDX `version` change-counter and reserved-zero areas differ run-to-run — assert-and-mask.
- DBC 263-byte backlink is always zeroed by CodeBase; preserve on modify, never populate.
- The `W`-field blank byte-order upstream quirk (`0x00,0x20` per char) — decide replicate vs fix and
  test the chosen behavior (`DBF-FORMAT.md` §11.2). Out of scope for v1 creation regardless.

---

## 9. Docs map

The seven files under `claude/specs/` are the **format authority**. They were authored *from the C
source with `FILE:line` citations*. **Honesty note:** their independent adversarial re-verification
pass did **not** complete — the citations are believed correct but were not double-checked by a
second, hostile reviewer. Therefore a **spot-check verification of the specs against the oracle is
itself an early M0 task** (see §6.1), and every milestone gate re-checks the relevant spec facts
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

These seven specs were verified against the C source and against real sample files shipped with the
library (`original/examples/DATA/`), and corrected in place; they are the authoritative record of
on-disk formats.
