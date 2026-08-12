# 006-tags-on-a-table — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

Give the index a table to belong to: open the production index with the table, expose its tags, let a
caller select one, and navigate **records** in that tag's order. This is the first public index API,
and the step where a tag entry stops being a number and becomes a record.

**Gate:** for every tag of every corpus index, driving the table in tag order from top to end visits
exactly the record sequence the tag's `[keys]` section records — and every field of every record
visited still matches that table's own record dump. The second half is what makes it a *table* test:
navigating by index must deliver the same records reading by number does.

**Capability:** `CDX-READ` — the last of it, and the first public surface  ·  **Governing spec(s):**
`specs/CDX-FORMAT.md` §2, `specs/DBF-FORMAT.md` §2.3, `specs/API-ERRORS.md` §4

## Not in this step

| Deferred | To | Why |
|---|---|---|
| **Seeking by a value** — `Table.Seek("SMITH")` | **007** | Needs the value-to-key transforms, which are `COLLATION`. Seeking by key bytes exists after 005 and is internal; the public one arrives with the transforms that make it usable |
| **Opening a non-production index** — `Table.OpenIndex("other.cdx")` | when something needs it | The production index is what a table declares and what every corpus case has. A second entry point with no caller is API ahead of demand (ADR-22) |
| **Multiple index files at once** | with the above | The C library keeps a list per table; nothing here needs a second element in it |
| **Writing keys on append or update** | `WRITE` | A selected tag makes the *read* order; maintaining it is the write path |
| **The optimizer's use of tags** | `QUERY` | It wants several cursors over one tag and no table cursor at all, which is why `TagCursor` was kept separate from `CdxTag` in 004 |

---

## The pad byte, which this step is what actually settles

ADR-26 says a machine-collated tag has to be *told* which byte its trail counts stand for, because the
file records the key expression's text and not its type. It also says `EXPR` is what will answer that.

**For the common shape, this step answers it instead, and exactly rather than heuristically.** Once a
tag belongs to a table, the table's field descriptors are right there: if the tag's expression is,
after trimming, the name of one of its fields, then that field's type *is* the key's type, and the pad
byte follows from the C library's own mapping (i4init.c:557-604):

| Field type | Key type | Pad byte |
|---|---|---|
| `C`, and `Z` (binary character) | character | `' '` under machine collation, `'\0'` under any other |
| `N`, `F`, `B`, `Y`, `I`, `D`, `T` | numeric, currency, date, datetime | `'\0'` |
| anything else, or an expression that is not a bare field name | — | **refused**, naming the expression |

That covers all 17 corpus tags, and — going by the 56 tags of the shipped `original/examples/DATA/`
samples — the overwhelming majority of real ones. A composite expression such as `UPPER(NAME)` or
`STR(ID)+CITY` still needs `EXPR`, and until then such a tag is refused **at selection rather than at
open**, so one unreadable tag does not make a table unopenable. ADR-28 records this.

*Rejected — guessing from `keyLen`.* Eight bytes means numeric except when a character field is eight
wide, and a wrong pad byte silently corrupts the padded tail of every key in the tag.

*Rejected — deferring the whole step until `EXPR` exists.* It would leave the port unable to navigate
by index for the sake of tags no corpus case holds, and `EXPR` is a larger subsystem than this one.

---

## What the C library does with a selected tag

Worth stating because two behaviours are not guessable and both are tested here.

**Navigation goes through the tag and then to the record.** `d4top` with a tag selected calls
`tfile4top` and then `d4go` on the record the entry names (D4TOP.C:186-208); `d4skip` calls
`tfile4dskip` and then `d4go` (d4skip.c:1277-1310). So record order is tag order, and the record buffer
is filled by an ordinary read.

**An entry naming a record the table does not have is skipped, not refused.** Both paths loop:
`while (recno > recCount) skip one more in the direction of travel`, and give up as end of file
(d4skip.c:1296-1308; D4TOP.C:205-215). The comment says why — another process may have added a key
before the record — and a reader that refused instead would fail on a file the C library reads.

**Hitting the end of a tag sets *both* flags.** `d4skip`'s tag path sets `bofFlag = 1` and then calls
`d4goEof` (d4skip.c:1281-1285). That is the same shape step 002 found for an empty table, and the port
already models beginning and end as two independent flags (`RecordPosition`), so it costs nothing to
reproduce.

**The production index is `<table>.cdx`, lower case, and the header bit says whether to look.** DBF
byte 28 bit `0x01` is set when a production index was created (`i4create.c:1404-1418`), and the C
library opens the file named by `INDEX4EXT` = `"cdx"` (d4defs.h:2578, 2609). Every corpus index case
carries the bit; the seven older tables do not.

---

## Classes

