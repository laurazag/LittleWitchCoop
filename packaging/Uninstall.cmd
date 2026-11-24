@echo off
setlocal DisableDelayedExpansion
set "LWCOOP_GAME=%~1"
set "LWCOOP_QUIET=%~2"
set "LWCOOP_FIND=Steam: Little Witch - Manage - Browse local files."
set "LWCOOP_PROMPT=Paste the game folder containing LWIW.exe: "
set "LWCOOP_INVALID=This is not the Windows Little Witch in the Woods game folder."
set "LWCOOP_RUNNING=Close the game before continuing."
set "LWCOOP_CONFLICT=Another winhttp.dll already exists. That loader will not be replaced."
set "LWCOOP_DONE=Little Witch Co-op removed. Saves, translation, BepInEx and settings preserved."
set "LWCOOP_FAILED=Uninstall was not completed. See README.txt."
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
set "LWCOOP_PLUGINS=%LWCOOP_GAME%\BepInEx\plugins\LittleWitchCoop"
for %%N in (LittleWitchCoop.dll EMOJI-CREDITS.txt LICENSE-TWEMOJI.txt Minecraftia-Regular.ttf MINECRAFTIA-CREDITS.txt) do (
 if exist "%LWCOOP_PLUGINS%\%%N" (
  del /Q "%LWCOOP_PLUGINS%\%%N"
  if exist "%LWCOOP_PLUGINS%\%%N" goto failed
 )
)
rem Remove the folder only if empty; never recursively delete user-added files.
if exist "%LWCOOP_PLUGINS%" rd "%LWCOOP_PLUGINS%" 2>nul
echo %LWCOOP_DONE%
if /I not "%LWCOOP_QUIET%"=="/quiet" set /p "LWCOOP_EXIT=Press Enter to close..."
exit /b 0
:invalid
echo %LWCOOP_INVALID%
goto failed
:running
echo %LWCOOP_RUNNING%
:failed
echo %LWCOOP_FAILED%
if /I not "%LWCOOP_QUIET%"=="/quiet" set /p "LWCOOP_EXIT=Press Enter to close..."
exit /b 1


