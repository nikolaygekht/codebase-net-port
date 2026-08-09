@echo off
REM ===========================================================================
REM  sonar.bat — build, run the tests with coverage, and publish a SonarQube
REM  analysis of CodeBase.NET. Windows only.
REM
REM  Prerequisites:
REM    - .NET SDK and a JRE (the scanner needs one)
REM    - dotnet tool install --global dotnet-sonarscanner
REM    - .sonar.config.bat next to this script (gitignored), setting:
REM          set sonar_url=http://host:9010
REM          set sonar_token=squ_xxxxxxxx
REM    - every test project referencing coverlet.collector and
REM      LiquidTestReports.Markdown — see FOR-DEVELOPERS.md
REM
REM  Analysis settings — exclusions, coverage and test-report paths — live in
REM  SonarQube.Analysis.xml, not here. Only the project identity and the
REM  secrets are passed on the command line.
REM ===========================================================================
setlocal

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%net\CodeBase.Net.sln"
set "PROJECT_KEY=CodeBase.Net"
set "PROJECT_NAME=CodeBase.NET"
set "RESULTS=%ROOT%TestResults"

if not exist "%ROOT%.sonar.config.bat" (
   echo [sonar] ERROR: .sonar.config.bat not found next to this script.
   echo [sonar] Create it ^(it is gitignored^) with:
   echo [sonar]     set sonar_url=http://host:9010
   echo [sonar]     set sonar_token=squ_xxxxxxxx
   exit /b 1
)
call "%ROOT%.sonar.config.bat"

if not defined sonar_url   ( echo [sonar] ERROR: sonar_url not set by .sonar.config.bat & exit /b 1 )
if not defined sonar_token ( echo [sonar] ERROR: sonar_token not set by .sonar.config.bat & exit /b 1 )

if not exist "%SOLUTION%" (
   echo [sonar] ERROR: %SOLUTION% not found.
   echo [sonar] The .NET solution does not exist yet - see STATE.md.
   exit /b 1
)

REM Stale results would be picked up by the report globs in the settings file.
if exist "%RESULTS%" rd /s /q "%RESULTS%"

echo [sonar] begin
dotnet sonarscanner begin ^
   /k:"%PROJECT_KEY%" /n:"%PROJECT_NAME%" ^
   /d:sonar.host.url="%sonar_url%" ^
   /d:sonar.token="%sonar_token%" ^
   /s:"%ROOT%SonarQube.Analysis.xml" || exit /b 1

echo [sonar] build
dotnet build "%SOLUTION%" --configuration Debug || exit /b 1

echo [sonar] test + coverage
dotnet test "%SOLUTION%" --configuration Debug --no-build ^
   --collect "XPlat Code Coverage" ^
   --logger trx ^
   --logger "liquid.md;LogFileName=test-report.md" ^
   --results-directory "%RESULTS%" ^
   -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
set "TEST_RC=%ERRORLEVEL%"

REM Publish even when tests failed - the .trx carries the failures into Sonar.
echo [sonar] end
dotnet sonarscanner end /d:sonar.token="%sonar_token%" || exit /b 1

if not "%TEST_RC%"=="0" (
   echo [sonar] tests FAILED ^(exit %TEST_RC%^) - the analysis was still published
   exit /b %TEST_RC%
)

echo [sonar] OK
exit /b 0
