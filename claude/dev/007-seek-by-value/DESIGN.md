# 007-seek-by-value — design

Using an index while reading a table: **select an index, seek to a value (exactly or not), then step
forward and back — either through everything in the tag's order, or through just what matches.**

Phase 2 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written before any `.cs` file is opened.

## Goal

One sentence: **a caller can ask a table where a value is, instead of walking until they find it.**

That is the last piece before the library does something a plain DBF reader cannot, and it is the
operation the optimizer is built out of — a per-tag constraint is a seek to one end of a range and a
walk to the other.

**Deliberately not a range or query object.** A cursor, in the shape the rest of `Table` already has:
positioning calls that move one cursor and report what they found. An `IEnumerable` wrapper for LINQ
is a later and separate decision, and is easy to add over a cursor; it is hard to remove once the
cursor is hidden behind it.

## What already exists

Most of the machinery landed in 005 and 006. Naming this precisely is what keeps 007 small.

| Capability | Where | Status |
|---|---|---|
| Select an index | `Table.SelectTag(Tag?)` | **done**, 006 |
| First / last in tag order | `Table.Top()` / `Table.Bottom()` with a tag selected | **done**, 006 |
| Next / previous in tag order | `Table.Skip(±1)`, and `GoNextIndexed` / `GoPreviousIndexed` | **done**, 006 |
| Seek over **key bytes** | `TagCursor.Seek`, `SeekAtOrBefore`, `SeekLast`, `SeekNext`, `SeekPrevious`, `SeekExact` | **done**, 005 — but `internal` |
| Seek over a **value** | — | **this step** |

So 007 is **not** a new navigation model. It is the missing half of one: turning a typed value into
the key bytes `TagCursor` already searches by, and exposing the seek family on `Table`.

## The five things this step actually builds

### 1. Value to key bytes — the `stok` / `dtok` transforms

`KEY-COLLATION.md` §1 has the C library's own selection table (`tfile4initSeekConv`, i4init.c:557-753):
each tag picks a string converter and a double converter from its key type. That table is the design.

The machine-collation half is small and entirely arithmetic:

- **`t4dblToFox`** (§2.1) for `N`, `F`, `D`, `T` and `B` — reverse to big-endian, then add `0x80` to
  byte 0 with **8-bit wraparound** when the value is `>= 0`, else complement all eight bytes.
  **`-0.0` takes the positive path** and wraps to all zeros, sorting below everything. The
  `bits | 0x8000…` shortcut is not bit-exact and must not be used (CLAUDE.md gotcha list).
- **`t4floatToFox`** (§2.2) — the same on four bytes, for `H`.
- **`t4strToInt`, `t4strToLongLong`, `t4dblToCurFox`** — two's-complement, flip the sign bit only.
- **`t4noChangeStr`** — machine-collated strings: the bytes as they are, padded with the tag's pad byte.

**Entity, pure, exhaustively unit-testable**, and gateable from the corpus we already have: every tag's
stored keys sit beside the field values they were computed from, so *value in, stored key out* is a
golden assertion with no new generator case.

### 2. `GENERAL` collation — in scope

The `COLL4ARR.C` translate and compress tables, **ported verbatim as static byte arrays** — not derived,
and never via .NET `CompareInfo`, which cannot reproduce the stored bytes (CLAUDE.md gotcha list,
`KEY-COLLATION.md` §3.3). Then `t4convertSubSortCompressChar` (§3.4): output is always `2 x lenIn`,
built as *head* weights followed by *tail* weights, zero-padded, with trailing blanks stripped first
and expansions (`headChar == 0xFF`) contributing two heads each.

This closes the audit's #1 finding (E14) and risk R2, and `CDXCOLL` gates it — `keyLen` 20 and 40,
accents, and the `oe`/`ss`/`th` expansions, with every stored key already beside the value it came from.

**`considerPartialSeek` stops being a deferred question and becomes a requirement.** §3.4 makes the
tails conditional: on a partial seek where `maxKeyLen > verifyLen + hasNull`, the tails are **omitted**,
because secondary weights from a truncated value would otherwise interfere with the prefix match
(u4util.c:2256-2264). A `GENERAL` prefix seek is wrong without it, so it is part of this step rather
than a later refinement.

