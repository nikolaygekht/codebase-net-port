# 012-p1-tails — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | `FoxNumeric` on unparseable, over-long, over-precise and sign-only text (A); `CaseMap` against its own definition for every single-byte code page the 26 marks name, plus one multi-byte page, plus idempotence (`map(map(b)) == map(b)`) and "ASCII is untouched except a–z" (C3); `.NOT.` over a null operand (C1) | the rule itself, stated without a file: a case map that is wrong for one code page, a `.NOT.` that ignores null |
| 2 Component | `Table.Seek(string)` and its three siblings on a numeric, date, integer and logical tag throw and leave the cursor where it was (A); a filter `.NOT.(K_NNUM = 0)` through `Table.Query` (C1) | that the refusal reaches the public surface and changes no state |
| 3 Fault injection | `MemoReader` over hand-built type-3 images: wrong zlib header, a length prefix above `0x7FFFFFF0`, a prefix that disagrees with the inflated length, a truncated stream, a corrupted Adler-32 trailer, a payload shorter than 4 bytes (D) | the paths no valid file reaches. Images are **input**; the expectation is a refusal, never bytes |
| 4 Golden / corpus | every tag of every corpus table refuses or accepts a string seek by its key kind (A); `CDXCOLL` and `QRYGATE` through every reference-backed query gate (B); the new `CDXEXPR` field expressions and the fresh-per-record reading (C1, Decision 3); the tag-context section (C2); `VFPFUNC.CDX`'s `T_UPPER`/`T_LOWER` keys byte for byte and the lifted `LOWER` exclusion (C3); `CMPMEMO`'s every memo through the ordinary record gate, plus the on-disk type of each entry read from the `.fpt` (D) | whether we match what a real engine wrote |

**Corpus coverage.** Every part is gated on a corpus case except C3's general rule, which has no oracle
by decision (`DESIGN.md` Decision 1) — its layer-1 derivation stands in, and the VFP oracle gates the
one code page where host and table agree. `CMPMEMO`'s dump does not record an entry's on-disk type, so
the golden test reads it from the `.fpt` header bytes to prove the compressed path was actually taken.

