@echo off
rem Roda King's Valley II (build + execucao). Parametros extras vao para o jogo.
rem Exemplos de diagnostico (opcionais), antes de chamar:
rem   set KINGSVALLEY_START_SCENE=level
rem   set KINGSVALLEY_LEVEL=18
cd /d "%~dp0"
dotnet run --project src\KingsValley.Desktop %*
if errorlevel 1 pause
