# 007-seek-by-value — summary

**Closed 2026-08-14.** A caller can now ask a table where a value is instead of walking until they
find it. **1174 tests** (648 unit and component, 526 golden), up from 1054.

This is the step the library exists for. Byte-compatibility was the foundation; a seek that lands in
one descent is the first thing this port does that a plain DBF reader cannot, and it is the operation
the query optimizer is built out of.

## What shipped

```csharp
table.SelectTag(table.Tags["NAME"]);

table.Seek("SMITH");            // exact, whole key -- Ok or NoRecord
table.SeekPrefix("SMITH");      // matches SMITH, SMITHSON, SMITHERS
table.SeekAtOrAfter("S");       // Found | After | Eof -- lands on a neighbour by design
table.SeekAtOrBefore("S");      // Found | Before | Bof
while (table.SeekNext() == GoResult.Ok) { ... }   // the rest of the matches
```

Underneath: the `COLL4ARR.C` weight tables for cp1252, cp437 and cp850 copied verbatim; every numeric
transform; the `GENERAL` head-and-tail key; the `flags4dateTime` bitmap; the partial-seek rules; the
selection table that picks between them; and `Synchronize` rewritten from a walk into a descent.

**`COLLATION` is done and risk R2 is retired** — one of the two highest silent-corruption risks in the
port, alongside `CDX-READ`'s bit-packed leaves. Both are now closed.

## The gates

| Gate | What it proves |
|---|---|
| **3559 keys** rebuilt from the values that produced them, across every tag of every indexed case | The transforms reproduce the C library, for every key kind, ascending and descending |
| **Every value of every character tag sought and found**, including three-level trees and collated tags | The whole path works end to end, not just the arithmetic |
| **Every position of eight tags**, `Go(n)` then `Skip(±1)`, against the dump's own key order | The `Synchronize` rewrite changed speed and not answers |
| The seek contracts, at component level | A miss positions on nothing; the two seeks are genuinely different; a search does not outlive its position |

Mutation-checked: removing the datetime decrement, dropping `GENERAL`'s tails, emitting one head for
an expansion, and seeking without the record number in `Synchronize` all fail exactly their own tests.

## A corpus case the plan did not have

**`CDXTIME`** — 256 datetimes over one `T` field, ascending and descending. The datetime key is the
only one that is not arithmetic: an empirical 86400-bit table decides whether the computed double is
nudged down, and the C's own comment says FoxPro's conversion "could not be deciphered". 10802 bytes of
data are worth nothing unless real keys can check the copy, so the values are chosen with 97 of 256
landing on a set bit, alongside the day's edges, leap and non-leap Februaries, and a blank datetime.

`CDXCOLL` also gained two values with ten ligatures each, to reach the collated tail-count guard.

## Where the design was wrong, and where the spec was incomplete

**`considerPartialSeek` is bigger than `KEY-COLLATION.md` §3.4 says.** Suppressing the tail weights is
half of it. Reading `tfile4stok` (D4SEEK.C:39-142) showed the converted key is then **cut back to its
head bytes**, found by scanning for the first byte below sixteen. That works because no head weight is
ever that low — the property sub-step 1 tests across all 768 table entries. The two sub-steps turned
out to be the same fact from opposite ends.

**The code-page validation arrived five sub-steps early**, and had to. `GENERAL` names a sort order,
not a table; the weight table cannot be chosen without the table's code page, and the index file does
not record it. So the audit's §1.3 finding closed as a consequence of writing the code rather than as a
separate task.

**`Synchronize` never needed `EXPR`.** Every note said it did — the audit, `STATE.md`, this folder's
own triage. But ADR-28 restricts a selectable tag to a bare field name, so deriving the record's key
means reading a field. With this step's transforms plus 005's `SeekExact`, it is a descent.

## Two rules that cannot be tested, proved rather than assumed

Both are C branches this port reproduces faithfully and no mutation can make fail. Recorded so they do
not read as gaps, and so nobody "fixes" them later.

- **The datetime one-byte borrow.** It differs from a plain decrement only when the low two mantissa
  bytes are zero. A real Julian day sits between 2^21 and 2^22, where the double's step is 2^-31, so
  those bytes vanish only when the second of the day is a multiple of 675 — 128 of the 86400 — and
  **none of those 128 carries the decrement flag**.
- **The collated tail-count guard.** It bites only once the tails outnumber the field width, and by
  then the copy into the space the heads left is already clipping harder. Checked exhaustively over
  every width and mix of expanding characters: **zero** combinations where it changes the output.

## Process failure worth recording

**I destroyed the uncommitted seek surface with `git checkout -- Table.cs`** while restoring a mutation
check — the exact mistake `DEV_APPROACH.md` §4 warns against, in a rule added *after step 006 lost
`Table.cs` the same way*. It was reconstructed and the suite went green before anything else happened.

The rule was followed correctly for `TableTagCursor.cs` and `CollatedKey.cs` in the same session, with
copies aside and `md5sum` verification. It failed on `Table.cs` because that mutation was in a
*different* file from the one being restored, and the habit did not transfer. **The rule should say:
restore by checksum from a copy, never by `git`, regardless of which file the mutation touched.**

## Not in this step

- **`EXPR`** — step 008. `IKeyValueSource` is the seam it plugs into; `FieldValueSource` is its only
  implementation today and `ExpressionValueSource` sits beside it without anything above moving.
- **The performance pass** — after 008, and smaller than it was: P3 and P5 were resolved here by
  design rather than measurement.
- **Currency, float and logical value-seeks.** The transforms exist and are property-tested; no corpus
  tag indexes such a field, so the public overloads are not offered for them.
