@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if errorlevel 1 goto :root_error

call "%~dp0BUILD.cmd"
if errorlevel 1 exit /b %errorlevel%

set "EXE=%~dp0src\CosmicTranscriber\bin\Release\net10.0-windows10.0.19041.0\CosmicTranscriber.exe"
start "Cosmic Transcriber" "%EXE%"
if errorlevel 1 goto :launch_failed
exit /b 0

:root_error
echo ERROR: Could not switch to the source directory.
exit /b 1

:launch_failed
echo ERROR: The application could not be launched.
exit /b 1
