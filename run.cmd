@echo off
setlocal
rem King's Valley II launcher for Windows.
rem Forwards all arguments to the game (e.g. run.cmd --headless).

cd /d "%~dp0"
dotnet run --project src\KingsValley.Desktop %*
if errorlevel 1 pause
