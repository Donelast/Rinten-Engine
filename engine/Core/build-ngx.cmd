@echo off
rem Makes rintenngx.dll - NVIDIA's NGX, the library DLSS is reached through - and
rem puts it, with NVIDIA's DLSS runtime, into game\bin\win64. See CoreDlss, and
rem build-ngx.sh for why this is fetched and linked rather than a line in
rem engine\natives.json: NGX is a static library, and the SDK's licence lets a
rem game carry its binaries but not an open repository hold them.
rem
rem   build-ngx.cmd           skip if already there
rem   build-ngx.cmd force     download and link again even if it is
rem
rem Needs the "Desktop development with C++" workload, for link.exe, and curl and
rem certutil, which Windows has.

setlocal

rem NVIDIA/DLSS at SDK 310.9.1 - the same pin as build-ngx.sh.
set DLSS_COMMIT=374959484e79a640feaba44c93ac8cfb0a03f5b5
set NGX_SHA256=4e5d355086d2bc11e1a0842457d2519ea528ee1f3e112c45679a84960c07dff3
set RUNTIME_SHA256=3975567b8943c53acce397f2b72380092f84f162d00b0d2c7d08a1025c563983
set RAW=https://raw.githubusercontent.com/NVIDIA/DLSS/%DLSS_COMMIT%

set SRC=%~dp0
set WORK=%SRC%build\ngx\win64
set DEST=%SRC%..\..\game\bin\win64

if /i not "%~1"=="force" if exist "%DEST%\rintenngx.dll" if exist "%DEST%\nvngx_dlss.dll" (
	echo NGX and DLSS already in %DEST%
	exit /b 0
)

set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
set VS=
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i

if not defined VS (
	echo ERROR: no Visual Studio with the C++ tools on this machine.
	exit /b 1
)

call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

if not exist "%WORK%" mkdir "%WORK%"
if not exist "%DEST%" mkdir "%DEST%"

call :fetch lib/Windows_x86_64/x64/nvsdk_ngx_s.lib "%WORK%\nvsdk_ngx_s.lib" %NGX_SHA256% || exit /b 1
call :fetch lib/Windows_x86_64/rel/nvngx_dlss.dll "%WORK%\nvngx_dlss.dll" %RUNTIME_SHA256% || exit /b 1

rem What the DLL exports: NGX's C functions, by the names CoreDlss asks for.
set DEF=%WORK%\rintenngx.def
> "%DEF%" echo LIBRARY rintenngx
>> "%DEF%" echo EXPORTS
for %%f in (VULKAN_RequiredExtensions VULKAN_Init_with_ProjectID VULKAN_Shutdown1 VULKAN_GetCapabilityParameters ^
	VULKAN_AllocateParameters VULKAN_DestroyParameters VULKAN_CreateFeature1 VULKAN_EvaluateFeature_C VULKAN_ReleaseFeature ^
	VULKAN_GetFeatureRequirements Parameter_SetI Parameter_SetUI Parameter_SetF Parameter_SetVoidPointer ^
	Parameter_GetI Parameter_GetUI) do >> "%DEF%" echo 	NVSDK_NGX_%%f

rem The whole archive into one DLL that exports those. The archive is built
rem against the static C runtime.
link /nologo /DLL /OUT:"%WORK%\rintenngx.dll" /DEF:"%DEF%" /WHOLEARCHIVE:"%WORK%\nvsdk_ngx_s.lib" ^
	libcmt.lib libvcruntime.lib libucrt.lib kernel32.lib user32.lib advapi32.lib shell32.lib ole32.lib version.lib setupapi.lib || exit /b 1

copy /y "%WORK%\rintenngx.dll" "%DEST%\rintenngx.dll" >nul || exit /b 1
copy /y "%WORK%\nvngx_dlss.dll" "%DEST%\nvngx_dlss.dll" >nul || exit /b 1

echo.
echo rintenngx.dll and nvngx_dlss.dll -^> %DEST%
exit /b 0

:fetch
if exist "%~2" (
	for /f "skip=1 tokens=*" %%h in ('certutil -hashfile "%~2" SHA256 ^| findstr /v "hash"') do if /i "%%h"=="%~3" exit /b 0
)
echo Downloading %~nx2
curl -fsSL -o "%~2" "%RAW%/%~1" || exit /b 1
for /f "skip=1 tokens=*" %%h in ('certutil -hashfile "%~2" SHA256 ^| findstr /v "hash"') do if /i "%%h"=="%~3" exit /b 0
echo ERROR: %~nx2 does not match its hash
del "%~2"
exit /b 1
