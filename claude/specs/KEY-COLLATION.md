# CDX Index Key Encoding and Collation — CodeBase (S4FOX / Visual FoxPro compatible)

**Scope.** This document specifies, at implementation level, how CodeBase transforms expression
results into the sortable byte strings stored in compact CDX indexes (S4FOX build), including all
collation tables, the descending/null rules, and how a C# port must reproduce them byte-for-byte.
S4MDX / S4CLIPPER divergences are noted only briefly. S4CLIENT code paths are ignored.

All facts cite `FILE:line` in `original/source`. File names on disk are mixed-case
(`i4conv.c`, `u4util.c`, `E4EXPR.C`, `COLL4ARR.C`, …); the directory is case-insensitive.

---

## 1. Architecture of key production

Every open tag (`TAG4FILE`) carries three function pointers plus collation state
(d4data.h:4026-4074):

| Field | Meaning |
|---|---|
| `cmp` | key comparison used while walking blocks; for S4FOX always `t4cdxCmp` (i4init.c:325, 559) |
| `stok` | "string-to-key": converts a seek string into key bytes (`C4STOK` signature, d4data.h:204-208) |
| `dtok` | "double-to-key": converts a numeric seek value into key bytes (`C4DTOK`) |
| `collateName` | `enum Collate4name` — which collation this tag uses (d4data.h:4070) |
| `isUnicode` | true for `r5wstr`/`r5wstrLen` expressions (d4data.h:4071) |
| `pChar` | trailing-pad byte used by the CDX leaf trail-count compression (d4data.h:4072) |

There are two independent key-building paths that MUST produce identical bytes:

1. **Index maintenance path** — `expr4key()` evaluates the tag expression and converts the raw
   result via `expr4keyConvert()` (E4EXPR.C:755-812). Strings go through
   `expr4keyConvertIndexStr()` (E4EXPR.C:656-751); all other types through the S4FOX
   `expr4keyConvertIndexDependent()` (E4EXPR.C:318-556).
2. **Seek path** — `tfile4stok()` / `tfile4dtok()` (D4SEEK.C:39-191) call the per-tag
   `stok`/`dtok` selected by `tfile4initSeekConv()` (i4init.c:557-753).

`tfile4initSeekConv` selection table (i4init.c:561-732):

| Expression type | `stok` | `dtok` | `pChar` |
|---|---|---|---|
| `r4date`, `r4dateDoub` | `t4dtstrToFox` | `t4dblToFox` | `'\0'` (i4init.c:563-568) |
| `r4numDoub` w/ currency field | `t4strToCur` | `t4dblToCurFox` | `'\0'` (i4init.c:570-576) |
| `r4num`, `r4numDoub` | `t4strToFox` | `t4dblToFox` | `'\0'` (i4init.c:578-582) |
| `r4str`, machine collation | `t4noChangeStr` | 0 | `' '` (i4init.c:587-591) |
| `r4str`, `collate4subSortCompress` | `t4convertSubSortCompressChar` | 0 | `'\0'` (i4init.c:602-608) |
| `r4str`, `collate4simple` | `t4convertSimpleChar` | 0 | `'\0'` (i4init.c:610-612) |
| `r5wstr`/`r5wstrLen`, machine | `t4unicodeToMachine` | 0 | `'\0'` (i4init.c:628-634) |
| `r5wstr`/`r5wstrLen`, subSortCompress | `t4convertSubSortCompressUnicode` | 0 | `'\0'` (i4init.c:656-658) |
| `r4log` | `t4strToLog` | 0 | (i4init.c:668-671) |
| `r4floatBin` | `t4strToFloat` | `t4dblToFloat` | `'\0'` (i4init.c:674-678) |
| `r5ui4`, `r5ui2` | `t4strToUnsignedInt` | `t4dblToUnsignedInt` | `'\0'` (i4init.c:679-684) |
| `r5i2`, `r4int` | `t4strToInt` | `t4dblToInt` | `'\0'` (i4init.c:685-690) |
| `r5i8` | `t4strToLongLong` | `t4dblToLongLong` | `'\0'` (i4init.c:694-698) |
| `r5dbDate` | `t4dtstrToDbDate` | `t4dblToDbDate` | `'\0'` (i4init.c:701-705) |
| `r5dbTime` | `t4strToTime` | 0 | `'\0'` (i4init.c:706-710) |
| `r4dateTime` | `t4strToDateTime` | 0 | `'\0'` (i4init.c:711-715) |
| `r4dateTimeMilli` | `t4strToDateTimeMilli` | 0 | `'\0'` (i4init.c:716-720) |
| `r4currency` | `t4strToCur` | `t4dblToCurFox` | `'\0'` (i4init.c:721-727) |
| `r5dbTimeStamp` | `t4strToDbTimeStamp` | 0 | `'\0'` (i4init.c:728-732) |

Type codes (single chars) from d4defs.h:2774-2822:
`r4double/r4bin='B'`, `r4str='C'`, `r4dateDoub='d'`, `r4date='D'`, `r4float='F'`,
`r4floatBin='H'`, `r4int='I'`, `r4log='L'`, `r4memo='M'`, `r4numDoub='n'`, `r4num='N'`,
`r5wstrLen='O'`, `r5ui4='P'`, `r5i2='Q'`, `r5ui2='R'`, `r4dateTime='T'`, `r5wstr='W'`,
`r4currency='Y'`, `r5i8='1'`, `r5dbDate='2'`, `r5dbTime='3'`, `r5dbTimeStamp='4'`,
`r4dateTimeMilli='7'`. Note **`r4double` is `'B'`**, not `'R'` (d4defs.h:2775).

