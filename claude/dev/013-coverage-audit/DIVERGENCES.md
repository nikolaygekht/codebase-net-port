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
| A table with compressed data (`0x31`, flag byte 14) | reads it | **Kept 2026-11-12 (7a)**, user's call reconfirmed: ADR-56. Only compressed memo entries are supported (ADR-55). Documented on `CodeBaseEngine.OpenTable` and in `README.md` |
| ~~A table with long field names (CodeBase's descriptor layout, flag byte 16)~~ | reads it | **Resolved 2026-11-12 (7a-1): supported**, user's call (ADR-72). The layout is read and gated by `LONGNAME`; the feature flags are read on `0x31` only, as the reference, gated by `FLAGS30` |
| ~~A field of type `7` (datetime with milliseconds, a CodeBase extension)~~ | reads it | **Resolved 2026-11-12 (7a-4): supported**, user's call (ADR-75, superseding ADR-32): read, evaluated and keyed, gated by `DTMILLI` |
| A tag over `DATE()`, `TIME()`, `RECCOUNT()` | opens it | **Kept 2026-11-12 (7a-2)**, user's call: ADR-73, which supersedes ADR-49 in part; documented on `Table.SelectTag` and in `README.md`. ~~`RECNO()`~~ is **supported** now, gated by `RECNOTAG` |
| A tag keyed through the date picture (`DTOC`, `CTOD`), under the engine's default options | reads it under its compiled-in picture | **Implemented 2026-11-12 (7a-3)**, user's call: ADR-42's refusal had never been written, and the gap gave a wrong record set; ADR-74. Documented on `CodeBaseEngine.ExpressionOptions`, `Table.SelectTag` and in `README.md` |
| CodeBase's own collations, `sortSeq` `"CBnnnnn"`: croatian and croatianUpper (cp1250), spanish (cp1252, cp850), avaya1252 | reads and writes them | **Decided 2026-11-14 (7a): to be supported**, user's call — weights copied verbatim from `COLL4ARR.C`, a corpus case by the C generator. Planned as 7a-11 |
| A collation loaded from disk: `"CB00004"`, `collate4test`, read from `coll4inf`/`collate4`/`compres4` tables in the current directory (u4util.c:2572-2700) | reads it | **Decided 2026-11-14 (7a): refused**, user's call — the format is understood, but no such tables ship anywhere: the slot is a hook a CodeBase user fills by hand. ADR and documentation to follow |
| GENERAL on a code page other than 1252/437/850, and VFP's named collations | refuses cp1250 (E84907); on other pages no branch matches (i4init.c:378-405) | **Decided 2026-11-14 (7a): to be tried**, user's call — VFP's weights recovered from keys VFP writes, through the VFP oracle. Planned as 7a-13. Not a divergence from CodeBase but VFP compatibility beyond it |
| ~~The `W` and `O` UTF-16 fields~~ | reads them | **Resolved 2026-11-12 (7a-5): supported**, user's call (ADR-76): read, machine-ordered keys and seeks, `STR()` by the table's code page (ADR-77), gated by `WSTR`; `W` on a `0x32` table refused as VFP 9's Blob |
| Keys of the OLE-DB types | supports them | **Decided 2026-11-14 (7a): not supported**, user's call — out of scope, `PORTING-PLAN.md` §2.2 |
| Collated Unicode keys (a `W` tag under a collation) | supports them, the Unicode arrays derived from the character ones (`collate4setupUnicodeFromChar`, u4util.c) | **Decided 2026-11-14 (7a): to be supported**, user's call |

## 2. Where the optimizer answers by a scan instead of a tag

The answer is right, only slower. **Kept as it is, 2026-11-14, user's call;** five ways to answer more
terms by a tag are recorded for later in `PORTING-PLAN.md` §2.3, beside the collated-equality item.

- **Collated tags — never** (ADR-54, ADR-59). The reference uses them and answers wrongly. Using one
  for equality as an untrusted candidate set is future work (`PORTING-PLAN.md` §2.3).
- **Integer, currency and datetime tags** — only under an explicit `QueryTagScope.AllSeekable`
  (ADR-52). By default the port behaves as the reference, which never uses those types.
- A constant the tag cannot hold exactly (`K_INT = 2.5`) — checked per record (ADR-54).
- Filtered and unique tags, a negation, an `.OR.` with a term no tag answers — as the reference does.

## 3. Where the port's answer differs from the reference's, by decision

- **ADR-53** — two reference defects not replicated: a range bound inverted on a reflected
  comparison, and the residual check reading the wrong positions. **Kept 2026-11-14, user's call.**
- **ADR-61** — the null that sticks between records is not replicated. **Kept 2026-11-14, user's call.**
- **ADR-69** (**user's call**) — a `Bottom` that finds nothing sets `Eof` in every path; the
  reference returns `r4eof` in all three but leaves `relate4eof` false in two.
- **ADR-58** — `Seek(string)` only on a character tag; the reference silently converts text to a
  number. **Kept 2026-11-14, user's call, and `Seek(decimal)` and `Seek(DateTime)` added** (7a-8,
  ADR-80), so currency and datetime tags are sought by value.
- **ADR-64** (**user's call**) — a `DESCEND` tag is sought through the tag's transform.
- **ADR-65** (**user's call**) — `UPPER`/`LOWER` by the table's code page; the reference uses the
  host's.
- **ADR-66** (**user's call**) — filter literals in the table's encoding.
- **ADR-71** (**user's call**) — a date picture with four `Y` and no `C` (the Swedish and Finnish
  builds' `YYYY-MM-DD`) writes the whole year; the reference writes two digits and the letters `YY`.
- **ADR-63** (**user's call**) — the future index writer without `i4tagAdd`'s omissions.
- **ADR-78** (**user's call**, 2026-11-14) — a `T` key is Visual FoxPro's conversion, deciphered by
  the VFP oracle; the reference's bitmap is that conversion sampled around 2000 and one unit off on
  some seconds of other days, so a seek there finds what VFP finds and not what the reference wrote.
  The bitmap is kept for `'7'`.
- **ADR-79** (**user's call**, 2026-11-14) — `GetDateTime` reads a `T` field to the second; VFP stores
  about half its values one millisecond short.
- **ADR-45, ADR-50** — for prefix `=` and division by zero, CodeBase was chosen over VFP. **Kept
  2026-11-14, user's call; to be documented** for the user.
- **Owed from 010** — `MOD` by zero and `SQRT` of a negative answer 0 where VFP raises; VFP's
  `PROPER` is not in the port. **Decided 2026-11-14, user's call:** `MOD` by zero and `SQRT` of a
  negative **raise an error, as VFP does** (they are VFP's functions, CodeBase has neither); `PROPER`
  **to be added**, gated by `VFPFUNC.CDX`'s `T_PROPER` keys. **`MOD`/`SQRT` done (7a-9, ADR-81),**
  witnessed by `vfpband.prg`, with VFP's laziness in `IIF`/`AND`/`OR` so its guards hold; and a
  finding of that design, also done: the thirteen dropped a null operand's marking, a wrong record
  set (`ABS(K_NNUM) = 0` admitted nulls) — now null as in VFP (ADR-82, the user's call 2026-11-15).
  **`PROPER` done (7a-10, ADR-83)**, its word rule witnessed by `vfpprop.prg` over every byte.

## 4. Out of scope altogether

`PORTING-PLAN.md` §2.2: client/server, MDX/NTX, dBase III memo, the report writer, encryption,
`largeFileOffset`, dates before AD 1 (ADR-33). Later: relations between tables; `EXPR-ALIAS` is
already planned as step 014.

## Worth looking at first

1. ~~**Long field names are refused with no ADR, and possibly on the wrong version.**~~ Resolved in
   7a-1: supported, and the flags read on `0x31` only (ADR-72).
2. **ADR-49 and ADR-42 refuse tags the reference opens.** ADR-49 resolved in 7a-2: `RECNO()`
   supported, the other three kept and documented (ADR-73). ADR-42 turned out to refuse nothing — its
   refusal was never implemented, and a tag keyed through the date picture answers a query wrongly;
   that is 7a-3.
