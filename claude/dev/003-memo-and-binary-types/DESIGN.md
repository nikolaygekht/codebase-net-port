# 003-memo-and-binary-types — design

Phases 1, 2 and 4 of [`DEV_APPROACH.md`](../../DEV_APPROACH.md). Written **before** any code.

## Goal

Read the payload behind a memo reference, in both the reference encodings the format uses, and read
the two in-record binary types. **Gate:** for all five corpus tables that have an `.fpt`, the memo
half of the `[records]` section — every `ref=`, every `len=`, and every payload byte — plus
`VFPMEMO`'s `BINCHAR`, the one ordinary field step 002 skipped. That is 224 memo values, 153 of them
non-empty, and the last thing in the dump that nothing asserts.

**Capability:** `DBF-READ`, memo half (`PORTING-PLAN.md` §5)  ·  **Governing spec(s):**
`specs/FPT-MEMO.md` §3, `specs/DBF-FORMAT.md` §5-6

## Not in this step

| Deferred | To | Why |
|---|---|---|
| **Writing a memo** — allocation, update-in-place, the `nextBlock` pointer, orphaned blocks | `WRITE` | Read path only, as steps 001 and 002 were. The allocation rules are the larger half of `FPT-MEMO.md` §3.7 and none of them can be gated by a corpus that is only ever read |
| **Compaction** (`d4memoCompress`) | `WRITE` | Rewrites the whole file; meaningless without a write path |
| **The `F4MEMO` cache** — `status`, `isChanged`, `contentsOld` | never, probably | It exists to support write-back and to invalidate index keys built over memo expressions. Neither exists yet, and a read-only reader that caches is an optimization we have no measurement for. Decision 6 |
| **The DBT formats** (dBase III/IV, `S4MMDX`/`S4MNDX`) | out of scope | ADR-08: FoxPro 2.x is the legacy-memo case, and `.dbt` cannot be produced from the S4FOX build at all |
| **Locking** the memo file | `LOCKING` | The 0x40000000 byte lock serializes *allocation*. Nothing here allocates |

## Classes

| Class | Role | Responsibility | Notes |
|---|---|---|---|
| `MemoReference` | Entity | Turns a memo field's in-record bytes into a block number | Pure. Two encodings, chosen by the declared width — Decision 2 |
| `MemoBlockHeader` | Entity | The eight bytes that begin an entry: type and payload length, both big-endian | Pure, mirrors `MemoFileHeader` which already exists |
| `MemoType` | Entity | The four entry types the format defines, as an enum | So that "picture" and "OLE" are values rather than bare integers |
| `MemoReader` | Controller | Reads the entry at a block number out of an `IRandomAccessSource` | Owns no handle. Where the corruption guards live |
| `MemoEntry` | Entity | One entry read back: its type and its payload | A value, so a caller can tell an empty memo from a picture of nothing |
| `Table` (extended) | Controller | Gains the memo accessors and the binary-field rules | Already the façade; no new public entry point |

`FieldValueDecoder` gains the binary-field rules rather than a new class: it already holds the
per-type matrix and this is three more rows in it.

## Public surface

```csharp
using var engine = new CodeBaseEngine();
using Table table = engine.OpenTable("customer.dbf");
table.Go(1);

FieldDefinition notes = table.Fields["NOTES"];

table.GetMemoLength(notes);      // int — payload bytes, 0 when there is no memo
table.GetMemoBytes(notes);       // byte[] — the payload, verbatim
table.GetMemoString(notes);      // string — decoded with the table's code page (ADR-21)
table.GetMemoBlock(notes);       // int — the stored block number, 0 for none. Diagnostics
table.GetMemoType(notes);        // MemoType — Text, Picture, ObjectLinking, Compressed
```

**Deliberately not exposed yet:** a stream over a memo (`f4memoFile`'s territory, and a
write-path shape), partial reads at an offset, and any way to ask for a memo without a positioned
record. Reading a memo needs the cursor to be on the record that names it, exactly as a field read
does.

## Seams