### 3. The public seek surface — one method per behaviour

**No behaviour is implied by an argument's shape or hidden behind a status code.** Two axes could have
been left implicit, and both get their own method instead.

```csharp
// exact -- the value is there, or the cursor is on no record
public GoResult   Seek(string value);              // Ok | NoRecord
public GoResult   SeekPrefix(string value);        // Ok | NoRecord -- matches SMITH, SMITHSON, ...

// positioning -- lands on a neighbour by design, and says which
public SeekResult SeekAtOrAfter(string value);     // Found | After  | Eof
public SeekResult SeekAtOrBefore(string value);    // Found | Before | Bof

// continue whichever search is active
public GoResult   SeekNext();                      // Ok | NoRecord
public GoResult   SeekPrevious();
```

Each of the four value-taking methods carries the same overload set: `string`, `double`, `int`,
`decimal`, `DateOnly`, `DateTime`, `bool`, and `ReadOnlySpan<byte>` as a raw escape hatch. The tag's key
type picks the transform; a value whose type the tag cannot take is refused by name rather than
converted, because a silent conversion is a wrong record set.

```csharp
public enum SeekResult { Found, After, Before, Eof, Bof }
```

**Axis one: does a miss reposition?** `Seek` says only whether the value was there. `SeekAtOrAfter`
exists to land on a neighbour and reports which side it landed on. Rolling both into one method that
returns `After` makes every caller who wanted an exact answer read a status code to discover the cursor
moved somewhere they did not ask for.

**Axis two: whole key or prefix?** `CDX-FORMAT.md` §7, established by 005 against the corpus: a search
value carrying trailing pad stands for a whole key, one without it is a prefix. That is the reference's
rule and it stays reachable — but it must not be *implicit*, because it makes `Seek("SMITH")` and
`Seek("SMITH               ")` mean opposite things on the same tag with the same type, decided by
invisible whitespace. `Seek` pads to the key length and matches whole keys; `SeekPrefix` matches on the
value's own length. A caller's trimming habits stop being load-bearing.

`SeekPrefix` also isolates **`considerPartialSeek`** — `GENERAL`'s tail-weight suppression
(`KEY-COLLATION.md` §3.4) — to exactly one method, instead of it depending on a runtime property of the
caller's string.

**Two consequences accepted deliberately:**

- **A failed exact `Seek` leaves the cursor on no record**, with the record blanked — the same shape
  `Go(n)` already has past the end of the table. That is why it returns the existing `GoResult`: the
  answer is genuinely binary, and a new enum would carry members that cannot occur. *Rejected:*
  restoring the previous position on a miss, which is friendlier but needs both cursors saved and
  restored and adds a third outcome to document.
- **`SeekAtOrBefore` becomes public**, having been internal since 005. Once `SeekAtOrAfter` is a named
  method its mirror cannot be the one you have to reach through the optimizer for, and the pair is what
  gives a range without a range object:

```csharp
table.SelectTag(byName);
table.SeekAtOrAfter("A");
while (!table.Eof && string.CompareOrdinal(table.GetString(name), "M") <= 0)
{
    ...
    table.Skip(1);
}
```

**`SeekExact` stays internal.** Its second argument is a record number, which disambiguates a duplicate
run for the optimizer and for `Synchronize` (deliverable 5). A reading caller has no use for it.

### 4. Collation must match the code page

The audit's §1.3 finding, which belongs here and nowhere else: **nothing checks that a tag's collation
name agrees with the table's code page.** A `GENERAL` tag on a cp850 table is read today with the
cp1252 weight table. Harmless while only stored keys are read — a key is bytes — and wrong the moment a
value is converted into one, which is exactly what this step adds. Validate at tag resolution and refuse
the mismatch.

### 5. `Synchronize` stops being O(n) — and it does not need `EXPR`