### 1.1 Key lengths

`expr4keyLenFromType()` (E4EXPR.C:1001-1018, S4FOX branch):

| Expression type | Key bytes |
|---|---|
| `r4num`, `r4date`, `r4numDoub`, `r4currency`, `r4dateTime`, `r4dateTimeMilli` | 8 (`sizeof(double)`) |
| `r4int`, `r5ui4`, `r5ui2`, `r5i2` | 4 (`sizeof(S4LONG)`) |
| `r4log` | 1 |
| everything else (`r4str`, unicode, `r5i8`, db types…) | collation-dependent (returns −2) |

For the collation-dependent case, `expr4keyLen()` computes
`nullIndicatorLen + exprLen + exprLen * keySizeCharPerCharAdd` (E4EXPR.C:1049-1099);
i.e. character keys **double in length** under the GENERAL collation
(`keySizeCharPerCharAdd == NEED4ONE_EXTRA_BYTE == 1`, d4defs.h:1911, i4conv.c:338-339) and stay
1:1 under machine or `collate4simple` collations. If the expression is nullable, 1 extra leading
byte is added (E4EXPR.C:1056). If the tag expression wraps `ASCEND()`/`DESCEND()`
(`hasAscendOrDescend`), the per-char expansion is *not* added (E4EXPR.C:1090-1092).

Maximum key size: `I4MAX_KEY_SIZE = 240` for S4FOX (d4defs.h:2128). A collated key built from a
memo field is force-halved when it would reach the limit: `len /= (1 + keySizeCharPerCharAdd)`
(i4add.c:764-780; same logic on open at i4init.c:466-483).

---

## 2. Numeric / binary type transforms (exact algorithms)

All multi-byte transforms produce **big-endian byte order with an order-preserving sign fix-up**,
so that plain unsigned `memcmp` sorts keys numerically. Two distinct fix-ups exist:

* **Two's-complement integers** (int, i8, currency): flip only the sign bit.
* **IEEE 754 sign-magnitude floats** (float, double, date, datetime): set the sign bit when the
  value is ≥ 0, complement *all* bytes when negative.

### 2.1 Double — `t4dblToFox` (i4conv.c:2432-2466) — used for N, F, D, T keys and `B` doubles

Input: IEEE 754 binary64 `d` (little-endian in memory on Intel builds).

```
isPositive = (d >= 0)
if isPositive:
    for i in 0..7: result[i] = byte_of(d, 7-i)     # reverse to big-endian
    result[0] += 0x80                              # set sign bit
else:
    for i in 0..7: result[i] = ~byte_of(d, 7-i)    # reverse and complement every byte
```

Equivalent C# **for all normal values**: `ulong bits = BitConverter.DoubleToUInt64Bits(d);`
`bits = (d >= 0) ? bits | 0x8000000000000000UL : ~bits;` then write `bits` big-endian.
Note `+0.0` maps to `80 00 … 00`. `-0.0` compares `>= 0` as **true** in C (the test at
i4conv.c:2437), so it takes the *positive* path too — but its big-endian byte 0 is already `0x80`,
and `result[0] += 0x80` (byte add, i4conv.c:2440) **wraps** `0x80 + 0x80 → 0x00`, so `-0.0` maps to
`00 00 … 00`, sorting *below every other value* (including all negatives, which occupy `00 10 …`–`7F FF …`).
The `bits | 0x8000…` form above is therefore **not** bit-exact for `-0.0` (it would yield `80 00 … 00`);
a faithful port must reverse the bytes then add `0x80` to byte 0 with 8-bit wraparound, e.g.
`key[0] = unchecked((byte)(key[0] + 0x80))`.

`t4dblToFoxExport` is a plain exported alias (i4conv.c:2421-2428). The reverse transform
`t4foxToDbl` exists only under OLEDB5BUILD (i4conv.c:2472-2495).

### 2.2 Float (4-byte, field type `'H'`) — `t4floatToFox` (i4conv.c:2383-2417)

Identical algorithm on 4 bytes: reverse to big-endian; if `*flt >= 0` add 0x80 to byte 0, else
complement all 4 bytes.

### 2.3 Signed 32-bit integer `'I'` — `t4intToFox` (i4conv.c:1012-1035)

```
result = big_endian_bytes(val)          # x4reverseLong
if val > 0:  result[0] += 0x80
else:        result[0] -= 0x80          # val <= 0
```

Because a positive int32 has MSB 0 and a non-positive one has MSB 1 (or 0x00 for zero, where
`-0x80` wraps to 0x80), this is exactly `result[0] ^= 0x80` — offset-binary/"excess-2³¹"
encoding: `key = big_endian(val XOR 0x80000000)`. `r5i2`/`r5ui2` field values are widened to
32 bits before this transform (E4EXPR.C:336-388; i4init.c:679-690).

### 2.4 Unsigned 32-bit `'P'`/`'R'` — `t4unsignedIntToFox` (i4conv.c:1041-1051)

