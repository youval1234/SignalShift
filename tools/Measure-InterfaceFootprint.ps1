param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$ReportPath,
    [int]$SampleSeconds = 10
)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath)) | Out-Null
$data = Join-Path ([IO.Path]::GetTempPath()) ('SonarShift-memory-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($data) | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'settings.json'), '{"Initialized":true,"HotkeysEnabled":false,"Outputs":[],"Inputs":[],"Profiles":[]}')
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
function Launch([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.Environment['SONARSHIFT_TEST_DATA'] = $data
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $owned.Add($process)
    return $process
}
function Command([string]$Name) {
    $path = Join-Path $data ([guid]::NewGuid().ToString('N') + '.json')
    $process = Launch @('--host-command', $Name, '--report', $path)
    if (!$process.WaitForExit(10000) -or $process.ExitCode -ne 0) { throw "Command failed: $Name" }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}
function Sample([Diagnostics.Process[]]$Processes, [string]$Scenario) {
    $samples = @()
    for ($index = 0; $index -lt $SampleSeconds; $index++) {
        Start-Sleep -Seconds 1
        $counters = @(Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "Name LIKE 'SonarShift%'" -ErrorAction Stop)
        $rows = @(foreach ($process in $Processes) {
            $process.Refresh()
            if ($process.HasExited) { throw 'Measured process exited.' }
            $counter = $counters | Where-Object { $_.IDProcess -eq $process.Id } | Select-Object -First 1
            if (!$counter) { throw 'Private working-set counter missing.' }
            [pscustomobject]@{ pid=$process.Id; workingSetBytes=$process.WorkingSet64; privateWorkingSetBytes=[long]$counter.WorkingSetPrivate; privateCommittedBytes=$process.PrivateMemorySize64 }
        })
        $samples += [pscustomobject]@{
            processes=$rows
            workingSetBytes=[long](($rows | Measure-Object workingSetBytes -Sum).Sum)
            privateWorkingSetBytes=[long](($rows | Measure-Object privateWorkingSetBytes -Sum).Sum)
            privateCommittedBytes=[long](($rows | Measure-Object privateCommittedBytes -Sum).Sum)
        }
    }
    [pscustomobject]@{
        scenario=$Scenario; processCount=$Processes.Count
        meanWorkingSetBytes=[long](($samples | Measure-Object workingSetBytes -Average).Average)
        meanPrivateWorkingSetBytes=[long](($samples | Measure-Object privateWorkingSetBytes -Average).Average)
        meanPrivateCommittedBytes=[long](($samples | Measure-Object privateCommittedBytes -Average).Average)
        samples=$samples
    }
}
try {
    $hostProcess = Launch @('--tray')
    Start-Sleep -Seconds 5
    $initial = Command 'status'
    if (!$initial.eventDriven) { throw 'Audio callbacks unavailable.' }
    $measurements = @(Sample @($hostProcess) 'Tray only')
    Command 'show' | Out-Null
    Start-Sleep -Seconds 5
    $state = Command 'status'
    if (!$state.interfaceRunning) { throw 'Interface did not start.' }
    $interface = Get-Process -Id $state.interfacePid
    $owned.Add($interface)
    $measurements += Sample @($hostProcess, $interface) 'Settings open: all app processes combined'
    Command 'close-interface' | Out-Null
    if (!$interface.WaitForExit(10000)) { throw 'Interface retained after close.' }
    Command 'quit' | Out-Null
    if (!$hostProcess.WaitForExit(10000)) { throw 'Host did not exit.' }
    $result = [pscustomobject]@{
        executableBytes=(Get-Item -LiteralPath $Executable).Length
        executableSha256=(Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash
        os=[Environment]::OSVersion.VersionString; sampleSeconds=$SampleSeconds
        measurements=$measurements
        notes='Same isolated settings and hardware. Combined working sets double-count shared pages; private working sets do not. No physical device switches performed.'
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    $measurements | Select-Object -ExcludeProperty samples | ConvertTo-Json
} finally {
    foreach ($process in $owned) { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
}