`TableTagCursor.Synchronize` is the only O(n) scaling path left in the navigation surface. After
`Go(n)`, the tag cursor has no idea where record `n` sits, so today it walks the whole tag comparing
**record numbers** — the one thing a leaf entry carries besides its key, and the one the tree is *not*
ordered by. Correct, and O(n) with a block read per leaf crossing (P1 and P3 compound here).

The C library instead re-derives the record's key and seeks it (`d4seekSynchToCurrentPos`,
`D4SEEK.C:1141`), which is why every note so far — the audit's P3, `STATE.md`, this folder's own
triage — says the fix "needs `EXPR`".

**That is wrong for the tags this port can select.** ADR-28 already restricts a selectable tag to one
whose key expression is a **bare field name**; anything else is refused. So "evaluate the expression"
means *read that field out of the record we already loaded*, and no expression engine is involved. All
three pieces exist once this step lands:

1. The record is in the buffer — `Fetch(n)` put it there.
2. Value to key bytes — **this step builds it**.
3. `TagCursor.SeekExact(search, record)` — **005 already built it**, and its record argument exists
   precisely to pick one entry out of a duplicate run.

So `Synchronize` becomes: read the field, build the key, `SeekExact`. **O(log n)**, and the ADR-30
refusal gets cheaper with it — "this record is not in the tag" costs a failed descent instead of a full
walk.

**Two things to get right rather than assume:**

- **The build transform, not the seek transform.** `KEY-COLLATION.md` §1 lists these as two separate C
  paths: keys are *built* through `expr4keyConvert` and *sought* through `stok`/`dtok`. They agree for
  machine collation; for `GENERAL` they deliberately differ, because a partial seek suppresses the tail
  weights (§3.4). `Synchronize` needs the **build** form — it is reconstructing a key that already
  exists on disk, not searching for a prefix.
- **A record with no entry must still refuse.** A null field, or a record the tag's filter excludes, has
  no entry at all. `SeekExact` must miss, and ADR-30's refusal must stand rather than landing on a
  neighbour.

**Built as the real seam now, not a bare-field shape to be widened later.** Deriving a record's key is
two steps, and only the first is expression-dependent:

```
record + tag  --[IKeyValueSource]-->  a typed value  --[KeyTransform]-->  key bytes
```

`IKeyValueSource` is the seam. One implementation exists in 007 — `FieldValueSource`, which reads the
bare field the tag names — and step 008 adds `ExpressionValueSource` beside it without touching
`RecordKey`, `KeyTransform`, `Synchronize`, or anything above them. The interface is narrow enough to
fake in three lines, which is what `DEV_APPROACH.md` §3.2 asks of a boundary.

**An expression-based tag throws `NotSupported`, at one place, by name.** This is ADR-28's existing
refusal, and 007 makes it load-bearing rather than merely tidy: because every *selectable* tag is a bare
field name, key derivation is **total** — it cannot fail for structural reasons, only because a record
genuinely has no entry (a null, or a filtered-out record), which is ADR-30's case and stays a refusal.
That is what lets `Synchronize` drop its O(n) fallback entirely instead of keeping a walk "just in
case": there is no case.


## Classes

| Class | Role | Responsibility |
|---|---|---|
| `KeyTransform` (new) | Entity | One value, one key type, one pad byte, out come key bytes. Pure, no I/O, no tag |
| `SeekConverter` (new) | Entity | The `tfile4initSeekConv` selection: which transform a tag's key type uses. A lookup, resolved once per tag |
| `TagCursor` | Controller | Unchanged. It already seeks by key bytes |
| `TableTagCursor` | Controller | Gains the seek family, translating value to bytes then delegating |
| `Table` | Controller | Gains `Seek`, `SeekNext`, `SeekPrevious`, and the typed overloads |
| `RecordKey` (new) | Entity | A record plus a tag, out come the key bytes that tag stored for it. Composes a value source with a transform; knows nothing about where the value came from |
| `IKeyValueSource` (new) | Boundary | Record plus tag, out comes a typed value. `FieldValueSource` in 007; `ExpressionValueSource` in 008. The only place `EXPR` will touch this step's work |

**No new boundary.** Nothing here touches a file that is not already open.

## Decisions this design makes

