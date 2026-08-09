# Development approach

How work is *run*, step by step. The other documents answer different questions and this one does
not repeat them:

- [`PORTING-PLAN.md`](PORTING-PLAN.md) — **what** we build, at what priority, and each capability's gate.
- [`specs/`](specs/) — **what the bytes are**. Authoritative; if code and a spec disagree, the spec wins.
- [`ARCHITECTURE-DECISIONS.md`](ARCHITECTURE-DECISIONS.md) — **why** things are the way they are,
  and what was rejected. Do not relitigate an accepted entry without new information.
- [`../STATE.md`](../STATE.md) — **where the project is** right now: ready, last session, next.
- **This file** — how a single step is designed, tested, planned and executed.

The rule that gives everything else its shape: **design and plan are written down before code is
written.** A step that starts with an editor open on a `.cs` file has skipped the part that catches
mistakes cheaply.

---

## 1. The loop

Every step runs the same six phases. Each phase has an output; a phase is not done until its output
exists in the step folder.

| # | Phase | Output | Move on when |
|---|---|---|---|
| 1 | **Frame** — one small, verifiable step | `DESIGN.md` §Goal | The goal names its own verification, in one sentence |
| 2 | **Design** — classes and responsibilities (ECB + SOLID, §3) | `DESIGN.md` | Every class has one sentence of responsibility and an ECB role |
| 3 | **Test pyramid** — what is proved at which layer (§4) | `PLAN.md` §Tests | Each layer names what only *it* can catch |
| 4 | **Seams** — what gets faked or mocked, and where (§5) | `DESIGN.md` §Seams | Every boundary the tests need to control is an interface |
| 5 | **Plan** — ordered sub-steps, each with its own check | `PLAN.md` §Steps | Every sub-step ends in something runnable or assertable |
| 6 | **Execute** | `STATE.md`, then `SUMMARY.md` | The step's gate passes, `SUMMARY.md` is written, and §5 of the porting plan reflects what the step advanced |

Phases 2–5 are cheap to redo and expensive to skip. Phase 6 is the only one that writes production
code.

### What "small and verifiable" means

A step is the right size when **all** of these hold:

- it has exactly one gate, and that gate is mechanical (a test run, not an opinion);
- `PLAN.md` lists roughly 3–8 sub-steps, not twenty;
- it advances a single capability (`PORTING-PLAN.md` §5);
- the whole diff is reviewable in one sitting.

If any fails, split it. Two small steps with two gates beat one step with a gate that is really a
checklist. Splitting is free before phase 6 and expensive after.

---

## 2. Step folders — `claude/dev/XXX-step-name/`

All work is tracked in numbered folders, so any step can be reconstructed later from its own record.

```
claude/dev/
├─ README.md                  index of steps (one line each)
├─ _template/                 copy this to start a step
└─ 001-dbf-header-reading/
   ├─ DESIGN.md               phase 2 + 4 — classes, responsibilities, seams
   ├─ PLAN.md                 phase 3 + 5 — test pyramid, ordered sub-steps
   ├─ STATE.md                phase 6 — live: where we are, decisions, blockers
   └─ SUMMARY.md              on completion — what shipped, what deviated
```

Starting a step:

```bash
cp -r claude/dev/_template claude/dev/007-cdx-leaf-codec
```

Rules:

- **Numbers are never reused and never renumbered.** An abandoned step keeps its number and gets a
  `SUMMARY.md` saying why it was abandoned — that is information, not clutter.
- **`STATE.md` is per-step and short-lived**; the root `STATE.md` is the project-level one and links
  to the active step. Do not duplicate content between them: project-level decisions go up, step
  mechanics stay down.
- **`SUMMARY.md` is what a future session reads.** Assume nobody re-reads `PLAN.md` after the step
  closes. If a decision matters beyond the step, it belongs in `SUMMARY.md` and, if it is
  project-level, in `ARCHITECTURE-DECISIONS.md`.
- **Commits name their step** (`001-dbf-header-reading: …`), so history and folder line up.

---

## 3. Design rules — ECB and SOLID

### 3.1 Entity / Boundary / Controller

