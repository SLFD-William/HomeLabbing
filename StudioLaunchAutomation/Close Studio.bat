@echo off

REM Close only the rig app window (identified by its dedicated profile dir).
powershell.exe -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='msedge.exe'\" | Where-Object { $_.CommandLine -like '*StudioRigApp*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"

REM Ask OBS to close normally so it can save its state cleanly.
REM (Logic lives in Close-OBS.ps1 to avoid batch-file quoting/escaping issues.)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Users\willi\Documents\Repository\StudioLaunchAutomation\Close-OBS.ps1"

taskkill /IM "G-Force Standalone.x64.exe" /F >nul 2>&1
taskkill /IM "G-Force Toolbar.exe" /F >nul 2>&1
taskkill /IM reaper.exe /F >nul 2>&1
taskkill /IM reaper-mcp-server.exe /F >nul 2>&1
taskkill /IM Mcp.Reaper.exe /F >nul 2>&1
taskkill /IM Mcp.Headrush.exe /F >nul 2>&1
taskkill /IM Mcp.Obs.exe /F >nul 2>&1
taskkill /IM Mcp.CapCut.exe /F >nul 2>&1
REM Elmer (the voice assistant) keeps running so it can launch the studio again.
REM Leave VS Code open; the Agents window may share the user's editor process.
exit