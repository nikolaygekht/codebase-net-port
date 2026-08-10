# 001-dbf-open-and-header — state

Live record of execution (phase 6). Short-lived: it is superseded by `SUMMARY.md` when the step
closes. Project-level state is the root [`STATE.md`](../../../STATE.md).

**Step closed 2026-08-09.** All 8 sub-steps done, gate green. See `SUMMARY.md`, which is what a
future session should read; this file is the working record behind it.

`dotnet test` green: 135 unit + 89 golden.

## Sub-step progress

| # | Step | Status |
|---|---|---|
| 1 | Solution skeleton + corpus helper + guard tests | **done** |
| 2 | `DbfHeader` decodes the 32-byte header | **done** |
| 3 | `FieldDescriptor` + `FieldDescriptorTable` | **done** |
| 4 | Format variant answers the four version questions | **done** |
| 5 | `CorpusDump` parser + golden tests for header and [descriptors] | **done** |
| 6 | `FieldResolver` produces the field table | **done** |
| 7 | `DbfOpener` + `CodeBaseEngine` + `Table` | **done** |
| 8 | The gate: `TableMetadataGoldenTests` | **done** |

## What sub-step 1 landed

`net/CodeBase.Net.sln` (four projects), `net/Directory.Build.props`, and:

- `net/CodeBase.Net` — net8.0, `GenerateDocumentationFile`, **no package references** (ADR-17), and
  `InternalsVisibleTo` for the three test assemblies plus `DynamicProxyGenAssembly2` so Moq can
  proxy the internal boundary interfaces. No types yet.
- `CodeBase.Net.TestUtils` — a plain library, not a test project. Holds `Corpus`, the single
  resolver of the corpus directory (`DEV_APPROACH.md` §4), and will hold the in-memory boundary
  fakes. It searches upward from **the directory holding its own assembly** — so it does not depend
  on the working directory, verified by running the suite from `/tmp` — for a `corpus/` directory
  **that contains `.DBF` files**, stopping at a filesystem root or at `/home` or `/mnt`. The
  content requirement is what stops an empty `corpus/` higher up the tree from shadowing the real
  one and yielding a silently empty golden suite.
- `CodeBase.Net.Tests` — layers 1-3. Builds; empty, so `dotnet test` reports "no test is available"
  for it until sub-step 2.
- `CodeBase.Net.Golden` — layer 4. `CorpusLayoutTests`, 6 passing.

**Verification:** `dotnet build` clean with `TreatWarningsAsErrors`; `dotnet test` green, 6 tests.
`--collect "XPlat Code Coverage" --logger trx` both produce their files, so the Sonar wiring in
`FOR-DEVELOPERS.md` is real rather than aspirational.

## What sub-step 2 landed

`ErrorCode` (four values so far, the `e4` numbers verified against both `API-ERRORS.md` §3 and
`d4defs.h`), `CodeBaseException`, `DbfHeader` and `FeatureFlags` — the last two `internal`, since the
public surface exposes their contents through `Table`, not the types themselves. 22 unit tests.

`FeatureFlags` is its own type rather than eight bytes on the header: the validation rule is
conditional on the version, which reads far better as an argument to one focused parser than as a
branch buried in the header decode.

## What sub-step 3 landed

`FieldDescriptor` and `FieldFlags` (both **public**, per Decision 17) and `FieldDescriptorTable`
(internal). 18 more unit tests.

`FieldDescriptor` is strictly the *stored* view — nothing interpreted, nothing corrected:

- the name keeps its stored case, so `_NullFlags` survives as written and can be matched byte-exact,
  while `FieldDefinition` will report the upper-cased name (Decision 15);
- the type letter is not upper-cased, since that is an open-time step;
- `Length` and `Decimals` stay separate bytes, because combining them into a 16-bit character length
  needs the type, which this view does not judge;
- `StoredOffset` is reported even when nonsensical, since the engine recomputes offsets and ignores
  it (Decision 1).

**Spot-checked against real bytes before writing the tests** (risk R11): decoding `VFPNULL.DBF`'s
descriptor region by hand reproduced all 14 lines of the checked-in dump exactly, terminator at
region offset 448. So the offsets in `DBF-FORMAT.md` §4 are right, and the layer-1 tests below are
not merely self-consistent. That check was a script run once; sub-step 5 turns it into a standing
golden test.

## What sub-step 4 landed

`IDbfFormatVariant` with its `Resolve` factory, `VisualFoxProVariant` (0x30 and 0x31, the only
variant that reads descriptor flags), `LegacyVariant` (everything else, carrying its own version
byte), and `DbfVersion` for the two constants that matter by identity. 26 more unit tests.

**A bug the tests caught, and the most valuable finding of the step so far.** The version comparison
that gates Visual FoxPro field types is **signed**: the C library holds the version in a plain
`char` (`d4data.h:3220`), signed on MSVC, and the generator builds without `/J`. So every byte from
0x80 up is negative and compares below 0x30. My first implementation compared `byte` values, which
made **0xF5 — an ordinary FoxPro 2 table with a memo — appear to allow datetime, currency and double
fields**, which the C library refuses on it. Fixed to `(sbyte)version >= 0x30`, and the theory now
pins 0x7F true, 0x83 and 0xF5 false.

