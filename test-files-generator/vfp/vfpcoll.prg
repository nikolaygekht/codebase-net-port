*===========================================================================
*  vfpcoll.prg -- which of VFP's collations build on the current code page
*                 (step 013, sub-step 7a-15; ADR-44)
*
*  A survey, not a corpus case: for each collation name VFP documents, set it
*  and try to index a small cursor, and write one line per name -- built, with
*  the name IDXCOLLATE() reports, or the error VFP raised. Run once per code
*  page through run-in-vm.ps1 -CodePage; vfpgen.prg then recovers the weights
*  of each pair that builds.
*===========================================================================

LPARAMETERS tcOutDir

LOCAL lcOut, lcSelf, lnCp, lnH, lni, lcName, lcResult, loEx
LOCAL ARRAY laNames[15]

SET TALK OFF
SET SAFETY OFF
SET CPDIALOG OFF
CLOSE TABLES ALL

lcSelf = SYS(16, 0)
IF EMPTY(m.lcSelf)
   lcSelf = SYS(16)
ENDIF
lcOut = IIF(EMPTY(m.tcOutDir), ADDBS(JUSTPATH(m.lcSelf)) + "out", m.tcOutDir)
lcOut = ADDBS(m.lcOut)
IF !DIRECTORY(m.lcOut)
   MD (m.lcOut)
ENDIF

laNames[1]  = "GENERAL"
laNames[2]  = "CZECH"
laNames[3]  = "DUTCH"
laNames[4]  = "GERMAN"
laNames[5]  = "GREEK"
laNames[6]  = "HUNGARY"
laNames[7]  = "ICELAND"
laNames[8]  = "NORDAN"
laNames[9]  = "POLISH"
laNames[10] = "RUSSIAN"
laNames[11] = "SLOVAK"
laNames[12] = "SPANISH"
laNames[13] = "SWEFIN"
laNames[14] = "TURKISH"
laNames[15] = "UNIQWT"

lnCp = CPCURRENT()
lnH = FCREATE(m.lcOut + "VFPCOLL" + TRANSFORM(m.lnCp) + ".txt")
= FWRITE(m.lnH, "codePage " + TRANSFORM(m.lnCp) + CHR(10))

FOR lni = 1 TO ALEN(laNames)
   lcName = laNames[m.lni]
   lcResult = ""
   TRY
      SET COLLATE TO (m.lcName)
      CREATE CURSOR SURVEY (K C(4))
      INSERT INTO SURVEY VALUES ("ab")
      INDEX ON K TAG T
      lcResult = "built " + IDXCOLLATE(1)
      USE IN SURVEY
   CATCH TO loEx
      lcResult = "refused " + TRANSFORM(loEx.ErrorNo)
      IF USED("SURVEY")
         USE IN SURVEY
      ENDIF
   ENDTRY
   SET COLLATE TO "MACHINE"
   = FWRITE(m.lnH, PADR(m.lcName, 8) + " " + m.lcResult + CHR(10))
ENDFOR

= FCLOSE(m.lnH)
? "done " + TRANSFORM(m.lnCp)
RETURN
