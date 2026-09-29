@echo off
setlocal
echo ========================================================
echo   Net.Pi - Building Standalone Single-File Binary
echo ========================================================
echo.

cd /d "%~dp0"
set RUNTIME=%1
if "%RUNTIME%"=="" set RUNTIME=win-x64

echo Publishing for runtime: %RUNTIME% ...
echo Output directory: dist\%RUNTIME%\

dotnet publish src/Net.Pi.Cli/Net.Pi.Cli.csproj ^
  -c Release ^
  -r %RUNTIME% ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist\%RUNTIME%

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUCCESS] Net.Pi standalone executable generated at:
    echo dist\%RUNTIME%\Net.Pi.Cli.exe
) else (
    echo.
    echo [FAILED] dotnet publish failed with code %ERRORLEVEL%.
)
