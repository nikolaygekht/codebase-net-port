# CodeBase — Public API & Error-Code Inventory (S4FOX, stand-alone)

**Scope.** This document is the definitive inventory of the CodeBase C library public API surface and
its error/return code system, for the purpose of planning the C# port. Configuration analyzed:
`S4FOX` (Visual FoxPro-compatible DBF/CDX/FPT) + `S4STAND_ALONE` (no client/server). All `S4CLIENT` /
`S4SERVER` code paths are explicitly **out of scope**.

**Primary sources** (all paths relative to `original/source/`):
- `d4declar.h` — public function declarations (3224 lines)
- `D4INLINE.H` — macro-implemented API (`S4INLINE` build) (388 lines)
- `d4defs.h` — all `r4*` / `e4*` constant values (2925 lines)
- `d4data.h` — public structs incl. `CODE4` configuration members (4558 lines)
- `e4expr.h` — expression module API; `R4RELATE.H` — relate module API; `c4trans.H` — transaction API
- `C4CODE.C` — `code4initLow()` (CODE4 defaults). NOTE: this file contains stray NUL bytes; use
  binary-tolerant tools (`grep -a`) when re-reading it.

Every factual claim carries a `(FILE:LINE)` citation to the line actually read in the source.

---

## 1. Error-handling model

- Every CODE4 session carries a sticky error state: `int errorCode` and `long errorCode2`
  (d4data.h:2154, d4data.h:2156). Once `errorCode < 0`, most API calls short-circuit and return
  `e4codeBase` (= `-1`, d4defs.h:2099) until the application clears it with `error4set(c4, 0)`
  (d4declar.h:814). `errorCode2` holds the extended (internal, positive) error identifier
  (`error4set2`, d4declar.h:815).
- Errors are raised internally via macros `error4( c4, code, extendedCode )` and
  `error4describe( c4, code, extendedCode, d1, d2, d3 )` which map to `error4default` /
  `error4describeDefault` (e4error.h:108-109; declarations d4declar.h:809-811).
- Application-visible error functions:

| Function | Signature | Behavior | Cite |
|---|---|---|---|
| `error4callback` | `void error4callback(CODE4*, ERROR_CALLBACK)` | install error callback (`ERROR_CALLBACK` gets c4, errCode, errCode2, 3 desc strings) | d4declar.h:808; callback typedef d4data.h:1928-1943 |
| `error4default` | `int error4default(CODE4*, const int, const long)` | raise error (target of `error4` macro) | d4declar.h:809 |
| `error4describeDefault` | `int (CODE4*, int, long, const char*, const char*, const char*)` | raise error with 3 description strings | d4declar.h:810 |
| `error4file` | `int error4file(CODE4*, const char*, const int)` | set the error-log output file | d4declar.h:812 |
| `error4hook` | `void error4hook(CODE4*, int, long, const char*, const char*, const char*)` | user-overridable hook called on every error | d4declar.h:813 |
| `error4set` | `int error4set(CODE4*, const int)` | set/clear `errorCode` (pass 0 to clear) | d4declar.h:814 |
| `error4set2` | `long error4set2(CODE4*, const long)` | set/clear `errorCode2` | d4declar.h:815 |
| `error4lastDescription` | `const char* (CODE4*)` | text of last error's descriptions | d4declar.h:816 |
| `error4text` | `const char* error4text(CODE4*, const long)` | map error number to message text | d4declar.h:817 |
| `error4exitTest` | `void error4exitTest(CODE4*)` | exits app if error state set | d4declar.h:835 |
| `error4code` | macro → `((c4)->errorCode)` in DLL/LIB builds | read current error code | d4declar.h:1320 |
| `error4code2` | macro → `((c4)->errorCode2)` | read extended error code | d4declar.h:1321 |
| `error4number2` | `long error4number2(const long)` | maps internal E-numbers | d4declar.h:2220 |
| `error4out` | `void error4out(CODE4*, int, long, const char*, ...)` | writes to error log | d4declar.h:2222 |

- `E4FILE_LINE` builds prepend `__FILE__`/`__LINE__` capture to every `error4` call
  (e4error.h:96-106; `code4lineNoSet`/`code4fileNameSet` d4declar.h:2233-2234).

**C# consequence:** the port should throw exceptions carrying `ErrorCode` (the e4 value) and
`ErrorCode2` (extended id), plus preserve the "sticky error" semantics only as an option for
compatibility (see §10).

---

## 2. `r4*` return codes (non-error status values)

All values from d4defs.h (integer return codes block, d4defs.h:1802-1857).

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `r4off` | -2 | transaction status: off | d4defs.h:1803 |
| `r4success` | 0 | success | d4defs.h:1804 |
| `r4found` | 1 | seek: primary key match | d4defs.h:1805 |
| `r4after` | 2 | seek: positioned after (no exact match) | d4defs.h:1806 |
| `r4eof` | 3 | end of file | d4defs.h:1807 |
| `r4bof` | 4 | beginning of file | d4defs.h:1808 |
| `r4entry` | 5 | no index entry / no record (go) | d4defs.h:1809 |
| `r4noRecords` | 6 | data file contains no records | d4defs.h:1810 |
| `r4delay` | 7 | (internal) delay | d4defs.h:1811 |
| `r4database` | 8 | (internal) | d4defs.h:1812 |
| `r4descending` | 10 | tag is descending | d4defs.h:1813 |
| `r4candidate` | 15 | tag unique mode: candidate (unique non-null; reject writes) | d4defs.h:1814 |
| `r4unique` | 20 | key not unique; do not write/append | d4defs.h:1815 |
| `r4batchUnique` | 21 | key not unique on batch write/append | d4defs.h:1816 |
| `r4uniqueContinue` | 25 | key not unique; write/append anyway (key silently not added) | d4defs.h:1817 |
| `r4batchUniqueContinue` | 26 | batch variant of r4uniqueContinue | d4defs.h:1818 |
| `r4locked` | 50 | record/file locked by another user | d4defs.h:1821 |
| `r4noCreate` | 60 | could not create file | d4defs.h:1822 |
| `r4noExist` | 64 | file does not exist | d4defs.h:1823 |
| `r4noOpen` | 70 | could not open file | d4defs.h:1824 |
| `r4noTag` | 80 | seek with no default tag | d4defs.h:1825 |
| `r4terminate` | 90 | relate: no match with `relate4terminate` set | d4defs.h:1826 |
| `r4exit` | 100 | a function requests program termination | d4defs.h:1827 |
| `r4continue` | 105 | internal | d4defs.h:1828 |
| `r4inactive` | 110 | transaction state: inactive | d4defs.h:1829 |
| `r4partial` | 115 | transaction state: partial | d4defs.h:1830 |
| `r4active` | 120 | transaction state: active | d4defs.h:1831 |
| `r4rollback` | 130 | transaction state: rollback in progress | d4defs.h:1832 |
| `r4authorize` | 140 | lacks authorization (server; N/A stand-alone) | d4defs.h:1833 |
| `r4connected` | 150 | client/server only | d4defs.h:1834 |
| `r4logOn` | 160 | logging on | d4defs.h:1835 |
| `r4logOpen` | 170 | log file open | d4defs.h:1836 |
| `r4logOff` | 180 | logging off | d4defs.h:1837 |
| `r4null` | 190 | expression/field evaluated to null | d4defs.h:1838 |
| `r4autoIncrement` | 195 | field is auto-increment | d4defs.h:1839 |
| `r4autoTimestamp` | 200 | field is auto-timestamp | d4defs.h:1841 |
| `r4done` | 210 | operation done | d4defs.h:1842 |
| `r4pending` | 215 | operation pending | d4defs.h:1843 |
| `r4deleted` | 220 | record deleted | d4defs.h:1844 |
| `r4timeout` | 225 | timeout | d4defs.h:1845 |
| `r4invalid` | 230 | invalid | d4defs.h:1846 |
| `r4schema` | 240 | schema (OLEDB) | d4defs.h:1847 |
| `r4open` | 245 | open state | d4defs.h:1848 |
| `r4skipped` | 247 | skipped | d4defs.h:1849 |
| `r4blankTcpAddress`…`r4notConnected` | 250…315 | networking codes — **out of scope** (client/server) | d4defs.h:1850-1857 |

Master-skip helpers: `r4skip` = 1, `r4noSkip` = 2 (d4defs.h:1870-1871).
Batch-read flags (bit flags): `r4batchSkip` 0x01, `r4batchTop` 0x02, `r4batchBottom` 0x04,
`r4batchSeekNext` 0x08, `r4batchSeekMatch` 0x10, `r4batchSeek` 0x20 (d4defs.h:1882-1887).

---

## 3. `e4*` error codes (negative values)

All values from d4defs.h.

### 3.1 General disk-access errors (d4defs.h:1936-1958)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4close` | -10 | close failure | d4defs.h:1937 |
| `e4create` | -20 | create failure | d4defs.h:1938 |
| `e4len` | -30 | length determination failure | d4defs.h:1939 |
| `e4lenSet` | -40 | set-length failure | d4defs.h:1940 |
| `e4lock` | -50 | lock failure | d4defs.h:1941 |
| `e4open` | -60 | open failure | d4defs.h:1942 |
| `e4permiss` | -61 | open: permission denied | d4defs.h:1943 |
| `e4access` | -62 | open: access violation | d4defs.h:1944 |
| `e4numFiles` | -63 | open: too many open files | d4defs.h:1945 |
| `e4fileFind` | -64 | open: file not found | d4defs.h:1946 |
| `e4exclusive` | -65 | open: exclusive access unavailable | d4defs.h:1948 |
| `e4instance` | -69 | instance error | d4defs.h:1953 |
| `e4read` | -70 | read failure | d4defs.h:1954 |
| `e4remove` | -80 | file remove failure | d4defs.h:1955 |
| `e4rename` | -90 | rename failure | d4defs.h:1956 |
| `e4unlock` | -110 | unlock failure | d4defs.h:1957 |
| `e4write` | -120 | write failure | d4defs.h:1958 |

