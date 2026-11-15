*===========================================================================
*  vfpprop.prg -- what Visual FoxPro's PROPER takes for a word
*                 (step 013, sub-step 7a-10; ADR-44)
*
*  PROPER capitalizes the first letter of each word and lowers the rest.
*  What a "word" is, VFP's documentation does not say, and the port needs it
*  exactly: is a word begun only after a blank, or also after an apostrophe,
*  a hyphen, a full stop, a digit, a tab? VFPFUNC.CDX's T_PROPER holds sixteen
*  values that do not decide it. Thirty-six named probes, then every byte
*  between two letters. This writes:
*
*    VFPPROP.DBF + .CDX   one row per probe, a nullable C(30), and one tag,
*                         PROPER(K) -- VFP's own key bytes are the oracle
*    VFPPROP.vfp.dump.txt each probe and VFP's answer, escaped, for review
*
*  Bytes above 0x7F are written with CHR() so the script's own encoding
*  cannot change them; the VM's code page is 1251, so they are Cyrillic.
*
*      DO vfpprop.prg [WITH "D:\some\where\out"]
*  or, unattended, from the host:  run-in-vm.ps1 -Script vfpprop.prg -TimeoutSeconds 120
*===========================================================================

LPARAMETERS tcOutDir

#DEFINE NAMED 36
#DEFINE PROBES 292

LOCAL lcOut, lcSelf, lcDbf, lni, lnH, luVal
LOCAL ARRAY laK[PROBES], laNote[PROBES]

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

laK[1]  = "john smith"
laNote[1] = "control"
laK[2]  = "JOHN SMITH"
laNote[2] = "the rest lowered"
laK[3]  = "jOHN sMITH"
laNote[3] = ""
laK[4]  = "  leading blanks"
laNote[4] = "a word after leading blanks"
laK[5]  = "multiple   spaces"
laNote[5] = ""
laK[6]  = "o'neil"
laNote[6] = "apostrophe"
laK[7]  = "it's"
laNote[7] = "apostrophe inside a word"
laK[8]  = "mary-jane"
laNote[8] = "hyphen"
laK[9]  = "mr.smith"
laNote[9] = "full stop"
laK[10] = "a.b.c"
laNote[10] = ""
laK[11] = "1st place"
laNote[11] = "digit first"
laK[12] = "x1y2z3"
laNote[12] = "letters after digits"
laK[13] = "3d"
laNote[13] = ""
laK[14] = "under_score word"
laNote[14] = "underscore"
laK[15] = "slash/word"
laNote[15] = "slash"
laK[16] = "comma,word"
laNote[16] = "comma"
laK[17] = "(paren) word"
laNote[17] = "bracket first"
laK[18] = "!bang"
laNote[18] = "punctuation first"
laK[19] = "tab" + CHR(9) + "word"
laNote[19] = "tab"
laK[20] = "line" + CHR(13) + "break"
laNote[20] = "carriage return"
laK[21] = "nbsp" + CHR(160) + "word"
laNote[21] = "no-break space, 0xA0"
laK[22] = "McDonald"
laNote[22] = ""
laK[23] = "a"
laNote[23] = "one letter"
laK[24] = ""
laNote[24] = "empty"
laK[25] = CHR(232) + CHR(226) + CHR(224) + CHR(237) + " " + CHR(239) + CHR(229) + CHR(242) + CHR(240) + CHR(238) + CHR(226)
laNote[25] = "Cyrillic, lower"
laK[26] = CHR(200) + CHR(194) + CHR(192) + CHR(205) + " " + CHR(207) + CHR(197) + CHR(210) + CHR(208) + CHR(206) + CHR(194)
laNote[26] = "Cyrillic, upper"
laK[27] = CHR(184) + "lka"
laNote[27] = "0xB8, the small io, first"
laK[28] = "abc" + CHR(168)
laNote[28] = "0xA8, the capital io, inside"
laK[29] = "ab" + CHR(150) + "cd"
laNote[29] = "0x96, an en dash"
laK[30] = "a  b"
laNote[30] = ""
laK[31] = "ABCDEFGHIJKLMNOPQRSTUVWXYZABCD"
laNote[31] = "the whole width"
laK[32] = "x" + CHR(0) + "y"
laNote[32] = "a zero byte"
laK[33] = "@home #tag"
laNote[33] = ""
laK[34] = "a" + CHR(171) + "b" + CHR(187) + "c"
laNote[34] = "0xAB and 0xBB, guillemets"
laK[35] = "dot. space"
laNote[35] = ""
laK[36] = .NULL.
laNote[36] = "null"

