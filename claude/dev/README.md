# Development steps

One folder per step, in order. The method is [`../DEV_APPROACH.md`](../DEV_APPROACH.md); this file is
just the index.

Start a step by copying the template:

```bash
cp -r claude/dev/_template claude/dev/001-step-name
```

Numbers are never reused or renumbered. An abandoned step keeps its folder and gets a `SUMMARY.md`
explaining why — that is a record, not clutter.

| Step | Milestone | Status | What it did |
|---|---|---|---|
| [`001-dbf-open-and-header`](001-dbf-open-and-header/) | `DBF-READ` | **done** | Open a DBF (+ companion FPT) and expose its metadata: header, stored descriptors, resolved field table. 224 tests; gate green on all five corpus tables |
