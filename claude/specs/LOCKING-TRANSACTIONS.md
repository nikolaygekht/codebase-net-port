# CodeBase Multi-User Locking and Transactions — Implementation Specification for the C# Port

**Target configuration:** S4FOX (Visual FoxPro compatible), S4STAND_ALONE (no client/server), S4WIN32.
All `#ifdef S4CLIENT` / `#ifdef S4SERVER` code paths are out of scope; they are mentioned only where
needed to identify which branch of a function the stand-alone build compiles.

**Primary sources read for this spec:**
`d4lock.c`, `d4unlock.c`, `df4lock.c`, `df4unlok.c`, `f4lock.c`, `I4LOCK.C`, `m4file.c`,
`c4trans.c`, `c4trans.H`, `D4WRITE.C`, `D4APPEND.C`, `d4go.c`, `d4data.c`, `d4data.h`, `d4defs.h`,
`D4INLINE.H`, `d4declar.h`, `d4inc.h`, `PUSH4.H`/`POP4.H`, `d4file.c`, `D4OPEN.C`, `c4set.c`, `e4string.c`.

> **Struct packing note (applies to every on-disk struct in this spec):** `d4inc.h` includes
> `push4.h` (`#pragma pack(1)` for MSVC/Borland) before `d4defs.h`/`d4data.h` and `pop4.h` after all
> CodeBase headers (d4inc.h:94, d4inc.h:97, d4inc.h:110, d4inc.h:184; PUSH4.H:17-27; POP4.H:17-27).
> Therefore all structures below are **1-byte packed, little-endian** on the Windows build.

---

## 1. Constants

### 1.1 Lock-position constants (d4defs.h)

| Constant | S4FOX value | Source |
|---|---|---|
| `L4LOCK_POS` | `0x7FFFFFFE` (2 147 483 646) | (d4defs.h:2179) |
| `L4LOCK_POS_OLD` | `0x40000000` (1 073 741 824) | (d4defs.h:2178) |
| `L4LOCK_POS_COMPRESS` | `0xEFFFFFFF` — byte locked to serialize compressed writes (S4COMPRESS builds only) | (d4defs.h:2167-2168) |
| `WAIT4EVER` | `-1` | (d4defs.h:1227) |

Other builds (for reference only): S4CLIPPER `L4LOCK_POS = 1000000000L` (d4defs.h:2171);
S4MDX `L4LOCK_POS_OLD = 0x40000000`, `L4LOCK_POS = 0xEFFFFFFF` (or `1000000000/2000000000` when
`S4NO_NEGATIVE_LOCK`) (d4defs.h:2181-2188).

### 1.2 Lock type / unlockAuto constants (d4defs.h:1587-1593)

| Constant | Value | Meaning |
|---|---|---|
| `LOCK4OFF` | 0 | `unlockAuto` off — no automatic unlocking |
| `LOCK4ALL` | 1 | `unlockAuto`: unlock everything (all open tables) before a new lock |
| `LOCK4DATA` | 2 | `unlockAuto`: unlock only the current DATA4's locks |
| `LOCK4APPEND` | 10 | group-lock entry type |
| `LOCK4FILE` | 20 | group-lock entry type |
| `LOCK4RECORD` | 30 | group-lock entry type |
| `LOCK4INDEX` | 40 | group-lock entry type |

`enum Lock4type { lock4read = 0, lock4write = 1, lock4any = 2 }` (d4data.h:1271-1278).
In stand-alone shared mode read locks are purely in-memory bookkeeping; whenever the file is not
opened exclusive (`lowAccessMode != OPEN4DENY_RW`) every lock request is forced to `lock4write`
(d4lock.c:200-206, d4lock.c:856-867).

### 1.3 Return / error codes (d4defs.h)

| Constant | Value | Source |
|---|---|---|
| `r4success` | 0 | (d4defs.h:1804) |
| `r4unique` | 20 | (d4defs.h:1815) |
| `r4locked` | 50 | (d4defs.h:1821) |
| `r4inactive` | 110 | (d4defs.h:1829) |
| `r4partial` | 115 | (d4defs.h:1830) |
| `r4active` | 120 | (d4defs.h:1831) |
| `r4rollback` | 130 | (d4defs.h:1832) |
| `r4logOn` / `r4logOff` | 160 / 180 | (d4defs.h:1835-1837) |
| `e4lock` | −50 | (d4defs.h:1941) |
| `e4unlock` | −110 | (d4defs.h:1957) |
| `e4trans` | −1210 | (d4defs.h:2071) |

Key error texts: 81523 = "WAIT4EVER lock conflicts with own local impeding lock";
83801 = "invalid transaction file - run recovery utility"; 83804 = "expected record lock missing";
83807 = "transactions/logging not enabled"; 83808 = "no active transaction to rollback";
83814 = "cannot begin transactions because CODE4::logOpen is zero" (e4string.c:572, 631-646).

### 1.4 Open (share) modes (d4defs.h:1754-1757)

| Constant | Value |
|---|---|
| `OPEN4DENY_NONE` | 0 |
| `OPEN4DENY_RW` | 1 (exclusive) |
| `OPEN4DENY_WRITE` | 2 |

When a file's `lowAccessMode == OPEN4DENY_RW`, **no physical byte locks are placed or removed at
all** — every lock/unlock is a no-op at OS level and only in-memory state is kept
(f4lock.c:500-502, f4lock.c:711-713, df4lock.c:119-121, df4lock.c:269-271).

---

## 2. CODE4 locking configuration fields

| Field | Type | Semantics | Source |
|---|---|---|---|
| `lockAttempts` | `int` | How many times to attempt a lock. `0` behaves as `1`; `WAIT4EVER (-1)` = retry forever | (d4data.h:2220; f4lock.c:517-519) |
| `lockAttemptsSingle` | `int` | Retry count for a whole *group* lock pass in `code4lock()` | (d4data.h:2214; c4trans.c:3991-4013) |
| `lockDelay` | `unsigned int` | Delay between retries, in **hundredths of a second** (`u4delayHundredth`; `u4delaySec() == u4delayHundredth(100)`) | (d4data.h:2215; f4lock.c:616; D4INLINE.H:121) |
| `lockEnforce` | `int` | If true, modifying the record buffer (field assign, `d4blank`, `d4delete`, flush) errors with `e4lock` unless the current record is locked | (c4set.c:667-670; d4data.c:245-250, 310-315; F4CHAR.C:56; f4memo.c:158) |
| `readLock` | `char` | If true, `d4go()`/record reads automatically lock the record before reading | (d4data.h:2218; d4go.c:254-283; d4declar.h:1316-1317) |
| `c4trans.trans.unlockAuto` | `short` | 0 = `LOCK4OFF`, 1 = `LOCK4ALL`, 2 = `LOCK4DATA`; accessed via `code4unlockAuto(c4)` macro | (d4data.h:1180; d4declar.h:433, 1325) |
| `largeFileOffset` | `long` | When ≠ 0, an alternate (CodeBase-proprietary, **not VFP-compatible**) lock scheme is used: locks are placed at 64-bit offsets with the high dword = `largeFileOffset` (see §3.3.5) | (d4data.h:2377; df4lock.c:123, 274-286) |
| `accessMode` | `int` | Default open share mode for `d4open` | (c4set.c dv; d4defs.h:1754) |

