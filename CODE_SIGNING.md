# Code signing policy

**Status (2026-10-23): automation prepared, signing unavailable. SignPath Foundation declined the application because SonarShift has not yet established sufficient public visibility. No signing credentials are configured, and existing release executables are unsigned.** GitHub hosting, checksums and build provenance do not replace trusted Authenticode signatures. Smart App Control may block current releases.

The workflow supports an approved production policy through [SignPath.io](https://signpath.io). A future [SignPath Foundation](https://signpath.org) approval or an eligible organization/sponsor would need to supply the trusted certificate. There is no active signing sponsorship. To respect the owner's privacy preference, do not configure an individual certificate displaying their personal name, or substitute a self-signed certificate.

## Scope and roles

- Author, reviewer and release approver: repository owner [youval1234](https://github.com/youval1234).
- Contributors submit pull requests; the owner reviews changes before merging.
- Maintainers must use multifactor authentication for GitHub and SignPath before signing is enabled. This document does not attest to account settings that have not been verified.
- Every signing request requires manual release approval in SignPath. No unreviewed pull-request build is eligible for signing.
- Only `SonarShift.exe`, built from this repository's tagged source on GitHub-hosted Windows runners, is in scope. We do not sign third-party software with the project's signing credentials.
- Pushing a version tag starts **Sign and publish Windows release**. A manual dispatch from main can retry an existing tag. Both require configured production credentials and the exact expected certificate subject.
- Source must be reachable from main and its project version must match the tag. The commit is fixed before building and checked again before publication. Main ancestry is a technical guard; the owner must still review the source before tagging it.
- After provider approval, the workflow checks Windows Authenticode trust, the exact publisher, a timestamp, SonarShift metadata and version, and calculates the checksum from the signed bytes. A separate publication job checks the downloaded artifact again. Only this job has repository write permission.
- Publication first stages a draft, downloads its assets to check their signatures and hashes, then makes it public. Upload or verification failures leave it unpublished. Only drafts created by this automation can be resumed; existing public releases are never replaced automatically.
- Missing configuration, rejected/expired requests, unexpected publishers and unsigned builds stop the process. No unsigned fallback is released.

See the [privacy policy](PRIVACY.md). No local settings, exported backups, diagnostics, signing keys, tokens, or personal build paths belong in source or release artifacts. Release builds omit debug symbols to avoid embedding local debug paths.

## Owner-authorized preview releases

The owner may explicitly request a manual unsigned **prerelease** for testing before signing is available. Such previews use `preview-*` tags, remain marked as prereleases, and do not replace GitHub's latest stable release. They include the Windows CI-built executable, a SHA-256 checksum, exact source/build links, and an explicit warning that the EXE is unsigned and may be blocked by Windows. They contain no local settings, backups, or debug symbols.

This preview route does not invoke, bypass, or weaken the signed stable-release workflow. `v*` stable tags still require the production signing gates above; there is no unsigned fallback from a failed signing run.

## Setup after provider approval

1. Complete the provider's eligibility review, account verification, MFA and terms acceptance.
2. Configure the project for this GitHub repository and GitHub-hosted builds. Use `Signing/SignPath.xml` as the artifact configuration; the unsigned artifact is a ZIP containing only `SonarShift.exe`.
3. Configure a production signing policy with manual approval, timestamping and the approved organization/sponsor certificate. Restrict it to this repository, this workflow and reviewed version tags. Manual dispatches report the workflow's main ref to the provider, so allow them explicitly only if the provider policy permits that route.
4. Add GitHub repository variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG` and `SIGNPATH_EXPECTED_SIGNER_SUBJECT`. The last value must be the certificate's exact full Subject, confirmed with the provider; a project nickname is insufficient. Store a submitter-only token as repository secret `SIGNPATH_API_TOKEN`; never paste it in source, logs or issues.
5. Review and merge the release source into main, including setting `SonarShift.csproj`'s version (for example `1.4.0`). Create and push the matching `v1.4.0` tag. New tags must include this workflow version. For an existing tag, dispatch **Sign and publish Windows release** from main and supply the tag.
6. Approve the signing request in SignPath within the workflow's 20-minute wait. Build, submission, verification, checksum creation and publication run automatically around that required approval. If approval times out, retry the workflow after resolving the provider request.

New version tags produce stable releases and update GitHub's latest release. Do not tag experimental builds through this workflow. Existing unsigned public releases stay unchanged; use a new version for the first signed release. Re-running a published version stops without changing its assets. If a run failed after creating a draft, retrying can finish that draft.

PowerShell 7 on Windows is required for the release scripts. The normal build workflow runs `tools/Test-ReleaseAutomation.ps1` against the built executable to exercise rejection paths and source guards. Simulated signatures in those tests do not establish signing readiness. The real provider-to-release success path can only be validated after approved credentials are available.

Until setup is complete, `.github/workflows/sign-release.yml` fails before building or publishing. This automation does not change Windows security settings or automatically unblock an EXE on other computers. Even trusted signing cannot guarantee acceptance by every reputation service, antivirus product or enterprise policy.