This is the failure mode the specs warn about: `DBF-FORMAT.md` §2.1 and §5 describe the rule as
"`version >= 0x30`" with no mention of signedness, and both readings look right in isolation. Added
to the spec follow-ups in `DESIGN.md`.

## What sub-step 5 landed

`CorpusDump`, `DumpDescriptor`, `DumpField` and `DumpTokens` in the Golden project, plus the golden
tests they enable. 29 more golden tests, so the layer went from 6 to 35.

**The header and stored descriptors of all five tables are now decoded from the real bytes and
compared against what the C library says is in them.** That is the check no unit test can perform,
and it retires the self-consistency caveat standing over sub-steps 2 and 3. One extra invariant is
asserted per table while the data is to hand: the fields plus the deletion flag account for exactly
the record length, which is the foundation Decision 18 rests containment on.

**The golden layer was mutation-checked.** Moving the header-length read from offset 8 to offset 10
turned eleven golden tests red across all five tables; reverted and rebuilt. A golden suite that
cannot fail is worse than none, because it reports confidence it has not earned, and nothing else in
the suite would have caught this.

**The parser is strict.** Its first draft skipped unknown sections for forward compatibility, which
was wrong twice over: the DBF format is frozen so there is nothing to be compatible with, and the
*dump* format is ours and will grow an index half (ADR-13) — so an unnoticed section would leave a
golden test comparing an empty list and passing. It now reads two sections, names `[records]` as
deliberately unread, and refuses anything else, a dump missing a section or header value it needs,
or a section present but empty. Optional tokens stay optional per ADR-16: absence of `nullable=1` is
the answer, not a gap.

## What sub-step 6 landed

`FieldDefinition` (public) and `FieldResolver` with `ResolvedFields` (internal). 35 unit tests and
14 more golden ones; the golden layer passed on its first run, so the resolution rules were read
right.

Two rules came from the C source rather than the specs, and neither is stated in them:

- **The reported type is derived from the binary marking, per type** (`f4type`, `F4FIELD.C:261-292`).
  A stored `C` that is marked binary reports `Z`; a stored `M` reports `X` — but only when its
  binary marking is the flag, since a plain memo is marked binary too, by a different value, and
  still reports `M`. A general field is never restored to another letter.
- **Length and decimals resolve per type, in five groups** (`D4OPEN.C:384-427`). Only numeric,
  float, currency, datetime, binary float and double report a decimal count at all; integers,
  logicals, dates, memos and general fields report none; and everything else — the character type
  and the null-flags field, which reaches it by falling through — treats the decimal byte as the
  high half of a 16-bit length.

**Validation is one rule.** Reading the C's field switch case by case (Decision 19), the only
length check that fires in a release build on a type this port reads is that an integer is four
bytes. Every other length check it makes belongs to a type rejected as out of scope, and the
familiar rules for dates, logicals and memos fire only under `E4MISC`. Eight tolerance tests assert
that a date of 7 bytes, a logical of 2, a memo of 5 and the rest all **open**.

**The property test was vacuous, and a guard on the guard now says so.** It generated descriptors
and a random record length, then asserted "either refused, or every field lies inside the record" —
but a random record length never matches the descriptors, so all 2000 iterations took the refusal
branch and the containment half never ran. Half the lengths are now chosen to fit, and the test
asserts that more than 100 iterations actually resolved. Verified by reverting the fix: the guard
fails with "found 0".

## What sub-step 7 landed

The whole open path: `IRandomAccessSource`, `IRandomAccessSourceFactory`, `ICompanionFileResolver`
and their one concrete implementation `FileSystem`; `SourceReader` for reads that must not come up
short; `MemoFileHeader`; `CodePage` and `CodePageMap`; `DbfOpener` with `OpenedTable`; and the public
`CodeBaseEngine`, `Table` and `FieldCollection`. Plus `InMemorySource`, `FakeFileSystem` and
`FaultySource` in `TestUtils`, and 12 component and fault tests.

**Moq cannot mock `IRandomAccessSource`, and it is structural rather than a version problem.** Its
`Read` takes a `Span<byte>`, and no mocking library can express a ref-struct parameter in an
expression tree: `It.IsAny<Span<byte>>()` does not compile. The boundary that most wanted a mock is
the one boundary that cannot have one. Fault injection uses a hand-written `FaultySource` instead,
and the tests came out better for it — they assert `source.IsDisposed` after the failure, which
states the outcome rather than the call that produced it. `DESIGN.md`'s seams table corrected.

Consequence to settle in `SUMMARY.md`: **no test uses Moq any more.** The package and the
`DynamicProxyGenAssembly2` visibility entry are currently dead weight, kept because `IClock` and
`IFileLocks` are coming and take no spans.

**A short read is never padded.** `SourceReader.ReadExactly` refuses rather than zero-filling,
because the bytes that failed to arrive would decode as zeros and a header of zeros is a
plausible-looking header.

**Nothing leaks on a failed open.** The memo file is opened after the table, so a failure there is
the case that could strand a handle; the opener closes whatever it opened before rethrowing, and a
test asserts every source handed out was disposed.

