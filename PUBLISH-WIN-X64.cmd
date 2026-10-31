@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if errorlevel 1 goto :root_error

rem Source-only licensing draft: do not build or publish a potentially noncompliant bundle.
echo DISTRIBUTION HELD: complete reduced-package inventory, notices, native acceptance and release review.
echo See docs\CORE-ONLY-PACKAGING.md. SDK terms apply if SDK components are reintroduced.
exit /b 1

echo ================================================================
echo  Cosmic Transcriber 1.0.27 - self-contained Windows x64 publish
echo ================================================================

where dotnet >nul 2>&1
if errorlevel 1 goto :missing_sdk

set "OUT=%CD%\publish\win-x64-self-contained"
if not exist "%OUT%" goto :publish
rmdir /s /q "%OUT%"
if errorlevel 1 goto :cleanup_failed

:publish
dotnet publish ".\src\CosmicTranscriber\CosmicTranscriber.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:PublishTrimmed=false ^
  -p:DebugType=None ^
  -p:DebugSymbols=false ^
  -o "%OUT%" ^
  --nologo
if errorlevel 1 goto :publish_failed

if not exist "%OUT%\CosmicTranscriber.exe" goto :missing_executable

echo.
echo PUBLISH PASSED
echo Portable folder: %OUT%
echo The app will create its default transcript folder at: %OUT%\output
echo The clean publish folder will open in File Explorer; the app itself will not be launched.
echo If sharing the portable build, zip or copy this folder before the first app launch.
start "" "%OUT%"
exit /b 0

:root_error
echo ERROR: Could not switch to the source directory.
exit /b 1

:missing_sdk
echo ERROR: Install the .NET 10 SDK first.
echo Suggested command: winget install Microsoft.DotNet.SDK.10
exit /b 1

:cleanup_failed
echo ERROR: Could not remove the previous publish output folder.
exit /b 1

:publish_failed
echo.
echo PUBLISH FAILED
exit /b 1

:missing_executable
echo ERROR: Publish completed without CosmicTranscriber.exe.
exit /b 1