Big-endian byte reversal only; **no** sign-bit adjustment (comment i4conv.c:1045-1049).

### 2.5 Signed 64-bit `'1'` (i8) — `t4i8ToFox` (i4conv.c:1096-1115)

Same pattern as int32 on 8 bytes: `result = big_endian(val)`, then `result[0] += 0x80` if
`val > 0` else `result[0] -= 0x80` → `key = big_endian(val XOR 0x8000000000000000)`.
Inverse `t4foxToI8` at i4conv.c:1081-1090. 16-bit variant `t4shortToFox` (i4conv.c:1066-1076)
exists but index keys always use the widened 32-bit form.

### 2.6 Currency `'Y'` — `t4curToFox` (i4conv.c:1360-1391)

`CURRENCY4` is a 64-bit two's-complement integer holding value×10⁴, stored little-endian as
`unsigned short lo[4]` (d4data.h:2972-2976). Algorithm:

```
isPositive = ((short)lo[3] >= 0)               # sign of the high 16 bits (i4conv.c:1379)
for i in 0..7: result[i] = raw_byte[7-i]       # full 8-byte reversal (i4conv.c:1382-1384)
if isPositive: result[0] += 0x80  else: result[0] -= 0x80
```

i.e. the same excess-2⁶³ encoding as i8 (zero counts as positive here; the XOR result is
identical). Seek helpers: `t4strToCur` parses the string with `c4atoCurrency` then applies
`t4curToFox` (i4conv.c:273-283); `t4dblToCurFox` converts a double to currency via a 4-decimal
string (`c4dtoa45`) then the same (i4conv.c:226-235, 2370-2377).

### 2.7 Date `'D'` — `t4dtstrToFox` (i4conv.c:1124-1130) and E4EXPR.C:468-472

Key = `t4dblToFox( (double) date4long("CCYYMMDD") )`. `date4long` returns the **Julian day
number** — days since Jan 1, 4713 BC; e.g. 1981-01-01 → 2444606; blank (`"        "` or
`"00000000"`) → 0; invalid → −1 (D4DATE.C:670-698). It is computed as
`c4ytoj(year) + dayOfYear + JULIAN4ADJUSTMENT` with `JULIAN4ADJUSTMENT = 1721425`
(D4DATE.C:660-665, d4defs.h:2715), where `c4ytoj` counts days to the start of the year with
Gregorian 4/100/400 leap rules (D4DATE.C:346-360). An empty date therefore produces the key of
double 0.0 = `80 00 00 00 00 00 00 00`, which sorts before all real dates.

### 2.8 Datetime `'T'` — `t4dateTimeToFox` (i4conv.c:2209-2287)

Input is `S4LONG[2]`: `[0]` = Julian day, `[1]` = milliseconds since midnight.

```
extra  = ms % 1000 ; tm = ms - extra
if extra >= 500: tm += 1000                    # round to nearest second (i4conv.c:2250-2256)
val = day + tm / 86400000.0                    # (i4conv.c:2260)
if f4flagIsSet(flags4dateTime, tm/1000 + 1):   # (i4conv.c:2263)
    # decrement the least-significant byte of the little-endian double, with borrow:
    if lsb == 0: lsb = 0xFF; next_byte -= 1
    else:        lsb -= 1                      # (i4conv.c:2273-2279)
key = t4dblToFox(val)
```

`flags4dateTimeFlags` is an 86400-bit (10800-byte + 2 trailing zero bytes) empirical bitmap,
indexed by *second-of-day + 1*, marking datetimes whose double must be decremented by 1 ULP-byte
to match FoxPro's undeciphered conversion (table i4conv.c:1513-2191; wiring i4conv.c:2197-2206;
bit extraction `(1 << (flagNum & 7)) & flags[flagNum >> 3]`, F4FLAG.C:161-193). **This table must
be copied verbatim.**

`t4dateTimeToFoxMilli` (`r4dateTimeMilli` `'7'`, CodeBase-only) is identical except the
milliseconds are kept (no rounding) while the flag lookup still uses the whole second
(i4conv.c:2292-2364). Seek-side string parsers: `t4strToDateTime` expects `CCYYMMDDHH:MM:SS`
(i4conv.c:608-637); `time4long` converts `HHxMMxSS[.MMM]` to ms (i4conv.c:201-221).

### 2.9 Logical `'L'` — 1 byte

Index path stores ASCII `'T'` (0x54) or `'F'` (0x46) from the int expression value
(E4EXPR.C:512-536). Seek path `t4strToLog` maps `t/T/y/Y → 'T'`, `f/F/n/N → 'F'`, default `'F'`
(i4conv.c:889-916). Ascending byte order: F < T.

### 2.10 OLE-DB date/time structs (CodeBase-only field types `'2'`,`'3'`,`'4'`)

`DBDATE{short year; ushort month,day}` (6 bytes), `DBTIME{ushort hour,minute,second}` (6 bytes),
`DBTIMESTAMP{short year; ushort month,day,hour,minute,second; ulong fraction}` (16 bytes)
(d4data.h:4407-4436). Key transform = byte-swap each 16-bit member to big-endian, `fraction`
32-bit swapped (`t4dbDateToFox` i4conv.c:1239-1246, `t4dbTimeToFox` i4conv.c:1290-1297,
`t4dbTimeStampToFox` i4conv.c:1301-1312). No sign fix-up is applied (negative years would
missort — irrelevant for VFP compatibility since VFP has no such column types).

