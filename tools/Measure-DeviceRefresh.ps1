param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$ReportDirectory, [switch]$AssertFastPath)
$ErrorActionPreference='Stop'
$Executable=(Resolve-Path -LiteralPath $Executable).Path
$ReportDirectory=[IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force|Out-Null
@{Initialized=$true;HotkeysEnabled=$false;AutomaticUpdates=$false;Outputs=@();Inputs=@();Profiles=@(@{Id='sample-a';Name='Desk';OutputId='sample-output-a'},@{Id='sample-b';Name='Calls';InputId='sample-input-b'})}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $ReportDirectory 'settings.json')
$env:SONARSHIFT_TEST_DATA=$ReportDirectory
$hostProcess=Start-Process $Executable -ArgumentList '--tray' -WindowStyle Hidden -PassThru
function Command([string]$command){
    $path=Join-Path $ReportDirectory ($command+'.json')
    $process=Start-Process $Executable -ArgumentList @('--host-command',$command,'--report',('"'+$path+'"')) -WindowStyle Hidden -PassThru
    if(!$process.WaitForExit(15000) -or $process.ExitCode -ne 0){throw 'Refresh probe client failed'}
    Get-Content $path -Raw|ConvertFrom-Json
}
try{
    Command 'status'|Out-Null
    $results=@()
    foreach($kind in @('defaults','mute')){
        Command ('test-refresh-'+$kind)|Out-Null
        $samples=@();$count=0
        while($count -lt 100){$sample=Command ('test-refresh-'+$kind);$samples+=$sample;$count+=$sample.iterations}
        $results+=@{scenario=$kind;iterations=$count;elapsedMs=($samples|Measure-Object elapsedMs -Sum).Sum;allocatedBytes=($samples|Measure-Object allocatedBytes -Sum).Sum;fullEnumerations=($samples|Measure-Object fullEnumerations -Sum).Sum;propertyReads=($samples|Measure-Object propertyReads -Sum).Sum;samples=$samples}
    }
    $results|ConvertTo-Json -Depth 6|Set-Content (Join-Path $ReportDirectory 'results.json')
    if($AssertFastPath){foreach($result in $results){if($result.fullEnumerations -ne 0 -or $result.propertyReads -ne 0 -or $result.allocatedBytes -gt 512000){throw 'Known-state refresh regressed to full scans or excessive allocation'}}}
    $results|Select-Object scenario,iterations,elapsedMs,allocatedBytes,fullEnumerations,propertyReads|ConvertTo-Json
}finally{
    Command 'quit'|Out-Null
    if(!$hostProcess.WaitForExit(10000)){throw 'Probe host did not exit'}
}
