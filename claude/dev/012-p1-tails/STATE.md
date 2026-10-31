# 012-p1-tails — state

**Status:** in progress — sub-steps 1–5c, 7 and 7a done
**Current sub-step:** PLAN.md step 6

## Done so far

- [x] step 1 — generator baseline on this machine (2026-10-29). Clean rebuild of `codebase.lib` (137 TUs,
  0 failures) and `testgen.exe` with Visual Studio 2026 (`e:\msdev\dev`, `config.bat`'s first
  candidate); the regenerated corpus is **byte-identical** to the committed one.
- [x] step 2 — A, `Seek(string)` refuses (2026-10-29). `TableTagCursor.SearchFor`'s `default:` throws
  `CodeBaseException(Info)` naming the tag, its kind and the overload that would work, before the
  buffer is touched. `SeekTypeGoldenTests`: all four text overloads over all 41 corpus tags, expected
  kind from the reference (table dump field type; expression dump compiled type; function table for
  `EMPTY(K_TEXT)`, the one expression the expression dump lacks); a refused seek leaves the cursor and
  a running typed search intact; the advice per kind. `FoxNumericTests` (layer 1, 23 cases).
  **Mutation:** the old branch restored → exactly the 19 non-character tags red, all 22 character
  tags green. ADR-58; `Table.Seek(string)` docs; 009's defect write-up marked fixed (items 3–4 open);
  `CachedReadGoldenTests` comment. Release 2491 + 873 green.
  - The plan's "expected values from `.cdx.dump.txt`" did not hold: the index dump records no key
    type. The three sources above replace it, all reference-written.
  - Also removed two stray `<summary>` blocks that preceded another member's own (`Table.Seek(string)`
    and `IndexFileReader.Invalidate`) — docgen would have taken the wrong brief.
- [x] step 2a — F, the engine's table list (2026-10-29). One lock in `CodeBaseEngine` around `open`
  and `closed`; tables opened and disposed outside it; `OpenTables` a snapshot; an open that loses the
  race with `Dispose` closes its table and throws `ObjectDisposedException`. Engine class docs state
  the threading split. `EngineTableListConcurrencyTests` (8 threads × 1000 open/close; close while
  others open). **Mutation:** `lock (new object())` in place of the gate → red in 8 runs of 8; fixed
  code green repeatedly, ~0.25 s.
- [x] step 2b — G, timed tests alone (2026-10-29). `TimingCollection` (parallelization disabled);
  `BlockCacheSpeedupTests` joins it. ADR-57 and `FOR-DEVELOPERS.md` amended. Eight consecutive full
  Release runs green: 2493 + 873.
- [x] step 3 — B, the query refusals (2026-10-29). `CDXCOLL` gains four query filters (no index change);
  new case `QRYGATE` (lone unique, filtered and GENERAL tags, each beside a plain control; 13 filters).
  Regeneration changed exactly the six declared files. **Finding: the reference uses GENERAL tags** —
  the refusal is compiled out (`d4defs.h:669-673`); six filters disagree with its own scan. The port
  keeps refusing them: ADR-59 (ADR-54 marked), `QUERY-OPTIMIZER.md` §3.4/§8/§11.1, `PORTING-PLAN.md`
  §8, `QueryTagScope`/`TagEligibility`/engine docs corrected (the scope docs still said `AllSeekable`
  adds collated tags, stale since ADR-54). Gates: `QRYGATE` in every decision gate, `CDXCOLL` in Gate A
  only; `CorpusQueryDump.CollatedTags`/`Comparable` exclude the six by rule; new
  `CollationDivergenceGoldenTests` (scan and stored order). `ARefusedTagDoesNotStopTheSearch` now uses
  `T_FILT`/`T_NUM`; stale comment in `QueryEquivalenceGoldenTests` fixed; 011 records corrected.
  **Mutations:** unique refusal off → `QRYGATE` red at every depth; filter refusal off → `CDXBASE`,
  `CDXEXPR`, `QRYGATE` red; collation refusal off → `CollationDivergenceGoldenTests` and `QRYGATE`'s tag
  match red. Each reddens a reference-backed gate, not only Gate A. 2493 + 937 green.
