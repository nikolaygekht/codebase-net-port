# 012-p1-tails — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

Close the four tails the P1 capabilities left owed — a string seek that silently answers on a
non-character tag, the query refusals no dump isolates, the `EXPR` behaviours gated by nothing, and the
compressed memo entry — each with a witness it did not have before. **Verified when `dotnet test` is
green and one generator run leaves every pre-existing corpus file byte-identical except the files this
design declares (§Corpus changes).**

**Capability:** none opened — this folder is a remediation, numbered like a step (`claude/dev/README.md`),
in the shape of [`007-audit-glm`](../007-audit-glm/). It advances `DBF-READ`, `CDX-READ`, `EXPR` and
`QUERY` by closing what each left owed, and `CORPUS` by four cases.
**Governing specs:** `FPT-MEMO.md` §3.9, `EXPRESSIONS.md` §6–§7, `QUERY-OPTIMIZER.md` §3.4, `CDX-FORMAT.md` §7.

## What changes, in short

The rest of this document is the reasoning and the citations; this section is what will actually be
different afterwards.

**A. `Seek(string)` on a non-character tag.**
- **Now:** `table.Seek("250")` on a numeric tag silently seeks the key for zero — a wrong answer, no
  error. The same on date, integer and logical tags.
- **After:** a `CodeBaseException` saying, in effect, "tag T_NUM is numeric; use `Seek(double)`".
  Character tags behave as before.
- **Code:** one branch in `TableTagCursor.SearchFor`.
- **Tests:** every tag of every corpus table accepts or refuses a string seek by its key kind; plus
  what `FoxNumeric` answers for `"ABC"`, `"-"`, an over-long number and too many decimals, pinned.

**B. The optimizer's refusals.**
- **Now:** the optimizer already refuses three kinds of tag — unique, filtered, GENERAL collation —
  and the code is right. But no corpus case shows the C library refusing a unique tag, so breaking
  that refusal reddens no reference-backed test; only the full-scan comparison guards it.
- **After:** `CDXCOLL` gets a query dump, and a new table `QRYGATE` has a field with only a unique
  tag, a field with only a filtered tag, a field with only a GENERAL tag, and beside them control
  fields with plain tags. The C library's dump records, per filter, which tag it used: "none" for the
  problem field, the tag for the control.
- **Code:** none in the library. The generator, the table lists of the reference-backed tests, and
  the wrong claims in `QUERY-OPTIMIZER.md` §3.4 and 011's `SUMMARY.md`.
- **Check:** break each of the three refusals in turn; a reference-backed test must go red.

**C1. `.NOT.` and null — a real bug.**
- **Now:** on a record where `K_NNUM` is null, `.NOT.(K_NNUM = 0)` is true here and false in
  CodeBase, so a query with that filter returns **extra records**.
- **After:** `.NOT.` of null is false.
- **Code:** one line in `ExpressionEvaluator`.
- **Generator:** `CDXEXPR` gains expressions over a null operand — `<>`, `#`, `.NOT.`, arithmetic.
- **Not copied:** the C library's own bug, where after the first null record every computed value of
  that expression stays null. To make it visible, the generator evaluates each expression twice —
  one parse for all records, and a fresh parse per record — and the port is checked against the
  second.

**C2. An expression parsed for a tag.**
- **What is checked:** CodeBase computes some key lengths differently when an expression is parsed for
  an index — a memo field in a tag, `DESCEND` under a non-machine collation. The generator gains a
  section of such parses.
- **Code:** whichever of those lengths the port gets wrong; the dump says how many — possibly none.
- **Docs:** `EXPRESSIONS.md` records that error −510 is unreachable here.

**C2a. The tag-context tags themselves, and `ASCEND`/`DESCEND` under a tag's collation.** *(Added
2026-10-31, at the user's call, after sub-step 5.)*
- **Why:** sub-step 5 witnessed the lengths through the resolver alone; no corpus tag was a bare memo
  or a GENERAL tag over `ASCEND`/`DESCEND`. Two new cases hold such tags, so the rules are witnessed in
  stored keys, seeks and query answers.
