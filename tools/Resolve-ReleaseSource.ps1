param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$ExpectedCommit,
    [string]$RepositoryPath = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Invalid release tag. Expected vMAJOR.MINOR.PATCH.'
}
if ($ExpectedCommit -and $ExpectedCommit -notmatch '^[a-fA-F0-9]{40}$') { throw 'Invalid expected commit.' }

# Public repository: refreshing refs needs no persisted GitHub credentials.
git -C $RepositoryPath fetch --no-tags origin '+refs/heads/main:refs/remotes/origin/main' "+refs/tags/${Tag}:refs/tags/${Tag}"
if ($LASTEXITCODE -ne 0) { throw 'Cannot fetch the release tag and main.' }
$commit = git -C $RepositoryPath rev-parse --verify "refs/tags/$Tag^{commit}"
if ($LASTEXITCODE -ne 0) { throw 'Release tag does not resolve to a commit.' }
if ($ExpectedCommit -and $commit -cne $ExpectedCommit.ToLowerInvariant()) {
    throw 'The release tag moved. Refusing to sign or publish different source.'
}
git -C $RepositoryPath merge-base --is-ancestor $commit refs/remotes/origin/main
if ($LASTEXITCODE -ne 0) { throw 'Release source must be reachable from reviewed main.' }
$projectText = git -C $RepositoryPath show "${commit}:SonarShift.csproj"
if ($LASTEXITCODE -ne 0) { throw 'Tagged source has no SonarShift project.' }
[xml]$project = $projectText -join "`n"
$versions = @($project.Project.PropertyGroup.Version | Where-Object { $_ })
if ($versions.Count -ne 1 -or $versions[0] -cne $Tag.Substring(1)) {
    throw 'The project version must match the release tag.'
}
if ($env:GITHUB_OUTPUT) { "commit=$commit" >> $env:GITHUB_OUTPUT }
Write-Host "Reviewed source: $Tag at $commit"
