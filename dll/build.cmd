@echo off
rem Builds the x86 msvfw32 proxy. Needs Visual Studio (or Build Tools) with the C++ workload.
rem Output: msvfw32.dll (next to this script), which A2G-Patch.exe embeds and installs.
rem VCVARS may be set beforehand to a specific vcvarsall.bat; otherwise vswhere finds the newest install.
setlocal

if not defined VCVARS (
    for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VCVARS=%%i\VC\Auxiliary\Build\vcvarsall.bat"
)
if not exist "%VCVARS%" (
    echo [Fehler] vcvarsall.bat nicht gefunden. Bitte in Visual Studio die Workload
    echo "Desktopentwicklung mit C++" installieren, oder VCVARS auf vcvarsall.bat setzen.
    exit /b 1
)

call "%VCVARS%" x86 || exit /b 1

cd /d "%~dp0"
rem No C runtime (/Zl /GS- /NODEFAULTLIB, own entry point), so Windows 2000 and XP load it.
cl /nologo /W3 /O1 /GS- /Zl /LD msvfw32.c /link /DEF:msvfw32.def /OUT:msvfw32.dll /NODEFAULTLIB /ENTRY:DllMain /SUBSYSTEM:WINDOWS,5.01 kernel32.lib
if errorlevel 1 exit /b 1
rem Windows 2000 refuses images whose subsystem version is above 5.0, and the linker won't write
rem less than 5.01: set operating system and subsystem version in the optional header to 5.0.
powershell -NoProfile -Command "$b = [IO.File]::ReadAllBytes('msvfw32.dll'); $o = [BitConverter]::ToInt32($b, 0x3C) + 24; foreach ($f in 40, 48) { $b[$o + $f] = 5; $b[$o + $f + 1] = 0; $b[$o + $f + 2] = 0; $b[$o + $f + 3] = 0 }; [IO.File]::WriteAllBytes('msvfw32.dll', $b)"
if errorlevel 1 exit /b 1

del /q msvfw32.obj msvfw32.exp msvfw32.lib 2>nul
echo.
echo Fertig: %~dp0msvfw32.dll