| Seam | Interface | Faked how | Used to test |
|---|---|---|---|
| memo file reads | `IRandomAccessSource` | `InMemorySource` over a hand-built `.fpt` image | every entry shape, layer 2 |
| memo read failures | `IRandomAccessSource` | `FaultySource`, and a truncated image | an entry whose payload runs off the end, a header that will not read, layer 3 |
| the entry bytes | none needed | `MemoBlockHeader.Parse` takes a span | layer 1, no file anywhere |

The memo file is **already open**: `DbfOpener` opens it and reads its header when the table declares
one, and `OpenedTable.Memo` holds the source. Nothing new is opened in this step, which is why there
is no new boundary.

## Decisions

1. **A memo read is a read, not a lookup of something already loaded.** `GetMemoBytes` seeks and
   reads on each call. The C library caches per field (`F4MEMO`, `FPT-MEMO.md` §3.8), but that cache
   exists to hold *pending writes* and the *previous* value for index-key maintenance. Neither is in
   this port. Caching a read-only value would be an optimization with no measurement behind it and a
   staleness question attached to it.
   *Rejected — caching per field per record.* Reconsider when `WRITE` needs the dirty flag anyway.

2. **The reference encoding is chosen by the field's declared width, not by the table version.**
   Four bytes means a little-endian signed integer; anything else means right-aligned ASCII digits.
   That is exactly the test `f4long` makes — `if (f4len(field) == 4)` then the binary path, otherwise
   fall through to the ASCII conversion (`F4LONG.C:365-368, 343`). Keying off the version would be
   wrong for a `0x30` table someone created at compatibility 25.

3. **The ten-byte reference is right-aligned and space-padded, and this is now witnessed.**
   `FPT-MEMO.md` §7 lists the padding as its first open question, because `c4ltoa45`'s body is not in
   this source drop. `F2XMEMO` answers it: block 1 is `"         1"`, block 10 is `"        10"`, and
   an absent memo is ten spaces. Blank parses as zero, which means no memo. **The spec gets updated
   in this step**, from UNVERIFIED to witnessed-by-corpus.

4. **Block number zero or below means no memo, and is not an error.** `memo4fileReadPart` returns
   length zero before touching the file (`m4file.c:175-179`). So an empty memo and a missing one are
   the same thing, `GetMemoLength` answers 0, and `GetMemoBytes` answers an empty array rather than
   null. A caller that wants to distinguish "no memo" from "an empty one" cannot, because the format
   cannot.

5. **The payload length is the entry's `numChars`, and it is the payload only.** The struct comment
   in the C says it includes the block header; the FOX code stores the payload length and adds the
   header size separately (`FPT-MEMO.md` §3.2). Verified against real bytes before this design was
   written: for all 153 non-empty entries in the corpus, `numChars` equals the payload length the
   dump records, and the payload starts at `blockNumber * blockSize + 8`.

6. **An entry may span blocks, and nothing about reading it is block-wise.** The payload is
   `numChars` contiguous bytes after the header; blocks matter only for *addressing* the entry and
   for allocation, which this step does not do. `VFPMEMO` and `F2XMEMO` each hold a 505-byte payload,
   which with its header is 513 bytes and therefore crosses a 512-byte boundary. That is the only
   straddling length in the corpus, so it is thin cover for a rule that is otherwise trivially right.

7. **Block size zero means one, not an error.** `m4file.c:620-631` — "FoxPro now supports block size
   of '0' - which means '1'". Every corpus file uses 512, so this is ungated and must be listed as
   such. It matters because a zero block size makes every entry byte-addressed, and a reader that
   divided by it would fault instead.

