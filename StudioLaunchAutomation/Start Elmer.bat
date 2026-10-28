@echo off
REM Starts Elmer (voice listener + command executor) hidden. Safe to run twice: does nothing if already running.
REM Mic: set VOICE_DEVICE to part of the input device name (e.g. Realtek, Focusrite, Logi).
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ^
  "if (Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | Where-Object { $_.CommandLine -like '*voice_input.py*' }) { exit }; $d='C:\Users\willi\Documents\Repository\CreativeRig\src\VoiceInput'; $env:PYTHONUNBUFFERED='1'; if (-not $env:VOICE_DEVICE) { $env:VOICE_DEVICE='Realtek' }; Start-Process python -ArgumentList 'voice_input.py' -WorkingDirectory $d -RedirectStandardOutput \"$d\voice.log\" -RedirectStandardError \"$d\voice.err\" -WindowStyle Hidden"