Defaults: the source tree does not contain `code4initLow` (the CODE4 constructor is in a file not
present in this distribution), so initial values are **[UNVERIFIED from source]**. Sequiter's
documentation states `lockAttempts = 1`, `lockDelay = 0`, `unlockAuto = 1 (LOCK4ALL)`,
`readLock = 0`, `lockEnforce = 0`; the server build explicitly sets `unlockAuto = 1` in
`code4tranInitLow` (c4trans.c:4153) and the comment at d4data.h:2151 confirms `readLock` defaults
to 0. The C# port should adopt these defaults and expose all of them as settings.

---

## 3. LOCKING

### 3.1 The low-level engine: `file4lockInternal` / `file4unlockInternal` (f4lock.c)

Signature: `file4lockInternal(FILE4 *file, unsigned long posStart, long posStartHi, unsigned long numBytes, long numBytesHi)` (f4lock.c:441).
On Win32 it calls `LockFile(handle, posStart, posStartHi, numBytes, numBytesHi)`
(f4lock.c:420-424) and `UnlockFile(...)` (f4lock.c:770-774) — i.e. **byte-range locks with 64-bit
offsets and lengths**; the retry loop:

```
if (numBytes == 0 || file.lowAccessMode == OPEN4DENY_RW) return 0        // (f4lock.c:501-502)
numAttempts = c4.lockAttempts; if (numAttempts == 0) numAttempts = 1     // (f4lock.c:517-519)
loop:
    rc = LockFile(...)                                                    // (f4lock.c:532)
    if success: return 0                                                  // (f4lock.c:544-557)
    if error is not ERROR_LOCK_VIOLATION: return e4lock error E90613      // (f4lock.c:563-593)
    if (numAttempts == 1) return r4locked                                 // (f4lock.c:600-601)
    if (numAttempts > 1)  numAttempts--                                   // (f4lock.c:602-603)
    sleep(lockDelay / 100 seconds); goto loop                             // (f4lock.c:616)
```

`WAIT4EVER (-1)` never reaches `numAttempts == 1`, so it loops forever.
`file4unlockInternal` calls `UnlockFile` with **exactly the same (offset, length)** used to lock —
Windows requires the ranges to match; a failed unlock returns `e4unlock` (error E90614 includes the
failing byte offset) (f4lock.c:662-807).

**Who-holds-it registration:** when a lock attempt returns `r4locked`, `dfile4registerLocked(dfile,
item, doExtra)` records the conflict for diagnostics: `item = -1` file lock, `0` append lock,
`>0` record number; exposed through `code4lockItem()`, `code4lockFileName()`,
`code4lockUserId()`, `code4lockNetworkId()` (the user/net names come from the TRAN4 `userId`/`netId`
registered via the log file) (f4lock.c:98-212, 238-318).

### 3.2 In-memory lock state (per process)

`DATA4FILE` (one per physical file, shared by clones in one CODE4):
`fileClientWriteLock`/`fileServerWriteLock` — owner of the file write-lock;
`appendClientLock`/`appendServerLock` — owner of the append lock;
`recordLockWriteCount`, `recordLockReadCount` — counts of record locks across all DATA4s
(d4data.h:3293-3294); `fileReadLocks` — list of `Lock4` for file read-locks (d4data.h:3346).
Each `DATA4` holds `lockedRecords`, a list of `Lock4 { data, recNum, lockType }`
(d4data.h:3541; d4lock.c:320-343). In stand-alone `data4serverId(d4) == data4clientId(d4) ==
d4->clientId` and `data4lockId(d4) == d4->lockId`, both assigned from a per-CODE4 counter
`c4->clientDataCount` at open (D4INLINE.H:62-66; D4OPEN.C:823).

Lock-test semantics used throughout (`d4lockTest`, `dfile4lockTestFileInternal`, …):
**return 1 = we hold the lock, `r4locked` (50) = another DATA4/user holds it, 0 = nobody holds it**
(df4lock.c:315-316; d4lock.c:1118).

### 3.3 The FoxPro byte-range lock protocol (VFP interop — must match exactly)

CodeBase distinguishes two DBF flavors at runtime (df4lock.c:147, df4unlok.c:302):

* **“New style” (VFP 3.0+)**: header `version` byte == `0x30` (a file `0x31` — autoincrement — is
  normalized to `0x30` in memory, d4data.h:3064) **or** the header’s `hasMdxMemo` flag byte
  (DBF header offset 28: `0x01` = production index attached, `0x02` = memo, d4data.h:3086-3088;
  read into `DATA4FILE` at D4OPEN.C:2144) has bit 0 set.
  (`dfile4lockAppend` uses the equivalent test `(hasMdxMemo & 0x01) || compatibility == 30`,
  df4lock.c:277.)
* **“Old style” (FoxPro 2.x)**: everything else.

All locks below are 1 byte long unless stated. Offsets are 32-bit (high dword = 0).

#### 3.3.1 DBF record lock

| Style | Byte locked | Source |
|---|---|---|
| New (VFP) | `0x7FFFFFFE − recNo` (`L4LOCK_POS − rec`), length 1 | (df4lock.c:147-151) |
| Old (FP 2.x) | `0x40000000 + headerLen + (recNo−1) × recWidth` (`L4LOCK_POS_OLD + recordPosition`), length 1 | (df4lock.c:152-158; record position formula d4file.c:181-187, d4file.c:398-409) |

Unlock uses the identical formula (df4unlok.c:277-325). Lock is taken with
`file4lockInternal(..., 1L, 0)` (df4lock.c:171); on success `recordLockWriteCount++`
(df4lock.c:185); unlock does `recordLockWriteCount--` (df4unlok.c:324-325).
`rec` must be ≥ 1 (df4lock.c:104).

#### 3.3.2 DBF append (EOF) lock

| Style | Byte locked | Source |
|---|---|---|
| New (VFP) | `0x7FFFFFFE` (`L4LOCK_POS`) | (df4lock.c:276-278) |
| Old (FP 2.x) | `0x40000000` (`L4LOCK_POS_OLD`) | (df4lock.c:279-280) |

Unlock identical (df4unlok.c:90-100). On success the owner ids are recorded in
`appendServerLock`/`appendClientLock` (df4lock.c:289-293). When the append lock is released,
the cached record count is invalidated (`numRecs = -1`, df4unlok.c:109) and, if needed, the DBF
header (record count / autoincrement) is flushed first (df4unlok.c:70-83).

#### 3.3.3 DBF file (table) lock

One range covering the whole lock region **including the append byte and all record-lock bytes**:

* Lock: offset `0x40000000` (`L4LOCK_POS_OLD`), length `0x3FFFFFFF` (`L4LOCK_POS_OLD − 1`)
  → covers bytes `0x40000000 … 0x7FFFFFFE` inclusive (df4lock.c:618-621, with comment
  “codebase locks the append byte as well…”).
* Unlock: same range (df4unlok.c:158-161).

Preconditions enforced before the physical lock (dfile4lockFile, df4lock.c:530-660):
the file lock is refused with `r4locked` if any other DATA4 (same process) holds the append lock
(`dfile4lockTestAppend`, df4lock.c:550-563), any record write locks exist
(`recordLockWriteCount != 0`, df4lock.c:566-579), or read locks exist when requesting
`lock4write` (df4lock.c:581-594). On success `fileClientWriteLock`/`fileServerWriteLock` are set
(df4lock.c:650-651).

