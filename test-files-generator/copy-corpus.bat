@echo off
REM ===========================================================================
REM  copy-corpus.bat — publishes generated test files into the checked-in
REM  corpus that the C# port is tested against.
REM
REM      copy-corpus.bat            copy bin\out  -> ..\net\corpus
REM      copy-corpus.bat <srcdir>   copy <srcdir> -> ..\net\corpus
REM
REM  Run bin\testgen.exe first. Existing corpus files are overwritten; nothing
REM  is deleted, so a renamed case leaves its old files behind — check
REM  `git status` after copying.
REM ===========================================================================
setlocal

set "ROOT=%~dp0"
set "SRC=%~1"
if "%SRC%"=="" set "SRC=%ROOT%bin\out"
set "DEST=%ROOT%..\net\corpus"

if not exist "%SRC%\*.DBF" (
   echo [copy-corpus] ERROR: no .DBF files in %SRC%
   echo [copy-corpus] Run bin\testgen.exe first.
   exit /b 1
)

if not exist "%DEST%" mkdir "%DEST%"

echo [copy-corpus] %SRC%
echo [copy-corpus]   -^> %DEST%

for %%X in (DBF fpt dump.txt) do (
   if exist "%SRC%\*.%%X" (
      copy /y "%SRC%\*.%%X" "%DEST%\" >nul || (
         echo [copy-corpus] ERROR: copy of *.%%X failed
         exit /b 1
      )
      for %%F in ("%SRC%\*.%%X") do echo [copy-corpus]      %%~nxF
   )
)

echo [copy-corpus] OK
endlocal
exit /b 0
