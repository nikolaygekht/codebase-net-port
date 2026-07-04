# CodeBase.NET

A modern, fully-managed **C#/.NET port of the CodeBase database engine** — read and write
Visual FoxPro-compatible `DBF` tables, `CDX` compound indexes, and `FPT` memo files
**byte-for-byte**, with no native dependencies.

> **Status: pre-implementation.** The format specifications and the master porting plan are
> complete and verified against the original C source and real sample files. The C# code has not
> been written yet. This repository currently holds the reference material that the implementation
> will be built from.

## What it is

CodeBase is a mature C library (originally by Sequiter Software, Inc., ~117k lines) implementing the
xBase / dBASE family of database files with full DBF, CDX index, and FPT memo support. This project re-implements the **stand-alone, Visual FoxPro-compatible**
subset of that engine as idiomatic C#, so that .NET applications can work with legacy xBase data —
and so that files written by this library are openable and correctly navigable by Visual FoxPro and
by the original C library, and vice versa.

"Byte-for-byte" is the whole point. Index key ordering and CDX leaf compression must reproduce the
exact stored bytes, not merely an equivalent result. This is a rewrite in the spirit of the
original — **not** a P/Invoke wrapper and **not** a mechanical transliteration of the C.

## Scope

**In scope (v1):**
- Stand-alone (single-process and multi-user file locking); no client/server.
- Visual FoxPro DBF format (version bytes `0x30`/`0x31`, plus FoxPro 2.x `0x03`/`0xF5`).
- VFP field types (C, N, F, D, L, M, B, I, Y, T, G, and the `_NullFlags` null bitmap).
- CDX compound indexes (`S4FOX`), including the bit-packed leaf compression.
- FPT memo files.
- The xBase expression engine (needed to create and maintain indexes).
- Byte-range locking and transactions/logging.

**Out of scope:**
- Client/server (`S4CLIENT`).
- MDX (dBASE IV) and NTX (Clipper) index formats.
- OLE-DB `r5*` extension types, reporting, encryption, compression, Palm/CE.

A "later maybe" list (relations/query optimizer, Unicode keys, additional collations, DBT memo)
lives in the porting plan.

## Repository layout

| Path | Contents |
|------|----------|
| `claude/PORTING-PLAN.md` | The master plan: intent, scope, architecture, milestone roadmap with verification gates, testing strategy, and risk register. **Start here.** |
| `claude/specs/` | Seven source-cited format specifications — the **authoritative** description of the on-disk formats and engine behavior. |
| `original/source/` | The original CodeBase C source (reference only — never modified). |
| `original/examples/` | Original C examples and **real sample `DBF`/`CDX`/`FPT` files** used as a verification oracle. |
| `LICENSE` | GNU General Public License v3. |

### The specifications

| Spec | Covers |
|------|--------|
| `specs/DBF-FORMAT.md` | DBF header, field descriptors, record layout, all field-type encodings, `_NullFlags`, code pages, EOF rules. |
| `specs/CDX-FORMAT.md` | CDX file structure, tag directory, node layouts, and the bit-packed leaf compression. |
| `specs/KEY-COLLATION.md` | Expression-result → sortable key bytes, and the verbatim collation tables. |
| `specs/FPT-MEMO.md` | FPT (and DBT) header/block formats, allocation, and record memo references. |
| `specs/LOCKING-TRANSACTIONS.md` | Byte-range lock protocol at VFP offsets, transaction/log format, recovery. |
| `specs/EXPRESSIONS.md` | Lexer/parser/precedence, the built-in function table, and key-affecting semantics. |
| `specs/API-ERRORS.md` | Public API inventory, `r4*`/`e4*` codes, `CODE4` defaults, and the C → C# mapping. |

## Planned technology stack

- **.NET 8+** (LTS), C# 12
- **xUnit v3** for tests
- **AwesomeAssertions** for fluent assertions
- **BenchmarkDotNet** for performance work
- **System.Text.Encoding.CodePages** for legacy code-page support

## Testing approach

Correctness is anchored on a **reference oracle**: the original C library, plus the real sample
files in `original/examples/DATA/`, are used to differential-test the C# implementation
byte-for-byte in both directions. Building that oracle is Milestone 0. See the porting plan for the
full verification-gated roadmap.

## License

Licensed under the **GNU General Public License v3** (see [`LICENSE`](LICENSE)).

The original CodeBase library by Sequiter, Inc. is distributed under the GNU Lesser General Public
License v3; this port is a derivative work relicensed under GPL v3 as permitted by the LGPL.
