# Remediation plan for the 002–005 audit

**Written:** 2026-08-13, after re-verifying [`audit.md`](audit.md)'s concrete claims against the code,
`claude/specs/*.md`, and `original/source/`. Named `REMEDIATION-PLAN.md` rather than `PLAN.md` because
this folder is an audit record, not a step: `PLAN.md` in a step folder means the test pyramid and
ordered sub-steps that `DEV_APPROACH.md` defines, and this is neither.

**Not a decision record.** Anything settled here gets promoted — a decision to
`ARCHITECTURE-DECISIONS.md`, a format fact to the relevant spec, a status to `PORTING-PLAN.md` §5,
and the ordering to `STATE.md` §3.

---

## 1. Triage

The audit was an independent pass and its method was sound, but CLAUDE.md's rule applies to it as it
does to the specs: spot-check a claim against real bytes before building on it. Three did not survive.

### 1.1 Verified — real and actionable

| Audit # | Finding | Verification |
|---|---|---|
| P1 | No block cache | `NodeReader.ReadAt` allocates a fresh array per read. Confirmed. The audit cites `NodeReader.cs:61` (the method declaration) and `STATE.md` §3 cites `:73` (the `ReadExactly` call, which is where the array is actually allocated). **`STATE.md` is the better reference** — keep it |
| P2 | Key array allocated per `EntryAt` | `LeafBlock.cs:117`, `new IndexEntry(key.AsSpan().ToArray(), …)`, called once per comparison inside `LeafBlock.Seek`. Confirmed, and `BranchBlock.cs:114` likewise |
| E4 | `MemoFileHeader.BlockSize` read as `UInt16` | `MemoFileHeader.Parse` reads `BinaryPrimitives.ReadUInt16BigEndian`. `FPT-MEMO.md:109` says the field is a signed `short` in memory (d4data.h:2858). A real divergence for stored values above 32767 |
| E3 | `FoxDate` year 0 | `FoxDate.cs:133-134` computes `leap` as `(year % 4 == 0 && year % 100 != 0) \|\| year % 400 == 0`, which is 1 for year 0 — contradicting the comment directly above it saying year zero is 1 BC and not a leap year. One of the two is wrong |
| §4 #1 | No cycle guard in the leaf chain | `TagCursor.cs:449`, `while (true)`. Confirmed — but **narrower than the audit states**: the loop only continues when `candidate.Count == 0`, so an A→B→A cycle of *non-empty* blocks returns on the first one. Only a cycle of **empty** leaf blocks hangs. Still a one-line bound |

### 1.2 False positive — the port is already correct