#### 3.3.4 Index (CDX) and memo (FPT) locks

| File | Byte locked | Length | Notes | Source |
|---|---|---|---|---|
| `.CDX` | `0x7FFFFFFE` (`L4LOCK_POS`) | 1 | per INDEX4FILE; after locking, `i4versionCheck(index,1,1)` re-validates cached blocks against disk; `fileLocked = serverId` recorded in memory | (I4LOCK.C:378-443, lock at 384, unlock at 490) |
| `.FPT` | `0x40000000` (`L4LOCK_POS_OLD`) | 1 | taken with `lockAttempts` forced to `WAIT4EVER` (blocks until obtained); “a memo file lock only lasts if the .dbf file is locked” | (m4file.c:40-79, lock at 67, force-wait at 60-61, comment at 43; unlock m4file.c:83-102) |

`dfile4lockIndex(data, serverId)` iterates all open index files of the table calling
`index4lock` on each, retrying the whole set with `lockAttempts` restored around the loop; on
partial failure everything obtained so far is unlocked (df4lock.c:668-760).
Memo locking is invoked automatically around memo write operations (m4file.c:512, 837) and by
`d4lockAll` when the table has memo fields (d4lock.c:576-582).

#### 3.3.5 `largeFileOffset ≠ 0` variant (CodeBase-only, breaks VFP interop)

Record lock: (hi = `largeFileOffset`, lo = `recNo`), length 1 (df4lock.c:160-161);
append: (hi = `largeFileOffset`, lo = 0), length 1 (df4lock.c:285-286);
file: (hi = `largeFileOffset`, lo = 0), length `0xFFFFFFFF` (`ULONG_MAX`) (df4lock.c:624-625);
CDX: (`L4LOCK_POS`, hi = `largeFileOffset`) (I4LOCK.C:384). The C# port should implement this
only if >2 GB DBFs without VFP interop are required; default must be `largeFileOffset = 0`.

#### 3.3.6 Protocol variants in other builds (brief)

* **S4CLIPPER**: record = `1e9 + recNo`; append = byte `1e9`; file = range `[1e9, 2e9)` length
  `1e9` (df4lock.c:126-129, 282, 605-607). Each `.NTX` tag file is locked over the range
  `[1e9, 2e9)` (I4LOCK.C:190, 239).
* **S4MDX**: record = `0xEFFFFFFF − (recNo+1)`, guarded by a transient lock of byte
  `0xEFFFFFFE` (`L4LOCK_POS−1`) around the record-lock attempt (df4lock.c:131-140, 164-176);
  append = byte `0xEFFFFFFF`; file = range `[0x40000000, 0xEFFFFFFF]` length
  `L4LOCK_POS−L4LOCK_POS_OLD+1` (df4lock.c:612-615); MDX index = byte `0xEFFFFFFE`
  (I4LOCK.C:380-381); memo = 2 bytes at `0xEFFFFFFE` (m4file.c:63-64).

### 3.4 High-level lock functions and their exact behavior

All are in d4lock.c unless noted. General pattern for `WAIT4EVER` self-deadlock protection: if the
conflicting lock is held **by this process** (another DATA4/clone) and it will not be auto-released,
returning `r4locked` would spin forever under `WAIT4EVER`; instead an `e4lock` error E81523 is
raised (d4lock.c:223-229 and analogous checks at 676-694, 708-725, 890-908, 929-947; df4lock.c:70-76).

* **`d4lock(data, rec)`** = `d4lockInternal(data, rec, doUnlock=1, lockType=lock4write)`
  (d4lock.c:162-165).
  Algorithm (d4lock.c:169-350):
  1. If file is shared, force `lockType = lock4write` (200-206).
  2. `d4lockTest(data, rec, lockType)`: returns 1 (already ours → return 0), `r4locked`
     (another DATA4 of this process → if `doUnlock==0` or `unlockAuto != LOCK4ALL` → return
     `r4locked`/E81523), or 0 (208-253).
  3. If `doUnlock`: perform auto-unlock according to `code4unlockAuto`: `LOCK4ALL` →
     `code4unlockDo(all open tables)`; `LOCK4DATA` → unlock this DATA4’s index locks, records and
     append lock (266-291).
  4. Physically lock via `dfile4lock` (§3.3.1) (303-308). `lock4read` requests place no OS lock,
     they only bump `recordLockReadCount` (309-313).
  5. On success add a `Lock4{data, rec, lockType}` to `data->lockedRecords` (315-345).

* **`d4lockFile(data)`** = `d4lockFileInternal(data, 1, lock4write)` (d4lock.c:820-823, 825-1045):
  tests for conflicts, auto-unlocks per `unlockAuto`, then `dfile4lockFile` (§3.3.3).
  After a successful FOX write-lock the auto-increment value is refreshed from disk
  (d4lock.c:1026-1040).

* **`d4lockAll(data)`** = `d4lockAllInternal` (d4lock.c:522-613): `d4lockFileInternal` +
  `dfile4lockMemo` (if memo fields) + `d4lockIndex`; on any failure performs auto-unlock per
  `unlockAuto` and returns the failure.

* **`d4lockAppend(data)`** = `d4lockAppendInternal(data, 1)` (d4lock.c:620-815): tests file lock
  then append lock (returns 1→0 if already ours), auto-unlocks per `unlockAuto` (records/index
  only — deliberately not the append/memo in append case, comment at 748), then `dfile4lockAppend`
  (§3.3.2); refreshes autoincrement value (779-810).

* **`d4lockTest(data, rec [,lockType])`** — see §3.2 semantics (d4lock.c:1119-1188).
  **`d4lockTestFile(data)`**: 1/us, `r4locked`/other, 0/none (d4declar.h:736 maps it to
  `dfile4lockTestFile(..., lock4write)`); **`d4lockTestAppend(data)`** returns 1 if we hold file
  lock or append lock, else 0 (d4lock.c:1221-1258).

* **`d4unlock(data)`** = `d4unlockLow(data, lockId, doReqdUpdate=1)` (d4unlock.c:326-349):
  flushes the table (`d4update`), then unlocks data + memo + index:
  `d4unlockData` = `d4unlockFile` + `d4unlockAppendInternal` + `d4unlockRecords`
  (d4unlock.c:445-468), then `dfile4memoUnlock` and `dfile4unlockIndex` (d4unlock.c:276-282).
  **Transaction guard:** if transactions are enabled and a transaction is `r4active` and the file
  is writable and not temporary, `d4unlockDo` **returns 0 without unlocking anything**
  (d4unlock.c:208-215). `code4unlock(c4)` (unlock everything in the CODE4) errors with
  `e4transViolation` while a transaction is active (d4unlock.c:763-786).
  `d4unlockRecord(data, rec)` / `d4unlockRecords(data)` unlock single/all record locks — only
  `lock4write` entries are physically unlocked; read entries just decrement
  `recordLockReadCount` (d4unlock.c:541-671). Note: `d4unlockData/File/Record(s)` are no-ops when
  `unlockAuto == LOCK4OFF` (d4unlock.c:452-453, 492-493, 552-553, 616-617), but
  `d4unlockAppendInternal` deliberately ignores `unlockAuto` (d4unlock.c:409-411).