- **Found on the way:** the reference's `i4tagAdd` skips the re-parse `i4create` makes once the
  collation is set, writes `DESCEND(K_TEXT)` under GENERAL with key length 20, and refuses its own file
  at open (−310). Both cases are built by `i4create` alone; the omissions are recorded so the writer
  does not port them (Decision 6).
- **Now:** a GENERAL tag over `ASCEND`/`DESCEND` of a string cannot be keyed here. The evaluator
  converts without the collation and the key builder then collates the result a second time — the
  reference does the opposite on both counts (`EXPRESSIONS.md` §9.5) — and both throw on the width.
  A string seek on any `DESCEND` tag never finds, here as in the reference.
- **After:** inside a non-machine tag, `ASCEND`/`DESCEND` of a string collates its operand (heads,
  tails, zeros) and `DESCEND` complements it; a non-machine tag with any `ASCEND`/`DESCEND` stores
  those bytes without collating again. A tag whose whole expression is `ASCEND`/`DESCEND` of a string
  is sought through that transform (Decision 7).
- **Generator:** `TAGCTX` (machine, `i4create`): `T_MEMO`, `T_NMEMO` (nullable memo), `T_TEXT`,
  `T_ASC`, `T_DESC`. `TAGGEN` (cp1252, GENERAL, `i4create`): `G_TEXT`, `G_ASC`, `G_DESC` over the
  same kind of text, with accents and an expansion. `dump-index.cpp`: a nullable tag is not an
  identity transform for `[seeknext]` (`tfile4stok` puts the indicator in front, D4SEEK.C:47-57).
- **Code:** `TagContext` carries the collation and code page; `CompiledExpression` keeps the tag it
  was resolved for; `ExpressionEvaluator.KeyForm` collates under it; `SeekConverter` chooses the
  uncollated kind for such a tag and records a whole-expression conversion; `TableTagCursor.SearchFor`
  seeks through it.

**C2b. A tag whose expression disagrees with its key length is refused at open.** *(Added
2026-10-31, at the user's call.)*
- **Now:** the reference resolves a tag's expression at open and refuses the index when the key
  length it computes differs from the header's (E84901, `e4index`, i4init.c:162-166 and :485-490).
  The port has no such check: it opens the tag and builds seek keys from an expression the keys were
  not built by.
- **Found while designing it:** a memo under GENERAL is resolved at half the cap — `info[0].len` and
  the expression's length divided by (1 + the collation's add) when at least 239 (i4init.c:466-481,
  i4create.c:1029-1045) — so its expression is 120 and its key 240, or 119 and 239 when nullable.
  The port has no such rule; with the check it would refuse a valid file.
- **After:** `SeekConverter.For` computes the key length the reference computes — `ExpressionKey.Length`
  with the collation's add for a collated string, none for a conversion — and refuses a mismatch with
  `CodeBaseException(Index)` naming the tag. The refusal is per tag at first use, like every other
  refusal of a tag here, so the table still opens and a query leaves the tag out; the reference fails
  the whole index open. `TypeResolver` applies the memo halving under a collated tag.
- **Generator:** `TAGGEN` gains `K_MEMO` and a nullable `K_NMEMO` with `G_MEMO`/`G_NMEMO`. New case
  `TAGBAD`: `T_TEXT` by `i4create`, then `G_DESC` over `DESCEND(K_TEXT)` added by `i4tagAdd` under
  GENERAL — the file the reference refuses to open. Its table dump is taken without the production
  index, and `TAGBAD.open.dump.txt` records the refusal (`i4open` → −310).

**C3. `UPPER`/`LOWER` by the table's code page.**
- **Now:** only `a–z` change case. In a cp1251 table `UPPER("привет")` returns `"привет"`.
- **After:** `"ПРИВЕТ"`. Case is mapped by the table's encoding; an unmarked table maps ASCII only, as
  now, unless the host set `CodeBaseEngine.DefaultEncoding` (amended 2026-10-31, Decision 1). This
  affects filters, and the keys the port computes to seek an `UPPER(...)` tag.
- **Code:** a new `CaseMap` (a 256-byte table for single-byte encodings); `EvaluationContext` carries
  it; `UPPER` and `LOWER` take it from there.
- **Docs:** an ADR; the XML docs of `Query` and `Seek`; `README.md` — a foreign index with `UPPER` over
  non-ASCII keys is unsupported, reindex it.
