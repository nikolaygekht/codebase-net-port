# CodeBase.NET

A modern, fully-managed **C#/.NET port of the CodeBase database engine** — read and write
Visual FoxPro-compatible `DBF` tables, `CDX` compound indexes, and `FPT` memo files
**byte-for-byte**, with no native dependencies.

> **Status: pre-implementation.** The format specifications, the porting plan and the golden test
> corpus exist; the C# code does not yet. Nothing here is usable as a library today.

## What it is

[CodeBase](https://github.com/MPSystemsServices/CodeBase-for-DBF) is a mature C library by Sequiter,
Inc. (~117k lines) implementing the xBase / dBASE family of database files, with full DBF, CDX index
and FPT memo support. Formerly proprietary, it was **released as open source under the LGPL v3 in
2018** and is published at that repository by M-P Systems Services, Inc. under agreement with
Sequiter. This project re-implements the **stand-alone, Visual FoxPro-compatible** subset of that
engine as idiomatic C#, so that .NET applications can work with legacy xBase data — and so that files
written by this library are openable and correctly navigable by Visual FoxPro and by the original C
library, and vice versa.

"Byte-for-byte" is the whole point. Index key ordering and CDX leaf compression must reproduce the
exact stored bytes, not merely an equivalent result. This is a rewrite in the spirit of the
original — **not** a P/Invoke wrapper and **not** a mechanical transliteration of the C.

## Why it exists

Byte-compatibility is the foundation, not the goal. The library's main value is its **bitmap query
optimizer**: Rushmore-style filter decomposition that answers a query by seeking index tags and
combining record-number sets, instead of scanning the table. Everything in the read path exists to
make that possible.

Its correctness rule outranks its speed — a wrong record set is far worse than a slow one, so any
filter term that cannot be proven safe to optimize falls back to scanning, and every case is checked
against a brute-force full scan.

## Scope

**In scope (v1):**
- Stand-alone operation, single-process and multi-user file locking; no client/server.
- Visual FoxPro DBF format (version bytes `0x30`/`0x31`, plus FoxPro 2.x `0x03`/`0xF5`).
- VFP field types (`C N F D L M B I Y T G`, and the `_NullFlags` null bitmap).
- CDX compound indexes, including the bit-packed leaf compression.
- FPT memo files.
- The xBase expression engine needed to create and maintain indexes.
- **The bitmap query optimizer.**
- Byte-range locking, transactions and logging.

**Out of scope:**
- Client/server.
- MDX (dBASE IV) and NTX (Clipper) index formats.
- OLE-DB extension types, reporting, encryption, compression, Palm/CE.

Multi-table joins, Unicode collated keys, additional collations and DBT memo are possible later; see
[the porting plan](claude/PORTING-PLAN.md) for the full scope and the priority of each capability.

## Requirements

.NET 8 or later. No native dependencies, no NuGet dependencies, and nothing platform-specific in the
library itself.

One thing the host application must do: DBF record text is stored in a legacy code page
(cp437/cp850/cp1252/cp1250), and .NET 8 does not provide those encodings until a provider is
registered. The library reads whatever you registered and never registers one itself, because doing
so would change `Encoding.GetEncoding` for every other component in your process. So if you read text
from a table, reference `System.Text.Encoding.CodePages` and call this once at start-up:

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```

Alternatively, set `CodeBaseEngine.DefaultEncoding` to an encoding you already hold — that covers
tables whose code-page byte is unmarked or unrecognized without any provider at all.

## Documentation

- [`FOR-DEVELOPERS.md`](FOR-DEVELOPERS.md) — building, testing, and contributing.
- [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md) — scope, architecture, and what gets ported at
  what priority.
- [`claude/specs/`](claude/specs/) — seven source-cited specifications of the on-disk formats,
  written from the original C source. Useful in their own right if you work with xBase files.
- [`STATE.md`](STATE.md) — current progress.

## Licence

Licensed under the **GNU General Public License v3** (see [`LICENSE`](LICENSE)).

The original [CodeBase](https://github.com/MPSystemsServices/CodeBase-for-DBF) library by Sequiter,
Inc. is distributed under the GNU Lesser General Public License v3; this port is a derivative work
relicensed under GPL v3 as permitted by the LGPL.
