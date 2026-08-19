# 008-seek-by-value — state

**Live, per-step.** Where execution is, decisions taken during it, and what is blocking. The project
state is the root [`STATE.md`](../../../STATE.md).

**Updated:** 2026-08-14 · **1126 tests** (637 unit and component, 489 golden), all green. **Sub-steps 1
to 5 are done — the plan's stopping point.** `COLLATION` is closed, nothing is public, and every key
kind is gated against the C library's own keys.

## Done

**Sub-step 1 — the `COLL4ARR.C` tables.** `Cdx/CollationTables.cs`: `cp1252`, `cp437` and `cp850`
GENERAL weight tables at 256 entries each, plus the cp1252/cp437 and cp850 expansion tables. Extracted
mechanically from the C rather than transcribed, then verified three ways — against the raw C by eye,
against the values `KEY-COLLATION.md` §3.3 cites independently, and by 14 unit tests.

The extractor had to resolve **`S4ELICON`**, a Swedish-sort variant guarding four cp1252 entries
(196-198 among them). It is not defined in the `S4FOX` build, so every block takes its `#else` branch;
a test pins that, because taking the wrong branch would be silent and would misorder Scandinavian text
only.

The test worth keeping: **every head weight is 16 or above, across all 256 entries of all three
tables**. That is the property `D4SEEK.C:117,134` relies on to read values under ten as tail markers,
and it catches a shifted row anywhere — which spot checks cannot.

**Sub-step 2 — the arithmetic transforms.** `Cdx/KeyTransform.cs`: double, single, int32, int64,
uint32, currency, date, logical. Each writes into a caller-owned `Span<byte>` and returns the length,
so nothing allocates.

Tested by the **ordering property** rather than by written-down bytes: for an ascending list of values,
the keys must compare ascending as plain bytes. One property fails for every sign-handling mistake at
once and needs no expectation typed in. `-0.0` is asserted separately, because it is the one place the
reference is arithmetically wrong and this port must be wrong with it — its key wraps to all zeros and
sorts below every negative.

## Decided during execution

- **Currency range is checked before scaling, not after.** Multiplying by ten thousand overflows the
  `decimal` type itself long before the result would overflow a currency field, and that throws an
  exception this library does not own. Caught by the refusal test.
- **`FromDate` goes through `FromDouble`**, as the reference does, so a date's ordering rides on the
  double transform and there is one sign rule rather than two.

**Sub-step 2b — the datetime transform, and the corpus case that made it gateable.** The blocking
question below was answered by building `CDXTIME`: 256 datetimes over one `T` field, ascending and
descending tags, values chosen so 97 land on a set bit of the decrement bitmap and the rest do not,
alongside the day's edges and the calendar's. `DateTimeKeyFlags` is the 10802-byte table copied
verbatim; `KeyTransform.FromDateTime` is the rounding, the flag lookup and the one-byte borrow.

Every stored key of both tags is reproduced. Disabling the decrement fails both golden tests, so the
bitmap is genuinely exercised.

**Sub-step 3 — `t4convertSubSortCompressChar`.** `Cdx/CollatedKey.cs`, gated by `CDXCOLL`: all 32 keys
of the `GENERAL` tag rebuilt from field values, with the machine-collated tag beside it as a control so
a port that quietly stored raw text would fail. **This closes E14 and risk R2.** Not stripping trailing
blanks fails it, and so does an expansion that emits one head instead of two.

**Sub-step 4 — `considerPartialSeek`.** `CollatedKey.WriteSearch`. Reading `tfile4stok`
(D4SEEK.C:39-142) showed the rule is bigger than `KEY-COLLATION.md` §3.4 describes: suppressing the
tails is only half of it. The converted key is then **cut back to its head bytes**, which the reference
finds by scanning for the first byte below sixteen — relying on the very property sub-step 1 tests, that
no head weight is ever that low. What is left is a true byte prefix of every stored key starting with
the same characters, which is what makes a prefix seek work at all.

Two behaviours fall out and are tested as such: a trailing blank in the search changes nothing, and an
expanding character still lengthens the search by two rather than one.