**Expected values.** A: the corpus's own tag kinds, read from `.cdx.dump.txt`. B: the reference's query
dumps. C1/C2: `CDXEXPR.expr.dump.txt`. C3: the derivation from `Encoding` + Unicode (layer 1) and
`vfp/VFPFUNC.CDX`'s stored keys (layer 4). D: `CMPMEMO.dump.txt`, which the C library wrote by
decompressing its own entries.

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **Generator baseline on this machine.** Build `codebase.lib` and `testgen.exe` with Visual Studio 2026, run, `copy-corpus.bat`. Record the toolchain in `FOR-DEVELOPERS.md` and the generator README if it differs | `git status` shows **no** corpus change — the regeneration is byte-identical before anything is added |
| 2 | **A — `Seek(string)` refuses.** `SearchFor`'s `default:` throws; `FoxNumeric` layer-1 tests; `CachedReadGoldenTests`' recorded outcomes move from `Ok`/`NoRecord`/`ArgumentOutOfRangeException` to the refusal; ADR for Decision 2 | layer-4 sweep over every tag green; **mutation**: restore the `default:` branch and the sweep reddens on every non-character tag |
| 2a | **F — the engine's table list.** A lock around `open`; `OpenTables` a snapshot; an open racing `Dispose` refused | the stress test green repeatedly; **mutation**: no lock → it fails |
| 2b | **G — timed tests alone.** `BlockCacheSpeedupTests` in a non-parallel collection | full Release runs repeated without a timing failure |
| 3 | **B — the query refusals.** `CDXCOLL` gains its query list; new case `QRYGATE`; regenerate; add both to the reference-backed gates and `QRYGATE` to Gate A and `CorpusLayoutTests`; correct `QUERY-OPTIMIZER.md` §3.4, 011's `SUMMARY.md`, `ARefusedTagDoesNotStopTheSearch`, and the stale ADR-52 comment in `QueryEquivalenceGoldenTests` | depths 1–4 green on both tables; **three mutations** on `TagEligibility.Allows`, one per refusal, and each reddens a *reference-backed* gate, not only Gate A |
| 4 | **C1 — null operands.** Generator: the new field expressions and the fresh-per-record second evaluation; regenerate. Port: `.NOT.` honours null. Spec: `EXPRESSIONS.md` §6 gains the `.NOT.` rule and the stickiness finding; ADR for Decision 3 | the value gate green against the fresh reading; the sticky and fresh readings differ **only** on null-operand expressions; **mutation**: revert `.NOT.` and the gate reddens on `.NOT.(K_NNUM = 0)` |
| 5 | **C2 — the tag-context parse.** Generator: a `[tag rejections]` section parsing with a real `TAG4FILE` and a second open table; regenerate. Port: fix whichever lengths it gets wrong; `EXPRESSIONS.md` records −510 as unreachable here, with the citation | the section's rows green; a mutation for each length rule fixed |
| 6 | **C3 — case mapping by code page.** `CaseMap`; `EvaluationContext` carries it; `UPPER`/`LOWER` use it. ADR for Decision 1; XML docs on `Table.Query` and the seek family; `README.md`; correct `EXPRESSIONS.md` §7.1, §7.5, §14.1 and 010's `SUMMARY.md` | layer 1 green on every code page; `VFPFUNC` keys byte for byte; every existing `UPPER` golden still green; **mutation**: an ASCII-only map reddens the VFP gate |
| 7 | **D — compressed memos.** Vendor zlib under `test-files-generator/third_party/zlib/` with its licence; build it into `codebase.lib`; `cb-config.h` drops `S4OFF_COMPRESS` and adds `S4COMPRESS_ZLIB`; **regenerate first with no new case** and confirm byte-identical; then case `CMPMEMO`; port: `MemoReader` inflates. New ADR superseding ADR-23; correct `FPT-MEMO.md` §3.9 | byte-identical regeneration before the case lands; `CMPMEMO` green; layer 3 green; **mutation**: skip the 4-byte prefix and the gate reddens |
| 7a | **E — compressed data refused at open.** One check in `DbfOpener.Open`, ahead of the file-length check; ADR; `PORTING-PLAN.md` §2.2 | layer 2 green; **mutation**: drop the version condition and the `0x30` test reddens; drop the check and the `0x31` test reddens |
| 8 | **Close.** `SUMMARY.md`; `PORTING-PLAN.md` §5 (and `CORPUS`'s stale row); `claude/dev/README.md`; `net/corpus/README.md` and `test-files-generator/README.md` for three cases; root `STATE.md` | the gate |

Sub-steps 2–7 are independent of each other once 1 is green, and each lands as its own commit.

## Gate

```bat
cd test-files-generator && build-lib.bat && build-gen.bat && bin\testgen.exe && copy-corpus.bat
git status --short net/corpus      &:: only the files DESIGN.md §Corpus changes declares
cd ..\net && dotnet test CodeBase.Net.sln -c Release
```

Green, and the corpus diff is exactly the declared list.

## Risks

- **Turning on `S4COMPRESS` compiles code this repository has never built** (~20 files of file-level
  compression). Compile rot is possible. Sub-step 7 regenerates *before* adding the case, so a break
  shows as a build failure or a moved byte, not as a puzzling dump.
- **The double evaluation could disturb the existing value gate.** It is an addition to the dump format;
  the C# reader of `CDXEXPR.expr.dump.txt` must learn it before the old assertions run against the new
  file. Sub-step 4 lands the reader change and the regeneration together.
- **Decision 1 has no oracle beyond cp1251.** A wrong derivation for, say, cp850 would pass. The
  layer-1 test compares the map to its definition, which guards the code, not the definition; the
  definition is what the ADR argues for.
- **A sub-step that cannot be closed** (for instance if `S4COMPRESS` will not build) is recorded and
  left, not forced — each part is independent, and `SUMMARY.md` says which closed.
