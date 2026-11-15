*===========================================================================
*  vfpgen.prg -- VFP's GENERAL collation on the current code page, as keys
*                (step 013, sub-step 7a-13; ADR-44)
*
*  CodeBase carries GENERAL's weights for cp1252, cp437 and cp850 only, and
*  refuses or misses every other code page (i4init.c:378-405). Visual FoxPro
*  has GENERAL for many more. This script writes the keys VFP builds on
*  whatever code page it runs under -- run-in-vm.ps1 -CodePage sets it through
*  config.fpw -- so the weights can be recovered from them:
*
*    VFPG<cp>.DBF + .CDX   one C(12) field, under one GENERAL tag G:
*      rows 1-256          each byte 0x00-0xFF alone: its heads and tails
*      next 256 per partner each byte followed by a partner, a byte whose
*                          own key shows a tail that is not zero -- the only
*                          way to tell a byte with tail 0 from one with none
*      then                runs of twelve consecutive bytes, 0x20 up, so
*                          expansions, tails and the cut at the key's width
*                          meet in one key
*    VFPG<cp>.vfp.dump.txt the settings, the partners, and what VFP said
*
*      run-in-vm.ps1 -Script vfpgen.prg -CodePage 1250 -ScriptArgs '"E8 9A"'
*===========================================================================

LPARAMETERS tcOutDir, tcPartners, tcCollate, tlLetterPairs, tcExtra

*-- tcExtra: more rows, written after the runs and before any letter pairs,
*-- as space-separated hex strings, one row each ("445A53" is DZS) -- each
*-- contraction alone and beside each partner, to read its own tails, and
*-- the longer spellings a contraction might extend to. VFP takes no string
*-- literal over 255 characters: pass a long list as literals joined with +.

*-- tcCollate names another of VFP's collations (step 013, 7a-15: RUSSIAN
*-- first); GENERAL when left out, so the VFPG tables are unchanged. With
*-- tlLetterPairs every letter is also written beside every letter, ISALPHA()
*-- on the current code page: a collation that weighs two letters as one --
*-- Czech "ch", Hungarian "cs" -- shows only there.

LOCAL lcOut, lcSelf, lcDbf, lcName, lnCp, lni, lnj, lnH, lcErr, lcColl, loEx, lcCollate
LOCAL ARRAY laP[1], laL[1]
LOCAL lnPartners, lnRows, lnLetters

SET TALK OFF
SET SAFETY OFF
SET EXCLUSIVE ON
SET DELETED OFF
SET CPDIALOG OFF
CLOSE TABLES ALL

SET COLLATE TO "MACHINE"
SET EXACT OFF
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

lnCp = CPCURRENT()
lcCollate = IIF(VARTYPE(m.tcCollate) = "C" AND !EMPTY(m.tcCollate), UPPER(ALLTRIM(m.tcCollate)), "GENERAL")
lcName = "VFP" + IIF(m.lcCollate == "GENERAL", "G", LEFT(m.lcCollate, 3)) + TRANSFORM(m.lnCp)
lcDbf = m.lcOut + m.lcName + ".DBF"

lnPartners = 0
IF VARTYPE(m.tcPartners) = "C" AND !EMPTY(m.tcPartners)
   lnPartners = ALINES(laP, ALLTRIM(m.tcPartners), 1, " ")
ENDIF

CREATE TABLE (m.lcDbf) FREE (ID I, K C(12))
lnRows = 0

FOR lni = 0 TO 255
   lnRows = m.lnRows + 1
   INSERT INTO (m.lcName) VALUES (m.lnRows, CHR(m.lni))
ENDFOR

FOR lnj = 1 TO m.lnPartners
   FOR lni = 0 TO 255
      lnRows = m.lnRows + 1
      INSERT INTO (m.lcName) VALUES (m.lnRows, CHR(m.lni) + CHR(EVALUATE("0x" + laP[m.lnj])))
   ENDFOR
ENDFOR

*-- Runs of twelve: 0x20-0x2B, 0x2C-0x37, ... up to 0xFF.
FOR lni = 0x20 TO 0xFF STEP 12
   LOCAL lcRun
   lcRun = ""
   FOR lnj = m.lni TO MIN(m.lni + 11, 0xFF)
      lcRun = m.lcRun + CHR(m.lnj)
   ENDFOR
   lnRows = m.lnRows + 1
   INSERT INTO (m.lcName) VALUES (m.lnRows, m.lcRun)
