# Working in this repository

This is **CodeBase.NET** — a C#/.NET port of the CodeBase xBase database engine (DBF tables, CDX
indexes, FPT memo files), targeting **byte-for-byte Visual FoxPro compatibility**. Read
[`README.md`](README.md) for the project overview and [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md)
for scope, architecture, and the milestone roadmap before starting non-trivial work.

## The one rule that matters most

**Compatibility means reproducing exact bytes, not equivalent behavior.** Files this library writes
must be openable by Visual FoxPro and the original C library, and files they write must be read
correctly here — including index key ordering and CDX leaf compression. When in doubt, match the
bytes the original produces. Verify against the real sample files in `original/examples/DATA/`.

## Source of truth

- **`claude/specs/*.md` are authoritative for on-disk formats and engine behavior.** They were
  written from the C source with `FILE.C:line` citations and verified against both the source and
  real sample files. If code and a spec disagree on a byte layout, the spec wins — or the spec has a
  bug worth fixing, not working around silently.
- **`claude/PORTING-PLAN.md` governs *how* we build** — scope, architecture, milestone ordering,
  and each milestone's verification gate. It does not override the specs on format facts.
- **`original/source/` is read-only reference.** Never modify it. Filenames are mixed-case; search
  case-insensitively (`grep -ri`, `find -iname`). Focus on the `S4FOX` build configuration; ignore
  `S4CLIENT` (client/server) code paths entirely.

## Non-obvious gotchas (from the verified specs)

- **Collation tables must be ported verbatim.** Do **not** use .NET `CultureInfo` / `CompareInfo` /
  `GetSortKey()` to build index keys — they cannot reproduce the exact stored bytes. Port the
  `COLL4ARR.C` translation/compress tables as static byte arrays. See `specs/KEY-COLLATION.md`.
- **CDX and FPT use big-endian fields** (root pointers, block numbers, block sizes, memo lengths),
  while DBF headers are little-endian. Don't assume one endianness across the codebase.
- **CDX leaf nodes are bit-packed/compressed**, not fixed-size entry arrays — the highest-risk area.
  See `specs/CDX-FORMAT.md` before touching index reading.
- **`-0.0` sorts via the positive key path** (`-0.0 >= 0` is true), and the byte-add wraps; the
  naive `bits | 0x8000…` form is not bit-exact. See `specs/KEY-COLLATION.md`.
- **Native VFP writes a trailing `0x1A`** even on `0x30` files, though CodeBase itself does not;
  readers must tolerate a stray trailing marker. See `specs/DBF-FORMAT.md`.
- **`CODE4` defaults are set in `code4initLow`, not by zero-init** — e.g. `compatibility = 25`,
  `limitKeySize = 1`. See `specs/API-ERRORS.md`.

## API design conventions (once implementation starts)

- **Idiomatic C#, not a C transliteration.** Typed exception hierarchy (`CodeBaseException` carrying
  the original `ErrorCode`/`ExtendedCode` as properties) for errors; `r4*` flow/status values
  (found/eof/locked/…) stay as return-value enums.
- `IDisposable` ownership hierarchy, properties over getter/setter methods, `Stream`-based I/O,
  `Span<byte>` for buffers.

## Technology stack

.NET 8+ / C# 12, **xUnit v3**, **AwesomeAssertions** (not FluentAssertions — commercial since v8),
BenchmarkDotNet, System.Text.Encoding.CodePages.

## Testing

Correctness is anchored on a **reference oracle** — the original C library and the real sample files
in `original/examples/DATA/` — used to differential-test the C# code byte-for-byte in both
directions. Prefer real-file / differential tests over hand-written expected values for format code.
Each milestone in the plan has a verification gate; don't consider a milestone done until its gate
passes.

## License

The project is **GNU GPL v3** (`LICENSE`). The original CodeBase library is LGPL v3; this port is a
GPL-v3 derivative. Keep license headers consistent with GPL v3 when adding source files.
