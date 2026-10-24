$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (!$msbuild) { throw 'Visual Studio C++ build tools are required for the optional camera component.' }
nuget restore "$PSScriptRoot/Native/packages.config" -PackagesDirectory "$PSScriptRoot/Native/packages" -NonInteractive
if ($LASTEXITCODE -ne 0) { throw 'Camera dependency restore failed.' }
foreach ($project in 'SignalShift.Camera', 'SignalShift.CameraSetup', 'SignalShift.CameraTests') {
    if ($project -eq 'SignalShift.CameraSetup') {
        $hash = (Get-FileHash "$root/dist/camera/SignalShift.Camera.dll" -Algorithm SHA256).Hash
        $bytes = for ($i=0; $i -lt 64; $i+=2) { '0x' + $hash.Substring($i,2) }
        [IO.File]::WriteAllText("$PSScriptRoot/Native/CameraExpectedHash.h", ('#pragma once' + "`n" + 'inline constexpr unsigned char ExpectedCameraHash[32] = {' + ($bytes -join ',') + '};' + "`n"))
    }
    & $msbuild "$PSScriptRoot/Native/$project.vcxproj" /p:Configuration=Release /p:Platform=x64 /m /verbosity:minimal
    if ($LASTEXITCODE -ne 0) { throw "Native camera build failed: $project" }
}
Copy-Item "$PSScriptRoot/Native/MICROSOFT-LICENSE.txt", "$PSScriptRoot/Native/THIRD-PARTY-NOTICES.md", "$PSScriptRoot/README.md" "$root/dist/camera/"
$checksums = foreach ($name in 'SignalShift.Camera.dll', 'SignalShift.CameraSetup.exe') {
    (Get-FileHash "$root/dist/camera/$name" -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name
}
[IO.File]::WriteAllLines("$root/dist/camera/SHA256SUMS.txt", $checksums, [Text.Encoding]::ASCII)