Every class gets exactly one of three roles, named in `DESIGN.md`.

| Role | What it is here | Rules |
|---|---|---|
| **Entity** | The on-disk structures and domain values: `DbfHeader`, `FieldDescriptor`, `RecordBuffer`, `MemoBlockHeader`, `NodeHeader`, `IndexKey`, collation tables | Pure. `ReadOnlySpan<byte>` in, values out; values in, bytes out. **No I/O, no clock, no ambient state, no logging.** Never mocked — construct the real thing. |
| **Boundary** | Everything that touches the outside world: `IRandomAccessSource` (read/write at offset), `IFileLocks`, `IClock`, and the public API surface the user calls | Interfaces, thin, no logic worth testing. The **only** place `FileStream` appears. This is where test doubles attach. |
| **Controller** | Use cases that orchestrate: `TableReader`, `MemoReader`, `TagSeeker`, later `QueryOptimizer` | Owns no file handles and no byte layout. Takes boundaries via constructor. Holds the sequencing that is worth unit-testing without a disk. |

Why this split earns its keep in *this* project: the byte-exact layer is the risky part, and making
it pure means it can be tested exhaustively at memory speed with no files involved — while the file
handling, which is boring but I/O-bound, is isolated behind two or three interfaces that fakes can
stand in for.

The failure mode to watch for: a class that seeks a stream *and* decodes a header. That is a
boundary and an entity fused together, and it can only be tested with a real file.

### 3.2 SOLID, stated concretely

- **SRP** — parsing, I/O, and caching are three classes, never one. If a name contains "and", split it.
- **OCP / LSP** — format variants (`0x03` / `0x30` / `0xF5`; 4-byte vs 10-byte memo references;
  compat 25 vs 30 behaviour) are resolved **once at open** into a strategy object. Do not scatter
  `if (version == 0x30)` through the code; a new variant must be a new implementation, not an edit
  to fifteen call sites.
- **ISP** — boundary interfaces stay narrow enough to fake by hand in three lines. If a test has to
  stub eight members to exercise one, the interface is too wide.
- **DIP** — controllers depend on boundary interfaces. Concrete `FileSource` is constructed only at
  the composition root (`Table.Open`), nowhere else.

### 3.3 What design does not get to decide

The specs already settled the byte layout, the endianness islands, and the collation-table mandate.
Design decides *class shape*, not format facts. If a design seems to need a format fact the specs do
not state, that is a spec gap to fill — not a judgement call to make in code.

---

## 4. The test pyramid

Four layers. The corpus sits at the top and is the gate — but it is deliberately **not** the whole
suite.

| Layer | Runs against | Catches what nothing below it can | Speed |
|---|---|---|---|
| **1. Unit** (most tests) | Entities, pure functions: julian dates, currency scaling, key transforms, descriptor decode | Every branch and edge of the byte layer, exhaustively | µs |
| **2. Component** | Controllers over an in-memory boundary fake | Sequencing and state-machine bugs — seek/skip/EOF, buffer reuse — without a disk | ms |
| **3. Fault injection** | Controllers over a **mocked** boundary (§5) | What the corpus physically cannot express: truncation, short reads, `IOException`, contention | ms |
| **4. Golden / corpus** | Real files in `net/corpus/` + their `.dump.txt` | Whether we actually match the bytes the C library produces | ms–s |

### The two rules that make this work

**Every format claim ends at layer 4.** A field type, a header field, a memo reference is not "done"
until a corpus test asserts it against a dump produced by the C library. Layers 1–3 can all pass on
a self-consistent misunderstanding; only layer 4 can tell us we misread the format.

**Layer 4 must not be the only thing standing.** The corpus is a *witness to happy paths that
happen to exist in it*. It cannot prove behaviour on a truncated file, an unreadable disk, a field
type no case covers, or an input no sane writer produces — and it never fails fast enough to be a
good debugging tool. So:

- when a defect is found at layer 4, add the regression test at **the lowest layer that can catch
  it**, and keep the corpus assertion too;
- when a code path has no corpus coverage, that is a signal to **add a generator case and
  regenerate** (`test-files-generator/`), not to settle for a unit test;