- **E2, `'H'` missing from `BlankRecord.ZeroBlankTypes`** (the audit's rank 5, **C, Medium**).
  `f4blank` (`original/source/f4field.c:135-168`) lists the zero-filled types explicitly:
  `r5wstr`, `r5wstrLen`, `r4int`, `r4charBin`, `r4memoBin`, `r4currency`, `r4dateTime`,
  `r4dateTimeMilli`, `r4double`, `r5guid`, `r5i2`, `r5ui2`, `r5ui4`, `r5i8`, `r5ui8`, `r5dbTime`,
  `r5dbTimeStamp`. **`r4floatBin` (`'H'`) is not among them** — it falls to `default:
  c4memset(…, ' ', …)`. `DBF-FORMAT.md` §6.14 transcribes this correctly. A blank `'H'` field **is**
  four spaces, exactly what this port produces. `ZeroBlankTypes = ['I','Y','T','B','X','Z']` matches
  the C library for every type the port admits.
  **Action: none, beyond recording the verification so it is not raised a third time.**

### 1.3 Overstated — real but smaller than labelled

- **E1, `'7'`** (ranked **C, Medium**, justified as "a real FoxPro 2.x table with a `'7'` datetime
  column would be refused"). That case cannot arise: `'7'` is a **CodeBase extension**, admitted only
  when `version >= 0x30` signed (`DBF-FORMAT.md:76` and §5 line 236), so no FoxPro 2.x table can carry
  one. The audit's own §1.2 says **C, Low**, which is the right label. The real issue survives and is
  unchanged: live `'7'` decode paths in `FieldValueDecoder`/`FoxDateTime` sit behind an open-time
  refusal in `FieldResolver.ReadableTypes`, so the support story is dishonest whichever way you read it.

- **E5, `FoxNumeric` uses `double.TryParse`.** Already documented in the class's own summary, with the
  reason (`c4atod`'s body is absent from the source drop) and the gate (224 corpus values compared bit
  for bit). A known and stated limitation, not a discovery.

- **E14, collation key construction ungated** (ranked #1, **E, High**). This is `COLLATION` not being
  started — the roadmap, already `STATE.md` §3 and `PORTING-PLAN.md` §5. Correct as a priority, not a
  finding.

### 1.4 Genuinely new relative to `STATE.md` §3

E1, E3, E4, the cycle guard, the four §4 robustness items, the GENERAL-collation-vs-code-page mismatch
(§1.3 of the audit), and P5–P7. Everything else the project had already named for itself.

---

## 2. The plan

### 2.0 First, with no code

Resolve the four judgement calls `STATE.md` §3 already carries from step 006 — ADR-29 (the two
navigation surfaces failing differently), ADR-30 (refusing a step from a record the tag does not
list), the removal of `Table.HasProductionIndex`, and the mutation-check process rule. They block
nothing and are cheaper to settle before a public seek sits on them.

### 2.1 Audit remediation — carried out in this folder

Small; roughly one session; mostly verification against the C rather than new design. It takes **no step
number**: it opens no capability and `PORTING-PLAN.md` §5 gains no row, so it is designed and executed
here, beside the audit that motivated it. Designed in [`DESIGN.md`](DESIGN.md).

1. **Remove `'7'`** (**ADR-32**) — the three branches in `FieldValueDecoder` (`:33`, `:62`, `:199`),
   `FoxDateTime.ToText`'s `includeMilliseconds` parameter and its two branches, the unit test that
   exercises the millisecond path, and the `field.Type == '7'` sub-expression in
   `RecordGoldenTests.cs:161` that can never evaluate true. The refusal at open stays.
2. **`MemoFileHeader.BlockSize`** — read as a signed `short` per `FPT-MEMO.md:109`; unit case at
   32768 showing the sign.
3. **`FoxDate` year 0** — check `DayOfYear`/`YearToDays` against `D4DATE.C`; the correction the comment
   claims may live in `YearToDays` rather than where the comment sits. Fix whichever of code and
   comment is wrong, and cite the C line.
4. **Leaf-chain cycle guard** — bound the empty-block walk in `TagCursor.StepPhysical` the way the
   descent paths are bounded by `MaxDepth`.
5. **Record E2 as verified-correct** in the step's `SUMMARY.md`, with the `f4field.c:135-168` citation,
   so it is not re-raised.

6. **The three remaining §4 robustness items ride along** (§3 #6 — the cut line is withdrawn):
   `LeafGeometry.Parse` bounds its mask widths (`recordBits <= 32`, `dupBits <= 8`, `trailBits <= 8`)
   and not only their sum; `SeekFirstAtOrAbove` tries the next sibling when the descent lands on an
   empty leaf, the way `StepPhysical` already does; and a short read from an index file reports
   `ErrorCode.Index` rather than `ErrorCode.Data`. The audit labels these `HARDENING`-tier, but "before
   more layers land on the read path" applies to them as much as to the performance pass, and each is
   bounded.

   The one **not** included is the audit's §4 block-alignment check on child pointers. It is not a
   bound on a corrupt value the way the others are — it needs a rule about what a legal child offset
   *is*, which is `BlockAddressing`'s to state, and that is a design question rather than a guard.
   It stays on the `HARDENING` list.

> **Numbering note, added 2026-08-19.** §2.2 and §2.3 are in the order this plan *proposed* on
> 2026-08-13: measure first, then `COLLATION`. What happened was the reverse — seek by value went first
> as step 008 and the measurement landed as [`009-performance-audit`](../009-performance-audit/) — so
> §2.3 precedes §2.2 in reality. The step numbers here have been corrected to where the work actually
> went; the section order is left as written, because it is the record of a plan and not of an outcome.

### 2.2 Step 009 — measure

Add the benchmark project (BenchmarkDotNet is in the stack list, unused) and take the baseline
`STATE.md` §3 already specifies: over `CDXDEEP` and `IDXONE`, a full tag-order table walk, a full
index-only walk, a seek storm, and `Go(n)`-then-`Skip(1)`.

**Then stop before the cache.**

- **P2 (per-entry allocation) lands here.** It is local to `LeafBlock`/`BranchBlock`, has no design
  content — a `CompareAt(int)` against the internal span — and can go in the moment the benchmark
  shows the cost.
- **P5 is not measured here — it is designed away in 007.** Calling the copy "defensive" and then
  designing 007 to keep the search on the cursor were the same decision described twice. Once the
  cursor owns the key buffer, the copy happens once per cursor rather than once per seek, and there is
  nothing left to measure. `D4SEEK.C:1130-1137` shows the reference reaching the same conclusion with a
  buffer parked on `CODE4`.

- **P6 and P7 are measured here, not fixed here.** `IndexFileReader.Tag(name)`'s linear scan
  (`IndexFileReader.cs:119`) runs over tens of tags and will very likely disappear against one block
  read. `LeafBlock.Seek`'s missing duplicate-count skipping is **already a documented decision** in the
  method's own remarks, not an oversight. Measure all three; expect to keep at least two, and say so
  when that is the outcome rather than leaving them looking unexamined.

- **P7 must be measured together with P2, not after it.** P2 removes the per-entry *copy*; the *rebuild*
  stays, because a compressed leaf's keys are relative and must be reconstructed either way — so P7's
  stated justification ("the keys are rebuilt anyway") survives P2 intact. But once the copy is gone the
  full-length comparison is a much larger share of what remains, so measuring P2, fixing it, then
  measuring P7 separately would attribute the cost to the wrong one.

- **P1 (the block cache) does not.** Where the cache lives is an ADR, and the right answer depends on
  `QUERY`'s access pattern, which does not exist yet. The audit says so itself: the optimizer will
  drive this path far harder than a walk — several cursors over one tag, thousands of seeks per query.
  Designing the cache now means designing it with less information than will be available once the
  optimizer's shape is known. Measure now; design the cache against a real access pattern.

Keep the rule that produced the current code: no optimization without a measurement and a gate that
still passes. A cache that serves a stale block is a wrong record set.

### 2.3 Step 008 — `COLLATION` and seek by value

The audit's #1 and the reason the project exists — the last piece before a caller can ask a table a
question rather than walk it. Gateable from the corpus already committed, since every tag's stored keys
sit beside the field values they were computed from.

Fold in the audit's §1.3 finding while the code is open: **validate that a tag's collation name matches
the table's code page.** A `GENERAL` tag on a cp850 table is currently read with the cp1252 weight
table. Harmless while only reading keys — keys are bytes — and wrong the moment a *value* is seeked,
which is precisely what this step adds.

It also inherits the three questions 005 named: the `.NULL.` convention for an empty public seek,
whether `SeekNext`'s degrade-to-seek should survive into a public API, and `considerPartialSeek`.

---

## 3. Decided — 2026-08-13

All seven questions this plan opened are closed. Nothing here is still waiting on a call.

| # | Question | Decision | Recorded in |
|---|---|---|---|
| 1 | ADR-29 — two navigation surfaces failing differently | **Confirmed as written.** Collapsing to one shape costs either `d4skip` fidelity or loop termination, for a symmetry no caller asked for. The gate already proves the record sequences identical | ADR-29 (unchanged) |
| 2 | ADR-30 — refusing a tag-order step from an unlisted record | **Confirmed as written.** "A wrong record set is far worse than a slow one" is the project's ground rule, and reporting end of file is exactly the plausible-looking wrong answer it forbids | ADR-30 (unchanged) |
| 3 | `Table.HasProductionIndex` removal | **Confirmed.** No caller, no test, and the property could never disagree with `HasIndex` | **ADR-31** (new) |
| 4 | The mutation-check process rule | **Adopted**, as process rather than architecture | `DEV_APPROACH.md` §4, "Proving a gate" |
| 5 | The `'7'` type | **Out of scope; remove the dead branches.** `PORTING-PLAN.md` §2.1 never listed it, and both of the audit's premises for admitting it are false (see ADR-32) | **ADR-32** (new), `PORTING-PLAN.md` §2.3 |
| 6 | The §4 robustness items — remediation or `HARDENING`? | **Ride along with the remediation.** The "before more layers land" argument applies to them as much as to the performance pass, and the code is open anyway | §2.1 below — the cut line is withdrawn |
| 7 | Ordering: measure vs `COLLATION` first | **Keep §2.2's split** — measure early, land P2, defer the cache design until `QUERY`'s access pattern exists | `STATE.md` §3 |

**On #7, the reasoning rather than the ruling.** The audit puts `COLLATION` first and defers
measurement; `STATE.md` §3 puts the performance pass first, "before more layers land on the read path".
The split satisfies both, because the two halves of the performance pass have different urgencies: a
baseline measurement is cheap, non-destructive and never invalidated by later work, while the cache is a
design whose right answer depends on how the optimizer drives the index — which does not exist yet.
This is the one decision here that is a judgement rather than a finding, and the one most reasonable to
overrule: going straight at `COLLATION` and dropping 008 to a later slot would be a choice, not a
correction.

**Consequential edit to §2.1.** #6 withdraws the cut line: `LeafGeometry` mask-width bounds,
`SeekFirstAtOrAbove` on an empty non-root leaf, and the `ErrorCode.Data`/`ErrorCode.Index`
classification are **in** the remediation, not deferred. #5 settles §2.1 item 1 as *remove*.

---

## 5. Disposition of every finding

Added 2026-08-13, after noticing that §1.4 named P5-P7 as new and then §2 never placed them. That was
not the only one. This table exists so the omission cannot recur: **every** row of the audit's §5 ranked
table and **every** bullet of its §1.3 appears here with a home, including the ones whose home is
"nothing to do".

### 5.1 The ranked table (audit §5)

| Rank | # | Home |
|---|---|---|
| 1 | E14 collation construction | Step 008 (§2.3) |
| 2 | P1 block cache | Step 009, then **deferred past it** — the cache is designed against `QUERY`'s access pattern, not guessed at now (§3 #7) |
| 3 | P2 per-entry allocation | Step 009 (§2.2) |
| 4 | E1 `'7'` | **Closed** — ADR-32, out of scope, dead branches removed in the remediation |
| 5 | E2 `'H'` blanking | **Closed — false positive.** `f4blank` space-fills `r4floatBin` (`f4field.c:135-168`); the port was already right (§1.2) |
| 6 | P3 `Synchronize` O(n) | **Resolved in 008, and it never needed `EXPR`.** ADR-28 restricts a selectable tag to a bare field name, so deriving the current record's key means reading a field — not evaluating an expression. With 008's transforms plus 005's `SeekExact` it becomes O(log n). The "needs `EXPR`" note here, in the audit, and in `STATE.md` was wrong |
| 7 | Cycle guard | Remediation, `DESIGN.md` item 4 |
| 8 | E3 `FoxDate` year 0 | **Closed** — ADR-33. The code was right; the C's own comment was wrong and had been copied faithfully |
| 9 | E4 `MemoFileHeader.BlockSize` | Remediation, `DESIGN.md` item 2 |
| 10 | E5 `FoxNumeric` | **Closed — already a documented known**, with its reason and its 224-value gate stated in the class summary (§1.3) |
| 11 | E6 invalid milliseconds in `'T'` | **Remediation, new item 8** — a unit test and a bound. See 5.3 |
| 12 | E7 deleted records ungated | **Corpus gap** — 5.2 |
| 13 | E8 memo block size ≠ 0/512 | **Corpus gap** — 5.2 |
| 14 | E9 `IsNull` with a short bitmap | **Remediation, new item 9** — a unit test. See 5.3 |
| 15 | E10-E13 seek edge cases unit-only | **Corpus gaps** — 5.2. E12 additionally needs step 008's value-to-key transforms |
| 16 | `LeafGeometry` mask widths | Remediation, `DESIGN.md` item 6 |
| 17 | `SeekFirstAtOrAbove` empty leaf | Remediation, `DESIGN.md` item 6 |
| 18 | Child-pointer block alignment | **`HARDENING`**, deliberately — it needs a rule about what a legal child offset is, which is `BlockAddressing`'s to state (`DESIGN.md` item 6) |
| 19 | Index short read reports `Data` | Remediation, `DESIGN.md` item 6 |
| 20 | P5 `KeySearch.For` copies | **Resolved by 008's design, not by measurement.** The triage above called the copy "defensive" and separately let 008 keep the search on the cursor — those are the same decision, and splitting them made a settled question look like an open finding. A cursor that owns its key buffer copies once per cursor, not once per seek. See `008-seek-by-value/DESIGN.md` |
| 21 | P6 `Tag(name)` linear scan | **Step 009** (§2.2) — measure first; a tag directory holds tens of tags, so this is very likely noise |
| 22 | P7 no duplicate-count skipping | **Step 009** (§2.2) — measure **together with P2**, see §2.2 |

### 5.2 Corpus gaps — they want generator cases, not unit tests

`DEV_APPROACH.md` §4 is explicit that an uncovered path is a signal to add a generator case. These are
the audit's findings that qualify, and unlike the remediation's guards they are **not** things the C
library cannot write — the generator simply was never taught them. They belong to the `CORPUS`
capability, which `PORTING-PLAN.md` §5 already carries as in progress.

| Gap | Source |
|---|---|
| Deleted records — all **1188** dumped corpus records are `deleted=0` | E7 |
| A memo block size other than 0 and 512 | E8 |
| `SeekExact` on a descending tag's duplicate run | E10 |
| `SeekNext` across a branch boundary | E11 |
| `SeekNext` against the reference on numeric/date/currency tags (**also needs step 008**) | E12 |
| A deep, multi-block single-tag `.IDX` | E13 |
| A CDX block size other than 512, and a multiplier above one | audit §1.3 |
| `keyLen` above 240, with its 16-bit compression counters | audit §1.3 |
| A multi-block tag directory (roughly 40+ tags) | audit §1.3 |
| A tree built by insert-and-split rather than the bulk path | audit §1.3 |
| A non-empty free list, which only a delete path fills | audit §1.3 |
| A VFP 9 (`0x32`) table with a memo, whose memo file this port does not open | audit §1.3 |

The last one is not only a corpus gap: `LegacyVariant.cs:10-15` documents it as a **reproduced C library
defect**, so a case would pin the reproduction rather than close a hole. Worth having for that reason.

`PORTING-PLAN.md` §6.3 carries this list; it is the document that owns corpus contents.

### 5.3 Two that ride along with the remediation

Both are unit tests over existing entities, minutes of work, and the "before more layers land" argument
covers them:

- **E6, invalid milliseconds in a `'T'` field.** `FoxDateTime.ToDateTime` does
  `AddMilliseconds(milliseconds)` with no bound, so a corrupt value above 86,400,000 rolls into the
  following day rather than being refused. The C library has the same blind spot, so the *behaviour*
  is likely correct-by-reproduction — but it is untested either way, and `AddMilliseconds` can also
  throw out of `DateTime` range, which would be an exception type escaping the library's own hierarchy.
  Pin the behaviour, and convert any escaping exception into a `CodeBaseException`.
- **E9, `IsNull` with a bitmap shorter than the bit count.** `Table.cs:832` short-circuits to
  `byteIndex < bitmap.Length`, reporting non-null. Almost certainly right — it is the defensive
  reading — but it is ungated, and it decides whether a field reads as null, which is a value question
  rather than a robustness one. A unit test either way.

## 4. Housekeeping this plan creates

- This folder is untracked and is not a numbered step. The remediation is carried out **here**, under
  [`DESIGN.md`](DESIGN.md) beside the audit that motivated it, rather than opening a step number for it —
  `007` stays free for the next genuinely new step. `claude/dev/README.md` gains a line for the folder
  saying what it is.
