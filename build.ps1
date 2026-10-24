param([switch]$Test, [switch]$AudioRoundtrip, [switch]$Compact)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $compression = $Compact.IsPresent.ToString().ToLowerInvariant()
    dotnet publish SignalShift.csproj -c Release -r win-x64 --self-contained true -p:EnableCompressionInSingleFile=$compression -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    if ($Test -or $AudioRoundtrip) {
        $report = Join-Path $PSScriptRoot ("dist\diagnostics-$([guid]::NewGuid().ToString('N')).json")
        $arguments = @('--diagnostics', '--report', ('"' + $report + '"'))
        if ($AudioRoundtrip) { $arguments += '--audio-roundtrip' }
        $diagnosticProcess = Start-Process -FilePath 'dist\SignalShift.exe' -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        if ($diagnosticProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw 'The diagnostic process failed or did not produce a fresh report.' }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        $result.checks | Where-Object { $_.test } | Format-Table test,passed
        if (!$result.success) { throw 'Diagnostics failed; inspect dist\diagnostics.json' }
    }
    $hash = (Get-FileHash -LiteralPath 'dist\SignalShift.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath 'dist\SHA256SUMS.txt' -Value ($hash + '  SignalShift.exe') -Encoding ascii
    Write-Host 'Portable executable: dist\SignalShift.exe'
}
finally { Pop-Location }
