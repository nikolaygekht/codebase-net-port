@echo off
REM ===========================================================================
REM  config.bat — locates the MSVC toolchain used to build the reference
REM  CodeBase library and the test-file generator.
REM
REM  Sets CB_VCVARS to the full path of a vcvars batch file.
REM
REM  IMPORTANT: this project builds 32-bit (x86). The original C library writes
REM  native structs to disk, and only the x86 build is byte-correct and
REM  warning-clean; the x64 (S464BIT) path has unresolved rot in the 64-bit
REM  file-offset layer. Point CB_VCVARS at vcvars32.bat, not vcvars64.bat.
REM
REM  To override without editing this file:
REM      set CB_VCVARS=C:\Path\To\VC\Auxiliary\Build\vcvars32.bat
REM      build-lib.bat
REM ===========================================================================

if defined CB_VCVARS goto :validate

REM ---- Candidate locations. Add yours here; first match wins. --------------
call :try "e:\msdev\dev\VC\Auxiliary\Build\vcvars32.bat"
call :try "%ProgramFiles%\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars32.bat"
call :try "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars32.bat"
call :try "%ProgramFiles%\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars32.bat"
call :try "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars32.bat"
call :try "%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Professional\VC\Auxiliary\Build\vcvars32.bat"
if defined CB_VCVARS goto :validate

REM ---- Last resort: ask vswhere for the newest install ---------------------
set "CB_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%CB_VSWHERE%" (
   for /f "usebackq tokens=*" %%i in (`"%CB_VSWHERE%" -latest -products * -property installationPath 2^>nul`) do (
      call :try "%%i\VC\Auxiliary\Build\vcvars32.bat"
   )
)

:validate
if not defined CB_VCVARS (
   echo [config] ERROR: could not locate vcvars32.bat.
   echo [config] Set CB_VCVARS to its full path, or add your path to config.bat.
   exit /b 1
)
if not exist "%CB_VCVARS%" (
   echo [config] ERROR: CB_VCVARS points at a file that does not exist:
   echo [config]   %CB_VCVARS%
   exit /b 1
)
exit /b 0

:try
if defined CB_VCVARS exit /b 0
if exist %1 set "CB_VCVARS=%~1"
exit /b 0