### 2.11 Numeric `'N'`/`'F'` keys are IEEE doubles, **not** BCD

In S4FOX, an `r4num` (string-form numeric) expression result is parsed with
`c4atod(text, len)` and passed to `t4dblToFox` (E4EXPR.C:459-466; seek path `t4strToFox`,
i4conv.c:1220-1234, `*lenOut = 8`). BCD keys (`C4BCD`) are **S4MDX only** — see §7.

---

## 3. Character keys and collations

### 3.1 Data structures

`enum Collate4type` (d4data.h:32-42): `collate4machineByteOrder=0`, `collate4simple=1`,
`collate4subSort=2`, `collate4compress=4`, `collate4subSortCompress=6`, `collate4unknown=16`.
(`subSort` and `compress` alone are declared but not implemented — selection falls to
`error4(e4notSupported)`, i4init.c:613-616; `collate4convertCharToKey` E4EXPR.C:840-843.)

`COLLATE4` (d4data.h:46-87) holds: `collateType`, `charToKeyTranslationArray`,
`unicodeToKeyTranslationArray`, `charToKeyCompressionArray`, `unicodeToKeyCompressionArray`,
`keySizeCharPerCharAdd` (extra key chars per input char), `expandOrCompressChar` (head-byte
marker value, 0xFF), `noTailChar` (0xFF = "no tail byte"), alloc flags, unicode equivalents,
`lossOfData`, plus runtime seek-state `considerPartialSeek/hasNull/maxKeyLen/lenIn`
(d4data.h:81-86). Constants: `NO4TAIL_BYTES 0xFF`, `NO4TAIL_BYTES_UNICODE 0xFFFF`,
`EXPAND4CHAR_TO_TWO_BYTES 0xFF`, `NEED4ONE_EXTRA_BYTE 1`, `MUST4GENERATE_ARRAY 0`
(d4defs.h:1903-1918).

Per-character table element `Translate4arrayChar { unsigned char headChar, tailChar }`
(d4data.h:123-130); unicode variant has a 16-bit `headChar` (d4data.h:134-141).
`Expansion4compressionArray` holds `type` (`expand4=1`, `compress4=2`, `done4=3` terminator)
and for expansion two indexes into the translation array (d4data.h:145-172).

`enum Collate4name` (d4data.h:91-117): `collate4none=0`, `collate4machine=1`,
`collate4generalCp1252=2`, `collate4generalCp437=3`, `collate4test=4`,
`collate4croatianCp1250=5`, `collate4croatianUpperCp1250=6`, `collate4generalCp850=7`,
`collate4avaya1252=8`, `collate4spanishCp1252=9`, `collate4spanishCp850=10`.
`NUM4AVAIL_COLLATION_ENTRIES = 10`; array index = name−1 (`collate4arrayIndex`,
`collation4get` macros, d4data.h:179-189).

### 3.2 The built-in collation registry — `collationArray` (i4conv.c:314-480)

| # | Name | Type | Char table | Compress table | key add/char | lossOfData |
|---|---|---|---|---|---|---|
| 1 | machine | machineByteOrder | (filler) | — | 0 | 0 (i4conv.c:319-333) |
| 2 | generalCp1252 | subSortCompress | `cp1252generalCollationArray` | `cp1252generalCompressArray` | 1 | 1 (i4conv.c:338-339) |
| 3 | generalCp437 | subSortCompress | `cp437generalCollationArray` | `cp437generalCompressArray` | 1 | 1 (i4conv.c:345-359) |
| 4 | test | unknown | loaded from disk tables | loaded | — | — (i4conv.c:365-366) |
| 5 | croatianCp1250 | simple | `cp1250croatianCollationArray` | — | 0 | 0 (i4conv.c:371-385) |
| 6 | croatianUpperCp1250 | simple | `cp1250croatianUpperCollationArray` | — | 0 | 0 (i4conv.c:390-404) |
| 7 | generalCp850 | subSortCompress | `cp850generalCollationArray` | `cp850generalCompressArray` | 1 | 1 (i4conv.c:407-421) |
| 8 | avaya1252 | subSortCompress | `avaya1252CollationArray` | `avaya1252CompressArray` | 1 | 1 (i4conv.c:426-440) |
| 9 | spanishCp1252 | simple | `spanishCp1252CollationArray` | — | 0 | 0 (i4conv.c:444-458) |
| 10 | spanishCp850 | simple | `spanishCp850CollationArray` | — | 0 | 0 (i4conv.c:459-473) |

Unicode tables for entries 2/3/7/8 are `MUST4GENERATE_ARRAY` (generated lazily, §3.6). The
`collate4test` entry can be populated from three DBF tables on disk named `coll4inf`,
`collate4`, `compres4` (d4defs.h:1898-1900) via `collate4setupReadFromDisk` (u4util.c:2570).

### 3.3 Table contents in COLL4ARR.C (copy verbatim — do not re-derive)

`COLL4ARR.C` is `#include`d by i4conv.c (i4conv.c:309). Arrays (each translation array has
exactly 256 `{head,tail}` entries, one per byte value; sizes below):

