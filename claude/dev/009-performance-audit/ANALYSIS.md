# 009-performance-audit — where the 14× comes from, and what the port's CPU is spent on

**Added 2026-08-19, after [`SUMMARY.md`](SUMMARY.md) closed.** "The block cache is worth 14×" is not
actionable until you know *which* cost it removes and whether the same win is available here. This
decomposes it.

Raw output: [`results-cpu-analysis.txt`](results-cpu-analysis.txt). Superseded raw output from the first,
defective pass: [`results-cache-analysis.txt`](results-cache-analysis.txt).

> ## A retraction, first
>
> **An earlier version of this file claimed the 14× "does not transfer" — that a cache is worth only
> 2.6× to the port, and that the port's CPU work is 7.7× the C's. Both numbers were wrong, and the
> conclusion drawn from them was wrong.** They came from a defect in this harness, not from the library:
> the RAM-backed configuration never left tier-0 JIT, so it measured roughly four times its real cost.
> See §8. Corrected: **a cache is worth 11.6× to the port on a character seek and 23.4× on a walk**, and
> the port's CPU work is **1.70×** the C's, not 7.7×. `SUMMARY.md` finding 2, as originally written,
> stands.

## 1. What "block optimization" actually is

Not disk buffering, and not an index-specific structure. It is a **general user-space block cache over
file reads**, and every read in the library goes through the same door.

`file4readInternal` (`f4file.c:2017-2093`) branches on one flag:

```c
if ( f4->doBuffer )
   urc = (unsigned)opt4fileRead( f4, pos, ptr, len ) ;   /* the cache */
else
   return file4readLow( f4, pos, ptr, len ) ;            /* the OS */
```

`opt4fileRead` (`o4opt.c:1450-1607`) is the cache:

- **A hash table over (file, block-aligned position).** `opt4fileHash` (`o4opt.c:1203`) is
  `((file->hashInit + pos) >> blockPower) & mask` — the file contributes a per-file salt, so two files
  never collide systematically.
- **A hit is a `memcpy`** out of `blockOn->data`. No syscall.
- **A miss reads one block and inserts it** — `opt4fileGetBlock` (evicting if needed), then
  `opt4fileReadFile`, then `opt4blockAdd`.
- **Five priority LRU lists**, not one: `dbfLo`, `dbfHi`, `indexLo`, `indexHi`, `other` (`OPT4`, in
  `d4data.h:661-671`). Index blocks live on their own lists, so **a table scan cannot evict the index
  tree** — exactly the eviction failure a single LRU would have.
- **Per-block `hitCount`, `readTime`, `accessTime`** feed `opt4blockUpgradePriorityCheck`
  (`o4opt.c:1017`): a block that keeps being hit is promoted. A tree's root ends up pinned by its own
  hit rate rather than by a rule.
- **Read-ahead**: a request spanning several blocks reads the run in one call (`opt4fileReadSpBuffer`).

**It is off by default, and `cb->optimize` does not turn it on.** `optimize` defaults to
`OPT4EXCLUSIVE` (-1), which is *permission* to buffer; `f4->doBuffer` is set only once `code4optStart`
allocates the pool. So the audit's `c` configuration reads through the OS exactly as `CodeBase.Net`
does, and the comparison in `SUMMARY.md` was like-for-like.

## 2. The constants

Measured standalone, no CodeBase linked, on the warm `PERF10K.cdx` (`perf.exe syscall`):

| Operation | Cost |
|---|---|
| `SetFilePointerEx` + `ReadFile`, 512 B — **what the C library does** (`f4file.c:1253,1318`) | **1.100 µs** |
| Positional `ReadFile` with `OVERLAPPED` offset — **what `RandomAccess.Read` does**, so what the port pays | **0.849 µs** |
| `memcpy` of 512 B from RAM — **what a cache hit costs** | **0.002 µs** |

The file is entirely in the OS page cache, so **none of this is disk I/O**: it is the syscall, its
transition and its copy. A hit is ~500× cheaper than a miss, and the port already uses the cheaper of
the two syscall forms.

## 3. Reads per operation, counted rather than inferred

`GetProcessIoCounters`, on one untimed pass per scenario:

| Scenario | `c` reads | `c` bytes | `cs` reads | `cs` bytes | cached (either) |
|---|---|---|---|---|---|
| `name-hit` | 4.286 | 1 247 | **5.000** | **2 109** | **0** |
| `amount-hit` | 3.500 | 845 | 4.000 | 1 597 | 0 |
| `tag-walk` | 2.044 | 100 | 1.044 | 83 | 0 |

**The port's byte counts are exact, and they name the tree.** 2 109 = 4 × 512 + 61 and
1 597 = 3 × 512 + 61 — four index blocks plus one 61-byte record (`RecordLength`) for the `C(20)` tag,
three plus one for the `N(12,2)` tag. So `T_NAME` is **four levels** deep, `T_AMT` **three**, and a seek
reads every level plus the record, every time.

The walk gives the leaf count the same way: 83.4 bytes/record × 10 000 − 10 000 × 61 = 224 000 bytes of
index = **437 leaf blocks**; 1.044 reads/record is one record read plus 437/10 000 leaf reads.

## 4. The 14× is read syscalls, and only read syscalls

Subtract the cached configuration from the uncached one to get the cost of the reads that disappeared.
Four scenarios, four independent estimates of one constant:

| Scenario | `c` I/O time | ÷ reads | **µs per read** |
|---|---|---|---|
| `name-hit` | 5.163 µs | 4.286 | **1.205** |
| `name-miss` | 5.130 µs | 4.286 | **1.197** |
| `amount-hit` | 4.215 µs | 3.500 | **1.204** |
| `tag-walk` | 2.416 µs | 2.044 | **1.182** |

**1.18–1.21 µs, from four scenarios with different read counts and different code paths**, against
1.100 µs measured standalone for the same call pair. The residual ~0.1 µs is the library's own per-read
bookkeeping in `file4readInternal`.

So `name-hit` spends **5.16 of its 5.56 µs — 93% — inside read syscalls.** Remove them and 0.40 µs is
left; 5.56 / 0.40 = 13.9×. There is no other mechanism to look for.

## 5. The same win is available here — 11.6× on a seek, 23.4× on a walk

Measured directly, by swapping the port's internal `IRandomAccessSource` for one holding each file in a
`byte[]` — a *perfect* cache: whole file resident, no syscall, no eviction, no coherence problem — with
identical checksums throughout:

| µs/op | `c` | `c-opt` | `cs` | `cs-ram` | **cache buys the port** | cache buys the C |
|---|---|---|---|---|---|---|
| `name-hit` `C(20)`, 4 levels | 5.562 | 0.399 | 7.840 | **0.677** | **11.6×** | 13.9× |
| `amount-hit` `N(12,2)`, 3 levels | 4.968 | 0.753 | 7.217 | **1.039** | **6.9×** | 6.6× |
| `tag-walk`, per record | 2.494 | 0.078 | 1.053 | **0.045** | **23.4×** | 32.0× |

**`SUMMARY.md` finding 2 is confirmed rather than corrected: the cache dominates, and it dominates for
this port too.** On the numeric tag the port gains slightly *more* than the C does (6.9× against 6.6×).

**The walk deserves its own line: 23.4×.** That is the largest single number in this audit, and it is
the access pattern that matters most — `QUERY` evaluates a range constraint by seeking one end of a tag
and then *walking*, so a bitmap build is thousands of walk steps, not thousands of independent descents.
A 10 000-record tag range goes from 10.5 ms to 0.45 ms.

## 6. And the port's CPU work is 1.4–1.7× the C's, not several times

With I/O removed from both sides, what is left is each library's own work:

| Scenario | port CPU | C CPU | **ratio** | ratio uncached |
|---|---|---|---|---|
| `name-hit` | 0.677 µs | 0.399 µs | **1.70×** | 1.41× |
| `amount-hit` | 1.039 µs | 0.753 µs | **1.38×** | 1.45× |
| `tag-walk` | 0.045 µs | 0.078 µs | **0.58× — the port is 1.7× faster** | 0.42× |

For a managed port against 32-bit `/O2` C this is a good place to be, and it means **the headline 1.41×
was not hiding anything**: the port is modestly behind on descent and ahead on stepping, with or without
I/O in the picture.

