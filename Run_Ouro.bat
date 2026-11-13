@echo off
setlocal
cd /d "%~dp0"
title Ouro Replay Engine - SKYX Labs

echo =======================================================
echo          OURO REPLAY ENGINE - SKYX LABS
echo =======================================================
echo.

echo [1/2] Compilando la aplicacion (.NET 8 Windows x64)...
call dotnet build "Ouro.csproj" -c Debug -p:IntermediateOutputPath=obj/Launcher/ -p:OutputPath=bin/Launcher/
if %errorlevel% neq 0 (
    echo.
    echo =======================================================
    echo [ERROR] La compilacion fallo. Revisa los errores arriba.
    echo =======================================================
    echo.
    pause
    exit /b %errorlevel%
)

echo.
if /I "%~1"=="--build-only" exit /b 0
echo [2/2] Lanzando Ouro Replay Engine...
start "" "%~dp0bin\Launcher\Ouro.exe"
echo.
echo Ouro se ha iniciado correctamente.
timeout /t 2 >nul
