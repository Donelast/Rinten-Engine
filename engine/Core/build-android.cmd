@echo off
rem Builds rintencore.so for Android and installs it into game\bin\android-arm64.
rem
rem   build-android.cmd            incremental
rem   build-android.cmd fresh      delete the build dir first
rem
rem Needs the Android NDK, found through ANDROID_NDK_HOME (or ANDROID_NDK_ROOT).
rem Everything else comes with it: the NDK ships its own CMake toolchain file and
rem a clang that targets the phone.
rem
rem arm64 only. A 32-bit phone will not run this engine, and every device Vulkan
rem is worth having on has been arm64 for years.

setlocal

set NDK=%ANDROID_NDK_HOME%
if "%NDK%"=="" set NDK=%ANDROID_NDK_ROOT%

if "%NDK%"=="" (
	echo ERROR: no Android NDK.
	echo Set ANDROID_NDK_HOME to it - for example C:\Users\you\AppData\Local\Android\Sdk\ndk\27.0.12077973
	exit /b 1
)

set TOOLCHAIN=%NDK%\build\cmake\android.toolchain.cmake
if not exist "%TOOLCHAIN%" (
	echo ERROR: "%TOOLCHAIN%" is not there, so "%NDK%" is not an NDK.
	exit /b 1
)

rem The NDK brings CMake and Ninja along with the SDK, but a machine that builds
rem for Windows already has both - use whichever is on the path.
where cmake >nul 2>nul || (
	echo ERROR: no cmake on the path.
	exit /b 1
)

set SRC=%~dp0
set BLD=%SRC%build\android-arm64
set DEST=%SRC%..\..\game\bin\android-arm64

if /i "%~1"=="fresh" if exist "%BLD%" rmdir /s /q "%BLD%"

rem API 28 is where Vulkan is worth relying on and where the NDK's own default
rem sits; below that the loader is present on paper and absent in practice.
cmake -S "%SRC%." -B "%BLD%" -G Ninja ^
	-DCMAKE_TOOLCHAIN_FILE="%TOOLCHAIN%" ^
	-DANDROID_ABI=arm64-v8a ^
	-DANDROID_PLATFORM=android-28 ^
	-DCMAKE_BUILD_TYPE=Release || exit /b 1

cmake --build "%BLD%" || exit /b 1

if not exist "%DEST%" mkdir "%DEST%"

copy /y "%BLD%\bin\rintencore.so" "%DEST%\rintencore.so" >nul
if errorlevel 1 (
	echo.
	echo ERROR: could not install rintencore.so into "%DEST%".
	exit /b 1
)

echo.
echo rintencore.so -^> %DEST%
