@echo off
rem Builds rintencore.dll and installs it into game\bin\win64.
rem
rem   build-win64.cmd            incremental
rem   build-win64.cmd fresh      delete the build dir first
rem
rem Needs the "Desktop development with C++" workload - it supplies the
rem compiler, and the CMake and Ninja that this script uses.

setlocal

set SRC=%~dp0
set BLD=%SRC%build\win64
set DEST=%SRC%..\..\game\bin\win64

rem Where Visual Studio is, asked rather than assumed. This used to name one
rem edition at one path, so a machine carrying Professional - or carrying
rem Community anywhere else - was told it had no compiler at all.
set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
set VS=
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i

if not defined VS (
	echo ERROR: no Visual Studio with the C++ tools on this machine.
	echo Install the "Desktop development with C++" workload.
	exit /b 1
)

if /i "%~1"=="fresh" if exist "%BLD%" rmdir /s /q "%BLD%"

call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

rem CMake and Ninja off PATH when they are there and out of the workload when
rem they are not: vcvars64 puts neither on PATH.
set CMAKE=cmake
where cmake >nul 2>&1 || set CMAKE=%VS%\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe

set NINJA_EXE=%VS%\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe
set NINJA=
where ninja >nul 2>&1 || set NINJA=-DCMAKE_MAKE_PROGRAM="%NINJA_EXE%"

if not "%CMAKE%"=="cmake" if not exist "%CMAKE%" (
	echo ERROR: no cmake on PATH and none in the Visual Studio install.
	echo Install "C++ CMake tools for Windows", or put cmake on PATH.
	exit /b 1
)

if defined NINJA if not exist "%NINJA_EXE%" (
	echo ERROR: no ninja on PATH and none in the Visual Studio install.
	echo Install "C++ CMake tools for Windows", or put ninja on PATH.
	exit /b 1
)

"%CMAKE%" -S "%SRC%." -B "%BLD%" -G Ninja -DCMAKE_BUILD_TYPE=Release %NINJA% || exit /b 1
"%CMAKE%" --build "%BLD%" || exit /b 1

rem The editor holds the DLL open, so say so plainly instead of failing with a
rem copy error nobody can read.
rem The symbols go with it, so a minidump from this build reads as function names.
if exist "%BLD%\bin\rintencore.pdb" copy /y "%BLD%\bin\rintencore.pdb" "%DEST%\rintencore.pdb" >nul

copy /y "%BLD%\bin\rintencore.dll" "%DEST%\rintencore.dll" >nul
if errorlevel 1 (
	echo.
	echo ERROR: could not install rintencore.dll into "%DEST%".
	echo Close the editor and the launcher, then run this again.
	exit /b 1
)

rem And NVIDIA's NGX, which DLSS is reached through - see build-ngx.cmd. A machine
rem that cannot fetch it still builds: the engine then simply offers no DLSS.
call "%SRC%build-ngx.cmd" || echo NGX not built - DLSS will not be offered

echo.
echo rintencore.dll -^> %DEST%
