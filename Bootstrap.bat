@echo off
rem Builds the engine on Windows: the native core, then the managed engine, then
rem the shaders.
rem
rem Needs the .NET 10 SDK, and Visual Studio with the "Desktop development with
rem C++" workload for the core. Without the C++ tools, run the first line with
rem --skip-native: game\bin\win64 already carries a built rintencore.dll.

dotnet run --project .\engine\Tools\RintenBuild\RintenBuild.csproj -- build %* || exit /b 1
dotnet run --project .\engine\Tools\RintenBuild\RintenBuild.csproj -- build-shaders
