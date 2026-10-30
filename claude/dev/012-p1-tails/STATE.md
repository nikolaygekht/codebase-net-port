# 012-p1-tails — state

**Status:** in progress — design and plan written, awaiting the user's review before sub-step 2
**Current sub-step:** PLAN.md step 2

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
- [ ] step 4 — C1, null operands
- [ ] step 5 — C2, the tag-context parse
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