| Array | Location | Size |
|---|---|---|
| `cp1252generalCollationArray` | COLL4ARR.C:21-300 | 256 × 2 bytes |
| `cp1252generalCompressArray` | COLL4ARR.C:304-311 | 4 expansions + `done4`: "OE","AE","TH","SS" |
| `cp437generalCollationArray` | COLL4ARR.C:315-577 | 256 × 2 |
| `cp437generalCompressArray` | COLL4ARR.C:579 | alias of the cp1252 compress array |
| `cp850generalCollationArray` | COLL4ARR.C:583-843 | 256 × 2 |
| `cp850generalCompressArray` | COLL4ARR.C:847-855 | "OE","AE","SS","UE"+`done4` |
| `cp1250croatianCollationArray` | COLL4ARR.C:859-1121 | 256 × 2 (tails unused) |
| `cp1250croatianUpperCollationArray` | COLL4ARR.C:1123-1383 | 256 × 2 |
| `avaya1252CollationArray` | COLL4ARR.C:1385-1645 | 256 × 2 |
| `spanishCp1252CollationArray` | COLL4ARR.C:1647-1906 | 256 × 2 |
| `spanishCp850CollationArray` | COLL4ARR.C:1908-2167 | 256 × 2 |
| `avaya1252CompressArray` | COLL4ARR.C:2169 | alias of cp1252 compress array |

Semantics of an entry, using cp1252 GENERAL as example:

* Control chars map to head 16, no tail: `{16, 0xFF}` (COLL4ARR.C:24 ff.).
* `'A'`(65) and `'a'`(97) both map to `{96, 0}` — GENERAL is **case-insensitive** with equal
  tails for the case pair (COLL4ARR.C extract: entry 65 and 97 both `{96,0}`).
* Accented variants share the head with the base letter but carry a distinct nonzero tail used
  as a *sub-sort* (secondary) weight, e.g. `'ü'`(252) → `{120, 4}` where `'u'` → `{120, 0}`
  (COLL4ARR.C:296-297).
* `headChar == 0xFF` (`EXPAND4CHAR_TO_TWO_BYTES`) marks an expansion; `tailChar` is then an
  index into the compress array, e.g. `'œ'`(156) → expansion #0 = "OE" (COLL4ARR.C:180,
  304-311), `'þ'`(254) → expansion #2 = "TH" (COLL4ARR.C:298).
* Head values ≥ 16 are used by real characters; values < 16 never appear as heads, which the
  partial-seek logic exploits (D4SEEK.C:117, 134).

### 3.4 subSortCompress ("GENERAL") key transform — `t4convertSubSortCompressChar` (u4util.c:2201-2360)

Output length is always `2 × lenIn` (u4util.c:2229). Algorithm:

```
len = lenIn
while len > 0 and input[len-1] == ' ': len--        # strip trailing blanks (u4util.c:2249-2250)

doTails = true
verifyLen = (collate->lenIn == 0) ? 2*lenIn : collate->lenIn*2
if collate->considerPartialSeek and collate->maxKeyLen > verifyLen + collate->hasNull:
    doTails = false                                  # partial seek: omit tails (u4util.c:2256-2264)

heads = [] ; tails = []
for each input byte c (0..len-1):
    e = translateArray[c]
    if e.headChar == 0xFF:                           # expansion
        x = compressArray[e.tailChar]                # must be expand4; compress4 → error4(e4notSupported) (u4util.c:2316-2320)
        for each of x's two expansion char indexes i:
            heads += translateArray[i].headChar
            if doTails and translateArray[i].tailChar != 0xFF: tails += that tail
    else:
        heads += e.headChar
        if doTails and e.tailChar != 0xFF and len(tails) < lenIn: tails += e.tailChar

result = heads + tails truncated to (2*lenIn - len(heads)) + zero padding to 2*lenIn
                                                     # (u4util.c:2344-2359)
```

So the key layout is: *head bytes* (primary weights, one per character, two per expanded
character), immediately followed by *tail bytes* (secondary weights, only for characters that
have one), zero-filled to twice the input length. Trailing blanks contribute nothing (they sort
lowest even though `' '` itself has head 17, COLL4ARR.C:24/entry 32 — see the comment at
u4util.c:2242-2247).

### 3.5 simple collation transform — `t4convertSimpleChar` (u4util.c:2366-2425)

1:1 mapping: `result[i] = translateArray[input[i]].headChar`, `*lenOut = lenIn`. Trailing
blanks are **not** stripped (deliberately, u4util.c:2380-2383); remainder zero-padding logic
(u4util.c:2419-2424) is a no-op since head count == lenIn.

### 3.6 Unicode (`r5wstr` — CodeBase extension, not VFP)

* Machine order: every UTF-16 code unit is byte-swapped to big-endian so memcmp works
  (`t4unicodeToMachine`, i4conv.c:588-603); used also as the generic `ASCEND()` handling
  (i4conv.c:185-189).
* Collated: `t4convertSubSortCompressUnicode` (i4conv.c:484-583) mirrors §3.4 with 16-bit heads
  and 16-bit big-endian tails; output length `lenIn + lenIn*keySizeCharPerCharAdd` bytes
  (i4conv.c:496); trailing `L' '` stripped (i4conv.c:517-524).
* The 65536-entry unicode table is generated from the 256-entry char table:
  entries 0-255 copy the char heads (byte-swapped) and tails, entries 256-65535 map to
  themselves big-endian with no tail (`collate4setupUnicodeFromChar`, u4util.c:2700-2790;
  rationale comment d4data.h:104-107).

