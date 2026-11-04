@echo off
title Subir Ouro a GitHub
if exist "%~dp0.git" (
    cd /d "%~dp0"
) else if exist "D:\Proyectos juegos-apps\Ouro\.git" (
    cd /d "D:\Proyectos juegos-apps\Ouro"
) else (
    cd /d "C:\Proyectos juegos-apps\Ouro"
)

echo ========================================================
echo        SUBIENDO PROYECTO OURO A GITHUB
echo ========================================================
echo Ubicacion: %CD%
echo Conectando con https://github.com/002119/Ouro.git ...
echo.
git push -u origin main
echo.
if %ERRORLEVEL% EQU 0 (
    echo [EXITO] Proyecto subido correctamente a GitHub.
) else (
    echo [AVISO] Si te pidio iniciar sesion, completa el login en la ventana que se abrio.
)
echo.
pause
