*===========================================================================
*  vfpfunc.prg -- the VFP-only function oracle (step 010-expr, ADR-44)
*
*  WHAT THIS IS FOR
*  ----------------
*  CodeBase has 35 named functions and no arithmetic at all: ABS, INT, ROUND,
*  MOD, MIN, MAX, SQRT, SIGN, LEN, AT, ASC, LOWER and REPLICATE are all
*  refused with e4unrecFunction (-470). Visual FoxPro has them, and a CDX that
*  VFP writes may legitimately index by one -- so the port must read such a
*  tag (ADR-43). Nothing in net/corpus/ can gate that, because the corpus
*  generator IS CodeBase.
*
*  This script is the second oracle. It runs in Visual FoxPro 9 and produces
*  the files CodeBase cannot write:
*
*    VFPFUNC.DBF + .cdx   a table with one tag per VFP-only function, so the
*                         per-function parse-time key LENGTH and the stored key
*                         BYTES both come from VFP itself
*    VFPFVAL.DBF          one record per constant expression, the result stored
*                         in a field of its own type -- the value oracle, in
*                         binary, with no decimal formatting in the way
*    *.vfp.dump.txt       what VFP says about both, for human review and diff
*
*  THE COMMITTED .DBF/.cdx FILES ARE THE ORACLE, not the text dumps. A double
*  written to a B field is the exact bits VFP computed; printing it as text
*  would hide the low bits the key transform depends on. The text dumps are
*  provenance and review material, and say so where a number is display-only.
*
*  HOW TO RUN (in the VFP 9 VM)
*  ----------------------------
*      DO vfpfunc.prg
*  or  DO vfpfunc.prg WITH "D:\some\where\out"
*
*  Output lands in .\out\ beside this script unless a directory is passed.
*  Copy it into the repo with copy-vfp-corpus.bat, never by hand.
*
*  DETERMINISM. Every SET this file's output depends on is pinned below and
*  recorded in the dump header: COLLATE (key bytes), CENTURY and DATE (every
*  date kernel, ADR-42), EXACT (the = comparison, DESIGN.md Decision 8), NULL,
*  DECIMALS, and the current code page. The DBF header date stamp is frozen to
*  26-01-01 exactly as the C generator freezes it (ADR-07), so re-running on
*  another day does not churn the bytes. The .cdx version counter at offset 8
*  is VFP's own and may still move; PORTING-PLAN.md section 8 already lists it
*  as assert-and-mask.
*
*  The trailing 0x1A that VFP writes and CodeBase does not is DELIBERATELY
*  kept. It is the reader-tolerance case the corpus never had.
*===========================================================================

LPARAMETERS tcOutDir

#DEFINE ROWS 16

LOCAL lcOut, lcDbf, lcVal, lcExpr, lcTag, lcGrp, lcType, lcErr, lcMsg, lcNote
LOCAL lnH, lni, lnj, lnCount, llOk, luVal, lcSelf, loEx
LOCAL ARRAY laText[ROWS], laCity[ROWS], laNum[ROWS], laInt[ROWS]

SET TALK OFF
SET SAFETY OFF
SET EXCLUSIVE ON
SET DELETED OFF
SET CPDIALOG OFF
CLOSE TABLES ALL
CLOSE DATABASES ALL

*-- Pinned, not inherited. Everything below depends on these.
SET COLLATE TO "MACHINE"
*-- ROLLOVER is not a display setting: it decides which century a two-digit
*-- year lands in, and VFP defaults it to the machine clock (current year
*-- + 50, mod 100). Unpinned, the CTOD rows below change every January.
SET CENTURY TO 19 ROLLOVER 29
SET CENTURY OFF
SET DATE TO AMERICAN
SET EXACT OFF
SET NULL OFF
SET DECIMALS TO 2

lcSelf = SYS(16, 0)
IF EMPTY(m.lcSelf)
   lcSelf = SYS(16)
ENDIF
lcOut = IIF(EMPTY(m.tcOutDir), ADDBS(JUSTPATH(m.lcSelf)) + "out", m.tcOutDir)
lcOut = ADDBS(m.lcOut)
IF !DIRECTORY(m.lcOut)
   MD (m.lcOut)
ENDIF

