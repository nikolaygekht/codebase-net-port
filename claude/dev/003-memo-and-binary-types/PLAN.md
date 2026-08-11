# 003-memo-and-binary-types — plan

Phases 3 and 5 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written after `DESIGN.md`, before code.

## Tests

| Layer | Tests | Catches what nothing below can |
|---|---|---|
| 1 Unit | `MemoReference` in both encodings; `MemoBlockHeader` big-endian parse; `MemoType` mapping; the `FieldValueDecoder` rows for `M`, `X`, `G`, `Z` including every refusal | Values no corpus file holds: a block size of zero, a type of 0, 2 or 3, a ten-byte reference that is blank, negative or not a number, and a length above the corruption ceiling |
| 2 Component | `MemoReader` over `InMemorySource` on a hand-built `.fpt`: entry at block *n*, payload spanning two and three blocks, an empty entry, two entries back to back | That the offset formula and the header parse agree. A payload read from the right block at the wrong offset is still eight plausible bytes of the previous header |
| 3 Fault injection | `FaultySource` and truncated images: a payload running past the end of the file, a block header past the end, an `IOException` mid-payload, a length above `0x7FFFFFF0` | That a short read **refuses** rather than returning what it got. A truncated memo returning a truncated string is the failure that looks most like data |
| 4 Golden / corpus | The memo half of `[records]` for all five tables with an `.fpt`, plus `VFPMEMO.BINCHAR`, through the public API | Whether we match the real bytes at all. Layers 1-3 can pass on a self-consistent misreading of the reference encoding |

**Corpus coverage.** Gated on the five tables with an `.fpt`: `F2XMEMO` (the ten-byte ASCII
reference, 28 non-empty entries), `VFPMEMO` (four-byte binary references over `M`, `X` and `G`, plus
the `Z` field, 56 non-empty), `VFPNULL` (17 non-empty, and the memo-versus-null interaction),
`CP1251` and `CP936` (26 each, memo text under a marked code page). **224 memo values, 153 of them
non-empty.**

Touched but **uncovered by any corpus case**, and to be listed as ungated in `SUMMARY.md`:

- **A block size other than 512**, and in particular **zero**, which means byte granularity
  (Decision 7). Every corpus `.fpt` is 512.
- **Entry types 0, 2 and 3.** All 153 entries are type 1. Type 3 is refused by Decision 8, so what is
  ungated is the refusal firing on a real file rather than a hand-built one.
- **A payload spanning more than two blocks.** The longest is 505 bytes, which with its header is 513
  and so crosses exactly one boundary. Three-block cover is layer 2 only.
- **A `G` field with much in it.** Four non-empty values, longest 24 bytes.

Each is a cheap generator case if it turns out to matter; the block-size-zero one is the only one
likely to, and it is worth deciding during execution whether it belongs here or with `WRITE`.

**Expected values.** Every payload, length and reference comes from `net/corpus/<NAME>.dump.txt`,
which the C library wrote — already parsed and kept by `CorpusDump`, deliberately, so this step adds
assertions and not parsing. **One exception, declared:** decoded memo *strings* for `CP1251` and
`CP936` come from the generator's documented input, as ADR-21 allows and as step 002 already does for
character fields. Hand-built `.fpt` images in layers 1-3 are **input**, never expectation.

## Steps

