# Defect: `Seek(string)` on a tag whose keys are not character

> **Fixed 2026-10-29** in step 012 (`012-p1-tails`, sub-step 2): the text overloads refuse every tag
> whose keys are not character, and `FoxNumeric` has its layer-1 tests (items 1 and 2 below). Items 3
> and 4 remain open. Decision: ADR-58.

**Found 2026-08-21**, while writing sub-step 5's cached-vs-uncached equivalence test. Not a cache
defect: it is present with the cache off, and the cache is what made me point a string seek at every
tag of every corpus table for the first time.

**Not fixed here.** This is a correctness defect inside a performance step, and folding it in would mix
a behaviour change into the step whose gate is "nothing a file decodes to may change". It wants its own
step, its own mutation check and its own corpus witness.

## What happens

`Table.Seek(string)` reaches `TableTagCursor.SearchFor(value, prefix)`
(`Dbf/TableTagCursor.cs:80`). For `KeyKind.Character` and `KeyKind.CollatedCharacter` it does the right
thing. Every other kind falls into `default:` and hands the **encoded string's bytes** to
`RecordKey.WriteValue` (`Cdx/RecordKey.cs:88`), which exists to build a key from *a record's field
bytes* and therefore reads them as such:

| tag's key kind | what a string seek does | how it shows |
|---|---|---|
| `N`, `F` numeric | `FoxNumeric.ToDouble` on the text; **unparseable text is 0.0** (`Dbf/FoxNumeric.cs:37-44`) | **silently seeks the key for zero** and reports Ok or NoRecord |
| `B` double | `BitConverter.ToDouble` needs exactly 8 bytes | `ArgumentOutOfRangeException` |
| `I` int, `Y` currency | `BinaryPrimitives.ReadInt32/Int64LittleEndian` over text bytes | a garbage key when the string is wide enough, `ArgumentOutOfRangeException` when it is not |
| `D` date, `T` datetime | same, through `FoxDate.ToJulian` / `FromDateTime` | as above |

The numeric row is the serious one: **no exception, a plausible answer, the wrong key.** `CLAUDE.md`
names that outcome as worse than any slowness, and this is a case of it.

The zero is not a bug in `FoxNumeric.ToDouble` — a blank numeric field really does read as zero, which
is what the C library does and what the corpus witnesses. It is wrong only on this path, where text
that was never a number is being turned into a key.

## Why it is a contract break and not just a rough edge

`STATE.md` §1 states the promise: *"`Seek` also takes a `double`, an `int` and a `DateOnly` where the
tag's keys are of that kind, and **refuses by name where they are not**."* The mirror case is missing:
a **string** on a tag whose keys are not character neither refuses nor works. Step 008 built the
refusals in one direction and this direction was never exercised, because no test had ever pointed a
string seek at a numeric tag.

It is also the same shape as a defect step 007's audit already fixed once: *"a `'T'` millisecond count
that leaves the calendar is a library error rather than an `ArgumentOutOfRangeException`"*.

## What the fix looks like

`SearchFor`'s `default:` branch should refuse the way `SearchForDouble` and friends refuse when the kind
does not match — a `CodeBaseException` naming the tag and the kind it wants. The numeric paths already
have the message shape to copy (`Dbf/TableTagCursor.cs:136-160`, "This tag's keys are not numbers").

Cheap, but it needs its own gate: a corpus test that every tag of every corpus table refuses a string
seek unless its keys are character, and the mutation check that the refusal is not decorative.

## What is already true

Sub-step 5's `CachedReadGoldenTests.EverySeekLandsTheSameThroughTheCache` records the **outcome** of
every string seek, exception type included, and requires the cached and uncached runs to agree. So the
current behaviour — right or wrong — is pinned against the cache changing it, without being blessed as
correct. When the refusal lands, that test's recorded outcomes change from `Ok`/`NoRecord`/
`ArgumentOutOfRangeException` to a refusal, and it will still be comparing like with like.

---

## The larger finding behind it, added 2026-08-21

Raised in review: *a function that answers `0.0` for everything it cannot parse means the unsuccessful
scenarios are not covered by testing at all.* That is the sharper version of this defect and it is
about the suite, not about one branch.

