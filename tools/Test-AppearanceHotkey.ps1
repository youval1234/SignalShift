param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$ReportPath)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$data = Join-Path $env:TEMP ('SignalShift-theme-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($data) | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'settings.json'), '{"Initialized":true,"HotkeysEnabled":true,"OutputHotkeyEnabled":false,"InputHotkeyEnabled":false,"ThemeHotkeyEnabled":true,"ThemeShortcut":{"Modifiers":7,"Key":135,"Label":"test"},"Notifications":false,"CameraEnabled":false}')
Add-Type 'using System; using System.Runtime.InteropServices; public static class ThemeHotkeyMessages { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l); }'
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
function Launch([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable); $start.UseShellExecute = $false
    $start.Environment['SONARSHIFT_TEST_DATA'] = $data
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start); $owned.Add($process); return $process
}
function Command([string]$Name) {
    $path = Join-Path $data ([guid]::NewGuid().ToString('N') + '.json')
    $process = Launch @('--host-command',$Name,'--report',$path)
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Host command failed.' }
    return Get-Content $path -Raw | ConvertFrom-Json
}
try {
    $hostProcess = Launch @('--tray'); Start-Sleep -Seconds 3
    $first = Command 'status'
    if ($first.conflicts.Count -ne 0 -or $first.wpfLoaded -or $first.cameraEnabled -or !$first.testHotkeyWindow) { throw 'Invalid isolated host baseline.' }
    $states = [Collections.Generic.List[string]]::new()
    foreach ($expected in 'Light','Dark','Light','Dark') {
        if (![ThemeHotkeyMessages]::PostMessage([IntPtr]$first.testHotkeyWindow, 0x312, [IntPtr]3, [IntPtr]0)) { throw 'Hotkey message was not delivered.' }
        Start-Sleep -Milliseconds 200
        $state = Command 'status'; $saved = Get-Content (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
        if ($state.theme -cne $expected -or $saved.Theme -cne $expected -or $state.wpfLoaded -or $state.interfaceRunning -or $state.cameraEnabled) { throw 'Tray shortcut did not persist the theme without loading UI/camera.' }
        $states.Add($state.theme)
    }
    [pscustomobject]@{ success=$true; dispatch='WM_HOTKEY through registered theme action'; themes=$states; wpfLoaded=$state.wpfLoaded; cameraEnabled=$state.cameraEnabled; interfaceRunning=$state.interfaceRunning } | ConvertTo-Json | Set-Content -LiteralPath $ReportPath -Encoding utf8
    $null = Command 'quit'
    if (!$hostProcess.WaitForExit(10000)) { throw 'Host did not exit.' }
} finally {
    foreach ($process in $owned) { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