### 3.2 Database-specific errors (d4defs.h:1960-1966)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4data` | -200 | data file invalid/corrupt | d4defs.h:1961 |
| `e4fieldName` | -210 | invalid field name | d4defs.h:1962 |
| `e4fieldType` | -220 | invalid field type | d4defs.h:1963 |
| `e4recordLen` | -230 | invalid record length | d4defs.h:1964 |
| `e4append` | -240 | append failure | d4defs.h:1965 |
| `e4seek` | -250 | seek failure | d4defs.h:1966 |

### 3.3 Index-file errors (d4defs.h:1968-1975)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4entry` | -300 | tag entry not located | d4defs.h:1969 |
| `e4index` | -310 | index file invalid/corrupt | d4defs.h:1970 |
| `e4tagName` | -330 | invalid tag name | d4defs.h:1971 |
| `e4unique` | -340 | key is not unique | d4defs.h:1972 |
| `e4batchUnique` | -345 | key not unique on batch update | d4defs.h:1973 |
| `e4tagInfo` | -350 | tag information invalid | d4defs.h:1974 |
| `e4candidate` | -360 | key not unique/non-null (candidate) | d4defs.h:1975 |

### 3.4 Expression-evaluation errors (d4defs.h:1977-1991)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4commaExpected` | -400 | comma expected | d4defs.h:1978 |
| `e4complete` | -410 | expression incomplete | d4defs.h:1979 |
| `e4dataName` | -420 | unrecognized data alias | d4defs.h:1980 |
| `e4lengthErr` | -422 | length error | d4defs.h:1981 |
| `e4notConstant` | -425 | constant expected | d4defs.h:1982 |
| `e4numParms` | -430 | wrong number of parameters | d4defs.h:1983 |
| `e4overflow` | -440 | overflow during evaluation | d4defs.h:1984 |
| `e4rightMissing` | -450 | right bracket missing | d4defs.h:1985 |
| `e4typeSub` | -460 | sub-expression type mismatch | d4defs.h:1986 |
| `e4unrecFunction` | -470 | unrecognized function | d4defs.h:1987 |
| `e4unrecOperator` | -480 | unrecognized operator | d4defs.h:1988 |
| `e4unrecValue` | -490 | unrecognized value | d4defs.h:1989 |
| `e4unterminated` | -500 | unterminated string | d4defs.h:1990 |
| `e4tagExpr` | -510 | expression invalid for use in a tag | d4defs.h:1991 |

### 3.5 Optimization errors (d4defs.h:1993-1996)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4opt` | -610 | optimization error | d4defs.h:1994 |
| `e4optSuspend` | -620 | optimization suspend failure | d4defs.h:1995 |
| `e4optFlush` | -630 | optimization flush failure | d4defs.h:1996 |

### 3.6 Thread/event errors (d4defs.h:1998-2002)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4event` | -650 | event error | d4defs.h:1999 |
| `e4outstanding` | -660 | outstanding operations | d4defs.h:2000 |
| `e4signal` | -670 | signal error | d4defs.h:2001 |
| `e4semaphore` | -680 | semaphore error | d4defs.h:2002 |

### 3.7 Relation errors (d4defs.h:2004-2007)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4relate` | -710 | relation error | d4defs.h:2005 |
| `e4lookupErr` | -720 | lookup error | d4defs.h:2006 |
| `e4relateRefer` | -730 | referenced relation missing/uninitialized | d4defs.h:2007 |

### 3.8 Report-writer errors — out of scope for the port (d4defs.h:2009-2023)

`e4report` -810, `e4styleCreate` -811, `e4styleSelect` -812, `e4styleIndex` -813,
`e4areaCreate` -814, `e4groupCreate` -815, `e4groupExpr` -816, `e4totalCreate` -817,
`e4objCreate` -818, `e4repWin` -819, `e4repOut` -820, `e4repSave` -821, `e4repRet` -822,
`e4repData` -823 (d4defs.h:2010-2023).

### 3.9 Critical/internal errors (d4defs.h:2039-2050)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4info` | -910 | unexpected info in internal variable (corruption) | d4defs.h:2040 |
| `e4memory` | -920 | out of memory | d4defs.h:2041 |
| `e4parm` | -930 | unexpected parameter | d4defs.h:2042 |
| `e4parm_null` (`e4parmNull`) | -935 | unexpected null parameter | d4defs.h:2044,2043 |
| `e4demo` | -940 | demo record-count limit exceeded (`E4DEMO_MAX` = 200, d4defs.h:2122) | d4defs.h:2045 |
| `e4result` | -950 | unexpected result | d4defs.h:2046 |
| `e4verify` | -960 | verification failure (e.g. struct-size mismatch, C4CODE.C:864) | d4defs.h:2047 |
| `e4struct` | -970 | structure invalid | d4defs.h:2048 |
| `e4compatibility` | -980 | FoxPro index format incompatibility (internal) | d4defs.h:2050 |

### 3.10 "Feature disabled" errors (d4defs.h:2054-2063)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4notIndex` | -1010 | built with `S4OFF_INDEX` | d4defs.h:2055 |
| `e4notMemo` | -1020 | built with `S4OFF_MEMO` | d4defs.h:2056 |
| `e4notRename` | -1030 | built with `S4NO_RENAME` | d4defs.h:2057 |
| `e4notWrite` | -1040 | built with `S4OFF_WRITE` | d4defs.h:2058 |
| `e4notClipper` | -1050 | Clipper-only feature | d4defs.h:2059 |
| `e4notLock` | -1060 | `S4LOCK_HOOK` | d4defs.h:2060 |
| `e4notSupported` | -1090 | generally not supported | d4defs.h:2062 |
| `e4version` | -1095 | header/library version mismatch (raised by code4init, C4CODE.C:840) | d4defs.h:2063 |

### 3.11 Memo errors (d4defs.h:2065-2067)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4memoCorrupt` | -1110 | memo (FPT) file corrupt | d4defs.h:2066 |
| `e4memoCreate` | -1120 | memo file create failure | d4defs.h:2067 |

### 3.12 Transaction errors (d4defs.h:2069-2075)

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `e4transViolation` | -1200 | operation illegal inside/outside transaction | d4defs.h:2070 |
| `e4trans` | -1210 | transaction subsystem error | d4defs.h:2071 |
| `e4rollback` | -1220 | rollback failure | d4defs.h:2072 |
| `e4commit` | -1230 | commit failure | d4defs.h:2073 |
| `e4transAppend` | -1240 | transaction log append failure | d4defs.h:2074 |
| `e4transStatus` | -1250 | invalid transaction status | d4defs.h:2075 |

### 3.13 Communication & misc errors