8. **Compressed entries are refused — not because the format is unknown, but because it cannot be
   gated.** The stream format is no longer a mystery, and that is worth writing down even though the
   decision does not change:

   - **It is zlib, and it is wrapped.** `source/zlib.h` is in the drop (v1.1.4), and the sibling
     compressor that *is* present — `connect4lowCompress` / `connect4lowUncompress`,
     `c4conlow.c:69-152` — calls zlib's high-level `compress2()` and `uncompress()`. Those produce
     and consume an RFC 1950 zlib-wrapped stream, not raw deflate. `FPT-MEMO.md` §3.9 lists exactly
     that question as UNVERIFIED; the sibling call site answers it by strong inference.
   - **The trailing flag on `c4compress` means "prefix the uncompressed length".** The memo path
     passes `1` (`m4file.c:762`); every other caller passes `0` (`D4CREATE.C:2790`, `2852`,
     `f4create.c:1196`, `f4write.c:412`). The buffer is sized `ptrLen*1.001 + 1 + 12 + sizeof(long)`
     — "extra bytes required - long for length, extra for zlib" — and the reader takes a 4-byte
     native-endian length off the front of the data before inflating (`m4file.c:199-212`). So the
     memo layout differs from the file-compression layout by that prefix.
   - **A second, incompatible algorithm exists.** `S4COMPRESS_QUICKLZ` is a real build option
     (`D4all.h:126`, commented out, no sources shipped). Nothing in a type-3 entry says which
     algorithm produced it, so the entry is **not self-describing** — a file from a QuickLZ build is
     indistinguishable on disk from a zlib one.

   **Three separate things, which must not be run together:**

   - **"zlib is not shipped in the drop" — true, and irrelevant.** It blocks nothing. The *reader*
     needs no library at all: `System.IO.Compression.ZLibStream` is in the base class library, so
     type 3 costs no NuGet dependency and ADR-17 is untouched. The *generator* would need zlib to
     write one, and adding zlib to an MSVC build is ordinary work, not an obstacle.
   - **"`c4compress`'s body is missing" — true, and a small unknown, not a wall.** It is declaration
     only (`d4declar.h:2451-2452`; the "moved to c4code.c" comment at `c4conlow.c:67` points at a
     drop we do not have, and `C4CODE.C` here has none). But the *reader* pins the layout down
     precisely — 4-byte native-endian uncompressed length, then the wrapped stream
     (`m4file.c:199-212`) — so reconstructing the wrapper is a short, well-specified job rather than
     guesswork.
   - **"there is no corpus case" — true, and this is the actual reason.** Every one of the 153
     entries is type 1, because our generator compiles with `S4OFF_COMPRESS`
     (`test-files-generator/src/cb-config.h:55`). An inflate path with no case behind it is a
     decoder nothing can contradict, and that is what `DEV_APPROACH.md` §4 rules out.

   So the decision is **sequencing, not impossibility**: type 3 is out of *this* step because the
   corpus does not yet cover it, and it is refused rather than half-implemented. Lifting it is a
   `CORPUS` task of modest size — add zlib to the generator, reconstruct the `c4compress` wrapper
   from the reader, and add a case with `code4memoCompress` on and a payload longer than one block
   (writing is opt-in *and* only above one block, `m4file.c:734-743`). Once that case exists the
   reader is a few lines over `ZLibStream`. **See Q4:** the recommendation is to do it, as its own
   step after this one. Being a CodeBase-only feature is a reason to support it, not to skip it —
   this is a port of CodeBase, and files the original library writes are the primary input.

   **The refusal names the flag, not just the type.** `FeatureFlags.MayHaveCompressedMemos` is
   already parsed from DBF `flags[1]` by step 001 (`D4OPEN.C:2193-2195`), so a table created with
   compression enabled can be identified as such. The message says that the table was created with
   compressed memos enabled and that this port does not read them yet — which is a far better thing
   to meet than "unknown memo type 3".

   *Rejected — returning the compressed bytes as if they were the payload.* That hands the caller
   plausible garbage, which is the failure this library cares most about.
   *Rejected — inflating now and gating later.* It would very likely work, and "very likely" is not
   a gate; a QuickLZ-built file would also inflate to nonsense rather than to an error.

9. **Types 0 and 2 are read and reported, not refused.** Picture and OLE-object entries hold bytes
   like any other; CodeBase echoes the type back and never validates it (`m4file.c:257`,
   `FPT-MEMO.md` §7 item 7). So `GetMemoType` reports what the file says and `GetMemoBytes` returns
   the payload. Ungated — every corpus entry is type 1.

