# Closes OBS gracefully, targeting the real main window (not a Preview/Fullscreen
# Projector window, which CloseMainWindow() can mistakenly grab instead).

$p = Get-Process -Name 'obs64' -ErrorAction SilentlyContinue
if (-not $p) { exit 0 }

Add-Type -Namespace Win32 -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
[DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
'@

$script:mainHandle = [IntPtr]::Zero

$callback = {
    param($hWnd, $lParam)
    [uint32]$procId = 0
    [Win32.Native]::GetWindowThreadProcessId($hWnd, [ref]$procId) | Out-Null
    if ($procId -eq $p.Id -and [Win32.Native]::IsWindowVisible($hWnd)) {
        $len = [Win32.Native]::GetWindowTextLength($hWnd)
        if ($len -gt 0) {
            $sb = New-Object System.Text.StringBuilder ($len + 1)
            [Win32.Native]::GetWindowText($hWnd, $sb, $sb.Capacity) | Out-Null
            $title = $sb.ToString()
            if ($title -notmatch 'Projector') {
                $script:mainHandle = $hWnd
                return $false
            }
        }
    }
    return $true
}

[Win32.Native]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

if ($script:mainHandle -ne [IntPtr]::Zero) {
    [Win32.Native]::PostMessage($script:mainHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
} else {
    $p.CloseMainWindow() | Out-Null
}

if (-not $p.WaitForExit(15000)) {
    Write-Error 'OBS did not close gracefully within 15 seconds'
    exit 1
}

exit 0
