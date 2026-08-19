@echo off
echo ========================================
echo DestBinParser 功能测试
echo ========================================
echo.

cd /d "%~dp0"

echo 编译项目...
dotnet build ResBinManager\ResBinManager.csproj -c Release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ✗ 编译失败
    pause
    exit /b 1
)

echo.
echo 运行测试...
echo.

dotnet run --project ResBinManager\ResBinManager.csproj --no-build -c Release -- Tests\DestBinParserTest.cs

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ✗ 测试执行失败
    pause
    exit /b 1
)

echo.
echo ========================================
echo 测试完成！
echo ========================================
pause