### 3.5 Which API calls lock implicitly, and what they leave locked

* **`d4write(data, rec)`** is the macro `d4writeLow(data, rec, unlock=0, doLock=1)`
  (d4declar.h:754). It **locks the record** before writing (D4WRITE.C:293-298) and — because
  `unlock == 0` — **leaves it locked**. Internal flush paths call `d4writeLow` with `unlock=1`;
  then after the write, if `unlockAuto != 0` and the whole file is not locked and **no transaction
  is active** (`d4transEnabled` → keep locks, D4WRITE.C:608-612), the DATA4’s locks are released
  (D4WRITE.C:602-623).
* **`d4append(data)`** (D4APPEND.C:1360-1460): via `d4lockAppendRecord(data,1)` it locks the
  **append byte** and the **record `recCount+1`** (D4APPEND.C:92-193, record lock at 155-179).
  After the append: if a *mini-transaction* was started for the append (see §4.9) and
  `unlockAuto != LOCK4OFF`, the **append byte and index locks are unlocked** but the **new record
  stays locked** (D4APPEND.C:1420-1445: `d4unlockAppendInternal` + `dfile4unlockIndex`).
  If a user transaction is active (no mini-transaction), **nothing is unlocked** — the append byte
  and record remain locked until commit/rollback/unlock. On failure the provisional record lock is
  released (D4APPEND.C:1448-1455). So the folklore claim “d4append locks but does not unlock” is
  **true for the appended record always, and true for the append byte except in the
  mini-transaction case**.
* **`d4go(data, rec)`** locks the record before reading when `readLock` is set, performing
  `unlockAuto` processing first (d4go.c:254-283).
* **`d4pack` / `d4zap` / `d4reindex`** internally require full locks: `d4lockAllInternal(d4,1)`
  (d4pack.c:320; d4zap.c:116; r4reindx.c:74) — and `d4reindex` additionally `d4lockIndex`
  (r4reindx.c:174).
* **Memo writes** take the FPT byte lock transparently (m4file.c:512, 837) and it is released by
  `d4unlock`/auto-unlock (d4unlock.c:276-278).
* **Rollback internals** relock records they must restore, with `doUnlock=0`
  (c4trans.c:1952-1955).

### 3.6 Group locks and deadlock avoidance

`d4lockAdd(data, rec)`, `d4lockAddAppend(data)`, `d4lockAddFile(data)`, `d4lockAddAll(data)` queue
`LOCK4GROUP { data, id:{type, recNum, serverId, lockId} }` entries on `trans->locks`
(d4lock.c:355-520; struct at d4data.h:4325-4338). `code4lock(c4)` then attempts to acquire the
whole set atomically (c4trans.c:3854-4030, stand-alone branch 3962-4028):

1. If `unlockAuto == LOCK4ALL`, unlock everything first (3963-3969); temporarily set
   `unlockAuto = 0` (3972).
2. Iterate the pending list; each entry is tried once per pass (`lock4groupLock`).
   On `r4locked` the pass continues with the *same* entry next pass; when a full pass completes
   `count++`, and if `count >= lockAttemptsSingle` (and it isn’t `WAIT4EVER`) **all locks acquired
   so far are released** (`tran4unlock`) and `r4locked` is returned (3986-4000) — this
   all-or-nothing back-off is CodeBase’s deadlock-avoidance for multi-object locks.
   With `lockAttemptsSingle == 1` a single failure aborts immediately (4008-4010).
3. On success entries move to the acquired list and are freed; returns 0 (3980-3986).

Single-lock deadlock avoidance = the E81523 rule (§3.4) plus the retry/delay loop (§3.1). There is
no lock-ordering or wait-for-graph mechanism.

### 3.7 Cache coherency tied to locking

After acquiring a file/index lock the engine calls `file4refresh` (drop optimization caches;
df4lock.c:658-660, I4LOCK.C:401-403) and `i4versionCheck` (I4LOCK.C:416-418) so that data cached
while unlocked is re-read. Unlocking flushes: `tfile4update`/`index4update` before releasing index
locks (I4LOCK.C:477-480), header update before releasing append/file locks (df4unlok.c:70-83,
140-147). The C# port must preserve this order: **flush before unlock, invalidate after lock**.

---

## 4. TRANSACTIONS

### 4.1 API (stand-alone)

Real names (c4trans.H:69-73, 111-112; stand-alone implementations c4trans.c:2931-2978):

```c
int code4tranStart   (CODE4 *)          // c4trans.c:2931 → tran4lowStart(&c4->c4trans.trans, 0, 0)
int code4tranCommit  (CODE4 *)          // c4trans.c:2794 → phaseOne(dualPhaseCommit) + phaseTwo(doUnlock=1)
int code4tranCommitPhaseOne(CODE4*, CommitPhaseType) / PhaseTwo(CODE4*, int doUnlock)
int code4tranRollback(CODE4 *)          // c4trans.c:2957 → tran4lowRollback(...,0,1) + code4unlock
int code4tranStatus  (CODE4 *)          // macro → c4->c4trans.trans.currentTranStatus (c4trans.H:85)
int code4logOpen  (CODE4*, const char *fileName, const char *userId) ;   // (d4declar.h:427)
int code4logCreate(CODE4*, const char *fileName, const char *userId) ;   // (d4declar.h:425)
void code4logOpenOff(CODE4*) ;                                           // (d4declar.h:428)
```

Example usage confirming the API (examples/source/C/TRANSFER.C:41-93): `code4logOpen(&cb,0,"user1")`,
fallback `code4logCreate`, `code4tranStart`, `code4tranCommit`, `code4tranRollback`.
There is **no** `d4tranBegin` API.

Statuses: `currentTranStatus` ∈ { `r4inactive`, `r4active`, `r4partial`, `r4rollback`, `r4off` }.
`code4transEnabled(c4)` = `c4trans.enabled && status != r4rollback && status != r4off`
(D4INLINE.H:84).

`code4logOpen`/`code4logCreate` bodies are not present in this source tree (only
`S4OFF_STAND_WRITE_TRAN` stubs at e4not_s.c:1953-1969). From call sites their behavior is:
enable the log file via `code4transFileEnable(&c4->c4trans, fileName, doCreate)`
(c4trans.c:3425-3562: resolves name from `c4->transFileName` if `fileName==0`, opens or creates,
sets `c4trans.enabled=1`, `status=tran4notRollbackOrCommit`) and register the user via
`tran4addUser(trans, 0, userId, len)` (c4trans.c:3614-3752). **[Exact body UNVERIFIED — missing
translation unit; reconstruct from these two functions.]** `d4open`/`d4create` auto-call
`code4logOpen(c4,0,0)` when `c4->logOpen` is set (D4OPEN.C:83-92; D4CREATE.C:183). If a transaction
is started while not enabled and `c4->logOpen == 0` → error E83814 (c4trans.c:2079-2087).

### 4.2 Logging levels: LOG4* (d4defs.h:1740-1745)