### 3.7 Machine collation for `r4str`

Identity copy (`t4noChangeStr`, i4init.c:103-109). DBF character fields are space-padded, so
machine keys are space-padded; `pChar = ' '` (i4init.c:591) drives the leaf trailing-byte
compression (§6). Confirmed against real bytes: `CLASSES.CDX` (codepage 0, empty `sortSeq`
⇒ `collate4machine`) stores the `CODE` character-field keys verbatim ("CMPT201", "MATH114",
"ECON101"…) with trailing blanks trail-count-compressed away — the tag-of-tags leaf likewise
holds `"CODE"` padded with `0x20` spaces (CLASSES.CDX block 3, offset 1536). No head/tail
transform is applied under machine collation. (All shipped sample CDX in `examples/DATA` use
machine order — none carry a `"GENERAL"`/`"CBnnnnn"` `sortSeq` — so the head+tail GENERAL layout
of §3.4 is verified from source only, not from these sample bytes.)

---

## 4. Collation selection

### 4.1 On index/tag open — `tfile4init` (i4init.c:306-551)

The tag header is read as: 16-byte fixed part + CodeBase extras (`topSize = 28` bytes,
i4init.c:331), then `sortSeq[8]` at header offset `topSize+466 = 494` (i4init.c:339-345) and
five shorts `descending, filterPos, filterLen, exprPos, exprLen` at offset `topSize+474 = 502`
(i4init.c:348-351); T4HEADER layout at d4data.h:3893-3925. (These are the standard VFP compact
CDX tag-header offsets 494/502 relative to the tag header node.)

Mapping of `sortSeq` (i4init.c:372-418):

| `sortSeq` | Collation chosen |
|---|---|
| `"\0"…` (empty) | `collate4machine` (i4init.c:372-376) |
| `"GENERAL"` | by DBF code page: cp1252 or cp0 → `collate4generalCp1252`; cp437 → `collate4generalCp437`; cp850 → `collate4generalCp850`; cp1250/cp0004 → **error e4index** (i4init.c:378-405) |
| `"CBnnnnn"` | ordinal `nnnnn` = `Collate4name`; > 10 → error (i4init.c:407-416) |
| anything else | error e4index (i4init.c:417-418) |

The **code page** is the raw byte at DBF header offset 29 (`DATA4HEADER_FULL.codePage`,
d4data.h:3062-3090; read at d4open.c:2217). CodeBase's cp constants coincide with the VFP
language-driver IDs: `cp0=0, cp437=1, cp850=2, cp1252=3, cp0004=4, cp1250=-56 (0xC8)`
(d4defs.h:1924-1933). So VFP's LDID byte 0x03 (Windows ANSI) + `"GENERAL"` → generalCp1252, etc.

### 4.2 On tag create — `tfile4setCollatingSeq` (i4tag.c:2887-3064)

Input is `CODE4.collateName/collateNameUnicode` (d4data.h:2585-2586) or the legacy
`CODE4.collatingSequence` (`sort4machine=0, sort4general=1, sort4croatian=2,
sort4croatianUpper=3, sort4spanish=4`, d4defs.h:1874-1878). Rules:

* Machine → `sortSeq` zeroed (i4tag.c:2918-2924, 3034-3038).
* Non-machine requested but the expression is not "collatable" (anything other than plain
  character/memo fields or ASCEND/DESCEND of them) → forced to machine
  (i4tag.c:2932-2943; collatability check `tfile4setCollationSeqCollatable`, i4tag.c:2860-2882).
* `sort4general` → per data-file code page: cp1252/cp0→generalCp1252, cp850→generalCp850,
  cp437→generalCp437, cp1250/cp0004→croatianCp1250 (i4tag.c:2972-2993).
* Header string: `"GENERAL\0"` when the chosen general collation matches the file's code page
  (FoxPro-compatible); otherwise `"CB" + 5-digit zero-padded ordinal` (e.g. `"CB00005"`) with a
  NUL terminator (i4tag.c:2999-3018, 3040-3059).
* The compound index's internal *tag-of-tags* directory always uses machine order
  (i4create.c:878).

Writing on create: 16 header bytes (`LEN4HEADER_WR = 0x10`, d4defs.h:1750), 478 zero bytes,
8 bytes `sortSeq` (⇒ file offset 494), 5 shorts starting with `descending` (⇒ offset 502), then
the expression and filter text; the header block is 512 bytes (`B4BLOCK_SIZE_INTERNAL = 512`
for S4FOX, d4defs.h:2213) (i4add.c:886-930).

---

## 5. Descending keys, ASCEND()/DESCEND(), nulls

### 5.1 Descending tags

A descending tag sets `header.descending = 1` (i4add.c:712-714) — **key bytes are stored
unmodified, in ascending physical order**; only logical traversal is reversed:
`tfile4dskip` negates the skip (`return -tfile4skip(t4, -numSkip)`) under `S4HAS_DESCENDING`
(defined for S4FOX, d4defs.h:1061-1065; code i4tag.c:64-89; design comment i4tag.c:67-73:
"physically with FoxPro compatibility … the tag is not actually in descending order"). Top/bottom
are likewise swapped when the flag is set (i4tag.c:540). This matches VFP compact CDX. (S4MDX
stores keys physically descending via typeCode 0x08 instead, i4add.c:479-489.)