## Decisions taken during execution

Neither changes `DESIGN.md`; both are recorded here and belong in `SUMMARY.md`.

1. **A fourth project, `CodeBase.Net.TestUtils`** (named `TestSupport` when first written).
   `DESIGN.md` implied three. `DEV_APPROACH.md` §4
   requires *one* corpus resolver, and both test projects need it — Golden to read files, layers 2-3
   to load corpus bytes into fakes. A plain library is where that belongs; the alternative was
   duplicating the helper or having one test project reference another.
   Consequence: `SonarQube.Analysis.xml` gained a `**/*.TestUtils/**` coverage exclusion, since the
   scanner sees a library, not a test project, and would count it against coverage.
2. **`CodeBaseException` carries `Code` only, not `ExtendedCode`.** `PORTING-PLAN.md` §4.1 documents
   both, `ExtendedCode` mirroring the C library's internal E-number. Nothing propagates one yet, so
   the property would be permanently zero — a promise the type could not keep. Adding it later is
   additive and non-breaking; shipping an always-zero property is not.
3. **The long-field-name layout is reported by the header, not refused by it.** `PLAN.md` had the
   `NotSupported` rejection in sub-step 2. `DbfHeader` decodes 32 bytes and knows nothing of the
   descriptor layout, and the C library likewise accepts `flags[4]` at header level and branches
   later, so `Flags.UsesLongFieldNames` is reported and sub-step 3 does the refusing. `PLAN.md`
   corrected in place.
4. **Three fault rows moved from sub-step 2 to the opener.** `headerLen` past EOF, `numRecs`
   exceeding what the file can hold, and the 32-bit overflow case all need the file's length, which
   a pure header decode does not have. `PLAN.md` now splits the group into "contradicts itself" and
   "contradicts the file".
8. **The dump parser moved from sub-step 7 to sub-step 5**, so `FieldResolver`, the opener and the
   already-written header and descriptor decoders are all gated against real bytes as they land
   instead of four sub-steps later. The prompt was the right question — hand-built bytes can only
   ever prove a decoder agrees with the offsets typed into its own test, and every sub-step that
   went by widened the window in which a self-consistent misreading could hide. Sub-step 5 now also
   carries golden tests for the `header` and `[descriptors]` sections, which retires that caveat for
   sub-steps 2 and 3. The hand-built unit tests stay: no corpus file is malformed, so they remain the
   only way to test refusal.
5. **`FieldDescriptorTable` takes the long-name flag as an argument** rather than reading a header.
   It keeps the entity pure and one-purpose: the caller already holds the header, and the table
   reader needs one bit of it, not a dependency on the header type.
6. **A descriptor cut short by the end of the region is `Data`, distinct from "no terminator".**
   Both are corrupt, but the messages name different faults, which matters when the corpus grows a
   deliberately truncated case.
7. **`Corpus.TableNames` discovers, and a separate test pins the expectation.** Discovery means a new
   generator case is covered without editing a list; `CorpusLayoutTests` asserts the discovered set
   equals the five documented cases, so broken discovery fails loudly instead of producing an empty
   golden suite. That is the plan's "count guard", implemented before anything it guards exists.

## Findings worth carrying

- **The .NET 10 SDK writes `.slnx` by default.** `dotnet new sln` produced one; the plan and ADR-14
  call for `CodeBase.Net.sln`, and the Sonar scanner does not read `.slnx`. Recreated with
  `--format sln`, and noted in `FOR-DEVELOPERS.md` so it is not "fixed" back.
- **Microsoft.NET.Test.Sdk is pinned to 17.14.1**, not the current 18.8.1. VSTest 18 with
  `xunit.runner.visualstudio` 3.1.5 is untested pairing; 17.14.1 is the version the runner documents
  against. Revisit when the runner ships a 4.x stable.
- **AwesomeAssertions 9.5.0 detects xUnit v3 correctly.** Verified with a deliberate failing
  assertion, which reported as an ordinary test failure through
  `AwesomeAssertions.Execution.LateBoundTestFramework` with the *because* message intact — not
  "test framework could not be detected". A green suite never exercises that path, so it was checked
  explicitly and the probe removed.
- `xunit.v3` does not contribute implicit usings; test files need `using Xunit;` even with
  `ImplicitUsings` enabled.

## Housekeeping applied mid-step

- **`TestSupport` renamed to `TestUtils`** across the project, solution, Sonar exclusion and docs.
- **The library moved from `net/src/CodeBase.Net/` to `net/CodeBase.Net/`**, and `net/src/` is gone.
  `net/` now holds the solution, `Directory.Build.props`, the library, `tests/` and `corpus/`.
  Updated with it: the three project references, the solution, `CLAUDE.md`, `FOR-DEVELOPERS.md`,
  `PORTING-PLAN.md` §3.1 (whose tree also gained `Directory.Build.props` and `TestUtils`), and this
  step's `DESIGN.md` and `PLAN.md`. `SonarQube.Analysis.xml` needed no change — none of its paths
  named `src/`.

## Blockers

None.
