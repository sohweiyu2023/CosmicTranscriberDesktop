@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if errorlevel 1 exit /b 1
set "EXE=%~dp0src\CosmicTranscriber\bin\Release\net10.0-windows\CosmicTranscriber.exe"
set "LAUNCHER=%~dp0scripts\DiagnosticLauncher\bin\Release\net10.0\DiagnosticLauncher.exe"
if not exist "%LAUNCHER%" goto :missing_build
rem Keep the launcher alive; no rebuild, host trace, script host, or policy change.
"%LAUNCHER%" --exe "%EXE%"
exit /b %errorlevel%
:missing_build
echo Build the source solution separately before running diagnostics. Existing bytes were not changed.
exit /b 1