? "VFP oracle -> " + m.lcOut

*===========================================================================
*  1. VFPFUNC.DBF -- the table the tags are built over
*
*  The field names and the K_TEXT values deliberately mirror CDXEXPR, so a
*  row of this dump can be read beside the CodeBase one without translating.
*  Values above 0x7F are written with CHR() rather than as literals, so the
*  script's own file encoding cannot change what lands in the table.
*===========================================================================

lcDbf = m.lcOut + "VFPFUNC.DBF"
CREATE TABLE (m.lcDbf) FREE ;
   (ID I, K_TEXT C(20), K_CITY C(15), K_NUM N(12,3), K_INT I, ;
    K_DBL B(6), K_DATE D, K_TS T, K_CUR Y, K_LOG L)

laText[1]  = "SMITH"
laText[2]  = "SMITHSON"
laText[3]  = "john smith"
laText[4]  = "  leading blanks"
laText[5]  = "ABCDEFGHIJKLMNOPQRST"
laText[6]  = ""
laText[7]  = "    "
laText[8]  = "123.45"
laText[9]  = "not a number"
laText[10] = "01/15/2000"
laText[11] = "99/99/9999"
laText[12] = "ZEBRA"
laText[13] = "AARDVARK"
laText[14] = "MIDDLE"
laText[15] = "MIDDLE"
laText[16] = CHR(201) + CHR(232)

laCity[1]  = "EDMONTON"
laCity[2]  = "CALGARY"
laCity[3]  = "TORONTO"
laCity[4]  = ""
laCity[5]  = "MONTREAL"
laCity[6]  = "OTTAWA"
laCity[7]  = "  "
laCity[8]  = "HALIFAX"
laCity[9]  = "REGINA"
laCity[10] = "VICTORIA"
laCity[11] = "WINNIPEG"
laCity[12] = "QUEBEC"
laCity[13] = "LONDON"
laCity[14] = "PARIS"
laCity[15] = "PARIS"
laCity[16] = "BERLIN"

*-- The numeric column carries the midpoints and the signs the arithmetic set
*-- is interesting at: 2.5 and -2.5 for ROUND, 0.125 and 0.135 for the third
*-- decimal, both signs for MOD and SIGN, and zero.
laNum[1]  = 0.000
laNum[2]  = -9999999.999
laNum[3]  = 99999999.999
laNum[4]  = -0.001
laNum[5]  = 2.500
laNum[6]  = -2.500
laNum[7]  = 0.125
laNum[8]  = 0.135
laNum[9]  = 5.000
laNum[10] = -5.000
laNum[11] = 3.000
laNum[12] = -3.000
laNum[13] = 1.500
laNum[14] = -1.500
laNum[15] = 12345.679
laNum[16] = -0.500

FOR lni = 1 TO ROWS
   laInt[m.lni] = IIF(MOD(m.lni, 3) = 0, -(m.lni * 7), m.lni * 7)
ENDFOR

FOR lni = 1 TO ROWS
   APPEND BLANK
   REPLACE ID     WITH m.lni, ;
           K_TEXT WITH laText[m.lni], ;
           K_CITY WITH laCity[m.lni], ;
           K_NUM  WITH laNum[m.lni], ;
           K_INT  WITH laInt[m.lni], ;
           K_DBL  WITH laNum[m.lni] / 2, ;
           K_DATE WITH DATE(2000, 1, 15) + m.lni, ;
           K_TS   WITH DATETIME(2026, 1, 1, 12, 0, m.lni), ;
           K_CUR  WITH m.lni * 1.25 - 5, ;
           K_LOG  WITH (MOD(m.lni, 2) = 0)
ENDFOR

*===========================================================================
*  2. The tags -- one per VFP-only function, plus four shared-name probes
*
*  Column 1 is the tag name (10 characters is the CDX limit), column 2 the
*  expression, column 3 why the row is here.
*
*  Rows 1-13 are exactly the thirteen CDXEXPR records as codebase-refuses,
*  in the same order, so the two dumps line up row for row.
*
*  Rows 14-15 are the string functions whose result LENGTH is per-function --
*  ADR-43 names these as the one real risk, because key length follows the
*  result TYPE everywhere else.
*
*  Rows 16-18 are shared names, where CodeBase stays authoritative for what we
*  write but a divergence would break READING a VFP-written tag. TRIM is the
*  sharpest: CodeBase returns the source width NUL-padded, so if VFP sizes its
*  key differently our parser computes a key length the file does not have.
*===========================================================================