### 5.2 DESCEND()/ASCEND() expression functions

`DESCEND(x)` = `ASCEND(x)` followed by a **bitwise NOT of every key byte**:
`e4descend()` calls `c4descend` (e4functi.c:3081-3085), and for non-Clipper builds
`c4descend` is `to[len] = ~from[len]` (i4conv.c:249-265; the Clipper variant negates,
`-from[len]`, for compatibility, comment i4conv.c:251-252). `c4descendBinary` is always `~`
(i4conv.c:240-245). `e4applyAscend` shows the canonical "ascend" per type (numeric → clipped
text for non-FOX, `t4dblToFox`/`t4intToFox`/etc. for S4FOX; strings unchanged)
(i4conv.c:108-195). When a general-collated string is wrapped in ASCEND/DESCEND the collation
is applied inside the expression evaluation and the tag conversion layer skips re-collating
(`hasAscendOrDescend`, E4EXPR.C:673-683, 1086-1092).

### 5.3 Null handling (VFP 0x30 nullable fields)

If any field in the tag expression allows nulls (`expr4nullLow`, E4EXPR.C:879-925), the key is
1 byte longer (E4EXPR.C:1056) and typeCode bit 0x02 is set in the tag header (i4add.c:740-742;
d4data.h:3900-3901). After the type conversion (E4EXPR.C:795-807):

* value **is null** → the whole key, including the extra byte, is `0x00` bytes;
* value **not null** → key bytes shift right one position and byte 0 = `0x80`.

So nulls sort before every non-null value. Candidate-key tags (typeCode 0x04) never reserve the
null byte (E4EXPR.C:894-900). Seek side: a zero-length seek against a nullable tag builds the
single byte `0x00` ("seek for null"); otherwise `0x80` is prefixed and the remaining length
capped at keyLen−1 (D4SEEK.C:44-63); numeric seeks prefix `0x80` before `dtok` output
(D4SEEK.C:153-190). This all applies only when the data file is VFP 3.0-level
(`compatibility == 30`, set from DBF version 0x30, d4open.c:2205-2217).

---

## 6. Comparison, padding, truncation, partial seek

* **Comparator**: `t4cdxCmp(data, search, len)` compares unsigned bytes left to right; returns
  the count of equal leading bytes, or −1 when `data[i] > search[i]` ("gone too far")
  (i4init.c:280-300). The `v4map` remap inside it is only compiled for non-FOX language builds
  (`S4VMAP` is never defined when S4FOX, d4defs.h:707-730) — **S4FOX comparison is pure
  unsigned memcmp**, all ordering intelligence lives in the key bytes.
* **Trailing pad (`pChar`) and leaf compression**: compact CDX leaf entries store
  duplicate-count/trail-count compressed keys (`B4NODE_HEADER`, d4data.h:3674-3684). The trail
  count is the number of trailing `pChar` bytes (`b4calcBlanks`, b4block.c:140-151; usage
  b4block.c:1134); on read the key is reconstituted by padding with `pChar`
  (b4block.c:1935). Hence `pChar` must be `' '` exactly for machine-order character tags and
  `'\0'` for everything else (§1 table; compound-header tags default `' '`, i4init.c:524).
* **Partial seek**: a seek shorter than the key length compares only `len` bytes (search logic
  strips trailing `pChar` from the search value, b4block.c:2214). For general collations the
  effective compare length is recomputed by counting leading head bytes (values ≥ 16) in the
  converted key, since tail bytes are omitted on partial seeks (D4SEEK.C:104-140;
  `collate4simpleMapping(collate) == (keySizeCharPerCharAdd == 0)`, D4SEEK.C:25-36).
* **Truncation**: keys longer than `I4MAX_KEY_SIZE (240)` are rejected/reduced; memo-sourced
  collated keys are halved (§1.1). `hasTrim` expressions (e.g. `TRIM(f)`) replace trailing
  `'\0'` with `' '` before machine keys / before collation (E4EXPR.C:697-702, 736-748).

---

## 7. S4MDX / S4CLIPPER divergences (brief)

* **MDX numeric keys are 12-byte BCD** (`C4BCD {sigDig; digitInfo; bcd[10]}`,
  d4data.h:4310-4315). `digitInfo`: bit 7 sign (1=negative), bits 6-2 digit count (max 31),
  bits 1-0 always `01` (C4BCD.C:16-20). `sigDig` = number of digits before the decimal point
  biased by 0x34 (52 = "zero", i4conv.c:2537, d4data.h:4312). Builders: `c4bcdFromA`
  (i4conv.c:2500-2604), `c4bcdFromD` via `ecvt` with `E4ACCURACY_DIGITS = 20`
  (C4BCD.C:84-127, d4defs.h:2161-2163). Comparator `c4bcdCmp` (sign, then sigDig, then packed
  digits memcmp, C4BCD.C:34-81). MDX date keys are raw doubles of the Julian day with blank
  date mapped to 1.0E300 (E4EXPR.C:273-299).
* **Clipper (NTX-style)** numeric keys are right-justified ASCII with `c4clip` sign folding
  (`t4strToClip`, i4conv.c:937-994); dates are `"CCYYMMDD"` text (i4init.c:787-795); comparator
  is `u4memcmp`/`t4cmpDoub` (i4init.c:762-777, 789).
