@echo off

REM Each app is only started if it isn't already running, so this is safe to run on a partly-open studio.

REM Rig page in its own Edge app window. The dedicated profile dir lets Close Studio.bat
REM find and close only this window (and lets us tell whether it is already open).
powershell.exe -NoProfile -Command "if (Get-CimInstance Win32_Process -Filter \"Name='msedge.exe'\" | Where-Object { $_.CommandLine -like '*StudioRigApp*' }) { exit 1 }"
if errorlevel 1 (
    echo Rig page already open.
) else (
    start "" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --user-data-dir="C:\Users\willi\AppData\Local\StudioRigApp" --app="http://192.168.21.147/#rig" --no-first-run
)

REM MCP servers (CreativeRig: RigReaper/RigHeadrush/RigObs/RigCapCut) are managed
REM by Copilot, so nothing to launch here.

REM Launch REAPER with your template
tasklist /FI "IMAGENAME eq reaper.exe" 2>nul | find /I "reaper.exe" >nul
if errorlevel 1 (
    start "" "C:\Program Files\REAPER (x64)\reaper.exe" "C:\Users\willi\AppData\Roaming\REAPER\ProjectTemplates\August 14 2026 Base Template.rpp"
    REM Give REAPER a few seconds to start
    timeout /t 5 /nobreak > nul
) else (
    echo REAPER already running.
)

REM Launch G-Force standalone
tasklist /FI "IMAGENAME eq G-Force Standalone.x64.exe" 2>nul | find /I "G-Force Standalone" >nul
if errorlevel 1 (
    start "" "C:\Program Files (x86)\SoundSpectrum\G-Force\G-Force Standalone.x64.exe"

    REM Maximize the G-Force window after it's ready (not fullscreen)
    timeout /t 2 /nobreak > nul
    powershell.exe -NoProfile -Command "$p = Get-Process -Name 'G-Force Standalone.x64' -ErrorAction SilentlyContinue; if ($p -and $p.MainWindowHandle -ne 0) { Add-Type -Namespace Win32 -Name Native -MemberDefinition '[DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);'; [Win32.Native]::ShowWindow($p.MainWindowHandle, 3) }"
) else (
    echo G-Force already running.
)

REM Launch OBS with the requested scene selected
REM /D sets OBS's working directory so it can find its locale/data files
tasklist /FI "IMAGENAME eq obs64.exe" 2>nul | find /I "obs64.exe" >nul
if errorlevel 1 (
    start "" /D "C:\Program Files\obs-studio\bin\64bit" "C:\Program Files\obs-studio\bin\64bit\obs64.exe" --scene "Captain Cilipourri Naught"
    REM Give OBS a few seconds to initialize before the AI connects to MCP servers
    timeout /t 5 /nobreak > nul
) else (
    echo OBS already running.
)

REM Launch the VS Code Agents window last so the apps are running when its MCP servers connect.
tasklist /FI "IMAGENAME eq Code.exe" 2>nul | find /I "Code.exe" >nul
if errorlevel 1 (
    call "C:\Users\willi\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd" --agents "C:\Users\willi\Documents\Repository"
) else (
    echo VS Code already running.
)
