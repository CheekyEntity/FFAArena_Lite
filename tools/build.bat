@echo off
setlocal enableextensions enabledelayedexpansion

rem Change CWD to repo root (tools\..)
pushd %~dp0..

set PROJECT=FFAArena_Lite
set CSProj=src\%PROJECT%.csproj
set OUTDIR=src\bin\Release\netstandard2.1
set DLL=%OUTDIR%\%PROJECT%.dll
set PKGDIR=packageFiles
set STAGE=release\_stage
set ZIP=release\%PROJECT%.zip

echo Building %PROJECT%...
if not exist %CSProj% (
  echo ERROR: Project file not found: %CSProj%
  popd & exit /b 1
)

call dotnet build %CSProj% -c Release || (popd & exit /b 1)
if not exist %DLL% (
  echo ERROR: Built DLL not found: %DLL%
  popd & exit /b 1
)

echo Preparing Thunderstore staging directory...
if exist %STAGE% rmdir /s /q %STAGE%
mkdir %STAGE% || (popd & exit /b 1)
mkdir %STAGE%\BepInEx\plugins\%PROJECT% || (popd & exit /b 1)

rem Copy root package files: icon.png, manifest.json, README.md (if present)
for %%F in (icon.png manifest.json README.md) do (
  if exist %PKGDIR%\%%F copy /y %PKGDIR%\%%F %STAGE%\ >nul
)

rem Copy built DLL to BepInEx structure
copy /y %DLL% %STAGE%\BepInEx\plugins\%PROJECT%\ >nul || (popd & exit /b 1)

rem Copy any extra folders from packageFiles into the mod folder (e.g., Assets, Sprites)
if exist %PKGDIR% (
  for /d %%D in (%PKGDIR%\*) do (
    set NAME=%%~nxD
    if /I not "!NAME!"==".git" if /I not "!NAME!"==".github" if /I not "!NAME!"==".gitignore" (
      if /I not "!NAME!"==".vscode" if /I not "!NAME!"==".idea" (
        if /I not "!NAME!"=="BepInEx" (
          if /I not "!NAME!"=="release" (
            echo Copying folder: !NAME!
            xcopy /e /i /y "%%D" "%STAGE%\BepInEx\plugins\%PROJECT%\!NAME!\" >nul
          )
        )
      )
    )
  )
)

rem Copy any raw resources from src\Resources (e.g., ffaoverlay.png) into plugin Resources folder
if exist src\Resources (
  echo Copying src\Resources to plugin Resources...
  xcopy /e /i /y "src\Resources" "%STAGE%\BepInEx\plugins\%PROJECT%\Resources\" >nul
)

echo Creating Thunderstore zip: %ZIP%
if not exist release mkdir release
if exist %ZIP% del /q %ZIP%
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force" || (popd & exit /b 1)

echo Cleaning up staging...
rmdir /s /q %STAGE%

echo Done. Package created at %ZIP%
popd
