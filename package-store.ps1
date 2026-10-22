param(
    [switch]$Draft,
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$Version = '1.3.0.0',
    [string]$MakeAppxPath
)
$ErrorActionPreference = 'Stop'
# Store certification supplies the trusted signature. This script never installs
# certificates, changes Windows protection, or installs/launches the package.
if ($Draft) {
    $IdentityName = 'Sonora.Draft'
    $Publisher = 'CN=SonoraDraft'
    $PublisherDisplayName = 'Sonora draft - not a verified publisher'
} elseif (!$IdentityName -or !$Publisher -or !$PublisherDisplayName) {
    throw 'Copy Identity Name, Publisher and PublisherDisplayName from Partner Center > Product identity, or use -Draft for packaging validation only.'
}
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Invalid package Identity Name.' }
$packageVersion = [version]$Version
if ($packageVersion.Revision -ne 0 -or $packageVersion.Major -lt 1 -or ($packageVersion.Major, $packageVersion.Minor, $packageVersion.Build | Where-Object { $_ -lt 0 -or $_ -gt 65535 })) {
    throw 'Use a four-part Store version such as 1.3.0.0; the final component must be zero.'
}
if (!$MakeAppxPath) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $MakeAppxPath = Get-ChildItem -Path "$sdkBin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$MakeAppxPath -or !(Test-Path -LiteralPath $MakeAppxPath)) { throw 'Provide -MakeAppxPath pointing to the x64 MakeAppx.exe from the official Windows SDK or Microsoft.Windows.SDK.BuildTools NuGet package.' }
$MakeAppxPath = (Resolve-Path -LiteralPath $MakeAppxPath).Path
if ((Get-AuthenticodeSignature -LiteralPath $MakeAppxPath).Status -ne 'Valid') { throw 'The Windows SDK packaging tool must have a valid Microsoft signature.' }
$outputRoot = Join-Path $PSScriptRoot 'dist\store'
# Each run gets a new staging directory, so removed build files never leak into a package.
$stage = Join-Path $outputRoot ('staging-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Push-Location $PSScriptRoot
try {
    dotnet publish Sonora.csproj -c Release -r win-x64 --self-contained true -p:SonoraStore=true -p:PublishSingleFile=false -p:EnableCompressionInSingleFile=false -p:DebugType=None -o $stage
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
    $image = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'Assets\Sonora.png'))
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
    $package = Join-Path $outputRoot ("Sonora-$Version-x64$suffix.msix")
    & $MakeAppxPath pack /d $stage /p $package /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX validation/packaging failed.' }
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($package + '.sha256'), "$hash  $([IO.Path]::GetFileName($package))`n", [Text.Encoding]::ASCII)
    Write-Host "Unsigned Store submission package: $package"
    if ($Draft) { Write-Warning 'DRAFT identity only. Not ready for submission or installation. Rebuild with the actual Product identity after publisher verification.' }
    else { Write-Warning 'Upload to Partner Center for certification and Microsoft signing. This unsigned file does not fix Smart App Control by itself.' }
} finally { Pop-Location }
