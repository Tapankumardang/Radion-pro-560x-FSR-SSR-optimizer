@echo off
title Revert GPU Optimizations
echo Requesting Administrator privileges to restore factory defaults...

:: Check for administrative rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Elevating privileges via Windows UAC...
    powershell -Command "Start-Process powershell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"\"%~dp0Revert-Optimizations.ps1\"\"' -Verb RunAs"
    exit /b
)

:: Already elevated
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Revert-Optimizations.ps1"
echo.
pause
