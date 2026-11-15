# CodeBase.NET

A modern, fully-managed **C#/.NET port of the CodeBase database engine** — read and write
Visual FoxPro-compatible `DBF` tables, `CDX` compound indexes, and `FPT` memo files
**byte-for-byte**, with no native dependencies.

> **Status: reading works, writing does not.** Tables, memo files and CDX indexes can be opened and
> read, and a table can be navigated in an index tag's order. Nothing writes yet, and the query
> optimizer — the reason the project exists — is still ahead. See [`STATE.md`](STATE.md).

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

## Differences from CodeBase

Where this library deliberately answers differently from the C library, each with the decision behind
it in [the decision record](claude/ARCHITECTURE-DECISIONS.md):

- **A tag keyed by `DATE()`, `TIME()` or `RECCOUNT()` is refused when selected** (ADR-73). Such a key
  is the clock or the table's size at the moment it was built, so it can never be found again; the C
  library opens the tag and maintains an index that disagrees with itself. The refusal names the
  function. A tag keyed by `RECNO()` is supported — a record's number is the same every time.
- **A tag keyed through the date picture — `DTOC` or `CTOD` in its expression — is refused until
  `CodeBaseEngine.ExpressionOptions` is set** (ADR-42, ADR-74). The index does not record which
  picture built its keys, and two pictures of one length key the same date differently, so the C
  library reads such a tag under whatever picture it was compiled with. Set the options to the
  picture the index was built with — `ExpressionOptions.CodeBase` for the C library's default build —
  and the tag is used.
- **A table whose records CodeBase compressed is refused when opened** (ADR-56). That is CodeBase's
  whole-table compression — `d4compress`, or a table created with compressed data — which Visual
  FoxPro cannot read either, and which even CodeBase opens read-only. Copy it to an uncompressed
  table with CodeBase first. Compressed memo entries in an ordinary table are read (ADR-55).
- **`STR()` of a `W` field narrows by the table's code page, not the machine's** (ADR-77). CodeBase
  converts the UTF-16 text with Windows' ANSI code page, so its answer — and every key built from it —
  depends on the computer. This library uses the code page the table's text is in and reproduces
  Windows' best-fit mapping, so the two agree on a Windows machine whose ANSI code page is the table's.
- **A tag collated `CB00004` is refused when the index is opened** (ADR-84). That is the collation
  CodeBase reads from three tables (`coll4inf`, `collate4`, `compres4`) on its user's own disk; no
  index carries them. CodeBase's other collations — Croatian, Spanish, Avaya, and GENERAL by name,
  `CB00002` to `CB00010` — are read and sought.
- **`SeekPrefix` on a collated `W` or `O` tag finds what begins with the prefix** (ADR-85). CodeBase,
  given a value shorter than the key, searches zero bytes and lands on the tag's first key. Whole
  values, and every key, are CodeBase's.
- **A `W` field on a Visual FoxPro 9 table (`0x32`) is refused** (ADR-76). There it is VFP 9's Blob;
  CodeBase reads it as its own UTF-16 string and gets it wrong. Elsewhere `W` and `O` are read, and
  their unassigned tail is U+2000, which is what CodeBase writes there — not a space.

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

`UPPER` and `LOWER`, in filters and in index expressions, change case by the table's own code page —
the same answer on every machine, which neither CodeBase nor Visual FoxPro gives, since both follow
the host's ANSI code page. An unmarked table changes `a`–`z` only, as CodeBase does, unless you set
`DefaultEncoding`. One consequence: an index another engine built with `UPPER`/`LOWER` over non-ASCII
letters under a different mapping holds keys this library does not compute, and seeks or queries
through that tag can be wrong. Reindex such a tag. It cannot be detected from the file.

## Reading a table in index order

```csharp
using CodeBase.Net;
using CodeBase.Net.Dbf;

using CodeBaseEngine engine = new();
using Table table = engine.OpenTable("customers.dbf");

FieldDefinition name = table.Fields["NAME"];

// The production index opens with the table when its header declares one.
Tag byName = table.Tags["NAME"];

table.SelectTag(byName);

for (GoResult go = table.Top(); go == GoResult.Ok; )
{
    Console.WriteLine(table.GetString(name));

    if (table.Skip(1) != SkipResult.Moved)
        break;
}
```

`SelectTag` makes the tag the cursor's order: `Top`, `Bottom` and `Skip` then move through the index
instead of through record numbers, exactly as `d4tagSelect` does in the C library, and `Go` still means
a record number. Where a caller would rather not change the table's mode, the same walk is available
per call — `GoFirstIndexed(tag)`, `GoNextIndexed(tag)`, `GoLastIndexed(tag)`, `GoPreviousIndexed(tag)` —
and both forms share one cursor per tag, so a walk started with either continues with the other.

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
