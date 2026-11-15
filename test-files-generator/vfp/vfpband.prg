*===========================================================================
*  vfpband.prg -- what Visual FoxPro's own functions do where they have no
*                 ordinary answer: an error, or a .NULL. operand
*                 (step 013, sub-step 7a-9; ADR-44)
*
*  Step 010 made MOD by zero and SQRT of a negative answer 0, and recorded
*  that VFP "raises" -- unwitnessed: vfpfunc.prg wrapped SQRT in ABS to avoid
*  the error. The user decided (2026-11-14) the port raises as VFP does. This
*  script witnesses what that means before it is designed:
*
*    [const]     the error number and message, or the value, of each probe
*    [null]      whether a .NULL. operand raises or answers .NULL.
*    [guard]     whether IIF, AND and OR evaluate the operand they do not
*                need -- the port's evaluator is postfix and evaluates every
*                operand
*    [records]   each probe over a cursor whose values hit the cases, then
*    [count]     COUNT FOR and
*    [index]     INDEX ON over it
*    [null-band] each of the thirteen functions VFP has and CodeBase does not
*                (ADR-43) with a typed .NULL. in each argument position: the
*                port dropped the marking in all of them
*
*  It writes VFPBAND.vfp.dump.txt and nothing else; the cursors are temporary.
*
*      DO vfpband.prg [WITH "D:\some\where\out"]
*  or, unattended, from the host:  run-in-vm.ps1 -Script vfpband.prg -TimeoutSeconds 120
*===========================================================================

LPARAMETERS tcOutDir

LOCAL lcOut, lcSelf, lni, lnCount
PRIVATE pnH

SET TALK OFF
SET SAFETY OFF
SET EXCLUSIVE ON
SET DELETED OFF
SET CPDIALOG OFF
CLOSE TABLES ALL

SET COLLATE TO "MACHINE"
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

pnH = FCREATE(m.lcOut + "VFPBAND.vfp.dump.txt")
IF m.pnH < 0
   ERROR "cannot create VFPBAND.vfp.dump.txt"
ENDIF

DO Put WITH "# CodeBase.NET VFP oracle: VFP's own functions where they have no ordinary answer"
DO Put WITH "# Written by test-files-generator/vfp/vfpband.prg in Visual FoxPro 9."
DO Put WITH "# Do not edit by hand; re-run the script in the VM instead."
DO Put WITH "producer     " + '"' + VERSION(1) + '"'
DO Put WITH "codePage     " + TRANSFORM(CPCURRENT())
DO Put WITH "null         " + SET("NULL")
DO Put WITH "decimals     " + TRANSFORM(SET("DECIMALS"))
DO Put WITH "# A value is STR(x, 25, 12) and VARTYPE; an error is its number and message."
DO Put WITH ""

*-- [const] ----------------------------------------------------------------
DO Put WITH "[const]"
DO Probe WITH "MOD(5,3)", "control"
DO Probe WITH "MOD(5,0)", ""
DO Probe WITH "MOD(0,0)", ""
DO Probe WITH "MOD(-5,0)", ""
DO Probe WITH "MOD(5.5,0)", ""
DO Probe WITH "MOD(5,0.0)", ""
DO Probe WITH "MOD(5,-0)", "negative zero, if the literal is one"
DO Probe WITH "MOD(5,1E-300)", "tiny but not zero"
DO Probe WITH "5 % 0", "the operator form"
DO Probe WITH "SQRT(4)", "control"
DO Probe WITH "SQRT(-1)", ""
DO Probe WITH "SQRT(-0.5)", ""
DO Probe WITH "SQRT(-1E-300)", "tiny negative"
DO Probe WITH "SQRT(-0)", "negative zero: -0 < 0 is false"
DO Probe WITH "ATN2(SQRT(-0),-1)", "pi if SQRT(-0) is +0, -pi if -0"
DO Probe WITH "ATN2(-0,-1)", "control for the line above"
DO Probe WITH "1/0", "provenance for ADR-50, which kept CodeBase's 0"
DO Probe WITH "0/0", "provenance for ADR-50"
DO Put WITH ""

