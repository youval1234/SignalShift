param([Parameter(Mandatory)][string]$ExePath)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) "Sonora-release-tests-$([guid]::NewGuid())"
$null = New-Item -ItemType Directory -Path $root
$passed = 0
function Reject([scriptblock]$Action, [string]$Message) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        $script:passed++
        return
    }
    throw "Expected rejection: $Message"
}
function Invoke-FixtureGit([string[]]$Arguments) {
    & git @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Fixture git operation failed: $Arguments" }
}

# These are disposable local Git fixtures; they never push to GitHub.
$remote = Join-Path $root 'origin.git'
$source = Join-Path $root 'source'
Invoke-FixtureGit @('init', '--bare', '--quiet', $remote)
Invoke-FixtureGit @('init', '--quiet', '--initial-branch=main', $source)
Invoke-FixtureGit @('-C', $source, 'config', 'user.name', 'Release tests')
Invoke-FixtureGit @('-C', $source, 'config', 'user.email', 'tests@example.invalid')
Invoke-FixtureGit @('-C', $source, 'remote', 'add', 'origin', $remote)
'<Project><PropertyGroup><Version>1.3.0</Version></PropertyGroup></Project>' | Set-Content (Join-Path $source 'Sonora.csproj')
Invoke-FixtureGit @('-C', $source, 'add', 'Sonora.csproj')
Invoke-FixtureGit @('-C', $source, 'commit', '--quiet', '-m', 'Reviewed source fixture')
Invoke-FixtureGit @('-C', $source, 'tag', 'v1.3.0')
Invoke-FixtureGit @('-C', $source, 'tag', 'v9.0.0')
Invoke-FixtureGit @('-C', $source, 'checkout', '--quiet', '-b', 'unreviewed')
'<Project><PropertyGroup><Version>8.0.0</Version></PropertyGroup></Project>' | Set-Content (Join-Path $source 'Sonora.csproj')
Invoke-FixtureGit @('-C', $source, 'commit', '--quiet', '-am', 'Unreviewed source fixture')
Invoke-FixtureGit @('-C', $source, 'tag', 'v8.0.0')
Invoke-FixtureGit @('-C', $source, 'push', '--quiet', 'origin', 'main', '--tags')
& "$PSScriptRoot/Resolve-ReleaseSource.ps1" -RepositoryPath $source -Tag v1.3.0
$passed++
Reject { & "$PSScriptRoot/Resolve-ReleaseSource.ps1" -RepositoryPath $source -Tag '../main' } 'Invalid release tag'
Reject { & "$PSScriptRoot/Resolve-ReleaseSource.ps1" -RepositoryPath $source -Tag v1.3.0 -ExpectedCommit ('0' * 40) } 'release tag moved'
Reject { & "$PSScriptRoot/Resolve-ReleaseSource.ps1" -RepositoryPath $source -Tag v9.0.0 } 'project version must match'
Reject { & "$PSScriptRoot/Resolve-ReleaseSource.ps1" -RepositoryPath $source -Tag v8.0.0 } 'reachable from reviewed main'

$exe = Join-Path $root 'Sonora.exe'
Copy-Item -LiteralPath $ExePath -Destination $exe
[xml]$project = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'Sonora.csproj') -Raw
$tag = 'v' + $project.Project.PropertyGroup.Version
$verifier = "$PSScriptRoot/Verify-SignedRelease.ps1"
$subject = 'CN=Release test fixture, O=Not a real signing publisher'
Reject { & $verifier -ExePath $exe -Tag $tag -ExpectedSubject $subject -WriteChecksum } 'Trusted Authenticode signature required'
if (Test-Path (Join-Path $root 'SHA256SUMS.txt')) { throw 'Unsigned input produced a checksum.' }

# Simulated signature results test the policy only. No certificate is created,
# installed or used to sign anything. The first test above uses the real OS verifier.
Write-Host 'Testing simulated signature policies; these fixtures are not real signed builds.'
function Get-AuthenticodeSignature {
    param($LiteralPath)
    [pscustomobject]@{ Status = $status; SignerCertificate = [pscustomobject]@{ Subject = $subject }; TimeStamperCertificate = $timestamp }
}
$status = 'Valid'
$timestamp = [pscustomobject]@{ Subject = 'Test timestamp' }
& $verifier -ExePath $exe -Tag $tag -ExpectedSubject $subject -WriteChecksum
$passed++
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
& $verifier -ExePath $exe -Tag $tag -ExpectedSubject $subject -ExpectedHash $hash -CheckChecksum
$passed++
Reject { & $verifier -ExePath $exe -Tag $tag -ExpectedSubject 'CN=Wrong publisher' } 'Unexpected signing publisher'
$timestamp = $null
Reject { & $verifier -ExePath $exe -Tag $tag -ExpectedSubject $subject } 'timestamp is required'
$timestamp = [pscustomobject]@{ Subject = 'Test timestamp' }
$status = 'HashMismatch'
Reject { & $verifier -ExePath $exe -Tag $tag -ExpectedSubject $subject } 'Trusted Authenticode signature required'
$status = 'Valid'
Reject { & $verifier -ExePath $exe -Tag v99.0.0 -ExpectedSubject $subject } 'version does not match'
Reject { & $verifier -ExePath $exe -Tag $tag -ExpectedSubject $subject -ExpectedHash ('0' * 64) } 'artifact changed'
'wrong checksum' | Set-Content (Join-Path $root 'SHA256SUMS.txt')
Reject { & $verifier -ExePath $exe -Tag $tag -ExpectedSubject $subject -CheckChecksum } 'checksum does not match'
Remove-Item Function:Get-AuthenticodeSignature
Write-Host "$passed release automation checks passed. Fixtures: $root"
