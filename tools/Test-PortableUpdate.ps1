param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$PreviousExecutable, [Parameter(Mandatory)][string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$PreviousExecutable = (Resolve-Path -LiteralPath $PreviousExecutable).Path
$root = [IO.Path]::GetFullPath($ReportDirectory)
$priorEnvironment = $env:SONARSHIFT_TEST_DATA
New-Item -ItemType Directory -Path $root -Force | Out-Null
try {
    $env:SONARSHIFT_TEST_DATA = $root
    $settings = Join-Path $root 'settings.json'
    '{"Initialized":true,"HotkeysEnabled":false,"AutomaticUpdates":false,"Profiles":[],"Outputs":[],"Inputs":[]}' | Set-Content -LiteralPath $settings
    $settingsHash = (Get-FileHash $settings).Hash
    $installed = Join-Path $root 'Installed.exe'
    Copy-Item -LiteralPath $PreviousExecutable -Destination $installed
    Set-Content -LiteralPath $installed -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`n" -NoNewline
    $zone = Get-Content -LiteralPath $installed -Stream Zone.Identifier -Raw
    $stage = Join-Path $root ('Updates\' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item -LiteralPath $Executable -Destination (Join-Path $stage 'SignalShift.exe')
    Copy-Item -LiteralPath $Executable -Destination (Join-Path $stage 'Updater.exe')
    $plan = @{Target=$installed; OriginalHash=(Get-FileHash $installed).Hash.ToLowerInvariant(); Hash=(Get-FileHash $Executable).Hash.ToLowerInvariant(); HostPid=0; UiPid=0; ShowWindow=$false}
    $manifest = Join-Path $stage 'plan.json'
    $plan | ConvertTo-Json | Set-Content -LiteralPath $manifest
    $helper = Start-Process -FilePath (Join-Path $stage 'Updater.exe') -ArgumentList @('--apply-update', ('"'+$manifest+'"')) -WindowStyle Hidden -PassThru
    if (!$helper.WaitForExit(45000) -or $helper.ExitCode -ne 0) { throw 'Update helper failed or timed out.' }
    $report = Join-Path $root 'host.json'
    Start-Process $installed -ArgumentList @('--host-command','status','--report',('"'+$report+'"')) -WindowStyle Hidden -Wait
    $state = Get-Content $report -Raw | ConvertFrom-Json
    if ((Get-FileHash $installed).Hash -ne (Get-FileHash $Executable).Hash) { throw 'Installed bytes differ from candidate.' }
    if ((Get-Content $installed -Stream Zone.Identifier -Raw) -cne $zone) { throw 'Download security zone changed.' }
    if ((Get-FileHash $settings).Hash -ne $settingsHash) { throw 'Settings changed.' }
    if ($state.wpfLoaded -or $state.interfaceRunning -or $state.audioTestsLoaded) { throw 'Tray restart loaded UI or audio-test libraries.' }
    @{success=$true; version=$state.version; sameFilename=$true; settingsPreserved=$true; downloadZonePreserved=$true; trayOnly=$true; nativeCallbacks=$state.eventDriven} | ConvertTo-Json | Set-Content (Join-Path $root 'update-report.json')
    Get-Content (Join-Path $root 'update-report.json')
}
finally {
    if (Test-Path -LiteralPath (Join-Path $root 'Installed.exe')) { Start-Process (Join-Path $root 'Installed.exe') -ArgumentList @('--host-command','quit') -WindowStyle Hidden -Wait }
    $env:SONARSHIFT_TEST_DATA = $priorEnvironment
}