*-- [null] -----------------------------------------------------------------
DO Put WITH "[null]"
DO Probe WITH "MOD(.NULL.,3)", ""
DO Probe WITH "MOD(5,.NULL.)", ""
DO Probe WITH "MOD(.NULL.,0)", "null dividend, zero divisor"
DO Probe WITH "SQRT(.NULL.)", ""
DO Put WITH ""

*-- [guard] ----------------------------------------------------------------
DO Put WITH "[guard]"
DO Probe WITH "IIF(.T., 1, SQRT(-1))", "unselected branch raises?"
DO Probe WITH "IIF(.F., SQRT(-1), 1)", "unselected branch raises?"
DO Probe WITH "IIF(.T., SQRT(-1), 1)", "selected branch"
DO Probe WITH "IIF(.F., 1, MOD(5,0))", "selected branch"
DO Probe WITH ".F. AND SQRT(-1) > 0", "AND short-circuits?"
DO Probe WITH "SQRT(-1) > 0 AND .F.", "left operand raises first?"
DO Probe WITH ".T. OR SQRT(-1) > 0", "OR short-circuits?"
DO Probe WITH "SQRT(-1) > 0 OR .T.", "left operand raises first?"
DO Probe WITH ".T. AND MOD(5,0) = 0", "AND evaluates the right operand"
DO Probe WITH "NOT (.F. AND SQRT(-1) > 0)", "short-circuit under NOT"
DO Probe WITH "MIN(1, SQRT(-1))", "no laziness expected"
DO Put WITH ""

*-- [records] --------------------------------------------------------------
*-- One numeric field, nullable, holding the cases in record order:
*-- 1: 4   2: -4   3: 0   4: .NULL.   5: 9
CREATE CURSOR VFPMATH (K_NUM N(8,2) NULL)
INSERT INTO VFPMATH VALUES (4)
INSERT INTO VFPMATH VALUES (-4)
INSERT INTO VFPMATH VALUES (0)
INSERT INTO VFPMATH VALUES (.NULL.)
INSERT INTO VFPMATH VALUES (9)

DO Put WITH "[records]"
DO Put WITH "# K_NUM by record: 1: 4  2: -4  3: 0  4: .NULL.  5: 9"
FOR lni = 1 TO RECCOUNT("VFPMATH")
   GO m.lni IN VFPMATH
   DO Probe WITH "SQRT(K_NUM)", "record " + TRANSFORM(m.lni)
   DO Probe WITH "MOD(10,K_NUM)", "record " + TRANSFORM(m.lni)
ENDFOR
DO Put WITH ""

DO Put WITH "[count]"
DO CountFor WITH "SQRT(K_NUM) > 1"
DO CountFor WITH "MOD(10,K_NUM) = 0"
DO CountFor WITH "K_NUM >= 0 AND SQRT(K_NUM) > 1"
DO CountFor WITH "K_NUM <> 0 AND MOD(10,K_NUM) = 0"
DO CountFor WITH "IIF(K_NUM < 0, 0, SQRT(K_NUM)) > 1"
DO Put WITH ""

DO Put WITH "[index]"
DO IndexOn WITH "SQRT(K_NUM)", "T_SQRT"
DO IndexOn WITH "MOD(10,K_NUM)", "T_MOD"
DO IndexOn WITH "IIF(K_NUM < 0, 0, SQRT(K_NUM))", "T_GUARD"
USE IN VFPMATH
DO Put WITH ""

*-- [null-band] ------------------------------------------------------------
*-- One record, every field .NULL.: a typed null of each kind the thirteen
*-- functions take, so each argument position can be probed on its own.
CREATE CURSOR VFPNULLS (N_ N(8,2) NULL, I_ I NULL, C_ C(10) NULL, L_ L NULL)
INSERT INTO VFPNULLS VALUES (.NULL., .NULL., .NULL., .NULL.)
GO 1 IN VFPNULLS