### What the residual 1.70× is made of

An isolated descent over the same real blocks — `bench.exe out N micro` — in two variants that must
return identical record numbers:

| Tag | variant | µs/seek | bytes/seek | comparisons/seek |
|---|---|---|---|---|
| `T_NAME` `C(20)` | shipped | 0.695 | 3 860 | 20.92 |
| `T_NAME` | **compare in place** | **0.401** | 2 568 | 20.92 |
| `T_AMT` `N(12,2)` | shipped | 1.061 | 3 688 | 49.51 |
| `T_AMT` | **compare in place** | **0.550** | 1 944 | 49.51 |

**The port materialises a fresh `byte[keyLength]` for every key comparison.** `BranchBlock.EntryAt` does
`entry[..keyLength].ToArray()`; `LeafBlock.EntryAt` does `key.AsSpan().ToArray()`. The C compares in
place — against the block for a branch (`b4block.c:2150`), and against a moving pointer into the
compressed key text for a leaf (`b4leafSeek`, `b4block.c:2192-2245`), materialising nothing.

**Removing just those copies is worth 1.7–1.9× of the descent** and takes 1 292–1 744 bytes/seek off the
allocation, with no algorithmic change and no change in results — `KeySearch.Compare` already takes a
`ReadOnlySpan<byte>`, so the arrays were never needed by the comparison. That is **suspect P2**, and it
is enough on its own to close most of the 1.70×.

### Why the copy is there — the reason is real, and it covers one call site in five

Worth writing down, because "delete the copies" reads like an oversight was found and it was not.
`LeafBlock.EntryAt`'s doc comment states the intent: *"The key is a fresh array, so a caller may keep it;
the block reuses its own buffer between calls."* That constraint is genuine. `LeafBlock` holds **one**
`key` buffer rebuilt incrementally as `builtIndex` advances, because a compressed leaf's keys are
relative — entry *n* is built from entries 0..*n* — so a span into it is invalidated by the next
`EntryAt` call. A fresh array is the right way to make an entry independently valid.

**`EntryAt` is doing double duty**: "give me an entry I can keep" and "let me peek at a key to compare
it". The safety cost of the first is charged to every use of the second. All five call sites:

| # | Site | Uses | Keeps the key? | Calls per seek |
|---|---|---|---|---|
| 1 | `BranchBlock.Seek`, binary search | `.Key`, compared then dropped | no | ~9 |
| 2 | `LeafBlock.Seek`, linear scan | `.Key`, compared then dropped | no | ~12 |
| 3 | **`TagCursor.Current`** | returns the `IndexEntry` outward | **yes** | ≤1 |
| 4 | `TagCursor.cs:340`, descent | `.Child` only — the key is never read | no | 3 |
| 5 | `TagCursor.cs:542`, descend first/last | `entry.Child` only | no | 0–3 |

**And that one site's contract is load-bearing, not hypothetical.** `IndexGoldenTests.cs:88` accumulates
keys across cursor moves — `walked.Add(new DumpIndexKey(cursor.Current.Key, cursor.Current.Record))` — so
if the key aliased `LeafBlock`'s reused buffer all 3364 entries would point at the same array and hold the
last key, and the index gate would break or pass vacuously. **`EntryAt` must keep copying**, which rules
out the tempting inversion of making it return a span and adding a copying variant beside it: that would
compile at every call site and corrupt exactly that gate, silently.

**One site of five needs an owned key.** Sites 4 and 5 allocate a 20-byte array to read a 4-byte child
pointer out of the struct. Sites 1 and 2 are every comparison. So ~24 of the ~25 copies per seek are on
paths that discard the key immediately or never look at it.

**For `BranchBlock` the justification does not apply at all.** Branch keys are stored *uncompressed at a
fixed stride* inside `block`, which the object owns and never mutates, so a `ReadOnlySpan<byte>` into it
stays valid for the block's whole life. There is no shared mutable buffer to protect: the leaf's
constraint was copied onto a class that does not have it.