Communication errors `e4corrupt` -1300 … `e4preprocessMismatch` -1396 (d4defs.h:2078-2093) are
client/server — **out of scope** except `e4corrupt` (-1300, d4defs.h:2078) which is also used for
corrupt files. Miscellaneous: `e4max` -1400 (d4defs.h:2096), `e4codeBase` -1 (generic "error
occurred somewhere", d4defs.h:2099), `e4name` -1420 (2100), `e4authorize` -1430 (2101),
`e4invalidUserId` -1440 (2102), `e4invalidPassword` -1450 (2103), `e4invalidTcpAddress` -1460
(2104), `e4connectDenied` -1470 (2105), `e4invalidLicence` -1480 (2106). Server-only:
`e4server` -2100, `e4config` -2110 (d4defs.h:2112-2113).

---

## 4. Field-type constants (used by `FIELD4INFO.type`, `f4type`, `expr4type`)

Character constants, d4defs.h:2774-2822. **Note pitfall:** `r4double` is `'B'` (binary double),
identical to `r4bin`; it is NOT `'R'`.

| Constant | Value | Meaning | Cite |
|---|---|---|---|
| `r4bin` / `r4double` | `'B'` | 8-byte IEEE double field (VFP "Double") | d4defs.h:2774-2775 |
| `r4str` | `'C'` | character | d4defs.h:2776 |
| `r4dateDoub` | `'d'` | date as double (expression type) | d4defs.h:2777 |
| `r4date` | `'D'` | date (8-char CCYYMMDD) | d4defs.h:2778 |
| `r4float` | `'F'` | float (numeric ASCII) | d4defs.h:2779 |
| `r4floatBin` | `'H'` | 4-byte binary float | d4defs.h:2781 |
| `r4gen` | `'G'` | general (OLE) memo ref | d4defs.h:2782 |
| `r4int` | `'I'` | 4-byte integer | d4defs.h:2783 |
| `r4log` | `'L'` | logical | d4defs.h:2784 |
| `r4memo` | `'M'` | memo | d4defs.h:2785 |
| `r4numDoub` | `'n'` | numeric as double (expression type) | d4defs.h:2786 |
| `r4num` | `'N'` | numeric (ASCII) | d4defs.h:2787 |
| `r5wstrLen` | `'O'` | wide string + length suffix (OLEDB) | d4defs.h:2795 |
| `r5ui4` | `'P'` | unsigned 4-byte int (OLEDB) | d4defs.h:2797 |
| `r5i2` | `'Q'` | signed 2-byte int (OLEDB) | d4defs.h:2798 |
| `r5ui2` | `'R'` | unsigned 2-byte int (OLEDB) | d4defs.h:2799 |
| `r4dateTime` | `'T'` | datetime (8 bytes) | d4defs.h:2804 |
| `r5guid` | `'V'` | GUID (OLEDB schema) | d4defs.h:2806 |
| `r5wstr` / `r4unicode` | `'W'` | wide string | d4defs.h:2807-2808 |
| `r4memoBin` | `'X'` | binary memo | d4defs.h:2809 |
| `r4currency` | `'Y'` | currency (8-byte scaled int) | d4defs.h:2810 |
| `r4charBin` | `'Z'` | binary character | d4defs.h:2811 |
| `r4system` | `'0'` | FoxPro hidden `_NullFlags` field | d4defs.h:2813 |
| `r5i8` | `'1'` | 8-byte signed int | d4defs.h:2814 |
| `r5dbDate` | `'2'` | DBDATE (6 bytes) | d4defs.h:2815 |
| `r5dbTime` | `'3'` | DBTIME (6 bytes) | d4defs.h:2816 |
| `r5dbTimeStamp` | `'4'` | DBTIMESTAMP (16 bytes) | d4defs.h:2817 |
| `r5date` | `'5'` | automation date (double) | d4defs.h:2818 |
| `r5ui8` | `'6'` | 8-byte unsigned | d4defs.h:2819 |
| `r4dateTimeMilli` | `'7'` | datetime with millisecond accuracy | d4defs.h:2821 |

`r4charBin`/`r4memoBin` are converted to `'C'`/`'M'` in the physical header; the "binary" aspect is
stored elsewhere in the field info (d4defs.h:2771-2772).

**Creation structs**:
`FIELD4INFO { char *name; short type; unsigned short len; unsigned short dec; unsigned short nulls; }`
(d4data.h:1906-1913);
`TAG4INFO { char *name; const char *expression; const char *filter; short unique; unsigned short descending; }`
(d4data.h:1917-1924). `TAG4INFO.unique` / `CODE4.errDefaultUnique` take `e4unique`, `r4unique`,
`r4uniqueContinue` (comment d4data.h:2060) and `r4candidate` (d4defs.h:1814).

---

## 5. Other public constants (S4FOX build)

| Group | Constants | Cite |
|---|---|---|
| Open/deny modes (`CODE4.accessMode`) | `OPEN4DENY_NONE` 0, `OPEN4DENY_RW` 1, `OPEN4DENY_WRITE` 2 (`OPEN4SPECIAL` 3 internal) | d4defs.h:1753-1756 |
| Optimization modes (`optimize`, `optimizeWrite`) | `OPT4EXCLUSIVE` -1, `OPT4OFF` 0, `OPT4ALL` 1 | d4defs.h:1758-1760 |
| Log modes (`CODE4.log`) | `LOG4TRANS` 0, `LOG4ON` 1, `LOG4ALWAYS` 2, `LOG4OFF` 3 | d4defs.h:1742-1745 |
| Auto-unlock modes (`unlockAuto`) | `LOCK4OFF` 0, `LOCK4ALL` 1, `LOCK4DATA` 2 | d4defs.h:1587-1589 |
| Lock wait | `WAIT4EVER` -1 (used as `lockAttempts` = wait forever) | d4defs.h:1227 |
| Collating sequences (`collatingSequence`) | `sort4machine` 0, `sort4general` 1, `sort4croatian` 2, `sort4croatianUpper` 3, `sort4spanish` 4 | d4defs.h:1874-1878 |
| Code pages (`codePage`) | `cp0` 0, `cp437` 1, `cp850` 2, `cp1252` 3, `cp0004` 4, `cp1250` -56 | d4defs.h:1924-1933 |
| VFP collations (`Collate4name`) | `collate4none` 0, `collate4machine` 1, `collate4generalCp1252` 2, `collate4generalCp437` 3, … `collate4generalCp850` 7 | d4data.h:91-113 |
| Index format enum | `r4cdx` 200, `r4mdx` 201, `r4ntx` 202, `r4unknown` 204 (`code4indexFormat` returns these; S4FOX → r4cdx) | d4data.h:1893-1901; d4declar.h:1954 |
| File extensions | `CDX4EXT` "cdx", `MDX4EXT` "mdx", `NTX4EXT` "ntx", `DBT4EXT` "dbt", `FPT4EXT` "fpt"; S4FOX uses `INDEX4EXT` = CDX | d4defs.h:2576-2587, 2609 |
| S4FOX index limits | `I4MULTIPLY` 1, `I4MAX_KEY_SIZE` 240, `F4MAX_NUMERIC` 20, `F4MAX_DECIMAL` 19 | d4defs.h:2126-2131 |
| Key-expression source limit | `I4MAX_EXPR_SIZE` 255 (S4FOX) — "don't change, code depends on it to write to disk" | d4defs.h:2202 (warning 2195-2196; d4defs.h:2205 is the identical S4CLIPPER value) |
| CDX block size | `B4BLOCK_SIZE_INTERNAL` 512 (S4FOX) | d4defs.h:2213 |
| S4FOX file lock positions | `L4LOCK_POS` 0x7FFFFFFE, `L4LOCK_POS_OLD` 0x40000000 | d4defs.h:2178-2179 (inside `#ifdef S4FOX`) |
| Demo cap | `E4DEMO_MAX` 200 records (`e4demo` raised beyond) | d4defs.h:2122 |
| DBF version bytes written by create (S4FOX) | `compatibility==30` → 0x30 (0x31 when autoincrement/compressed/long-field-names present); FoxPro 2.x → 0x03 / 0xF5 with memo | D4CREATE.C:1342,1355-1371 |

---

## 6. CODE4 configuration members and defaults

`code4init(c4)` is a macro → `code4initLow(c4, 0, S4VERSION, code4structSize(c4), 0)` in stand-alone
(d4declar.h:400). `code4initLow` starts by `memset`-ing the whole struct to zero (C4CODE.C:874), so
**anything not listed below defaults to 0**. Defaults below are from `code4initLow`, whose body runs
C4CODE.C:798-1613 (function opens at :798, closing brace at :1613 — several defaults, incl.
`compatibility` and `limitKeySize`, are set well past line 1244).

| Member | C type | Meaning | Default (stand-alone S4FOX) | Cites (decl; default) |
|---|---|---|---|---|
| `accessMode` | char | file-open deny mode (`OPEN4DENY_*`); alias `exclusive` | 0 = `OPEN4DENY_NONE` (memset) | d4data.h:2211; C4CODE.C:874 |
| `autoOpen` | int | auto-open production index with data file | 1 | d4data.h:2058; C4CODE.C:993 |
| `createTemp` | int | create files as temporary | 0 (memset) | d4data.h:2059; C4CODE.C:874 |
| `safety` | char | create fails if file exists (no overwrite) | 1 | d4data.h:2073; C4CODE.C:994 |
| `errDefaultUnique` | short | default unique-error mode for new tags (`e4unique`/`r4unique`/`r4uniqueContinue`) | `r4uniqueContinue` (25) | d4data.h:2060; C4CODE.C:984 |
| `errCreate` | int | raise error on failed create | 1 (`c4setErrCreate(c4,1)`) | d4data.h:2153; C4CODE.C:982 |
| `errExpr` | int | raise error on bad expression | 1 | d4data.h:2061; C4CODE.C:985 |
| `errFieldName` | int | raise error on bad field name | 1 | d4data.h:2062; C4CODE.C:986 |
| `errOff` | int | suppress error message display | 0 (memset) | d4data.h:2063; C4CODE.C:874 |
| `errOpen` | int | raise error on failed open | 1 | d4data.h:2064; C4CODE.C:987 |
| `errEncrypt` | int | raise error on encryption-DLL load failure | 1 | d4data.h:2066; C4CODE.C:989 |
| `errTagName` | int | raise error on bad tag name | 1 | d4data.h:2229; C4CODE.C:991 |
| `errGo` | int | raise error on bad `d4go` record number | 1 | d4data.h:2445; C4CODE.C:1039 |
| `errSkip` | int | raise error on skip beyond BOF/EOF | 1 | d4data.h:2447; C4CODE.C:1040 |
| `errRelate` | int | raise error on relate terminate-no-match | 1 | d4data.h:2446; C4CODE.C:1041 |
| `errUnlock` | int | raise error on unlock problems | 1 | d4data.h:2436; C4CODE.C:1177 |
| `errorCode` / `errorCode2` | int / long | sticky error state (see §1) | 0 (memset) | d4data.h:2154,2156 |
| `readOnly` | int | open all files read-only | 0 (memset) | d4data.h:2155 |
| `readLock` | char | lock records when reading | 0 (memset) | d4data.h:2218 |
| `lockAttempts` | int | number of lock attempts (-1 = `WAIT4EVER`) | `WAIT4EVER` (-1) | d4data.h:2220; C4CODE.C:1043 |
| `lockAttemptsSingle` | int | attempts per lock inside a group lock | 1 | d4data.h:2214; C4CODE.C:1021 |
| `lockDelay` | unsigned int | delay between lock attempts (hundredths of a second) | 100 | d4data.h:2215; C4CODE.C:997 |
| `lockEnforce` | int | require explicit locks before record modification | 0 | d4data.h:2448; C4CODE.C:1047 |
| unlockAuto (in `c4trans.trans.unlockAuto`) | short | automatic unlocking mode (`LOCK4OFF/ALL/DATA`); accessor `code4unlockAuto` macro | `LOCK4ALL` (1) | d4declar.h:433; C4CODE.C:1238 |
| `fileFlush` | int | force hard flush on every write | 0 (memset) | d4data.h:2216 |
| `singleOpen` | short | one DATA4 instance per file per CODE4 | 1 | d4data.h:2444; C4CODE.C:1045 |
| `optimize` | int | memory-buffering of files (`OPT4*`) | `OPT4EXCLUSIVE` (-1) | d4data.h:2225; C4CODE.C:1108 |
| `optimizeWrite` | int | write-buffering of files (`OPT4*`) | `OPT4EXCLUSIVE` (-1) | d4data.h:2224; C4CODE.C:1109 |
| `memMaxPercent` / `memStartMax` | int/long | optimization memory ceiling | **`memMaxPercent` = 25** (use 25% of available memory; stand-alone non-JNI), **`memStartMax` = 0x50000** (327680, WinTel; 0xF0000 elsewhere) — both set at end of `code4initLow` inside `#ifndef S4OFF_OPTIMIZE`; see `code4memStartMax` d4declar.h:1404 | d4data.h:2226-2227; C4CODE.C:1394,1384 |
| `memSizeBlock` | unsigned | block size (bytes) for memo & index buffers | 0x400 (1024) | d4data.h:2228; C4CODE.C:1054 |
| `memSizeBuffer` | unsigned | pack/zap buffer size | 0x8000 (32768) | d4data.h:2069; C4CODE.C:1058 |
| `memSizeSortBuffer` | unsigned | sort file buffer size | 0x8000 (32768) | d4data.h:2070; C4CODE.C:1057 |
| `memSizeSortPool` | unsigned | sort pool size | 0xF000 (61440) | d4data.h:2071; C4CODE.C:1056 |
| `memSizeMemo` | unsigned | memo work-buffer size | 0x200 (512) | d4data.h:2246; C4CODE.C:1060 |
| `memSizeMemoExpr` | unsigned | max memo size usable in expressions | 0x400 (1024) | d4data.h:2249; C4CODE.C:972 |
| `memStartData` / `memExpandData` | unsigned/int | DATA4 allocation pool start/expand counts | 10 / 5 | d4data.h:2072,2068; C4CODE.C:1062-1063 |
| `memStartBlock` / `memExpandBlock` | unsigned/int | index block pool | 10 / 10 | d4data.h:2233,2230; C4CODE.C:1066,1065 |
| `memStartIndex` / `memExpandIndex` | unsigned/int | INDEX4 pool | 10 / 5 | d4data.h:2234,2231; C4CODE.C:1067,1069 |
| `memStartTag` / `memExpandTag` | unsigned/int | TAG4 pool | 10 / 10 | d4data.h:2235,2232; C4CODE.C:1068,1070 |
| `memStartLock` / `memExpandLock` | unsigned/int | lock pool | 5 / 10 | d4data.h:2213,2212; C4CODE.C:1011-1012 |
| `memStartDataFile` / `memExpandDataFile` | unsigned/int | DATA4FILE pool | 5 / 5 | d4data.h:2091,2090; C4CODE.C:1003-1004 |
| `memStartTagFile` / `memExpandTagFile` | unsigned/int | TAG4FILE pool | 10 / 5 | d4data.h:2237,2236; C4CODE.C:1006-1007 |
| `memStartIndexFile` / `memExpandIndexFile` | unsigned/int | INDEX4FILE pool | 5 / 5 | d4data.h:2276-2277; C4CODE.C:1016-1017 |
| `log` | short | transaction logging mode (`LOG4*`) | `LOG4TRANS` (0) | d4data.h:2067; C4CODE.C:1090 |
| `logOpen` | int | open/auto-create the transaction log | 1 (stand-alone) | d4data.h:2339; C4CODE.C:1093 |
| `transFileName` | char* | explicit transaction log file name | 0 (memset) | d4data.h:2301 |
| `timeout` | long | (client/server) request timeout | -1 | d4data.h:2074; C4CODE.C:1101 |
| `compatibility` | short | FoxPro compatibility: 25, 26 or 30; `==30` → VFP 3.0+ DBF (0x30 header) | **25** (`c4->compatibility = 25` under `#ifdef S4CLIENT_OR_FOX`, which is defined for S4FOX; create treats non-30 as FoxPro 2.x → 0x03/0xF5) | d4data.h:2075; C4CODE.C:1529 (S4CLIENT_OR_FOX defined d4defs.h:344/355); create logic D4CREATE.C:1342 |
| `collatingSequence` | short | collating sequence for new VFP tags (`sort4*`) | `sort4machine` (0) | d4data.h:2132; C4CODE.C:1000 |
| `codePage` | short | code page byte written to new DBF headers (`cp*`) | `cp0` (0) | d4data.h:2133; C4CODE.C:1001 |
| `collateName` / `collateNameUnicode` | enum Collate4name | VFP collation for char/unicode tags | `collate4none` / `collate4machine` | d4data.h:2585-2586; C4CODE.C:1075-1076 |
| `foxCreateIndexBlockSize` | unsigned | CDX block size for created indexes (multiple of 512) | 512 | d4data.h:2292; C4CODE.C:1079 |
| `foxCreateIndexMultiplier` | unsigned | CDX pointer multiplier | 1 | d4data.h:2293; C4CODE.C:1080 |
| `limitKeySize` | char | enforce `I4MAX_KEY_SIZE` on tag creation (`code4limitKeySizeSet`, d4declar.h:353) | **1** (`c4->limitKeySize = 1`, key size limited by default) | d4data.h:2138; C4CODE.C:1553 |
| `numericStrLen` / `decimals` | int | default numeric key len / decimals (Clipper) | 17 / 2 | d4data.h:2087-2088; C4CODE.C:976-977 |
| `indexExtension[4]` / `memoExtension[4]` | char[] | resolved lazily: "cdx"/"fpt" for S4FOX | zero; filled on first `code4indexExtension` call | d4data.h:2115-2116; C4CODE.C:2861-2869 (strings d4defs.h:2578,2586) |
| `hWnd` / `hInst` | HWND/HINSTANCE | window/instance for error message boxes | 0 (memset) | d4data.h:2077,2282 |
| `errorLog` | FILE4* | error-log file handle | 0 (memset) | d4data.h:2130 |
| `doTrans` | int | maintain transaction file | 0 (memset) | d4data.h:2251 |
| Date format (in `c4trans.trans.dateFormat`) | string | picture for date↔string conversion | "MM/DD/YY" via `code4dateFormatSet` | C4CODE.C:1126; accessors d4declar.h:414-415 |

Other init-time actions: `initialized = 1` (C4CODE.C:1176); transaction subsystem initialized with
`code4tranInit(c4)` (C4CODE.C:1114); version check raises `e4version` (C4CODE.C:822-841); struct
size check raises `e4verify` (C4CODE.C:844-864).

---

## 7. Public API inventory

Legend: **[F]** exported function; **[M]** macro (must become a method/property in C#);
**Core** = port it; **Conv** = convenience, port when needed; **OOS** = out of scope
(client/server, report writer, VB/Delphi thunks, OLE-DB glue).

### 7.1 `code4*` — engine/session (CODE4)

| Name | Kind | Signature (stand-alone) | Behavior | Port |
|---|---|---|---|---|
| `code4init` | M → `code4initLow(c4,0,S4VERSION,structSize,0)` | `int(CODE4*)` | initialize CODE4, set defaults (§6) | Core (ctor) — d4declar.h:400 |
| `code4initLow` | F | `int(CODE4*, const char*, long, LONGLONG, char)` | real initializer w/ version & struct-size verify | Core — d4declar.h:348; C4CODE.C:798 |
| `code4initUndo` | F | `int(CODE4*)` | close everything, free all memory (counterpart of init) | Core (Dispose) — d4declar.h:351 |
| `code4alloc`/`code4initAlloc` | M | allocate+init CODE4 on heap | Conv (C# ctor covers) — d4declar.h:399,401 |
| `code4close` | F | `int(CODE4*)` | close all open DATA4 files | Core — d4declar.h:410 |
| `code4exit` | F | `void(CODE4*)` | initUndo + exit process | OOS (process exit) — d4declar.h:417 |
| `code4flush` | F | `int(CODE4*)` | flush all open files | Core — d4declar.h:419 |
| `code4data` | F | `DATA4*(CODE4*, const char* alias)` | find open DATA4 by alias | Core — d4declar.h:345 |
| `code4dateFormat`/`code4dateFormatSet` | F | get/set date picture ("MM/DD/YY" etc.) | Core (property) — d4declar.h:414-415 |
| `code4indexExtension` / `code4memoExtension` | F | `const char*(CODE4*)` | "cdx"/"fpt" for S4FOX | Conv — d4declar.h:346-347; C4CODE.C:2856-2878 |
| `code4indexFormat` | F | `enum index4format(CODE4*)` | returns `r4cdx` for S4FOX | Conv — d4declar.h:1954 |
| `code4indexBlockSize`/`code4indexBlockSizeSet` | F | get/set CDX create block size | Conv — d4declar.h:1539-1541 |
| `code4optStart` | F | `int(CODE4*)` | start memory optimization (read caching) | Core — d4declar.h:368 |
| `code4optSuspend` | F | `int(CODE4*)` | suspend optimization, flush caches | Core — d4declar.h:369 |
| `code4optAll` | F | `int(CODE4*)` | register all open files for optimization | Conv — d4declar.h:424 |
| `code4lock` | F | `int(CODE4*)` | lock everything registered (group lock) | Core — d4declar.h:354 |
| `code4unlock` | F | `int(CODE4*)` | release all locks on all open files | Core — d4declar.h:370 |
| `code4lockClear` | F | `void(CODE4*)` | clear pending group-lock registrations | Conv — d4declar.h:363 |
| `code4lockFileName`/`code4lockNetworkId`/`code4lockUserId`/`code4lockItem` | F | describe the blocking lock after `r4locked` | Conv — d4declar.h:364-367 |
| `code4unlockAuto` / `code4unlockAutoSet` | M (→ `c4trans.trans.unlockAuto`) | get/set `LOCK4OFF/ALL/DATA` | Core (property) — d4declar.h:433,1325 |
| `code4tranStart` / `code4tranCommit` / `code4tranRollback` | F | `int(CODE4*)` | transaction control (see §8) | Core — c4trans.H:73,111,72 |
| `code4tranInit` / `code4tranInit2` | F | init transaction subsystem / with explicit log name | Core — c4trans.H:71,99 |
| `code4tranStatus` | M | `(c4)->c4trans.trans.currentTranStatus` (`r4inactive/r4active/...`) | Core (property) — c4trans.H:85 |
| `code4transFileEnable` | F | `int(CODE4TRANS*, const char*, int doCreate)` | open/create the transaction log file | Core — d4declar.h:1960 |
| `code4logCreate` / `code4logOpen` / `code4logFileName` / `code4logOpenOff` | F | manage transaction log file | Core — d4declar.h:425-428 |
| `code4autoIncrementStart` | F | `void(CODE4*, double)` | seed autoincrement for next create | Conv — d4declar.h:356 |
| `code4largeOn` | F | `void(CODE4*)` | enable >4GB file support (S4FILE_EXTENDED) | Conv — d4declar.h:355 |
| `code4limitKeySizeSet` | F | `void(CODE4*, short)` | enforce max key size | Conv — d4declar.h:353 |
| `code4collate` / `code4collateUnicode` | F | `short(CODE4*, short collateType)` | set VFP collation for new tags | Core — d4declar.h:1406-1407 |
| `code4collateName*Set` | F | set `Collate4name` directly | Conv — d4declar.h:2635-2636 |
| `code4calcCreate` / `code4calcReset` | F | named calculations for expressions | Conv — e4expr.h:339-340 |
| `code4version` | F | `long(CODE4*)` | library version | Conv — d4declar.h:1931 |
| `code4timeoutHook`, `code4lockHook` | F | optional user hooks | Conv — d4declar.h:1945,1949 |
| `code4validate` | F | table-validation subsystem | Conv — d4declar.h:3048 |
| `c4get*/c4set*` family | F | ~60 getters/setters mirroring CODE4 members for cross-DLL use (`c4getAccessMode` d4declar.h:1414 … `c4setSafety` d4declar.h:1525) | OOS as functions — become C# properties |
| `code4connect`, `code4serverName`, `code4tables`, pipes, compression/encryption config (`code4compressConfigure` d4declar.h:281, `code4encryptConnection` d4declar.h:340) | F | client/server & wire encryption | OOS |
| `code4encryptInit` / `code4encryptFile` | F | file encryption via external DLL | OOS initially (returns `e4notSupported` when absent) — d4declar.h:2863-2864 |

### 7.2 `error4*` — see §1 (all Core; become exception machinery + `ErrorText` map).

### 7.3 `d4*` — table/record (DATA4)

| Name | Kind | Signature | Behavior | Port |
|---|---|---|---|---|
| `d4create` | F | `DATA4*(CODE4*, const char* name, const FIELD4INFO*, const TAG4INFO*)` | create DBF (+production CDX if tags) and open it | Core — d4declar.h:498 |
| `d4createTemp` | F | `DATA4*(CODE4*, const FIELD4INFO*, const TAG4INFO*)` | create temporary table | Conv — d4declar.h:500 |
| `d4open` | F | `DATA4*(CODE4*, const char* name)` | open DBF (+auto-open production index if `autoOpen`) | Core — d4declar.h:601 |
| `d4openClone` | F | `DATA4*(DATA4*)` | second cursor on same file | Conv — d4declar.h:608 |
| `d4close` | F | `int(DATA4*)` | close table (+its indexes/memo) | Core — d4declar.h:497 |
| `d4alias` / `d4aliasSet` | F | get/set alias | Core — d4declar.h:467-468 |
| `d4fileName` | F | `const char*(DATA4*)` | file name | Core — d4declar.h:514 |
| `d4fullPath` | F | full path of data file | Conv — d4declar.h:1984 |
| `d4append` | F | `int(DATA4*)` | append current buffer as new record (writes keys, unique checks) | Core — d4declar.h:470 |
| `d4appendBlank` | F | `int(DATA4*)` | append a blank record | Core — d4declar.h:473 |
| `d4appendStart` | F | `short(DATA4*, short useMemoEntries)` | prepare append (memo recycling) | Core — d4declar.h:475 |
| `d4blank` | F | `void(DATA4*)` | blank the record buffer | Core — d4declar.h:481 |
| `d4go` | M → `d4goLow2(d4,rec,1,0)` | `int(DATA4*, long recNo)` | position to physical record | Core — d4declar.h:534 (impl 531) |
| `d4goBof` / `d4goEof` | F | position to BOF/EOF marker | Core — d4declar.h:535-536 |
| `d4top` / `d4bottom` | F | `int(DATA4*)` | first/last record in current tag order (or physical) | Core — d4declar.h:662,483 |
| `d4skip` | F | `int(DATA4*, const long n)` | move n records in tag order; returns r4eof/r4bof at ends | Core — d4declar.h:656 |
| `d4seek` | F | `int(DATA4*, const char* key)` | seek string key in selected tag; r4success/r4found/r4after/r4eof | Core — d4declar.h:690 |
| `d4seekDouble` | F | `int(DATA4*, const double)` | seek numeric key | Core — d4declar.h:691 |
| `d4seekN` | F | `short(DATA4*, const char*, const short len)` | seek partial (length-limited) key | Core — d4declar.h:697 |
| `d4seekNext` / `d4seekNextDouble` / `d4seekNextN` | F | seek next duplicate | Core — d4declar.h:719-721 |
| `d4seekLongLong` / `d4seekNextLongLong` | F | seek 8-byte int key (r5i8 tags) | Conv — d4declar.h:694-695 |
| `d4bof` / `d4eof` | F | `int(DATA4*)` | at BOF/EOF? | Core — d4declar.h:482,502 |
| `d4recNo` | M → `d4recNoLow` | `long(DATA4*)` | current record number | Core — d4declar.h:634 (impl 628) |
| `d4recCount` | M → `d4recCountDo2(d4,!ASSUME4LOCKED)` | `long(DATA4*)` | number of records | Core — d4declar.h:672 (impl 625) |
| `d4record` | M → `d4recordLow` | `char*(DATA4*)` | pointer to record buffer | Core — d4declar.h:635 (impl 642) |
| `d4recordOld` | M | `((d4)->recordOld)` old (pre-change) buffer | Conv — d4declar.h:653 |
| `d4recWidth` | M → `d4recWidthLow` | `unsigned long(DATA4*)` | record width incl. deleted flag | Core — d4declar.h:640 (impl 654) |
| `d4position` / `d4positionSet` / `d4position2` | F | relative position 0..1 in tag/file | Conv — d4declar.h:623,764,624 |
| `d4write` | M → `d4writeLow(d4,rec,0,1)` | `int(DATA4*, long rec)` | write record buffer to record `rec`, update keys | Core — d4declar.h:754 (impl 664) |
| `d4flush` | F | `int(DATA4*)` | flush data+index+memo of this table | Core — d4declar.h:521 |
| `d4refresh` / `d4refreshRecord` | F | re-read from disk (multi-user) | Core — d4declar.h:655,765 |
| `d4changed` | F | `short(DATA4*, short flag)` | mark/query record-buffer changed flag | Core — d4declar.h:756 |
| `d4delete` / `d4recall` / `d4deleted` | F | set/clear/test soft-delete flag on current record | Core — d4declar.h:501,758,745 |
| `d4pack` | F | `int(DATA4*)` | physically remove deleted records (+reindex) | Core — d4declar.h:610 |
| `d4packWithProgress` | F | pack w/ callback | Conv — d4declar.h:622 |
| `d4zap` | F | `int(DATA4*, long from, long to)` | delete record range physically | Core — d4declar.h:665 |
| `d4memoCompress` | F | `int(DATA4*)` | rewrite FPT removing dead space | Core — d4declar.h:586 |
| `d4reindex` / `d4reindexWithProgress` | F | rebuild all open index files | Core / Conv — d4declar.h:688-689 |
| `d4remove` | F | `int(DATA4*)` | close and delete table files from disk | Core — d4declar.h:753 |
| `d4field` | F | `FIELD4*(DATA4*, const char* name)` | field by name | Core — d4declar.h:508 |
| `d4fieldJ` | F | `FIELD4*(DATA4*, short j)` | field by 1-based index | Core — d4declar.h:510 |
| `d4fieldNumber` | F | `int(DATA4*, const char*)` | field index by name | Core — d4declar.h:511 |
| `d4fieldInfo` | F | `FIELD4INFO*(DATA4*)` | reconstruct FIELD4INFO array (caller frees) | Core — d4declar.h:752 |
| `d4numFields` | F | `short(DATA4*)` | number of fields | Core — d4declar.h:599 |
| `d4fieldsAdd` / `d4fieldsRemove` / `d4modifyStructure` | F | restructure table | Conv — d4declar.h:512-513,590 |
| `d4index` | F | `INDEX4*(DATA4*, const char* name)` | find open index file by name | Core — d4declar.h:537 |
| `d4indexList` | F | `LIST4*(DATA4*)` | list of open indexes | Conv — d4declar.h:785 |
| `d4indexesRemove` | F | remove all index files of table | Conv — d4declar.h:1060 |
| `d4tag` | F | `TAG4*(DATA4*, const char* name)` | find tag by name across open indexes | Core — d4declar.h:698 |
| `d4tagDefault` | F | first tag | Conv — d4declar.h:699 |
| `d4tagNext` / `d4tagPrev` | F | iterate tags | Core — d4declar.h:700-701 |
| `d4tagSelect` / `d4tagSelected` | F | set/get active tag (NULL = record order) | Core — d4declar.h:702,704 |
| `d4tagSync` | F | sync record position to tag | Conv — d4declar.h:705 |
| `d4numTags` | F | tag count | Conv — d4declar.h:706 |
| `d4lock` | F | `int(DATA4*, const long recNo)` | lock one record | Core — d4declar.h:562 |
| `d4lockAll` / `d4lockFile` / `d4lockAppend` | F | lock all records+file / whole file / append zone | Core — d4declar.h:556,561,559 |
| `d4lockAdd*` | F | register locks for group `code4lock` | Conv — d4declar.h:552-555 |
| `d4lockTest` / `d4lockTestFile` / `d4lockTestAppend` | F/M | test lock ownership | Conv — d4declar.h:566,736,580 |
| `d4unlock` | F | `int(DATA4*)` | release this table's locks | Core — d4declar.h:713 |
| `d4optimize` / `d4optimizeWrite` | F | per-file read/write buffering control | Core — d4declar.h:763,609 |
| `d4freeBlocks` | F | free cached index blocks | Conv — d4declar.h:527 |
| `d4check` | F | verify index integrity vs data (`E4ANALYZE`-style check) | Conv — d4declar.h:484 |
| `d4copyTable` | F | copy table+optionally index to folder | Conv — d4declar.h:496 |
| `d4compress` | F | create compressed copy of table | OOS (CodeBase-specific compressed-DBF feature) — d4declar.h:2460 |
| `d4log` / `d4logStatus` | F/M | per-table transaction logging enable/query | Core — d4declar.h:583,782 |
| `d4versionNumber` | F | change counter of data file | Conv — d4declar.h:2166 |
| `d4readBuffer` / `d4writeBuffer` / `d4skipCache` etc. | F | client/server batch buffering | OOS — d4declar.h:2103-2106 |
| `d4blankLow`, `d4goData`, `d4read`, `d4readOld`, `dfile4*` family | F | internal (DATA4FILE layer) | internal — e.g. d4declar.h:2095-2101,1967-2018 |

### 7.4 `f4*` — field access (FIELD4)

All Core unless noted. Assign functions do NOT write to disk; they modify the record buffer.

| Name | Signature | Behavior | Cite |
|---|---|---|---|
| `f4assign` | `void(FIELD4*, const char*)` | assign string (type-converting) | d4declar.h:846 |
| `f4assignN` | `void(FIELD4*, const char*, unsigned)` | assign counted string | d4declar.h:859 |
| `f4assignChar` | `void(FIELD4*, const int)` | assign single char | d4declar.h:847 |
| `f4assignDouble` / `f4assignFloat` | assign double / binary float | d4declar.h:850,849 |
| `f4assignInt` / `f4assignLong` / `f4assignLongLong` | assign integer values | d4declar.h:852-857 |
| `f4assignField` | copy field to field | d4declar.h:851 |
| `f4assignNull` / `f4assignNotNull` | set/clear null flag (`r4system` `_NullFlags` field) | d4declar.h:860,919 |
| `f4assignCurrency` / `f4assignDateTime` | assign from string forms | d4declar.h:457,460 |
| `f4assignUnicode` / `f4assignWideString` | assign wide string | d4declar.h:862-863 |
| `f4assignPtr` | `char*(FIELD4*)` raw writable pointer into record buffer (marks changed) | d4declar.h:861 |
| `f4blank` | blank one field | d4declar.h:873 |
| `f4char` | first char of field | d4declar.h:874 |
| `f4str` | `char*(FIELD4*)` null-terminated copy (static buf) | d4declar.h:914 |
| `f4ncpy` | copy up to n bytes out | d4declar.h:903 |
| `f4double` / `f4double2` / `f4float` / `f4int` / `f4long` / `f4longLong` | numeric readers | d4declar.h:877-884, 218 |
| `f4currency` / `f4dateTime` | string forms of Y/T fields | d4declar.h:458-459 |
| `f4true` | logical field true? | d4declar.h:915 |
| `f4null` / `f4nullable` | null state / nullability | d4declar.h:904-905 |
| `f4name` / `f4type` / `f4len` / `f4decimals` | metadata | d4declar.h:902,916,882,876 |
| `f4data` | owning DATA4 | d4declar.h:875 |
| `f4number` | field ordinal | d4declar.h:906 |
| `f4ptr` | raw read pointer into record buffer | d4declar.h:912 |
| `f4isMemo` | is memo-backed | d4declar.h:924 |
| `f4memoAssign` / `f4memoAssignN` | write memo content | d4declar.h:886-887 |
| `f4memoStr` / `f4memoPtr` / `f4memoNcpy` / `f4memoLen` | read memo content/length | d4declar.h:892,891,890,889 |
| `f4memoAssignFile` / `f4memoFile` | load/save memo from/to disk file | d4declar.h:899,898 |
| `f4memoFree` | release cached memo memory | d4declar.h:888 |
| `f4memoAssignUnicode` / `f4memoChanged` / `f4memoLenMax` | memo extras | d4declar.h:864,895-896 |
| `f4accessMode` / `f4lockEnforce` | Conv (config passthrough) | d4declar.h:920,922 |

### 7.5 `file4*` — low-level file layer (FILE4)

Core for the port's internal I/O layer; not necessarily public in C#.
`file4create(FILE4*, CODE4*, const char*, int exclusive)` d4declar.h:931; `file4open` d4declar.h:963;
`file4openTest` d4declar.h:967; `file4close` d4declar.h:927; `file4read` / `file4readAll`
d4declar.h:975-976; `file4write` d4declar.h:989; `file4lenLow` d4declar.h:936; `file4lenSet` macro
d4declar.h:952; `file4lock`/`file4unlock` macros → `file4lockInternal`/`file4unlockInternal`
d4declar.h:955-957,984-986; `file4flush` d4declar.h:935; `file4refresh` d4declar.h:981;
`file4replace` d4declar.h:982; `file4temp` d4declar.h:994; `file4optimize` macro d4declar.h:969;
`file4optimizeWrite` d4declar.h:971; `file4exists` d4declar.h:941.
Sequential I/O: `file4seqReadInit`/`file4seqRead`/`file4seqReadAll` (d4declar.h:1009,1014-1015),
`file4seqWriteInit`/`file4seqWrite`/`file4seqWriteFlush`/`file4seqWriteRepeat`
(d4declar.h:1022-1026). In C#: replace with `FileStream`-based class preserving lock offsets
(§5 `L4LOCK_POS` 0x7FFFFFFE, d4defs.h:2180).

### 7.6 `i4*` — index file (INDEX4; a CDX file containing tags)

| Name | Signature | Behavior | Port |
|---|---|---|---|
| `i4create` | `INDEX4*(DATA4*, const char* name, const TAG4INFO*)` | create index file (NULL name → production CDX) | Core — d4declar.h:1044 |
| `i4createWithProgress` | with callback | Conv — d4declar.h:1043 |
| `i4open` | `INDEX4*(DATA4*, const char* name)` | open non-production index | Core — d4declar.h:1051 |
| `i4close` | `int(INDEX4*)` | close index file | Core — d4declar.h:1041 |
| `i4fileName` | `const char*(INDEX4*)` | file name | Core — d4declar.h:1049 |
| `i4reindex` | `int(INDEX4*)` | rebuild all tags in file | Core — d4declar.h:1052 |
| `i4tag` | `TAG4*(INDEX4*, const char*)` | find tag by name | Core — d4declar.h:1053 |
| `i4tagAdd` | `int(INDEX4*, const TAG4INFO*)` | add tag(s) to existing CDX | Core — d4declar.h:1054 |
| `i4tagRemove` | `int(TAG4*)` | remove tag from CDX | Core — d4declar.h:1056 |
| `i4tagInfo` | `TAG4INFO*(INDEX4*)` | reconstruct TAG4INFO array | Conv — d4declar.h:1055 |

### 7.7 `t4*` — tag (TAG4). Many are macros over the internal `tfile4*` layer (TAG4FILE)

| Name | Kind | Behavior | Port |
|---|---|---|---|
| `t4open` | M → `t4openLow(d4, i4, name, 0)` | open single tag | Conv — d4declar.h:1142,1144 |
| `t4create` | F | `TAG4*(DATA4*, const TAG4INFO*, INDEX4*, int)` create standalone tag | Conv — d4declar.h:1146 |
| `t4close` | F | close tag | Conv — d4declar.h:1139 |
| `t4alias` | F | tag name | Core — d4declar.h:1136 |
| `t4expr` | M → `(t4)->tagFile->expr->source` | key expression source | Core — d4declar.h:1164 |
| `t4filter` | M → `(t4)->tagFile->filter->source` (or 0) | filter expression source | Core — d4declar.h:1153 |
| `t4unique` / `t4uniqueSet` / `t4uniqueModify` | F | get/set unique mode (`r4unique`/`r4uniqueContinue`/`e4unique`/`r4candidate`; modify rewrites header) | Core — d4declar.h:1145,1157,1141 |
| `t4descending` | F | is descending | Core — d4declar.h:1169 |
| `t4count` | M → `tfile4count(t4->tagFile)` | number of keys | Conv — d4declar.h:1140 |
| `t4eof` | M → `tfile4eof` | tag positioned at eof | Conv — D4INLINE.H:196 |
| `t4position` | F | position as fraction | Conv — d4declar.h:2572 |
| `t4seekN` | F | seek in tag w/o moving data (optional) | Conv — d4declar.h:541 |
| `t4index` | F | owning INDEX4 | Conv — d4declar.h:538 |
| `t4fileName` | F | index file name for tag | Conv — d4declar.h:1050 |
| `t4tagInfo` | F | TAG4INFO of one tag | Conv — d4declar.h:1175 |
| `t4reindex` | F | rebuild one tag | Conv — d4declar.h:2657 |
| `t4field` | F | FIELD4 if key is a simple field | Conv — d4declar.h:2959 |
| `t4keyLenExported` | F | key length | Conv — d4declar.h:1682 |
| `tfile4top/bottom/skip/seek/go/key/recNo/position/positionSet/count/eof` | F | internal navigation on TAG4FILE (used by d4-layer) | internal Core — d4declar.h:2563-2580 |

### 7.8 `expr4*` — dBASE expression engine (EXPR4)

| Name | Kind | Signature | Behavior | Port |
|---|---|---|---|---|
| `expr4parse` | M → `expr4parseLow(d4, src, 0)` | `EXPR4*(DATA4*, const char*)` | compile expression against table | Core — D4INLINE.H:127; e4expr.h:326 |
| `expr4free` | M | free expression (+work buffer) | Core — e4expr.h:322 |
| `expr4double` / `expr4double2` | F | evaluate as double | Core — e4expr.h:320-321 |
| `expr4str` | F | evaluate as string (static buffer) | Core — e4expr.h:328 |
| `expr4true` | F | evaluate as logical | Core — e4expr.h:329 |
| `expr4vary` | F | `int(EXPR4*, char**)` evaluate, result type-dependent; returns length | Core — e4expr.h:332 |
| `expr4type` | M → `(e4)->type` | result type (`r4str`/`r4date`/`r4dateDoub`/`r4log`/`r4num`/`r4numDoub`/`r4memo`…) | Core — e4expr.h:330 |
| `expr4len` | M → `(e4)->len` | result length | Core — e4expr.h:324 |
| `expr4source` | F | original source text | Core — e4expr.h:327 |
| `expr4data` | M | owning DATA4 | Core — e4expr.h:318 |
| `expr4context` | F | re-bind expression to another DATA4 | Conv — e4expr.h:313 |
| `expr4null` | M → `expr4nullLow(e4,1)` | did last eval hit null | Conv — e4expr.h:366 |
| `expr4key` | F | `int(EXPR4*, char**, TAG4FILE*)` evaluate to index-key bytes (collation applied) | internal Core — e4expr.h:362 |
| `expr4keyLen` / `expr4keyLenFromType` | F | key length calculation | internal Core — e4expr.h:359-360 |
| `expr4execute` | F | raw evaluate | internal — e4expr.h:341 |
| `expr4calc*` + `code4calcCreate/Reset` | F | named calc variables in expressions | Conv — e4expr.h:339-349 |
| `expr4currency` | F | currency result reader | Conv — d4declar.h:3034 |
| `e4functions` / `expr4functions` | F | table of built-in dBASE functions | internal Core — e4expr.h:336,342 |

### 7.9 `relate4*` — relations & query optimization (RELATE4/RELATION4)

Constants: relation types `relate4exact` 108, `relate4scan` 109, `relate4approx` 110; error actions
`relate4blank` 105, `relate4skipRec` 106, `relate4terminate` 107; internals `relate4filterRecord`
101, `relate4doRemove` 102, `relate4skipped` 104, `relate4sortSkip` 120, `relate4sortDone` 121,
`relate4continue` 200 (R4RELATE.H:22-33).

| Name | Signature | Behavior | Port |
|---|---|---|---|
| `relate4init` | `RELATE4*(DATA4* master)` | create relation tree rooted at master table | Core — R4RELATE.H:326 |
| `relate4createSlave` | `RELATE4*(RELATE4*, DATA4* slave, const char* masterExpr, TAG4* slaveTag)` | add child relation (master expr looked up in slave tag; NULL tag = recno lookup) | Core — R4RELATE.H:317 |
| `relate4querySet` | `int(RELATE4*, const char* filterExpr)` | set filter over the composite; enables bitmap/tag optimization | Core — R4RELATE.H:335 |
| `relate4sortSet` | `int(RELATE4*, const char* sortExpr)` | set sort expression (temp-file sort) | Core — R4RELATE.H:349 |
| `relate4sortSetTag` | `int(RELATE4*, TAG4*)` | sort using tag collation | Conv — R4RELATE.H:351 |
| `relate4top` / `relate4bottom` | `int(RELATE4*)` | first/last composite match | Core — R4RELATE.H:352,314 |
| `relate4skip` | `int(RELATE4*, const long n)` | move through matches (returns `r4eof`/`r4bof`/`r4terminate`) | Core — R4RELATE.H:345 |
| `relate4skipMaster` | skip master table only | Conv — R4RELATE.H:347 |
| `relate4skipEnable` | `int(RELATE4*, const int)` | enable backward skipping (extra bookkeeping) | Core — R4RELATE.H:348 |
| `relate4eof` | `int(RELATE4*)` | past end | Core — R4RELATE.H:320 |
| `relate4doAll` / `relate4doOne` | `int(RELATE4*)` | position all slaves / this one for current master | Core — R4RELATE.H:318-319 |
| `relate4next` | `int(RELATE4**)` | tree iteration | Conv — R4RELATE.H:329 |
| `relate4count` | `unsigned long(RELATE4*)` | count matches (bitmap-optimized when `fullyMapped`, R4RELATE.H:262) | Core — R4RELATE.H:316 |
| `relate4type` | `int(RELATE4*, int)` | set `relate4exact`/`relate4scan`/`relate4approx` | Core — R4RELATE.H:353 |
| `relate4errorAction` | `int(RELATE4*, const int)` | set `relate4blank`/`relate4skipRec`/`relate4terminate` | Core — R4RELATE.H:321 |
| `relate4matchLen` | `int(RELATE4*, const int)` | chars of key that must match | Conv — R4RELATE.H:328 |
| `relate4free` | `int(RELATE4*, const int closeFiles)` | free whole relation tree | Core — R4RELATE.H:322 |
| `relate4freeRelate` | free one node (CodeReporter) | Conv — R4RELATE.H:355 |
| `relate4changed` | notify structure changed | Conv — R4RELATE.H:315 |
| `relate4optimizeable` | `int(RELATE4*)` | can query be bitmap-optimized? | Conv — R4RELATE.H:371 |
| `relate4lock` / `relate4unlock` / `relate4lockAdd` | lock all tables of relation | Conv — R4RELATE.H:356-357,369 |
| `relate4retain` | keep positions across doAll | Conv — R4RELATE.H:342 |
| `relate4data`/`relate4dataTag`/`relate4master`/`relate4masterExpr` | M | accessors | Core — R4RELATE.H:359-362 |
| `relate4readBuffer`, `relate4callbackInit`, `relate4querySetCallbackInit` | client-server / Win32 callback plumbing | OOS — R4RELATE.H:344,332,338 |
| Bitmap internals: `bitmap4create/evaluate/seek/destroy`, `log4*`, `const4*` | internal query optimizer (leaf/AND/OR bitmaps over tag ranges, `BITMAP4LEAF` 0x40 / `BITMAP4AND` 0x20 R4RELATE.H:268-269) | internal Core — R4RELATE.H:378-405 |

### 7.10 `date4*` — date helpers (Conv; C# should use DateTime but must keep CCYYMMDD/julian conversions for file format)

`date4assign` macro → `date4assignLow(buf, julian, 0)` (d4declar.h:788-789); `date4long` (798);
`date4today` (802); `date4timeNow` (799); `date4cdow`/`date4cmonth` (790-791); `date4dow` (792);
`date4format`/`date4init` (793,796); `date4isLeap` (797); `date4formatMdx`/`Mdx2` (794-795);
component macros `date4day`/`date4month`/`date4year` (803-805); julian helpers
`date4yymmddToJulianLong` (232), `c4ytoj` (253), `c4monDy` (254).

### 7.11 Support modules

- `u4*` (alloc/name utils, d4declar.h:1179-1215, 2753-2763) — internal; replace with .NET equivalents.
- `mem4*` pooled allocator (d4declar.h:1125-1132, 2406-2448) — **do not port**; GC replaces it.
- `l4*`/`LIST4` intrusive linked lists (d4declar.h:1081-1110) — replace with .NET collections.
- `sort4*` external merge sort (S4SORT.H; used by reindex & relate sort) — internal Core.
- `c4atod/c4atol/c4dtoa45/c4ltoa45` etc. conversions (d4declar.h:224-254, 1896-1899) — internal Core
  (must replicate DBF numeric-text formatting exactly).
- `report4*`/`style4*`/`area4*`/`group4*`/`total4*`/`obj4*` (R4REPORT.H), `pipe4*` (d4declar.h:391-397),
  `*VBCE`/`*thunk4*`/`code4*VB` wrappers (d4declar.h:3098-3187, 1343-1401), OLE-DB helpers — **OOS**.

---

## 8. Transaction API (stand-alone)

The public API is `code4tran*` — there is no `d4tranBegin`-style API.

| Function | Signature | Behavior | Cite |
|---|---|---|---|
| `code4tranInit` | `int(CODE4*)` | init transaction subsystem (called by code4init) | c4trans.H:71 |
| `code4tranInit2` | `int(CODE4*, const char* logName, const char* userId)` | init with explicit log file (stand-alone export) | c4trans.H:99 |
| `code4tranStart` | `int(CODE4*)` | begin transaction; status → `r4active` (120) | c4trans.H:73 |
| `code4tranCommit` | `int(CODE4*)` | commit (two-phase internally: `code4tranCommitPhaseOne/Two`, c4trans.H:69-70) | c4trans.H:111 |
| `code4tranRollback` | `int(CODE4*)` | roll back using log file | c4trans.H:72 |
| `code4tranStartSingle` / `code4tranCommitSingle` | `int(CODE4*)` | non-nested single variants | c4trans.H:83,112 |
| `code4tranStatus` | M → `c4->c4trans.trans.currentTranStatus` | `r4inactive` 110 / `r4active` 120 / `r4rollback` 130 / `r4off` -2 | c4trans.H:85; d4defs.h:1829-1832,1803 |
| `code4tranStatusSet` | M | force status | c4trans.H:86 |
| `code4transEnabled` | M | `c4->c4trans.enabled && status != r4rollback && status != r4off` | D4INLINE.H:84 |
| `code4transFileEnable` | `int(CODE4TRANS*, const char*, int doCreate)` | open/create log file | d4declar.h:1960 |
| `tran4fileTop/Bottom/Skip`, `tran4type/id/len/getData` | log-file examination (utilities) | Conv — c4trans.H:207-228 |

Log-entry type codes (`TRAN4*`, used inside the log file): `TRAN4OPEN` 1, `TRAN4OPEN_TEMP` 2,
`TRAN4CLOSE` 3, `TRAN4START` 4, `TRAN4COMMIT_PHASE_ONE` 5, `TRAN4COMMIT_PHASE_TWO` 6,
`TRAN4ROLLBACK` 7, `TRAN4WRITE` 8, `TRAN4APPEND` 9, `TRAN4VOID` 10, `TRAN4CREATE` 11, `TRAN4PACK` 12,
`TRAN4ZAP` 13, `TRAN4INIT` 15, `TRAN4SHUTDOWN` 16, `TRAN4BACKEDUP` 17, `TRAN4INIT_UNDO` 18,
`TRAN4CLOSE_BACKUP` 19, `TRAN4DIRECTORY` 20, `TRAN4CREATE_ENCRYPT` 21 (c4trans.H:145-169);
`TRAN4VERSION_NUM` 3 (c4trans.H:182). Transaction status default after init: `r4inactive`
(c4trans.c:4157); `unlockAuto` interplay: rollback requires records still locked, hence
`unlockAuto`/`lockEnforce` coupling (D4INLINE.C:26-59).

---

## 9. Proposed C-function-group → C# mapping

Namespace suggestion: `CodeBase.Net` (working name). All handle-owning classes implement
`IDisposable`; errors become `CodeBaseException` with `ErrorCode` (e4 value), `ErrorCode2`
(extended id) and `Description` preserved.

| C group | C# type | Notes |
|---|---|---|
| `CODE4` + `code4*` config members | `class CodeBaseEngine : IDisposable` | §6 members become properties with the cited defaults (`AccessMode` enum {DenyNone=0,DenyReadWrite=1,DenyWrite=2}, `Safety`, `AutoOpen`, `LockAttempts`, `LockDelay` (TimeSpan, stored in 1/100 s), `UnlockAuto` enum, `ReadLock`, `LockEnforce`, `Optimize`/`OptimizeWrite` enum, `Log` enum, `Compatibility`, `CollatingSequence`, `CodePage`, `MemSize*` advanced-settings bag). `code4init`/`code4initUndo` → ctor/`Dispose`; `code4close` → `CloseAll()`; `code4flush` → `FlushAll()`; `code4data` → `FindTable(alias)`. |
| `error4*`, `e4*`/`r4*` | `class CodeBaseException : Exception` + `enum ErrorCode` (e4 values) + `enum ResultCode` (r4 values) | non-error statuses (`r4found`, `r4after`, `r4eof`, `r4bof`, `r4locked`, `r4unique`, `r4entry`, `r4noRecords`, `r4terminate`) are **return values**, not exceptions. Keep numeric values identical to §2/§3. Optional `Engine.LastError` for sticky-state compatibility; `errOff`/`err*` toggles map to "throw vs return code" policy flags per operation group. |
| `DATA4` + `d4*` | `class Table : IDisposable` | `Open/Create` static factories on `CodeBaseEngine`; navigation `Top/Bottom/Skip/Go/Seek*` returning `ResultCode`; `Append*`, `Write` (or auto-flush on move with `Changed` tracking), `Pack/Zap/Reindex/Remove`; `RecordBuffer` exposed as `Span<byte>`; `Deleted`, `RecNo`, `RecCount`, `Alias`, `Fields`, `Tags`, `SelectedTag`. Locking: `Lock(recNo)`, `LockAll()`, `LockFile()`, `LockAppend()`, `Unlock()`. |
| `FIELD4` + `f4*` | `class Field` (+`MemoField`) | typed getters/setters (`GetString/SetString`, `GetDouble`, `GetInt32`, `GetDateTime`, `GetBytes`…), `IsNull`, `Name`, `Type` (enum from §4 char codes), `Length`, `Decimals`. Memo: `GetMemo…/SetMemo…`, `AssignFromFile/SaveToFile`. |
| `INDEX4` + `i4*` | `class IndexFile : IDisposable` | `Create/Open/Close/Reindex`, `Tags`, `AddTag(TagInfo)`, `RemoveTag`. Production CDX auto-managed by `Table`. |
| `TAG4` + `t4*` | `class Tag` | `Name`, `Expression`, `Filter`, `Unique` (enum {Unique=e4unique, RejectSilently=r4unique, Continue=r4uniqueContinue, Candidate=r4candidate}), `Descending`, `KeyCount`, `Position`. |
| `EXPR4` + `expr4*` | `class DbExpression : IDisposable` | `Parse(table, source)`, `Type`, `EvaluateDouble/String/Boolean/Object()`, `Source`; internal `EvaluateKey(tag)` for index maintenance. |
| `RELATE4/RELATION4` + `relate4*` | `class Relation : IDisposable` (root) + `class RelationNode` | `CreateSlave(table, masterExpr, slaveTag)`, `Query` (set → `relate4querySet`), `Sort`, `RelationType` enum {Exact=108,Scan=109,Approx=110}, `ErrorAction` enum {Blank=105,SkipRecord=106,Terminate=107}, `Top/Bottom/Skip/Count`, `Optimizeable`. Bitmap optimizer ported internally. |
| `code4tran*` | `class Transaction : IDisposable` on engine: `engine.BeginTransaction()` → commit/rollback; `TransactionStatus` enum {Off=-2,Inactive=110,Partial=115,Active=120,Rollback=130} | log file format per §8 for crash-recovery compatibility. |
| `FILE4`/`file4*`, `file4seq*` | internal `class DbfFileStream` | wraps `FileStream` + region locking at CodeBase offsets (`L4LOCK_POS` 0x7FFFFFFE etc., d4defs.h:2179-2180) for interop with running C apps. |
| `date4*`, `c4atod`-family | internal static `DbfConvert` | exact DBF text/number/date/julian formatting. |
| `mem4*`, `u4*`, `l4*`, `opt4` cache | not ported / internal | replaced by GC, `List<T>`, and optional block cache honoring `Optimize*` settings. |
| report writer, OLE-DB, VB/CE thunks, pipes, comms, encryption DLLs | not ported | OOS per task. |

---

## 10. Open questions / risks for the C# port

1. **Sticky-error semantics vs exceptions.** Vast amounts of C client code rely on
   `errorCode < 0 → all calls return e4codeBase` (§1). If the C# port throws instead, the
   short-circuit behavior disappears. Proposal: throw + keep `Engine.LastError`; needs sign-off.
2. **`err*` policy flags.** Each `errOpen/errGo/errSkip/errRelate/errTagName/errFieldName/…`
   flag turns a condition from "error" into "return code" (e.g. `errOpen=0` → `d4open` returns
   NULL with `r4noOpen` semantics instead of raising `e4open`). The C# API must decide per flag
   whether to expose Try-style methods or keep the flags. Inventory of flags: §6.
3. **`compatibility` default is 25** (`c4->compatibility = 25` under `#ifdef S4CLIENT_OR_FOX`,
   C4CODE.C:1529 — NOT 0; the memset zero is overwritten later in `code4initLow`); **only `==30`
   produces VFP 3.0 files** (D4CREATE.C:1342). With the default 25, `d4create` writes FoxPro 2.x
   headers (0x03, or 0xF5 with memo — confirmed in sample DATA files: BANK.DBF byte0=0x03,
   EXAMPLE.DBF byte0=0xF5, FOXUSER.DBF byte0=0x30). For a "VFP-compatible-first" port, consider
   defaulting the C# engine to 30 — this deviates from the C library default and must be documented.
4. **`code4initAlloc` macro typo** in the stand-alone branch: expands to `code4initAlloclow(0)`
   (lowercase `l`, d4declar.h:401) which cannot compile — evidence that stand-alone builds always
   use `code4init`, not `code4initAlloc`. No port impact, but don't mirror the macro.
5. **`C4CODE.C` contains embedded NUL bytes** (verified via `xxd`; grep treats it as binary). If the
   file is used for further extraction, use binary-tolerant tooling; content around NULs appeared
   intact where read.
6. **`lockDelay` unit** is hundredths of a second (delay loop uses `u4delayHundredth`,
   d4declar.h:2736) with default 100 (C4CODE.C:997) = 1 s between attempts, `lockAttempts` default
   `WAIT4EVER`(-1) → potential infinite waits; the C# API should surface a `TimeSpan`-based timeout.
7. **Static string buffers.** `f4str`, `expr4str`, `error4text` return pointers into per-CODE4
   buffers (`fieldBuffer`, d4data.h:2111) — thread-unsafe by design. C# returns managed strings;
   document the allocation-behavior difference for hot loops.
8. **Collation tables.** VFP "general" collations are loaded from embedded tables/`collate4.dbf`
   resources (`collate4setupReadFromDisk`, d4declar.h:2640; tables `v4general`, d4declar.h:3002).
   The exact table contents are required for byte-identical CDX keys — must be extracted in the
   index-format spec, not this document.
9. **Demo limit `E4DEMO_MAX` 200 / `e4demo` (-940)** (d4defs.h:2122,2045) must NOT be ported.
10. **`d4compress` compressed-DBF format** (d4declar.h:2460) is CodeBase-proprietary (not VFP);
    recommend excluding from v1 and returning `e4notSupported` (-1090).
11. **Unverified numeric-text edge cases.** Exact rounding/overflow behavior of `c4dtoa45`
    (d4declar.h:1896) and `c4atod` governs both field content and index keys; port needs
    char-for-char golden tests against the C library. [UNVERIFIED: exact rounding rules — algorithm
    body not analyzed in this pass.]
12. **Thread model.** CODE4 is single-threaded per instance (semaphore options `S4SEMAPHORE`
    exist but stand-alone default is none); the C# engine should document one-engine-per-thread or
    add its own locking.
