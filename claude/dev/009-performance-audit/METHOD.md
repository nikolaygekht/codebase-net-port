# 009-performance-audit — method

How the numbers in [`SUMMARY.md`](SUMMARY.md) were produced, what was controlled, and how to run it
again. The findings are in the summary; this file is the instrument.

## The data

One table, written by the reference C library so that neither side has an authoring advantage — and
because this port cannot write one yet (`WRITE` is not started).

`PERF10K.DBF` (610 KB) + `PERF10K.cdx` (305 KB), version `0x30`, 10 000 records, no code-page mark:

| Field | Type | Role |
|---|---|---|
| `ID` | `I(4)` | read after every seek, and summed into the checksum |
| `NAME` | `C(20)` | the character key — tag `T_NAME` |
| `CITY` | `C(16)` | filler, so the record has realistic width |
| `AMOUNT` | `N(12,2)` | the numeric key — tag `T_AMT` |
| `HIRED` | `D(8)` | filler |

**`NAME` fills `C(20)` exactly**, so a seek is a whole-key seek on both sides with no padding or prefix
rule in play. Its value embeds a scrambled row number (`(i * 7919) % 10000`, coprime with the row count,
so the mapping is a permutation): unique keys, key order unrelated to record order, and leading letters
derived from the scramble so neighbouring keys share little and the leaves are realistically — not
artificially — compressible.

**`AMOUNT` steps in quarters**, so the value is exact in binary and the two decimals the field stores
are the two decimals the key was built from. No rounding sits between the value asked for and the key on
disk.

## The scenarios

| Scenario | 10 000 × | Tag |
|---|---|---|
| `name-hit` | seek a value that exists | `T_NAME` |
| `name-miss` | seek a value that does not exist, land on the neighbour | `T_NAME` |
| `amount-hit` | seek a number that exists | `T_AMT` |
| `tag-walk` | read all 10 000 records in tag order | `T_NAME` |

`tag-walk` is not a seek scenario. It is collected because suspect P4 wanted a number and it costs
nothing beside the others.

## Holding the two sides to the same work

This is what makes the ratios worth quoting.

- **The queries are files, not an algorithm written twice.** `perf.exe gen` writes
  `queries-name.txt`, `queries-name-miss.txt` and `queries-amount.txt`; both programs read the same
  lines in the same order. Neither side can drift from the other by re-deriving values.
- **Every scenario reports a checksum** — the sum of `ID` over every record it landed on. All four match
  to the digit between all three configurations. The two libraries did not do *similar* work: they
  reached the same records, in the same order, and read a field out of each. A pass that quietly found
  nothing could not look fast.
- **Query order is scrambled twice.** `NAME`'s key order is already unrelated to record order; the query
  order is then a second permutation (`(j * 3571) % 10000`). Neither side gets a sequential-access head
  start, and the seeks genuinely descend the tree.
- **Both binaries run on Windows**, against the same file on `D:`. Running the C# side under WSL would
  have put its reads through the `/mnt/d` bridge and measured the filesystem, not the library.
- **Warm-up runs to a wall-clock floor, not a pass count**, then N timed reps. Pass counting was not
  enough, twice over. One pass left the first scenario decaying monotonically over its first six reps —
  105, 104, 107, 114, 108, 89, 83, 80, 80, 80 ms — as tiered JIT promoted the seek path with the clock
  running. Five fixed that for the file-backed runs but **not** for the RAM-backed ones added later: a
  RAM pass takes ~7 ms against ~80 ms, so five of them never gave the background JIT the wall-clock it
  needs to reach tier-1, and the measurement came out ~4× high. See [`ANALYSIS.md`](ANALYSIS.md) §8. The
  harness now warms until **both** a pass count and a 1.5 s floor are met; the check that it worked is
  that default tiered and `DOTNET_TieredCompilation=0` agree to within 2%. **The C side warms up the same
  number of passes**, so the two harnesses differ in nothing but the library beneath them.
- **Order-effect checks are built in**, because that is what caught the defect above: the main harness
  re-measures `name-hit` last as `name-hit-2`, and the micro harness runs `shipped` and `spans` twice
  each in opposite order. A figure that moves when only its position moves is measuring the runtime.
- **Three interleaved rounds** of all three configurations, so a drift in machine state shows up in all
  of them rather than in whichever ran last.

### What maps to what

| Scenario | C | C# |
|---|---|---|
| `name-hit` | `d4seek` → `r4success` | `Seek(string)` → `Ok` |
| `name-miss` | `d4seek` → `r4after` | `SeekAtOrAfter` → `After` |
| `amount-hit` | `d4seekDouble` | `Seek(double)` |
| `tag-walk` | `d4top` / `d4skip(1)` | `Top()` / `Skip(1)` |

