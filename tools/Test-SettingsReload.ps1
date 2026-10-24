param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$ReportPath)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$data = Join-Path $env:TEMP ('SignalShift-reload-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($data) | Out-Null
$settings = Join-Path $data 'settings.json'
[IO.File]::WriteAllText($settings, '{"Initialized":true,"HotkeysEnabled":false,"Notifications":false,"HotkeySounds":false,"Theme":"Dark"}')
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
function Launch([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false; $start.Environment['SONARSHIFT_TEST_DATA'] = $data
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start); $owned.Add($process); return $process
}
function Command([string]$Action) {
    $path = Join-Path $data ([guid]::NewGuid().ToString('N') + '.json')
    $process = Launch @('--host-command',$Action,'--report',$path)
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw 'Isolated command failed.' }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}
try {
    $hostProcess = Launch @('--tray'); Start-Sleep -Seconds 2
    $before = Command 'status'
    $bytes = [Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($settings).Replace('Dark','Light'))
    $lockedFile = [IO.File]::Open($settings, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $lockedFile.SetLength(0); $lockedFile.Write($bytes); $lockedFile.Flush()
        Start-Sleep -Milliseconds 900
        $during = Command 'status'
        if ($during.pid -ne $before.pid -or $during.theme -ne 'Dark') { throw 'Locked reload changed the working preferences or stopped the host.' }
    } finally { $lockedFile.Dispose() }
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 200; $after = Command 'status'
    } until ($after.theme -eq 'Light' -or [DateTime]::UtcNow -ge $deadline)
    if ($after.pid -ne $before.pid -or $after.theme -ne 'Light' -or $after.wpfLoaded) { throw 'Settings did not recover after unlocking.' }
    [pscustomobject]@{success=$true;whileLocked=$during.theme;afterUnlock=$after.theme;sameProcess=($after.pid -eq $before.pid);wpfLoaded=$after.wpfLoaded} | ConvertTo-Json | Set-Content -LiteralPath $ReportPath -Encoding utf8
} finally {
    if ($hostProcess -and !$hostProcess.HasExited) {
        try { $null = Command 'quit'; $null = $hostProcess.WaitForExit(3000) } catch { }
    }
    foreach ($process in $owned) { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
