# 001-dbf-open-and-header — summary

**Closed:** 2026-08-09. **Gate passed.** Capability advanced: `DBF-READ`, metadata half.
**Amended 2026-08-10** — the code-page half of this step was wrong, not merely ungated; see the last
section and `STATE.md` beside this file.

Assume nobody re-reads `DESIGN.md` or `PLAN.md` after this. What matters beyond the step is here.

## What shipped

Open a DBF file, and the memo file beside it when the header declares one, and report the table's
shape. `dotnet test net/CodeBase.Net.sln` was green on **224 tests** at close — 135 unit, component and
fault, 89 golden — with the gate, `TableMetadataGoldenTests`, executing 36 across all five corpus
tables. After the 2026-08-10 amendment: **341 tests**, 225 unit and 116 golden, over seven tables.

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");

foreach (FieldDefinition f in table.Fields)
    Console.WriteLine($"{f.Name} {f.Type}({f.Length},{f.Decimals}) @{f.RecordOffset}");
```

`net/` holds the solution, `Directory.Build.props`, the library, `tests/` and `corpus/`. Four
projects: `CodeBase.Net` (**no NuGet dependencies**, ADR-17), `CodeBase.Net.Tests`,
`CodeBase.Net.Golden`, `CodeBase.Net.TestUtils`.

**Not in it, by design:** any record or field *value*, any navigation, memo payloads, writing,
locking. Step 002 takes the `[records]` section of the same dumps.

## The five things worth knowing

**1. The Visual FoxPro type gate is a *signed* comparison.** `d4->version` is a plain `char`
(`d4data.h:3220`), signed on MSVC, so every byte from `0x80` up is negative and compares below
`0x30`. Read as unsigned — which is how `DBF-FORMAT.md` §2.1 stated it — `0xF5`, an everyday FoxPro 2
table with a memo, silently admits `T`, `Y` and `B` fields the C library refuses. Caught by a failing
test, not by review. Spec corrected.

**2. Open-time length validation is almost entirely absent in a release build.** Only
`I`/`P`/`R`/`Q`/`V`/`5`/`1`/`6` are length-checked; the familiar `M`/`G` four-or-ten rule is
`#ifdef E4MISC`, and `C N F D L B H Y T 7 0` are never checked. Of the types this port reads, **the
only length rule is that an integer is four bytes.** A reader that rejects a 7-byte date is *less*
compatible than the original, not more — eight tests assert such files **open**. Spec corrected.

**3. Safety is structural, not a by-product of validation.** Since most checks are gone, containment
rests on one unconditional invariant: `1 + Σ fieldLength == recordLength`, with offsets accumulated
from 1. That makes every field's bytes a subrange of the record by construction. It is asserted as a
property over generated descriptors, and that property test runs with the optional length checks
disabled so containment can never quietly come to depend on one.

**4. `Span<byte>` boundaries cannot be mocked, at all.** `It.IsAny<Span<byte>>()` does not compile;
a lambda passing a ref struct cannot become an expression tree; and Castle DynamicProxy boxes
arguments into an `object[]`, which a ref struct cannot enter — so the generated proxy throws
`InvalidProgramException` on first call. Verified, not assumed. Every byte-reading boundary in this
port is faked by hand, and each fake is kept honest by a **contract test both it and the real
implementation pass** (`RandomAccessSourceContract`). That pattern is now `DEV_APPROACH.md` §5.

**5. Golden tests were mutation-checked three times.** A gate that cannot fail is worse than none.
Moving a header read from offset 8 to 10 turned eleven golden tests red; dropping the field-name
upper-casing turned the gate red; breaking `InMemorySource`'s end-of-file behaviour turned the fake's
contract tests red while the real file's passed. Separately, the containment property test was found
**vacuous** — a random record length never matched the generated descriptors, so all 2000 iterations
took the refusal branch and the containment half never ran. It now asserts that more than 100
iterations actually resolved.

## Deviations from the design and plan

| Deviation | Why |
|---|---|
| A fourth project, `CodeBase.Net.TestUtils` | `DEV_APPROACH.md` §4 wants one corpus resolver and both test projects need it. Cost: a `**/*.TestUtils/**` coverage exclusion in `SonarQube.Analysis.xml` |
| Sub-step order changed: the dump parser moved from 7 to 5 | So every later sub-step is gated against real bytes as it lands, not four sub-steps afterwards. Prompted by the question "shouldn't we take a real snapshot from our corpus?" — the answer was yes, and earlier |
| The dump reader is **strict**, not tolerant | Its first draft skipped unknown sections. That leaves a golden test comparing an empty list and passing. It now reads two sections, names `[records]` as deliberately unread, and refuses anything else, a missing section, or a section present but empty |
| `CodeBaseException` carries `Code` only, not `ExtendedCode` | Nothing propagates an E-number yet, so the property would be permanently zero. Additive to add later |
| Long field names rejected by the descriptor reader, not the header | A 32-byte header decode knows nothing of descriptor layouts, and the C library also accepts `flags[4]` at header level and branches later |
| Fault injection uses hand-written fakes, not Moq | See point 4 — structural, not a choice |
| Three fault rows moved from sub-step 2 to the opener | They need the file's length, which a pure header decode does not have |

## Divergences from the C library

Only one, and it is not a choice: a fixed-width field declared narrower than its natural width, at
the **end** of a record, would have the C library read past the record buffer into its own allocation
slack — undefined, so there is nothing to match. We clamp at the record end and treat the missing
bytes as zero. Everywhere else the per-type accessor widths are copied exactly, including the C's
habit of reading a `T` field's full 8 bytes regardless of a shorter declared length. Promoted to
`PORTING-PLAN.md` §8.

## Left open

- ~~**No corpus table has a non-zero `codePage`**, so `CodePageMap`'s real branches are ungated.~~
  **Closed 2026-08-10, and it was worse than "ungated".** `CP1251.DBF` and `CP936.DBF` joined the
  corpus (ADR-18), and gating the map showed it was **incorrect**: `EncodingFor` hard-coded four code
  page numbers, so 22 of the 26 marks Visual FoxPro documents decoded as cp437 without any error.
  Visual FoxPro's documentation is now the authority for the mark table (ADR-19, `DBF-FORMAT.md`
  §8.1), all 26 are implemented, and `Table.CodePageNumber` reports the number (ADR-20). **The
  lesson worth carrying: "ungated" and "correct" are different claims, and this step conflated
  them** — a fallback that cannot fail will not show up as a bug until something exercises it.
  What remains for step 002 is only the last rung, bytes to string, plus the two decoding defaults
  `Encoding` currently chooses for us (a character cut in half becomes U+FFFD silently; an undefined
  byte passes through as a control character).
- **Still ungated:** version `0x31` with feature flags, the `H` field type, and the long-field-name
  layout that is currently refused.
- **No test uses Moq any more.** The package and the `DynamicProxyGenAssembly2` visibility entry are
  dead weight, kept because `IClock` and `IFileLocks` are coming and take no spans. Drop them if
  those never arrive.
- **`Microsoft.NET.Test.Sdk` pinned to 17.14.1**, not 18.x, which is an untested pairing with
  `xunit.runner.visualstudio` 3.1.5. Revisit when the runner ships a 4.x stable.