**Where the reasoning slipped.** `LeafBlock.Seek`'s remarks argue that "rebuilding each key as we go
costs the same walk and keeps the comparison in one place … the keys are rebuilt anyway for whoever reads
the entry." That is sound about the **rebuild**, and §6 confirms the trade against P7 was right. But it
carries silently from *rebuild* to *copy out*, which is a separate act — and ~20 of the 21 rebuilt keys
per seek are read by nobody. There is **no ADR and no design-doc entry**; the decision was made in the
code and the comment records it honestly. A correct decision applied at the wrong granularity.

**So the fix is a shape change, not an algorithm change**, it is additive, and the good form never hands
out a span at all — the hot paths do not want a *key*, they want a *comparison* or a *pointer*:

| Add | For | Why it is safe |
|---|---|---|
| `LeafBlock.CompareAt(index, search)` → `int` | `LeafBlock.Seek`, ~12 calls/seek | rebuilds into the existing buffer and compares; **the span never escapes** |
| `BranchBlock.ChildAt(index)` → `uint` | `TagCursor.cs:340`, `TagCursor.cs:542` | reads four bytes; no key is touched |
| inline `search.Compare(block.AsSpan(offset, keyLength))` in `BranchBlock.Seek` | ~9 calls/seek | a branch block never mutates its buffer, so the span is valid for its whole life |

`KeySearch.Compare` already takes a `ReadOnlySpan<byte>`, so nothing else moves, no public surface
changes, and `TagCursor.Current`, `IndexFileReader`'s tag-directory read and every test keep working
because `EntryAt` is untouched.

**Two caveats on "easy".** It is low-risk, not zero-work: `DEV_APPROACH.md` wants the gate re-run and the
change mutation-checked, and it belongs to the performance step rather than a drive-by edit. And it should
be measured *with* the cache rather than after it — the cache also removes the `byte[512]` per block that
is most of the 2 568 bytes/seek left in the compare-in-place column, so measuring them in sequence would
attribute the saving to the wrong one, the same trap as P7-after-P2.

**Suspect P7 is a dead end on this data, and now measurably so.** Adding the C's duplicate-count skip
(`b4->curDupCnt`, `b4block.c:2232`) changed the comparison count by **nothing** — 20.92 and 49.51,
identical — because every key in both tags is unique, so no entry ever shares enough prefix to be
skipped. It costs a little extra bookkeeping and returns nothing. P7 needs a duplicate-heavy tag before
it can be judged, which `SUMMARY.md` already records as a corpus gap.

## 7. Finding 6, closed: it is entries per leaf

`SUMMARY.md` finding 6 flagged an oddity as unexplained — cached, the C's **numeric** seek costs *more*
than its character seek (0.753 against 0.399 µs) despite a shallower tree and a shorter key. The
comparison counts explain it, on both sides:

- **An 8-byte key packs about 50 entries into a 512-byte leaf; a 20-byte key packs about 23.** Measured:
  **49.51 comparisons per numeric seek against 20.92 per character seek.**
- **The leaf search is a linear scan in both implementations** — the port's `LeafBlock.Seek`, and the
  C's `b4leafSeek`, which does `b4top` then walks forward. Neither can binary-search a leaf, because the
  keys are delta-compressed and rebuilding entry *n* requires entries 0..*n*. Only branch nodes, whose
  keys are stored uncompressed at a fixed stride, get a binary search (`b4block.c:2147`, and
  `BranchBlock.Seek` likewise).
- So **more entries per leaf means more comparisons means more CPU**, and the numeric tag is dearer
  cached on both sides. Uncached the ordering flips because the numeric tag has one level fewer — 3.500
  reads against 4.286 — and at 1.2 µs a read, syscalls swamp the extra comparisons.

The earlier guess in this file, that `tfile4dtok`'s null check over an `N(12,2)` field was responsible,
is **withdrawn**: it is not needed, and the port shows the same inversion without executing that code.

## 8. The harness defect, and why it is worth recording

The first version of §5 and §6 reported the port's cached seek at 3.10 µs and concluded a cache was
worth only 2.6× here. **The real figure is 0.677 µs.** The cause:

- `Measure` warmed up by **pass count** — five passes. Tier-1 JIT promotion happens on a background
  thread after a **wall-clock** delay.
- A file-backed pass takes ~80 ms, so five passes gave the JIT ~400 ms and the code reached tier-1. A
  RAM-backed pass takes ~7 ms, so five passes gave it ~35 ms and **the code never left tier-0** —
  instrumented for PGO, and about four times slower than its own steady state.