LOCAL ARRAY laTag[18, 3]

laTag[1,1]  = "T_ABS"
laTag[1,2]  = "ABS(K_NUM)"
laTag[1,3]  = "r4numDoub, no new key route"
laTag[2,1]  = "T_INT"
laTag[2,2]  = "INT(K_NUM)"
laTag[2,3]  = "r4numDoub, truncation direction on both signs"
laTag[3,1]  = "T_ROUND"
laTag[3,2]  = "ROUND(K_NUM,1)"
laTag[3,3]  = "r4numDoub, the rounding rule STR also raises"
laTag[4,1]  = "T_MOD"
laTag[4,2]  = "MOD(K_NUM,3)"
laTag[4,3]  = "r4numDoub, sign of the result on a negative operand"
laTag[5,1]  = "T_MIN"
laTag[5,2]  = "MIN(K_NUM,100)"
laTag[5,3]  = "r4numDoub"
laTag[6,1]  = "T_MAX"
laTag[6,2]  = "MAX(K_NUM,100)"
laTag[6,3]  = "r4numDoub"
laTag[7,1]  = "T_SQRT"
laTag[7,2]  = "SQRT(ABS(K_NUM))"
laTag[7,3]  = "r4numDoub; ABS inside because SQRT of a negative raises"
laTag[8,1]  = "T_SIGN"
laTag[8,2]  = "SIGN(K_NUM)"
laTag[8,3]  = "r4numDoub"
laTag[9,1]  = "T_LEN"
laTag[9,2]  = "LEN(K_TEXT)"
laTag[9,3]  = "r4numDoub over a character operand"
laTag[10,1] = "T_AT"
laTag[10,2] = 'AT("A",K_TEXT)'
laTag[10,3] = "r4numDoub, two character operands"
laTag[11,1] = "T_ASC"
laTag[11,2] = 'ASC(K_TEXT+" ")'
laTag[11,3] = "r4numDoub; the pad keeps ASC off an empty string, which raises"
laTag[12,1] = "T_LOWER"
laTag[12,2] = "LOWER(K_TEXT)"
laTag[12,3] = "r4str, result length is the operand's -- expect keyLen 20"
laTag[13,1] = "T_REPL"
laTag[13,2] = "REPLICATE(K_TEXT,2)"
laTag[13,3] = "r4str, result length is n x operand -- expect keyLen 40"
laTag[14,1] = "T_PROPER"
laTag[14,2] = "PROPER(K_TEXT)"
laTag[14,3] = "r4str, beyond the thirteen; length should follow the operand"
laTag[15,1] = "T_STRTRN"
laTag[15,2] = 'STRTRAN(K_TEXT,"A","BB")'
laTag[15,3] = "r4str with a value-dependent length -- how does VFP size it?"
laTag[16,1] = "T_VTRIM"
laTag[16,2] = "TRIM(K_TEXT)"
laTag[16,3] = "shared name; CodeBase returns source width NUL-padded"
laTag[17,1] = "T_VSTR"
laTag[17,2] = "STR(K_NUM,12,3)"
laTag[17,3] = "shared name; CodeBase rounds half away from zero (ADR-43 open)"
laTag[18,1] = "T_UPPER"
laTag[18,2] = "UPPER(K_TEXT)"
laTag[18,3] = "shared name, the control row -- this one must agree"

FOR lni = 1 TO ALEN(laTag, 1)
   lcTag  = laTag[m.lni, 1]
   lcExpr = laTag[m.lni, 2]
   TRY
      INDEX ON &lcExpr TAG (m.lcTag)
      laTag[m.lni, 3] = laTag[m.lni, 3] + " | built"
   CATCH TO loEx
      lcMsg = NoPath(loEx.Message, m.lcOut)
      laTag[m.lni, 3] = laTag[m.lni, 3] + " | REFUSED: " + m.lcMsg
      ? "  tag " + m.lcTag + " REFUSED: " + m.lcMsg
   ENDTRY
ENDFOR