- a step whose tests live only at layer 4 has an untestable design — go back to phase 2.

### Expected values: where they may come from

This sharpens the CLAUDE.md rule ("never hand-write expected bytes"):

- **Allowed as test *input*:** hand-built byte arrays, including deliberately malformed ones. Feeding
  a parser 20 bytes to check it rejects a truncated header invents nothing.
- **Allowed as *expectation*:** values read from a corpus `.dump.txt`; bytes sliced out of a corpus
  file (cite which file and offset); round-trip invariants (`decode(encode(x)) == x`,
  `encode(decode(bytes)) == bytes`); and ordering invariants for keys.
- **Not allowed as *expectation*:** bytes typed in from reading a spec. That records our
  interpretation of the format, then asserts we interpreted it the way we interpreted it. If no
  corpus case covers the path, generate one.

### Mechanics

- Layer 4 tests live in `net/tests/CodeBase.Net.Golden`; layers 1–3 in `net/tests/CodeBase.Net.Tests`.
- Tag by layer (`[Trait("Layer", "Golden")]`) so the fast layers can run alone during development.
- One helper resolves the corpus directory from the repo root — no test hard-codes a path.

---

## 5. Test doubles, and Moq

**Moq** ([`Moq`](https://www.nuget.org/packages/Moq), BSD-3-Clause) is the mocking library.
Pin **≥ 4.20.2**: versions 4.20.0–4.20.1 bundled *SponsorLink*, which read the developer's git
email at build time; it was removed in 4.20.2. (Same class of concern that put this project on
AwesomeAssertions instead of FluentAssertions — check what a test dependency does at build time.)

### What to mock

| Mock it | Do not mock it |
|---|---|
| Boundaries you own: `IRandomAccessSource`, `IFileLocks`, `IClock` | Entities — construct the real `DbfHeader`; it is pure and cheap |
| Types you own that wrap something you don't | `FileStream`, `Span<byte>`, .NET types — wrap them, then mock the wrapper |
| Failure behaviour: throw, return a short read, report a lock as held | Collation tables or key transforms — faking those fakes the thing under test |

### Fake vs mock

Prefer a **hand-written in-memory fake** when the test needs *data* — an `InMemorySource` over a
`byte[]` (often loaded straight from a corpus file) is clearer and faster than a mock with fifteen
`Setup` calls, and it does not break when an unrelated call is added.

Reach for **Moq** when the test needs *behaviour that is hard to produce for real*: an `IOException`
mid-read, a read that returns fewer bytes than asked, a lock that is already taken, a sequence of
calls that must happen in order. That is layer 3, and it is the reason the boundary interfaces exist.

### Using it well

- `MockBehavior.Strict` for boundary contracts: an unexpected call is a design change and should
  fail loudly.
- Assert **outcomes**, not call counts. `Verify` is for interactions that *are* the requirement
  ("the file lock was released"), not as a proxy for a result you could have asserted directly.
- No mock should ever appear in a layer-4 test. If a corpus test needs one, it is testing the wrong
  thing.

---

## 6. When execution contradicts the plan

It will. The rule is that the documents stay true, not that the plan was right.

1. Update `DESIGN.md` / `PLAN.md` **in place** — they describe what we are doing, not what we once
   intended.
2. Note the change and its reason in the step's `STATE.md` while it is live.
3. List it under **Deviations** in `SUMMARY.md` when the step closes.
4. If it is a project-level decision (an architecture change, a rejected approach, a new constraint),
   promote it to `ARCHITECTURE-DECISIONS.md` as a new entry so the next session does not
   relitigate it.

A deviation that is written down is a finding. A deviation that is not is a trap for whoever reads
the code next.

---

## 7. Traceability

- One step → one folder → one commit, or a short series of commits all naming that step.
- The root `STATE.md` names the active step folder; the active step's `STATE.md` names the current
  sub-step.
- `claude/dev/README.md` is the index: one line per step, newest last, with its status.
- Closing a step updates the capability's status in `PORTING-PLAN.md` §5 — that table, not this
  folder, is the project's answer to "what is done".
- Nothing that matters is recorded only in a chat transcript.