| # | Step | Verify by |
|---|---|---|
| 1 | **`MemoReference` reads both encodings.** Four bytes little-endian; anything else right-aligned ASCII, blank meaning zero | Unit: *`\x02\x00\x00\x00` is block 2*; *`"         1"` is block 1 and `"        10"` is 10*; *ten spaces are 0*; *a ten-byte field of junk is 0 rather than an error*; *the width decides, not the table version* (Decision 2). **Golden:** *every `ref=` in all five dumps parses to the block number the payload is actually at* — which is checkable because the dump gives both |
| 2 | **`MemoBlockHeader` and `MemoType`.** Two big-endian numbers and the type enum | Unit: *type and length are read big-endian, not little*; *a header of `00 00 00 01 00 00 01 F9` is text of 505 bytes*; *type 3 maps to Compressed*; *an unknown type is preserved rather than rejected* (Decision 9) |
| 3 | **`MemoReader` fetches an entry.** Offset from the block number and block size; the three corruption guards | Component over `InMemorySource`: *the entry at block *n* starts at `n * blockSize`*; *the payload starts eight bytes later*; *a payload crossing two and three blocks reads whole*; *block size zero addresses by the byte* (Decision 7). Layer 3: *a payload running past the end of the file is refused*; *a header past the end is refused*; *a length above `0x7FFFFFF0` is refused*; *the message names the block* |
| 4 | **The `FieldValueDecoder` rows for `M`, `X`, `G` and `Z`,** including the refusals | Unit: *`GetMemoBytes` on a `C` field throws* (Q2); *`GetMemoString` on `X` or `G` throws* (Decision 11); *`GetString` on `Z` throws* (Decision 12); *`GetRawBytes` on `Z` returns its declared width*. **Golden:** *`VFPMEMO.BINCHAR`'s raw bytes match the dump in all 32 records* — the field step 002 skipped |
| 5 | **The memo accessors on `Table`.** `GetMemoLength`, `GetMemoBytes`, `GetMemoBlock`, and `GetMemoType` if Q1 keeps it | Component: *a reference of zero reads as length 0 and an empty array, touching no file* (Decision 4); *at end of file every memo field reads as absent* (Q3); *a closed table refuses*. **Golden:** *every `len=` in all five dumps*, which is 224 assertions before a single payload byte is compared |
| 6 | **The payloads.** `GetMemoBytes` over the whole corpus | **Golden:** *every payload byte of all 153 non-empty entries in all five tables*. Plus a **mutation check**: change the payload offset from `+8` to `+0` and confirm every memo table goes red, as sub-step 3 of step 002 did for the record offset |
| 7 | **`GetMemoString` and the code pages.** Per ADR-21, on the memo path | Unit: *a payload ending on a dangling lead byte yields its whole characters and a replacement*; *a binary memo refuses*. **Golden:** *`CP1251`'s memo text decodes to the generator's documented input*; *`CP936`'s 63-byte and 401-byte payloads each end in a replacement character and never throw* |
| 8 | **The gate: extend `RecordGoldenTests` to stop skipping.** The memo branch becomes assertions; the skip counter goes to zero | The gate below. The counter is what proves it: sub-step 9 of step 002 made the suite assert that fields asserted plus memo fields skipped equals the field count, so the skip dropping to zero is checked arithmetic rather than a claim |
| 9 | **Update `FPT-MEMO.md`** — *already done while designing, so this row is a check rather than a task.* Open question 1 answered by the corpus (Decision 3); §3.2's payload-only rule witnessed rather than argued; §3.9 and open question 3 rewritten now that the compressed-stream format is resolved (Decision 8) | The spec diff, which is committed with the plan. Confirm during execution that nothing in §3 contradicts what the code ended up doing |

## Gate

```
dotnet test net/CodeBase.Net.sln
```

green, with the record golden suite asserting, for **all seven** corpus tables and every one of their
32 records, **every field with nothing skipped** — the ordinary fields step 002 gated, plus every
`ref=`, `len=` and payload byte, plus `BINCHAR`. The suite must report a **skip count of zero** and a
non-zero assertion count: it already asserts that the two add up to the field count, so the gate is
that the arithmetic still holds with the skip term gone.

## Risks

| Risk | Why it would make this step *wrong* | Cheapest early exposure |
|---|---|---|
| **The reference encoding is chosen wrongly** — by version rather than by width | Every memo in a `0x30` table created at compatibility 25 reads from the wrong block, and reads *something*, because any block number lands somewhere in the file | Sub-step 1's golden check: every `ref=` must resolve to the block whose payload the dump records. Wrong-but-plausible fails immediately |
| **`numChars` read as including the header** | Every payload short by eight bytes, and the last eight bytes of every memo silently lost — a truncation that looks like data | Verified against real bytes before this design; sub-step 6 re-checks it over all 153 entries |
| **The payload offset is wrong by the header size** | Every memo returns eight bytes of its own header followed by all but the last eight of its text | The mutation check in sub-step 6, which is why that sub-step names it explicitly |
| **Big-endian read as little-endian** | A 505-byte entry reads as length 4,177,526,784 and trips the corruption guard, so this one fails loudly rather than quietly — but only if the guard is right | Sub-step 2 unit tests, then sub-step 3's guards. The failure mode is good; the guard is what makes it good |
| **A truncated memo file returns what it got** | The most data-shaped failure available: a short string that looks like a short memo | Layer 3 is written in sub-step 3, before any golden payload assertion exists to be reassured by |
| **Block size zero divides by zero** | A legal file faults instead of reading | Sub-step 3 covers it at layer 2; ungated by the corpus and named as such |