**Sub-step 5 — the selection table and the record-to-key path.** `SeekConverter` resolves a tag's key
kind once from the table's fields, the way `tfile4initSeekConv` does (i4init.c:557-753), as data rather
than function pointers. `IKeyValueSource` is the seam `EXPR` plugs into, with `FieldValueSource` its
only implementation; `RecordKey` composes the two and is what sub-step 11 will use to make
`Synchronize` a descent.

**The gate is the whole corpus at once:** every key of every tag of every indexed case, rebuilt from
the value in the record it names — **3559 keys** across character, collated, numeric, double, date,
integer and datetime kinds, ascending and descending, filtered and unique. Counted per case and
asserted, so a run that quietly found fewer tags fails rather than passes.

**The code-page validation came early, because it had to.** A collated tag names a sort order, not a
table; which weight table GENERAL means depends on the table's code page, and the index file does not
record it. So `CollationWeights` had to exist before the first collated key could be built, and the
audit's §1.3 finding — nothing checks that the two agree — is closed here rather than in sub-step 10.
A mismatch is refused at tag resolution, naming both.

## Found while gating — two rules that cannot be, or are not, exercised

**The datetime one-byte borrow is provably unreachable, not merely untested.** The C borrows into the
second byte and stops (i4conv.c:2273-2279), which differs from a plain decrement only when the low two
bytes are both zero. A real Julian day sits between 2^21 and 2^22, where the double's step is 2^-31, so
the low sixteen mantissa bits vanish only when the second of the day is a multiple of 675 — 128 of the
86400 — and **none of those 128 carries the decrement flag**. Replacing the borrow with `bits - 1`
passes every test because the two are equivalent on every input the format can hold. The reference's
shape is kept and the equivalence is written into the code comment, so it does not read as a lurking
bug.

**The collated tail-count guard is reachable but not exercised.** `tail < length` (u4util.c:2331-2336)
stops an ordinary character contributing a tail once the tails have filled. Expansions add two tails
each and are **not** guarded, so ten expansion characters followed by a letter would trip it in a
`C(20)` field. `CDXCOLL`'s values are ordinary words with a few accents, so nothing does. Dropping the
guard passes every test today.

**Recommendation:** add one value like ten `œ` followed by ten letters to `CDXCOLL` and regenerate.
It is the cheapest way to gate a branch that silently drops weights, and the ripple is small — every
expectation comes from the regenerated dumps, and only the record count moves.

## Superseded — the scope call that is now answered

**The datetime transform needs a 10800-byte empirical table that nothing can gate.**
`t4dateTimeToFox` (`KEY-COLLATION.md` §2.8) is not arithmetic: it rounds to the nearest second, then
consults `flags4dateTimeFlags` — an 86400-bit bitmap indexed by second-of-day (i4conv.c:1513-2191) —
and decrements the double by one byte where the bit is set, to match a FoxPro conversion the C's own
authors did not decipher. The spec says it **must be copied verbatim**.

**No corpus tag indexes a `T` field.** Step 006 already named this: a tag over `Y`, `T`, `Z` or `F` is
handled by the resolver and exercised by no corpus case.

That makes datetime different in kind from the other unexercised types. Currency, float and logical are
also un-gated by the corpus, but they are three lines of arithmetic with an ordering property that can
be checked without a file. The datetime table can only be checked against real keys — porting 10800
bytes of empirical data with no way to know it is right is exactly the risk `CLAUDE.md` forbids taking
("if a path is untested, add a generator case and regenerate").

**Answered: ported, with a corpus case.** `CDXTIME` was built rather than the type refused, which is
the better trade — the table is 10802 bytes of data that only real keys can check, and now they do.
`PORTING-PLAN.md` §6.3 records the gap as closed.

## Next

**The step is at its planned stopping point.** Sub-steps 6 to 12 are the public surface: the cursor's
key buffer, `Seek` and `SeekPrefix`, `SeekAtOrAfter` and `SeekAtOrBefore`, `SeekNext`, and the
`Synchronize` rewrite. None of it is started.

Before it does start, the `CDXCOLL` tail-guard gap above is worth closing — it is the one branch in
what has landed that silently drops weights and that no test exercises.
