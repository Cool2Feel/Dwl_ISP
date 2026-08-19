@echo off
REM ========================================
REM RES.BIN Resource Manager - Quick Start
REM ========================================

echo.
echo ========================================
echo  RES.BIN Resource Manager v1.0
echo  AX329x SDK Tool
echo ========================================
echo.

REM 检查 .NET 是否安装
where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] .NET SDK not found!
    echo Please install .NET 6.0 or later from:
    echo https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo [INFO] Checking project...
cd ResBinManager

echo [INFO] Restoring dependencies...
dotnet restore
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to restore dependencies
    pause
    exit /b 1
)

echo [INFO] Building project...
dotnet build --configuration Release
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Build failed
    pause
    exit /b 1
)

echo.
echo ========================================
echo  Build successful!
echo ========================================
echo.
echo Starting application...
echo.

dotnet run --configuration Release

pause
