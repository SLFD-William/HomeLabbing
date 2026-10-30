@echo off
REM Stops Elmer (voice listener, status pill and command executor) to free the GPU and memory.
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ^
  "Get-CimInstance Win32_Process | Where-Object { ($_.Name -eq 'python.exe' -and $_.CommandLine -like '*voice_input.py*') -or $_.Name -eq 'Voice.Commands.exe' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
