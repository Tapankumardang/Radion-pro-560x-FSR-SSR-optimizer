@echo off
title GPU Optimization - Radeon Pro 560X
echo Requesting Administrator privileges to apply system and GPU optimizations...

:: Check for administrative rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Elevating privileges via Windows UAC...
    powershell -Command "Start-Process powershell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"\"%~dp0Optimize-GPU.ps1\"\"' -Verb RunAs"
    exit /b
)

:: Already elevated
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Optimize-GPU.ps1"
echo.
pause
