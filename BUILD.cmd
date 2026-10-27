@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if errorlevel 1 goto :root_error

echo ================================================================
echo  Cosmic Transcriber 1.0.27 - .NET 10 build
echo ================================================================

where dotnet >nul 2>&1
if errorlevel 1 goto :missing_sdk

set "DOTNET_VERSION="
for /f "delims=" %%V in ('dotnet --version 2^>nul') do set "DOTNET_VERSION=%%V"
if not defined DOTNET_VERSION goto :version_error

echo Active SDK: %DOTNET_VERSION%
echo %DOTNET_VERSION%| findstr /b /c:"10." >nul
if errorlevel 1 goto :wrong_sdk

echo.
echo [1/3] Restoring packages...
dotnet restore ".\CosmicTranscriber.sln" --nologo
if errorlevel 1 goto :restore_failed

echo.
echo [2/3] Building Release...
dotnet build ".\CosmicTranscriber.sln" -c Release --no-restore --nologo -m:1
if errorlevel 1 goto :build_failed

echo.
echo [3/3] Verifying executable...
set "EXE=src\CosmicTranscriber\bin\Release\net10.0-windows10.0.19041.0\CosmicTranscriber.exe"
if not exist "%EXE%" goto :missing_executable
if not exist "scripts\DiagnosticLauncher\bin\Release\net10.0\DiagnosticLauncher.exe" goto :missing_executable

echo.
echo BUILD PASSED
echo Executable: %CD%\%EXE%
exit /b 0

:root_error
echo ERROR: Could not switch to the source directory.
exit /b 1

:missing_sdk
echo ERROR: The .NET SDK was not found.
echo Install the current .NET 10 SDK, then run this file again.
echo Suggested command: winget install Microsoft.DotNet.SDK.10
exit /b 1

:version_error
echo ERROR: dotnet --version did not return an SDK version.
exit /b 1

:wrong_sdk
echo ERROR: This project requires a .NET 10 SDK.
echo global.json requests SDK 10.0.402 with compatible patch roll-forward.
exit /b 1

:restore_failed
echo.
echo RESTORE FAILED
exit /b 1

:build_failed
echo.
echo BUILD FAILED
exit /b 1

:missing_executable
echo ERROR: Build reported success but %EXE% was not found.
exit /b 1