| Class | Role | Responsibility |
|---|---|---|
| `Table` (extended) | Controller | Gains the tag surface and tag-order navigation. Already the façade; no new public entry point |
| `Tag` | Boundary (public) | What a caller sees of a tag: its name, its key length, whether it is descending, unique, filtered, its expression and filter text. Read-only, and holds no position |
| `TagCollection` | Boundary (public) | The table's tags, by name and by index, mirroring `FieldCollection` so the two feel the same |
| `KeyTypeResolver` | Entity | The table above: an expression and a field table in, a key type and a pad byte out, or a refusal. Pure, so every row of it is a unit test |
| `TableTagCursor` | Controller | Couples a `TagCursor` to the table's `RecordPosition`: moves the tag, skips entries pointing past the end, reads the record, keeps the two ends' flags right. The one implementation behind both the mode-based navigation and the explicit `…Indexed` methods (Decision 9) |
| `DbfOpener` (extended) | Controller | Opens the production index when the header declares one, and hands it to the table with the memo file |
| `OpenedTable` (extended) | Entity | Carries the open `IndexFileReader` alongside the data and memo sources |

`Tag` and `TagCollection` are the only new public types; the four `…Indexed` methods are new members on
`Table`. `SeekResult` from `PORTING-PLAN.md` §4.2 stays unused until 007.

## Public surface

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");   // opens CUSTOMER.cdx if the header says so

table.HasIndex;                       // bool — the header's production-index bit, and a file behind it
table.Tags.Count;                     // int
table.Tags["NAME"];                   // Tag — by name, case-insensitively
foreach (Tag tag in table.Tags) { }

table.SelectTag(table.Tags["NAME"]);  // navigation is now in that tag's order
table.SelectedTag;                    // Tag? — null means record order
table.SelectTag(null);                // back to record order

for (GoResult go = table.Top(); go == GoResult.Ok; )
{
    string name = table.GetString(table.Fields["NAME"]);
    if (table.Skip(1) != SkipResult.Moved) break;
}
```

`Top`, `Bottom` and `Skip` keep their signatures and change meaning with the selected tag, exactly as
the C library's do. `Go(recordNumber)` does **not** — a record number is a record number whatever tag
is selected — and that asymmetry is worth a test of its own.

**And the same traversal is available without the mode**, for callers who would rather say what they
mean at the call site than depend on a selection made earlier:

```csharp
Tag byName = table.Tags["NAME"];

for (GoResult go = table.GoFirstIndexed(byName); go == GoResult.Ok; )
{
    string name = table.GetString(table.Fields["NAME"]);
    if (table.GoNextIndexed(byName) != GoResult.Ok) break;
}