*-- Then every byte as the one character between two letters: which of the
*-- 256 begin a word is the whole of PROPER's rule, so it is sampled whole.
FOR lni = 0 TO 255
   laK[NAMED + 1 + m.lni] = "a" + CHR(m.lni) + "b"
   laNote[NAMED + 1 + m.lni] = "between, 0x" + Hex2(m.lni)
ENDFOR

lcDbf = m.lcOut + "VFPPROP.DBF"
CREATE TABLE (m.lcDbf) FREE (ID I, K C(30) NULL)
FOR lni = 1 TO PROBES
   INSERT INTO VFPPROP VALUES (m.lni, laK[m.lni])
ENDFOR
INDEX ON PROPER(K) TAG T_PROPER

lnH = FCREATE(m.lcOut + "VFPPROP.vfp.dump.txt")
IF m.lnH < 0
   ERROR "cannot create VFPPROP.vfp.dump.txt"
ENDIF
DO Put WITH m.lnH, "# CodeBase.NET VFP oracle: what PROPER takes for a word"
DO Put WITH m.lnH, "# Written by test-files-generator/vfp/vfpprop.prg in Visual FoxPro 9."
DO Put WITH m.lnH, "# Do not edit by hand; re-run the script in the VM instead."
DO Put WITH m.lnH, "# VFPPROP.DBF and its .cdx are the oracle; this is provenance."
DO Put WITH m.lnH, "producer     " + '"' + VERSION(1) + '"'
DO Put WITH m.lnH, "codePage     " + TRANSFORM(CPCURRENT())
DO Put WITH m.lnH, "collate      " + SET("COLLATE")
DO Put WITH m.lnH, ""
DO Put WITH m.lnH, "[probes]"
DO Put WITH m.lnH, "# ID  K -> PROPER(K), both trimmed and escaped; ; note"
SET ORDER TO 0
SCAN
   luVal = PROPER(K)
   DO Put WITH m.lnH, "  " + PADL(TRANSFORM(ID), 3) + "  " + ;
      IIF(ISNULL(K), ".NULL.", EscStr(RTRIM(K))) + " -> " + ;
      IIF(ISNULL(m.luVal), ".NULL.", EscStr(RTRIM(m.luVal))) + ;
      IIF(EMPTY(laNote[ID]), "", "   ; " + laNote[ID])
ENDSCAN
DO Put WITH m.lnH, ""
DO Put WITH m.lnH, "[tags]"
FOR lni = 1 TO TAGCOUNT()
   DO Put WITH m.lnH, "  " + PADR(TAG(m.lni), 10) + " expr=" + '"' + KEY(m.lni) + '"' + ;
      " keyType='" + TYPE(KEY(m.lni)) + "'"
ENDFOR
= FCLOSE(m.lnH)

USE
= FreezeStamp(m.lcDbf)
CLOSE TABLES ALL
? "done"
RETURN

*===========================================================================
*  Helpers, as in vfpfunc.prg
*===========================================================================

PROCEDURE Put(tnH, tcText)
   = FWRITE(m.tnH, m.tcText + CHR(10))
ENDPROC


FUNCTION EscStr(tc)
   LOCAL lcOut, lni, lnB
   lcOut = '"'
   FOR lni = 1 TO LEN(m.tc)
      lnB = ASC(SUBSTR(m.tc, m.lni, 1))
      DO CASE
      CASE m.lnB = 34 OR m.lnB = 92
         lcOut = m.lcOut + "\" + CHR(m.lnB)
      CASE m.lnB >= 32 AND m.lnB <= 126
         lcOut = m.lcOut + CHR(m.lnB)
      OTHERWISE
         lcOut = m.lcOut + "\x" + Hex2(m.lnB)
      ENDCASE
   ENDFOR
   RETURN m.lcOut + '"'
ENDFUNC


FUNCTION Hex2(tnByte)
   LOCAL lcDigits
   lcDigits = "0123456789ABCDEF"
   RETURN SUBSTR(m.lcDigits, INT(m.tnByte / 16) + 1, 1) + ;
          SUBSTR(m.lcDigits, MOD(m.tnByte, 16) + 1, 1)
ENDFUNC


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
