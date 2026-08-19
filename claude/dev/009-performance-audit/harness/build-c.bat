@echo off
REM ===========================================================================
REM  build-c.bat -- compiles src-c\perf.cpp and links it against the static
REM  CodeBase library the test-file generator already builds.
REM
REM  Prerequisite:  test-files-generator\build-lib.bat  (obj\codebase.lib)
REM
REM  Output:  obj\perf.obj, obj\*.log
REM           bin\perf.exe
REM
REM  x86, /O2, same cb-config.h switches as the generator, so this measures the
REM  same library build the corpus was produced with.
REM ===========================================================================
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "GENDIR=%ROOT%..\..\test-files-generator"
set "SRCDIR=%ROOT%..\..\original\source"
set "CFGDIR=%GENDIR%\src"
set "OBJDIR=%ROOT%obj"
set "BINDIR=%ROOT%bin"
set "LIBOUT=%GENDIR%\obj\codebase.lib"

if /i "%~1"=="clean" (
   if exist "%OBJDIR%" rd /s /q "%OBJDIR%"
   if exist "%BINDIR%" rd /s /q "%BINDIR%"
)

if not exist "%LIBOUT%" (
   echo [build-c] ERROR: %LIBOUT% not found.
   echo [build-c] Run test-files-generator\build-lib.bat first.
   exit /b 1
)

call "%GENDIR%\config.bat" || exit /b 1
echo [build-c] toolchain: %CB_VCVARS%
call "%CB_VCVARS%" >nul 2>&1
if errorlevel 1 ( echo [build-c] ERROR: vcvars failed & exit /b 1 )

if not exist "%OBJDIR%" mkdir "%OBJDIR%"
if not exist "%BINDIR%" mkdir "%BINDIR%"

echo [build-c] compiling perf.cpp...
cl /c /nologo /W3 /O2 /D_CRT_SECURE_NO_WARNINGS /DWIN32 ^
   /FI"%CFGDIR%\cb-config.h" /I"%SRCDIR%" /I"%CFGDIR%" ^
   /Tp"%ROOT%src-c\perf.cpp" /Fo"%OBJDIR%\perf.obj" >"%OBJDIR%\perf.log" 2>&1
if errorlevel 1 (
   echo [build-c] ERROR: compile failed
   findstr /i "error" "%OBJDIR%\perf.log"
   exit /b 1
)

echo [build-c] linking -^> bin\perf.exe
link /nologo /OUT:"%BINDIR%\perf.exe" "%OBJDIR%\perf.obj" "%LIBOUT%" ^
     user32.lib advapi32.lib >"%OBJDIR%\link.log" 2>&1
if errorlevel 1 (
   echo [build-c] ERROR: link failed
   findstr /i "error" "%OBJDIR%\link.log"
   exit /b 1
)

echo [build-c] OK
endlocal
exit /b 0
