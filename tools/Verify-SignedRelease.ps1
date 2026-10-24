param(
    [Parameter(Mandatory)][string]$ExePath,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ExpectedSubject,
    [string]$ExpectedHash,
    [switch]$WriteChecksum,
    [switch]$CheckChecksum
)
$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid release tag.' }
if ($ExpectedHash -and $ExpectedHash -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid expected SHA-256.' }
$exe = Get-Item -LiteralPath $ExePath
if ($exe.Name -cne 'SignalShift.exe' -or $exe.PSIsContainer) { throw 'Only SignalShift.exe can be released.' }
$signature = Get-AuthenticodeSignature -LiteralPath $exe.FullName
if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate) {
    throw "Trusted Authenticode signature required; received $($signature.Status)."
}
if ($signature.SignerCertificate.Subject -cne $ExpectedSubject) { throw 'Unexpected signing publisher.' }
if (!$signature.TimeStamperCertificate) { throw 'A trusted timestamp is required.' }
$version = $exe.VersionInfo
if ($version.ProductName -cne 'SignalShift' -or $version.CompanyName -cne 'SignalShift' -or $version.OriginalFilename -cne 'SignalShift.dll') {
    throw 'Signed executable metadata does not match SonarShift.'
}
$expectedVersion = $Tag.Substring(1) + '.0'
if ($version.FileVersion -cne $expectedVersion) { throw 'Signed executable version does not match the release tag.' }
$hash = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ExpectedHash -and $hash -cne $ExpectedHash.ToLowerInvariant()) { throw 'Signed artifact changed between jobs.' }
$checksumPath = Join-Path $exe.DirectoryName 'SHA256SUMS.txt'
$checksum = "$hash  SignalShift.exe"
if ($CheckChecksum -and (Get-Content -LiteralPath $checksumPath -Raw).TrimEnd("`r", "`n") -cne $checksum) {
    throw 'Release checksum does not match the signed executable.'
}
# Never write a checksum or success output until all requested checks passed.
if ($WriteChecksum) { Set-Content -LiteralPath $checksumPath -Value $checksum -Encoding ascii }
if ($env:GITHUB_OUTPUT) { "sha256=$hash" >> $env:GITHUB_OUTPUT }
Write-Host "Verified $Tag, trusted publisher and timestamp; SHA-256: $hash"
