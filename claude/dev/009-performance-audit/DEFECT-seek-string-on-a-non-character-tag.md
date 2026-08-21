# Defect: `Seek(string)` on a tag whose keys are not character

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
