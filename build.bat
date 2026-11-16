@echo off
rem Builds YARG Demucs without Visual Studio. Needs the .NET 8 SDK (or newer).
rem The exe ends up in src\YargDemucs\bin\Release\net8.0\yarg-demucs.exe
rem Pass "debug" to build the Debug version instead.

setlocal
cd /d "%~dp0"

set CONFIG=Release
if /i "%~1"=="debug" set CONFIG=Debug

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    exit /b 1
)

dotnet build src\YargDemucs\YargDemucs.csproj -c %CONFIG% -nologo
if errorlevel 1 (
    echo.
    echo Build failed. If yarg-demucs.exe is running, close it and try again.
    exit /b 1
)

echo.
echo Built: %CD%\src\YargDemucs\bin\%CONFIG%\net8.0\yarg-demucs.exe
endlocal