| Constant | Value | Meaning (per-DATA4 `logVal`, seeded from `c4->log` at open, D4OPEN.C:872) |
|---|---|---|
| `LOG4TRANS` | 0 | log only while an explicit transaction is active (default) |
| `LOG4ON` | 1 | log all writes/appends (mini-transactions wrap standalone updates) |
| `LOG4ALWAYS` | 2 | like ON, cannot be turned off by `d4log()` (d4data.c:463-464) |
| `LOG4OFF` | 3 | never log this file (system/schema files) |
| `LOG4BACK_ONLY` | 4, `LOG4COMPRESS` 5 | server-only backup-log modes (d4defs.h:1740-1741) |

`d4log(data, onOff)` toggles between `LOG4ON` and `LOG4TRANS` (d4data.c:440-480).
`d4transEnabled(data, checkActive)` = `logVal != LOG4OFF && code4transEnabled(c4) &&
(!checkActive || status == r4active)` (D4APPEND.C:924-948).
`d4startMiniTransactionIfRequired`: if `logVal ∉ {LOG4TRANS, LOG4OFF}` and no transaction is
active, `code4tranStartSingle()` is called and 1 returned (D4APPEND.C:954-995).
`tran4active(c4, data)` blocks unlogged writes to shared files while a transaction could exist
elsewhere: in stand-alone it errors `e4transViolation` unless `logVal == LOG4TRANS`, or the file is
file-locked by us, or not opened DENY_NONE (c4trans.c:4624-4655).

### 4.3 Log file — physical format

File name: the given name with extension forced to `.log` (`u4nameExt(buf,…, "log", 0)`,
c4trans.c:575-576, 855-856). Default name when `code4logOpen(c4,0,0)`: taken from
`c4->transFileName` (settable via `c4setTransFileName`, c4set.c:38-52); ultimate default
**[UNVERIFIED — set in missing init code; Sequiter docs: derived from the executable name]**.
Open share mode: `OPEN4DENY_RW` (exclusive) when `transShared == 0` (default), `OPEN4DENY_NONE`
when the log is shared (`transShared == 1`) (c4trans.c:3453-3459, 858-878).

The file is a pure append-only sequence of entries — there is **no file header**; the first entry
is a `TRAN4SHUTDOWN` entry written at creation (c4trans.c:598-616).

#### 4.3.1 Entry layout (c4trans.c:32-52 “Every entry…”, tran4fileLowAppend c4trans.c:232-353)

```
+0                4 bytes  entryLen  (uint32 LE)  = 4 + dataLen + 32   // includes itself
+4                dataLen  transactionData (payload, type-specific)
+4+dataLen        32 bytes LOG4HEADER
```

`TRAN4ENTRY_LEN` = `unsigned S4LONG` = 4 bytes (c4trans.H:17-19);
`tran4entryLen(h) = sizeof(LOG4HEADER) + h->dataLen + sizeof(TRAN4ENTRY_LEN)` (D4INLINE.H:106).
The header is written **after** the payload so the file can be scanned backwards (header read at
`pos`, payload at `pos − dataLen`, tran4fileLowRead c4trans.c:1158-1204; backward skip
`pos −= entryLen` c4trans.c:1355-1364; forward skip via the leading length field
c4trans.c:1373-1394). The status check at open verifies the first and last entries have
`entryLen == 36` (`sizeof(LOG4HEADER)+sizeof(TRAN4ENTRY_LEN)`) (c4trans.c:751-757, 778-785).

#### 4.3.2 `LOG4HEADER` — 32 bytes, packed(1), little-endian (d4data.h:1143-1162; LOG4TIME d4data.h:1127-1141)

| Offset | Len | Type | Field | Contents |
|---|---|---|---|---|
| 0 | 4 | int32 | `transId` | transaction id; 0 for entries outside a transaction (tran4set: c4trans.c:1567-1594) |
| 4 | 4 | int32 | `clientId` | stand-alone non-shared: the `id2` argument (0 for user transactions); shared log: this process’s `userIdNo` (c4trans.c:1601-1625) |
| 8 | 4 | int32 | `clientDataId` | `data4clientId(data)` — per-CODE4 table open counter (c4trans.c:1635-1637; D4OPEN.C:823) |
| 12 | 4 | int32 | `serverDataId` | `data4serverId(data)` (== clientDataId in stand-alone); for `TRAN4SHUTDOWN` holds the log **version number** `TRAN4VERSION_NUM = 3` (c4trans.c:603-612; c4trans.H:182) |
| 16 | 2 | int16 | `type` | entry type, see §4.3.3 (c4trans.c:1627) |
| 18 | 4 | uint32 | `dataLen` | payload length (c4trans.c:1633) |
| 22 | 4 | int32 | `time.year` | local calendar year (tran4getTime, c4trans.c:163-190) |
| 26 | 1 | int8 | `time.month` | 1–12 |
| 27 | 1 | int8 | `time.day` | 1–31 |
| 28 | 4 | uint32 | `time.seconds` | seconds past midnight, `((h*60)+m)*60+s` |

The time is captured **once per transaction event** (`tran4getTime` called at start/commit/
rollback, c4trans.c:2135-2137, 2278-2281, 2632-2635) and copied into every header
(c4trans.c:1640).

> The S4WINCE variant of these structs replaces `long` with `char[4]` because CE would otherwise
> pad them; the comment confirms the on-disk intent is 1-byte alignment (d4data.h:1122-1126).

#### 4.3.3 Entry types (c4trans.H:145-169)

| Name | Value | Payload (`transactionData`) |
|---|---|---|
| `TRAN4OPEN` | 1 | file open notification [payload written by d4open path — alias/name; not needed for rollback (ignored, c4trans.c:2257-2260)] |
| `TRAN4OPEN_TEMP` | 2 | as above for temp files |
| `TRAN4CLOSE` | 3 | file close notification |
| `TRAN4START` | 4 | none (`dataLen = 0`) (c4trans.c:2137-2142) |
| `TRAN4COMMIT_PHASE_ONE` | 5 | none (c4trans.c:2635-2647) |
| `TRAN4COMMIT_PHASE_TWO` | 6 | none (c4trans.c:2674-2679) |
| `TRAN4ROLLBACK` | 7 | none; `transId` = id of the rolled-back transaction (c4trans.c:2278-2284) |
| `TRAN4WRITE` | 8 | see §4.3.4 |
| `TRAN4APPEND` | 9 | see §4.3.5 |
| `TRAN4VOID` | 10 | 1 zero byte; voids the immediately preceding entry of this transaction (failed append/write), skipped on rollback (D4APPEND.C:1107-1121; c4trans.c:2257) |
| `TRAN4CREATE` | 11 | file create marker (ignored on rollback, c4trans.c:2261-2263) |
| `TRAN4PACK` | 12 / `TRAN4ZAP` | 13 | pack/zap markers (rollback of these is an error E83809, c4trans.c:2265-2266) |
| `TRAN4INIT` | 15 | user registration: `[netIdLen:int16][netId bytes][userIdLen:int16][userId bytes]`; empty userId is stored as `"PUBLIC"`; max lengths `LEN4TRANS_USERID=10`, `LEN4TRANS_NETID=20` (c4trans.c:3720-3746; d4defs.h:1719-1720) |
| `TRAN4SHUTDOWN` | 16 | clean-shutdown marker; header zeroed except `type` and `serverDataId = TRAN4VERSION_NUM(3)`; `dataLen = 0` (c4trans.c:4171-4188) |
| `TRAN4BACKEDUP` | 17 | log has been backed up — file may not be reused (open fails E83815, c4trans.c:798-802) |
| `TRAN4INIT_UNDO` | 18, `TRAN4CLOSE_BACKUP` 19, `TRAN4DIRECTORY` 20, `TRAN4CREATE_ENCRYPT` 21 | server/backup bookkeeping |

