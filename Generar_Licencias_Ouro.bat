@echo off
title Ouro Replay - Generador Oficial de Licencias
set "ROOT=%~dp0"
set "PROJECT=%ROOT%Tools\OuroKeyGen\OuroKeyGen.csproj"
set "OUT=%ROOT%Tools\OuroKeyGen\bin\Release\net8.0-windows"

if not exist "%OUT%\OuroKeyGen.exe" (
    echo Compilando el emisor de licencias...
    dotnet build "%PROJECT%" -c Release --nologo
    if errorlevel 1 (
        echo.
        echo No se pudo compilar el emisor de licencias.
        pause
        exit /b 1
    )
)

cd /d "%OUT%"
cls
OuroKeyGen.exe
pause
