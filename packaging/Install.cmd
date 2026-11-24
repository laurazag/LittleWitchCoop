@echo off
setlocal DisableDelayedExpansion
set "LWCOOP_GAME=%~1"
set "LWCOOP_QUIET=%~2"
set "LWCOOP_FIND=Steam: Little Witch - Manage - Browse local files."
set "LWCOOP_PROMPT=Paste the game folder containing LWIW.exe: "
set "LWCOOP_INVALID=This is not the Windows Little Witch in the Woods game folder."
set "LWCOOP_RUNNING=Close the game before continuing."
set "LWCOOP_CONFLICT=Another winhttp.dll already exists. That loader will not be replaced."
set "LWCOOP_DONE=Little Witch Co-op 0.10.6 installed. Launch through Steam and press F2."
set "LWCOOP_FAILED=Installation was not completed. See README.txt."
if defined LWCOOP_GAME goto validate
echo %LWCOOP_FIND%
set /p "LWCOOP_GAME=%LWCOOP_PROMPT%"
if not defined LWCOOP_GAME goto invalid
:validate
set "LWCOOP_GAME=%LWCOOP_GAME:"=%"
for %%I in ("%LWCOOP_GAME%") do set "LWCOOP_GAME=%%~fI"
if not exist "%LWCOOP_GAME%\LWIW.exe" goto invalid
if not exist "%LWCOOP_GAME%\LWIW_Data\Managed\Assembly-CSharp.dll" goto invalid
tasklist /FI "IMAGENAME eq LWIW.exe" /NH | find /I "LWIW.exe" >nul
if not errorlevel 1 goto running
set "LWCOOP_PAYLOAD=%~dp0Mod files"
if exist "%LWCOOP_GAME%\BepInEx\core\BepInEx.dll" goto plugin
if exist "%LWCOOP_GAME%\winhttp.dll" goto conflict
if not exist "%LWCOOP_GAME%\BepInEx\core" mkdir "%LWCOOP_GAME%\BepInEx\core"
xcopy "%LWCOOP_PAYLOAD%\BepInEx\core\*" "%LWCOOP_GAME%\BepInEx\core\" /E /I /Y >nul
if errorlevel 1 goto failed
for %%N in (winhttp.dll doorstop_config.ini .doorstop_version) do (
    copy /B /Y "%LWCOOP_PAYLOAD%\%%N" "%LWCOOP_GAME%\%%N" >nul
    if errorlevel 1 goto failed
)
:plugin
set "LWCOOP_PLUGINS=%LWCOOP_GAME%\BepInEx\plugins\LittleWitchCoop"
if not exist "%LWCOOP_PLUGINS%" mkdir "%LWCOOP_PLUGINS%"
if not exist "%LWCOOP_PLUGINS%\LittleWitchCoop.dll" goto copyplugin
:backupname
set "LWCOOP_BACKUP=%LWCOOP_GAME%\LittleWitchCoop-backups\%RANDOM%-%RANDOM%"
if exist "%LWCOOP_BACKUP%" goto backupname
mkdir "%LWCOOP_BACKUP%"
copy /B /Y "%LWCOOP_PLUGINS%\LittleWitchCoop.dll" "%LWCOOP_BACKUP%\LittleWitchCoop.dll" >nul
if errorlevel 1 goto failed
:copyplugin
xcopy "%LWCOOP_PAYLOAD%\BepInEx\plugins\LittleWitchCoop\*" "%LWCOOP_PLUGINS%\" /I /Y >nul
if errorlevel 1 goto failed
echo %LWCOOP_DONE%
if /I not "%LWCOOP_QUIET%"=="/quiet" set /p "LWCOOP_EXIT=Press Enter to close..."
exit /b 0
:invalid
echo %LWCOOP_INVALID%
goto failed
:running
echo %LWCOOP_RUNNING%
goto failed
:conflict
echo %LWCOOP_CONFLICT%
:failed
echo %LWCOOP_FAILED%
if /I not "%LWCOOP_QUIET%"=="/quiet" set /p "LWCOOP_EXIT=Press Enter to close..."
exit /b 1















