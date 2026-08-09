# NNN-step-name — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

One or two sentences: what this step makes possible, and how we will know it works. Name the
verification here — if the goal cannot state its own gate, the step is not framed yet.

**Capability:** `…` (`PORTING-PLAN.md` §5)  ·  **Governing spec(s):** `specs/….md`

## Not in this step

What a reader might reasonably expect here but is deliberately deferred, and to where. Keeps the
step small and stops scope creep being mistaken for progress.

## Classes

One row per class. Every class gets exactly one ECB role (`DEV_APPROACH.md` §3.1) and one sentence
of responsibility — if the sentence needs an "and", split the class.

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `Xxx` | Entity | … | pure: span in, values out |
| `IYyy` | Boundary | … | the only place I/O happens |
| `ZzzReader` | Controller | … | takes `IYyy`, owns no handles |

## Public surface

The API this step adds or changes, and what it deliberately does not expose yet.

## Seams

What the tests need to control, and the interface that lets them (`DEV_APPROACH.md` §5).

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| file reads | `IRandomAccessSource` | in-memory fake over `byte[]` | happy paths, layer 2 |
| read failures | `IRandomAccessSource` | Moq | truncation, `IOException`, layer 3 |

## Decisions

Choices made here that a later reader should not have to re-derive — and the alternatives rejected,
with the reason. Format facts belong in the specs, not here.

## Open questions

Anything that must be answered to finish this step, and who or what answers it.