DO Put WITH "[null-band]"
DO Put WITH "# N_ N(8,2), I_ I, C_ C(10), L_ L -- all .NULL."
DO Probe WITH "ABS(N_)", ""
DO Probe WITH "INT(N_)", ""
DO Probe WITH "ROUND(N_,2)", ""
DO Probe WITH "ROUND(5.25,I_)", "null places"
DO Probe WITH "MOD(N_,3)", ""
DO Probe WITH "MOD(5,N_)", ""
DO Probe WITH "MOD(N_,0)", "null dividend, zero divisor"
DO Probe WITH "MIN(N_,1)", ""
DO Probe WITH "MIN(1,N_)", ""
DO Probe WITH "MIN(1,2,N_)", "variadic, null last"
DO Probe WITH "MAX(N_,1)", ""
DO Probe WITH "MAX(1,N_)", ""
DO Probe WITH "MIN(C_,'a')", "the string overload"
DO Probe WITH "MAX('a',C_)", "the string overload"
DO Probe WITH "SQRT(N_)", ""
DO Probe WITH "SIGN(N_)", ""
DO Probe WITH "LEN(C_)", ""
DO Probe WITH "AT(C_,'abc')", ""
DO Probe WITH "AT('a',C_)", ""
DO Probe WITH "AT('a','abc',I_)", "null occurrence"
DO Probe WITH "ASC(C_)", ""
DO Probe WITH "LOWER(C_)", ""
DO Probe WITH "REPLICATE(C_,2)", ""
DO Probe WITH "REPLICATE('a',I_)", "null count"
DO Probe WITH "IIF(L_,1,2)", "control: a null condition"
DO Probe WITH "ABS(N_) = 0", "the wrong-record-set case: .NULL. or .F.?"
USE IN VFPNULLS
DO Put WITH ""

*-- [table] ----------------------------------------------------------------
*-- VFPBAND.DBF + .CDX, the oracle in binary: tags over the band's functions
*-- on nullable fields, so the key VFP stores for a .NULL. result -- and for
*-- every ordinary one beside it -- can be rebuilt by the port record by
*-- record. SQRT is over ABS because a negative raises (see [index]).
LOCAL lcDbf
lcDbf = m.lcOut + "VFPBAND.DBF"
CREATE TABLE (m.lcDbf) FREE (ID I, N_ N(8,2) NULL, C_ C(10) NULL)
INSERT INTO VFPBAND VALUES (1, 4, "Abc")
INSERT INTO VFPBAND VALUES (2, -2.5, .NULL.)
INSERT INTO VFPBAND VALUES (3, .NULL., "xbYz")
INSERT INTO VFPBAND VALUES (4, 0, "")
INSERT INTO VFPBAND VALUES (5, .NULL., .NULL.)
INSERT INTO VFPBAND VALUES (6, 7.75, "bob")

INDEX ON ABS(N_) TAG T_ABS
INDEX ON INT(N_) TAG T_INT
INDEX ON ROUND(N_,0) TAG T_ROUND
INDEX ON MOD(N_,3) TAG T_MOD
INDEX ON MIN(N_,1) TAG T_MIN
INDEX ON MAX(N_,1) TAG T_MAX
INDEX ON SQRT(ABS(N_)) TAG T_SQRT
INDEX ON SIGN(N_) TAG T_SIGN
INDEX ON LEN(C_) TAG T_LEN
INDEX ON AT("b",C_) TAG T_AT
INDEX ON ASC(C_) TAG T_ASC
INDEX ON LOWER(C_) TAG T_LOWER
INDEX ON REPLICATE(C_,2) TAG T_REPL

