@echo off
cd /d "%~dp0"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if errorlevel 1 (
    echo Publication echouee.
    pause
    exit /b 1
)
echo.
echo Exe autonome genere :
echo bin\Release\net8.0-windows\win-x64\publish\BrawlhallaOverlay.exe
pause
