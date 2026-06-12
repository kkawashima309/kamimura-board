@echo off
REM ============================================================
REM PdfStudio Auto Build and Installer Creation Script
REM Target: .NET 10 (LTS)
REM ============================================================

setlocal enabledelayedexpansion
chcp 65001 > nul
title PdfStudio - Build and Installer

cd /d "%~dp0"

echo.
echo ============================================================
echo   PdfStudio v0.5 - Auto Build Script (.NET 10)
echo ============================================================
echo.

REM ---------- 1. .NET SDK Check ----------
echo [1/6] Checking .NET 10 SDK...
where dotnet >nul 2>&1
if errorlevel 1 goto :NoDotNet

REM Look for .NET 10.x SDK
set "FOUND_SDK="
for /f "tokens=1" %%v in ('dotnet --list-sdks 2^>nul') do (
    echo %%v | findstr /R /C:"^10\." >nul
    if not errorlevel 1 set "FOUND_SDK=1"
)
if not defined FOUND_SDK goto :NoDotNet

for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set "DOTNET_VERSION=%%v"
echo   OK: .NET SDK %DOTNET_VERSION% detected
echo.

REM ---------- 2. WiX v4 Tool Check ----------
echo [2/6] Checking WiX v4 tool...
where wix >nul 2>&1
if errorlevel 1 (
    echo   WiX not found. Installing as global tool...
    dotnet tool install --global wix --version 4.0.5 2>nul
    if errorlevel 1 (
        dotnet tool update --global wix --version 4.0.5 2>nul
    )
    set "PATH=%USERPROFILE%\.dotnet\tools;%PATH%"
)
where wix >nul 2>&1
if errorlevel 1 (
    echo.
    echo ============================================================
    echo   ERROR: WiX is not on PATH
    echo ============================================================
    echo Please add %USERPROFILE%\.dotnet\tools to your PATH,
    echo or close and reopen Command Prompt, then retry.
    echo.
    pause
    exit /b 1
)

echo   Checking WiX extensions...
wix extension list -g 2>nul | findstr /I "WixToolset.UI.wixext" >nul
if errorlevel 1 wix extension add -g WixToolset.UI.wixext/4.0.5

wix extension list -g 2>nul | findstr /I "WixToolset.Util.wixext" >nul
if errorlevel 1 wix extension add -g WixToolset.Util.wixext/4.0.5

echo   OK: WiX ready
echo.

REM ---------- 3. NuGet Restore ----------
echo [3/6] Restoring NuGet packages (may take several minutes on first run)...
dotnet restore PdfStudio.sln --verbosity quiet
if errorlevel 1 goto :RestoreFailed
echo   OK: Restore complete
echo.

REM ---------- 4. Release Build ----------
echo [4/6] Building PdfStudio in Release (self-contained, takes 3-5 min)...
echo.

set "PUBLISH_DIR=%~dp0src\PdfStudio.Wpf\bin\Release\net10.0-windows\win-x64\publish"

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish src\PdfStudio.Wpf\PdfStudio.Wpf.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    --verbosity quiet
if errorlevel 1 goto :BuildFailed

if not exist "%PUBLISH_DIR%\PdfStudio.exe" goto :BuildFailed

echo   OK: Build complete
echo   Output: %PUBLISH_DIR%
echo.

REM ---------- Ensure tools/tessdata folder is in publish output ----------
if exist "%~dp0tools" (
    echo   Copying tools folder...
    xcopy /E /I /Y /Q "%~dp0tools" "%PUBLISH_DIR%\tools" >nul
    if errorlevel 1 (
        echo   Warning: failed to copy tools folder
    ) else (
        echo   OK: tools folder copied to publish output
    )
)
echo.

REM ---------- 5. Generate Components.wxs ----------
echo [5/6] Generating WiX components list...

powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0installer\GenerateComponents.ps1" ^
    -PublishDir "%PUBLISH_DIR%" ^
    -OutputPath "%~dp0installer\Components.wxs"
if errorlevel 1 goto :ComponentGenFailed
echo.

REM ---------- 6. Build MSI ----------
echo [6/6] Building MSI installer...
echo.

if not exist "build" mkdir build

cd installer

wix build Product.wxs Components.wxs ^
    -ext WixToolset.UI.wixext ^
    -ext WixToolset.Util.wixext ^
    -arch x64 ^
    -o "..\build\PdfStudioSetup.msi"

if errorlevel 1 (
    cd ..
    goto :MsiFailed
)
cd ..

if not exist "build\PdfStudioSetup.msi" goto :MsiFailed

echo.
echo ============================================================
echo   BUILD SUCCESS !
echo ============================================================
echo.
echo   Generated MSI installer:
echo     %CD%\build\PdfStudioSetup.msi
echo.
echo   How to use:
echo     1. Double-click PdfStudioSetup.msi
echo     2. Follow the installation wizard (admin rights required)
echo     3. Launch PdfStudio from Start Menu
echo.
echo ============================================================
echo.

start "" "%CD%\build"
pause
exit /b 0


REM ============================================================
REM Error Handlers
REM ============================================================
:NoDotNet
echo.
echo ============================================================
echo   ERROR: .NET 10 SDK is not installed or not detected
echo ============================================================
echo.
echo Installed SDKs on this system:
dotnet --list-sdks 2>nul
echo.
echo Please download and install .NET 10 SDK from:
echo.
echo   https://dotnet.microsoft.com/download/dotnet/10.0
echo.
echo Choose ".NET SDK x64" Installer, install it,
echo then run this script again.
echo.
echo Opening download page in your browser...
start https://dotnet.microsoft.com/download/dotnet/10.0
echo.
pause
exit /b 1

:RestoreFailed
echo.
echo ============================================================
echo   ERROR: NuGet restore failed
echo ============================================================
echo Check your internet connection and try again.
pause
exit /b 1

:BuildFailed
echo.
echo ============================================================
echo   ERROR: Build failed
echo ============================================================
echo Check the log above for the error details.
pause
exit /b 1

:ComponentGenFailed
echo.
echo ============================================================
echo   ERROR: Components.wxs generation failed
echo ============================================================
pause
exit /b 1

:MsiFailed
echo.
echo ============================================================
echo   ERROR: MSI build failed
echo ============================================================
echo Check the log for Components.wxs and Product.wxs errors.
pause
exit /b 1