#### 4.3.4 `TRAN4WRITE` payload (D4WRITE.C:355-515)

```
[recNo      : int32]                                   // D4WRITE.C:363-373
[oldRecord  : recWidth bytes]  (read from disk first)  // D4WRITE.C:381-390
[newRecord  : recWidth bytes]                          // D4WRITE.C:392
for each memo field i in 0..nFieldsMemo-1 (in order):  // D4WRITE.C:405-490
    if field changed:
        [oldMemoLen : uint32][oldMemo bytes]           // 0 length if no old entry
        [newMemoLen : uint32][newMemo bytes]
    else:
        [0 : uint32][0 : uint32]
```

`dataLen` is initially `4 + 2×recWidth` in `tran4set` and grown as memo data is added
(D4WRITE.C:364-365, 500-509). `clientDataId`/`serverDataId` identify the table.

#### 4.3.5 `TRAN4APPEND` payload (D4APPEND.C:1013-1070)

```
[recNo   : int32]                       // the appended record number
[record  : recWidth bytes]
for each memo field i:                  // always all memo fields
    [memoLen : uint32][memo bytes]
```

#### 4.3.6 Transaction-id generation (c4trans.c:970-1131)

Non-shared log (default): `TRAN4FILE.transId` is a counter; if uninitialized (< 0), seek to the
bottom of the log, scan backwards to the last entry with `transId != 0`, and continue from
`that + 1` (wrap at `LONG_MAX` → restart at 1) (c4trans.c:986-1023).
Shared log: ids are `userIdNo + k × TRAN4MAX_USERS` (`TRAN4MAX_USERS = 1000`, c4trans.H:189) so
each of ≤1000 concurrent users allocates ids in a disjoint residue class without scanning
(c4trans.c:1025-1130).

### 4.4 Log-file locking (shared log only) and user registration

Constants (c4trans.H:174-189): `TRAN4LOCK_BASE = 1000000000`; `TRAN4LOCK_SERVER` +0;
`TRAN4LOCK_MULTIPLE` +1; `TRAN4LOCK_BACKUP` +2; `TRAN4LOCK_RESTORE` +3; `TRAN4LOCK_FIX` +4;
`TRAN4LOCK_USERS = TRAN4LOCK_BASE + 1000`; `TRAN4MAX_USERS = 1000`.

* Every writer must hold byte `TRAN4LOCK_MULTIPLE` (=1 000 000 001) of the `.log` while appending;
  `tran4fileLowAppend` locks it (WAIT4EVER) if not already held and unlocks after
  (c4trans.c:281-306, 419-425; code4tranLockTransactions c4trans.c:4357-4431 — held-locks tracked
  in the `fileLocks` bitmask, bit `i` == byte `TRAN4LOCK_BASE+i`, d4data.h:1302).
* Default build (no `S4TRANS_FULL_LOCK_OFF`): `code4tranStart` acquires `TRAN4LOCK_MULTIPLE` for
  the **whole duration** of the transaction and commit/rollback release it
  (c4trans.c:2114-2131, 2292-2296, 2698-2702; strategy comment c4trans.c:60-79).
* User-id registration: each process locks the first free byte in
  `[TRAN4LOCK_USERS+1 … TRAN4LOCK_USERS+TRAN4MAX_USERS]` with one attempt each; index+1 becomes its
  `userIdNo`; the lock is held until shutdown (tran4addUser, c4trans.c:3676-3706).
* Integrity check at open (`tran4fileLowStatusFile`, c4trans.c:668-807): serialize on byte
  `TRAN4LOCK_USERS + TRAN4MAX_USERS + 1` (WAIT4EVER), then try to lock each user byte once —
  if any is held, another process is using the log, so it is assumed valid; if all are free, the
  **last entry must be `TRAN4SHUTDOWN`** (and first entry sane), otherwise open fails with
  E83801 “invalid transaction file - run recovery utility”.
* Clean shutdown (`code4transInitUndo`, c4trans.c:4216-4319): release own user byte; if
  `file4lockInternal(file, TRAN4LOCK_USERS, 0, TRAN4MAX_USERS, 0)` succeeds (last user), append the
  final `TRAN4SHUTDOWN` entry (`code4transInitUndoMarkShutdown`, c4trans.c:4171-4188) — only when
  `currentTranStatus == r4inactive` (c4trans.c:4253-4258). With a non-shared (exclusive) log the
  shutdown marker is always appended (c4trans.c:4300-4302).

### 4.5 Write-ahead ordering and flushing

For every logged operation the log entry is appended **before** any index or data write:
`d4writeLow` → `tran4lowAppend` (D4WRITE.C:513-515) precedes `d4writeKeys` (D4WRITE.C:529-531) and
the data write; `d4appendLow` → `d4appendRegisterTransaction` (D4APPEND.C:1348) precedes
`d4doAppend` (D4APPEND.C:1350-1352). The append passes `doImmediateFlushing = 1` unless buffered
batch writes are on (D4WRITE.C:513-515; D4APPEND.C:1073-1075); flushing calls `file4flush` on the
log file handle; in optimized builds a `needsFlushing` flag defers the flush for non-write entry
types (c4trans.c:352-425). Commit phase one flushes the data files (`tran4lowUpdate` →
`dfile4updateHeader` + `d4flushData` per changed table, c4trans.c:2560-2583) **before** appending
`TRAN4COMMIT_PHASE_ONE`, which is itself flushed when `phaseType == singlePhaseCommit`
(c4trans.c:2646-2647).

### 4.6 `code4tranStart` → `tran4lowStart(trans, clientId=0, doUnlock=0)` (c4trans.c:2064-2152)

1. If `c4trans.enabled == 0`: if `c4->logOpen == 0` → error E83814; else `code4logOpen(c4,0,0)`
   (2079-2087).
2. If already `r4active` → error E93801 (2096-2099); if the log is mid rollback/commit
   (`transFile->status != tran4notRollbackOrCommit`) → error E83801 (2101-2102).
3. (shared log) acquire `TRAN4LOCK_MULTIPLE` WAIT4EVER (2114-2131).
4. `tran4getTime`; append `TRAN4START` (new transId, dataLen 0); status ← `r4active`
   (2135-2144).
5. `savedUnlockAuto ← unlockAuto; unlockAuto ← 0` — **automatic unlocking is disabled for the
   duration of the transaction** (2146-2149). It is restored in commit phase two
   (c4trans.c:2687-2689) and rollback (c4trans.c:2290-2292).

### 4.7 Commit (c4trans.c:2587-2718; wrapper 2794-2809)

`code4tranCommit` = phase one (`dualPhaseCommit`) + phase two (`doUnlock=1`) (c4trans.c:2794-2809).

