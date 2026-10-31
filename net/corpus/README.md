# Corpus — golden test files

Checked-in reference files produced by the original Sequiter CodeBase C library, with the
expected values dumped beside them. The C# port is tested against these; **building or
testing CodeBase.NET never compiles or runs C**, and needs neither Windows nor MSVC.

One exception, fenced off in [`vfp/`](#vfp-produced-cases--vfp): files produced by **Visual FoxPro
9** rather than by CodeBase, for the cases CodeBase cannot write.

Regenerate with [`test-files-generator/`](../../test-files-generator/README.md):

```bat
build-lib.bat & build-gen.bat & bin\testgen.exe & copy-corpus.bat
```

Never hand-edit these files, and never hand-write expected values in a test. If a code path
turns out to be untested, add a generator case and regenerate.

## Cases

| Files | Version | Covers |
|---|---|---|
| `DB3TYPE.DBF` | `0x03` | dBase III / FoxPro 2.x field set — `C`, `N`, `D`, `L`. No memo, no 263-byte reserved area. Same shape as the 40 dBase III tables in `original/examples/DATA/`. |
| `VFPTYPE.DBF` | `0x30` | Every non-memo Visual FoxPro type: `C N F D L I B Y T`. Exercises the 263-byte reserved area, 4-byte integers, 8-byte doubles, currency scaling and datetime (julian + ms) encodings. |
| `F2XMEMO.DBF` + `.fpt` | `0xF5` | FoxPro 2.x table with a memo: the **10-byte ASCII** in-record memo reference. |
| `VFPMEMO.DBF` + `.fpt` | `0x30` | VFP memo and binary types: `M` text memo, `X` binary memo, `G` general, `Z` binary character — the last three stored as `M`/`M`/`C` with descriptor flag `0x04`. **4-byte binary** memo references. |
| `VFPNULL.DBF` + `.fpt` | `0x30` | Nullable fields and the hidden `_NullFlags` bitmap. See below. |
| `CP1251.DBF` + `.fpt` | `0x30` | A marked code page, single-byte: header byte 29 = `0xC9`, Windows Cyrillic. High-byte text in the record and in the memo. See below. |
| `CP936.DBF` + `.fpt` | `0x30` | A marked code page, multi-byte: header byte 29 = `0x7A`, Simplified Chinese GBK. Trail bytes that look like ASCII, and characters cut in half at a field boundary. See below. |
| `CDXBASE.DBF` + `.cdx` | `0x30` | **Index, ten tags, one block each.** One tag per key shape: character with shared prefixes and blank keys, the same field descending, runs of duplicates, a unique tag, keys holding bytes below the pad character, numeric, double (including `-0.0`), date, integer, and a filtered tag. See below. |
| `CDXDEEP.DBF` + `.cdx` | `0x30` | **Index, multi-level trees.** 600 records and four tags; `D_WIDE` is three levels deep (55 leaves under two levels of branch), so interior nodes, sibling chains and full leaves are all reachable, and `D_PFX` is **descending** so a backwards walk and a descending seek cross block boundaries. See below. |
| `CDXTIME.DBF` + `.cdx` | `0x30` | **Index, datetime keys.** 256 datetimes over one `T` field, indexed ascending and descending. The `T` key is the only one that is not arithmetic: an empirical 86400-bit bitmap decides whether the computed double is nudged down before conversion (i4conv.c:1513-2191), and these values are chosen so roughly a third land on a set bit and the rest do not. Also the day's edges, leap and non-leap Februaries, month and year rollovers, a blank datetime, and a duplicate run. |
| `CDXEXPR.DBF` + `.cdx` + `.fpt` | `0x30` | **Index, expression keys.** 16 records and 22 tags, the corpus's first tags whose key is not a bare field. Ten are function *shapes* whose key rules differ (`UPPER`, `TRIM`, `SUBSTR`, both concat forms, `STR` over an int and over a numeric, `DTOS`, `DTOC`, `DESCEND`); five are one per result *type* a function can return (`VAL` → r4numDoub, `CTOD` → r4dateDoub, `EMPTY` → r4log, `K_FLT*1` → r4floatBin, `DATETIME` → r4dateTime); five are the types **no function returns** and so can only be bare fields (`N`, `D`, `I`, `Y`, `T`); one is over a nullable field, for the leading `0x80` indicator; one carries a `FOR` filter using `.AND.`, `$` and a function. It is also the only table with an `H` field, which `d4create` forces to 4 bytes. |
| `CDXCOLL.DBF` + `.cdx` | `0x30` | **Index, collation.** cp1252, one `C(20)` field indexed twice — machine (`keyLen` 20, pad `0x20`) and `GENERAL` (`keyLen` 40, pad `0x00`) — over accented text and the `œ`/`ß`/`þ` expansions. See below. |
| `IDXONE.DBF` + `.cdx` + `.IDX` | `0x30` | **Index, single-tag file.** The same 300-record tree in both shapes: a compound `.cdx` holding one tag, and the `.IDX` derived from it. See below. |
| `CMPMEMO.DBF` + `.fpt` | `0x31` | **Compressed memo entries**, a CodeBase extension Visual FoxPro cannot read. Created with `code4memoCompress` on, so the header carries `flags[1] = 1`; every entry longer than one block is stored as type 3 — a 4-byte length, then a zlib stream. Its `CMPMEMO.memo.dump.txt` says which. See below. |
| `QRYGATE.DBF` + `.cdx` | `0x30` | **Query refusals, isolated.** Three tags the reference's optimizer is written to refuse — unique `Q_UNIQ`, filtered `Q_FILT`, GENERAL `Q_GEN` — each the only tag over its own field, beside a control field holding the same values under a plain tag. Its query dump shows the unique and filtered refusals as `tag=-`, and shows the GENERAL one **used**, wrongly: that refusal is compiled out of every non-testing build (ADR-59). 24 records, cp1252. |
| `TAGCTX.DBF` + `.cdx` + `.fpt` | `0x30` | **Tags parsed in a tag's context, machine order.** `T_MEMO` over a memo that is the whole expression (resolved at 240, not 1024), `T_NMEMO` over a nullable memo (239 behind the null byte), `T_ASC`/`T_DESC` over `ASCEND`/`DESCEND(K_TEXT)`, and the plain `T_TEXT`. Every tag by `i4create`. Its query dump shows the reference never answering a memo term from a memo tag (1024 against 240). 8 records, cp1252. |
| `TAGGEN.DBF` + `.cdx` | `0x30` | **`ASCEND`/`DESCEND` under GENERAL.** `G_ASC` and `G_DESC` hold the collated key of the text and its complement, at key length 40; `G_TEXT` is the plain GENERAL control. Built by `i4create` because `i4tagAdd` writes such a tag with a key length the reference then refuses to open (`claude/specs/CDX-FORMAT.md` §8.6). Text with an accent and two expansions. Its queries are over `ID`, which no tag covers, and walk each tag in order. 8 records, cp1252. |

## Benchmark data — `benchmark/`

`benchmark/PERF10K.DBF` + `benchmark/PERF10K.cdx` are a **workload, not an expectation**: 10 000
records, a `C(20)` tag four levels deep (`T_NAME`) and an `N(12,2)` tag three levels deep (`T_AMT`).
They exist so the read path can be measured against a table large enough to matter — `CDXDEEP`'s 55
leaves cannot make a block cache evict anything — and they are the table step 009's performance audit
took its figures from, so a number printed today is comparable with the ones in
[`claude/dev/009-performance-audit/SUMMARY.md`](../../claude/dev/009-performance-audit/SUMMARY.md).

**It has no `.dump.txt`, and it must not acquire one.** Nothing asserts against its bytes, only
against how fast they are read. A dump would be a multi-megabyte expectation nobody reads, and an
invitation to start asserting against a table whose whole purpose is to be large. Two tests in
`CorpusLayoutTests` pin that: the files exist, and the dump does not.

**It is one directory down for a mechanical reason.** `Corpus.TableNames` discovers tables by
enumerating this directory, non-recursively, and twelve golden test classes iterate what it finds. A
dumpless table in the corpus root would be swept into all of them. The subdirectory keeps it out of
their way, and the benchmark reaches it through `Corpus.BenchmarkPathOf` instead.

The field and tag shapes, the row count and the scrambling stride are fixed by that audit's
measurement rather than free to tidy: change them and its numbers stop being a baseline.

## VFP-produced cases — `vfp/`

**Two files landed on 2026-08-29**, from the first script; the rest of this section is the
convention they follow.

| Files | Covers |
|---|---|
| `VFPFUNC.DBF` + `.CDX` | 16 records and **18 tag groups, of which 17 are live tags** — one per function CodeBase refuses (ADR-43), plus `TRIM`, `STR` and `PROPER` as divergence probes and `UPPER` as a control. The tag headers carry VFP's own **key lengths**, which is what the case exists for; the eighteenth group is a tag whose build failed and whose blocks VFP left behind |
| `VFPFVAL.DBF` | 77 constant expressions, each result stored in a field **of its own type**, so a numeric answer is the bits VFP computed rather than a formatting of them. `RESLEN` carries a character result's true length, because the `C(80)` field pads; `CBNOTE` carries CodeBase's answer beside VFP's |

What they settled is in
[`test-files-generator/vfp/README.md`](../../test-files-generator/vfp/README.md); the durable form is
`specs/EXPRESSIONS.md` §9.1 and `specs/CDX-FORMAT.md` §3. **Both tables carry code-page mark `0xC9`
(cp1251)**, the VM's own, where the rest of this corpus leaves byte 29 at `0x00` — ordering is
unaffected under `MACHINE`, but case conversion above `0x7F` is that code page's.

`vfp/` holds files produced by **Visual FoxPro 9**, in a VM, by the scripts in
[`test-files-generator/vfp/`](../../test-files-generator/vfp/README.md) — **ADR-44**. They exist for
the cases the C generator cannot produce, because that generator *is* CodeBase: an index whose tags
use the thirteen functions CodeBase refuses (ADR-43), code-page marks beyond its five constants, a
VFP 9 `0x32` table with a memo, `V`/`Q`/`W` fields, a DBC backlink, a tree built by insert-and-split.

**Three things are different about them, and each is deliberate.**

*The file is the expectation, not a dump beside it.* CodeBase cannot dump a VFP-built CDX at all —
opening a tag parses its expression (`i4ntag.c:1390-1395`), so a tag over `LOWER(…)` never opens.
The `*.vfp.dump.txt` files are provenance and review material, and say so wherever a number in them
is a formatting of stored bits rather than the bits.

*They carry VFP provenance, which the rest of this corpus explicitly does not* (see "Three things to
know before comparing bytes"). A **trailing `0x1A`** is present, because VFP writes one on a `0x30`
file and CodeBase does not — that byte is a reader-tolerance case, not a defect to normalise away.
The header date stamp is frozen to `26-01-01` exactly as elsewhere (ADR-07), but the `.cdx`
tag-directory version counter is VFP's own and may move between runs.

*They are one directory down for the same mechanical reason as `benchmark/`*: `Corpus.TableNames`
enumerates the corpus root non-recursively, and a table with no `<NAME>.dump.txt` beside it would be
swept into every data-driven golden test. The gates that want these name them.

## Record counts

The seven memo and type tables hold **32 records** each; rows 1-3 carry the edge cases (zero,
minimum/maximum, blank/empty). The index tables hold 32, 600, 32 and 300 respectively — depth needs
records, and a wide key needs fewer of them. `CMPMEMO` holds 8, one per payload length that matters,
`QRYGATE` 24, and `TAGCTX` and `TAGGEN` 8 each.

### What `CMPMEMO` pins down

The compression rule and the stored layout, from the reference writing them. Payload lengths are 0,
1, 511, **512** and **513** — the block size, which stays type 1, and one past it, the first type 3 —
then 4096 and 66000 (whose length prefix needs 17 bits) of compressible text, and pseudo-random
binary that **grows** when compressed and is stored as type 3 anyway (513 bytes stored as 528).

The ordinary dump records every memo **decompressed**, because that is what a reader sees, so it
cannot say how an entry was stored. `CMPMEMO.memo.dump.txt` can: the generator reads each entry back
through `memo4fileReadPart` with decompression off and writes one line per memo field per record —
`rec 5 NOTES block=10 type=3 stored=69` — the block, the type on disk and the length on disk. A
record with no memo shows `block=0 type=0 stored=0`. Only this table has one.

`CMPMEMO.fpt`'s compressed bytes are what zlib 1.3.1 produced; see the generator README.

`CDXBASE`, `CDXDEEP`, `CDXCOLL`, `CDXTIME`, `IDXONE`, `QRYGATE`, `TAGCTX` and `TAGGEN` are the tables whose header byte 28 carries the
production-index bit (`0x01`), which `i4create` sets when the index file is the table's own
(`i4create.c:1404-1418`).

### What `VFPNULL` pins down

Ten nullable fields, interleaved with three plain ones, so **the null-bit ordinal is not the field
index** — `N_I` is field 8 and null bit 5. Ten bits make `_NullFlags` two bytes wide, so bits 8 and
9 land in the second byte. Records 1-6 are hand-picked bitmaps (all set `FF 03`, none set `00 00`,
alternating `55 01`, each byte alone); the rest follow a rolling pattern.

Three facts a reader would otherwise have to discover the hard way:

- **The file has one more descriptor than the API has fields.** `_NullFlags` (type `'0'`, flags
  `0x05`) is written after every user field, but `d4numFields` subtracts it (`d4declar.h:594`), so
  `[descriptors]` lists 14 and `[fields]` lists 13.
- **Nulling a field does not blank its bytes.** Every field is assigned a value before the nulls are
  applied, and the assigned bytes are still there — `N_C "ALPHA     " null=1`.
- **A memo that holds a block reference is never null.** Flushing memo content at append time writes
  the new block id with `f4assignLong` (`f4memo.c:801-807`), which clears the null bit. Rows that
  null the memo therefore leave it empty; **record 7 is the deliberate exception** — it asks for a
  null memo *and* writes 40 bytes of content, and the resulting bitmap is `01 00`, with bit 9 clear.

Memo payload lengths cycle through `0, 1, 7, 63, 200, 503, 504, 505` bytes: 504 is exactly one
512-byte FPT block once the 8-byte block header is added, and 505 is the first length that needs
two blocks.

### What `CP1251` and `CP936` pin down

Both are version `0x30` with a memo, and both hold `ID` `TEXT` … `MEMO`, so the only thing that
distinguishes them from `VFPMEMO` is the text they carry and the code page they name. Header byte 29
is the point: every other table leaves it `0x00`, so a reader that ignored it outright would pass the
rest of the suite.

**Neither marker is one CodeBase will set.** `c4setCodePage` accepts only cp0/437/850/1252/1250 and
raises `e4parm` on anything else, but `d4create` writes `CODE4.codePage` into the header verbatim
with no validation (`D4CREATE.C:1391`) and `d4open` reads it straight back (`D4OPEN.C:2217`). The
generator assigns the field directly. `0xC9` and `0x7A` are what Visual FoxPro stamps on Cyrillic and
Simplified Chinese tables, so the files stay realistic — see ADR-18 for why bytes the C library
refuses to set are the right ones to gate against.

`CP1251` is the single-byte half. Its `SWEEP` field walks `0x80-0xFF` whole across records 1-8,
sixteen bytes at a time, **including `0x98`** — the one byte cp1251 leaves undefined, so what a
reader makes of it is a decision and not an accident. `EXACT` is filled to its width and `SHORT`
never is, so blank padding beside high-byte content is gated in both directions, and the memo carries
the same high bytes so the FPT path is not left to the ASCII cases.

`CP936` is the multi-byte half, and it is the one that breaks byte-wise reasoning. GBK trail bytes
are `0x40-0xFE`, overlapping ASCII:

- **`TRAIL` holds characters whose second byte is `\`, `|`, `A`, `~` or `@`.** In the dump they show
  up as those literal characters inside `"\x81\\\x81|\x81A\x81~\x81@\x82\\"`. Anything scanning a
  character field byte-wise — for a path separator, a delimiter, a quote — finds them.
- **`CUT` is seven bytes wide and is assigned eight bytes of text**, so its last byte is always a
  lead byte with nothing behind it. A field width is a byte count, `f4assignN` truncates
  (`F4STR.C:155-168`), and Visual FoxPro produces exactly this. `EXACT` is the opposite case: four
  characters filling eight bytes with no padding at all.
- **Memo payload lengths 63 and 401 are odd**, so those payloads end mid-character too — the same cut
  on the FPT path instead of in the record.

Both marks are defined by **Visual FoxPro's** documentation — `0xC9` is 1251 and `0x7A` is 936, in the
26-mark table recorded in `DBF-FORMAT.md` §8.1, which ADR-19 makes the authority for this byte. The
port resolves both: these two tables report `CodePage.Cp1251`/`Cp936` and `CodePageNumber` 1251/936,
gated in `TableMetadataGoldenTests`. What is still missing is the last link — decoding a field's bytes
to a string — which is step 002, and these two dumps are the gate for it.

### What the index cases pin down

**`CDXBASE` is the readable one.** Ten tags over eight fields, 32 records, every tree a single
root-and-leaf block, so the whole dump can be read by a human. Between them the tags cover: duplicate
counts from 0 to 14 and trail counts from 0 to the whole key; two blank keys adjacent, which is the
invariant that a key of all pad has `dup=0` however its neighbour looks; two pairs of equal keys, whose
order is then the record number; a key filling its field exactly; keys carrying `0x00`, `0x1F` and
`0x80`-`0xFF`, so only an unsigned comparison orders them; `-0.0`, which keys to eight zero bytes and
sorts below every negative; a blank date; `LONG_MIN` and `LONG_MAX`; a unique tag holding 5 keys where
the table has 32 records; and a filtered tag holding 22.

**`CDXDEEP` is where interior nodes live.** Depth is bought with key *width* rather than record count:
`D_WIDE` is a deliberately incompressible `C(40)`, so a leaf holds about eleven keys and 600 records
need 55 leaves, six branches and a root — **three levels**. `D_PFX` is the opposite extreme, a `C(20)`
with a fourteen-byte shared prefix packing 114 keys into a leaf with 3 bytes to spare, and it is
**descending** — which costs it nothing, because keys are stored ascending whatever the flag says, and
makes it the only multi-block descending tag in the corpus. Without one, a descending seek's step back
into the *previous block* is unreachable: `CDXBASE`'s `T_TEXTD` is a single block. `D_DUP` has ten
distinct values over 600 records, so runs of equal keys cross leaf boundaries and interior entries end
up with keys equal to each other; one of its leaves has `freeSpace` exactly 0.

Turning that flag on is also byte-level proof of a specification claim: the whole of `CDXDEEP.cdx`
changed in **one byte** — offset 2550, `D_PFX`'s `descending` short — so "keys are physically stored
ascending; the flag only inverts traversal" (`CDX-FORMAT.md` §7) is witnessed and not merely sourced.

**`IDXONE` is one tree in two file shapes.** `IDXONE.cdx` is an ordinary compound file holding a single
tag; `IDXONE.IDX` is derived from it by copying the tag header from offset 1024 to offset 0 and
clearing the compound bit (`0x60` → `0x20`). Nothing else moves, so every node number stays valid and
the two files differ in 1025 bytes. The C library **cannot write** a single-tag file
(`i4create.c:847`) — and **cannot `d4check` one either**, because its block accounting flags the tag
directory's header and then every tag's header, which in such a file is the same block
(`i4check.c:889-914`). So this case is witnessed differently: the generator walks both files and
requires the same 300 keys and record numbers from each. See ADR-25.

## The expression dumps

Two files that are not table dumps, written for the expression engine (step `010-expr`), and three
more for the query optimizer (step `011-query-optimizer`):

* **`FUNCTIONS.dump.txt`** — the reference library's own `v4functions[193]`, printed from the array:
  per row, its name, code, name length, priority, parameter count, return type and parameter types.
  It depends on no table. The port's own function table is compared against it row for row, so a
  transcription slip is a failing test rather than a parse difference discovered later.
* **`CDXEXPR.expr.dump.txt`** — what the C library's parser and evaluator answer, in six sections:
  `[constant expressions]` (131), which reference no field and so have no record dimension;
  `[field expressions]` (82), evaluated at every record, for the types no literal can express
  (`r4num`, `r4date`, `r4int`, `r4currency`, a memo, and null); `[clock-dependent expressions]` (4),
  recorded for **shape only** because a value would change on every run;
  `[rejections]` (18), which records for each malformed string whether the parser accepted it and
  with which `error4code`; and `[vfp-only]` (13), the names Visual FoxPro has that the reference
  refuses, which this port accepts (ADR-43) and which are therefore **not** an expectation; and
  `[tag context]` (7), expressions parsed **for a tag** — `CDXEXPR`'s machine-order `T_NUM`, and
  `CDXCOLL`'s GENERAL `C_GEN`, borrowed by opening that table beside this one — recorded for shape
  only, because a tag changes lengths and no value: a bare memo is capped at the key size, and
  `DESCEND`/`ASCEND` of a string is widened by a GENERAL collation (`EXPRESSIONS.md` §9.5).

  **The expression lists are sized by row coverage, not by taste.** Of the 193 rows of
  `v4functions`, 144 are reached by some expression here and the other 49 cannot be reached at all —
  39 name a field type this library does not open, 6 are the currency comparisons that are dead in
  the reference itself because promotion beats them, and 4 are the report writer's and the
  terminator. A second pass on 2026-09-03 added 46 expressions for rows nothing had reached, which
  is why the constant and field counts are what they are. Adding an expression changes this dump and
  nothing else in the corpus; adding a *tag* would rewrite the index.

  Each expression carries its **compiled node array** — per node the row index of `v4functions` that
  overload resolution chose, plus its length, parameter count and field number — because a missing
  overload row does not fail loudly, it selects the neighbouring row and returns a plausible value.
  Values are the **raw result bytes**, escaped by the same rules as a key: a numeric result is a
  double, and printing it as text would hide the low bits the key transform depends on.

  **A field expression is evaluated twice** (ADR-61): through one compiled `EXPR4` across the
  records, as the `rec` lines record, and parsed afresh for each record. A `fresh` line follows
  wherever the second reading gives other bytes, because the reference never clears a computed
  node's null flag and so carries a null forward from an earlier record. No `fresh` line means the
  two agree; they differ on exactly `.NOT.(K_NNUM = 0)` and `K_NNUM + 1 > 0`, the two of the five
  null-operand expressions added on 2026-10-30 where a parent reads a computed child's null. The
  port is gated on the fresh reading.

* **`CDXEXPR.query.dump.txt` (54 filters), `CDXBASE.query.dump.txt` (15),
  `CDXDEEP.query.dump.txt` (12), `QRYGATE.query.dump.txt` (13), `CDXCOLL.query.dump.txt` (4),
  `TAGCTX.query.dump.txt` (6) and `TAGGEN.query.dump.txt` (3)** — what the reference's **query optimizer** decided about a filter,
  written by `dump-query.cpp`. The optimizer produces no bytes, so it cannot be gated the way a
  format is; what it produces is decisions, and `RELATE4`/`RELATION4` are public structs, so the
  decisions are read out directly rather than inferred from the records that come out at the end.
  Per filter: the compiled node array with **the tag the optimizer matched to each node**,
  `fullyMapped`, `bitmapsFreed`, the **candidate bitset before any residual filtering** (taken
  immediately after the query is prepared *and* again after the walk, so the file itself says whether
  the set survives being consumed — it does), the records returned in record-number order, the same
  in tag order with a tag selected, `relate4count`, and an `agree` verdict.

  **Every filter is run twice**, the second time with `RELATION4.bitmapDisable` set, which takes the
  optimizer out of the circuit entirely (`m4map.c:341`) and leaves the reference evaluating the
  filter per record. That second reading is the reference's own **scan truth**, and it is what makes
  the first usable: the reference has a defect that inverts a range bound on a reflected comparison
  nested inside a connective, so on such a filter a correct port is *expected* to disagree with the
  optimized answer. Rather than a hand-written exception list, each case carries `agree yes|no` and
  the expectation follows from it. **Seven of the 98 filters disagree.** One is
  `K_INT > 0 .AND. 4 > K_NUM`, whose two readings are *disjoint* — the case written to provoke the
  defect. The other six are the reference answering with a **GENERAL** tag: its refusal is compiled
  out (`d4defs.h:669-673`), so every filter over `CDXCOLL` and `QRYGATE`'s two GENERAL filters come
  out wrong. The port refuses those tags (ADR-59), and the decision gates skip those six by a rule
  read from the dumps.

  Two things to know before reading the bits. The bitset is often stored as its **complement**
  (`isFlip=1`), because the reference flags the complement whenever a range covers more than half a
  tag — 599 of `CDXDEEP`'s 600 records are stored as one raw bit — so a comparison must read the bits
  *through* the flag. And a filter that optimizes nothing has **no bitset** (`set absent`) while
  `bitmapsFreed` stays **0**: that flag means bitmapping *failed*, not that there is no bitmap, and
  no corpus filter sets it.

  The C's `resultPos` and `i1` are deliberately absent — positions in its evaluation stack and
  constant pool, artefacts of a stack machine that returns pointers.

## The index dump

`<NAME>.cdx.dump.txt` accompanies each index file (`<NAME>.idx.dump.txt` for the `.IDX`). It is a
sibling of the table's dump rather than more sections inside it — ADR-24, which also explains the rule
that matters most here: **every value comes from the C library's own structures, never from a re-read
of the bytes.** Keys and record numbers come from its navigation (`tfile4key`, `tfile4recNo`), block
structure from the live block object at `tfile4block`, and the per-entry counts from its own
`x4dupCnt`/`x4trailCnt`/`x4recNo` macros. A bit-packed leaf is the highest-risk decode in the port, and
a generator that unpacked it here would prove only that our writer and our reader misunderstand the
format the same way.

```
file         CDXBASE.cdx          the index file
table        CDXBASE.DBF          the table it belongs to
shape        compound             or single-tag
blockSize    512                  multiplier 1 unless the CodeBase note says otherwise
check        ok                   d4check, or skipped-single-tag with the reason above

[tag *directory*]                 the hidden tag-name tree, read as the tag it is
header       keyLen=… typeCode=0x… signature=0x… descending=… pChar=0x… root=… freeList=…
             version=… headerNode=…
text         exprPos=… exprLen=… filterPos=… filterLen=… sortSeq="GENERAL"
expr         "K_TEXT"
filter       ""
count        10                   keys in the tag
[blocks]                          every block, in the order a full walk reached it
node=… attr=… nKeys=… left=… right=… leaf=1 freeSpace=… recNumLen=… dupCntLen=…
       trailCntLen=… infoLen=… recNumMask=0x… dupByteCnt=0x… trailByteCnt=0x…
  0 rec=… dup=… trail=…           one line per packed leaf entry
  …                               an interior block instead lists: i child=… rec=… key="…"
blocks 1                          how many blocks were listed
[keys]                            navigation order — reversed for a descending tag
  "ALPHA               " 5
```

Two things to know when comparing against it. `left` and `right` are printed as unsigned decimal with
no interpretation, so "no sibling" reads as `4294967295`. And the `[blocks]` order is documentation
rather than a promise — a port may read blocks in any order, so the golden tests compare them as a set
keyed by node number.

### The seek sections

Each tag also carries what the C library answered when asked to *find* things. The search values are
**derived from that tag's own keys** by the generator rather than listed by hand, so they stay meaningful
when a case's data is retuned; the name on each line says how it was derived.

```
[seeks]                              tfile4seek: 10 to 12 cases per tag
  first-key   "ALPHA   " 8 rc=0 rec=1 key="ALPHA   "
  prefix-half "AL" 2 rc=0 rec=1 key="ALPHA   "
  above-all   "\xFF\xFF…" 8 rc=2 eof
[seeknext]                           d4seekN then d4seekNextN, as record numbers
  middle-key  "GREEN     " 10 seek=0 -> 2 8 14 20 26 32
  between     "GREEN    !" 10 seek=2 -> none
```

- `rc` is `tfile4seek`'s own return: 0 found, 2 landed on a greater key, 3 nothing at or after. It is
  recorded **beside** whether the cursor ended past the end, because the two are not equivalent — the
  library can return 2 with the cursor already at the end, and its data-file wrapper normalises that to 3.
- `[seeknext]` exists only for the eleven tags whose key transform is the identity, which is a
  machine-collated character tag: driving `d4seekNext` needs a *value*, and only there are the value
  bytes the key bytes. A tag without one says `[seeknext]   not-identity-transform`, so its absence is a
  statement rather than an omission.
- **One case is deliberately not comparable between the two sections.** A zero-length value means
  "matches everything" at the tag level and "seek for .NULL." through the data file's API
  (`data4seekConvertKeyToTagFormat`, `D4SEEK.C:951-956`), so `empty` shows `rc=0` in one and `seek=2` in
  the other. The dump keeps both because the difference is a fact about the library.

## Three things to know before comparing bytes

**The header date stamp is frozen.** DBF bytes 1-3 are the "last update" date, which would
otherwise change on every regeneration. The generator overwrites them with `26-01-01`
(2026-01-01) after closing each table. It is the only place the generator alters what the C
library wrote, and it makes these files byte-stable.

**The memo extension is lower-case `.fpt`** while the table is `.DBF` — that is what CodeBase
writes (`d4defs.h:2589-2598`). **The production index is lower-case `.cdx` for the same reason.**
Harmless on Windows; a port running on Linux must resolve the companion file case-insensitively. The
derived `IDXONE.IDX` is upper-case because the generator, not CodeBase, names it.

**These files carry CodeBase provenance, not Visual FoxPro's.** They are written in genuine-VFP
shape (`flags[8]` and `autoIncrementVal` zero, no trailing `0x1A`, no CodeBase extensions), but
they were not produced by VFP. `codePage` is `0x00` (unmarked) in every case but `CP1251` and
`CP936`, because unmarked is what CodeBase defaults to.

## Dump format

`<NAME>.dump.txt` accompanies each table. LF line endings, plain text, meant to be diffed in
review. Sections:

- header fields read **raw from the file** (version, frozen stamp, `numRecs`, `headerLen`,
  `recordLen`, `hasMdxMemo`, `codePage`);
- `[descriptors]` — the 32-byte field descriptors **as stored on disk**. This is the
  authoritative view: `d4create` rewrites `X`→`M` and `Z`→`C` and records "binary" in the flags
  byte, so the stored type differs from the creation type;
- `[fields]` — what the C library reports after reopening, which is where the `X`/`Z` creation
  types reappear;
- `[records]` — per record, the raw in-record bytes of every field (printable ASCII verbatim,
  everything else `\xHH`), plus a decoded value for numeric, integer, date and datetime fields.
  Memo fields show the in-record reference (`ref=`) and then the memo contents.

Three tokens appear **only when they are true**, so a table without nullable fields dumps exactly as
it did before they existed:

| Token | Where | Meaning |
|---|---|---|
| `nullable=1` | `[fields]` line | the field accepts nulls (`f4nullable`) |
| `null=1` | `[records]` field line | the field is null in this record (`f4null`) |
| `_NULLFLAGS "…"` | last line of each record | the raw `_NullFlags` bitmap bytes. The field is not in `[fields]`, so without this line the bitmap layout would never be gated against stored bytes — only through the `null=1` flags. |

The name is upper-cased here (`_NULLFLAGS`) but stored mixed-case in the descriptor (`_NullFlags`):
CodeBase upper-cases every field name on open (`c4upper`, `D4OPEN.C:316`) while matching the system
field against the stored bytes case-sensitively (`D4OPEN.C:2611`).
