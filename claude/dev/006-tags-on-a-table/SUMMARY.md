# 006-tags-on-a-table — summary

**Closed:** 2026-08-12. **Gate passed.** Capability advanced: `CDX-READ` — **done for reading**. A table
now opens its production index and can be navigated in a tag's order. What is left of `CDX-READ` waits on
`EXPR`, and is named below.

No commit hash here, for the same reason the root `STATE.md` header carries none: a file cannot name the
commit it is part of. `git log` over this folder is the record.

## What shipped

**An index tag as an order the cursor can follow.** **1039 tests green**, up from 972: 586 unit,
component and fault-injection, 453 golden.

```csharp
using Table table = engine.OpenTable("customers.dbf");   // opens customers.cdx when byte 28 says so

FieldDefinition name = table.Fields["NAME"];
Tag byName = table.Tags["NAME"];

table.SelectTag(byName);                 // the tag is now the cursor's order
for (GoResult go = table.Top(); go == GoResult.Ok; )     // Top is the tag's first record
{
    Use(table.GetString(name));
    if (table.Skip(1) != SkipResult.Moved)
        break;
}

// or, without changing the table's mode:
for (GoResult go = table.GoFirstIndexed(byName); go == GoResult.Ok; go = table.GoNextIndexed(byName))
    Use(table.GetString(name));
```

`Tag` and `TagCollection` are the only new public types; `Table` gained `Tags`, `HasIndex`,
`SelectedTag`, `SelectTag` and the four `…Indexed` methods, and `Top`/`Bottom`/`Skip` branch on the
selection. **No corpus work and no Windows** — the corpus already held everything this step is gated
against, and the gate joins two existing dumps by record number.

**The pad byte is now derived rather than supplied.** `KeyTypeResolver` implements ADR-28: a tag whose
expression is, trimmed and case-insensitively, the name of one of the table's fields takes that field's
type, and the pad byte follows from `i4init.c:557-604`. All 18 corpus tags are of that shape and the
golden gate checks each derived byte against the `pChar` its dump records — the value 004 and 005 had to
be told. Resolution is **lazy, per tag, on first use**, so a tag this port cannot type refuses when it is
selected and leaves the rest of the table working.

**Library** — `KeyTypeResolver`, `Tag`, `TagCollection`, `TableTagCursor` with `TagLanding`, the `Table`
surface above, `DbfOpener.OpenIndex`, `OpenedTable.Index`, and `CdxTag.PadByte` made lazy.
`Table.HasProductionIndex` was **removed**: it reported the header's claim, which `HasIndex` now reports
as a fact, and two properties that can never disagree are one property and a trap.

**Tests** — `KeyTypeResolverTests` (the whole type table, including the four types no corpus tag indexes),
`TableTagsTests` with `IndexedTableImage` (a hand-built table *and* index, for the pairs the corpus cannot
express), and on the golden side `TagNavigationGoldenTests`.

## What this step proved

- **3364 records reached through an index**, over 18 tags of four tables — and **every field of every one
  of them checked against that record's entry in the table's own dump**. A walk that returned the right
  record numbers while reading each one's neighbour is the failure this half exists for, and mutation C2
  below shows it is the only thing that catches it.
- **The same 3364 through both surfaces**, and again in reverse from the other end. The mode-based and
  explicit forms must never drift, so the gate runs the whole corpus through each.
- **The seven un-indexed tables stay exactly as they were** — `HasIndex` false, `Tags` empty, record-order
  navigation unchanged. That is the regression risk this step carried, and it is in every golden run.
- **Every derived pad byte equals the C library's.** 18 tags, including the `GENERAL`-collated one, which
  needs no field at all.
- **The ends follow `d4skip`, not intuition** (ADR-29, `CDX-FORMAT.md` §7.1): a backwards skip off the
  front of a tag stops on the tag's first record and leaves it readable; a forward one ends past the last
  record; at end of file a backwards skip re-enters through the bottom and counts that as a step; a skip
  of zero does not consult the tag at all.
- **Mutation-checked four ways**, and the radii are the evidence:

  | Mutation | Failed | Where |
  |---|---|---|
  | Navigate in record order while a tag is selected | 12 | Every selected-tag walk; the explicit form and all seven un-indexed tables stay green |
  | Ignore the descending flag | 25 | Only `CDXBASE` and `CDXDEEP`, the two files with a descending tag (`T_TEXTD`, `D_PFX`); `CDXCOLL` and `IDXONE` green |
  | Read the record from before the tag moved | hangs | Coarse but decisive: the walk stops advancing, because the next step re-synchronizes to the record it just reported |
  | Read a neighbouring record but report the right number | 4 | **Only** the four golden walks that verify field values — every record-number assertion stays green |

## Deviations from the plan

- **`Table.Skip`'s tag path went further than the design described.** Reading `d4skip` line by line for
  the end cases turned up four behaviours the design had not named — the skip of zero, the re-entry
  backwards from end of file, and the two different end shapes — and one it had named wrongly (running
  out of entries does *not* always raise both flags). All four are now reproduced and cited, the facts are
  in `CDX-FORMAT.md` §7.1, and the surface difference they create between `Skip` and the four `…Indexed`
  methods is **ADR-29**.
- **Stepping from a record the tag does not list became a refusal, not an end-of-file answer.** The C
  library re-derives the record's key through the expression; this port cannot, and answering "end" would
  be a plausible-looking wrong record set. **ADR-30**.
- **The pad byte became lazy.** The design said "refused at selection", but 004's reader resolved every
  tag's pad byte while opening the file, which would have made one untypeable tag close the whole table.
  `CdxTag.PadByte` now resolves on first use; `IndexFileReaderTests` states the new contract.
- **No hand-built two-leaf component case.** The plan listed "tag order over two blocks" at layer 2;
  `CDXDEEP`'s three-level tags walk 600 records through the table in the gate, which covers the leaf chain
  better than a synthetic pair would, and 004 already covers block chaining at the index layer.
- **`HasProductionIndex` was removed** — see above. It had no test and no caller.

## Ungated — no corpus case exists

- **A composite key expression** such as `UPPER(NAME)`, refused until `EXPR`. Component and unit tests
  cover the refusal; no corpus tag has one, and adding a case would gate the refusal, not the reading.
- **A tag over a `Y`, `T`, `Z` or `F` field.** The resolver handles all four; the corpus indexes none of
  them. Unit tests only, and still a cheap `CDXBASE` extension later.
- **An index entry naming a record past the end of the table.** Every generated index agrees with its
  table, so this is a component test over a hand-built pair (`IndexedTableImage`) — the C library's own
  skip-forward behaviour, reproduced from `d4skip.c:1296-1308`.
- **A table whose every tag is refused**, which needs a composite expression to arise.
- **Two index files open on one table**, out of scope until something opens a second.
- **A tag-order move that races a writer** — `LOCKING`.

## For the next step

- **007 is seek by value** — `Table.Seek("SMITH")` — which needs `COLLATION`'s machine-half transforms to
  turn a value into key bytes. `KeySearch` is the seam; nothing above it needs to change.
- **It inherits three questions from 005**: the `.NULL.` convention for an empty public seek, whether
  `SeekNext`'s degrade-to-seek survives into the public API, and `considerPartialSeek` for a collated
  partial seek.
- **`EXPR` closes two things this step left open**, both named in ADR-28 and ADR-30: typing a non-field
  key expression, and positioning a tag on a record it does not list.
