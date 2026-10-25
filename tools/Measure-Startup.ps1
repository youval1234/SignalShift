param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$ReportDirectory, [int]$Samples=5)
$ErrorActionPreference='Stop'
$Executable=(Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory=[IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force|Out-Null
$results=@()
for($index=0;$index -lt $Samples;$index++){
    $data=Join-Path $ReportDirectory ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $data|Out-Null
    '{"Initialized":true,"HotkeysEnabled":false,"AutomaticUpdates":false,"Outputs":[],"Inputs":[],"Profiles":[]}'|Set-Content (Join-Path $data 'settings.json')
    $start=[Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute=$false
    $start.Environment['SONARSHIFT_TEST_DATA']=$data
    $start.Environment['SIGNALSHIFT_STARTUP_TICKS']=[Diagnostics.Stopwatch]::GetTimestamp().ToString()
    $process=[Diagnostics.Process]::Start($start)
    try{
        $watch=[Diagnostics.Stopwatch]::StartNew();$frame=$null;$ready=$null;$events=@()
        while((!$frame -or !$ready) -and $watch.Elapsed.TotalSeconds -lt 20){
            $events=@(Get-ChildItem $data -Filter 'startup-*.jsonl'|ForEach-Object{Get-Content $_.FullName|ForEach-Object{try{$_|ConvertFrom-Json}catch{}}})
            $frame=$events|Where-Object phase -eq 'first-frame'|Select-Object -First 1
            $ready=$events|Where-Object phase -eq 'page-ready'|Select-Object -First 1
            if(!$frame -or !$ready){Start-Sleep -Milliseconds 20}
        }
        if(!$frame -or !$ready){throw 'No first-frame startup event received'}
        $results+=@{sample=$index;firstFrameMs=$frame.elapsedMs;pageReadyMs=$ready.elapsedMs;events=$events}
    }finally{
        $quit=[Diagnostics.ProcessStartInfo]::new($Executable);$quit.UseShellExecute=$false;$quit.CreateNoWindow=$true
        $quit.Environment['SONARSHIFT_TEST_DATA']=$data
        $quit.ArgumentList.Add('--host-command');$quit.ArgumentList.Add('quit')
        $client=[Diagnostics.Process]::Start($quit);if(!$client.WaitForExit(10000)){throw 'Test quit client timed out'}
        if(!$process.WaitForExit(10000)){throw 'Test host did not exit'}
        $process.Dispose();$client.Dispose()
    }
}
$results|ConvertTo-Json -Depth 8|Set-Content (Join-Path $ReportDirectory 'samples.json')
$sorted=@($results.firstFrameMs|Sort-Object)
$readySorted=@($results.pageReadyMs|Sort-Object)
@{samples=$Samples;medianFirstFrameMs=$sorted[[int][math]::Floor($Samples/2)];medianPageReadyMs=$readySorted[[int][math]::Floor($Samples/2)];minMs=$sorted[0];maxMs=$sorted[-1]}|ConvertTo-Json