* MDX descending is physical (typeCode 0x08); Clipper `c4descend` negates rather than
  complements (i4conv.c:253-257).

---

## 8. Can .NET `CultureInfo`/`CompareInfo` reproduce these orderings?

**No — not for byte compatibility, and it must not be used.** Reasons:

1. CDX keys are *stored byte strings* ordered by unsigned memcmp (§6). Compatibility means
   reproducing the exact bytes VFP/CodeBase writes, not merely an equivalent ordering.
   `CompareInfo.GetSortKey()` produces NLS/ICU sort keys whose binary layout is explicitly
   version- and OS-dependent, changes across Windows/ICU updates, and never matches the
   head+tail layout of §3.4 (head block followed by tail block, zero-padded to 2×len).
2. The GENERAL tables are frozen 1990s FoxPro tables: case-insensitive primaries
   (`'A'`/`'a'` → same head 96/tail 0), accent sub-sort weights placed *after* the whole
   primary block, and hard-coded expansions ("œ"→OE, "ß"→SS, "þ"→TH) (COLL4ARR.C:21-311). No
   modern culture produces this; e.g. current cultures use multi-level weights interleaved
   per-string with different alphabets for cp437/cp850 box-drawing and Greek characters.
3. Trailing-blank stripping *before* collation (u4util.c:2242-2250) and the partial-seek
   tail suppression (u4util.c:2256-2264) are engine-specific behaviors, not culture behaviors.
4. Numeric/date/datetime keys additionally embed the empirical `flags4dateTimeFlags` ULP
   correction (§2.8), which no general-purpose library reproduces.

**Port requirement:** copy verbatim, as static byte arrays in C#:
all 8 translation arrays + 2 distinct compress arrays from COLL4ARR.C (§3.3, ~256×2 bytes
each), and `flags4dateTimeFlags` (10802 bytes, i4conv.c:1513-2191). Implement the transforms of
§2-§3 exactly as specified. `CultureInfo` may only be used for user-facing non-index features,
never for key generation. (For files created by CodeBase with `"CBnnnnn"` sequences the same
statement holds for the croatian/spanish/avaya tables.)

---

## 9. Open questions / risks for the C# port

1. **`flags4dateTimeFlags` provenance** — the table is an empirical patch ("FoxPro appears to
   have an unusual conversion algorithm which could not be deciphered", i4conv.c:1509-1512).
   Port it blindly; add round-trip tests against VFP-generated CDX files with datetimes covering
   many seconds-of-day, since any transcription error breaks seek equality silently.
2. **`compress4` (contractions) unimplemented** — the engine errors on contraction entries
   (u4util.c:2316-2320). None of the shipped tables use them, but disk-loaded custom collations
   (`collate4test`) could. Decide whether the port supports disk-loaded collations at all.
3. **Disk-loaded collations** (`coll4inf`/`collate4`/`compres4` tables, d4defs.h:1898-1900;
   u4util.c:2429-2570) — probably out of scope for a VFP-compatible port; document as
   unsupported and fail with the equivalent of `e4notSupported`.
4. **cp1250 + "GENERAL"** in a VFP-created file is rejected by CodeBase (i4init.c:403-404).
   Real VFP supports many more language drivers/collations (MACHINE, DUTCH, NORDAN, UNIQWT …)
   than CodeBase's list; files using them will open with an error. Decide whether the C# port
   needs additional VFP collation tables (they would have to be captured from VFP itself —
   CodeBase has no source for them).
5. **`c4atod` / `c4atoCurrency` parsing semantics** — numeric seek strings go through
   CodeBase's own parsers (E4EXPR.C:460; i4conv.c:279). The port must match their handling of
   blanks/signs/overflow, not `double.Parse`. Verify against the C source (`e4str2.c`/`c4.c`)
   when porting the expression engine.
6. **`-0.0` and NaN doubles** — `t4dblToFox` sends −0.0 down the *positive* path (−0.0 `>= 0` is
   true), where the byte-0 add wraps and yields `00 00 … 00` (sorting below all values, §2.1); it has
   no special NaN handling; NaN bit patterns would produce keys interleaved with large values.
   The expression engine never produces NaN in normal operation, but the port should decide on
   a policy (reject vs. mirror the bit-level behavior).
7. **Currency `isPositive` uses `>= 0` while int/i8 use `> 0`** (i4conv.c:1379 vs 1027, 1109).
   Both reduce to XOR-0x80 of the top byte, but keep the port bit-exact rather than
   "simplifying" differently per type.
8. **Unicode (`r5wstr`) collation** is a CodeBase extension with generated 65536-entry tables
   (§3.6); VFP itself has no unicode index keys. Recommend deferring it.
9. **Embedded 0x00 in character data** — trailing-space stripping and `hasTrim` null→space
   replacement (E4EXPR.C:697-702, 736-748) interact; binary character fields (`r4charBin`)
   should be checked against real files before assuming identical behavior.
10. **Header offsets** — this spec covers only sortSeq/descending in the tag header
    (offsets 494/502, §4.1); the full CDX header/node layout belongs to the index-format spec
    and must stay consistent with it (`B4NODE_HEADER` bit-packed leaves, d4data.h:3674-3684).