ENDFOR

*-- The extra rows.
LOCAL ARRAY laX[1]
LOCAL lnExtra, lcRow
lnExtra = 0
IF VARTYPE(m.tcExtra) = "C" AND !EMPTY(m.tcExtra)
   lnExtra = ALINES(laX, ALLTRIM(m.tcExtra), 1, " ")
   FOR lni = 1 TO m.lnExtra
      lcRow = ""
      FOR lnj = 1 TO LEN(laX[m.lni]) STEP 2
         lcRow = m.lcRow + CHR(EVALUATE("0x" + SUBSTR(laX[m.lni], m.lnj, 2)))
      ENDFOR
      lnRows = m.lnRows + 1
      INSERT INTO (m.lcName) VALUES (m.lnRows, m.lcRow)
   ENDFOR
ENDIF

*-- Every letter beside every letter, when asked for.
lnLetters = 0
IF VARTYPE(m.tlLetterPairs) = "L" AND m.tlLetterPairs
   FOR lni = 0 TO 255
      IF ISALPHA(CHR(m.lni))
         lnLetters = m.lnLetters + 1
         DIMENSION laL[m.lnLetters]
         laL[m.lnLetters] = m.lni
      ENDIF
   ENDFOR
   FOR lni = 1 TO m.lnLetters
      FOR lnj = 1 TO m.lnLetters
         lnRows = m.lnRows + 1
         INSERT INTO (m.lcName) VALUES (m.lnRows, CHR(laL[m.lni]) + CHR(laL[m.lnj]))
      ENDFOR
   ENDFOR
ENDIF

lcErr = ""
lcColl = ""
TRY
   SET COLLATE TO (m.lcCollate)
   INDEX ON K TAG G
   lcColl = IDXCOLLATE(1)
CATCH TO loEx
   lcErr = TRANSFORM(loEx.ErrorNo) + " " + loEx.Message
ENDTRY
SET COLLATE TO "MACHINE"

lnH = FCREATE(m.lcOut + m.lcName + ".vfp.dump.txt")
IF m.lnH < 0
   ERROR "cannot create the dump"
ENDIF
= FWRITE(m.lnH, "# CodeBase.NET VFP oracle: " + m.lcCollate + " on code page " + TRANSFORM(m.lnCp) + CHR(10))
= FWRITE(m.lnH, "# Written by test-files-generator/vfp/vfpgen.prg in Visual FoxPro 9." + CHR(10))
= FWRITE(m.lnH, "# " + m.lcName + ".DBF and its .cdx are the oracle; this is provenance." + CHR(10))
= FWRITE(m.lnH, "producer     " + '"' + VERSION(1) + '"' + CHR(10))
= FWRITE(m.lnH, "codePage     " + TRANSFORM(m.lnCp) + CHR(10))
= FWRITE(m.lnH, "cpdbf        " + TRANSFORM(CPDBF()) + CHR(10))
= FWRITE(m.lnH, "partners     " + IIF(m.lnPartners = 0, "-", ALLTRIM(m.tcPartners)) + CHR(10))
IF m.lnExtra > 0
   = FWRITE(m.lnH, "extra        " + ALLTRIM(m.tcExtra) + CHR(10))
ENDIF
IF m.lnLetters > 0
   = FWRITE(m.lnH, "letters      " + TRANSFORM(m.lnLetters) + " (every pair written)" + CHR(10))
ENDIF
= FWRITE(m.lnH, "records      " + TRANSFORM(RECCOUNT()) + CHR(10))
= FWRITE(m.lnH, "tag G        " + IIF(EMPTY(m.lcErr), "collate=" + m.lcColl + " keyType='" + TYPE(KEY(1)) + "'", "refused " + m.lcErr) + CHR(10))
= FCLOSE(m.lnH)

USE
= FreezeStamp(m.lcDbf)
CLOSE TABLES ALL
? "done " + TRANSFORM(m.lnCp)
RETURN


*-- Freeze the DBF header date stamp to 26-01-01, as the other scripts do (ADR-07).
FUNCTION FreezeStamp(tcPath)
   LOCAL lnH
   lnH = FOPEN(m.tcPath, 2)
   IF m.lnH < 0
      RETURN .F.
   ENDIF
   = FSEEK(m.lnH, 1, 0)
   = FWRITE(m.lnH, CHR(26) + CHR(1) + CHR(1))
   = FCLOSE(m.lnH)
   RETURN .T.
ENDFUNC
