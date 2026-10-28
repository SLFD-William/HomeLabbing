#Requires AutoHotkey v2.0
; ---------------------------------------------------------------
; CopilotHotkeys.ahk
; Ctrl+Alt+C  -> Launch "Captain Cilipourri Naught Studio.bat"
; Ctrl+Alt+K  -> Launch "Close Studio.bat" (graceful shutdown)
; Ctrl+Alt+V  -> Focus the VS Code Agents window, then trigger
;                Windows Voice Typing (Win+H) so dictation lands
;                in the agent prompt.
; ---------------------------------------------------------------

BatFile := "C:\Users\willi\Documents\Repository\StudioLaunchAutomation\Captain Cilipourri Naught Studio.bat"
CloseBatFile := "C:\Users\willi\Documents\Repository\StudioLaunchAutomation\Close Studio.bat"

^!c:: {
    Run('"' . BatFile . '"')
}

^!k:: {
    Run('"' . CloseBatFile . '"')
}

^!v:: {
    if WinExist("Agents ahk_exe Code.exe") {
        WinActivate()
    } else if WinExist("ahk_exe Code.exe") {
        WinActivate()
    }
    Sleep(150)
    Send("#h")  ; opens/starts Windows Voice Typing, dictating into the now-focused input
}
