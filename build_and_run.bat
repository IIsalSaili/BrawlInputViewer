@echo off
cd /d "%~dp0"
dotnet build -c Release
if errorlevel 1 (
    echo Build echouee.
    pause
    exit /b 1
)
start "" "bin\Release\net8.0-windows\BrawlhallaOverlay.exe"
