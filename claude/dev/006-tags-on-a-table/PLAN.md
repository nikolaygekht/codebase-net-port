# 006-tags-on-a-table — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | `KeyTypeResolver`: every field type to its key type and pad byte, a bare name matched case-insensitively and with trailing blanks, a name that matches no field, a composite expression, and a collated tag needing no field at all | Every row of the type table, including the types no corpus tag indexes — `Y`, `T`, `Z`, `F` — and the refusals, which is where a wrong pad byte would otherwise get in |
| 2 Component | `Table` over `TableImage` with an index: tag order over two blocks, `SelectTag` not moving the cursor, `Skip(0)`, both ends, back to record order, an entry naming a record past the end of the table, a filtered tag reaching fewer records — and every one of those again through the explicit `GoFirstIndexed`/`GoNextIndexed` form, which must agree with the mode-based one entry for entry | The coupling between the tag cursor and the record cursor — the part that is *not* in either subsystem and therefore not covered by 004's tests or 002's |
| 3 Fault injection | A header declaring an index with no file beside it; an index whose tag expression names no field; `SelectTag` with a tag from another table; navigating with a tag selected on a table whose index was refused | That a table with an unreadable tag stays usable through its other tags, and that a missing declared index is an error at open rather than a surprise later |
| 4 Golden / corpus | For every tag of every corpus index: the record sequence a tag-order walk visits, against the tag's `[keys]`; and every field of every record visited, against the table's record dump | Whether navigating by index delivers the same records reading by number does. A tag-order walk that returned the right record *numbers* but read the wrong bytes would pass any index-only test |

**Corpus coverage.** 18 tags over four tables, so 3364 record positions visited by index, each checked
against the key sequence *and* against the record's own dumped field values. The seven older tables
cover the other half of the promise: no production-index bit, so `HasIndex` is false and `Tags` is
empty, and record-order navigation is unchanged — which is the regression risk this step carries.

Touched but **uncovered by any corpus case**, to be listed as ungated in `SUMMARY.md`:

- **An index entry naming a record past the end of the table** (Decision 4). Every generated index is
  consistent with its table, so this is a component test over a hand-built pair.
- **A composite key expression** such as `UPPER(NAME)`, refused until `EXPR`. No corpus tag has one, and
  adding a case would only gate the refusal, not the reading.
- **A tag over `Y`, `T`, `Z` or `F`** — the resolver handles them, the corpus indexes none of them. Unit
  tests only, and a candidate for a cheap `CDXBASE` extension later.
- **A table with an index whose tags are all refused**, which needs a composite expression to arise.
- **Two index files open on one table**, which is out of scope until something opens a second.