**Phase one** (`tran4lowCommitPhaseOne`, c4trans.c:2587-2650): if status ≠ `r4active` → return 0;
wait until `transFile->status == tran4notRollbackOrCommit`, then set it to
`tran4rollbackOrCommit`; status ← `r4partial`; `tran4lowUpdate` flushes every table with
`transChanged` (updates DBF header + flushes data; failure reverts and aborts); append
`TRAN4COMMIT_PHASE_ONE`.

**Phase two** (`tran4lowCommitPhaseTwo`, c4trans.c:2657-2718): requires status `r4partial`; append
`TRAN4COMMIT_PHASE_TWO` (flushed); `tran4lowRemoveKeys` — physically removes index keys queued in
each tag’s `removedKeys` list (deleted-key removal is deferred during a transaction so a rollback
can restore them; locks the index if needed) (c4trans.c:2318-2515); status ← `r4inactive`,
`transId = 0`, `unlockAuto` restored; `tran4updateData` updates headers of changed files
(c4trans.c:2524-2554); if `doUnlock` and `unlockAuto == 1` → `code4unlock(c4)`; (shared log)
release `TRAN4LOCK_MULTIPLE`; `transFile->status ← tran4notRollbackOrCommit`; close tables whose
`d4close` was deferred during the transaction (`tran4lowCloseDelayed`, c4trans.c:83-104 — closes
issued inside a transaction are queued on `closedDataFiles` and executed here).

### 4.8 Rollback (stand-alone `code4tranRollback` c4trans.c:2957-2978 → `tran4lowRollback` c4trans.c:2198-2316)

1. Preconditions: enabled; status == `r4active` else E83808; `transFile->status` free else E83801;
   set `transFile->status ← tran4rollbackOrCommit`, status ← `r4rollback` (2222-2229).
2. `tran4bottom(trans)` — position at the last log entry (2232).
3. Scan **backwards** (`tran4skip(TRAN4BACKWARDS)`); for every entry whose `transId` equals the
   current transaction id (2237-2272):
   * `TRAN4START` → done;
   * `TRAN4WRITE` → `tran4lowUnwrite` (see below);
   * `TRAN4APPEND` → `tran4lowUnappend`;
   * `TRAN4VOID`, `TRAN4OPEN`, `TRAN4OPEN_TEMP`, `TRAN4CLOSE`, `TRAN4CREATE(_ENCRYPT)` → skip;
   * anything else → error `e4rollback` E83809.
4. Append a `TRAN4ROLLBACK` entry (transId = rolled-back id, flushed) (2277-2284).
5. status ← `r4inactive`; `transId = 0`; restore `unlockAuto`; (shared) release
   `TRAN4LOCK_MULTIPLE`; `transFile->status ← tran4notRollbackOrCommit`; run deferred closes;
   `code4invalidate(c4)` discards in-memory record buffers (2286-2314).
6. `code4tranRollback` finally calls `code4unlock(c4)` — all locks are released after rollback
   (c4trans.c:2972-2977).

**`tran4lowUnwrite`** (c4trans.c:1848-2062): payload gives `recNo`, old record, new record, memo
pairs. Find the DATA4 via `serverDataId/clientDataId` (including tables closed during the
transaction; missing table → ignore). If the user’s current buffer holds unflushed changes to that
record, preserve them around the operation (1895-1915). `d4go(recNo)`; ensure the record is locked
(`d4lockTest == 1` else `d4lockInternal(..., doUnlock=0)`; failure → E83804 and the old/new record
images are dumped to `OLD_RECORD.FIL`/`NEW_RECORD.FIL`) (1952-2000). Copy the **old record** over
the buffer; for each memo field re-assign the old memo contents (`f4memoAssignN`) using the logged
old/new lengths (2003-2049); `d4writeLow(data, recNo, 0, 0)` (no lock/unlock) and `d4update`
(2051-2060).

**`tran4lowUnappend`** (c4trans.c:1749-1845): payload gives `recNo` + record image. Requires the
append lock to still be held (`d4lockTestAppend == 1`, else E83804 with diagnostic record dumps)
(1785-1830). If `recCount == recNo−1` it was already unappended → 0; if `recCount != recNo` →
E83805 corruption (1831-1837). Restore the record image to the buffer, `d4unappend(data)`
(truncates the file by one record) and `d4update` (1839-1844).

### 4.9 Mini-transactions (LOG4ON files without a user transaction)

`d4append`/`d4write` on a logged file with no active transaction wrap themselves in
`code4tranStartSingle()` … `code4tranCommitSingle()` (start: D4APPEND.C:954-995 via
`d4startMiniTransactionIfRequired`; commit: D4APPEND.C:1420-1427; `code4tranCommitSingle` =
phase one + phase two with `doUnlock = 0`, c4trans.c:2812-2840). On failure the mini-transaction
is rolled back (`code4tranRollbackSingle` in stand-alone = `code4tranRollback` path via
D4WRITE/D4APPEND error branches, e.g. D4WRITE.C:341-344; D4APPEND.C:1093-1101) and a `TRAN4VOID`
entry is appended to neutralize the already-written entry (D4APPEND.C:1104-1121).

### 4.10 Crash recovery

At `code4logOpen` time the status check (§4.4) detects a log that was not cleanly shut down and
**fails the open with E83801 “invalid transaction file - run recovery utility”**
(c4trans.c:793-807). The engine itself performs no automatic replay; recovery (scanning the log
and undoing entries of transactions that lack `TRAN4COMMIT_PHASE_TWO`/`TRAN4ROLLBACK`) is done by
Sequiter’s external utilities compiled with `S4UTILS` (log examination API: `tran4fileTop/Bottom/
Skip/Read`, exported at c4trans.H:206-228). The C# port must implement: (a) the same clean/dirty
detection, and (b) a recovery routine equivalent to the backward scan of §4.8 applied to every
unfinished transaction found in the log. **[The utility’s exact algorithm is not in this tree —
UNVERIFIED; the rollback algorithm of §4.8 is the verified template.]**

### 4.11 Interaction between transactions and locks — summary of verified rules

* `tran4lowStart` zeroes `unlockAuto` for the transaction’s duration (c4trans.c:2146-2149).
* `d4unlock` during an active transaction on a writable, non-temporary file silently does nothing
  (d4unlock.c:208-215); `code4unlock` errors `e4transViolation` (d4unlock.c:768-777).
* Commit phase two optionally unlocks everything (`doUnlock && unlockAuto == 1`,
  c4trans.c:2691-2696); `code4tranRollback` always unlocks (c4trans.c:2972-2977).
* Rollback requires the touched records/append bytes to still be locked, and errors E83804
  otherwise (c4trans.c:1785-1830, 1952-2000) — a direct consequence of the previous rules.
* Locks acquired inside the transaction are kept (no auto-unlock in `d4write`, D4WRITE.C:608-612).

---

## 5. Mapping to .NET

### 5.1 File opening (share modes)

| CodeBase | .NET `FileStream` |
|---|---|
| `OPEN4DENY_NONE` (shared) | `FileShare.ReadWrite` |
| `OPEN4DENY_WRITE` | `FileShare.Read` |
| `OPEN4DENY_RW` (exclusive) | `FileShare.None` |

Open the DBF/CDX/FPT with `FileAccess.ReadWrite` and the share mode above. When exclusive,
suppress all byte-range locking exactly like the C code does (f4lock.c:500-502).

