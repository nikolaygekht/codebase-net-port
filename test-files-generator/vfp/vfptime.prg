*===========================================================================
*  vfptime.prg -- the VFP datetime-key oracle (step 013, sub-step 7a-7)
*
*  WHAT THIS IS FOR
*  ----------------
*  A datetime key is t4dblToFox(day + ms / 86400000.0), nudged down one unit
*  in its last byte wherever an 86400-bit table says so. CodeBase carries that
*  table as "an empirical patch" -- its own comment says FoxPro's conversion
*  "could not be deciphered" (i4conv.c:1509-1512) -- and the port copies it
*  verbatim. So far it has only been checked against keys CodeBase wrote.
*  This script asks Visual FoxPro itself (KEY-COLLATION.md section 9 item 1).
*
*  It writes one table, VFPTIME.DBF, one T field and one ascending tag on it:
*
*    every second of 2000-01-01            86400 records, the whole table
*    every 37th second, plus 23:59:59,     2337 records each, on six days in
*      of six more days                    all three binades of the Julian
*                                          day: below 2^21 (before ~1029),
*                                          inside, above 2^22 (after ~6771)
*
*  Adding a whole day to a fraction rounds it to the grid of the sum's
*  binade, so a table indexed by the second alone can only be wrong across
*  binades -- which is why the days are chosen by binade and not by taste.
*  37 is prime, so the sample walks across the table's bytes.
*
*  THE .DBF/.cdx ARE THE ORACLE. VFPTIME.vfp.dump.txt is provenance only:
*  the settings, the per-day counts and the tag. It carries no run date, so
*  a re-run that writes the same bytes writes the same dump.
*
*  HOW TO RUN
*  ----------
*      run-in-vm.ps1 -Script vfptime.prg      (from the host, unattended)
*  or  DO vfptime.prg                          (in the VM, -> .\out\)
*  then copy-vfp-corpus.bat.
*
*  Values are DTOT(DATE(y, m, d)) + seconds, so no SET changes what is
*  stored; the usual ones are pinned and recorded anyway (ADR-44). The DBF
*  header date stamp is frozen to 26-01-01 as in vfpfunc.prg (ADR-07).
*===========================================================================

LPARAMETERS tcOutDir

#DEFINE STRIDE  37
#DEFINE LASTSEC 86399

LOCAL lcOut, lcDbf, lcSelf, lni, lnS, ltDay, lnH, lnCount
LOCAL ARRAY laDay[7], laNote[7]

SET TALK OFF
SET SAFETY OFF
SET EXCLUSIVE ON
SET DELETED OFF
SET CPDIALOG OFF
CLOSE TABLES ALL
CLOSE DATABASES ALL

SET COLLATE TO "MACHINE"
SET CENTURY ON
SET CENTURY TO 19 ROLLOVER 29
SET DATE TO ANSI
SET HOURS TO 24
SET NULL OFF

lcSelf = SYS(16, 0)
IF EMPTY(m.lcSelf)
   lcSelf = SYS(16)
ENDIF
lcOut = IIF(EMPTY(m.tcOutDir), ADDBS(JUSTPATH(m.lcSelf)) + "out", m.tcOutDir)
lcOut = ADDBS(m.lcOut)
IF !DIRECTORY(m.lcOut)
   MD (m.lcOut)
ENDIF

? "VFP datetime oracle -> " + m.lcOut

*-- Day 1 is sampled whole; the rest every STRIDE seconds. The binade is the
*-- Julian day's power of two: 2^21 = 2097152 falls in 1029, 2^22 = 4194304
*-- in 6771.
laDay[1] = DATE(2000, 1, 1)
laNote[1] = "inside  -- every second"
laDay[2] = DATE(100, 1, 1)
laNote[2] = "below 2^21"
laDay[3] = DATE(1000, 1, 1)
laNote[3] = "below 2^21"
laDay[4] = DATE(1900, 1, 1)
laNote[4] = "inside"
laDay[5] = DATE(2026, 11, 14)
laNote[5] = "inside"
laDay[6] = DATE(7000, 1, 1)
laNote[6] = "above 2^22"
laDay[7] = DATE(9999, 12, 31)
laNote[7] = "above 2^22"

lcDbf = m.lcOut + "VFPTIME.DBF"
CREATE TABLE (m.lcDbf) FREE (TS T)