10. **`GetMemoString` decodes by the same rules as `GetString`, and trailing blanks do not arise.**
    ADR-21 governs: the table's code page, best-effort recovery, never throwing. A memo payload has
    no declared width and so no padding, which makes ADR-22's trimming question moot here. `CP936`
    settles that the cut-character rule is exercised on this path too and not only at a field
    boundary: its memo payloads of 63 and 401 bytes both end on a dangling GBK lead byte.

11. **A binary memo refuses to be read as text.** `X` (binary memo) and `G` (general) hold bytes that
    are not in the table's code page — that is the whole meaning of the binary flag. `GetMemoString`
    on either throws; `GetMemoBytes` is the accessor for them. `M` is the text one.
    *Rejected — decoding them anyway.* It always produces something and the something is meaningless,
    and the caller has no way to notice.

12. **`Z` is not a memo at all, and this step is where that gets said out loud.** A binary character
    field is stored in the record like any other character field and is marked binary only so that
    nothing transcodes it. It is in this step because it is the last of the four letters step 002
    deferred, not because it shares any machinery. Its whole contribution is: its raw bytes join the
    gate, and `GetString` refuses it for the same reason `GetMemoString` refuses `X`.

13. **A memo read validates against the physical file, and says which entry failed.** Three guards,
    all from `m4file.c:229-238`: a length above `0x7FFFFFF0` is refused, a payload that would run past
    the end of the file is refused, and a block number whose header lies past the end of the file is
    refused. The message names the field, the record and the block, because "the memo file is
    corrupt" sends a reader nowhere.

14. **Accessors live on `Table`, keyed by a `FieldDefinition`,** as step 002's Decision 13 settled.
    No new argument about API shape; this is the same shape.

## Open questions

| # | Question | Status |
|---|---|---|
| Q1 | **Does `GetMemoType` earn its place in the public surface?** Every corpus entry is type 1, so it is an accessor with one gated answer, and the types it exists to report cannot be produced by anything we have. | **Open — decide during execution.** The argument for it is that `FPT-MEMO.md` §7 item 7 says a port should preserve unknown types rather than validate them, and reporting is how a caller sees one. The argument against is ADR-22's rule about API ahead of a caller. If it goes, `MemoEntry` keeps the type internally and compressed entries still throw |
| Q2 | **Should `GetMemoBytes` on a `C`, `N` or other non-memo field throw, or is that over-policing?** The refusal matrix in `FieldValueDecoder` says throw, consistently with every other accessor. | **Leaning throw**, for consistency with the eleven refusals already there. Settle in sub-step 4 when the matrix rows are written |
| Q3 | **What does a record read at end of file report for a memo field?** The blank record blanks the *reference* to zeros or spaces, so the reference reads as "no memo" and the payload is empty. That falls out of Decision 4 rather than needing its own rule — but it is untested until it is tested. | **Answered by a test, not a decision.** Sub-step 5 |
| Q4 | **Should the compressed-entry corpus case be added, making type 3 readable?** Decision 8 refuses type 3 only because nothing can gate it. That is a corpus gap, not a technical one: the reader needs no dependency (`ZLibStream` is in the base class library), and the generator needs zlib plus a reconstruction of the `c4compress` wrapper, whose layout the reader already pins down. | **Open — the user's call. Recommendation: yes, but as its own step after 003.** An earlier draft of this row recommended declining, on the grounds that type 3 is CodeBase-only and no Visual FoxPro file would hold one. That reasoning was **backwards for this project**, and the argument is recorded here so it is not made again: this is a port *of CodeBase*, `CLAUDE.md` requires that files the original C library writes are read correctly here, and the likely user is migrating an existing CodeBase application. If that application ever called `code4memoCompress`, its memo files hold type-3 entries and refusing them refuses the data the user came for. What is fair to say against doing it *now* is only sequencing: it needs a third-party source added to the generator (ADR-02 territory) plus a reconstructed `c4compress`, which is a `CORPUS` change with its own design, and folding it into 003 would make this step two subsystems wide — the thing `DEV_APPROACH.md` §1 forbids and that 003's own scope table already invokes against memo writing |
