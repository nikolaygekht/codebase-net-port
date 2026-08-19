@echo off
REM ===========================================================================
REM  run.bat -- build both sides, generate the table and query sets, then run
REM  all three configurations three times, interleaved, into out\results.txt.
REM
REM    run.bat            9 reps per scenario (the default)
REM    run.bat 15         15 reps
REM
REM  Prerequisite: test-files-generator\build-lib.bat (obj\codebase.lib).
REM ===========================================================================
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "REPS=%~1"
if "%REPS%"=="" set "REPS=9"

call "%ROOT%build-c.bat" || exit /b 1

echo [run] building the C# side...
dotnet build "%ROOT%src-cs\Bench\Bench.csproj" -c Release --artifacts-path "%ROOT%artifacts" -v q --nologo || exit /b 1

if not exist "%ROOT%out\PERF10K.DBF" (
   "%ROOT%bin\perf.exe" gen "%ROOT%out" || exit /b 1
)

set "CS=%ROOT%artifacts\bin\Bench\release\bench.exe"
set "OUT=%ROOT%out\results.txt"

echo [run] %REPS% reps per scenario, three interleaved rounds -^> out\results.txt
> "%OUT%" echo performance-1-experiment -- raw results

for /l %%r in (1,1,3) do (
   >> "%OUT%" echo.
   >> "%OUT%" echo ########## round %%r
   >> "%OUT%" echo --- c ^(plain: no library block cache^) ---
   "%ROOT%bin\perf.exe" bench "%ROOT%out" %REPS% plain >> "%OUT%" 2>&1
   >> "%OUT%" echo --- c ^(opt: code4optStart, block cache on^) ---
   "%ROOT%bin\perf.exe" bench "%ROOT%out" %REPS% opt >> "%OUT%" 2>&1
   >> "%OUT%" echo --- cs ^(CodeBase.Net^) ---
   "%CS%" "%ROOT%out" %REPS% >> "%OUT%" 2>&1
)

type "%OUT%"
endlocal
exit /b 0