### 5.2 Byte-range locks

`FileStream.Lock(long position, long length)` / `FileStream.Unlock(long position, long length)`
map 1:1 to `LockFile`/`UnlockFile` with 64-bit position/length — **all VFP offsets in §3.3 are
< 2^31 and exactly reproducible**, so a C# process will interoperate with live VFP/CodeBase C
applications on the same tables. Requirements/caveats:

* `Lock`/`Unlock` throw `IOException` on conflict — map “lock violation” (`HResult` 0x80070021,
  `ERROR_LOCK_VIOLATION = 33`) to `r4locked` and re-implement the retry loop of §3.1
  (attempts = `lockAttempts`, delay = `lockDelay` × 10 ms, `-1` = infinite).
* Unlock must use the **identical** (position, length) pair as the lock (Windows mandatory-lock
  semantics; the C code relies on this, e.g. file-lock range lock df4lock.c:620-621 / unlock
  df4unlok.c:158-161).
* These are **mandatory** locks on Windows: a locked byte range also blocks plain `Read`/`Write`
  of other processes that overlap it. That is harmless here because all lock bytes
  (≥ `0x40000000`) lie far beyond the real EOF of any non-huge DBF — the same trick VFP uses.
  Consequently, DBF files approaching `0x40000000` bytes (1 GB) begin to collide with the
  old-style record-lock region; VFP has the same limitation (2 GB file cap).
* `FileStream.Lock/Unlock` are Windows-only in modern .NET (`PlatformNotSupportedException` on
  Unix, where advisory `fcntl` locks would be needed — the C code’s S4UNIX branch uses `lockf`,
  f4lock.c:388-398). For a Windows-first port, P/Invoke is not required; on other platforms use
  `System.IO.File` handle + platform locks behind an abstraction.
* .NET provides no async byte-range lock; the retry loop should use `Thread.Sleep`/`Task.Delay`
  consistent with `u4delayHundredth`.

### 5.3 Concrete lock table for the C# implementation (S4FOX, `largeFileOffset == 0`)

| Object | File | Position | Length |
|---|---|---|---|
| Record `n` (VFP-style table) | .dbf | `0x7FFFFFFE − n` | 1 |
| Record `n` (FP 2.x table) | .dbf | `0x40000000 + headerLen + (n−1)×recWidth` | 1 |
| Append (VFP-style) | .dbf | `0x7FFFFFFE` | 1 |
| Append (FP 2.x) | .dbf | `0x40000000` | 1 |
| Whole table | .dbf | `0x40000000` | `0x3FFFFFFF` |
| Index | .cdx | `0x7FFFFFFE` | 1 |
| Memo | .fpt | `0x40000000` | 1 |
| Log: append serialization | .log | `1000000001` | 1 |
| Log: user id `i` (1-based) | .log | `1000001000 + i` | 1 |
| Log: user-area guard | .log | `1000002001` | 1 |
| Log: shutdown probe | .log | `1000001000` | 1000 |

“VFP-style” ⇔ DBF header byte 0 == 0x30/0x31 or header byte 28 has bit 0 (production CDX) set.

### 5.4 Transactions in C#

* Log writes must go through a single append routine reproducing §4.3.1 byte layout
  (`BinaryWriter` little-endian; header serialized with the packed 32-byte layout of §4.3.2).
* `Flush(flushToDisk: true)` (i.e. `FlushFileBuffers`) where the C code calls `file4flush` with
  `doImmediateFlushing` — before returning from a logged write and at commit/rollback markers.
* Keep the write-ahead invariant of §4.5 (log entry durable before touching DBF/CDX/FPT).
* The in-memory lock registry (§3.2) is required for correct semantics (self-conflict detection,
  `unlockAuto`, E81523) — OS locks alone are not sufficient because a second `Lock` of the same
  region **by the same process** would succeed/fail differently than CodeBase’s logic expects.

---

## 6. Open questions / risks for the C# port

1. **Missing translation units.** `code4initLow` (CODE4 defaults) and the stand-alone bodies of
   `code4logOpen`/`code4logCreate`/`code4logOpenOff`/`code4logFileName` are not in this source
   drop (only stubs in e4not_s.c:1953-1985). Defaults for `lockAttempts`, `lockDelay`,
   `unlockAuto`, `readLock`, and the default log-file name must be taken from Sequiter
   documentation or reverse-engineered from a binary. Recommended: `lockAttempts=1`,
   `lockDelay=0`, `unlockAuto=1`, `readLock=0`, log name = `<exe>.log` **[UNVERIFIED]**.
2. **Crash-recovery utility.** The engine only detects a dirty log (E83801); the recovery replay
   lives in the S4UTILS tools, not present here. The port needs its own recovery pass (§4.10).
3. **LOG4HEADER portability.** The 32-byte packed layout (§4.3.2) is what the Win32 build writes;
   log files are not interchangeable with unpacked/aligned builds (see the S4WINCE comment,
   d4data.h:1122-1126). Fix the C# serializer to the packed layout and never rely on
   `Marshal.SizeOf` of an unpacked struct.
4. **1 GB DBF collision.** Old-style record locks live at `0x40000000 + recordPosition`; a DBF
   whose data area exceeds `0x3FFFFFFE` bytes overlaps the lock region. Decide whether to cap
   file size (VFP-compatible behavior) or force `largeFileOffset` mode (breaks interop).
5. **`largeFileOffset` mode** (§3.3.5) is CodeBase-proprietary. Supporting it means C#-to-C#/C
   interop only; document it as incompatible with VFP.
6. **Read locks are process-local.** `lock4read` entries never hit the OS (df4lock.c:309-313 /
   d4lock.c:309-313); true shared read locks exist only for exclusively opened files. The port
   must not try to map them to `FileShare` or OS shared locks.
7. **Mandatory-lock semantics on non-Windows.** On Unix, advisory locks don’t block reads/writes;
   coherence guarantees that CodeBase gets for free from Windows mandatory locks (other process’s
   buffered reads can’t see half-written records they have locked) would need review.
8. **`transShared` mode** (shared .log, per-user id locks, residue-class transIds) adds a lot of
   complexity; VFP interop does not need it (VFP has its own transaction system that does not use
   CodeBase logs at all). Recommend shipping v1 with exclusive log (`transShared = 0`,
   `OPEN4DENY_RW`, c4trans.c:3453-3459) and deferring shared-log support.
9. **VFP’s own locking nuances**: VFP `SET MULTILOCKS`, `SET REPROCESS` and header locking during
   append differ in details from CodeBase’s append-byte approach (CodeBase locks `0x7FFFFFFE`
   rather than DBF header byte 0). The byte ranges here are what the CodeBase C engine uses and
   are the compatibility target; independent verification against a live VFP instance is
   recommended for the append path (VFP is documented to use the same `0x7FFFFFFE`-based scheme,
   but that claim is external to this source tree) **[external verification advised]**.
10. **Deferred index-key removal** (`removedKeys` processed only at commit phase two,
    c4trans.c:2318-2515) means the CDX temporarily contains keys for deleted/overwritten values
    during a transaction. The C# index engine must reproduce this to make rollback cheap and to
    match on-disk states other CodeBase processes may observe.