**Expected values.** Record sequences come from the `[keys]` sections of the index dumps and field
values from the table dumps, both written by the C library. The two are joined by record number, which
is the only thing this step's gate has to compute for itself.

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **`KeyTypeResolver`** — the field type to pad byte table, and its refusals | Layer 1, the whole table: *`C` and `Z` pad with a space under machine collation and with NUL under any other*; *the numeric family always pads with NUL*; *a bare name matches case-insensitively and ignores trailing blanks*; *an unknown name and a composite expression are both refused, naming the expression*; *a collated tag is resolved without consulting the fields at all* |
| 2 | **Opening the production index with the table** | Layer 2: *a table whose byte 28 declares an index opens it and reports its tags*; *one that does not has `HasIndex` false and no tags* (Q1). Layer 3: *a declared index with no file is an error at open, naming the file*; *the resolver finds `NAME.cdx` beside `NAME.DBF` on a case-sensitive filesystem*. **Golden:** *the four indexed tables report exactly the tag names their dumps list, and the seven older ones report none* |
| 3 | **`Tag` and `TagCollection`** | Layer 2: *by name, case-insensitively, and by position*; *a name that is not there throws and names what is*; *`Tag` reports the key length, direction, uniqueness, filter presence, expression and filter text*. **Golden:** *every tag's reported properties match its dump's header line* — which is 18 tags of header fields already asserted internally in 004, now through the public surface |
| 4 | **`SelectTag` and tag-order `Top`/`Bottom`** | Layer 2: *selecting does not move the cursor* (Decision 3); *`Top` then goes to the tag's first record and `Bottom` to its last*; *selecting null returns to record order*; *`Go` ignores the selection*. Layer 3: *a tag from another table is refused*; *a tag whose expression cannot be resolved is refused at selection, and the table's other tags still work* |
| 5 | **The explicit `…Indexed` four** (Decisions 9 to 11) | Layer 2: *`GoFirstIndexed(tag)` and `GoLastIndexed(tag)` reach the tag's ends without touching the selection*; *`GoNextIndexed` steps unconditionally and stops only at the end of the tag* (Decision 10); *a walk through the explicit form visits exactly what the mode-based walk visits*; *the selection is unchanged afterwards, so a mode-based `Skip` still means what it did*. Layer 3: *a tag whose expression cannot be typed is refused here too* (Decision 11) |
| 6 | **Tag-order `Skip`, and the two ends** | Layer 2: *a forward walk visits every record of the tag once*; *a backward walk from `Bottom` reverses it*; *stepping off either end sets the flags the record path already models, and the far end sets both* (Decision 5); *`Skip(0)` stays* (Q2); *an entry past the end of the table is skipped in the direction of travel* (Decision 4) |
| 7 | **The gate**: golden tag-order navigation over the corpus | The gate below. Run over **both** surfaces, since they share an implementation but not their entry points. **Mutation checks:** navigating in record order while a tag is selected (must fail every tag but leave the seven older tables green); ignoring the descending flag (must fail exactly `T_TEXTD`); reading the record *before* moving the tag cursor (an off-by-one that a key-sequence-only test would still catch, which is the point of also asserting field values) |
| 8 | **Documents** | `PORTING-PLAN.md` §5 marks `CDX-READ` **done** and records what still waits on `EXPR`; `README.md` gains the tag-order example, since this is the first index feature a user can see; ADR-28 lands; `SUMMARY.md`, `claude/dev/README.md` and the root `STATE.md` say what shipped |

## Gate

```
dotnet test net/CodeBase.Net.sln
```

green, with a golden suite asserting, **for every tag of every corpus index file**:

- the record numbers a walk from `Top` to the end visits, in order, equal the tag's `[keys]` sequence —
  **and the same walk through `GoFirstIndexed`/`GoNextIndexed` visits the same sequence**, so neither
  surface can drift from the other;
- the same walk backwards from `Bottom` gives the reverse;
- **every field of every record visited matches that record's entry in the table's own dump** — so the
  index delivers records, not just numbers;

and, for the seven tables with no index, that `HasIndex` is false, `Tags` is empty, and record-order
navigation is byte-for-byte what it was before this step. Counted arithmetically: positions visited must
equal the keys the dumps hold, and be non-zero.

## Risks

| Risk | Why it would make this step *wrong* | Cheapest early exposure |
|---|---|---|
| **A wrong pad byte for a tag** | Every padded key in that tag is wrong, which in *this* step shows up as nothing at all — navigation still works, because it follows the leaf chain rather than comparing keys. It would surface in 007 as a seek that never finds anything | Sub-step 1's exhaustive resolver table, and the golden check that each tag's resolved pad byte equals the `pChar` its dump records — the same value 004's gate used, now derived instead of supplied |
| **The off-by-one between moving the tag and reading the record** | The walk visits the right *set* of records in the right order but reads each one's neighbour | Asserting field values as well as record numbers, which is why the gate does both; and the named mutation in sub-step 6 |
| **Record-order navigation regresses** | 900 existing tests cover it, so this is the one risk the suite already guards — provided the tag path is a branch and not a rewrite | The seven un-indexed tables stay in every golden suite, and the mutation in sub-step 6 must leave them green |
| **A missing index file becomes a silent record-order open** | The table answers, in a different order, and nothing says why | Layer 3 in sub-step 2, which is the same shape as step 001's missing-memo test |
| **`SelectTag` moving the cursor** | Plausible-looking and wrong; a caller that selects a tag mid-walk would jump | The named test in sub-step 4 (Decision 3) |
| **The public surface arrives wider than it can be gated** | `Tag.CurrentKey`, `Tag.KeyCount`, a second index file — each is an API with no caller and no test (ADR-22) | The design lists what is deliberately not exposed; sub-step 8 checks the shipped surface against that list |
| **The two surfaces drift apart** | Sharing an implementation is the plan, but a fix applied to one entry point and not the other would leave the mode-based and explicit walks disagreeing — and each would look right in its own tests | The gate runs the whole corpus walk through both, and sub-step 5 asserts they visit the same entries on the hand-built cases |