- **Check:** `vfp/VFPFUNC.CDX`'s `T_UPPER`/`T_LOWER` keys match byte for byte.

**D. Compressed memos.**
- **Now:** a type-3 memo entry — CodeBase with compression on — throws "unsupported".
- **After:** it is read and decompressed. Damaged data is refused, not turned into garbage: a wrong
  zlib header, a length that does not match, a truncated stream.
- **Code:** `MemoReader`.
- **Generator:** a new case `CMPMEMO`; zlib is already vendored and builds.
- **Docs:** a new ADR superseding ADR-23; `FPT-MEMO.md` §3.9 corrected.

**E. A table with compressed data is refused at open.** *(Added 2026-10-29, at the user's call.)*
- **Now:** a `0x31` table with `flags[2] = 1` — CodeBase's whole-file compression, written by
  `d4compress` or with `compressedData` on at create — opens as though it were plain, and its
  compressed bytes would be read as records. The flag is parsed (`FeatureFlags.HasCompressedData`) and
  nothing looks at it.
- **After:** opening it throws `CodeBaseException(NotSupported)` naming the extension, before the file
  length is checked against the header (a compressed file is shorter than its records, and that
  message would point the wrong way). A `0x30` table with a stray byte there still opens, because the
  C library reads the flags only on `0x31` (D4OPEN.C:2152-2196).
- **Not supported, by decision:** a CodeBase-only, VFP-unreadable storage layer of its own — three
  offset-table formats ahead of block-compressed data (D4OPEN.C:2760 onward). If it is ever wanted,
  it is a post-v1 capability, and the seam is a decompressing `IRandomAccessSource`.
- **Code:** one check in `DbfOpener.Open`.
- **Tests:** layer 2 in `DbfOpenerTests`, over real corpus headers with one byte changed as input:
  `CMPMEMO.DBF` (a real `0x31`) with `flags[2]` set is refused; `VFPTYPE.DBF` (`0x30`) with the same
  byte set opens.
- **Docs:** an ADR; `PORTING-PLAN.md` §2.2.

**F. The engine's list of open tables is synchronised.** *(Added 2026-10-29, found in sub-step 2.)*
- **Now:** `CodeBaseEngine.open` is a plain `List<Table>`; `OpenTable` adds to it and a table's close
  removes from it, from whatever thread did either, with no lock. ADR-41 says engine-shared state is
  the library's to synchronise. Two threads opening or closing tables of one engine tear the list:
  `Dispose` then meets a null and throws, or a table is lost and its handle leaks.
  `OpenTables` returns the live list.
- **After:** one private lock guards the list. `OpenTables` returns a snapshot. A table opened while
  the engine is being closed is closed again and the open refused, so none escapes `Dispose`. Tables
  are disposed outside the lock.
- **Code:** `CodeBaseEngine` only.
- **Tests:** a layer-2 stress test of many threads opening and closing tables of one engine, then
  asserting the list is empty and `Dispose` is clean. **Mutation:** remove the lock and it fails.

**G. Timed tests run alone.** *(Added 2026-10-29.)*
- **Now:** `BlockCacheSpeedupTests`' two clock assertions run in parallel with the rest of the suite,
  and once fell to 1.91× in Release under that load.
- **After:** the class joins an xUnit collection with parallelisation disabled, which runs after the
  parallel collections, by itself. The floor is unchanged.

**In the library, eight places change:** `TableTagCursor`, `ExpressionEvaluator`, `EvaluationContext`,
`MemoReader`, `DbfOpener`, `CodeBaseEngine`, the new `CaseMap`, and possibly `TypeResolver`. Everything else is the generator, the
tests, and correcting what the documents got wrong.

## Why one folder and not four

`DEV_APPROACH.md` §1 wants one capability and one gate per step. These four tails share the thing that
dominates their cost — **one generator trip**, now local since development moved to Windows — and each
is small. The precedent is `007-audit-glm`: fixes across several areas, each its own sub-step with its
own check, under one gate that says nothing pre-existing moved. Each sub-step here is independently
revertible, and the plan orders them so the generator baseline is proved before anything depends on it.

## What the research overturned before any code

Four recorded claims are wrong, each found by reading the source and each confirmed by a second read
(2026-10-29). They are corrected in this step, in the documents that own them:

1. **`c4compress`/`c4uncompress` are in the drop**, `C4CODE.C:400-470`, under `#ifdef S4COMPRESS`.
   `FPT-MEMO.md` §3.9 ("bodies are not in this source drop … C4CODE.C here contains no compression") and
   ADR-23 ("reconstruct the wrapper") are wrong; the wrapper is **read, not reconstructed**: a 4-byte
   native (little-endian on MSVC) uncompressed length, then a `compress2` stream — RFC 1950 zlib format —
   at level `c4getFileCompressLevel` (default 9, `C4CODE.C:1560`).
2. **The filtered-tag refusal is already isolated.** Tag-directory order is **alphabetical**, so in
   `CDXBASE` `T_FILT` (over `K_N`) is visited **before** `T_NUM`, and `K_N > 0` recording `T_NUM` means
   the reference saw `T_FILT` and refused it. 011's `SUMMARY.md` §Deferred and `STATE.md` say the
   opposite ("a plain tag is found first").
3. **The unique-tag refusal is *not* isolated**, and `QUERY-OPTIMIZER.md` §3.4 cites the wrong evidence:
   `T_DUP` sorts before `T_UNIQ`, so `K_DUP = "CHERRY"` matches `T_DUP` and `T_UNIQ` is never examined.
   `QueryEquivalenceGoldenTests.ARefusedTagDoesNotStopTheSearch` proves nothing about the unique gate.
4. **`c4upper` is in the drop** (`c4.c:1195`), contrary to `EXPRESSIONS.md` §7.1 and §14.1, and
   **CodeBase's `UPPER` is host-dependent**: for a table marked 1252 or 1250 (`e4functi.c:2876-2911`) it
   calls `CharUpperA`, which maps through the **process** ANSI code page; every other mark takes
   `c4upper`, which is `_strupr_s` in the C locale — ASCII only. CodeBase has no `LOWER` (−470).

## The four parts

### A. `Seek(string)` refuses a tag whose keys are not character

The defect is written up in
[`009-performance-audit/DEFECT-seek-string-on-a-non-character-tag.md`](../009-performance-audit/DEFECT-seek-string-on-a-non-character-tag.md):
`TableTagCursor.SearchFor`'s `default:` branch hands a caller's text to `RecordKey.WriteValue`, which reads
it as a field's stored bytes, so a numeric tag silently seeks the key for zero. The fix is the refusal
the other overloads already make — `Seek` requires the exact type, decided in that review. The
`default:` branch throws `CodeBaseException(ErrorCode.Info)` naming the tag and its kind, with the
message shape of `TableTagCursor.Numeric`. `KeyKind.Logical`, which only an expression produces, is
refused the same way: there is no logical overload and a string is not one.

Plus the layer-1 tests the defect asked for: `FoxNumeric` pinned on unparseable, over-long,
over-precise and sign-only text, **as a decision recorded** — the C answer (`c4atod`) is not in the drop.

### B. The query refusals each get a dump that isolates them

- **`CDXCOLL` gets a query dump.** `C_GEN` (GENERAL) sorts before `C_MACH` over the same field, so the
  reference visits and refuses the collated tag on every `K_TEXT` term — the collation refusal isolated
  with **no index change**: `CDXCOLL.DBF`, `.cdx` and both existing dumps must regenerate byte-identical.
- **A new table, `QRYGATE`**, holds a lone unique tag, a lone filtered tag and a lone GENERAL tag, each
  over its own field, each beside a control field whose plain tag answers the same shape. A refusal then
  shows as `tag=-` and `fullyMapped 0` **whatever the walk order**, which is the property the existing
  cases only have by alphabetical accident. A new table rather than new tags on `CDXBASE`, because a new
  tag rewrites `CDXBASE.cdx` and its whole `.cdx.dump.txt` (011's reason, still good).
- The reference-backed gates (depths 1–4, leaf evaluation, merge) gain both tables in their table
  lists; Gate A gains `QRYGATE`; `CorpusLayoutTests` gains `QRYGATE`.

**Overturned by the first regeneration (2026-10-29).** The reference does **not** refuse a GENERAL tag:
the refusal at r4log.c:114-120 is inside `#if !defined( S4USE_GENERAL_TAGS_IN_RELATE )`, which
`d4defs.h:669-673` defines in every non-testing S4FOX build. Both dumps show the GENERAL tag used and
the answer wrong (`agree NO` on all six such filters). So the first bullet's premise was false and the
gates were built differently from the third bullet:
- `QRYGATE` joins every decision gate; `CDXCOLL` joins **none** — every one of its filters reached
  `C_GEN` first, and what the reference would have matched with the refusal in force is not in its
  dump. It stays in Gate A.
- The decision gates compare only cases that used no collated tag (`CorpusQueryDump.Comparable`),
  identified from the dumps — a matched tag whose index header names a sort sequence.
- `CollationDivergenceGoldenTests` checks those cases against what the reference *does* say: its scan,
  and its tag's stored key order.
- The port's behaviour does not change; its account does: ADR-59, `QUERY-OPTIMIZER.md` §3.4, §8, §11.1,
  `QueryTagScope` and `TagEligibility` docs, `PORTING-PLAN.md` §8.

### C. Three `EXPR` behaviours get witnesses, and one is a real defect

**C1. Null operands.** The reference (`e4functi.c`, all under `S4CLIENT_OR_FOX`):

| Form | Reference | Port today |
|---|---|---|
| `a <> b`, `a # b`, one side null | **1**, node null (`e4notEqual` :2315-2335, and its `Cur`/`DtTime` siblings) | `Logic(true).AsNull()` — right |
| `.NOT. x`, `x` null | **0**, node null (`e4not` :2248-2261) | `!x` — **wrong**: `.NOT.(K_NNUM = 0)` on a null row is true here and false there |
| both sides null | compares the zeroed values | compares normally — right |

The `.NOT.` row is a **wrong record set** for any filter that negates a comparison over a nullable
field, and it is fixed here.

**The reference's `isNull` is sticky.** A comparison or arithmetic node sets its `isNull` on a null
operand and no path clears it; only field-loader nodes reset theirs (`e4functi.c:2324-2331`, `:329-336`).
So within one compiled `EXPR4` evaluated record after record, every computed node stays null after the
first null record — `K_NNUM + 1 = 251` would be false at a record holding 250. Read from the source, not
yet witnessed. **The port does not replicate it** (Decision 3).

**C2. A tag-context parse.** The only raise of `e4tagExpr` (−510) is `e4parse.c:2383-2387`: a non-null tag
pointer **and** an alias that resolves to *another open* `DATA4`. The port has no multi-table expressions
and hands the scanner only the table's own name, so a foreign alias is `FieldName` (−210) in a tag and in
a filter alike; −510 is unreachable by construction. What a tag-context parse *does* change is lengths:
a bare memo tag's length cap (`e4parse.c:433-457`, which `TypeResolver.cs:53-60` names as missing) and
`DESCEND`/`ASCEND` of a string under a non-machine collation (`:655-672`). The generator gains a
tag-context section; the port gains whichever of those lengths it gets wrong.

**C3. `UPPER` and `LOWER` map by the table's code page** — Decision 1. The port today maps ASCII only,
in static methods that see no code page (`ExpressionEvaluator.cs:665`, `:873`).

### D. A compressed memo entry is read

`MemoReader.Read` stops refusing `MemoType.Compressed` (`Memo/MemoReader.cs:99-110`) and inflates it:
payload ≥ 4 bytes, read the u32 little-endian uncompressed length, refuse it above `0x7FFFFFF0` (the C's
own guard, `m4file.c:229-238`) before allocating, verify the RFC 1950 header (`CMF & 0x0F == 8`,
`(CMF·256 + FLG) % 31 == 0`), inflate `payload[4..]` with `ZLibStream`, and require the output length to
equal the prefix. The header check is what turns a **QuickLZ**-built entry (`D4all.h:126` — same type,
same layout, different algorithm) into a refusal rather than nonsense. `MemoEntry.Type` keeps reporting
`Compressed`, as the C keeps reporting 3 (`m4file.c:256`).

The generator vendors zlib, because `cb-config.h:55` defines `S4OFF_COMPRESS` precisely because none is
shipped. `cb-config.h` drops that define **and adds `S4COMPRESS_ZLIB`** — the force-include bypasses
`D4all.h:124-127`, and without it the build stops at `d4defs.h:556-557` (`#error - Must compile with a
compression option`). `S4COMPRESS` itself then follows from `d4defs.h:2840-2842` (Win32, not Win64).

## Not in this step

- **A blank and a malformed numeric field in the corpus** (the defect write-up's item 3). Part A refuses
  the seek path, which is the wrong-answer half; what `f4double` answers for `"ABC"` is a separate
  question about field decoding, with its own case.
- **The defect write-up's item 4**, a sweep for every parse that resolves to a default. Its own audit.
- **`MOD` by zero and `SQRT` of a negative** (010 owed) — the C library is not the oracle for VFP's
  raising behaviour, and the VFP VM run is separate (`STATE.md` §3).
- **ADR-42's other three language presets** (010 owed) — a picture string each, gated by nothing, and
  nothing in P1 depends on them.
- **Writing** a compressed memo. The port is read-only, and `.NET`'s deflate cannot reproduce zlib 1.x's
  bytes, so a type-3 writer could never be byte-exact anyway.
- **Turkic casing** for cp1254/cp857 (Decision 1).
- Step 009's performance leftovers. They are measured, not owed.

## Corpus changes

The gate's declared list. Anything else that moves in `git status` after a regeneration is a failure.

| File | Change |
|---|---|
| `CDXEXPR.expr.dump.txt` | new field expressions (C1), a tag-context section (C2), and a second, per-record-fresh evaluation of every field expression (Decision 3) |
| `CDXCOLL.query.dump.txt` | **new** |
| `QRYGATE.DBF`, `.cdx`, `.dump.txt`, `.cdx.dump.txt`, `.query.dump.txt` | **new** |
| `CMPMEMO.DBF`, `.fpt`, `.dump.txt`, `.memo.dump.txt` | **new** |
| `TAGCTX.DBF`, `.cdx`, `.fpt`, `.dump.txt`, `.cdx.dump.txt`, `.query.dump.txt` | **new** (C2a) |
| `TAGGEN.DBF`, `.cdx`, `.fpt`, `.dump.txt`, `.cdx.dump.txt`, `.query.dump.txt` | **new** (C2a); two memo fields and tags added (C2b) |
| `TAGBAD.DBF`, `.cdx`, `.dump.txt`, `.open.dump.txt` | **new** (C2b) |
| `vfp/*` | untouched — the VFP oracle is not rerun |

`CDXEXPR.query.dump.txt` changes **only** if a null filter is added to its query list (Decision 3 says
whether).

## Classes

Most of this step changes existing classes rather than adding new ones.

| Class | Role | Change | Notes |
|---|---|---|---|
| `TableTagCursor` | Controller | `SearchFor` refuses every non-character kind | the `default:` branch |
| `ExpressionEvaluator` | Controller | `.NOT.` honours null; `UPPER`/`LOWER` take the context's case map | stops being static for those two |
| `CaseMap` *(new)* | Entity | Maps a byte string to its upper or lower case under one `Encoding`, length-preserving | built once per table, lazily; pure apart from the `Encoding` it is given |
| `EvaluationContext` | Entity | carries the table's `CaseMap` | the evaluator's only route to a code page |
| `TypeResolver` | Controller | the tag-context lengths C2 shows it gets wrong | only if C2 finds a divergence |
| `MemoReader` | Controller | inflates a type-3 entry | `ZLibStream`, BCL — no dependency (ADR-17) |
| `TagContext` | Entity | carries the tag's collation and code page; the per-character add is derived | C2a |
| `CompiledExpression` | Entity | keeps the `TagContext` it was resolved for, null for a filter | the evaluator's route to the tag's weights |
| `ExpressionEvaluator` | Controller | `KeyForm` collates a string operand under a non-machine tag | e4functi.c:3042-3062 |
| `SeekConverter` | Entity | a non-machine tag with `ASCEND`/`DESCEND` keys as `Character` (already collated); records whether the whole expression is a string's `ASCEND` or `DESCEND`; refuses missing weights for it too | E4EXPR.C:671-684 |
| `ConversionSearch` *(new)* | Controller | writes the search for a value through a whole-expression `ASCEND`/`DESCEND`: pad to the operand, collate (heads only for a prefix), complement | pure; Decision 7 |
| `TableTagCursor` | Controller | `SearchFor` goes through `ConversionSearch` for such a tag | |

**`CaseMap`'s rule.** For a single-byte encoding, a 256-entry table built by decoding each byte, applying
`char.ToUpperInvariant`/`ToLowerInvariant`, and encoding back — kept only when the result is **exactly
one byte that decodes to the mapped character**, otherwise the byte maps to itself. For a multi-byte
encoding, decode the value, map, re-encode, and keep each character's mapping only where its encoded
length is unchanged — so a trail byte is never touched, which `_strupr_s` over Shift-JIS would get wrong.
Built from `Table.TextEncoding` for a table that names a known code page, the encoding its text is
already read with; a missing encoding provider fails at the first `UPPER`, as `GetString` already does
(ADR-17). An unmarked table (or one whose mark this library does not know) maps ASCII only unless the
host set `CodeBaseEngine.DefaultEncoding`, in which case that encoding is the table's (Decision 1,
amended). Built lazily, on the first `UPPER` or `LOWER`, so a table whose encoding is unavailable still
selects its tags. The multi-byte map is precomputed per character over the encoding's repertoire and
applied while walking the value with a `Decoder`, so only whole characters are ever replaced and any
byte that does not decode is copied as it stands. The query decomposer needs no map: it folds only
literals and date conversions into constants (`ExpressionEvaluator.IsConstant`), never a case
function, so every `UPPER` in a filter is evaluated per record with the table's context.

## Public surface

**No new public API.** Changed behaviour: `Table.Seek(string)` and its three siblings throw on a tag
whose keys are not character; `Table.GetMemo*` return a compressed entry's payload; a filter or a tag
using `UPPER`/`LOWER` maps non-ASCII characters by the table's code page; a string seek on a tag whose
whole expression is `ASCEND(x)`/`DESCEND(x)` takes the value of `x`, not key bytes (C2a).

## Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| a tag's key kind | `Table` over a corpus table | real corpus tags of every kind | A: every tag refuses or accepts a string seek |
| memo payload bytes | `IRandomAccessSource` | in-memory image | D: header check, length mismatch, oversized prefix, truncated stream — hand-built as **input** only |
| the encoding behind a case map | `CaseMap(Encoding)` | any `Encoding`, provider registered in the test project | C3: the map against its own definition, per code page |

## Decisions

**1. `UPPER` and `LOWER` map by the table's code page, and neither reference is followed.** Decided by
the user, 2026-10-29. CodeBase maps through the host's ANSI code page for two marks and ASCII for the
rest; Visual FoxPro's mapping predates working Unicode support in Windows and is no better evidence. The
port decodes by the table's encoding — the one its text is read with — and applies Unicode's invariant
case mapping. **Invariant, not a culture**: a code page does not name a language (cp1251 serves at least
four), and the only place a culture's casing differs from the invariant one is the Turkic `i`, where
following it would change the key of an all-ASCII value. Consequences, all accepted:

- **There is no full oracle.** The expectation is the definition itself: a layer-1 test rebuilds each
  map from `Encoding` + Unicode and compares. That is a derivation, not hand-typed bytes.
- **A partial oracle exists where host and table agree.** The VFP VM's ANSI code page is 1251 and its
  tables are marked 1251, so its `T_UPPER`/`T_LOWER` keys (`vfp/VFPFUNC.CDX`, record 16) and the excluded
  `LOWER("AB1c"+CHR(201))` row are the correct answer, and are gated byte for byte here.
- **An index another engine wrote with `UPPER`/`LOWER` over non-ASCII keys is unsupported** — its stored
  keys disagree with what this library computes, so a seek or a query over it can be wrong. The remedy
  is to reindex. It **cannot be detected** in general — a CDX carries no writer mark, and must not, since
  this library will write byte-identical ones — so it is documented, not refused: in the ADR, in the XML
  docs of `Table.Query` and the seek family, and in `README.md`. An index agrees with this library when
  its keys are ASCII, when Visual FoxPro wrote it on a host whose ANSI code page is the table's, or when
  CodeBase wrote it for a 1252/1250 table on such a host; it disagrees when CodeBase wrote it for any
  other mark over non-ASCII lower-case data, or for 1252/1250 on a mismatched host. (A lazy idempotence
  check — a key byte the case map would change proves a foreign mapping — catches the common case and
  is noted as a possible later addition, not built here.)
- **The corpus is unaffected**: the one non-ASCII value under an `UPPER` tag is `CDXEXPR` record 16,
  `C9 E8`, which in cp437 is `╔Φ` — neither has a case mapping.
- **Amended 2026-10-31, by the user: an unmarked table maps ASCII only, unless the host set
  `CodeBaseEngine.DefaultEncoding`.** CodeBase writes unmarked tables by default and maps them through
  ASCII (`c4upper`), so an `UPPER` tag it built over non-ASCII lower-case letters stores them
  unchanged; mapping them through cp437 would seek another key. An unmarked table states no language,
  and `a`–`z` are right in every ASCII-compatible encoding. A host that sets `DefaultEncoding` has
  stated the encoding of its unmarked tables, and then that encoding decides. (`EXPRESSIONS.md` §7.5's
  "witness" — `C9 E8` unchanged — does not tell ASCII from cp437, since neither byte has a case pair in
  cp437.)

**2. `Seek` requires the exact type.** Already decided in the 009 defect's review; recorded as an ADR
because it is a public contract. A conversion helper, if wanted, is an extension method outside the
library.

**3. The reference's sticky `isNull` is not replicated, and the corpus witnesses both readings.** A
filter whose answer depends on which records were evaluated before it cannot be answered by an
optimizer that evaluates a subset — replicating it would make the full-scan equivalence gate
unsatisfiable. The generator therefore evaluates every field expression **twice** — once through one
compiled `EXPR4` across records, as today, and once **freshly parsed per record** — the same move as
011's depth 0. The port is gated against the fresh reading; the two readings are asserted to differ only
on expressions with a null operand, which makes the stickiness a witnessed fact instead of a source
reading. ADR, in the shape of ADR-53. No null filter is added to the query lists, because the
reference's own scan truth would be sticky too.

**4. ADR-23 is superseded, not rewritten.** A new ADR closes it, with the wrapper read from `C4CODE.C`.

**5. The foreign-alias refusal is a spec fact, not an ADR.** The port has no multi-table expressions;
−510 is unreachable and −210 is the answer. `EXPRESSIONS.md` says so, with the citation.

**6. A tag is created the way `i4create` creates it; `i4tagAdd`'s omissions are not ported (ADR-63).**
Decided by the user, 2026-10-31. Nothing is written in this step; the decision binds `WRITE`, and the
facts are in `CDX-FORMAT.md` §8.6. `original/source/` is not patched — the generator uses `i4create`,
the reference's own correct path, so the corpus still witnesses the reference.

**7. A whole-expression `ASCEND`/`DESCEND` tag is sought through its transform (ADR-64).** Decided by
the user, 2026-10-31, for machine and GENERAL tags alike. The reference never finds a `DESCEND` tag's
key by a plain value; the expectation is the stored keys of `TAGCTX` and `TAGGEN`.

## Open questions

1. Does the generator build unchanged with Visual Studio 2026 on this machine? `config.bat`'s first
   candidate exists; sub-step 1 answers it before anything depends on it.
2. Does enabling `S4COMPRESS` compile? It turns on code in ~20 files this repository has never built.
   **Compile half answered (2026-10-29):** a trial build outside the repository, with `S4OFF_COMPRESS`
   replaced by `S4COMPRESS_ZLIB` against the drop's own `zlib.h`/`zconf.h`, compiled all 137 TUs with no
   failure; `codebase.lib` gains `c4compress`/`c4uncompress` and imports `compress2`/`uncompress` —
   the only new link dependency. **Answered in full (same day):** with zlib 1.3.1 vendored, the clean
   build (137 + 10 zlib TUs) links, and the regeneration with no new case is byte-identical.
3. Which tag-context lengths does the port get wrong? C2's dump answers it.
4. Is `isNull` stickiness real? Decision 3's double evaluation answers it.