**`name-miss` pairs `d4seek` with `SeekAtOrAfter`, not with `Seek`, deliberately.** On a miss `d4seek`
**positions on the following record** before returning `r4after`; C# `Seek` reports `NoRecord` and reads
nothing (ADR-29's distinction: a positioning call reports no record). Pairing it with `Seek` would have
charged the C for a record read the C# never did. `SeekAtOrAfter` is the call that does the same work —
and the matching checksums are the evidence that it does.

## Two hypotheses tested and rejected

Recorded so they are not raised again:

- **"The C's default settings already buffer, so the baseline is unfair."** They do not.
  `cb->optimize` defaults to `OPT4EXCLUSIVE` (-1), which is a *permission* to buffer, not a switch;
  nothing is cached until `code4optStart` is called. The `c` configuration therefore reads through the
  OS exactly as this port does. `c-opt` exists to price the cache separately.
- **"The C# timing spread is Gen0 pauses."** `DOTNET_GCgen0size=0x20000000` drove Gen0 collections to
  zero and moved the timings under 1%. The spread was tiered-JIT warm-up, fixed by warming to a
  wall-clock floor; see finding 5 and [`ANALYSIS.md`](ANALYSIS.md) §8.

## Environment

- WSL over Windows; the repository on `D:`. **All three binaries run on the Windows side.**
- `c` / `c-opt`: MSVC, **x86**, `/O2`, linked against `test-files-generator/obj/codebase.lib` — the same
  library build, with the same `cb-config.h` switches, that produced `net/corpus/`.
- `cs`: .NET 8 (runtime 8.0.29), Release, workstation GC.
- Timing: `QueryPerformanceCounter` on the C side, `Stopwatch` on the C# side.

## Running it again

The harness in [`harness/`](harness/) is the record; it was developed and run in
`experiments/performance-1-experiment/`, which is **gitignored** — the generated table, the query sets
and the build outputs do not belong in the repository.

```bash
# 1. the reference library, if it is not built (Windows/MSVC)
cd test-files-generator && cmd.exe /c build-lib.bat && cd ..

# 2. lay the harness back out
mkdir -p experiments/performance-1-experiment/{src-c,src-cs/Bench}
cp claude/dev/009-performance-audit/harness/perf.cpp       experiments/performance-1-experiment/src-c/
cp claude/dev/009-performance-audit/harness/Program.cs     experiments/performance-1-experiment/src-cs/Bench/
cp claude/dev/009-performance-audit/harness/Micro.cs       experiments/performance-1-experiment/src-cs/Bench/
cp claude/dev/009-performance-audit/harness/Bench.csproj   experiments/performance-1-experiment/src-cs/Bench/
cp claude/dev/009-performance-audit/harness/build-c.bat    experiments/performance-1-experiment/
cp claude/dev/009-performance-audit/harness/run.bat        experiments/performance-1-experiment/

# 3. build both sides, generate, run three rounds -> out/results.txt
cd experiments/performance-1-experiment && cmd.exe /c run.bat 9
```

By hand, if `run.bat` is not wanted:

```bat
build-c.bat
bin\perf.exe gen out
bin\perf.exe bench out 9 plain      :: no library block cache -- the fair comparison
bin\perf.exe bench out 9 opt        :: code4optStart, the cache on
bin\perf.exe syscall out 9          :: the read / cache-hit constants, no CodeBase linked
dotnet build src-cs\Bench\Bench.csproj -c Release --artifacts-path artifacts
set CS=artifacts\bin\Bench\release\CodeBase.Net.Tests.exe
%CS% out 9                          :: file-backed
%CS% out 9 ram                      :: whole file in a byte[] -- a perfect cache
%CS% out 9 micro                    :: the isolated descent, shipped vs compare-in-place
```

The C# executable is named `CodeBase.Net.Tests.exe` rather than `bench.exe`: `ram` and `micro` need
`CodeBaseEngine`'s internal constructor and the internal `Cdx` types, and borrowing one of the library's
existing `InternalsVisibleTo` names gets that without adding an `InternalsVisibleTo` to
`net/CodeBase.Net/CodeBase.Net.csproj` for the sake of an experiment.

Set `BENCH_REPS_DETAIL=1` to print each rep's time on the C# side — that is how the warm-up curve was
found.

`Bench.csproj` deliberately sits outside `net/`, so `net/Directory.Build.props` does not apply to it,
and builds with `--artifacts-path`, so running this audit **cannot touch `net/CodeBase.Net/obj` or the
solution's build state**.
