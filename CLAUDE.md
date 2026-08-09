# Working in this repository

This is **CodeBase.NET** — a C#/.NET port of the CodeBase xBase database engine (DBF tables, CDX
indexes, FPT memo files), targeting **byte-for-byte Visual FoxPro compatibility**. Read
[`README.md`](README.md) for the project overview and [`claude/PORTING-PLAN.md`](claude/PORTING-PLAN.md)
for scope, architecture, and the milestone roadmap before starting non-trivial work.

**The library's main value is the bitmap query optimizer** (Rushmore-style: decompose a filter into
per-tag range constraints, evaluate each by seeking a CDX tag, combine the record-number sets —
instead of scanning). Byte-compatibility is the foundation that makes it possible, not the goal.
It is milestone M5 and lands before any write support. Its one absolute rule: **a wrong record set
is far worse than a slow one** — if a term can't be proven safe to optimize, fall back to scanning,
and the full-scan equivalence gate runs on every case, always.

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
- **Endianness is per-field, not per-format.** DBF is little-endian throughout. **FPT is big-endian**
  for its header (`nextBlock`, `blockSize`) and every block header (`type`, `numChars`). **CDX is
  little-endian** — including the tag-header `root`/`freeList`/`keyLen`/`blockSize`, the
  `B4STD_HEADER` (`nodeAttribute`, `nKeys`, `leftNode`, `rightNode`) and the leaf `B4NODE_HEADER` —
  with exactly **two** big-endian islands: the tag-directory `version` counter (file offset 8) and
  the two trailing 4-byte fields (record number, child node) of each interior-node entry. Rule of
  thumb when reading the C: a swap guarded by `#ifdef S4BYTE_SWAP` means the field is *little*-endian
  on disk; an unguarded swap or one under `#else`/`#ifndef S4BYTE_SWAP` means *big*-endian. Don't
  assume one endianness across a file. See `specs/CDX-FORMAT.md` §5.1/§10 and `specs/FPT-MEMO.md`.
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

Correctness is anchored on **`corpus/` — checked-in golden files with the expected dumps beside
them**, generated offline by the original C library. Tests read the corpus; **building or testing
`CodeBase.Net` never compiles or runs C**, and needs neither Windows nor MSVC. Prefer golden-file
and round-trip tests over hand-written expected values for format code. Each milestone in the plan
has a verification gate; don't consider a milestone done until its gate passes.

`test-files-generator/` is the Windows/MSVC developer tool that produces the corpus (see its
`README.md` and `PORTING-PLAN.md` §6.1). It is **not** part of the solution and not a test
dependency. Run it only when the corpus needs new cases, and check in what it produces. If an
implementation path turns out to be untested, add a generator case and regenerate — never
hand-write expected bytes.

Two traps if you touch it: the C library **must be compiled as C++** (`d4declar.h:563-571` uses
`#ifdef __cplusplus` default arguments that call sites rely on), and it must be built **x86** (the
x64 `S464BIT` path is broken in the 64-bit file-offset layer). A Linux/gcc build is impossible from
this drop — `S4UNIX` needs `p4port.h`, which was never shipped.

Two things the corpus must not be naive about: `original/examples/DATA/` is **supplementary only**
(mostly dBASE III, missing 7 of 12 in-scope field types, and zero interior nodes across all 56
tags), and generated files carry **CodeBase** provenance, not VFP — stick to genuine-VFP shape
(`0x30`, `flags[8]`/`autoIncrementVal` zero) unless a case exists specifically to cover a CodeBase
extension.

## License

The project is **GNU GPL v3** (`LICENSE`). The original CodeBase library is LGPL v3; this port is a
GPL-v3 derivative. Keep license headers consistent with GPL v3 when adding source files.

# Ground Rules

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.