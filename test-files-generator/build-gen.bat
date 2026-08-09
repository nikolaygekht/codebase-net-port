@echo off
REM ===========================================================================
REM  build-gen.bat — compiles the test-file generator and links it against the
REM  static CodeBase library produced by build-lib.bat.
REM
REM    build-gen.bat          build
REM    build-gen.bat clean    delete generator objects and exe, then rebuild
REM
REM  Output:  obj\gen\*.obj    intermediate objects and per-file compile logs
REM           bin\testgen.exe  the generator
REM
REM  Every .cpp in src\ is compiled, so adding a case file needs no edit here.
REM  Run build-lib.bat first (or this script will tell you to).
REM ===========================================================================
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "SRCDIR=%ROOT%..\original\source"
set "CFGDIR=%ROOT%src"
set "OBJDIR=%ROOT%obj\gen"
set "BINDIR=%ROOT%bin"
set "LIBOUT=%ROOT%obj\codebase.lib"

if /i "%~1"=="clean" (
   echo [build-gen] cleaning...
   if exist "%OBJDIR%" rd /s /q "%OBJDIR%"
   if exist "%BINDIR%\testgen.exe" del /q "%BINDIR%\testgen.exe"
)

if not exist "%LIBOUT%" (
   echo [build-gen] ERROR: %LIBOUT% not found.
   echo [build-gen] Run build-lib.bat first.
   exit /b 1
)

call "%ROOT%config.bat" || exit /b 1
echo [build-gen] toolchain: %CB_VCVARS%
call "%CB_VCVARS%" >nul 2>&1
if errorlevel 1 ( echo [build-gen] ERROR: vcvars failed & exit /b 1 )

if not exist "%OBJDIR%" mkdir "%OBJDIR%"
if not exist "%BINDIR%" mkdir "%BINDIR%"

echo [build-gen] compiling generator...
set "OBJS="
for %%f in ("%CFGDIR%\*.cpp") do (
   REM  /Tp  compile as C++ — the library requires it (see README).
   REM  /FI  force-include our build configuration ahead of D4all.h.
   cl /c /nologo /W3 /O2 /D_CRT_SECURE_NO_WARNINGS /DWIN32 ^
      /FI"%CFGDIR%\cb-config.h" /I"%SRCDIR%" /I"%CFGDIR%" ^
      /Tp"%%f" /Fo"%OBJDIR%\%%~nf.obj" >"%OBJDIR%\%%~nf.log" 2>&1
   if errorlevel 1 (
      echo [build-gen] ERROR: compile failed for %%~nxf
      findstr /i "error" "%OBJDIR%\%%~nf.log"
      exit /b 1
   )
   echo [build-gen]   %%~nxf
   set "OBJS=!OBJS! "%OBJDIR%\%%~nf.obj""
)

if not defined OBJS (
   echo [build-gen] ERROR: no .cpp files found in %CFGDIR%
   exit /b 1
)

echo [build-gen] linking -^> bin\testgen.exe
link /nologo /OUT:"%BINDIR%\testgen.exe" !OBJS! "%LIBOUT%" ^
     user32.lib advapi32.lib >"%OBJDIR%\link.log" 2>&1
if errorlevel 1 (
   echo [build-gen] ERROR: link failed
   findstr /i "error" "%OBJDIR%\link.log"
   exit /b 1
)

echo [build-gen] OK
echo [build-gen] usage: bin\testgen.exe [output-dir]     ^(default: bin\out^)
endlocal
exit /b 0