- [x] step 4 — C1, null operands (2026-10-30), the first sub-step under `DEV_APPROACH.md` §4's
  test-first and tool rules.
  - Generator: five `FIELDEXPRS` appended (`K_NNUM <> 0`, `# 0`, `.NOT.(K_NNUM = 0)`, `* 2`,
    `K_NNUM + 1 > 0`), so no earlier index moved; `dump-expr.cpp` evaluates each field expression a
    second time, parsed afresh per record, and writes a `fresh` line only where the bytes differ.
    Regeneration changed exactly `CDXEXPR.expr.dump.txt`: 77 → 82 expressions, and **fresh lines on
    exactly `.NOT.(K_NNUM = 0)` and `K_NNUM + 1 > 0`**, at every non-null record after record 1 —
    the sticky `isNull` witnessed, as the design predicted from the source.
  - Test first: the reader learnt `fresh` lines (`DumpExpression.FreshValues`, `FreshValueAt`), the
    value gate reads the fresh value, `TheReferenceCarriesANullForwardOnlyWhereAComputedNullIsRead`
    pins where the readings differ, plus `TheNegationOfAnAbsentAnswerIsAbsentAndFalse` (layer 1) and
    `NegatedNullFilterTests` (layer 2, `Table.Query` over `CDXEXPR`). Before the fix all three failed
    for the expected reason only: the gate on `.NOT.(K_NNUM = 0)` at records 1, 6, 12; the query
    returned those three records extra.
  - Port: `ExpressionEvaluator.Negate` — a null operand gives false and null (e4functi.c:2247-2261).
    The decomposer never optimizes a negation, so the evaluator is the only path.
  - **Mutation:** the old `!operands[0].Logical` restored (copy-aside, md5 checked) → exactly those
    three tests red, one per layer. `crap4csharp` on the changed file: `Negate` CC 2, 100%; `Apply`
    unchanged at CC 114, 87.7% (013's F3/F6).
  - Docs: ADR-61; `EXPRESSIONS.md` §6 (the `.NOT.` rule and the stickiness, cited); both READMEs.
    Release 2496 + 938 green.
- [x] step 5 — C2, the tag-context parse (2026-10-30).
  - Generator: `CDXEXPR.expr.dump.txt` gains `[tag context]`, seven expressions parsed with a real
    `TAG4FILE` — `CDXEXPR.T_NUM` (machine) and `CDXCOLL.C_GEN` (GENERAL, its table opened beside) —
    each rule beside a case it must leave alone. Regeneration changed exactly that file. The
    reference: bare `K_MEMO` → 240, `EMPTY(K_MEMO)`'s memo → 1024; under GENERAL `DESCEND`/`ASCEND
    (K_TEXT)` → 40 (key length 40 too, not 80), `K_TEXT` and `UPPER(K_TEXT)` → 20.
  - The design's "possibly none" did not hold: the port had no tag context at all and got **both**
    wrong. Where it shows: the resolved length is the width a collated search pads to
    (`TableTagCursor`, `TagProbe`), and the optimizer's tag match compares node lengths
    (QUERY-OPTIMIZER.md §3.3) — so a bare memo tag matched a memo term here, at 1024 against 1024,
    and could have answered from keys truncated to 240 bytes. The reference never matches it (1024
    against 240), and now neither does the port.
  - Port: `TagContext` (the collation's per-character add), `TypeResolver.Resolve(parsed, tag)`
    applying both rules, and `SeekConverter.For` passing `TagContext.For(collation)`.
  - Test first, against a stub that ignored the context: `TagContextGoldenTests` red on exactly the
    three rule cases, the four controls green; layer-1 `TypeResolverTests` (both rules, the nullable
    memo's 239 by rule — no corpus witness — and conversions of non-strings unchanged); component
    `SeekConverterTagContextTests` over hand-built headers for the wiring, which no corpus tag
    reaches. One layer-1 expectation of mine was wrong (`DESCEND(K_NUM)` is 12, not 8 — stored
    numeric text keeps its width); replaced by "the same as without a tag", nothing hand-typed.
  - **Mutations**, copy-aside, md5 checked: no context passed in `SeekConverter` → the two wiring
    tests only; memo not capped → golden + wiring + three layer-1; not widened → golden + wiring +
    two layer-1. `crap4csharp`: `AssignKeyConversionLength` 0% → 66.7% (013's F3), `TagContext.For`
    100%, `AssignStringLength` CC 38 at 100% (F6).
  - Docs: `EXPRESSIONS.md` §9.5 corrected (the 240 cap was described as a tag-time refusal; it is a
    resolution length) with the witnessed table, and §3 marks −510 unreachable until `EXPR-ALIAS`;
    both READMEs; the stale `MemoOperandLength` comment.
  - Also fixed: `EngineTableListConcurrencyTests.ClosingTheEngineWhileOthersOpen_LeavesNoTableOpen`
    (2a's) failed once in a full run — its `Thread.Sleep(5)` could pass before any thread opened a
    table, failing the precondition. It now closes the engine once the first table is open.
  - Release 2509 + 940 green.
- [x] step 5a — C2a, the corpus (2026-10-31), added at the user's call.
  - **Finding: the reference's `i4tagAdd` cannot write a GENERAL tag over `DESCEND`/`ASCEND` of a
    string.** The first try added `G_DESC` to `TAGCTX` with `i4tagAdd` under GENERAL; the generator
    failed reopening it (−310, E84901). `i4create` parses such an expression again once the collation
    is set (i4create.c:1010-1025); `i4tagAdd` does not (i4add.c:746-760), writes key length 20, and
    the open, which reads the collation before parsing, resolves 40. The comparison of the two paths
    also found a candidate stored as plain unique, `signature` 0, and two size/type checks skipped.
    `CDX-FORMAT.md` §8.6; ADR-63 (the writer follows `i4create` on every path, resolving the key
    length with the open path's own call); `PORTING-PLAN.md` `WRITE`. `original/source/` not patched,
    by decision — the corpus would witness our fix.
  - Two tables, every tag by `i4create`: `TAGCTX` (machine; `T_MEMO`, `T_NMEMO`, `T_TEXT`, `T_ASC`,
    `T_DESC`, six filters) and `TAGGEN` (GENERAL, cp1252, an accent and two expansions; `G_TEXT`,
    `G_ASC`, `G_DESC`, three filters over `ID` walked in each tag's order). Regeneration changed
    exactly the eleven declared files.
  - Generator fix: `dump-index.cpp` treated a nullable machine string tag as an identity transform
    for `[seeknext]`, but `d4seekN` puts `0x80` in front of the value (D4SEEK.C:47-57) — the recorded
    runs answered another question. Only `TAGCTX.T_NMEMO` was affected; no other dump moved.
  - Harness: `SeekTypeGoldenTests` took a memo tag's type from the field (`M`); the reference compiles
    it as `C` for its tag. Both tables in `CorpusLayoutTests`, the index, seek, seek-by-value and
    query-result/oracle/equivalence gates; `TAGCTX` in the optimizer gates too (its control maps;
    `LeafEvaluation`'s floor is 1 for it), `TAGGEN` not (no plan of its maps; `AFullyMapped`'s floor
    is 0 for it). Corpus-size pins moved.
  - Memo tags, against the port as it stood: all 24 keys of `T_MEMO`/`T_NMEMO`/`T_TEXT` rebuilt byte
    for byte (240 cut, `0x80` + 239, zero padding), and every query answer and tag-order walk agreed.
- [x] step 5b — C2a, `ASCEND`/`DESCEND` under a tag's collation (2026-10-31).
  - The port keyed a GENERAL tag over a conversion the reference's opposite way on both counts —
    converted without the collation, then collated the result again — and threw on the width; a
    string seek on any `DESCEND` tag never found, here as in the reference.
  - Test first: the key gate over both tables (40 + 24 keys), every record's text sought through
    `ConversionSearch` equal to its stored key, `SeekByValueGoldenTests` over the four conversion tags
    and both controls. Red: exactly 12 golden tests, all 5b's.
  - Port: `TagContext` carries collation and code page; `CompiledExpression.Tag`;
    `ExpressionEvaluator.KeyForm` collates a string under a collated tag (e4functi.c:3042-3062);
    `SeekConverter` keys a collated string tag with a conversion as `Character` (E4EXPR.C:671-684),
    requires the weights for it, and records `Conversion` for a whole-expression one;
    `ConversionSearch` (new); `TableTagCursor.SearchFor` uses it. ADR-64 (sought through the
    transform, machine and GENERAL alike); `EXPRESSIONS.md` §9.5 (evaluation, key build, seek,
    stored-key witness; the "no nullable memo under an index" line removed); `Table.Seek` docs.
  - Layer 1: `ConversionSearchTests` (prefix of whole; `DESCEND` = `ASCEND` complemented; cut to the
    operand), `SeekConverterTagContextTests` (kind, conversion, weights refusal on cp1251, and
    `VAL(DESCEND(K_TEXT))` stays numeric — a bug of mine caught before it landed),
    `ExpressionEvaluatorTests` (a conversion under GENERAL is `CollatedKey`'s output, under machine
    as without a tag).
  - **Mutations**, copy-aside, md5 checked: no collation in `KeyForm` → the `TAGGEN` key gate and the
    layer-1 evaluator test; collating again → the key gate, four seek-by-value, two seek-type, three
    layer-1; no complement in `ConversionSearch` → six golden over both tables, two layer-1.
  - `crap4csharp` (unit coverage): `KeyForm` 30.0 → 7.0 (0% → 93%); `SeekConverter.For` 4.3 → 2.0,
    its new `StoredKindOf` 8.0 at 100%; `ConversionSearch` all ≤ 4; `SearchFor` 23.9 → 35.2 (CC 8 →
    10, coverage 37%) — 013's F5, `TableTagCursor` has no unit layer.
  - Release 2534 + 1085 green.
- [x] step 5c — C2b, the key-length check at open (2026-10-31), added at the user's call.
  - **Finding while designing it:** a memo under GENERAL is resolved at half the cap — `info[0].len`
    and the expression's length divided by (1 + add) when at least 239, at create and at open
    (i4create.c:1029-1045, i4init.c:466-481). The port had no such rule, and with the check it would
    have refused a valid file. Witnessed: `TAGGEN.G_MEMO` key length 240 (expression 120),
    `G_NMEMO` 239.
  - Generator: `TAGGEN` gains `K_MEMO`, nullable `K_NMEMO`, `G_MEMO`, `G_NMEMO`. New case `TAGBAD`:
    `T_TEXT` by `i4create`, `G_DESC` over `DESCEND(K_TEXT)` added by `i4tagAdd` under GENERAL; the
    table is dumped with `autoOpen` off, and `TAGBAD.open.dump.txt` records `i4open`'s answer,
    `refused errorCode=-310` (the case fails if it ever opens). Regeneration changed exactly the
    declared files; `TAGGEN.query.dump.txt` did not move.
  - Test first: `KeyLengthGoldenTests` (every tag of the nine indexed tables resolves to its header's
    length; `TAGBAD.G_DESC` refused with the dump's code; `T_TEXT` and a query still serve), the key
    gate over `TAGGEN`'s 40 keys, layer 1 for the halving and for headers whose length disagrees.
    Red: exactly 5c's tests.
  - Port: `SeekConverter.RefuseIfTheKeyLengthDisagrees` — `ExpressionKey.Length` with the
    collation's add for a collated string, none for a conversion — `CodeBaseException(Index)` naming
    the expression; per tag at first use, so the table opens and a query leaves the tag out (the
    reference fails the whole index). `TypeResolver.HalveAMemoForTheCollation`.
  - Harness: `SeekTypeGoldenTests` finds tags by `.cdx.dump.txt`, not `.cdx`; `CachedReadGoldenTests`
    records a refused selection in both runs; unit headers in `SeekConverterTagContextTests` now
    carry their true key lengths.
  - **Mutations**, copy-aside, md5 checked: no check → the `TAGBAD` refusal and four layer-1; no
    halving → the `TAGGEN` key, key-length, two seek-type and query-equivalence gates, three layer-1.
  - `crap4csharp`: `RefuseIfTheKeyLengthDisagrees` 3.0, `HalveAMemoForTheCollation` 6.0 (91%),
    `StoredKindOf` 8.0, `For` 2.0.
  - Docs: `CDX-FORMAT.md` §3 (`keyLen` checked at open), `EXPRESSIONS.md` §9.5 (the halving row,
    the witness), both READMEs (the open dump). Release 2542 + 1122 green.
- [ ] step 6 — C3, case mapping by code page
- [x] step 7 — D, compressed memos (2026-10-29), done out of order at the user's call.
  - zlib 1.3.1 vendored in `test-files-generator/third_party/zlib/` (core library only; `madler/zlib`
    v1.3.1 release tarball, SHA-256 `9a93b2b7…df23`, matching zlib.net); `build-lib.bat` compiles it
    as C into `obj\zlib`, include path ahead of `original\source`; `cb-config.h` has
    `S4COMPRESS_ZLIB` in place of `S4OFF_COMPRESS`. Clean build 147 TUs; regeneration with no new
    case **byte-identical**.
  - Case `CMPMEMO` (`case-cmpmemo.cpp`): version `0x31`, `flags[1] = 1`, 8 records. The reference
    confirms the rule: 512 bytes stays type 1, 513 is type 3, incompressible 513 bytes stored as 528.
    Regeneration changed only the four new `CMPMEMO.*` files.
  - Port: `MemoReader` decompresses type 3 (`ZLibStream`), refuses a short entry, a prefix above
    `0x7FFFFFF0`, a bad zlib header, a length mismatch either way, a cut stream and a bad Adler-32;
    drops its `mayHaveCompressedMemos` parameter. XML docs on `MemoType.Compressed` and
    `Table.GetMemoLength/Bytes/Type`.
  - Tests: 8 layer-3 cases in `MemoReaderTests` (the two old refusal tests replaced); golden —
    `CorpusMemoDump`/`DumpMemoEntry` read the new dump, the record and memo gates take the expected
    type from it, `TheCompressedTable_HoldsEntriesTheReferenceStoredBothWays`; corpus table lists
    13→14, memo tables 6→7. 2466 + 826 green.
  - **Mutations:** inflating from the start of the payload instead of past the prefix → 3 golden
    failures; not decompressing at all → 2. Both restored.
  - Docs: ADR-55 (supersedes ADR-23, marked); `FPT-MEMO.md` §3.9 and open question 3 corrected;
    both READMEs (case row, `CMPMEMO` section, zlib section).
- [x] step 7a — E, compressed data refused at open (2026-10-29), added at the user's call.
  `DbfOpener.RefuseIfCompressed`, before the length check, only on `0x31`; `NotSupported`. Two
  layer-2 tests in `DbfOpenerTests` over real corpus headers with byte 14 set (`CMPMEMO` refused,
  `VFPTYPE` opens). **Mutations:** drop the version condition → the `0x30` test reddens; drop the
  call → the `0x31` test reddens. ADR-56; `PORTING-PLAN.md` §2.2; `CodeBaseEngine.OpenTable` docs.
- [ ] step 8 — close

## Notes for the next session

- Running the generator's `.bat` files from an agent shell: the environment sets
  `NoDefaultCurrentDirectoryInExePath=1`, so `cmd` does not look in the current directory —
  `build-gen.bat` is "not recognized" there. Call them as `.uild-gen.bat`; they call each other by
  absolute path and work unchanged.

- `.gitattributes` is new and uncommitted: `*.bat`/`*.cmd` are `eol=crlf` in the working tree. The
  batch files were LF, and `cmd.exe` resolves `call`/`goto` targets by byte offset assuming CRLF, so
  `build-gen.bat` ran with its lines cut apart and **silently left the old `testgen.exe` in place**.
  Index content is unchanged (all LF); only the working-tree files were re-checked-out.
- `FileSystemTests.cs` carries an uncommitted fix from before this step (case-insensitive filesystem).

## Deviations from the plan

- Sub-step 1 needed `.gitattributes`, which the plan did not foresee. `FOR-DEVELOPERS.md` gets a line
  at close.
- Sub-step 7 ran before 2–6, at the user's call.
- Sub-step 7: the plan had the golden test read each entry's on-disk type from the `.fpt` itself.
  Instead the generator writes `CMPMEMO.memo.dump.txt` — the reference's own reading with
  decompression off — so the expected type comes from the C library, not from the test parsing the
  format. One more declared corpus file.
- ADR-23 closed in place rather than at step 8, at the user's call: every live reference updated
  (`PORTING-PLAN.md` §2.2 and the `DBF-READ` row, root `STATE.md` §1 and §3, `vfp/README.md`).
  Past-tense records — 003's documents, `dev/README.md`'s 003 row, `STATE.md` §2, ADR-44/46 —
  stay as written.

- Sub-steps 5a, 5b and 5c were not in the plan; the user added them after 5 (DESIGN §C2a, §C2b,
  PLAN rows).
  5a's first shape — one table with `G_DESC` added by `i4tagAdd` — is what found the `i4tagAdd`
  defect, and became two tables built by `i4create`.

- Sub-step 5: the plan named the section `[tag rejections]` with a second table for a −510 witness.
  It is `[tag context]`, and −510 is not witnessed: the port cannot reach it until expressions can
  name another table, which the user scheduled as its own capability, `EXPR-ALIAS`, step 014
  (ADR-62). The second table is opened anyway, for its GENERAL tag. The counted query comparison
  moved to 015.

- Seen and not acted on: `FeatureFlags.UsesLongFieldNames` drives the long-name refusal
  (`FieldDescriptorTable.Parse`) on **any** version, though the C library reads the flags only on
  `0x31`; a `0x30` table with a stray byte 16 would be refused here and read there. Small; not this
  step's.
- `BlockCacheSpeedupTests.TheCacheMakesAWalkAtLeastTwiceAsFast` failed once in a full run and passed
  on rerun. **Acted on, at the user's call (ADR-57):** the suite is documented as `-c Release` in
  `FOR-DEVELOPERS.md` and this plan's gate; the two timed assertions skip themselves when the library
  is built with the JIT optimizer off; the floor is unchanged; `009/results-block-cache.txt` §3
  carries a dated correction. Release: 2468 + 826 green, 1 skip; Debug: the same with the two timed
  tests skipped. Findings:
  - `dotnet test` runs **Debug**, where the library's JIT optimisation is off and the cached path's
    CPU dominates. Measured here, natively, 1000-record passes: tag walk off 1 320 µs / on 557 µs =
    **2.35×**; Go+Skip 4.8×. Release: walk ~9–10×, Go+Skip ~17×. The 2× floor has ~17% headroom
    in Debug, which a loaded full run eats.
  - **Not a regression**: the same harness against a worktree at `197f646` (009's close) gives the
    same figures — Debug walk 2.30×, Release 10×.
  - The test's justification "7.4x Release, 11.6x Debug" (`009/results-block-cache.txt` §3): 7.4× was
    a Release harness figure; **11.6× matches no Debug measurement** — it is the seek prediction from
    `ANALYSIS.md` §5. The margin in Debug was never measured.
  - Under WSL the suite read through `/mnt/d` (9p, ~170 µs per read), so uncached passes were
    enormous and the ratio could not fail; native NTFS reads are ~1 µs.
  - Second, smaller effect: in Release inside the xUnit host, cached Go+Skip measured 431 µs against
    100 µs standalone — the 300 ms wall-clock warm-up does not always reach tier-1 code there (a
    standalone process showed the same on its first round, 490 µs).
  - Cached paths issue zero reads and zero `Length` calls (counted), so no syscall crept back in.

- **Found during sub-step 2, a real race — fixed in sub-step 2a.** `CodeBaseEngine.open` is a plain `List<Table>`
  mutated by `OpenTable` (add) and by a table's close (remove) with no lock. ADR-41 puts engine-shared
  state on the library to synchronise, and `SharedCacheConcurrencyTests` opens and closes tables of one
  engine from several threads; about one full run in six,
  `DroppingBlocksWhileOthersAreReadingChangesNoAnswer` fails with a `NullReferenceException` in
  `CodeBaseEngine.Dispose` (line 168) — the list was torn. `OpenTables` also hands out the live list.
- **The timed gate also failed once in Release** (fixed in sub-step 2b) in a full parallel run: Go+Skip 1.91×. ADR-57's
  "clears the floor several times over" holds in isolation, not under the suite's own parallel load.

## Blockers

None.