- The tell was there and was not read: running the same variant twice in one process gave 2.66 µs the
  first time and 0.71 µs the last. A number that changes when only its *position* changes is measuring
  the runtime, not the code.

**Two checks now guard it**, and both are in [`harness/`](harness/): `Measure` warms up until a
**wall-clock** floor is met as well as a pass count, and the micro harness runs `shipped` and `spans`
twice each in opposite order, so an order effect is visible in the output instead of silent. With both
in place, default tiered and `DOTNET_TieredCompilation=0` agree to within 2% (0.684 against 0.694 µs),
which is the check that the warm-up is actually sufficient.

**The file-backed numbers in `SUMMARY.md` were never affected** — they were already at tier-1, and they
reproduce under `DOTNET_TieredCompilation=0` (7.84 against 8.12 µs). Only the RAM-backed configuration,
which this file added, was wrong.

## 9. What to take into the ADR

- **Build the cache. It is worth 11.6× on a seek and 23.4× on a walk**, and the walk is what `QUERY`
  will do most. This is far and away the largest item in the performance pass.
- **It is a syscall cache, not an I/O cache.** At this size nothing reaches the disk. On a table that
  does not fit in RAM the arithmetic changes completely, and nothing here measured that case.
- **A hit must be cheap.** The C's is a hash lookup plus a `memcpy` (0.002 µs / 512 B). A hit path that
  allocated would hand most of the win back — the same defect as P2, and an argument for handing out a
  span into a pooled block rather than a `byte[]`.
- **Copy the priority split, not just the LRU.** `dbfLo`/`dbfHi`/`indexLo`/`indexHi` exist so a table
  scan cannot evict the index tree. A single LRU gets this wrong exactly when `QUERY` needs it most: a
  full scan beside thousands of tag seeks.
- **Then P2 — compare in place.** Worth 1.7–1.9× of the descent by itself, needs no design decision, and
  removes 1 292–1 744 bytes/seek. After the cache it is the whole of the remaining gap to the C.
- **Root retention is no longer worth doing for its own sake.** The port issues 5 reads per character
  seek where the C issues 4.286, because `tfile4upToRoot` (`I4TAG.C:1349`) leaves the root loaded in
  `TAG4FILE.blocks` while the port re-descends from the root. That is ~11% *uncached*; with a cache the
  root is a hash hit costing 0.002 µs, and the saving disappears. **The cache subsumes it** — which is a
  reason to do the cache rather than the cheap trick.
- **P7 stays open but unpromising**, and needs a duplicate-heavy corpus tag before it can be judged.
- **One path was never measured and may be worse than any of the above.** `KeySearch.Compare` takes a
  **scalar byte-by-byte loop** whenever the search value carries trailing pad (`ComparesPadded`), and the
  vectorized `SequenceCompareTo` only when it does not. Every value in this audit fills `C(20)` exactly, so
  every comparison took the fast branch — but a real `Seek("SMITH")` on a `C(20)` tag pads, and therefore
  takes the scalar one. That is the common case in practice and it is unmeasured; it wants a query set of
  its own beside the P2 fix.

## 10. How this was measured

All in [`harness/`](harness/):

- `perf.exe syscall <dir> [reps]` — the three constants in §2, no CodeBase linked.
- Both harnesses report `reads_per_op` / `readbytes_per_op` from `GetProcessIoCounters`, on a separate
  untimed pass so the counter call never lands in a timed region.
- `bench.exe <dir> [reps] ram` — the C# side on the RAM-backed `IRandomAccessSource`.
- `bench.exe <dir> [reps] micro` — the isolated descent of §6, shipped against compare-in-place against
  dup-skip, each required to return the same record numbers, and two of them repeated in reverse order
  as the order-effect check.

`ram` and `micro` need `CodeBaseEngine`'s internal constructor and the internal `Cdx` types, so
`Bench.csproj` sets `AssemblyName` to `CodeBase.Net.Tests`, one of the library's existing
`InternalsVisibleTo` targets. **`net/` is still untouched** — no `InternalsVisibleTo` was added, and no
library code was changed, for any of this.
