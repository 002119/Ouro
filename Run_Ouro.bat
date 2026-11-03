@echo off
setlocal
cd /d "%~dp0"
title Ouro Replay Engine - Skyx Gaming

echo =======================================================
echo          OURO REPLAY ENGINE - SKYX GAMING
echo =======================================================
echo.

echo [1/2] Compilando la aplicacion (.NET 8 Windows x64)...
call dotnet build "Ouro.csproj" -c Debug
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
echo [2/2] Lanzando Ouro Replay Engine...
start "" "%~dp0bin\Debug\net8.0-windows10.0.22621.0\Ouro.exe"
echo.
echo Ouro se ha iniciado correctamente.
timeout /t 2 >nul