DO WriteFuncDump WITH m.lcOut, laTag
USE
= FreezeStamp(m.lcDbf)

*===========================================================================
*  3. VFPFVAL.DBF -- the constant-expression value oracle
*
*  One record per expression, the result stored in a field of its own type.
*  RESD is a Double, so a numeric result is the bits VFP computed and not a
*  formatting of them; RESLEN carries the true length of a character result,
*  because the C(80) field pads and a trailing blank would otherwise be lost.
*
*  Three groups, because two of them need a different environment:
*    main        the pinned settings above
*    exact-on    SET EXACT ON, for the = rows (DESIGN.md Decision 8)
*    century-on  SET CENTURY ON, for the date kernel (ADR-42)
*===========================================================================

lcVal = m.lcOut + "VFPFVAL.DBF"
CREATE TABLE (m.lcVal) FREE ;
   (GRP C(12), EXPRTEXT C(60), RESTYPE C(1), RESLEN I, ;
    RESD B(10), RESC C(80), RESL L, RESDT D, ERRMSG C(60), CBNOTE C(60))

LOCAL ARRAY laE[120, 3]
lnCount = 0

*-- (a) The thirteen CodeBase refuses, verbatim from CDXEXPR's [vfp-only]
*--     section and in its order. These have no other oracle at all.
lnCount = AddExpr(@laE, m.lnCount, "ABS(-1)",           "main", "")
lnCount = AddExpr(@laE, m.lnCount, "INT(1.5)",          "main", "")
lnCount = AddExpr(@laE, m.lnCount, "ROUND(1.5,0)",      "main", "")
lnCount = AddExpr(@laE, m.lnCount, "MOD(5,3)",          "main", "")
lnCount = AddExpr(@laE, m.lnCount, "MIN(1,2)",          "main", "")
lnCount = AddExpr(@laE, m.lnCount, "MAX(1,2)",          "main", "")
lnCount = AddExpr(@laE, m.lnCount, "SQRT(4)",           "main", "")
lnCount = AddExpr(@laE, m.lnCount, "SIGN(-3)",          "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'LEN("AB")',         "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'AT("B","AB")',      "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'ASC("A")',          "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'LOWER("AB")',       "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'REPLICATE("A",3)',  "main", "")

*-- (b) The edges of the arithmetic set. Midpoints in both directions, the
*--     third decimal where the nearest double sits below the printed value,
*--     and MOD with every sign combination -- the one place implementations
*--     genuinely disagree (truncated vs floored).
lnCount = AddExpr(@laE, m.lnCount, "INT(-1.5)",         "main", "")
lnCount = AddExpr(@laE, m.lnCount, "INT(-0.5)",         "main", "")
lnCount = AddExpr(@laE, m.lnCount, "ROUND(2.5,0)",      "main", "half up or half even")
lnCount = AddExpr(@laE, m.lnCount, "ROUND(-2.5,0)",     "main", "")
lnCount = AddExpr(@laE, m.lnCount, "ROUND(0.125,2)",    "main", "the double is exact")
lnCount = AddExpr(@laE, m.lnCount, "ROUND(0.135,2)",    "main", "the double is below")
lnCount = AddExpr(@laE, m.lnCount, "ROUND(12345.6789,3)", "main", "")
lnCount = AddExpr(@laE, m.lnCount, "ROUND(1.5,-1)",     "main", "negative places")
lnCount = AddExpr(@laE, m.lnCount, "MOD(-5,3)",         "main", "")
lnCount = AddExpr(@laE, m.lnCount, "MOD(5,-3)",         "main", "")
lnCount = AddExpr(@laE, m.lnCount, "MOD(-5,-3)",        "main", "")
lnCount = AddExpr(@laE, m.lnCount, "MOD(5.5,2)",        "main", "")
lnCount = AddExpr(@laE, m.lnCount, "SIGN(0)",           "main", "")
lnCount = AddExpr(@laE, m.lnCount, "SIGN(3)",           "main", "")
lnCount = AddExpr(@laE, m.lnCount, "SQRT(2)",           "main", "irrational, all 52 bits")
lnCount = AddExpr(@laE, m.lnCount, "ABS(-0.001)",       "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'MIN("A","B")',      "main", "the string overload")
lnCount = AddExpr(@laE, m.lnCount, 'MAX("A","B")',      "main", "")
lnCount = AddExpr(@laE, m.lnCount, "MIN(1,2,3)",        "main", "three parameters")
lnCount = AddExpr(@laE, m.lnCount, 'AT("Z","AB")',      "main", "not found")
lnCount = AddExpr(@laE, m.lnCount, 'AT("B","ABAB",2)',  "main", "occurrence")
lnCount = AddExpr(@laE, m.lnCount, 'LEN("")',           "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'LOWER("AB1c" + CHR(201))', "main", "above 0x7F")
lnCount = AddExpr(@laE, m.lnCount, 'REPLICATE("AB",0)', "main", "zero copies")

*-- (c) Shared names. Every row here has a CodeBase answer already recorded in
*--     claude/dev/010-expr/ORACLE-SUITE.txt, so this group is a straight diff.
*--     CodeBase stays authoritative for what we WRITE (ADR-43); a divergence
*--     found here is documented, not adopted.
lnCount = AddExpr(@laE, m.lnCount, "STR(2.5,10,0)",       "main", "cb: '         3'")
lnCount = AddExpr(@laE, m.lnCount, "STR(-2.5,10,0)",      "main", "cb: '        -3'")
lnCount = AddExpr(@laE, m.lnCount, "STR(0.125,10,2)",     "main", "cb: '      0.13'")
lnCount = AddExpr(@laE, m.lnCount, "STR(0.135,10,2)",     "main", "cb: '      0.14'")
lnCount = AddExpr(@laE, m.lnCount, "STR(12345.6789,12,3)", "main", "cb: '   12345.679'")
lnCount = AddExpr(@laE, m.lnCount, "STR(1,3,0)",          "main", "cb: '  1'")
lnCount = AddExpr(@laE, m.lnCount, "STR(123456,3,0)",     "main", "cb: '***' overflow")
lnCount = AddExpr(@laE, m.lnCount, "STRZERO(42,8)",       "main", "not a VFP function; cb: '00000042'")
lnCount = AddExpr(@laE, m.lnCount, "STRZERO(-42,8)",      "main", "not a VFP function; cb: '00000-42'")
lnCount = AddExpr(@laE, m.lnCount, 'VAL("123.45")',       "main", "cb: 123.45")
lnCount = AddExpr(@laE, m.lnCount, 'VAL("not a number")', "main", "cb: 0.0")
lnCount = AddExpr(@laE, m.lnCount, 'VAL("")',             "main", "cb: 0.0")
lnCount = AddExpr(@laE, m.lnCount, 'UPPER("john smith")', "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'TRIM("abc   ")',      "main", "cb: source width, NUL-padded")
lnCount = AddExpr(@laE, m.lnCount, 'TRIM("   ")',         "main", "cb: three NULs")
lnCount = AddExpr(@laE, m.lnCount, 'LTRIM("   abc")',     "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'ALLTRIM("  abc  ")',  "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'SUBSTR("ABCDEF",2,3)', "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'SUBSTR("ABCDEF",5,10)', "main", "past the end")
lnCount = AddExpr(@laE, m.lnCount, 'LEFT("ABC",10)',      "main", "cb: 'ABC', not padded")
lnCount = AddExpr(@laE, m.lnCount, 'RIGHT("ABCDEF",3)',   "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'PADL("X",5)',         "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'PADR("X",5)',         "main", "")
lnCount = AddExpr(@laE, m.lnCount, "SPACE(5)",            "main", "")
lnCount = AddExpr(@laE, m.lnCount, "CHR(65)",             "main", "")
lnCount = AddExpr(@laE, m.lnCount, "CHR(0)",              "main", "a NUL is a byte")
lnCount = AddExpr(@laE, m.lnCount, 'DTOS(CTOD("01/15/00"))', "main", "cb reads the clock")
lnCount = AddExpr(@laE, m.lnCount, 'DTOS(CTOD("01/15/28"))', "main", "below ROLLOVER 29 -- expect 20xx")
lnCount = AddExpr(@laE, m.lnCount, 'DTOS(CTOD("01/15/29"))', "main", "AT ROLLOVER 29 -- expect 19xx, the boundary is inclusive")
lnCount = AddExpr(@laE, m.lnCount, 'DTOS(CTOD("01/15/50"))', "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'DTOS(CTOD("01/15/70"))', "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'DTOS(CTOD("01/15/99"))', "main", "")
lnCount = AddExpr(@laE, m.lnCount, 'DTOC(CTOD("01/15/2000"))', "main", "cb picture is 8 chars")
lnCount = AddExpr(@laE, m.lnCount, 'DAY(CTOD("01/15/2000"))', "main", "")
lnCount = AddExpr(@laE, m.lnCount, '"AAA" = "AAAA"',      "main", "cb: TRUE, prefix -- the diverging row")
lnCount = AddExpr(@laE, m.lnCount, '"AAAA" = "AAA"',      "main", "cb: TRUE, prefix -- both engines agree here")

*-- (d) The two groups that need the environment changed.
lnCount = AddExpr(@laE, m.lnCount, '"AAA" = "AAAA"',      "exact-on", "")
lnCount = AddExpr(@laE, m.lnCount, '"AAAA" = "AAA"',      "exact-on", "")
lnCount = AddExpr(@laE, m.lnCount, 'DTOC(CTOD("01/15/2000"))', "century-on", "picture length")
lnCount = AddExpr(@laE, m.lnCount, 'DTOS(CTOD("01/15/29"))', "century-on", "the boundary again, with the display on")

FOR lni = 1 TO m.lnCount
   lcExpr = laE[m.lni, 1]
   lcGrp  = laE[m.lni, 2]
   lcNote = laE[m.lni, 3]

   DO CASE
   CASE m.lcGrp == "exact-on"
      SET EXACT ON
      SET CENTURY OFF
   CASE m.lcGrp == "century-on"
      SET EXACT OFF
      SET CENTURY ON
   OTHERWISE
      SET EXACT OFF
      SET CENTURY OFF
   ENDCASE

   lcType = "!"
   lcErr  = ""
   llOk   = .F.
   TRY
      lcType = TYPE(m.lcExpr)
      luVal  = EVALUATE(m.lcExpr)
      llOk   = .T.
   CATCH TO loEx
      lcType = "!"
      lcErr  = NoPath(loEx.Message, m.lcOut)
   ENDTRY

   APPEND BLANK
   REPLACE GRP     WITH m.lcGrp, ;
           EXPRTEXT WITH m.lcExpr, ;
           RESTYPE WITH m.lcType, ;
           ERRMSG  WITH m.lcErr, ;
           CBNOTE  WITH m.lcNote

   IF m.llOk
      DO CASE
      CASE m.lcType = "N"
         REPLACE RESD WITH m.luVal
      CASE m.lcType = "C"
         REPLACE RESC WITH m.luVal, RESLEN WITH LEN(m.luVal)
      CASE m.lcType = "L"
         REPLACE RESL WITH m.luVal
      CASE m.lcType = "D"
         REPLACE RESDT WITH m.luVal
      ENDCASE
   ENDIF
ENDFOR

SET EXACT OFF
SET CENTURY OFF

DO WriteValDump WITH m.lcOut
USE
= FreezeStamp(m.lcVal)

CLOSE TABLES ALL
? "done."
RETURN

*===========================================================================
*  Helpers
*===========================================================================

PROCEDURE WriteFuncDump(tcOut, taTag)
   LOCAL lnH, lni, lnj, lnFields, lcLine
   LOCAL ARRAY laF[1, 4]

   lnH = FCREATE(m.tcOut + "VFPFUNC.vfp.dump.txt")
   IF m.lnH < 0
      ? "  ERROR: cannot create VFPFUNC.vfp.dump.txt"
      RETURN
   ENDIF

   DO Put WITH m.lnH, "# CodeBase.NET corpus VFP oracle dump"
   DO Put WITH m.lnH, "# Written by test-files-generator/vfp/vfpfunc.prg in Visual FoxPro 9."
   DO Put WITH m.lnH, "# Do not edit by hand; re-run the script in the VM instead."
   DO Put WITH m.lnH, "# The .DBF and .cdx are the oracle. Numbers here are decimal text and"
   DO Put WITH m.lnH, "# are for review only -- the stored bits are in the file."
   DO Put WITH m.lnH, "table        VFPFUNC.DBF"
   DO Put WITH m.lnH, "producer     " + EscStr(VERSION(1))
   DO Put WITH m.lnH, "generated    " + DTOC(DATE()) + "   (header stamp frozen to 26-01-01)"
   DO Put WITH m.lnH, "codePage     " + TRANSFORM(CPCURRENT())
   DO Put WITH m.lnH, "collate      " + SET("COLLATE")
   DO Put WITH m.lnH, "century      " + SET("CENTURY")
   DO Put WITH m.lnH, "rollover     " + TRANSFORM(SET("CENTURY", 2))
   DO Put WITH m.lnH, "dateFormat   " + SET("DATE")
   DO Put WITH m.lnH, "exact        " + SET("EXACT")
   DO Put WITH m.lnH, "null         " + SET("NULL")
   DO Put WITH m.lnH, "decimals     " + TRANSFORM(SET("DECIMALS"))
   DO Put WITH m.lnH, "numRecs      " + TRANSFORM(RECCOUNT())
   DO Put WITH m.lnH, ""

   lnFields = AFIELDS(laF)
   DO Put WITH m.lnH, "[fields]"
   FOR lni = 1 TO m.lnFields
      DO Put WITH m.lnH, "  " + PADR(laF[m.lni, 1], 12) + " type='" + laF[m.lni, 2] + ;
         "' len=" + TRANSFORM(laF[m.lni, 3]) + " dec=" + TRANSFORM(laF[m.lni, 4])
   ENDFOR
   DO Put WITH m.lnH, ""

   *-- Physical order, not the controlling tag's: the last INDEX ON left an
   *-- order set, and a dump that changes shape when a tag is added is useless
   *-- to diff.
   SET ORDER TO 0
   DO Put WITH m.lnH, "[records]"
   SCAN
      lcLine = "  rec " + PADL(TRANSFORM(RECNO()), 3) + ;
         " K_TEXT=" + EscStr(K_TEXT) + ;
         " K_CITY=" + EscStr(K_CITY) + ;
         " K_NUM=" + ALLTRIM(STR(K_NUM, 14, 3)) + ;
         " K_INT=" + ALLTRIM(TRANSFORM(K_INT)) + ;
         " K_DATE=" + DTOS(K_DATE) + ;
         " K_LOG=" + IIF(K_LOG, "T", "F")
      DO Put WITH m.lnH, m.lcLine
   ENDSCAN
   DO Put WITH m.lnH, ""

   *-- What VFP can say about its own tags. It does not expose a tag's key
   *-- LENGTH, which is the number this case exists to settle -- that is read
   *-- from the .cdx tag header, where VFP wrote it.
   DO Put WITH m.lnH, "[tags]"
   DO Put WITH m.lnH, "# keyLen is deliberately absent: VFP does not expose it, and the"
   DO Put WITH m.lnH, "# tag header in VFPFUNC.cdx is the authority."
   GO TOP
   FOR lni = 1 TO TAGCOUNT()
      DO Put WITH m.lnH, "  " + PADR(TAG(m.lni), 10) + " expr=" + EscStr(KEY(m.lni)) + ;
         " keyType='" + TYPE(KEY(m.lni)) + "'"
   ENDFOR
   DO Put WITH m.lnH, ""

   DO Put WITH m.lnH, "[tag notes]"
   FOR lni = 1 TO ALEN(m.taTag, 1)
      DO Put WITH m.lnH, "  " + PADR(taTag[m.lni, 1], 10) + " " + ;
         PADR(taTag[m.lni, 2], 30) + " ; " + taTag[m.lni, 3]
   ENDFOR

   = FCLOSE(m.lnH)
ENDPROC


PROCEDURE WriteValDump(tcOut)
   LOCAL lnH, lcVal

   lnH = FCREATE(m.tcOut + "VFPFVAL.vfp.dump.txt")
   IF m.lnH < 0
      ? "  ERROR: cannot create VFPFVAL.vfp.dump.txt"
      RETURN
   ENDIF

   DO Put WITH m.lnH, "# CodeBase.NET corpus VFP value oracle"
   DO Put WITH m.lnH, "# Written by test-files-generator/vfp/vfpfunc.prg in Visual FoxPro 9."
   DO Put WITH m.lnH, "# Do not edit by hand; re-run the script in the VM instead."
   DO Put WITH m.lnH, "#"
   DO Put WITH m.lnH, "# VFPFVAL.DBF IS THE ORACLE. A numeric result is stored in RESD, a"
   DO Put WITH m.lnH, "# Double, and the text below is a formatting of it -- if the two ever"
   DO Put WITH m.lnH, "# disagree the field wins. RESLEN carries the true length of a"
   DO Put WITH m.lnH, "# character result, because the C(80) field pads."
   DO Put WITH m.lnH, "table        VFPFVAL.DBF"
   DO Put WITH m.lnH, "producer     " + EscStr(VERSION(1))
   DO Put WITH m.lnH, "codePage     " + TRANSFORM(CPCURRENT())
   DO Put WITH m.lnH, "collate      " + SET("COLLATE")
   DO Put WITH m.lnH, "century      " + SET("CENTURY")
   DO Put WITH m.lnH, "rollover     " + TRANSFORM(SET("CENTURY", 2))
   DO Put WITH m.lnH, "count        " + TRANSFORM(RECCOUNT())
   DO Put WITH m.lnH, ""
   DO Put WITH m.lnH, "[values]"

   SCAN
      lcVal = ""
      DO CASE
      CASE RESTYPE = "N"
         lcVal = ALLTRIM(STR(RESD, 24, 12)) + "   (display only, RESD is the bits)"
      CASE RESTYPE = "C"
         lcVal = EscStr(LEFT(RESC, RESLEN)) + " len=" + TRANSFORM(RESLEN)
      CASE RESTYPE = "L"
         lcVal = IIF(RESL, "TRUE", "FALSE")
      CASE RESTYPE = "D"
         lcVal = DTOS(RESDT) + "   (julian " + TRANSFORM(VAL(SYS(11, RESDT))) + ")"
      CASE RESTYPE = "!"
         lcVal = "REFUSED: " + ALLTRIM(ERRMSG)
      OTHERWISE
         lcVal = "type '" + RESTYPE + "' not stored by this script"
      ENDCASE

      DO Put WITH m.lnH, "  " + PADR(ALLTRIM(GRP), 11) + PADR(ALLTRIM(EXPRTEXT), 30) + ;
         " -> " + m.lcVal + IIF(EMPTY(CBNOTE), "", "   ; " + ALLTRIM(CBNOTE))
   ENDSCAN

   = FCLOSE(m.lnH)
ENDPROC


*-- Append one expression to the array, growing nothing: the array is sized
*-- above and this only fills it. Returns the new count.
FUNCTION AddExpr(taE, tnCount, tcExpr, tcGrp, tcNote)
   LOCAL lnAt
   lnAt = m.tnCount + 1
   taE[m.lnAt, 1] = m.tcExpr
   taE[m.lnAt, 2] = m.tcGrp
   taE[m.lnAt, 3] = m.tcNote
   RETURN m.lnAt
ENDFUNC


*-- LF line endings, matching the C generator's dumps so the two diff alike.
PROCEDURE Put(tnH, tcText)
   = FWRITE(m.tnH, m.tcText + CHR(10))
ENDPROC


*-- Printable ASCII verbatim, everything else \xHH, quotes and backslashes
*-- escaped, the whole thing quoted. Byte for byte what util.cpp's
*-- dumpEscapedBytes writes, so a value can be compared across the two dumps.
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


*-- Some VFP messages quote the absolute path of the file they were working
*-- on. That path is the VM's, not the script's, so it must not reach a
*-- checked-in dump. Both cases are stripped: VFP does not report the path
*-- in the case SYS(16) hands it to us.
FUNCTION NoPath(tcMsg, tcDir)
   RETURN STRTRAN(STRTRAN(m.tcMsg, m.tcDir, ""), LOWER(m.tcDir), "")
ENDFUNC


*-- Freeze the DBF header date stamp to 26-01-01, exactly as util.cpp does for
*-- the C-generated corpus (ADR-07). Without this every re-run churns three
*-- bytes in every file. It is the only place this script alters what VFP
*-- wrote -- the trailing 0x1A, which CodeBase never writes, is left alone on
*-- purpose.
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
