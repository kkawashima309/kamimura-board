@echo off
REM ============================================================
REM .NET 10 SDK Environment Diagnostic Tool
REM ============================================================
REM Run this BEFORE build-installer.bat to verify .NET 10 SDK.
REM ============================================================

chcp 65001 > nul
title .NET SDK Check

echo.
echo ============================================================
echo   .NET 10 SDK Environment Check
echo ============================================================
echo.

echo [1] Checking 'dotnet' command...
where dotnet
if errorlevel 1 (
    echo.
    echo Result: dotnet command NOT FOUND
    echo.
    echo Please install .NET 10 SDK from:
    echo   https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    pause
    exit /b 1
)
echo.

echo [2] dotnet --version:
dotnet --version
echo.

echo [3] Installed SDKs (dotnet --list-sdks):
dotnet --list-sdks
echo.

echo [4] Installed Runtimes (dotnet --list-runtimes):
dotnet --list-runtimes
echo.

echo [5] Looking for .NET 10.x SDK...
set "FOUND10="
for /f "tokens=1" %%v in ('dotnet --list-sdks 2^>nul') do (
    echo %%v | findstr /R /C:"^10\." >nul
    if not errorlevel 1 (
        set "FOUND10=1"
        echo   Found: %%v
    )
)

echo.
if defined FOUND10 (
    echo ============================================================
    echo   RESULT: .NET 10 SDK is OK. You can run build-installer.bat
    echo ============================================================
) else (
    echo ============================================================
    echo   RESULT: .NET 10 SDK NOT FOUND
    echo ============================================================
    echo.
    echo You may have other versions of .NET installed, but not 10.x.
    echo Please install .NET 10 SDK from:
    echo.
    echo   https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    echo Choose ".NET SDK x64" Installer.
)
echo.
pause
