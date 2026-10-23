param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$ReportPath, [switch]$LongRun)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
$data = Join-Path ([IO.Path]::GetTempPath()) ('SonarShift-switch-probe-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($data) | Out-Null
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath)) | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'settings.json'), '{"Initialized":true,"HotkeysEnabled":false,"Outputs":[],"Inputs":[],"Profiles":[]}')
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
function Launch([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.Environment['SONARSHIFT_TEST_DATA'] = $data
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start); $owned.Add($process); return $process
}
function Command([string]$Name) {
    $reply = Join-Path $data ([guid]::NewGuid().ToString('N') + '.json')
    $process = Launch @('--host-command', $Name, '--report', $reply)
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw "Command failed: $Name" }
}
try {
    $hostProcess = Launch @('--tray')
    Start-Sleep -Seconds 3
    if ($LongRun) { Command 'test-switch-stress-long' } else { Command 'test-switch-stress' }
    $probeReport = Join-Path $data 'stress-report.json'
    $timeout = [Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath $probeReport)) {
        if ($timeout.Elapsed.TotalSeconds -gt 180 -or $hostProcess.HasExited) { throw 'Switch probe failed or timed out.' }
        Start-Sleep -Milliseconds 500
    }
    $result = Get-Content -LiteralPath $probeReport -Raw | ConvertFrom-Json
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    $first = $result.checkpoints[0]; $last = $result.checkpoints[-1]
    if (($result.checkpoints.notificationHandle | Select-Object -Unique).Count -ne 1 -or $first.notificationHandle -eq 0) { throw 'Popup handle was not reused.' }
    if (@($result.checkpoints | Where-Object { $_.activationCount -ne 0 -or !$_.trayChecksCorrect }).Count -ne 0) { throw 'Popup stole focus or tray checks stopped tracking defaults.' }
    if (($last.allocatedBytes-$first.allocatedBytes)/$last.switches -gt 10000) { throw 'Synthetic switch allocation exceeded the regression budget.' }
    if ($last.gdiObjects-$first.gdiObjects -gt 4 -or $last.userObjects-$first.userObjects -gt 4) { throw 'Native GUI resource counts grew after warmup.' }
    [pscustomobject]@{
        simulatedSwitches=$last.switches
        allocatedBytes=$last.allocatedBytes-$first.allocatedBytes
        allocatedBytesPerSwitch=[math]::Round(($last.allocatedBytes-$first.allocatedBytes)/$last.switches,1)
        retainedManagedHeapChangeBytes=$last.managedHeapBytes-$first.managedHeapBytes
        privateCommitChangeBytes=$last.privateCommittedBytes-$first.privateCommittedBytes
        gdiObjectChange=[int]$last.gdiObjects-[int]$first.gdiObjects
        userObjectChange=[int]$last.userObjects-[int]$first.userObjects
        gen0Collections=$last.gen0-$first.gen0
    } | ConvertTo-Json
    Command 'quit'
    if (!$hostProcess.WaitForExit(10000)) { throw 'Probe host did not exit.' }
} finally {
    foreach ($process in $owned) { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
