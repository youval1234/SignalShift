param(
    [switch]$Draft,
    [switch]$UseStoreIdentity,
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$Version,
    [string]$MakeAppxPath
)
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SignalShift.csproj') -Raw
$appVersion = [string]$project.Project.PropertyGroup.Version
if (!$Version) { $Version = $appVersion + '.0' }
# Store certification supplies the trusted signature. This script never installs
# certificates, changes Windows protection, or installs/launches the package.
if ($Draft -and $UseStoreIdentity) { throw 'Choose either -Draft or -UseStoreIdentity.' }
if ($UseStoreIdentity) {
    $identity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Packaging/StoreIdentity.json') -Raw | ConvertFrom-Json
    $IdentityName = $identity.identityName
    $Publisher = $identity.publisher
    $PublisherDisplayName = $identity.publisherDisplayName
}
if ($Draft) {
    $IdentityName = 'SonarShift.Draft'
    $Publisher = 'CN=SonarShiftDraft'
    $PublisherDisplayName = 'SonarShift draft - not a verified publisher'
} elseif (!$IdentityName -or !$Publisher -or !$PublisherDisplayName) {
    throw 'Copy Identity Name, Publisher and PublisherDisplayName from Partner Center > Product identity, or use -Draft for packaging validation only.'
}
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Invalid package Identity Name.' }
$packageVersion = [version]$Version
if ($packageVersion.Revision -ne 0 -or $packageVersion.Major -lt 1 -or ($packageVersion.Major, $packageVersion.Minor, $packageVersion.Build | Where-Object { $_ -lt 0 -or $_ -gt 65535 })) {
    throw 'Use a four-part Store version such as 1.4.0.0; the final component must be zero.'
}
if ($Version -cne ($appVersion + '.0')) { throw 'The Store package version must match SignalShift.csproj, with a final .0 component.' }
if (!$MakeAppxPath) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $MakeAppxPath = Get-ChildItem -Path "$sdkBin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$MakeAppxPath -or !(Test-Path -LiteralPath $MakeAppxPath)) { throw 'Provide -MakeAppxPath pointing to the x64 MakeAppx.exe from the official Windows SDK or Microsoft.Windows.SDK.BuildTools NuGet package.' }
$MakeAppxPath = (Resolve-Path -LiteralPath $MakeAppxPath).Path
$toolSignature = Get-AuthenticodeSignature -LiteralPath $MakeAppxPath
if ($toolSignature.Status -ne 'Valid' -or $toolSignature.SignerCertificate.Subject -notmatch '(?:^|,\s*)CN=Microsoft Corporation(?:,|$)') {
    throw 'The Windows SDK packaging tool must have a valid Microsoft signature.'
}
$outputRoot = Join-Path $PSScriptRoot 'dist\store'
# Each run gets a new staging directory, so removed build files never leak into a package.
$stage = Join-Path $outputRoot ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Push-Location $PSScriptRoot
try {
    dotnet publish SignalShift.csproj -c Release -r win-x64 --self-contained true -p:SonarShiftStore=true -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false -p:DebugType=None -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Store build failed.' }
    $manifest = Get-Content -LiteralPath 'Packaging\AppxManifest.xml' -Raw
    foreach ($entry in @{
        '__IDENTITY_NAME__' = $IdentityName; '__PUBLISHER__' = $Publisher;
        '__PUBLISHER_DISPLAY_NAME__' = $PublisherDisplayName; '__VERSION__' = $Version
    }.GetEnumerator()) { $manifest = $manifest.Replace($entry.Key, [System.Security.SecurityElement]::Escape($entry.Value)) }
    [IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
    Add-Type -AssemblyName System.Drawing
    $assets = Join-Path $stage 'StoreAssets'
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    $image = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'Assets\SonarShift.png'))
    try {
        foreach ($asset in @{'StoreLogo.png'=50; 'Square150x150Logo.png'=150; 'Square44x44Logo.png'=44}.GetEnumerator()) {
            $bitmap = [Drawing.Bitmap]::new($asset.Value, $asset.Value)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($image, 0, 0, $asset.Value, $asset.Value)
                $bitmap.Save((Join-Path $assets $asset.Key), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $image.Dispose() }
    $suffix = if ($Draft) { '-DRAFT-unsigned' } else { '-StoreSubmission-unsigned' }
    $package = Join-Path $outputRoot ("SignalShift-$Version-x64$suffix.msix")
    & $MakeAppxPath pack /d $stage /p $package /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX validation/packaging failed.' }
    $verification = Join-Path $outputRoot ('verification-' + [guid]::NewGuid().ToString('N'))
    & $MakeAppxPath unpack /p $package /d $verification /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX unpack validation failed.' }
    $payload = @()
    foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $unpacked = Join-Path $verification $relative
        $expected = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if (!(Test-Path -LiteralPath $unpacked) -or (Get-FileHash -LiteralPath $unpacked -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) {
            throw "Packaged file did not survive the roundtrip: $relative"
        }
        if ($relative -match '(?i)(\.pdb$|\.pfx$|\.key$|(^|\\)(settings\.json|diagnostics[^\\]*\.json)$)') {
            throw "Private or debug file must not be packaged: $relative"
        }
        $payload += [ordered]@{ file = $relative.Replace('\', '/'); sha256 = $expected }
    }
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($package + '.sha256'), "$hash  $([IO.Path]::GetFileName($package))`n", [Text.Encoding]::ASCII)
    $receipt = [ordered]@{
        draft = [bool]$Draft; version = $Version; identityName = $IdentityName
        publisher = $Publisher; publisherDisplayName = $PublisherDisplayName
        packageSha256 = $hash; payload = $payload
        validation = 'MakeAppx pack/unpack and staged payload hashes; not Store certification or installed runtime testing'
    }
    [IO.File]::WriteAllText(($package + '.build.json'), ($receipt | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Write-Host "Unsigned Store submission package: $package"
    Write-Host "Verified $($payload.Count) staged files against the unpacked MSIX."
    if ($Draft) { Write-Warning 'DRAFT identity only. Not ready for submission or installation. Rebuild with the actual Product identity after publisher verification.' }
    else { Write-Warning 'Upload to Partner Center for certification and Microsoft signing. This unsigned file does not fix Smart App Control by itself.' }
} finally { Pop-Location }
