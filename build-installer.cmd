@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
cd /d "%ROOT%"

if /i "%~1"=="--help" goto :help
if /i "%~1"=="-h" goto :help
if not "%~1"=="" goto :unknown_argument

echo.
echo DisplayPilot installer build
echo ==============================
echo.

where dotnet.exe >nul 2>nul
if errorlevel 1 (
    echo ERROR: The .NET SDK was not found on PATH.
    echo Install .NET 8 SDK or newer, then run this script again.
    exit /b 1
)

set "ISCC_PATH="
if defined ISCC_EXE if exist "%ISCC_EXE%" set "ISCC_PATH=%ISCC_EXE%"
if not defined ISCC_PATH if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC_PATH if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC_PATH=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC_PATH for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC_PATH set "ISCC_PATH=%%I"

if not defined ISCC_PATH (
    echo ERROR: Inno Setup 6 compiler was not found.
    echo Install Inno Setup 6, add ISCC.exe to PATH, or set ISCC_EXE.
    exit /b 1
)

echo [1/3] Cleaning the framework-dependent publish folder...
if exist "%ROOT%publish" rmdir /s /q "%ROOT%publish"
if exist "%ROOT%publish" (
    echo ERROR: Could not clean "%ROOT%publish".
    exit /b 1
)

echo [2/3] Publishing DisplayPilot Release for win-x64...
dotnet publish "%ROOT%PrimaryDisplaySwap.csproj" -c Release -p:PublishProfile=FolderProfile
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    exit /b 1
)

if not exist "%ROOT%publish\DisplayPilot.exe" (
    echo ERROR: Publish completed without producing publish\DisplayPilot.exe.
    exit /b 1
)

echo [3/3] Compiling the Inno Setup installer...
"%ISCC_PATH%" "%ROOT%installer\DisplayPilot.iss"
if errorlevel 1 (
    echo ERROR: Inno Setup compilation failed.
    exit /b 1
)

if not exist "%ROOT%installer\output\DisplayPilot-Setup.exe" (
    echo ERROR: Inno Setup completed without producing DisplayPilot-Setup.exe.
    exit /b 1
)

echo.
echo SUCCESS: Installer created:
echo   %ROOT%installer\output\DisplayPilot-Setup.exe
echo.
exit /b 0

:help
echo Build the DisplayPilot Windows installer.
echo.
echo Usage:
echo   build-installer.cmd
echo.
echo Requirements:
echo   - .NET SDK on PATH
echo   - Inno Setup 6 in its default location or on PATH
echo   - Alternatively set ISCC_EXE to the full ISCC.exe path
exit /b 0

:unknown_argument
echo ERROR: Unknown argument "%~1".
echo Run build-installer.cmd --help for usage.
exit /b 2