DO Put WITH "[table]"
DO Put WITH "# VFPBAND.DBF and its .cdx are the oracle; this is provenance."
DO Put WITH "# ID: N_, C_ -- 1: 4, Abc  2: -2.5, .NULL.  3: .NULL., xbYz  4: 0, blank  5: .NULL., .NULL.  6: 7.75, bob"
FOR lni = 1 TO TAGCOUNT()
   DO Put WITH "  " + PADR(TAG(m.lni), 10) + " expr=" + '"' + KEY(m.lni) + '"' + ;
      " keyType='" + TYPE(KEY(m.lni)) + "'"
ENDFOR
USE IN VFPBAND
= FreezeStamp(m.lcDbf)

= FCLOSE(m.pnH)
? "done"
RETURN

*===========================================================================
*  Helpers
*===========================================================================

*-- One expression: its value and type, or the error it raises.
PROCEDURE Probe(tcExpr, tcNote)
   LOCAL luVal, lcShown, loEx
   TRY
      luVal = EVALUATE(m.tcExpr)
      lcShown = "-> " + Shown(m.luVal)
   CATCH TO loEx
      lcShown = "raises " + TRANSFORM(loEx.ErrorNo) + " " + loEx.Message
   ENDTRY
   DO Put WITH "  " + PADR(m.tcExpr, 32) + " " + m.lcShown + ;
      IIF(EMPTY(m.tcNote), "", "   ; " + m.tcNote)
ENDPROC


*-- COUNT FOR over the whole cursor: the count, or the error and the record
*-- it stopped on.
PROCEDURE CountFor(tcFor)
   LOCAL lnCount, lcShown, loEx
   lnCount = -1
   SELECT VFPMATH
   TRY
      COUNT FOR &tcFor TO lnCount
      lcShown = "-> " + TRANSFORM(m.lnCount)
   CATCH TO loEx
      lcShown = "raises " + TRANSFORM(loEx.ErrorNo) + " " + loEx.Message + ;
         " at record " + TRANSFORM(RECNO("VFPMATH"))
   ENDTRY
   DO Put WITH "  " + PADR(m.tcFor, 40) + " " + m.lcShown
ENDPROC


*-- INDEX ON: built or the error, then what the tag holds.
PROCEDURE IndexOn(tcKey, tcTag)
   LOCAL lcShown, loEx, lni, llHas
   SELECT VFPMATH
   TRY
      INDEX ON &tcKey TAG (m.tcTag)
      lcShown = "built"
   CATCH TO loEx
      lcShown = "raises " + TRANSFORM(loEx.ErrorNo) + " " + loEx.Message + ;
         " at record " + TRANSFORM(RECNO("VFPMATH"))
   ENDTRY
   llHas = .F.
   FOR lni = 1 TO TAGCOUNT()
      IF UPPER(TAG(m.lni)) == UPPER(m.tcTag)
         llHas = .T.
      ENDIF
   ENDFOR
   DO Put WITH "  " + PADR(m.tcTag, 8) + " " + PADR(m.tcKey, 32) + " " + m.lcShown + ;
      "; tag " + IIF(m.llHas, "present", "absent")
   SET ORDER TO 0
ENDPROC


FUNCTION Shown(tuVal)
   DO CASE
   CASE ISNULL(m.tuVal)
      RETURN ".NULL. (" + VARTYPE(m.tuVal, .T.) + ")"
   CASE VARTYPE(m.tuVal) $ "NFBIY"
      RETURN ALLTRIM(STR(m.tuVal, 25, 12)) + " (" + VARTYPE(m.tuVal) + ")"
   CASE VARTYPE(m.tuVal) = "L"
      RETURN IIF(m.tuVal, ".T.", ".F.") + " (L)"
   OTHERWISE
      RETURN TRANSFORM(m.tuVal) + " (" + VARTYPE(m.tuVal) + ")"
   ENDCASE
ENDFUNC


*-- LF line endings, matching the C generator's dumps.
PROCEDURE Put(tcText)
   = FWRITE(m.pnH, m.tcText + CHR(10))
ENDPROC


*-- Freeze the DBF header date stamp to 26-01-01, as vfpfunc.prg and util.cpp
*-- do (ADR-07).
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
