param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$ExpectedHash,
    [Parameter(Mandatory)][string]$ExpectedSubject,
    [Parameter(Mandatory)][string]$ExePath
)
$ErrorActionPreference = 'Stop'
if (!$env:GITHUB_ACTIONS -or $env:GITHUB_REPOSITORY -cne 'youval1234/Sonora' -or !$env:GH_TOKEN) {
    throw 'Publication is restricted to the Sonora GitHub Actions workflow.'
}
& "$PSScriptRoot/Resolve-ReleaseSource.ps1" -Tag $Tag -ExpectedCommit $Commit
& "$PSScriptRoot/Verify-SignedRelease.ps1" -ExePath $ExePath -Tag $Tag -ExpectedSubject $ExpectedSubject -ExpectedHash $ExpectedHash -CheckChecksum
$exe = (Get-Item -LiteralPath $ExePath).FullName
$checksum = Join-Path (Split-Path $exe -Parent) 'SHA256SUMS.txt'
$repo = $env:GITHUB_REPOSITORY
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$response = Invoke-WebRequest -Uri "https://api.github.com/repos/$repo/releases/tags/$Tag" -Headers $headers -SkipHttpErrorCheck
$marker = "<!-- Sonora signed-release automation: $Commit -->"
if ($response.StatusCode -eq 200) {
    $release = $response.Content | ConvertFrom-Json
    if (!$release.draft) { throw 'This release is already public. Use a new version tag; public releases are never silently replaced.' }
    if (!$release.body -or !$release.body.Contains($marker)) { throw 'Existing draft belongs to a different source commit or release process.' }
    gh release upload $Tag $exe $checksum --repo $repo --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Draft upload failed; release remains unpublished.' }
} elseif ($response.StatusCode -eq 404) {
    $notesPath = Join-Path $env:RUNNER_TEMP 'Sonora-release-notes.md'
    @(
        $marker,
        "Portable Windows x64 release built from reviewed source: $Commit.",
        '',
        'Sonora.exe has a verified, timestamped Authenticode signature from the configured publisher. SHA256SUMS.txt covers the signed executable.',
        '',
        'A valid signature does not guarantee acceptance by every Windows reputation service or organization policy.',
        '',
        "Build and signing run: https://github.com/$repo/actions/runs/$env:GITHUB_RUN_ID"
    ) | Set-Content -LiteralPath $notesPath -Encoding utf8
    gh release create $Tag $exe $checksum --repo $repo --verify-tag --draft --title "Sonora $($Tag.Substring(1))" --notes-file $notesPath
    if ($LASTEXITCODE -ne 0) { throw 'Draft creation failed; no public release created.' }
} else { throw "Cannot inspect existing release (HTTP $($response.StatusCode)); publication stopped." }

# Upload failures leave a draft. Check stored assets before making it public.
$downloadDirectory = Join-Path $env:RUNNER_TEMP "Sonora-release-check-$([guid]::NewGuid())"
gh release download $Tag --repo $repo --dir $downloadDirectory --pattern Sonora.exe --pattern SHA256SUMS.txt
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify draft downloads; release remains unpublished.' }
& "$PSScriptRoot/Verify-SignedRelease.ps1" -ExePath (Join-Path $downloadDirectory 'Sonora.exe') -Tag $Tag -ExpectedSubject $ExpectedSubject -ExpectedHash $ExpectedHash -CheckChecksum
& "$PSScriptRoot/Resolve-ReleaseSource.ps1" -Tag $Tag -ExpectedCommit $Commit
gh release edit $Tag --repo $repo --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Publication failed; inspect the draft before retrying.' }
"Published verified signed release: https://github.com/$repo/releases/tag/$Tag" >> $env:GITHUB_STEP_SUMMARY