**The cursor owns one key buffer, keeps the active search in it, and `SeekNext()` takes no argument.**

These are one decision, not two, and getting that wrong is what made the audit's P5 look like a standing
performance finding. Either a search is copied *and kept*, in which case the copy is the stored search
and not waste; or it is not kept, in which case the copy has no justification and the value must be
supplied again. There is no coherent third position where we copy, discard, and still re-take the value.

**What the C library does** settles the shape rather than the choice. `d4seekNextN` does take the value
again — but it converts it into `c4->fieldBuffer`, a **reusable buffer hanging off `CODE4`**, grown by
`u4allocAgain` only when a longer key needs it (`D4SEEK.C:1130-1137`). So the reference is not
allocating per seek either; it simply parks the buffer on the engine instead of on the cursor.

**This port parks it on the cursor**, which gives both properties at once:

- `TableTagCursor` owns a `byte[]` sized to the tag's key length — doubled for `GENERAL`, whose output
  is always `2 x lenIn`.
- The transforms **write into a `Span<byte>` destination and never return an array**, so converting a
  value produces no intermediate garbage.
- `KeySearch` gains a form that wraps a buffer it does not own (`KeySearch.Into(buffer, …)`) beside the
  existing owning `For(…)`. The cursor holds the buffer and the search together, so their lifetimes are
  the same object's and the borrow cannot dangle.
- `Seek` refills that buffer. `SeekNext()`/`SeekPrevious()` reuse it. A second `Seek` with a different
  value overwrites it.

The result is **zero steady-state allocation on the seek path** — better than the reference, which
reallocates when a longer key arrives, and better than either half of the audit's framing. A thousand
seeks on one cursor allocate one buffer.

**So P5 is resolved here, by design, not deferred to the measurement step.** It was never a micro-cost
to be measured; it was a structural consequence of not having decided who owns the search. The
optimizer will drive exactly this path, so designing it in now costs nothing and retrofitting it later
would mean changing `KeySearch`'s contract after callers exist.

Any positioning call that is not a seek clears the active search, so a stale one cannot silently outlive
its context.

**`SeekExact` stays internal.** Its second argument is a record number, which is a detail of how the
optimizer disambiguates a duplicate run, not something a reading caller has any use for.

**Typed overloads, not `object`.** `Seek(string)`, `Seek(double)`, `Seek(DateOnly)`, `Seek(decimal)`,
`Seek(int)`, `Seek(bool)`. The tag's key type decides which transform runs, and a value whose type the
tag cannot take is refused by name rather than silently converted — a wrong conversion is a wrong record
set, which is the failure this project ranks worst.

## Settled while designing

**Naming — no third scheme.** The capability list was *select / seek / next / nextMatching / previous /
previousMatching / first / last*. Four of those already exist: first and last are `Top` and `Bottom`
(the reference's own `d4top` / `d4bottom`), next and previous are `Skip(1)` and `Skip(-1)` plus the
`…Indexed` pair. Only the seek family is new, so only it gets new names. Adding `First`, `Last`, `Next`
and `Previous` beside the existing ones would make ADR-29's two navigation surfaces into three, for
ergonomics no caller has asked for. If cursor-style names are ever wanted as the *primary* surface, that
supersedes ADR-29 rather than sitting beside it.

**`GENERAL` collation is in 007.** The `COLL4ARR.C` tables are ported verbatim and `CDXCOLL` gates them.
A seek that worked on numeric tags but refused the string tags people actually index by name would not
be "using indexes while reading the table" in any useful sense.

**`Seek` has no per-call form.** There is no `Seek(tag, value)` to match the `…Indexed` four, because
the search is stateful: `Seek(tagA, x)` followed by a bare `SeekNext()` has no defensible answer.
Selecting the tag first keeps the search's owner unambiguous, and a seek with no tag selected is an
error rather than a record-order fallback — the first call in the class whose null branch is not a
fallback.

**`SeekNext` keeps the reference's name** rather than becoming `NextMatch`. It reads slightly wrong —
it continues a search rather than seeking a next thing — but it is `d4seekNext`, and the correspondence
is worth more than the sentence.