FOR lni = 1 TO 7
   ltDay = DTOT(laDay[m.lni])
   FOR lnS = 0 TO LASTSEC STEP IIF(m.lni = 1, 1, STRIDE)
      APPEND BLANK
      REPLACE TS WITH m.ltDay + m.lnS
   ENDFOR
   *-- The day's last second, unless the stride already landed on it.
   IF m.lni > 1 AND MOD(LASTSEC, STRIDE) <> 0
      APPEND BLANK
      REPLACE TS WITH m.ltDay + LASTSEC
   ENDIF
ENDFOR

INDEX ON TS TAG TS

*-- The dump: provenance, not the oracle.
lnH = FCREATE(m.lcOut + "VFPTIME.vfp.dump.txt")
IF m.lnH < 0
   ? "  ERROR: cannot create VFPTIME.vfp.dump.txt"
ELSE
   DO Put WITH m.lnH, "# CodeBase.NET corpus VFP datetime-key oracle"
   DO Put WITH m.lnH, "# Written by test-files-generator/vfp/vfptime.prg in Visual FoxPro 9."
   DO Put WITH m.lnH, "# Do not edit by hand; re-run the script in the VM instead."
   DO Put WITH m.lnH, "# VFPTIME.DBF and its .cdx are the oracle; this is provenance."
   DO Put WITH m.lnH, "table        VFPTIME.DBF"
   DO Put WITH m.lnH, "producer     " + '"' + VERSION(1) + '"'
   DO Put WITH m.lnH, "codePage     " + TRANSFORM(CPCURRENT())
   DO Put WITH m.lnH, "collate      " + SET("COLLATE")
   DO Put WITH m.lnH, "century      " + SET("CENTURY")
   DO Put WITH m.lnH, "rollover     " + TRANSFORM(SET("CENTURY", 2))
   DO Put WITH m.lnH, "dateFormat   " + SET("DATE")
   DO Put WITH m.lnH, "hours        " + TRANSFORM(SET("HOURS"))
   DO Put WITH m.lnH, "null         " + SET("NULL")
   DO Put WITH m.lnH, "stride       " + TRANSFORM(STRIDE)
   DO Put WITH m.lnH, "numRecs      " + TRANSFORM(RECCOUNT())
   DO Put WITH m.lnH, ""

   DO Put WITH m.lnH, "[days]"
   SET ORDER TO 0
   FOR lni = 1 TO 7
      COUNT FOR TTOD(TS) = laDay[m.lni] TO lnCount
      DO Put WITH m.lnH, "  " + DTOS(laDay[m.lni]) + "  " + PADL(TRANSFORM(m.lnCount), 5) + ;
         "  ; " + laNote[m.lni]
   ENDFOR
   DO Put WITH m.lnH, ""

   DO Put WITH m.lnH, "[tags]"
   DO Put WITH m.lnH, "# keyLen is in the .cdx tag header, which is the authority."
   FOR lni = 1 TO TAGCOUNT()
      DO Put WITH m.lnH, "  " + PADR(TAG(m.lni), 10) + " expr=" + '"' + KEY(m.lni) + '"' + ;
         " keyType='" + TYPE(KEY(m.lni)) + "'"
   ENDFOR

   = FCLOSE(m.lnH)
ENDIF

USE
= FreezeStamp(m.lcDbf)

CLOSE TABLES ALL
? "done: " + TRANSFORM(m.lnCount) + " on the last day"
RETURN

*===========================================================================
*  Helpers, as in vfpfunc.prg
*===========================================================================

*-- LF line endings, matching the C generator's dumps so the two diff alike.
PROCEDURE Put(tnH, tcText)
   = FWRITE(m.tnH, m.tcText + CHR(10))
ENDPROC


*-- Freeze the DBF header date stamp to 26-01-01, exactly as util.cpp does for
*-- the C-generated corpus (ADR-07).
FUNCTION FreezeStamp(tcPath)
   LOCAL lnH
   lnH = FOPEN(m.tcPath, 2)
   IF m.lnH < 0
      ? "  ERROR: cannot reopen " + m.tcPath + " to freeze the date stamp"
      RETURN .F.
   ENDIF
   = FSEEK(m.lnH, 1, 0)
   = FWRITE(m.lnH, CHR(26) + CHR(1) + CHR(1))
   = FCLOSE(m.lnH)
   RETURN .T.
ENDFUNC