table.GoLastIndexed(byName);          // the tag's last record
table.GoPreviousIndexed(byName);      // one step back in the tag's order
```

Four methods, one implementation shared with the mode-based three (Decision 9). They step
**unconditionally** — to the next record in the tag's order whatever its key is — which is the stopping
rule a whole-table walk needs, as distinct from the match-bounded stepping that walks one key's
duplicates and arrives with `Seek` in 007.

**Deliberately not exposed yet:** the key bytes behind the current record (a `Tag.CurrentKey` would be
a diagnostic with no caller), a tag's key count (it costs a full walk, which is what made the C
library's own counter wrong), and any way to open a second index file.

## Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| the index file's bytes | `IRandomAccessSource` | `InMemorySource` over an `IndexImage` build | tag-order navigation with no disk, layer 2 |
| finding the production index | `ICompanionFileResolver` | `FakeFileSystem`, and one that finds nothing | a header that declares an index with no file behind it, layer 3 |
| the table's own bytes | `IRandomAccessSource` | `TableImage`, extended to build a table *with* an index | a two-record table in tag order, which no corpus case is small enough to be |

The first two already exist; `TableImage` gains an index and that is the only new test infrastructure.

## Decisions

1. **The production index is opened with the table, when the header says there is one.** DBF byte 28
   bit `0x01` is the trigger, and a declared index whose file is missing is an error — the same rule
   step 001 applies to a declared memo file, and for the same reason: a table that quietly opens
   without its index navigates in record order and silently answers differently.
   *Rejected — probing for `<table>.cdx` regardless of the bit.* It would open an index the table does
   not declare, which is how a stale file starts being trusted.
   *Rejected — opening the index lazily on first use.* The failure then arrives at a `SelectTag` call
   rather than at `OpenTable`, which is the wrong place to learn the file is missing.

2. **Companion resolution is case-insensitive, and `.cdx` is lower case.** Already true for `.fpt`
   (ADR-06's territory, `ICompanionFileResolver`); the corpus confirms CodeBase writes `CUSTOMER.cdx`
   beside `CUSTOMER.DBF`. A port on a case-sensitive filesystem that assumed `.CDX` would find nothing.

3. **Selecting a tag does not move the cursor.** The C library's `d4tagSelect` only records the
   selection (d4index.c); the next `Top` or `Skip` is what re-positions. So a caller that selects a tag
   and reads the current record gets the same record it had — which is surprising enough to be worth a
   named test rather than a comment.

4. **An index entry naming a record past the end of the table is skipped, in the direction of travel.**
   Reproducing d4skip.c:1296-1308 rather than refusing, because the C library reads such a file and
   because the situation is legitimate under concurrency. It is also **unreachable from the corpus** —
   every generated index is consistent with its table — so it is a component test over a hand-built
   pair, and named as ungated.

5. **Hitting the end of a tag sets both end flags**, as `d4skip`'s tag path does. Not a guess about
   intent: the port models the two flags separately already, so it can be faithful here at no cost, and
   step 002 established the same shape for an empty table.

6. **The key type is resolved from the field table when the expression is a bare field name, and
   refused otherwise — at selection, not at open.** Per the section above and ADR-28. Refusing at
   selection keeps a table with one exotic tag fully usable through its other tags, which is what a
   caller would expect and what the C library does by simply not caring until asked.

7. **`Tag` is a value the caller holds, not a live cursor.** It answers what the header said. Two
   callers may hold the same `Tag` and the table has one selection, exactly as `FieldDefinition` works
   for fields — the shape step 002 settled and there is no reason to differ.

8. **A filtered tag is navigated as what it is: fewer records.** `T_FILT` holds 22 keys over 32
   records, so a walk in its order visits 22 records and the other ten are unreachable through it.
   Nothing has to *evaluate* the filter — that would be `EXPR` — because the keys that exist are
   already the filtered set. Worth stating because "some records are missing" looks like a defect until
   you know it is the point.

9. **Two surfaces onto one implementation: the C library's mode, and explicit index navigation.**
   `Top`/`Bottom`/`Skip` with a selected tag are what the C library offers and what a port of an existing
   application will expect. `GoFirstIndexed`/`GoLastIndexed`/`GoNextIndexed`/`GoPreviousIndexed` name the
   tag at the call site instead, which is what new code reads better as — a walk whose meaning does not
   depend on a `SelectTag` several screens earlier. Both go through the same `TableTagCursor`; the
   mode-based three are a thin layer that supplies `SelectedTag` as the argument.
   This is not two ways to do one thing for the sake of it: the mode is a **stateful** contract (anything
   that changes the selection changes what `Skip` means) and the explicit form is a **stateless** one,
   and a library that owes compatibility to the first and legibility to the second is better off saying
   so than picking one and making half its callers wrap it.
   *Rejected — only the mode.* Every caller that wants one indexed walk would have to save the previous
   selection, set it, walk, and restore it, which is boilerplate that invites a leaked selection.
   *Rejected — only the explicit form.* It would break the C library's own navigation shape, which
   `PORTING-PLAN.md` §4 keeps deliberately (`r4*` flow values, `Top`/`Skip` semantics) so that a port of
   an existing application is a rename and not a rewrite.

10. **The explicit methods step unconditionally, and that is the whole distinction from 007's seeking.**
    `GoNextIndexed` moves to the next record in the tag's order whatever its key is, stopping only at the
    end of the tag. The match-bounded stepping — stop when the key stops matching a search value — is
    `SeekNext`, arriving publicly in 007 over the internal operation 005 ports. Both stopping rules are
    wanted and neither substitutes for the other (005's Decision 13), so they are named differently at
    every layer: `Next`/`Previous` internally, `GoNextIndexed`/`GoPreviousIndexed` on the table,
    `SeekNext`/`SeekPrevious` for the bounded pair.

11. **`GoFirstIndexed(tag)` on a tag whose expression cannot be typed is refused, like `SelectTag` is.**
    The explicit form does not dodge ADR-28: it needs the same pad byte for the same reason. Refusing at
    the call rather than at open keeps the rest of the table usable either way.

## Open questions

| # | Question | Status |
|---|---|---|
| Q1 | **Should `Table.Tags` be empty or should `HasIndex` be false when a table has no production index?** Both, presumably — but the seven older corpus tables make this a real case rather than a hypothetical. | **Leaning both**: `HasIndex` false and `Tags` empty, with `SelectTag` on a tag from another table refused. Settle in sub-step 2 |
| Q2 | **Does `Skip(0)` re-read the current record in tag order?** In record order step 002 settled that it clears the beginning flag and stays put. The tag path adds a "record still exists" question. | **Answered by a test.** The C's tag path skips zero entries and then re-reads, so `Skip(0)` on a record that has since gone past the end would move — but that needs concurrency to arise, so the test states the single-user promise: `Skip(0)` stays |
| Q3 | **Is `SelectTag(Tag)` the right shape, or should it be `SelectTag(string)`?** The string form is what the C library offers and what a caller types; the object form cannot name a tag that does not exist. | **Leaning both**, with the string overload resolving through `Tags` so the failure is one message in one place. Cheap, and it keeps the common call short |
| Q4 | **What does `RecordCount` mean with a tag selected?** The table's record count is unchanged, but a filtered tag reaches fewer. | **Leaning leave it the table's count**, documented, because that is what the C library reports and because the alternative costs a full tag walk. A `Tag.KeyCount` can arrive when something needs it |