**Where the swallowing is.** `FoxNumeric.ToDouble` returns `0.0` when the text does not parse
(`Dbf/FoxNumeric.cs:37-44`).

> **Correction, 2026-08-21.** This note first said that making `ToDouble` refuse "would break behaviour
> the corpus proves". That is wrong, and checking made it worse rather than better: **the corpus
> witnesses nothing whatever about the fallback.** Every numeric value in it is a well-formed number
> with leading spaces — the nearest thing to a blank is `"       0.000"` in `VFPTYPE`, which is a
> *written* zero — and **there is no all-blank numeric field anywhere in the corpus**. So neither the
> junk case nor the blank case is witnessed, no test would notice the fallback changing in either
> direction, and it is a guess with nothing standing behind it. `FoxNumericGoldenTests` would not even
> catch a blank one if it existed: it skips values whose dump carries no number
> (`value.Number is null`).

The fix is still that the *seek* path must not route a caller's string through a function whose job is
reading a field's bytes — but the reason is that they are different kinds of input, not that the
fallback is proved.

**What is genuinely uncovered.**

- **There is no blank numeric field in the corpus at all**, which is a gap of its own and not an
  exotic one: blank is the state of every field of a record `d4blank` has just prepared, so it is the
  *default* state in xBase rather than an edge case. It belongs with the twelve corpus gaps
  `PORTING-PLAN.md` §6.3 already names.
- **`FoxNumeric` has no layer-1 tests at all.** There is no `FoxNumericTests.cs`; it is exercised only
  through the corpus, on values the generator wrote — which are all valid. `DEV_APPROACH.md` §4 says in
  as many words that layer 4 must not be the only thing standing, and here it is: *"The corpus is a
  witness to happy paths that happen to exist in it. It cannot prove behaviour on … an input no sane
  writer produces."*
- **The C library's own answer is unwitnessed.** `f4double` ends in `c4atod` (`F4DOUBLE.C:335-340`),
  which is *declared* in `d4declar.h:224` and **has no definition anywhere in this source drop**. So
  what the reference does with a numeric field holding `"ABC"` is not knowable from the sources we
  have, and no corpus case contains one. Until a generator case writes a malformed numeric field, this
  port's answer for it is a guess that happens to be plausible.
- **`RecordKey` and `TableTagCursor` are named by no layer-1 or layer-2 test.** They are reached only
  through `Table`, which is why a string seek at a numeric tag had never been tried until sub-step 5's
  equivalence test pointed one at every tag of every table.

**A sweep of the library for the same shape**, by which production types are named by no test below
layer 4: `FoxNumeric`, `RecordKey`, `SeekConverter`, `DateTimeKeyFlags`, `IndexEntry` are golden-only;
`CollationWeights`, `NodeReader`, `SourceReader`, `TableTagCursor`, `FieldCollection`, `TagCollection`,
`DbfVersion`, `FeatureFlags`, `LegacyVariant`, `VisualFoxProVariant` are named by none. Most of those
are covered transitively through their callers and that is legitimate — the approach tests contracts,
not classes. The ones that matter are the ones where a **failure path resolves to a plausible value**,
because those are invisible to a corpus-driven suite by construction: nothing in the corpus is
malformed, so nothing exercises them, and a wrong answer looks like an answer.

## What this asks for, beyond the refusal

1. **`Seek` requires the exact type**, refusing otherwise — decided in review. Conversion for callers
   who want it becomes an extension method later, outside the library's own contract.
2. **Layer-1 tests for `FoxNumeric`**, pinning what unparseable, over-long, over-precise and
   sign-only text answer — as a *decision recorded*, not as a discovery.
3. **A generator case with a blank numeric field and a malformed one**, so the C library's answer stops
   being unknown for either. That is the only way to settle whether `0.0` is faithful or merely
   convenient, and it is the same move the project already makes for every other format question. The
   blank one is the more urgent of the two, because blank is what an appended record starts as.
4. **A pass for the rest of the shape**: every place a parse or a lookup resolves to a default rather
   than refusing, checked for whether the default is witnessed or assumed.
