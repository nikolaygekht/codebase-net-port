# Where the port differs from the reference

**Recorded:** 2026-10-31, during step 013, at the user's request: everything the port deliberately
does not do, refuses, or does differently from CodeBase, gathered in one place. Sources: every
`ErrorCode.NotSupported` throw site in the library, all 67 ADRs, and `PORTING-PLAN.md` §2.2 (out of
scope), §2.3 (later) and §8 (benign divergences).

**"User's call"** marks the entries whose ADR records that the user decided them (ADR-56, 62–67).
The earlier ADRs carry no such mark, so whether the user saw them cannot be told from the record.

## 1. Files and tags the port refuses to read

Here a user hits an error.

| What | The reference | Where decided |
|---|---|---|
| A table with compressed data (`0x31`, flag byte 14) | reads it | ADR-56, **user's call** |
| A table with long field names (CodeBase's descriptor layout, flag byte 16) | reads it | **no ADR**; and the port checks the flag on any version, where the reference reads the flags only on `0x31` (recorded in 012 as seen, not acted on) |
| A field of type `7` (datetime with milliseconds, a CodeBase extension) | reads it | ADR-32 |
| A tag over `DATE()`, `TIME()`, `RECNO()`, `RECCOUNT()` | opens it | ADR-49 (a key that can never be found again) |
| A tag whose expression reads a two-digit year, under the default date policy | takes the century from the system clock | ADR-42 |
| GENERAL on a code page other than 1252/437/850; other collations (croatian, spanish, loaded from disk) | supports some | `PORTING-PLAN.md` §2.3, later |
| A non-compact FoxPro 2.x index | refuses it too | — |
| Keys of the OLE-DB types; collated Unicode keys | supports them | `PORTING-PLAN.md` §2.2 / §2.3 |

## 2. Where the optimizer answers by a scan instead of a tag

The answer is right, only slower.

- **Collated tags — never** (ADR-54, ADR-59). The reference uses them and answers wrongly. Using one
  for equality as an untrusted candidate set is future work (`PORTING-PLAN.md` §2.3).
- **Integer, currency and datetime tags** — only under an explicit `QueryTagScope.AllSeekable`
  (ADR-52). By default the port behaves as the reference, which never uses those types.
- A constant the tag cannot hold exactly (`K_INT = 2.5`) — checked per record (ADR-54).
- Filtered and unique tags, a negation, an `.OR.` with a term no tag answers — as the reference does.

## 3. Where the port's answer differs from the reference's, by decision

- **ADR-53** — two reference defects not replicated: a range bound inverted on a reflected
  comparison, and the residual check reading the wrong positions.
- **ADR-61** — the null that sticks between records is not replicated.
- **ADR-69** (**user's call**) — a `Bottom` that finds nothing sets `Eof` in every path; the
  reference returns `r4eof` in all three but leaves `relate4eof` false in two.
- **ADR-58** — `Seek(string)` only on a character tag; the reference silently converts text to a
  number.
- **ADR-64** (**user's call**) — a `DESCEND` tag is sought through the tag's transform.
- **ADR-65** (**user's call**) — `UPPER`/`LOWER` by the table's code page; the reference uses the
  host's.
- **ADR-66** (**user's call**) — filter literals in the table's encoding.
- **ADR-63** (**user's call**) — the future index writer without `i4tagAdd`'s omissions.
- **ADR-45, ADR-50** — for prefix `=` and division by zero, CodeBase was chosen over VFP.
- **Owed from 010** — `MOD` by zero and `SQRT` of a negative answer 0 where VFP raises; VFP's
  `PROPER` is not in the port.

## 4. Out of scope altogether

`PORTING-PLAN.md` §2.2: client/server, MDX/NTX, dBase III memo, the report writer, encryption,
`largeFileOffset`, dates before AD 1 (ADR-33). Later: relations between tables; `EXPR-ALIAS` is
already planned as step 014.

## Worth looking at first

1. **Long field names are refused with no ADR, and possibly on the wrong version.** The one refusal
   with no recorded decision.
2. **ADR-49 and ADR-42 refuse tags the reference opens.** A table with such a tag reads, but the tag
   cannot be used.
