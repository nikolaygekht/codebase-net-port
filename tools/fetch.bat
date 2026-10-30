@echo off
REM ===========================================================================
REM  fetch.bat — clone, or update to the latest commit, the two quality tools
REM  from Microsoft's repositories into tools\, and build them. The clones are
REM  gitignored; tools\README.md says how to run them.
REM
REM  Prerequisites: git, and the .NET 8 SDK the tools' global.json asks for.
REM  fetch.sh does the same on Linux and macOS.
REM ===========================================================================
setlocal

cd /d "%~dp0"

call :fetch crap4csharp Crap4CSharp || exit /b 1
call :fetch mutate4csharp Mutate4CSharp || exit /b 1

echo [fetch] done
exit /b 0

:fetch
if exist "%~1\.git" (
   echo [fetch] updating %~1
   git -C "%~1" pull --ff-only || exit /b 1
) else (
   echo [fetch] cloning %~1
   git clone "https://github.com/microsoft/%~1.git" "%~1" || exit /b 1
)
git -C "%~1" log -1 --format="[fetch] %~1 at %%h %%ad %%s" --date=short

REM Built from inside the clone, so its own global.json picks the SDK.
pushd "%~1"
dotnet build "src\%~2\%~2.csproj" -c Release --nologo -v quiet
set "RC=%ERRORLEVEL%"
popd
exit /b %RC%
