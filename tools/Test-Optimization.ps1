param(
    [Parameter(Mandatory)][string]$OptimizedExe,
    [string]$BaselineExe,
    [string]$ReportDirectory = (Join-Path $PSScriptRoot '..\dist\performance'),
    [int]$SampleSeconds = 20,
    [switch]$AllowExternalAudioEvents
)
$ErrorActionPreference = 'Stop'
$OptimizedExe = (Resolve-Path -LiteralPath $OptimizedExe).Path
if ($BaselineExe) { $BaselineExe = (Resolve-Path -LiteralPath $BaselineExe).Path }
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$ReportDirectory = (Resolve-Path -LiteralPath $ReportDirectory).Path
$runRoot = Join-Path $ReportDirectory ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$checks = @()
$measurements = @()
$ownedProcesses = [Collections.Generic.List[Diagnostics.Process]]::new()
function Start-TestProcess([string]$Executable,[string[]]$Arguments,[string]$DataDirectory) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.Environment['SONARSHIFT_TEST_DATA'] = $DataDirectory
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $ownedProcesses.Add($process)
    return $process
}
function New-TestData([string]$Name) {
    $path = Join-Path $runRoot $Name
    New-Item -ItemType Directory -Path $path | Out-Null
    '{"Initialized":true,"HotkeysEnabled":false,"Outputs":[],"Inputs":[],"Profiles":[]}' | Set-Content -LiteralPath (Join-Path $path 'settings.json') -Encoding utf8
    return $path
}
function Command([string]$Name,[string]$DataDirectory) {
    $report = Join-Path $DataDirectory ('command-' + [guid]::NewGuid().ToString('N') + '.json')
    $process = Start-TestProcess $OptimizedExe @('--host-command',$Name,'--report',$report) $DataDirectory
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw "Host command failed: $Name" }
    return Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
}
function Measure-Process([Diagnostics.Process]$Process,[string]$Scenario) {
    $Process.Refresh()
    if ($Process.HasExited) { throw "$Scenario exited before measurement." }
    $before = $Process.TotalProcessorTime.TotalSeconds
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $samples = @()
    for ($index=0; $index -lt $SampleSeconds; $index++) {
        Start-Sleep -Seconds 1
        $Process.Refresh()
        if ($Process.HasExited) { throw "$Scenario exited during measurement." }
        $samples += [pscustomobject]@{ workingSetBytes=$Process.WorkingSet64; privateCommittedBytes=$Process.PrivateMemorySize64; handles=$Process.HandleCount }
    }
    $elapsed = $watch.Elapsed.TotalSeconds
    return [ordered]@{
        scenario=$Scenario; pid=$Process.Id; seconds=$elapsed
        meanWorkingSetBytes=[long](($samples | Measure-Object workingSetBytes -Average).Average)
        meanPrivateCommittedBytes=[long](($samples | Measure-Object privateCommittedBytes -Average).Average)
        peakSampledWorkingSetBytes=[long](($samples | Measure-Object workingSetBytes -Maximum).Maximum)
        idleCpuSeconds=$Process.TotalProcessorTime.TotalSeconds-$before
        meanHandles=($samples | Measure-Object handles -Average).Average
        samples=$samples
    }
}
try {
    if ($BaselineExe) {
        $baselineData = New-TestData 'baseline'
        $baseline = Start-TestProcess $BaselineExe @('--tray') $baselineData
        Start-Sleep -Seconds 5
        $measurements += Measure-Process $baseline '1.4 tray: compressed self-contained'
        $baseline.Kill(); $baseline.WaitForExit()
    }
    $data = New-TestData 'optimized'
    $hostProcess = Start-TestProcess $OptimizedExe @('--tray') $data
    Start-Sleep -Seconds 5
    $initial = Command 'status' $data
    if (!$initial.eventDriven) { throw 'Core Audio callbacks were not registered; fallback polling is not an optimization pass.' }
    $measurements += Measure-Process $hostProcess '1.5 tray: compressed self-contained'
    $afterIdle = Command 'status' $data
    $idleRefreshes = $afterIdle.refreshes - $initial.refreshes
    if ($idleRefreshes -ne 0 -and !$AllowExternalAudioEvents) { throw 'Audio endpoints were re-enumerated while idle; on an active desktop, external audio events can invalidate this assertion.' }
    if ($idleRefreshes -eq 0) { $checks += 'No endpoint enumeration during idle sample' }
    else { $checks += "Active-desktop sample observed $idleRefreshes refreshes; no idle-enumeration assertion made" }
    $settings = Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json
    $settings.HotkeysEnabled = $true
    $settings | Add-Member OutputShortcut @{Modifiers=7;Key=135;Label='Ctrl + Alt + Shift + F24'}
    $settings | Add-Member InputShortcut @{Modifiers=7;Key=134;Label='Ctrl + Alt + Shift + F23'}
    # Match the application's atomic save so its watcher never reads a partial JSON file.
    $pendingSettings = Join-Path $data 'settings.json.tmp'
    [IO.File]::WriteAllText($pendingSettings, ($settings | ConvertTo-Json -Depth 8))
    [IO.File]::Move($pendingSettings, (Join-Path $data 'settings.json'), $true)
    Start-Sleep -Seconds 1
    $registered = Command 'status' $data
    if ($registered.conflicts.Count -ne 0) { throw 'Isolated shortcuts could not be registered.' }
    $checks += 'Settings watcher reloads isolated global shortcuts'
    $withHotkeys = Get-FileHash -LiteralPath (Join-Path $data 'settings.json') -Algorithm SHA256
    for ($cycle=0; $cycle -lt 3; $cycle++) {
        $opened = Command 'show' $data
        Start-Sleep -Seconds 3
        $state = Command 'status' $data
        if (!$state.interfaceRunning -or $state.interfacePid -eq 0) { throw 'Settings interface did not remain open.' }
        $interfaceProcess = Get-Process -Id $state.interfacePid
        $ownedProcesses.Add($interfaceProcess)
        $interfaceProcess.Refresh()
        if ($interfaceProcess.MainWindowTitle -notlike 'SonarShift*') { throw 'No branded settings window was found.' }
        if ($cycle -eq 0) {
            $measurements += Measure-Process $interfaceProcess '1.5 settings interface (background host remains separate)'
        }
        Command 'close-interface' $data | Out-Null
        if (!$interfaceProcess.WaitForExit(10000)) { throw 'Closing settings retained its interface process.' }
        Start-Sleep -Seconds 1
        if ((Command 'status' $data).conflicts.Count -ne 0) { throw 'Background shortcuts were not restored after closing settings.' }
    }
    $checks += 'Three settings open/close cycles release the UI process and restore shortcut ownership'
    for ($cycle=0; $cycle -lt 10; $cycle++) { Command 'test-notification' $data | Out-Null }
    Start-Sleep -Seconds 3
    $measurements += Measure-Process $hostProcess '1.5 tray after settings/notification stress'
    if ((Get-FileHash -LiteralPath (Join-Path $data 'settings.json') -Algorithm SHA256).Hash -ne $withHotkeys.Hash) { throw 'Opening and closing the UI changed saved preferences.' }
    $checks += 'UI lifecycle and notifications preserve settings byte for byte'
    Command 'quit' $data | Out-Null
    if (!$hostProcess.WaitForExit(10000)) { throw 'Background host did not quit.' }
    $checks += 'Explicit quit releases the background process'
    $result = [ordered]@{
        success=$true; runtime=[Environment]::Version.ToString(); os=[Environment]::OSVersion.VersionString
        logicalProcessors=[Environment]::ProcessorCount; sampleSeconds=$SampleSeconds
        externalAudioEventsAllowed=[bool]$AllowExternalAudioEvents; observedIdleRefreshes=$idleRefreshes
        baselineExeBytes=if ($BaselineExe) { (Get-Item -LiteralPath $BaselineExe).Length } else { $null }
        optimizedExeBytes=(Get-Item -LiteralPath $OptimizedExe).Length
        checks=$checks; measurements=$measurements
        hardwareLimit='Synthetic isolated settings; no real audio default changes. This does not replace physical-device testing.'
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'comparison.json') -Encoding utf8
    $result | ConvertTo-Json -Depth 8 | Write-Output
} finally {
    foreach ($process in $ownedProcesses) { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
