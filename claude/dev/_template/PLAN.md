# NNN-step-name — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

What each layer proves. Every layer must name something **only it** can catch; a layer with nothing
to add is a layer to drop for this step (`DEV_APPROACH.md` §4).

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | … | … |
| 2 Component | … | … |
| 3 Fault injection | … | … |
| 4 Golden / corpus | … against `net/corpus/….dump.txt` | whether we match the real bytes |

**Corpus coverage.** Which corpus cases this step is gated on, and any path it touches that **no**
corpus case covers — those need a generator case, not a unit test standing in for one.

**Expected values.** Confirm where each expectation comes from: corpus dump, corpus bytes (cite
file + offset), or a round-trip invariant. Never bytes typed in from a spec (`DEV_APPROACH.md` §4).

## Steps

3–8 of them. Each one ends in something runnable or assertable; "write class X" is not a step,
"class X decodes the header and its unit tests pass" is.

| # | Step | Verify by |
|---|---|---|
| 1 | … | … |
| 2 | … | … |

## Gate

The single mechanical check that closes this step. One command, one pass/fail.

## Risks

What could make this step wrong rather than merely late, and the cheapest thing that would expose it
early.
