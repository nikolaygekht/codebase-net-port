@echo off
REM ===========================================================================
REM  build-lib.bat — compiles the original CodeBase C library (read-only
REM  reference in original/source) into a static library we can link against.
REM
REM    build-lib.bat          incremental (skips objects that already exist)
REM    build-lib.bat clean    delete all objects and the library, then rebuild
REM
REM  Output:  obj\codebase\*.obj   intermediate objects and per-file logs
REM           obj\codebase.lib     the static library
REM
REM  Nothing under original/source is modified. Build switches come from
REM  src\cb-config.h, force-included ahead of the shipped D4all.h.
REM ===========================================================================
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "SRCDIR=%ROOT%..\original\source"
set "CFGDIR=%ROOT%src"
set "OBJDIR=%ROOT%obj\codebase"
set "LIBOUT=%ROOT%obj\codebase.lib"

if not exist "%SRCDIR%\D4all.h" (
   echo [build-lib] ERROR: cannot find the CodeBase sources at:
   echo [build-lib]   %SRCDIR%
   exit /b 1
)

if /i "%~1"=="clean" (
   echo [build-lib] cleaning...
   if exist "%OBJDIR%" rd /s /q "%OBJDIR%"
   if exist "%LIBOUT%" del /q "%LIBOUT%"
)

call "%ROOT%config.bat" || exit /b 1
echo [build-lib] toolchain: %CB_VCVARS%
call "%CB_VCVARS%" >nul 2>&1
if errorlevel 1 ( echo [build-lib] ERROR: vcvars failed & exit /b 1 )

if not exist "%OBJDIR%" mkdir "%OBJDIR%"

echo [build-lib] compiling (x86, C++)...
set /a BUILT=0, SKIPPED=0, EXCLUDED=0, FAILED=0

for %%f in ("%SRCDIR%\*.c") do (
   set "NAME=%%~nxf"
   set "EXCLUDE="

   REM --- Included directly by other translation units, never compiled alone.
   REM     Each says so in a comment at the top of the file. Compiling them
   REM     separately fails, or duplicates symbols at link time.
   if /i "!NAME!"=="c4long.c"   set "EXCLUDE=included by other sources"
   if /i "!NAME!"=="coll4arr.c" set "EXCLUDE=included by i4conv.c"
   if /i "!NAME!"=="e4str2.c"   set "EXCLUDE=included by e4error.c"

   REM --- OLE-DB only: unconditionally includes oledb5.hpp (which needs
   REM     defs5.hpp, absent from this drop) and its whole body is guarded by
   REM     #ifdef OLEDB5BUILD. Out of scope per PORTING-PLAN.md section 2.2.
   if /i "!NAME!"=="m4mem2.c"   set "EXCLUDE=OLE-DB only, out of scope"

   if defined EXCLUDE (
      set /a EXCLUDED+=1
   ) else if exist "%OBJDIR%\%%~nf.obj" (
      set /a SKIPPED+=1
   ) else (
      REM  /TP  the library REQUIRES C++: d4declar.h declares default arguments
      REM       under #ifdef __cplusplus and call sites depend on them.
      REM  /FI  force-include our build configuration ahead of D4all.h.
      cl /c /TP /nologo /W0 /O2 /D_CRT_SECURE_NO_WARNINGS /DWIN32 ^
         /FI"%CFGDIR%\cb-config.h" /I"%SRCDIR%" /I"%CFGDIR%" ^
         "%%f" /Fo"%OBJDIR%\%%~nf.obj" >"%OBJDIR%\%%~nf.log" 2>&1
      if errorlevel 1 (
         set /a FAILED+=1
         echo [build-lib]   FAILED %%~nxf  ^(see obj\codebase\%%~nf.log^)
      ) else (
         set /a BUILT+=1
      )
   )
)

echo [build-lib] compiled=!BUILT!  cached=!SKIPPED!  excluded=!EXCLUDED!  failed=!FAILED!
if !FAILED! gtr 0 exit /b 1

echo [build-lib] archiving -^> obj\codebase.lib
lib /nologo /OUT:"%LIBOUT%" "%OBJDIR%\*.obj" >"%ROOT%obj\lib.log" 2>&1
if errorlevel 1 (
   echo [build-lib] ERROR: lib failed, see obj\lib.log
   exit /b 1
)

echo [build-lib] OK
endlocal
exit /b 0
