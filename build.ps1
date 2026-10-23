param([switch]$Test, [switch]$AudioRoundtrip)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish SonarShift.csproj -c Release -r win-x64 --self-contained true -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    if ($Test -or $AudioRoundtrip) {
        $report = Join-Path $PSScriptRoot ("dist\diagnostics-$([guid]::NewGuid().ToString('N')).json")
        $arguments = @('--diagnostics', '--report', ('"' + $report + '"'))
        if ($AudioRoundtrip) { $arguments += '--audio-roundtrip' }
        $diagnosticProcess = Start-Process -FilePath 'dist\SonarShift.exe' -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        if ($diagnosticProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)) { throw 'The diagnostic process failed or did not produce a fresh report.' }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        $result.checks | Where-Object { $_.test } | Format-Table test,passed
        if (!$result.success) { throw 'Diagnostics failed; inspect dist\diagnostics.json' }
    }
    $hash = (Get-FileHash -LiteralPath 'dist\SonarShift.exe' -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath 'dist\SHA256SUMS.txt' -Value ($hash + '  SonarShift.exe') -Encoding ascii
    Write-Host 'Portable executable: dist\SonarShift.exe'
}
finally { Pop-Location }
